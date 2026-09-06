using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 教程编辑器：左步骤列表 · 中画布（底图 + 模拟暗幕/洞口/卡片，拖框画矩形）· 右属性栏。
    ///
    /// 【为什么不用 Inspector】
    /// VNTutorialDef 的核心是「洞挖在哪」——锚点只在运行时存在（Edit Mode 注册表是空的，
    /// 只能背着名字打字），归一化矩形靠脑补，卡片长什么样要进 Play Mode 走到那个界面才知道。
    /// 这里把三件事收拢：锚点从反射目录里选（<see cref="VNTutorialAnchorCatalog"/>）、
    /// 矩形在底图上拖、Play Mode 下真机预览停在任意一步。
    ///
    /// 【两种模式，一个窗口】
    ///   - Edit Mode：画布上按底图排版，暗幕/洞/卡片是 IMGUI 模拟的（看布局够用）
    ///   - Play Mode：多出三样——Ctrl+点 Game 视图拾取目标（<see cref="VNTutorialPicker"/>）、
    ///     真机预览（<see cref="VNTutorialPlayer.EditorPreviewApply"/>，改字立刻刷新）、
    ///     抓 Game 视图当底图。预览不写「看过」记录。
    ///
    /// 【撤销】窗口内独立栈（快照 = 整份 def 的 JSON），不挂 Unity 全局 Undo，
    /// 与部位区域编辑器 / 镜头编排窗口一致。
    ///
    /// 【底图不进资产】存 <c>&lt;项目根&gt;/TutorialEditor/Backdrops/</c>，路径按资产 GUID 记在
    /// EditorPrefs——它只是排版参考，跟教程内容无关，不该进 git。
    /// </summary>
    public class VNTutorialEditorWindow : EditorWindow
    {
        const float LeftWidth = 250f;
        const float RightWidth = 370f;
        const float ToolbarH = 22f;
        const float StatusH = 20f;
        const float HandleSize = 10f;
        const float RefW = 1920f, RefH = 1080f;
        const float CardWidth = 780f;         // 与 VNTutorialPlayer.cardWidth 默认一致
        const int ImagePickerId = 0x5A7A;

        const string BackdropPrefPrefix = "VNTutorialEditor.backdrop.";
        const string LangPref = "VNTutorialEditor.lang";
        const string ShowCardPref = "VNTutorialEditor.showCard";
        const string TutorialDir = "Assets/VNEffects/Tutorials";

        static readonly string[] LangNames = { "中", "英", "日" };
        static readonly string[] TargetModes = { "整屏（不挖洞）", "锚点", "矩形" };

        [MenuItem("Tools/VN Effects/教程 Tutorials/教程编辑器 Tutorial Editor", priority = 140)]
        public static void OpenMenu() => Open(null);

        public static void Open(VNTutorialDef def)
        {
            var window = GetWindow<VNTutorialEditorWindow>("教程编辑器");
            window.minSize = new Vector2(1180f, 640f);
            if (def != null) window.SetDef(def);
            else if (window._def == null) window.PickFirstDef();
            window.Show();
        }

        // ---- 跨域重载存活的状态 ----
        [SerializeField] VNTutorialDef _def;
        [SerializeField] int _selected = -1;
        [SerializeField] int _lang;
        [SerializeField] string _backdropPath;
        [SerializeField] bool _livePreview;
        [SerializeField] bool _pickArmed;
        [SerializeField] bool _showCard = true;
        [SerializeField] bool _wholeFold;
        [SerializeField] bool _liveListFold = true;

        Texture2D _backdrop;
        string _backdropLoadedFrom;

        readonly List<string> _undo = new List<string>();
        readonly List<string> _redo = new List<string>();

        Vector2 _listScroll, _propScroll;

        // 列表拖动排序
        int _dragStep = -1;
        bool _listDragging;

        // 画布拖框
        enum DragKind { None, Move, Size, Draw }
        DragKind _drag;
        Vector2 _dragStartNorm;
        Rect _dragStartArea;

        // Play Mode
        double _lastPreviewSync;
        bool _previewDirty = true;
        int _lastPreviewIndex = -1;
        VNTutorialPicker _picker;
        string _lastPickInfo;
        string _hoverAnchor;
        VNTutorialStep _anchorModePending;   // 选了「锚点」模式但还没填 id 的那一步

        VNTutorialDef[] _allDefs = System.Array.Empty<VNTutorialDef>();
        string[] _allDefNames = System.Array.Empty<string>();

        // ==================================================================
        // 生命周期
        // ==================================================================

        void OnEnable()
        {
            _lang = EditorPrefs.GetInt(LangPref, 0);
            _showCard = EditorPrefs.GetBool(ShowCardPref, true);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            ScanDefs();
            LoadBackdrop();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopPreview();
            Disarm();
            AssetDatabase.SaveAssets();
        }

        void OnFocus() => ScanDefs();
        void OnProjectChange() => ScanDefs();
        void OnLostFocus() => AssetDatabase.SaveAssets();

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode ||
                change == PlayModeStateChange.EnteredEditMode)
            {
                // 拾取器随场景一起没了；预览层也是（播放器销毁时自己放开暂停）
                _picker = null;
                _pickArmed = false;
                _lastPreviewIndex = -1;
            }
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                VNTutorialAnchorCatalog.Invalidate();
                _previewDirty = true;
            }
            Repaint();
        }

        void Update()
        {
            if (!EditorApplication.isPlaying) return;

            if (_livePreview &&
                EditorApplication.timeSinceStartup - _lastPreviewSync > 0.1)
            {
                _lastPreviewSync = EditorApplication.timeSinceStartup;
                SyncPreview();
            }

            if (_pickArmed)
            {
                var picker = VNTutorialPicker.Ensure();
                if (picker != null && picker != _picker)
                {
                    if (_picker != null) _picker.Picked -= OnPicked;
                    _picker = picker;
                    _picker.Picked += OnPicked;
                }
                if (_picker != null) _picker.Armed = true;
            }
        }

        // ==================================================================
        // 数据：资产选择 / 保存 / 撤销
        // ==================================================================

        void ScanDefs()
        {
            var list = new List<VNTutorialDef>();
            foreach (var guid in AssetDatabase.FindAssets("t:VNTutorialDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNTutorialDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def != null) list.Add(def);
            }
            list.Sort((a, b) => string.CompareOrdinal(DisplayId(a), DisplayId(b)));
            _allDefs = list.ToArray();
            _allDefNames = new string[_allDefs.Length];
            for (int i = 0; i < _allDefs.Length; i++) _allDefNames[i] = DisplayId(_allDefs[i]);
        }

        static string DisplayId(VNTutorialDef def) =>
            def == null ? "" : string.IsNullOrEmpty(def.id) ? def.name : def.id;

        void PickFirstDef()
        {
            if (_allDefs.Length == 0) ScanDefs();
            if (_allDefs.Length > 0) SetDef(_allDefs[0]);
        }

        void SetDef(VNTutorialDef def)
        {
            if (_def == def) return;
            StopPreview();
            _def = def;
            _selected = def != null && def.steps.Count > 0 ? 0 : -1;
            _undo.Clear();
            _redo.Clear();
            _previewDirty = true;
            _backdropPath = def != null ? EditorPrefs.GetString(BackdropKey(def), "") : "";
            LoadBackdrop();
            Repaint();
        }

        static string BackdropKey(VNTutorialDef def) =>
            BackdropPrefPrefix + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(def));

        VNTutorialStep Current =>
            _def != null && _selected >= 0 && _selected < _def.steps.Count ? _def.steps[_selected] : null;

        void Save()
        {
            if (_def == null) return;
            EditorUtility.SetDirty(_def);
            _previewDirty = true;
            Repaint();
        }

        void Snapshot()
        {
            if (_def == null) return;
            _undo.Add(JsonUtility.ToJson(_def));
            if (_undo.Count > 60) _undo.RemoveAt(0);
            _redo.Clear();
        }

        void PerformUndo()
        {
            if (_undo.Count == 0 || _def == null) return;
            _redo.Add(JsonUtility.ToJson(_def));
            ApplySnapshot(_undo[_undo.Count - 1]);
            _undo.RemoveAt(_undo.Count - 1);
        }

        void PerformRedo()
        {
            if (_redo.Count == 0 || _def == null) return;
            _undo.Add(JsonUtility.ToJson(_def));
            ApplySnapshot(_redo[_redo.Count - 1]);
            _redo.RemoveAt(_redo.Count - 1);
        }

        void ApplySnapshot(string json)
        {
            JsonUtility.FromJsonOverwrite(json, _def);
            _selected = Mathf.Clamp(_selected, -1, _def.steps.Count - 1);
            Save();
        }

        void CreateNewDef()
        {
            EnsureFolder(TutorialDir);
            string baseName = "新教程";
            string name = baseName;
            int n = 1;
            while (AssetDatabase.LoadAssetAtPath<VNTutorialDef>($"{TutorialDir}/{name}.asset") != null)
                name = baseName + (++n);

            var def = ScriptableObject.CreateInstance<VNTutorialDef>();
            def.id = name;
            def.steps.Add(Template(TemplateKind.Opening));
            AssetDatabase.CreateAsset(def, $"{TutorialDir}/{name}.asset");

            // 顺手登记进教程库，否则运行时找不到、Lint 报 unknown-tutorial
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg != null && !cfg.tutorials.Contains(def))
            {
                cfg.tutorials.Add(def);
                EditorUtility.SetDirty(cfg);
                VNAssetLibraryEvents.RaiseChanged();
            }
            AssetDatabase.SaveAssets();
            ScanDefs();
            SetDef(def);
            ShowNotification(new GUIContent($"已创建 {name}（记得改 id）"));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ==================================================================
        // 步骤模板
        // ==================================================================

        enum TemplateKind { Blank, Opening, Button, Portrait, Ending }

        static VNTutorialStep Template(TemplateKind kind)
        {
            var s = new VNTutorialStep();   // new 会跑字段初始化器：padding/corner/feather 有默认值
            switch (kind)
            {
                case TemplateKind.Opening:
                    s.area = new Rect(0f, 0f, 0f, 0f);
                    s.title = "欢迎";
                    s.body = "先花十几秒认识一下界面。\n讲解期间游戏是暂停的，随时按 ESC 可以跳过。";
                    s.card = VNTutorialCardSpot.Center;
                    break;
                case TemplateKind.Button:
                    s.shape = VNTutorialHole.RoundedRect;
                    s.padding = 14f;
                    s.corner = 22f;
                    s.feather = 18f;
                    s.area = new Rect(0.4f, 0.45f, 0.2f, 0.1f);
                    s.title = "这个按钮";
                    s.body = "说明它是干什么的、什么时候会用到。";
                    s.card = VNTutorialCardSpot.Auto;
                    break;
                case TemplateKind.Portrait:
                    s.shape = VNTutorialHole.Ellipse;
                    s.padding = 26f;
                    s.corner = 22f;
                    s.feather = 22f;
                    s.area = new Rect(0.35f, 0.15f, 0.3f, 0.7f);
                    s.title = "她";
                    s.body = "介绍这个角色 / 头像区域。";
                    s.card = VNTutorialCardSpot.Auto;
                    break;
                case TemplateKind.Ending:
                    s.area = new Rect(0f, 0f, 0f, 0f);
                    s.title = "开始吧";
                    s.body = "以上就是全部操作。想再看一遍，去设置面板重置教程记录。";
                    s.card = VNTutorialCardSpot.Center;
                    break;
                default:
                    s.area = new Rect(0f, 0f, 0f, 0f);
                    break;
            }
            return s;
        }

        void InsertStep(VNTutorialStep step)
        {
            if (_def == null) return;
            Snapshot();
            int at = _selected >= 0 ? _selected + 1 : _def.steps.Count;
            _def.steps.Insert(at, step);
            _selected = at;
            Save();
        }

        void DuplicateStep()
        {
            var cur = Current;
            if (cur == null) return;
            var copy = JsonUtility.FromJson<VNTutorialStep>(JsonUtility.ToJson(cur));
            InsertStep(copy);
        }

        void DeleteStep(int index)
        {
            if (_def == null || index < 0 || index >= _def.steps.Count) return;
            Snapshot();
            _def.steps.RemoveAt(index);
            _selected = Mathf.Clamp(_selected, -1, _def.steps.Count - 1);
            Save();
        }

        void MoveStep(int from, int to)
        {
            if (_def == null || from == to || from < 0 || to < 0 ||
                from >= _def.steps.Count || to >= _def.steps.Count) return;
            var s = _def.steps[from];
            _def.steps.RemoveAt(from);
            _def.steps.Insert(to, s);
            _selected = to;
            Save();
        }

        // ==================================================================
        // OnGUI
        // ==================================================================

        void OnGUI()
        {
            HandleObjectPicker();
            DrawToolbar();

            if (_def == null)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox(
                    "先在工具栏选一篇教程，或点「新建」。\n" +
                    "（资产放在 Assets/VNEffects/Tutorials/，新建时会自动登记进 VNGameConfig 的教程库）",
                    MessageType.Info);
                return;
            }

            var body = new Rect(0f, ToolbarH, position.width, position.height - ToolbarH - StatusH);
            var left = new Rect(body.x, body.y, LeftWidth, body.height);
            var right = new Rect(body.xMax - RightWidth, body.y, RightWidth, body.height);
            var center = new Rect(left.xMax + 4f, body.y + 4f,
                right.x - left.xMax - 8f, body.height - 8f);

            DrawStepList(left);
            DrawCanvas(center);
            DrawProperties(right);
            DrawStatus(new Rect(0f, position.height - StatusH, position.width, StatusH));
            HandleShortcuts();
        }

        // ------------------------------------------------------------------
        // 工具栏
        // ------------------------------------------------------------------

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int cur = System.Array.IndexOf(_allDefs, _def);
                int pick = EditorGUILayout.Popup(cur, _allDefNames, EditorStyles.toolbarPopup,
                    GUILayout.Width(200f));
                if (pick != cur && pick >= 0 && pick < _allDefs.Length) SetDef(_allDefs[pick]);

                if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                    CreateNewDef();
                using (new EditorGUI.DisabledScope(_def == null))
                    if (GUILayout.Button("定位", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        EditorGUIUtility.PingObject(_def);

                GUILayout.Space(10f);
                GUILayout.Label("文字语言", EditorStyles.miniLabel, GUILayout.Width(50f));
                for (int i = 0; i < LangNames.Length; i++)
                {
                    bool on = GUILayout.Toggle(_lang == i, LangNames[i], EditorStyles.toolbarButton,
                        GUILayout.Width(28f));
                    if (on && _lang != i)
                    {
                        _lang = i;
                        EditorPrefs.SetInt(LangPref, i);
                    }
                }

                GUILayout.Space(10f);
                if (GUILayout.Button("底图 ▾", EditorStyles.toolbarDropDown, GUILayout.Width(60f)))
                    ShowBackdropMenu();
                bool showCard = GUILayout.Toggle(_showCard, "卡片", EditorStyles.toolbarButton,
                    GUILayout.Width(40f));
                if (showCard != _showCard)
                {
                    _showCard = showCard;
                    EditorPrefs.SetBool(ShowCardPref, showCard);
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label(EditorApplication.isPlaying ? "● Play Mode（可拾取 / 真机预览）" : "Edit Mode",
                    EditorStyles.miniLabel);
                GUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                    if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(_redo.Count == 0))
                    if (GUILayout.Button("重做", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        PerformRedo();
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                {
                    AssetDatabase.SaveAssets();
                    ShowNotification(new GUIContent("已保存"));
                }
            }
        }

        void ShowBackdropMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(EditorApplication.isPlaying
                ? "抓当前 Game 视图（含 HUD / 面板）" : "抓当前 Game 视图（Edit Mode：只有主 Canvas）"),
                false, CaptureBackdrop);
            menu.AddItem(new GUIContent("从项目里选图片…"), false, () =>
                EditorGUIUtility.ShowObjectPicker<Texture2D>(null, false, "", ImagePickerId));
            menu.AddItem(new GUIContent("从磁盘选 PNG…"), false, () =>
            {
                string path = EditorUtility.OpenFilePanel("选底图", VNTutorialBackdrop.Dir, "png,jpg");
                if (!string.IsNullOrEmpty(path)) SetBackdrop(path);
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("打开底图文件夹"), false, () =>
            {
                Directory.CreateDirectory(VNTutorialBackdrop.Dir);
                EditorUtility.RevealInFinder(VNTutorialBackdrop.Dir);
            });
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_backdropPath)))
                menu.AddItem(new GUIContent("清除底图"), false, () => SetBackdrop(""));
            menu.ShowAsContext();
        }

        void HandleObjectPicker()
        {
            var e = Event.current;
            if (e.type != EventType.ExecuteCommand) return;
            if (EditorGUIUtility.GetObjectPickerControlID() != ImagePickerId) return;
            if (e.commandName == "ObjectSelectorUpdated" || e.commandName == "ObjectSelectorClosed")
            {
                var obj = EditorGUIUtility.GetObjectPickerObject();
                if (obj != null) SetBackdrop(AssetDatabase.GetAssetPath(obj));
                e.Use();
            }
        }

        void CaptureBackdrop()
        {
            string baseName = _def != null ? DisplayId(_def) : "backdrop";
            if (EditorApplication.isPlaying)
            {
                var picker = VNTutorialPicker.Ensure();
                if (picker == null) return;
                // 预览层盖在上面会一起被抓进去，先藏一帧
                var player = VNTutorialPlayer.Instance;
                bool hid = player != null && player.IsEditorPreview;
                if (hid) player.EditorPreviewSetVisible(false);
                picker.Capture(tex =>
                {
                    if (hid && player != null) player.EditorPreviewSetVisible(true);
                    if (tex == null) { ShowNotification(new GUIContent("抓屏失败")); return; }
                    string path = VNTutorialBackdrop.SavePng(tex, baseName);
                    DestroyImmediate(tex);
                    SetBackdrop(path);
                });
            }
            else
            {
                var tex = VNTutorialBackdrop.CaptureEditMode(out string error);
                if (tex == null)
                {
                    ShowNotification(new GUIContent("抓屏失败：" + error));
                    return;
                }
                string path = VNTutorialBackdrop.SavePng(tex, baseName);
                DestroyImmediate(tex);
                SetBackdrop(path);
            }
        }

        void SetBackdrop(string path)
        {
            _backdropPath = path ?? "";
            if (_def != null) EditorPrefs.SetString(BackdropKey(_def), _backdropPath);
            LoadBackdrop();
            Repaint();
        }

        void LoadBackdrop()
        {
            if (_backdrop != null && _backdropLoadedFrom == _backdropPath) return;
            // 磁盘读进来的贴图是 DontSave 的临时对象，换图时释放；项目资产不能 Destroy
            if (_backdrop != null && !string.IsNullOrEmpty(_backdropLoadedFrom) &&
                !_backdropLoadedFrom.StartsWith("Assets/"))
                DestroyImmediate(_backdrop);
            _backdrop = VNTutorialBackdrop.Load(_backdropPath);
            _backdropLoadedFrom = _backdropPath;
        }

        // ------------------------------------------------------------------
        // 左：步骤列表
        // ------------------------------------------------------------------

        void DrawStepList(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f));
            EditorGUILayout.LabelField($"步骤（{_def.steps.Count}）", EditorStyles.boldLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
            for (int i = 0; i < _def.steps.Count; i++)
            {
                var s = _def.steps[i];
                var row = GUILayoutUtility.GetRect(10f, 44f, GUILayout.ExpandWidth(true));
                bool selected = i == _selected;
                EditorGUI.DrawRect(row, selected
                    ? new Color(0.24f, 0.42f, 0.7f, 0.55f)
                    : (i % 2 == 0 ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.05f)));
                if (_listDragging && i == _dragStep)
                    EditorGUI.DrawRect(row, new Color(1f, 0.85f, 0.3f, 0.25f));

                var line1 = new Rect(row.x + 6f, row.y + 4f, row.width - 12f, 18f);
                var line2 = new Rect(row.x + 6f, row.y + 23f, row.width - 12f, 16f);
                string summary = s == null ? "(空)" : Summary(s);
                GUI.Label(line1, $"{i + 1}. {summary}", EditorStyles.boldLabel);
                GUI.Label(line2, s == null ? "" : TargetSummary(s), EditorStyles.miniLabel);

                HandleListRow(row, i);
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 新步骤 ▾")) ShowTemplateMenu();
                using (new EditorGUI.DisabledScope(Current == null))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(44f))) DuplicateStep();
                    if (GUILayout.Button("删除", GUILayout.Width(44f))) DeleteStep(_selected);
                }
            }
            EditorGUILayout.LabelField("拖动行排序 · Ctrl+D 复制 · 右键更多", EditorStyles.centeredGreyMiniLabel);
            GUILayout.EndArea();
        }

        void HandleListRow(Rect row, int index)
        {
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when row.Contains(e.mousePosition):
                    if (e.button == 1)
                    {
                        _selected = index;
                        ShowRowContextMenu(index);
                        e.Use();
                        return;
                    }
                    if (e.button == 0)
                    {
                        _selected = index;
                        _dragStep = index;
                        _listDragging = false;
                        GUI.FocusControl(null);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseDrag when _dragStep >= 0 && row.Contains(e.mousePosition) && index != _dragStep:
                    if (!_listDragging)
                    {
                        Snapshot();
                        _listDragging = true;
                    }
                    MoveStep(_dragStep, index);
                    _dragStep = index;
                    e.Use();
                    break;

                case EventType.MouseUp when _dragStep >= 0:
                    _dragStep = -1;
                    _listDragging = false;
                    Repaint();
                    break;
            }
        }

        void ShowRowContextMenu(int index)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("复制这一步"), false, DuplicateStep);
            if (index > 0) menu.AddItem(new GUIContent("上移"), false, () => { Snapshot(); MoveStep(index, index - 1); });
            else menu.AddDisabledItem(new GUIContent("上移"));
            if (index < _def.steps.Count - 1) menu.AddItem(new GUIContent("下移"), false, () => { Snapshot(); MoveStep(index, index + 1); });
            else menu.AddDisabledItem(new GUIContent("下移"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("删除"), false, () => DeleteStep(index));
            menu.ShowAsContext();
        }

        void ShowTemplateMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("空白步骤"), false, () => InsertStep(Template(TemplateKind.Blank)));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("开场图文页（不挖洞，卡片居中）"), false, () => InsertStep(Template(TemplateKind.Opening)));
            menu.AddItem(new GUIContent("按钮 / 面板讲解（圆角矩形洞）"), false, () => InsertStep(Template(TemplateKind.Button)));
            menu.AddItem(new GUIContent("角色 / 头像（椭圆洞）"), false, () => InsertStep(Template(TemplateKind.Portrait)));
            menu.AddItem(new GUIContent("结尾页（不挖洞）"), false, () => InsertStep(Template(TemplateKind.Ending)));
            menu.ShowAsContext();
        }

        static string Summary(VNTutorialStep s)
        {
            string t = !string.IsNullOrEmpty(s.title) ? s.title : s.body;
            if (string.IsNullOrEmpty(t)) return "(未填文字)";
            t = t.Replace("\n", " ");
            return t.Length > 14 ? t.Substring(0, 14) + "…" : t;
        }

        static string TargetSummary(VNTutorialStep s)
        {
            if (!string.IsNullOrEmpty(s.anchor)) return "锚点 " + s.anchor;
            if (s.HasArea) return (s.shape == VNTutorialHole.Ellipse ? "椭圆 " : "矩形 ") +
                                  $"{s.area.x:0.00},{s.area.y:0.00} {s.area.width:0.00}×{s.area.height:0.00}";
            return "整屏压暗（不挖洞）";
        }

        // ------------------------------------------------------------------
        // 中：画布
        // ------------------------------------------------------------------

        struct HoleView
        {
            public bool has;
            public Rect norm;        // 归一化（左下原点），已含 padding
            public bool editable;    // 矩形模式才能拖
            public bool live;        // 来自 Play Mode 的锚点实际位置
            public string note;
        }

        void DrawCanvas(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.15f, 1f));
            Rect art = FitRect(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, rect.height - 16f),
                RefW / RefH);

            if (_backdrop != null) GUI.DrawTexture(art, _backdrop, ScaleMode.StretchToFill, false);
            else
            {
                EditorGUI.DrawRect(art, new Color(0.22f, 0.24f, 0.3f, 1f));
                var style = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 13 };
                GUI.Label(art, "没有底图：工具栏「底图 ▾」抓 Game 视图或选一张图\n（没有也能排版，只是看不到参照）", style);
            }

            var step = Current;
            if (step == null)
            {
                GUI.Label(new Rect(art.x, art.yMax - 20f, art.width, 18f), "左侧选一步来编辑",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            var hole = ResolveHole(step);
            float scale = art.width / RefW;

            // ---- 暗幕（四块围出洞口；没洞就整块） ----
            var dim = new Color(0f, 0f, 0.01f, Mathf.Clamp01(_def.dim));
            if (hole.has)
            {
                Rect h = NormToGui(art, hole.norm);
                h = ClampToArt(art, h);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, art.y, art.xMax, h.y), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, h.yMax, art.xMax, art.yMax), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(art.x, h.y, h.x, h.yMax), dim);
                EditorGUI.DrawRect(Rect.MinMaxRect(h.xMax, h.y, art.xMax, h.yMax), dim);

                // 描边（HDR 颜色钳到 0~1 显示）
                var edge = _def.edgeColor;
                edge = new Color(Mathf.Clamp01(edge.r), Mathf.Clamp01(edge.g), Mathf.Clamp01(edge.b), 1f);
                float width = Mathf.Max(1.5f, _def.edgeWidth * scale);
                if (step.shape == VNTutorialHole.Ellipse) DrawEllipse(h, edge, width);
                else DrawRoundedRect(h, Mathf.Min(step.corner * scale, Mathf.Min(h.width, h.height) * 0.5f), edge, width);

                if (hole.editable)
                {
                    Handles.color = Color.yellow;
                    Handles.DrawAAPolyLine(1.5f,
                        new Vector3(h.x, h.y), new Vector3(h.xMax, h.y),
                        new Vector3(h.xMax, h.yMax), new Vector3(h.x, h.yMax), new Vector3(h.x, h.y));
                    EditorGUI.DrawRect(SizeHandleRect(h), Color.yellow);
                }
                else if (hole.live)
                {
                    GUI.Label(new Rect(h.x, h.y - 18f, 300f, 16f), "● 锚点实际位置（只读）",
                        MiniLabel(new Color(0.6f, 1f, 0.6f)));
                }
            }
            else EditorGUI.DrawRect(art, dim);

            // ---- 卡片模拟 ----
            if (_showCard) DrawCardMock(art, step, hole, scale);

            if (!string.IsNullOrEmpty(hole.note))
                GUI.Label(new Rect(art.x + 6f, art.yMax - 20f, art.width - 12f, 18f), hole.note,
                    MiniLabel(new Color(1f, 0.9f, 0.6f)));

            HandleCanvasInput(art, step, hole);
        }

        HoleView ResolveHole(VNTutorialStep step)
        {
            var v = new HoleView();
            float padU = step.padding / RefW, padV = step.padding / RefH;

            if (!string.IsNullOrEmpty(step.anchor))
            {
                RectTransform target = EditorApplication.isPlaying ? VNTutorialAnchors.Get(step.anchor) : null;
                if (target != null)
                {
                    v.has = true;
                    v.live = true;
                    v.norm = Pad(VNTutorialPicker.NormalizedRect(target), padU, padV);
                }
                else if (step.HasArea)
                {
                    v.has = true;
                    v.norm = Pad(step.area, padU, padV);
                    v.note = EditorApplication.isPlaying
                        ? $"锚点「{step.anchor}」当前没登记（那个界面还没打开？），显示的是兜底矩形"
                        : $"锚点「{step.anchor}」要进 Play Mode 才看得到实际位置，现在显示兜底矩形";
                }
                else
                {
                    v.note = EditorApplication.isPlaying
                        ? $"锚点「{step.anchor}」当前没登记（先在游戏里打开那个界面）"
                        : $"锚点「{step.anchor}」：进 Play Mode 后显示实际位置";
                }
                return v;
            }

            if (step.HasArea)
            {
                v.has = true;
                v.editable = true;
                v.norm = Pad(step.area, padU, padV);
            }
            else v.note = "整屏压暗：在画布上拖一个框就能变成矩形洞";
            return v;
        }

        static Rect Pad(Rect r, float padU, float padV) =>
            Rect.MinMaxRect(r.xMin - padU, r.yMin - padV, r.xMax + padU, r.yMax + padV);

        void DrawCardMock(Rect art, VNTutorialStep step, HoleView hole, float scale)
        {
            // 落位规则照抄 VNTutorialPlayer.PlaceCard：Auto 躲开洞（洞在上半屏就放下半屏）
            var spot = step.card;
            if (spot == VNTutorialCardSpot.Auto)
                spot = hole.has
                    ? (hole.norm.center.y > 0.5f ? VNTutorialCardSpot.Bottom : VNTutorialCardSpot.Top)
                    : VNTutorialCardSpot.Center;
            float cy = art.center.y;
            if (spot == VNTutorialCardSpot.Top) cy = art.center.y - art.height * 0.28f;
            else if (spot == VNTutorialCardSpot.Bottom) cy = art.center.y + art.height * 0.28f;

            float w = CardWidth * scale;
            float padX = 38f * scale, padTop = 30f * scale, padBottom = 26f * scale, spacing = 14f * scale;
            float inner = w - padX * 2f;

            string title = Pick(step.title, step.titleEn, step.titleJa);
            string body = Pick(step.body, step.bodyEn, step.bodyJa);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            { fontSize = Mathf.Max(8, Mathf.RoundToInt(36f * scale)), wordWrap = true };
            titleStyle.normal.textColor = new Color(1f, 0.93f, 0.72f);
            var bodyStyle = new GUIStyle(EditorStyles.label)
            { fontSize = Mathf.Max(7, Mathf.RoundToInt(27f * scale)), wordWrap = true, richText = true };
            bodyStyle.normal.textColor = new Color(0.92f, 0.93f, 0.97f);
            var footStyle = new GUIStyle(EditorStyles.miniLabel)
            { fontSize = Mathf.Max(6, Mathf.RoundToInt(21f * scale)) };

            float titleH = string.IsNullOrEmpty(title) ? 0f : Mathf.Max(48f * scale, titleStyle.CalcHeight(new GUIContent(title), inner));
            float imageH = step.image != null ? Mathf.Max(40f, step.imageHeight) * scale : 0f;
            float bodyH = string.IsNullOrEmpty(body) ? 20f * scale : bodyStyle.CalcHeight(new GUIContent(body), inner);
            float footH = 34f * scale;
            float h = padTop + padBottom + footH + bodyH +
                      (titleH > 0f ? titleH + spacing : 0f) + (imageH > 0f ? imageH + spacing : 0f) + spacing;

            var card = new Rect(art.center.x - w * 0.5f, cy - h * 0.5f, w, h);
            EditorGUI.DrawRect(card, new Color(0.07f, 0.08f, 0.13f, 0.96f));

            float y = card.y + padTop;
            if (titleH > 0f)
            {
                GUI.Label(new Rect(card.x + padX, y, inner, titleH), title, titleStyle);
                y += titleH + spacing;
            }
            if (imageH > 0f)
            {
                var imgRect = new Rect(card.x + padX, y, inner, imageH);
                var tex = step.image.texture;
                if (tex != null)
                {
                    var tr = step.image.textureRect;
                    var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                    var fit = FitRect(imgRect, tr.width / Mathf.Max(1f, tr.height));
                    GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
                }
                y += imageH + spacing;
            }
            GUI.Label(new Rect(card.x + padX, y, inner, bodyH), string.IsNullOrEmpty(body) ? "（正文）" : body, bodyStyle);

            float fy = card.yMax - padBottom - footH;
            int total = Mathf.Max(1, _def.steps.Count);
            footStyle.alignment = TextAnchor.MiddleLeft;
            footStyle.normal.textColor = new Color(1f, 1f, 1f, 0.45f);
            if (_def.allowSkip) GUI.Label(new Rect(card.x + padX, fy, inner, footH), "ESC 跳过教程", footStyle);
            footStyle.alignment = TextAnchor.MiddleCenter;
            footStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            GUI.Label(new Rect(card.x + padX, fy, inner, footH), $"{_selected + 1} / {total}", footStyle);
            footStyle.alignment = TextAnchor.MiddleRight;
            footStyle.normal.textColor = new Color(1f, 0.88f, 0.55f, 0.9f);
            GUI.Label(new Rect(card.x + padX, fy, inner, footH), _selected + 1 >= total ? "✓ 完成" : "▼ 点击继续", footStyle);
        }

        string Pick(string zh, string en, string ja)
        {
            // 与运行时 VNTutorialDef.Pick 同规则：En/Ja 留空回退中文
            switch (_lang)
            {
                case 1: return string.IsNullOrEmpty(en) ? zh : en;
                case 2: return string.IsNullOrEmpty(ja) ? zh : ja;
                default: return zh;
            }
        }

        void HandleCanvasInput(Rect art, VNTutorialStep step, HoleView hole)
        {
            var e = Event.current;
            bool inside = art.Contains(e.mousePosition);
            bool anchorMode = !string.IsNullOrEmpty(step.anchor);
            if (anchorMode) return;   // 锚点位置由运行时决定，画布上不可拖

            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0 && inside:
                {
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    if (hole.editable)
                    {
                        Rect h = NormToGui(art, hole.norm);
                        if (SizeHandleRect(h).Contains(e.mousePosition)) BeginDrag(DragKind.Size, norm, step);
                        else if (h.Contains(e.mousePosition)) BeginDrag(DragKind.Move, norm, step);
                        else BeginDrag(DragKind.Draw, norm, step);
                    }
                    else BeginDrag(DragKind.Draw, norm, step);
                    GUI.FocusControl(null);
                    e.Use();
                    break;
                }

                case EventType.MouseDrag when _drag != DragKind.None:
                {
                    Vector2 norm = GuiToNorm(art, e.mousePosition);
                    norm = new Vector2(Mathf.Clamp01(norm.x), Mathf.Clamp01(norm.y));
                    Vector2 delta = norm - _dragStartNorm;
                    switch (_drag)
                    {
                        case DragKind.Draw:
                            step.area = Rect.MinMaxRect(
                                Mathf.Min(_dragStartNorm.x, norm.x), Mathf.Min(_dragStartNorm.y, norm.y),
                                Mathf.Max(_dragStartNorm.x, norm.x), Mathf.Max(_dragStartNorm.y, norm.y));
                            break;
                        case DragKind.Move:
                        {
                            var r = _dragStartArea;
                            r.x = Mathf.Clamp(_dragStartArea.x + delta.x, 0f, 1f - r.width);
                            r.y = Mathf.Clamp(_dragStartArea.y + delta.y, 0f, 1f - r.height);
                            step.area = r;
                            break;
                        }
                        case DragKind.Size:
                        {
                            // 手柄在右下角：宽随 x 增，高随 y 减（y 向上为正）
                            var r = _dragStartArea;
                            float newW = Mathf.Max(0.02f, _dragStartArea.width + delta.x);
                            float newH = Mathf.Max(0.02f, _dragStartArea.height - delta.y);
                            r.y = _dragStartArea.yMax - newH;
                            r.width = newW;
                            r.height = newH;
                            step.area = r;
                            break;
                        }
                    }
                    e.Use();
                    Repaint();
                    break;
                }

                case EventType.MouseUp when _drag != DragKind.None:
                    _drag = DragKind.None;
                    // 点一下没拖出面积 → 不算画框，把误产生的零矩形清掉
                    if (step.area.width < 0.005f || step.area.height < 0.005f) step.area = new Rect(0f, 0f, 0f, 0f);
                    Save();
                    e.Use();
                    break;
            }
        }

        void BeginDrag(DragKind kind, Vector2 norm, VNTutorialStep step)
        {
            Snapshot();
            _drag = kind;
            _dragStartNorm = norm;
            _dragStartArea = step.area;
            if (kind == DragKind.Draw) step.area = new Rect(norm.x, norm.y, 0f, 0f);
        }

        // ---- 坐标：归一化左下原点 ⇄ GUI 左上原点 ----

        static Rect NormToGui(Rect art, Rect n) =>
            new Rect(art.x + n.x * art.width,
                     art.y + (1f - n.yMax) * art.height,
                     n.width * art.width, n.height * art.height);

        static Vector2 GuiToNorm(Rect art, Vector2 g) =>
            new Vector2((g.x - art.x) / art.width, 1f - (g.y - art.y) / art.height);

        static Rect ClampToArt(Rect art, Rect r) =>
            Rect.MinMaxRect(Mathf.Clamp(r.xMin, art.xMin, art.xMax), Mathf.Clamp(r.yMin, art.yMin, art.yMax),
                            Mathf.Clamp(r.xMax, art.xMin, art.xMax), Mathf.Clamp(r.yMax, art.yMin, art.yMax));

        static Rect SizeHandleRect(Rect r) =>
            new Rect(r.xMax - HandleSize * 0.5f, r.yMax - HandleSize * 0.5f, HandleSize, HandleSize);

        static Rect FitRect(Rect area, float aspect)
        {
            float w = area.width, h = area.height;
            if (w / h > aspect) w = h * aspect; else h = w / aspect;
            return new Rect(area.center.x - w * 0.5f, area.center.y - h * 0.5f, w, h);
        }

        static void DrawEllipse(Rect r, Color color, float width)
        {
            const int Steps = 48;
            var pts = new Vector3[Steps + 1];
            for (int i = 0; i <= Steps; i++)
            {
                float a = i / (float)Steps * Mathf.PI * 2f;
                pts[i] = new Vector3(r.center.x + Mathf.Cos(a) * r.width * 0.5f,
                                     r.center.y + Mathf.Sin(a) * r.height * 0.5f);
            }
            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts);
        }

        static void DrawRoundedRect(Rect r, float radius, Color color, float width)
        {
            radius = Mathf.Max(0f, radius);
            var pts = new List<Vector3>();
            void Arc(Vector2 c, float from)
            {
                const int N = 6;
                for (int i = 0; i <= N; i++)
                {
                    float a = (from + 90f * i / N) * Mathf.Deg2Rad;
                    pts.Add(new Vector3(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius));
                }
            }
            // GUI 的 y 向下：从右下角开始顺时针
            Arc(new Vector2(r.xMax - radius, r.yMax - radius), 0f);
            Arc(new Vector2(r.x + radius, r.yMax - radius), 90f);
            Arc(new Vector2(r.x + radius, r.y + radius), 180f);
            Arc(new Vector2(r.xMax - radius, r.y + radius), 270f);
            pts.Add(pts[0]);
            Handles.color = color;
            Handles.DrawAAPolyLine(width, pts.ToArray());
        }

        static GUIStyle MiniLabel(Color color)
        {
            var s = new GUIStyle(EditorStyles.miniLabel);
            s.normal.textColor = color;
            return s;
        }

        // ------------------------------------------------------------------
        // 右：属性
        // ------------------------------------------------------------------

        void DrawProperties(Rect rect)
        {
            GUILayout.BeginArea(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, rect.height - 12f));
            _propScroll = EditorGUILayout.BeginScrollView(_propScroll);

            DrawPlayModeBlock();
            EditorGUILayout.Space(6f);

            _wholeFold = EditorGUILayout.Foldout(_wholeFold, "整篇设置", true);
            if (_wholeFold) DrawWholeSettings();
            EditorGUILayout.Space(6f);

            var step = Current;
            if (step == null)
            {
                EditorGUILayout.HelpBox("左侧选一步，或点「+ 新步骤」。", MessageType.None);
            }
            else
            {
                EditorGUILayout.LabelField($"第 {_selected + 1} 步", EditorStyles.boldLabel);
                DrawStepTarget(step);
                EditorGUILayout.Space(4f);
                DrawStepLook(step);
                EditorGUILayout.Space(4f);
                DrawStepText(step);
                EditorGUILayout.Space(4f);
                DrawStepExtras(step);
            }

            EditorGUILayout.Space(10f);
            DrawTriggerHints();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawWholeSettings()
        {
            EditorGUI.BeginChangeCheck();
            string id = EditorGUILayout.TextField("id（剧本引用名）", _def.id);
            float dim = EditorGUILayout.Slider("暗幕浓度", _def.dim, 0f, 1f);
            Color edge = EditorGUILayout.ColorField(new GUIContent("洞口描边（HDR）"), _def.edgeColor, true, true, true);
            float edgeWidth = EditorGUILayout.FloatField("描边宽度（px）", _def.edgeWidth);
            float pulse = EditorGUILayout.Slider("描边呼吸", _def.edgePulse, 0f, 1f);
            bool allowSkip = EditorGUILayout.Toggle("允许 ESC 跳过", _def.allowSkip);
            bool once = EditorGUILayout.Toggle("看过一次就不再播", _def.once);
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                _def.id = id;
                _def.dim = dim;
                _def.edgeColor = edge;
                _def.edgeWidth = edgeWidth;
                _def.edgePulse = pulse;
                _def.allowSkip = allowSkip;
                _def.once = once;
                Save();
            }
        }

        void DrawStepTarget(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("高亮目标", EditorStyles.boldLabel);
            // 模式由数据推出来（锚点非空 → 锚点；有矩形 → 矩形；否则整屏）。
            // 「锚点」但还没填 id 的过渡态用 _anchorModePending 撑着，不往资产里写占位符。
            int mode = !string.IsNullOrEmpty(step.anchor) ? 1
                : _anchorModePending == step ? 1
                : step.HasArea ? 2 : 0;
            int newMode = EditorGUILayout.Popup("方式", mode, TargetModes);
            if (newMode != mode)
            {
                Snapshot();
                _anchorModePending = null;
                switch (newMode)
                {
                    case 0:
                        step.anchor = "";
                        step.area = new Rect(0f, 0f, 0f, 0f);
                        break;
                    case 1:
                        step.anchor = "";
                        step.area = new Rect(0f, 0f, 0f, 0f);
                        _anchorModePending = step;
                        break;
                    case 2:
                        step.anchor = "";
                        if (!step.HasArea) step.area = new Rect(0.4f, 0.45f, 0.2f, 0.1f);
                        break;
                }
                Save();
            }

            if (newMode == 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string anchor = EditorGUILayout.TextField("锚点 id", step.anchor.Trim());
                    if (EditorGUI.EndChangeCheck())
                    {
                        Snapshot();
                        step.anchor = anchor;
                        Save();
                    }
                    if (GUILayout.Button("▾", GUILayout.Width(22f))) ShowAnchorMenu(step);
                }

                string trimmed = step.anchor.Trim();
                var live = VNTutorialAnchorCatalog.LiveIds();
                if (string.IsNullOrEmpty(trimmed))
                    EditorGUILayout.HelpBox("从 ▾ 目录里选一个锚点，或 Play Mode 下 Ctrl+点 Game 视图拾取。", MessageType.Info);
                else if (!VNTutorialAnchorCatalog.Contains(trimmed) && !live.Contains(trimmed))
                    EditorGUILayout.HelpBox($"目录里没有「{trimmed}」：可能拼错，或对应模块还没登记这个锚点。", MessageType.Warning);
                else if (EditorApplication.isPlaying && !live.Contains(trimmed))
                    EditorGUILayout.HelpBox("这个锚点现在没登记（那个界面还没打开），预览会只有整屏压暗。", MessageType.None);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (EditorApplication.isPlaying && live.Contains(trimmed) &&
                        GUILayout.Button("在 Game 视图里闪一下", GUILayout.Width(150f)))
                        VNTutorialPicker.Ensure()?.Flash(VNTutorialAnchors.Get(trimmed));
                }

                EditorGUI.BeginChangeCheck();
                bool useFallback = step.HasArea;
                bool nf = EditorGUILayout.ToggleLeft("锚点找不到时用兜底矩形", useFallback);
                if (EditorGUI.EndChangeCheck())
                {
                    Snapshot();
                    step.area = nf ? new Rect(0.4f, 0.45f, 0.2f, 0.1f) : new Rect(0f, 0f, 0f, 0f);
                    Save();
                }
                if (step.HasArea)
                {
                    EditorGUI.BeginChangeCheck();
                    var r = EditorGUILayout.RectField("兜底矩形", step.area);
                    if (EditorGUI.EndChangeCheck()) { Snapshot(); step.area = r; Save(); }
                }
            }
            else if (newMode == 2)
            {
                EditorGUI.BeginChangeCheck();
                var r = EditorGUILayout.RectField("矩形（归一化，左下原点）", step.area);
                if (EditorGUI.EndChangeCheck()) { Snapshot(); step.area = r; Save(); }
                EditorGUILayout.LabelField("画布上：拖框体移动、拖右下角改大小、在空处拖出新框", EditorStyles.miniLabel);
            }
        }

        void ShowAnchorMenu(VNTutorialStep step)
        {
            var menu = new GenericMenu();
            var live = VNTutorialAnchorCatalog.LiveIds();
            var listed = new HashSet<string>();
            foreach (var entry in VNTutorialAnchorCatalog.Entries)
            {
                listed.Add(entry.id);
                string mark = live.Contains(entry.id) ? "● " : "";
                string id = entry.id;
                menu.AddItem(new GUIContent($"{entry.owner}/{mark}{id}"), step.anchor.Trim() == id,
                    () => { Snapshot(); step.anchor = id; Save(); });
            }
            // 运行时登记了但目录里没有的（比如皮肤 prefab 上挂的 VNTutorialAnchor）
            bool extra = false;
            foreach (var id in live)
            {
                if (listed.Contains(id)) continue;
                if (!extra) { menu.AddSeparator(""); extra = true; }
                string captured = id;
                menu.AddItem(new GUIContent($"当前登记（未在目录）/● {id}"), step.anchor.Trim() == id,
                    () => { Snapshot(); step.anchor = captured; Save(); });
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("重扫目录"), false, VNTutorialAnchorCatalog.Invalidate);
            menu.ShowAsContext();
        }

        void DrawStepLook(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("洞口外观", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var shape = (VNTutorialHole)EditorGUILayout.EnumPopup("形状", step.shape);
            float padding = EditorGUILayout.FloatField("外扩边距（px）", step.padding);
            float corner = EditorGUILayout.FloatField("圆角半径（px）", step.corner);
            float feather = EditorGUILayout.FloatField("边缘羽化（px）", step.feather);
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                step.shape = shape;
                step.padding = padding;
                step.corner = corner;
                step.feather = feather;
                Save();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("把边距/圆角/羽化套用到全部步骤", GUILayout.Width(210f)))
                {
                    Snapshot();
                    foreach (var s in _def.steps)
                    {
                        if (s == null || s == step) continue;
                        s.padding = step.padding;
                        s.corner = step.corner;
                        s.feather = step.feather;
                    }
                    Save();
                    ShowNotification(new GUIContent("已套用"));
                }
            }
        }

        void DrawStepText(VNTutorialStep step)
        {
            EditorGUILayout.LabelField($"文字（{LangNames[_lang]}，工具栏切换语言）", EditorStyles.boldLabel);
            string title = _lang == 0 ? step.title : _lang == 1 ? step.titleEn : step.titleJa;
            string body = _lang == 0 ? step.body : _lang == 1 ? step.bodyEn : step.bodyJa;

            EditorGUI.BeginChangeCheck();
            string newTitle = EditorGUILayout.TextField("标题（可空）", title ?? "");
            EditorGUILayout.LabelField("正文");
            string newBody = EditorGUILayout.TextArea(body ?? "", GUILayout.MinHeight(70f));
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                switch (_lang)
                {
                    case 0: step.title = newTitle; step.body = newBody; break;
                    case 1: step.titleEn = newTitle; step.bodyEn = newBody; break;
                    default: step.titleJa = newTitle; step.bodyJa = newBody; break;
                }
                Save();
            }
            if (_lang != 0 && string.IsNullOrEmpty(newTitle) && string.IsNullOrEmpty(newBody))
                EditorGUILayout.LabelField("留空 = 运行时回退中文", EditorStyles.miniLabel);
        }

        void DrawStepExtras(VNTutorialStep step)
        {
            EditorGUILayout.LabelField("其它", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var image = (Sprite)EditorGUILayout.ObjectField("配图", step.image, typeof(Sprite), false);
            float imageH = step.imageHeight;
            if (image != null) imageH = EditorGUILayout.FloatField("配图高度（px）", step.imageHeight);
            var card = (VNTutorialCardSpot)EditorGUILayout.EnumPopup("卡片位置", step.card);
            string se = EditorGUILayout.TextField("音效 id（SE 库）", step.se ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                Snapshot();
                step.image = image;
                step.imageHeight = imageH;
                step.card = card;
                step.se = se;
                Save();
            }
        }

        void DrawTriggerHints()
        {
            EditorGUILayout.LabelField("怎么触发", EditorStyles.boldLabel);
            string id = DisplayId(_def);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel($"tutorial {id}", EditorStyles.textField, GUILayout.Height(18f));
                if (GUILayout.Button("复制", GUILayout.Width(44f)))
                {
                    EditorGUIUtility.systemCopyBuffer = $"tutorial {id}";
                    ShowNotification(new GUIContent("已复制"));
                }
            }
            EditorGUILayout.LabelField($"强制重看：tutorial {id} force:on", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"模块首次自动播：event <模块> … tutorial:{id}", EditorStyles.miniLabel);
            var cfg = VNGameConfig.Active;
            if (cfg != null && (cfg.tutorials == null || !cfg.tutorials.Contains(_def)))
            {
                EditorGUILayout.HelpBox("这篇还没登记进 VNGameConfig 的教程库，运行时找不到。", MessageType.Warning);
                if (GUILayout.Button("登记进教程库"))
                {
                    cfg.tutorials.Add(_def);
                    EditorUtility.SetDirty(cfg);
                    VNAssetLibraryEvents.RaiseChanged();
                }
            }
        }

        // ------------------------------------------------------------------
        // Play Mode 区块：拾取 / 真机预览 / 实时锚点清单
        // ------------------------------------------------------------------

        void DrawPlayModeBlock()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "进 Play Mode 后这里会多出：Ctrl+点 Game 视图拾取目标、真机预览停在某一步、" +
                    "实时锚点清单（悬停在 Game 视图里闪一下）。",
                    MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Play Mode", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool live = GUILayout.Toggle(_livePreview, _livePreview ? "■ 停止真机预览" : "▶ 真机预览（停在当前步）",
                    "Button", GUILayout.Height(24f));
                if (live != _livePreview)
                {
                    _livePreview = live;
                    if (!live) StopPreview();
                    else _previewDirty = true;
                }
                using (new EditorGUI.DisabledScope(_def.steps.Count == 0))
                {
                    if (GUILayout.Button("◀", GUILayout.Width(28f), GUILayout.Height(24f)))
                        _selected = Mathf.Max(0, _selected - 1);
                    if (GUILayout.Button("▶", GUILayout.Width(28f), GUILayout.Height(24f)))
                        _selected = Mathf.Min(_def.steps.Count - 1, _selected + 1);
                }
            }
            if (GUILayout.Button("从头完整播一遍（force，按真实流程）"))
            {
                var player = VNTutorialPlayer.Instance;
                if (player != null)
                {
                    _livePreview = false;
                    StopPreview();
                    player.StartCoroutine(player.PlayCo(_def, true));
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool armed = GUILayout.Toggle(_pickArmed, _pickArmed ? "◎ 拾取中：Ctrl+左键点 Game 视图" : "◎ 拾取目标（Ctrl+左键）",
                    "Button", GUILayout.Height(24f));
                if (armed != _pickArmed)
                {
                    _pickArmed = armed;
                    if (!armed) Disarm();
                }
                using (new EditorGUI.DisabledScope(_picker == null || _picker.LastRect == null || Current == null))
                    if (GUILayout.Button("↑ 父级", GUILayout.Width(56f), GUILayout.Height(24f)))
                        _picker.PickParent();
            }
            if (!string.IsNullOrEmpty(_lastPickInfo))
                EditorGUILayout.LabelField(_lastPickInfo, EditorStyles.wordWrappedMiniLabel);
            if (_pickArmed && !_livePreview)
                EditorGUILayout.LabelField("提示：先开真机预览游戏才会冻住，否则 Ctrl+点击也会被游戏当普通点击", EditorStyles.wordWrappedMiniLabel);

            // ---- 实时锚点清单 ----
            var live2 = new List<string>(VNTutorialAnchors.Ids);
            live2.Sort(string.CompareOrdinal);
            _liveListFold = EditorGUILayout.Foldout(_liveListFold, $"当前已登记的锚点（{live2.Count}）", true);
            if (_liveListFold)
            {
                if (live2.Count == 0)
                    EditorGUILayout.LabelField("（还没有：先在游戏里打开要讲的界面 / 进那个小游戏）", EditorStyles.miniLabel);
                bool hoveringAny = false;
                foreach (var id in live2)
                {
                    var row = GUILayoutUtility.GetRect(10f, 18f, GUILayout.ExpandWidth(true));
                    bool hover = row.Contains(Event.current.mousePosition);
                    if (hover)
                    {
                        hoveringAny = true;
                        EditorGUI.DrawRect(row, new Color(1f, 0.9f, 0.3f, 0.15f));
                        if (_hoverAnchor != id)
                        {
                            _hoverAnchor = id;
                            VNTutorialPicker.Ensure()?.Flash(VNTutorialAnchors.Get(id));
                        }
                    }
                    GUI.Label(row, "● " + id, EditorStyles.miniLabel);
                    if (Event.current.type == EventType.MouseDown && hover && Current != null)
                    {
                        Snapshot();
                        Current.anchor = id;
                        Save();
                        Event.current.Use();
                    }
                }
                if (!hoveringAny) _hoverAnchor = null;
                if (Event.current.type == EventType.MouseMove) Repaint();
            }
        }

        void SyncPreview()
        {
            var player = VNTutorialPlayer.Instance;
            if (player == null) return;
            if (_def == null || _selected < 0 || _selected >= _def.steps.Count)
            {
                if (player.IsEditorPreview) player.EditorPreviewEnd();
                return;
            }
            if (!player.IsEditorPreview || _previewDirty || _lastPreviewIndex != _selected)
            {
                player.EditorPreviewApply(_def, _selected);
                _previewDirty = false;
                _lastPreviewIndex = _selected;
            }
        }

        void StopPreview()
        {
            if (!EditorApplication.isPlaying) return;
            var player = VNTutorialPlayer.Instance;
            if (player != null && player.IsEditorPreview) player.EditorPreviewEnd();
            _lastPreviewIndex = -1;
        }

        void Disarm()
        {
            if (_picker != null)
            {
                _picker.Armed = false;
                _picker.Picked -= OnPicked;
            }
            _picker = null;
        }

        void OnPicked(VNTutorialPicker.PickResult result)
        {
            var step = Current;
            if (step == null) return;
            if (result.rect == null)
            {
                _lastPickInfo = "点的地方没有 UI 控件";
                Repaint();
                return;
            }
            Snapshot();
            if (!string.IsNullOrEmpty(result.anchor))
            {
                step.anchor = result.anchor;
                _lastPickInfo = $"拾取：锚点 {result.anchor}\n{result.path}";
            }
            else
            {
                step.anchor = "";
                step.area = result.area;
                _lastPickInfo = $"拾取：没登记锚点，已写成矩形 {result.area.x:0.00},{result.area.y:0.00} " +
                                $"{result.area.width:0.00}×{result.area.height:0.00}\n{result.path}";
            }
            Save();
            ShowNotification(new GUIContent("已拾取"));
        }

        // ------------------------------------------------------------------

        void DrawStatus(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.15f));
            string backdrop = string.IsNullOrEmpty(_backdropPath) ? "无底图" : "底图：" + Path.GetFileName(_backdropPath);
            string text = $"{AssetDatabase.GetAssetPath(_def)}   ·   {backdrop}   ·   Ctrl+Z/Y 撤销重做（本窗口）  Ctrl+S 保存  Ctrl+D 复制步骤";
            GUI.Label(new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height), text, EditorStyles.miniLabel);
        }

        void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;
            if (ctrl && e.keyCode == KeyCode.Z) { PerformUndo(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.Y) { PerformRedo(); e.Use(); }
            else if (ctrl && e.keyCode == KeyCode.S)
            {
                AssetDatabase.SaveAssets();
                ShowNotification(new GUIContent("已保存"));
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.D && !EditorGUIUtility.editingTextField)
            {
                DuplicateStep();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete && !EditorGUIUtility.editingTextField && Current != null)
            {
                DeleteStep(_selected);
                e.Use();
            }
        }
    }
}
