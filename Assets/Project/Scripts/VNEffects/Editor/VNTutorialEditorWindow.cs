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
    public partial class VNTutorialEditorWindow : EditorWindow
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
        enum DragKind { None, Move, Size, Draw, Card, CardSize }
        DragKind _drag;
        Vector2 _dragStartNorm;
        Rect _dragStartArea;
        Vector2 _dragStartCard;      // Card 拖动起点的 cardPos
        float _dragStartCardW;       // CardSize 拖动起点：宽度（已解析，px）
        float _dragStartCardS;       // CardSize 拖动起点：缩放（已解析）
        Vector2 _dragStartMouse;     // CardSize 拖动起点鼠标（GUI px）
        Rect _lastArt;               // 本帧画布矩形（Inspector 切 Custom 时把当前卡片位置换算成 cardPos）
        Rect _cardMock;              // 本帧卡片模拟的 GUI 矩形（HandleCanvasInput 拿它做命中）
        Vector2 _cardHalfNorm;       // 卡片半宽半高（归一化），拖动时钳制用

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

    }
}
