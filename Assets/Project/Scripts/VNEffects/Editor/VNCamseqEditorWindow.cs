using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 镜头演出可视化编辑器（camseq）：
    ///   - 迷你画布：背景缩略图 + 各路径点的取景框 + 路径线，点击/拖动直接设点
    ///   - 路径点列表：拖拽排序、zoom/时长/缓动编辑
    ///   - 预览：进度条拖动或 ▶ 播放，取景框按真实缓动公式沿路径移动
    ///   - 文本双向：一键生成 camseq 文本到剪贴板；粘贴已有文本反向载入继续调
    /// 菜单：Tools → VN Effects → Camera Sequence Editor
    /// </summary>
    public class VNCamseqEditorWindow : EditorWindow
    {
        enum PointType { Anchor, Character, Coords, Stay }

        [System.Serializable]
        class Waypoint
        {
            public PointType type = PointType.Coords;
            public int anchorIndex = 4;   // middle
            public string charId = "";
            public int partIndex = 0;
            public int slotIndex = 1;     // 编辑态假定站位（center）
            public Vector2 coords;
            public float zoom = 1.4f;
            public float duration = 0.8f;
            public int easeIndex = 0;     // 0 = (默认)
            public float fade;            // >0 = 交叉叠化到本点（xfade:秒），代替平移/瞬切
            public float hold;            // >0 = 到达本点后停留的秒数（hold:秒）
            public string shake = "";     // 到达本点时震一下（shake:等级 或 shake:强度,秒数）
        }

        /// <summary>camseq 开场衔接方式（对应 start: 选项）</summary>
        enum StartMode { None, Cut, Fade }

        static readonly string[] StartModeNames =
        {
            "无", "cut（接 bg 转场盖屏瞬切）", "fade（当前画面叠化到首镜头）",
        };

        // ---- 与运行时一致的常量/词汇 ----
        static readonly string[] AnchorTokens =
            { "topleft", "top", "topright", "left", "middle", "right", "bottomleft", "bottom", "bottomright" };
        static readonly Vector2[] AnchorPositions =
        {
            new Vector2(-620, 340), new Vector2(0, 340), new Vector2(620, 340),
            new Vector2(-620, 0), new Vector2(0, 0), new Vector2(620, 0),
            new Vector2(-620, -340), new Vector2(0, -340), new Vector2(620, -340),
        };
        static readonly string[] PartTokens = { "(中心)", "head", "chest", "waist", "feet", "up", "mid", "down" };
        static readonly float[] PartFracs = { 0f, 0.36f, 0.15f, -0.08f, -0.42f, 0.3f, 0f, -0.3f };
        static readonly string[] SlotNames = { "left", "center", "right" };
        static readonly float[] SlotX = { -380f, 0f, 380f };
        static readonly string[] EaseNames =
        {
            "(默认)", "Linear", "InSine", "OutSine", "InOutSine", "InQuad", "OutQuad",
            "InOutQuad", "InCubic", "OutCubic", "InOutCubic", "OutBack", "InOutBack", "OutExpo",
        };
        static readonly Vector2 CanvasHalf = new Vector2(960f, 540f);
        static readonly Vector2 Overscan = new Vector2(60f, 60f);

        // ↓ 域重载存活组：进出 Play Mode / 重编译都会重建窗口，普通字段一律清空。
        //   正在调的这段运镜和它跟剧本的绑定关系绝不能丢，所以全部 [SerializeField]。
        [SerializeField] List<Waypoint> _points = new List<Waypoint>();
        [SerializeField] StartMode _startMode;
        [SerializeField] float _startFade = 0.6f;
        [SerializeField] bool _endFade;
        [SerializeField] float _endFadeDur = 0.6f;
        [SerializeField] float _scrub;          // 0~总时长 的预览时间

        ReorderableList _list;
        bool _playing;
        double _lastUpdateTime;
        string _pasteText = "";
        string _generatedText = "";
        Vector2 _scroll;

        // ---- 与剧本编辑器的双向绑定 ----
        // EditorWindow 是 ScriptableObject，引用能跨域重载存活；行号只是个 int。
        [SerializeField] VNScenarioEditorWindow _linkedEditor;
        [SerializeField] int _linkedRow = -1;
        [SerializeField] bool _linkLocked;      // 锁头：不跟随剧本的选中行
        [SerializeField] bool _liveApply = true; // 实时回写（关掉则手动点「应用回剧本」）
        [SerializeField] string _lastAppliedText = "";  // 最后一次与剧本一致的文本

        // ---- 画布底图 / 立绘 ----
        [SerializeField] string _bgOverrideId = "";   // 空 = 跟随绑定行推算出的背景
        [SerializeField] bool _showPortraits = true;  // 关掉退回三个灰色站位矩形

        // 绑定行的舞台快照缓存（行号或文档版本变了才重查）
        VNRowStageInfo _rowStage;
        int _rowStageRow = -1;
        int _rowStageVersion = -1;

        // ---- 第二批：场景预览 / 画布拖拽 / 预设库 ----
        enum DragMode { None, Center, Corner }
        DragMode _dragMode;

        // 场景预览的还原信息全部 [SerializeField]：不然脚本一重编译（域重载）
        // 就丢了原始位置/原背景，ZoomRoot 与背景会永久停在预览态还原不回去。
        [SerializeField] bool _scenePreviewing;
        [SerializeField] RectTransform _zoomRoot;
        [SerializeField] Vector2 _origPos;
        [SerializeField] Vector3 _origScale;

        // 舞台预览（把绑定行的背景/立绘摆进场景，让 Game 视图也对）
        [SerializeField] Sprite _origBgSprite;
        [SerializeField] bool _bgTouched;
        [SerializeField] int _stagedRow = -1;      // 已经摆过舞台的那一行
        // 临时立绘：HideFlags.DontSave，绝不写进场景文件，域重载时自动销毁
        readonly List<GameObject> _previewChars = new List<GameObject>();

        [SerializeField] bool _cameraView;         // 画布：整图 / 镜头视角

        // ---- 构图辅助线 ----
        [System.Flags]
        enum Guides
        {
            None = 0,
            Thirds = 1,        // 三分线
            Center = 2,        // 中心十字
            SafeArea = 4,      // 安全区（90%）
            DialogueBox = 8,   // 对话框遮挡区
            All = Thirds | Center | SafeArea | DialogueBox,
        }

        const string GuidesPrefKey = "VNCamseq.Guides";
        const Guides DefaultGuides = Guides.Thirds | Guides.DialogueBox;
        // 纯外观偏好 → EditorPrefs，不序列化进窗口（OnEnable 里读回来）
        Guides _guides = DefaultGuides;

        // ---- 撤销 / 重做（窗口内独立栈，**不进 Unity 全局 Undo**）----
        // 快照 = GenerateText()：路径点 + 开场/收尾叠化全在这一串文本里，
        // 恢复 = 反过来解析。全部 [SerializeField]，重编译后还能继续撤销。
        // 走全局 Undo 的话，在 Scene 视图按 Ctrl+Z 会莫名其妙撤到这里的改动。
        [SerializeField] List<string> _undoStack = new List<string>();
        [SerializeField] List<string> _redoStack = new List<string>();
        [SerializeField] string _undoBaseline = "";
        string _undoPending;          // 上一拍看到的文本（判断"还在连续改"）
        double _undoPendingTime;
        const int UndoDepth = 64;
        const double UndoIdleSeconds = 0.35;   // 静默这么久才切一步 → 拖滑条合并成一次

        VNCamseqPresetLibrary _library;
        string _presetName = "";
        int _presetIndex;

        public const string LibraryPath = "Assets/VNEffects/CamseqPresets.asset";

        [MenuItem("Tools/VN Effects/Camera Sequence Editor")]
        static void Open()
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");
            win.minSize = new Vector2(560f, 720f);
        }

        // ==================================================================
        // 与剧本编辑器的双向绑定
        // ==================================================================

        // 剧本编辑器每帧都要问「这行在编排吗」，逐行 FindObjectsOfTypeAll 太费；
        // 绑定关系本来就唯一，缓存成静态的。域重载后清空，下一次 SyncLink 立刻补回来。
        static VNScenarioEditorWindow s_linkEditor;
        static int s_linkRow = -1;

        /// <summary>剧本编辑器某行的「编排」按钮：打开窗口并绑定到那一行</summary>
        public static void OpenLinked(VNScenarioEditorWindow editor, int rowIndex)
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");
            win.minSize = new Vector2(560f, 720f);

            // 手动回写模式下手上还有没应用的稿，切走就没了——先问一句
            if (win.HasLink && win._linkedRow != rowIndex && !win._liveApply && win.LinkDirty)
            {
                if (EditorUtility.DisplayDialog("还有未应用的改动",
                        $"第 {win._linkedRow + 1} 行的编排还没写回剧本。要先应用再切过去吗？",
                        "应用后切换", "丢弃改动"))
                    win.ApplyToLink();
            }

            win.BindTo(editor, rowIndex, forceLoad: true);
            win.Focus();
        }

        /// <summary>剧本编辑器画「编排」按钮时问：这一行是不是正被编排？</summary>
        public static bool IsLinkedTo(VNScenarioEditorWindow editor, int rowIndex) =>
            s_linkEditor == editor && s_linkRow == rowIndex && rowIndex >= 0;

        void BindTo(VNScenarioEditorWindow editor, int rowIndex, bool forceLoad)
        {
            bool changed = _linkedEditor != editor || _linkedRow != rowIndex;
            _linkedEditor = editor;
            _linkedRow = rowIndex;
            s_linkEditor = editor;
            s_linkRow = rowIndex;
            if (changed || forceLoad) LoadFromLink();
        }

        bool HasLink => _linkedEditor != null && _linkedRow >= 0 &&
                        _linkedEditor.IsCamseqRow(_linkedRow);

        /// <summary>当前编排内容与剧本那一行不一致（手动回写模式下的脏标记）</summary>
        bool LinkDirty => HasLink && GenerateText() != _lastAppliedText;

        /// <summary>
        /// 每帧对齐绑定关系：没绑过就找一个打开着的剧本编辑器；未锁定时跟随它的选中行。
        /// 手动回写模式下一旦有未应用的改动就自动上锁——宁可停在原地，也不能悄悄切走丢稿。
        /// </summary>
        void SyncLink()
        {
            if (_linkedEditor == null)
            {
                var windows = Resources.FindObjectsOfTypeAll<VNScenarioEditorWindow>();
                if (windows.Length == 0) return;
                _linkedEditor = windows[0];
                _linkedRow = -1;
                // 从菜单打开、手上已经摆了点的：自动上锁，绝不让自动跟随覆盖掉现成的稿。
                // 用户点剧本行的「编排」或这里的「从剧本重载」才会真正接管。
                if (_points.Count > 0) _linkLocked = true;
            }

            if (!_liveApply && LinkDirty) _linkLocked = true;
            if (_linkLocked)
            {
                // 锁着也要让剧本编辑器的按钮高亮保持正确
                s_linkEditor = _linkedEditor;
                s_linkRow = _linkedRow;
                return;
            }

            int selected = _linkedEditor.SelectedCamseqRow();
            if (selected >= 0 && selected != _linkedRow) BindTo(_linkedEditor, selected, false);
            s_linkEditor = _linkedEditor;
            s_linkRow = _linkedRow;
        }

        /// <summary>从绑定的剧本行读回内容（覆盖当前编排）</summary>
        void LoadFromLink()
        {
            if (!HasLink) return;
            if (!_linkedEditor.TryGetCamseqText(_linkedRow, out string text)) return;
            _pasteText = text;
            ParseText(silent: true);
            _lastAppliedText = GenerateText();
            _scrub = 0f;
            ResetUndo();   // 换了一行 = 换了一段历史
            Repaint();
        }

        // ==================================================================
        // 撤销 / 重做
        // ==================================================================

        bool CanUndo => _undoStack.Count > 0 || GenerateText() != _undoBaseline;
        bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// 每拍看一眼内容变没变。变了要等「鼠标松开 + 静默一小会儿」才落一步，
        /// 拖滑条那 200 帧、连着敲的几个字符就自然合并成一次撤销。
        /// </summary>
        void TrackUndo()
        {
            string current = GenerateText();
            if (current == _undoBaseline) { _undoPending = null; return; }

            // 还在改（值一直在变）/ 鼠标还按着（拖点、拖滑条）→ 不切分
            if (_undoPending != current || GUIUtility.hotControl != 0 ||
                _dragMode != DragMode.None)
            {
                _undoPending = current;
                _undoPendingTime = EditorApplication.timeSinceStartup;
                return;
            }
            if (EditorApplication.timeSinceStartup - _undoPendingTime < UndoIdleSeconds) return;
            CommitUndo();
        }

        /// <summary>把还没落栈的改动立刻切成一步（清空/套预设这种大动作前先调，免得跟上一次微调粘一起）</summary>
        void CommitUndo()
        {
            string current = GenerateText();
            if (current == _undoBaseline) return;
            _undoStack.Add(_undoBaseline);
            if (_undoStack.Count > UndoDepth) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _undoBaseline = current;
            _undoPending = null;
        }

        /// <summary>换绑定行 / 从剧本重载后重开一段历史：绝不让 Ctrl+Z 撤回上一行的内容</summary>
        void ResetUndo()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _undoBaseline = GenerateText();
            _undoPending = null;
        }

        void PerformUndo()
        {
            CommitUndo();                       // 手上没落栈的改动也算一步
            if (_undoStack.Count == 0) return;
            int last = _undoStack.Count - 1;
            string snapshot = _undoStack[last];
            _undoStack.RemoveAt(last);
            _redoStack.Add(GenerateText());
            RestoreSnapshot(snapshot);
        }

        void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            int last = _redoStack.Count - 1;
            string snapshot = _redoStack[last];
            _redoStack.RemoveAt(last);
            _undoStack.Add(GenerateText());
            RestoreSnapshot(snapshot);
        }

        void RestoreSnapshot(string text)
        {
            ParseTextFrom(text, silent: true);
            _undoBaseline = GenerateText();
            _undoPending = null;
            // 焦点还留在数字框里的话，框里显示的仍是撤销前那串字符 → 强制丢焦点重画
            GUIUtility.keyboardControl = 0;
            if (_list != null) _list.index = Mathf.Min(_list.index, _points.Count - 1);
            Repaint();
        }

        // 快捷键走 ShortcutManager（窗口作用域）：本窗口有焦点时优先于全局 Undo，
        // 键位可在 Edit → Shortcuts 里改。撤销范围只有这个窗口，与场景编辑互不干扰。
        [Shortcut("VN/Camseq Editor/Undo", typeof(VNCamseqEditorWindow),
            KeyCode.Z, ShortcutModifiers.Action)]
        static void ShortcutUndo(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformUndo();
        }

        [Shortcut("VN/Camseq Editor/Redo", typeof(VNCamseqEditorWindow),
            KeyCode.Y, ShortcutModifiers.Action)]
        static void ShortcutRedo(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformRedo();
        }

        [Shortcut("VN/Camseq Editor/Redo (Alt)", typeof(VNCamseqEditorWindow),
            KeyCode.Z, ShortcutModifiers.Action | ShortcutModifiers.Shift)]
        static void ShortcutRedoAlt(ShortcutArguments args)
        {
            if (args.context is VNCamseqEditorWindow w) w.PerformRedo();
        }

        /// <summary>把当前编排写回剧本行</summary>
        void ApplyToLink()
        {
            if (!HasLink) return;
            string text = GenerateText();
            if (text == _lastAppliedText) return;
            if (_linkedEditor.ApplyCamseqText(_linkedRow, text))
            {
                _lastAppliedText = text;
                _linkLocked = false;   // 应用完就重新跟随
            }
        }

        void DrawLinkBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (!HasLink)
                {
                    GUILayout.Label(_linkedEditor == null
                        ? "未连接剧本编辑器（打开 Scenario Editor 后点 camseq 行的「编排」）"
                        : "剧本里选中的不是 camseq 行", EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();
                    return;
                }

                bool dirty = LinkDirty;
                if (GUILayout.Button(
                        new GUIContent($"◆ {_linkedEditor.ScenarioDisplayName} 第 {_linkedRow + 1} 行"
                                       + (dirty ? "（未应用）" : ""),
                            "点一下把剧本编辑器里的这一行滚到眼前"),
                        dirty ? EditorStyles.boldLabel : EditorStyles.label,
                        GUILayout.Width(220f)))
                    _linkedEditor.FocusRow(_linkedRow);

                _linkLocked = GUILayout.Toggle(_linkLocked,
                    new GUIContent(_linkLocked ? "已锁定" : "跟随选中",
                        "锁定后不再跟着剧本里的选中行切换；手动回写模式下有未应用改动会自动上锁"),
                    EditorStyles.toolbarButton, GUILayout.Width(64f));

                bool live = GUILayout.Toggle(_liveApply,
                    new GUIContent("实时回写", "改一下就立刻写回剧本；关掉则要手动点「应用回剧本」"),
                    EditorStyles.toolbarButton, GUILayout.Width(64f));
                if (live != _liveApply)
                {
                    _liveApply = live;
                    if (live) ApplyToLink();
                }

                using (new EditorGUI.DisabledScope(!dirty))
                {
                    var prev = GUI.backgroundColor;
                    if (dirty) GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
                    if (GUILayout.Button("应用回剧本", EditorStyles.toolbarButton,
                            GUILayout.Width(80f)))
                        ApplyToLink();
                    GUI.backgroundColor = prev;
                }
                if (GUILayout.Button(new GUIContent("从剧本重载", "丢弃这里的改动，按剧本行重新载入"),
                        EditorStyles.toolbarButton, GUILayout.Width(80f)))
                {
                    LoadFromLink();
                    _linkLocked = false;
                }
                GUILayout.FlexibleSpace();
                DrawCanvasSourceControls();
            }
        }

        /// <summary>底图来源下拉 + 立绘开关（没绑定剧本时也要能用，所以画在共用段）</summary>
        void DrawCanvasSourceControls()
        {
            var ids = SceneBackgroundIds();
            var display = new string[ids.Length + 1];
            display[0] = _rowStage != null && _rowStage.backdrop != null
                ? $"底图: 跟随剧本（{_rowStage.cgId ?? _rowStage.bgId}）" : "底图: 跟随剧本";
            for (int i = 0; i < ids.Length; i++) display[i + 1] = "底图: " + ids[i];

            int index = string.IsNullOrEmpty(_bgOverrideId)
                ? 0 : System.Array.IndexOf(ids, _bgOverrideId) + 1;
            if (index < 0) index = 0;
            int picked = EditorGUILayout.Popup(index, display,
                EditorStyles.toolbarPopup, GUILayout.Width(190f));
            if (picked != index)
                _bgOverrideId = picked <= 0 ? "" : ids[picked - 1];

            _showPortraits = GUILayout.Toggle(_showPortraits,
                new GUIContent("立绘", "按剧本推算出的站位画真实立绘；关掉退回灰色站位矩形"),
                EditorStyles.toolbarButton, GUILayout.Width(40f));
        }

        /// <summary>绑定行的舞台快照（背景 / CG / 在场角色），没绑定返回 null</summary>
        VNRowStageInfo RowStage
        {
            get
            {
                if (!HasLink)
                {
                    _rowStage = null;
                    _rowStageRow = -1;
                    return null;
                }
                int version = _linkedEditor.DocVersion;
                if (_rowStage == null || _rowStageRow != _linkedRow ||
                    _rowStageVersion != version)
                {
                    _linkedEditor.TryGetRowStage(_linkedRow, out _rowStage);
                    _rowStageRow = _linkedRow;
                    _rowStageVersion = version;
                }
                return _rowStage;
            }
        }

        void OnEnable()
        {
            _list = new ReorderableList(_points, typeof(Waypoint), true, true, true, true)
            {
                drawHeaderCallback = r => GUI.Label(r,
                    "路径点（拖手柄排序 | 时长 0 = 瞬切 | xfade>0 = 叠化到该点 | " +
                    "hold = 到点后停留 | 震 = 到点震屏）"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight * 2f + 10f,
                drawElementCallback = DrawElement,
                onAddCallback = l => _points.Add(new Waypoint()),
            };
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);

            // 纯外观偏好 → EditorPrefs（关窗重开、换项目窗口都还在）
            _guides = (Guides)EditorPrefs.GetInt(GuidesPrefKey, (int)DefaultGuides);
            // 域重载后 _undoBaseline 是序列化带回来的，别覆盖掉；只有全新窗口才初始化
            if (string.IsNullOrEmpty(_undoBaseline)) _undoBaseline = GenerateText();

            // 域重载会把 DontSave 的临时立绘销毁，但列表本身没序列化——
            // 重编译时场景预览还开着的话，这里按记录的状态把舞台重新摆一遍。
            if (_scenePreviewing)
            {
                _stagedRow = -1;
                ApplyStageToScene();
            }
        }

        void OnDisable()
        {
            StopScenePreview();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // 进出 Play 前后都还原场景，避免把预览状态序列化进场景/运行副本
            StopScenePreview();
        }

        void Update()
        {
            TrackUndo();
            if (_playing)
            {
                double now = EditorApplication.timeSinceStartup;
                _scrub += (float)(now - _lastUpdateTime);
                _lastUpdateTime = now;
                if (_scrub >= TotalDuration())
                {
                    _scrub = TotalDuration();
                    _playing = false;
                }
                Repaint();
            }
            if (_scenePreviewing)
            {
                // 绑定跟到别的行了 → 舞台要跟着换
                if (HasLink && _stagedRow != _linkedRow) ApplyStageToScene();
                ApplySceneState();
            }
        }

        // ==================================================================
        // GUI
        // ==================================================================

        void OnGUI()
        {
            SyncLink();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawToolbar();
            DrawLinkBar();
            GUILayout.Space(4f);

            // 迷你画布（16:9）
            var canvasRect = GUILayoutUtility.GetAspectRect(16f / 9f);
            DrawCanvas(canvasRect);
            GUILayout.Space(4f);

            // 预览进度条
            float total = TotalDuration();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"预览 {_scrub:0.00}s / {total:0.00}s", GUILayout.Width(140f));
                float newScrub = GUILayout.HorizontalSlider(_scrub, 0f, Mathf.Max(0.01f, total));
                if (!Mathf.Approximately(newScrub, _scrub))
                {
                    _scrub = newScrub;
                    _playing = false;
                }
            }
            GUILayout.Space(4f);

            // 开场 / 收尾叠化选项（对应 camseq 的 start: / end: 参数）
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("开场", GUILayout.Width(30f));
                _startMode = (StartMode)EditorGUILayout.Popup(
                    (int)_startMode, StartModeNames, GUILayout.Width(190f));
                if (_startMode == StartMode.Fade)
                {
                    GUILayout.Label("秒", GUILayout.Width(16f));
                    _startFade = Mathf.Max(0.05f,
                        EditorGUILayout.FloatField(_startFade, GUILayout.Width(40f)));
                }
                GUILayout.Space(12f);
                _endFade = GUILayout.Toggle(_endFade, "收尾叠化回全图", GUILayout.Width(108f));
                if (_endFade)
                {
                    GUILayout.Label("秒", GUILayout.Width(16f));
                    _endFadeDur = Mathf.Max(0.05f,
                        EditorGUILayout.FloatField(_endFadeDur, GUILayout.Width(40f)));
                }
                GUILayout.FlexibleSpace();
            }
            if (_startMode == StartMode.Cut && _points.Count > 0 && _points[0].duration > 0.001f)
                EditorGUILayout.HelpBox(
                    "start:cut 要求首个路径点时长为 0（瞬切），否则运行时按普通 camseq 执行",
                    MessageType.Warning);

            _list.DoLayoutList();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 瞬切起手（时长0）"))
                    _points.Insert(0, new Waypoint { type = PointType.Anchor, anchorIndex = 2, zoom = 1.8f, duration = 0f });
                if (GUILayout.Button("+ 回原点收尾"))
                    _points.Add(new Waypoint { type = PointType.Anchor, anchorIndex = 4, zoom = 1f, duration = 1f });
                var templateRect = GUILayoutUtility.GetRect(
                    new GUIContent("内置模板 ▾"), GUI.skin.button);
                if (GUI.Button(templateRect, "内置模板 ▾"))
                    ShowTemplateMenu(templateRect);
            }

            GUILayout.Space(6f);
            GUILayout.Label("生成的剧本文本（粘贴进 .vn.txt）：", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_generatedText)
                ? "（点上方「生成文本」）" : _generatedText, GUILayout.MinHeight(70f));

            GUILayout.Space(6f);
            GUILayout.Label("解析已有 camseq 文本（粘贴后点「解析载入」）：", EditorStyles.boldLabel);
            _pasteText = EditorGUILayout.TextArea(_pasteText, GUILayout.MinHeight(60f));
            if (GUILayout.Button("解析载入"))
            {
                CommitUndo();
                ParseText();
            }

            EditorGUILayout.HelpBox(
                "画布：点空白 = 给选中点设坐标；点取景中心 = 选中；拖动 = 移动；" +
                "拖选中框的四角 = 改 zoom。\n" +
                "叠化：xfade>0 的点用「截屏→瞬切→淡出」代替平移；预览时白框瞬切到新视角、" +
                "橙色残框 = 正在淡出的旧视角。场景预览里叠化段表现为瞬切" +
                "（真实叠化由运行时截屏完成）。\n" +
                "开场 cut 只在剧本里紧跟带 transition 的 bg 时生效（首点时长须为 0）。\n" +
                "场景预览：开启后拖进度条/按 ▶，Game 视图实时显示真实画面运镜，" +
                "关闭或进出 Play 自动还原（场景可能显示未保存标记，属正常）。\n" +
                "捕获当前镜头：把场景里 ZoomRoot 的当前状态反推成一个路径点" +
                "（可先手动摆好 ZoomRoot 再捕获）。\n" +
                "编辑态下「角色部位」按假定站位显示，Play 中按真实位置。" +
                "缓动默认：单段 InOutSine；多段首 InSine / 中 Linear / 末 OutSine，" +
                "叠化段会把连续补间分成独立组（与运行时一致）。\n" +
                "绑定条：从剧本编辑器 camseq 行的「编排」按钮进来后，这里的改动可以直接回写那一行；" +
                "「跟随选中」时在剧本里点另一个 camseq 行会自动切过来。\n" +
                "hold：到达该点后停留的秒数（0 = 不停）。要「推到脸上停一秒再拉回」写 hold 就够了，" +
                "不用再补一个同点位、时长 0 的路径点。\n" +
                "震：到达该点的瞬间震一下屏幕（light/medium/heavy 或自定义「强度,秒数」）。" +
                "震完才走下一段——停顿取 max(hold, 震动时长)，不是相加。" +
                "预览时间轴会把这段停顿算进去，但不模拟抖动本身。\n" +
                "辅助线：整图模式画在选中路径点的取景框里，镜头视角模式铺满画布。" +
                "对话框遮挡区按场景里真实对话框的尺寸换算——特写时脸有没有被挡住看这条。\n" +
                "撤销：Ctrl+Z / Ctrl+Y（Ctrl+Shift+Z 也行），只作用于本窗口，不动 Unity 全局撤销；" +
                "拖滑条、连续输入会合并成一步；换绑定行会重开一段历史。",
                MessageType.Info);

            EditorGUILayout.EndScrollView();

            // 实时回写：放在最后，确保这一帧的所有编辑都已落到 _points 上
            if (_liveApply && HasLink) ApplyToLink();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_playing ? "■ 停止" : "▶ 预览", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    _playing = !_playing;
                    if (_playing)
                    {
                        if (_scrub >= TotalDuration() - 0.001f) _scrub = 0f;
                        _lastUpdateTime = EditorApplication.timeSinceStartup;
                    }
                }
                if (GUILayout.Button("生成文本→剪贴板", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    _generatedText = GenerateText();
                    EditorGUIUtility.systemCopyBuffer = _generatedText;
                    ShowNotification(new GUIContent("已复制到剪贴板"));
                }
                if (GUILayout.Button("清空", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    if (EditorUtility.DisplayDialog("清空", "确定清空全部路径点？", "清空", "取消"))
                    {
                        CommitUndo();
                        _points.Clear();
                        _startMode = StartMode.None;
                        _endFade = false;
                        _startFade = _endFadeDur = 0.6f;
                    }
                }

                GUILayout.Space(8f);
                using (new EditorGUI.DisabledScope(!CanUndo))
                    if (GUILayout.Button(new GUIContent("↶", "撤销（Ctrl+Z，只作用于本窗口）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(!CanRedo))
                    if (GUILayout.Button(new GUIContent("↷", "重做（Ctrl+Y / Ctrl+Shift+Z）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformRedo();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_points.Count} 个路径点", EditorStyles.miniLabel);
            }

            // 第二行：场景预览 / 捕获 / 预设库
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool cameraView = GUILayout.Toggle(_cameraView,
                    new GUIContent(_cameraView ? "镜头视角" : "整图",
                        "整图 = 全景 + 取景框（可拖点编辑）\n" +
                        "镜头视角 = 画布直接显示镜头里看到的画面，拖进度条 / ▶ 就是运镜动画（只看不改）"),
                    EditorStyles.toolbarButton, GUILayout.Width(60f));
                if (cameraView != _cameraView) _cameraView = cameraView;

                var guidesRect = GUILayoutUtility.GetRect(
                    new GUIContent("辅助线 ▾"), EditorStyles.toolbarButton, GUILayout.Width(64f));
                var prevBg = GUI.backgroundColor;
                if (_guides != Guides.None) GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);
                if (GUI.Button(guidesRect,
                        new GUIContent("辅助线 ▾",
                            "构图辅助线：三分线 / 中心十字 / 安全区 / 对话框遮挡区。\n" +
                            "整图模式画在选中路径点的取景框里，镜头视角模式铺满整个画布"),
                        EditorStyles.toolbarButton))
                    ShowGuidesMenu(guidesRect);
                GUI.backgroundColor = prevBg;

                bool newPreview = GUILayout.Toggle(_scenePreviewing,
                    new GUIContent("场景预览",
                        "把绑定行的背景/立绘摆进场景并接管 ZoomRoot，" +
                        "让 Game 视图显示带后处理的真实画面；关掉全部还原"),
                    EditorStyles.toolbarButton, GUILayout.Width(70f));
                if (newPreview != _scenePreviewing)
                {
                    if (newPreview) StartScenePreview();
                    else StopScenePreview();
                }

                if (GUILayout.Button("捕获当前镜头", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    CaptureCurrentCamera();

                GUILayout.Space(10f);
                GUILayout.Label("预设:", GUILayout.Width(34f));
                _presetName = GUILayout.TextField(_presetName, GUILayout.Width(90f));
                if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                    SavePreset();

                var names = PresetNames();
                _presetIndex = EditorGUILayout.Popup(_presetIndex, names, GUILayout.Width(110f));
                using (new EditorGUI.DisabledScope(names.Length == 0 || names[0] == "(无预设)"))
                {
                    if (GUILayout.Button("载入", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        LoadPreset();
                    if (GUILayout.Button("删除", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                        DeletePreset();
                }
                GUILayout.FlexibleSpace();
            }
        }

        void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var w = _points[index];
            float line = EditorGUIUtility.singleLineHeight;
            var r1 = new Rect(rect.x, rect.y + 3f, rect.width, line);
            var r2 = new Rect(rect.x, rect.y + line + 7f, rect.width, line);

            // 第一行：编号 + 类型 + 目标
            float x = r1.x;
            GUI.Label(new Rect(x, r1.y, 24f, line), $"{index + 1}."); x += 26f;
            w.type = (PointType)EditorGUI.EnumPopup(new Rect(x, r1.y, 74f, line), w.type); x += 78f;

            float remain = r1.xMax - x;
            switch (w.type)
            {
                case PointType.Stay:
                    GUI.Label(new Rect(x, r1.y, remain, line),
                        new GUIContent("沿用上一个点（位置与 zoom 都不变）",
                            "原地：镜头一动不动，专门用来在序列中间插一段震动或停顿。\n" +
                            "画布上不画它的取景框——与上一个点完全重合，画出来只会互相遮住。"),
                        EditorStyles.miniLabel);
                    break;

                case PointType.Anchor:
                    w.anchorIndex = EditorGUI.Popup(new Rect(x, r1.y, remain, line), w.anchorIndex, AnchorTokens);
                    break;
                case PointType.Character:
                {
                    var ids = SceneCharacterIds();
                    float third = remain / 3f;
                    if (ids.Length > 0)
                    {
                        int cur = System.Array.IndexOf(ids, w.charId);
                        int sel = EditorGUI.Popup(new Rect(x, r1.y, third, line), Mathf.Max(0, cur), ids);
                        w.charId = ids[sel];
                    }
                    else
                    {
                        w.charId = EditorGUI.TextField(new Rect(x, r1.y, third, line), w.charId);
                    }
                    w.partIndex = EditorGUI.Popup(new Rect(x + third, r1.y, third, line), w.partIndex, PartTokens);
                    w.slotIndex = EditorGUI.Popup(new Rect(x + third * 2f, r1.y, third, line), w.slotIndex, SlotNames);
                    break;
                }
                case PointType.Coords:
                    w.coords = EditorGUI.Vector2Field(new Rect(x, r1.y, remain, line), GUIContent.none, w.coords);
                    break;
            }

            // 第二行：zoom / 时长 / 缓动 / 叠化 / 停留 / 震
            x = r2.x + 26f;
            GUI.Label(new Rect(x, r2.y, 42f, line),
                new GUIContent("zoom", w.type == PointType.Stay
                    ? "原地点没有自己的 zoom，沿用上一个点" : "取景倍率：1 = 全图，越大越推近"));
            x += 44f;

            // 右边几个数字框宽度固定，zoom 滑条吃掉剩下的宽度（窗口拉窄时先压滑条）
            const float tail = 6f + 22f + 48f + 32f + 86f + 38f + 44f + 34f + 40f
                               + 6f + 24f + 118f;
            float sliderW = Mathf.Max(60f, r2.xMax - x - tail);
            if (w.type == PointType.Stay)
            {
                // 禁用占位而不是隐藏：位置留着，切换点位类型时下面几格不会左右横跳
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(new Rect(x, r2.y, sliderW, line), "沿用上一个点");
            }
            else
            {
                w.zoom = EditorGUI.Slider(new Rect(x, r2.y, sliderW, line), w.zoom, 0.5f, 3f);
            }
            x += sliderW + 6f;

            GUI.Label(new Rect(x, r2.y, 22f, line),
                new GUIContent("秒", "移动到本点的时长；0 = 瞬切")); x += 22f;
            w.duration = EditorGUI.FloatField(new Rect(x, r2.y, 42f, line), w.duration); x += 48f;
            GUI.Label(new Rect(x, r2.y, 32f, line), "ease"); x += 32f;
            w.easeIndex = EditorGUI.Popup(new Rect(x, r2.y, 82f, line), w.easeIndex, EaseNames); x += 86f;
            GUI.Label(new Rect(x, r2.y, 38f, line),
                new GUIContent("xfade", "叠化到本点的秒数（>0 时代替平移/瞬切）")); x += 38f;
            w.fade = Mathf.Max(0f, EditorGUI.FloatField(new Rect(x, r2.y, 40f, line), w.fade));
            x += 44f;
            GUI.Label(new Rect(x, r2.y, 34f, line),
                new GUIContent("hold", "到达本点后停留的秒数（0 = 不停，直接走下一段）")); x += 34f;
            w.hold = Mathf.Max(0f, EditorGUI.FloatField(new Rect(x, r2.y, 40f, line), w.hold));
            x += 46f;

            GUI.Label(new Rect(x, r2.y, 24f, line),
                new GUIContent("震", VNCamShakeUi.Tooltip)); x += 24f;
            w.shake = VNCamShakeUi.Draw(new Rect(x, r2.y, 118f, line), w.shake);
        }

        // ==================================================================
        // 迷你画布
        // ==================================================================

        void DrawCanvas(Rect rect)
        {
            // 底：背景缩略图或深色底
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.12f));

            // 镜头视角模式：整个画面按当前时刻的运镜变换后再画，
            // 画布 = 玩家真正看到的那一帧（拖进度条/▶ 就是运镜动画）
            var view = _cameraView
                ? PreviewAtTime(_scrub).state
                : new CamState { offset = Vector2.zero, zoom = 1f };

            // 内容会超出画布（放大后背景/立绘都溢出），统一裁进画布内
            GUI.BeginGroup(rect);
            var local = new Rect(0f, 0f, rect.width, rect.height);
            var bgSprite = CanvasBackdrop();
            if (bgSprite != null)
                DrawSpriteAt(local, ViewPoint(view, Vector2.zero), CanvasHalf * view.zoom, bgSprite);
            DrawStageCharacters(local, view);
            GUI.EndGroup();

            DrawRectOutline(rect, new Color(1f, 1f, 1f, 0.35f), 1f);

            // 镜头视角下画面本身就是取景结果，再叠取景框/路径线只会挡视线
            if (_cameraView)
            {
                // 画布 = 玩家看到的那一屏，辅助线直接铺满
                DrawCompositionGuides(rect, rect);
                GUI.Label(new Rect(rect.x + 6f, rect.y + 4f, 200f, 18f),
                    "镜头视角（拖进度条看运镜）", EditorStyles.whiteMiniLabel);
                return;
            }

            // 各路径点取景框 + 路径线
            Vector2? prevCenter = null;
            for (int i = 0; i < _points.Count; i++)
            {
                // stay 的取景框与上一个点完全重合，画出来只会互相遮住、还会拖错
                if (_points[i].type == PointType.Stay) continue;
                var state = TargetState(i);
                var center = -state.offset / state.zoom;      // 取景中心（画布坐标）
                var half = CanvasHalf / state.zoom;

                bool selected = _list.index == i;
                var color = selected
                    ? new Color(1f, 0.85f, 0.2f, 0.95f)
                    : new Color(0.3f, 0.85f, 1f, 0.55f);
                DrawCanvasFrame(rect, center, half, color, selected ? 2f : 1f);

                var guiCenter = CanvasToGui(rect, center);
                GUI.Label(new Rect(guiCenter.x - 8f, guiCenter.y - 9f, 30f, 18f),
                    (i + 1).ToString(), EditorStyles.whiteBoldLabel);

                if (prevCenter.HasValue)
                    DrawDottedLine(rect, prevCenter.Value, center, new Color(1f, 1f, 1f, 0.5f));
                prevCenter = center;
            }

            // 预览取景框（沿路径插值）；叠化时再画一个渐隐的橙色残框 = 正在淡出的旧视角
            if (_points.Count > 0)
            {
                var ps = PreviewAtTime(_scrub);
                var center = -ps.state.offset / ps.state.zoom;
                DrawCanvasFrame(rect, center, CanvasHalf / ps.state.zoom, Color.white, 2.5f);
                if (ps.fading)
                {
                    var gc = -ps.fadeFrom.offset / ps.fadeFrom.zoom;
                    DrawCanvasFrame(rect, gc, CanvasHalf / ps.fadeFrom.zoom,
                        new Color(1f, 0.6f, 0.2f, Mathf.Clamp01(ps.ghostAlpha)), 2f);
                }
            }

            // 构图辅助线画在「选中路径点的取景框」里——那一框才是玩家看到的一屏；
            // 没选中点就跟着预览框走
            if (_guides != Guides.None && _points.Count > 0)
            {
                var gs = _list.index >= 0 && _list.index < _points.Count
                    ? TargetState(_list.index)
                    : PreviewAtTime(_scrub).state;
                DrawCompositionGuides(
                    FrameGuiRect(rect, -gs.offset / gs.zoom, CanvasHalf / gs.zoom), rect);
            }

            HandleCanvasInput(rect);
        }

        /// <summary>
        /// 在场立绘：按剧本推算出的站位 + VNStage 的真实高度画，
        /// 取景框套没套住脸能直接看出来。关掉开关 / 没绑定 / 没立绘可用时，
        /// 退回原来那三个灰色站位矩形。
        /// </summary>
        /// <summary>画布坐标 → 运镜变换后的坐标（与运行时 ZoomRoot 的缩放+平移同构）</summary>
        static Vector2 ViewPoint(CamState view, Vector2 p) => p * view.zoom + view.offset;

        void DrawStageCharacters(Rect rect, CamState view)
        {
            var info = RowStage;
            var stage = Object.FindFirstObjectByType<VNStage>();
            bool drew = false;

            if (_showPortraits && info != null && info.characters.Count > 0)
            {
                foreach (var c in info.characters)
                {
                    if (c.sprite == null) continue;

                    float height = 880f;
                    Vector2 offset = Vector2.zero;
                    if (stage != null)
                    {
                        var def = stage.characters.Find(d => d != null && d.id == c.id);
                        if (def != null)
                        {
                            height = stage.characterHeight * Mathf.Max(0.05f, def.sizeScale);
                            offset = def.positionOffset;
                        }
                    }
                    // 立绘 RectTransform 的锚点位置就是中心（与 VNStage.SlotPosition 一致）
                    var center = new Vector2(SlotX[Mathf.Clamp(c.slot, 0, 2)], -60f) + offset;
                    float aspect = c.sprite.rect.width / Mathf.Max(1f, c.sprite.rect.height);
                    var half = new Vector2(height * aspect * 0.5f, height * 0.5f);
                    DrawSpriteAt(rect, ViewPoint(view, center), half * view.zoom, c.sprite);
                    drew = true;
                }
            }

            if (drew) return;
            for (int s = 0; s < 3; s++)
            {
                var p = CanvasToGui(rect, ViewPoint(view, new Vector2(SlotX[s], -60f)));
                float hw = 880f * 0.28f * view.zoom * rect.width / 1920f;
                float hh = 880f * 0.5f * view.zoom * rect.height / 1080f;
                EditorGUI.DrawRect(new Rect(p.x - hw * 0.5f, p.y - hh, hw, hh * 2f),
                    new Color(1f, 1f, 1f, 0.05f));
            }
        }

        /// <summary>按画布坐标把 sprite 画到指定矩形（图集 sprite 走 textureRect 取 UV）</summary>
        static void DrawSpriteAt(Rect canvasRect, Vector2 center, Vector2 half, Sprite sprite)
        {
            var tl = CanvasToGui(canvasRect, new Vector2(center.x - half.x, center.y + half.y));
            var br = CanvasToGui(canvasRect, new Vector2(center.x + half.x, center.y - half.y));
            var dst = Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
            // 放大后完全移出画布的（镜头推到别处）直接跳过，省掉无意义的绘制
            if (dst.xMax < canvasRect.x || dst.x > canvasRect.xMax ||
                dst.yMax < canvasRect.y || dst.y > canvasRect.yMax) return;
            DrawSpriteRaw(dst, sprite, true);
        }

        static void DrawSpriteRaw(Rect dst, Sprite sprite, bool alphaBlend)
        {
            var texture = sprite.texture;
            if (texture == null) return;
            // 图集里的 sprite 不能整张 texture 当图用，必须按 textureRect 取 UV
            var source = sprite.textureRect;
            var uv = new Rect(
                source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(dst, texture, uv, alphaBlend);
        }

        void HandleCanvasInput(Rect rect)
        {
            var e = Event.current;

            if (e.type == EventType.MouseUp)
            {
                _dragMode = DragMode.None;
                return;
            }
            if (!rect.Contains(e.mousePosition)) return;

            bool hasSelection = _list.index >= 0 && _list.index < _points.Count;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                var click = GuiToCanvas(rect, e.mousePosition);

                // 1) 选中路径点的取景框四角（GUI 12px 内）→ 拖角改 zoom
                if (hasSelection && _points[_list.index].type != PointType.Stay)
                {
                    var st = TargetState(_list.index);
                    var center = -st.offset / st.zoom;
                    var half = CanvasHalf / st.zoom;
                    for (int cx = -1; cx <= 1; cx += 2)
                    for (int cy = -1; cy <= 1; cy += 2)
                    {
                        var cornerGui = CanvasToGui(rect,
                            center + new Vector2(half.x * cx, half.y * cy));
                        if (Vector2.Distance(cornerGui, e.mousePosition) < 12f)
                        {
                            _dragMode = DragMode.Corner;
                            e.Use();
                            return;
                        }
                    }
                }

                // 2) 任意取景中心 60 画布单位内 → 选中该点并可拖动
                int nearest = -1;
                float best = 60f;
                for (int i = 0; i < _points.Count; i++)
                {
                    if (_points[i].type == PointType.Stay) continue;  // 画布上没有它的框
                    var st = TargetState(i);
                    float d = Vector2.Distance(-st.offset / st.zoom, click);
                    if (d < best) { best = d; nearest = i; }
                }
                if (nearest >= 0)
                {
                    _list.index = nearest;
                    _dragMode = DragMode.Center;
                }
                else if (hasSelection && _points[_list.index].type != PointType.Stay)
                {
                    // 3) 空白处点击 = 给选中点设坐标（原地点没有自己的位置，别改它）
                    var w = _points[_list.index];
                    w.type = PointType.Coords;
                    w.coords = Round(click);
                    _dragMode = DragMode.Center;
                }
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && hasSelection &&
                     _points[_list.index].type != PointType.Stay)
            {
                var w = _points[_list.index];
                if (_dragMode == DragMode.Corner)
                {
                    // 拖角改 zoom：以取景中心为基准，指针到中心的距离 = 新的取景半宽/半高
                    var st = TargetState(w);
                    var center = -st.offset / st.zoom;
                    var mouse = GuiToCanvas(rect, e.mousePosition);
                    var half = new Vector2(
                        Mathf.Max(20f, Mathf.Abs(mouse.x - center.x)),
                        Mathf.Max(12f, Mathf.Abs(mouse.y - center.y)));
                    float zoom = Mathf.Max(CanvasHalf.x / half.x, CanvasHalf.y / half.y);
                    w.zoom = Mathf.Clamp(zoom, 0.5f, 3f);
                }
                else if (_dragMode == DragMode.Center)
                {
                    w.type = PointType.Coords;
                    w.coords = Round(GuiToCanvas(rect, e.mousePosition));
                }
                e.Use();
                Repaint();
            }
        }

        // ==================================================================
        // 场景内实时预览 / 捕获当前镜头
        // ==================================================================

        RectTransform FindZoomRoot()
        {
            var cam = Object.FindFirstObjectByType<VNCamera>();
            if (cam != null && cam.target != null) return cam.target;
            var go = GameObject.Find("ZoomRoot");
            return go != null ? go.transform as RectTransform : null;
        }

        void StartScenePreview()
        {
            _zoomRoot = FindZoomRoot();
            if (_zoomRoot == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot（先生成剧本演示场景）"));
                return;
            }
            _origPos = _zoomRoot.anchoredPosition;
            _origScale = _zoomRoot.localScale;
            _scenePreviewing = true;
            ApplyStageToScene();
            ApplySceneState();
        }

        void StopScenePreview()
        {
            RestoreStage();
            if (!_scenePreviewing) return;
            _scenePreviewing = false;
            if (_zoomRoot != null)
            {
                _zoomRoot.anchoredPosition = _origPos;
                _zoomRoot.localScale = _origScale;
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        // ==================================================================
        // 舞台预览：把绑定行的背景 / 立绘摆进场景，让 Game 视图也是那一行的画面
        // ==================================================================

        /// <summary>
        /// 按绑定行的推算结果摆舞台。背景只换 sprite（原值记下来可还原）；
        /// 立绘造临时 GameObject——**只挂最小的 RectTransform + Image**，
        /// 不带运行时那一堆 fx / blink / mouth 组件（它们的 Awake 在编辑期会乱来）。
        /// 全部标 HideFlags.DontSave：绝不写进场景文件，域重载也会被自动销毁。
        /// </summary>
        void ApplyStageToScene()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var info = RowStage;
            if (stage == null || info == null) return;
            _stagedRow = _linkedRow;

            if (info.backdrop != null && stage.backgroundImage != null)
            {
                if (!_bgTouched)
                {
                    _origBgSprite = stage.backgroundImage.sprite;
                    _bgTouched = true;
                }
                stage.backgroundImage.sprite = info.backdrop;
            }

            ClearPreviewCharacters();
            if (stage.characterLayer == null) return;
            foreach (var c in info.characters)
            {
                if (c.sprite == null) continue;

                float height = 880f;
                Vector2 offset = Vector2.zero;
                var def = stage.characters.Find(d => d != null && d.id == c.id);
                if (def != null)
                {
                    height = stage.characterHeight * Mathf.Max(0.05f, def.sizeScale);
                    offset = def.positionOffset;
                }

                var go = new GameObject($"[camseq预览] {c.id}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                { hideFlags = HideFlags.DontSave };
                var rect = (RectTransform)go.transform;
                rect.SetParent(stage.characterLayer, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition =
                    new Vector2(SlotX[Mathf.Clamp(c.slot, 0, 2)], -60f) + offset;

                float aspect = c.sprite.rect.width / Mathf.Max(1f, c.sprite.rect.height);
                rect.sizeDelta = new Vector2(height * aspect, height);

                var img = go.GetComponent<Image>();
                img.sprite = c.sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                _previewChars.Add(go);
            }
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void RestoreStage()
        {
            ClearPreviewCharacters();
            _stagedRow = -1;
            if (!_bgTouched) return;

            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage != null && stage.backgroundImage != null)
                stage.backgroundImage.sprite = _origBgSprite;
            _bgTouched = false;
            _origBgSprite = null;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        void ClearPreviewCharacters()
        {
            foreach (var go in _previewChars)
                if (go != null) DestroyImmediate(go);
            _previewChars.Clear();
        }

        void ApplySceneState()
        {
            if (_zoomRoot == null)
            {
                StopScenePreview();
                return;
            }
            // 叠化段里 ZoomRoot 直接是目标状态（真实叠化由运行时的截屏覆盖层完成）
            var s = PreviewAtTime(_scrub).state;
            _zoomRoot.localScale = Vector3.one * s.zoom;
            _zoomRoot.anchoredPosition = _origPos + s.offset;
            EditorApplication.QueuePlayerLoopUpdate(); // 编辑态强制刷新 Game 视图
        }

        /// <summary>把 ZoomRoot 当前的实际状态反推成一个路径点（坐标类型）</summary>
        void CaptureCurrentCamera()
        {
            var root = _zoomRoot != null ? _zoomRoot : FindZoomRoot();
            if (root == null)
            {
                ShowNotification(new GUIContent("场景里找不到 ZoomRoot"));
                return;
            }
            // 预览中用记录的基准位；否则假定基准为当前值即无偏移的 (0,0)
            var basePos = _scenePreviewing ? _origPos : Vector2.zero;
            float zoom = Mathf.Max(0.1f, root.localScale.x);
            Vector2 offset = root.anchoredPosition - basePos;
            Vector2 point = -offset / zoom;

            _points.Add(new Waypoint
            {
                type = PointType.Coords,
                coords = Round(point),
                zoom = Mathf.Clamp(zoom, 0.5f, 3f),
                duration = 0.8f,
            });
            _list.index = _points.Count - 1;
            ShowNotification(new GUIContent($"已捕获：({point.x:0},{point.y:0}) ×{zoom:0.##}"));
        }

        // ==================================================================
        // 预设库
        // ==================================================================

        VNCamseqPresetLibrary EnsureLibrary()
        {
            if (_library != null) return _library;
            _library = LoadOrCreateLibrary();
            return _library;
        }

        static VNCamseqPresetLibrary LoadOrCreateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            if (library != null) return library;

            string folder = System.IO.Path.GetDirectoryName(LibraryPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets", "VNEffects");
            library = CreateInstance<VNCamseqPresetLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>存一条预设（剧本编辑器的「把本行存为预设」也走这里，同名覆盖）</summary>
        public static void SavePreset(string name, string camseqText)
        {
            if (string.IsNullOrEmpty(name)) return;
            var library = LoadOrCreateLibrary();
            var existing = library.presets.Find(p => p != null && p.name == name);
            if (existing != null) existing.camseqText = camseqText;
            else library.presets.Add(new VNCamseqPresetLibrary.Preset
                { name = name, camseqText = camseqText });
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        string[] PresetNames()
        {
            if (_library == null)
                _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            if (_library == null || _library.presets.Count == 0)
                return new[] { "(无预设)" };
            var names = new string[_library.presets.Count];
            for (int i = 0; i < names.Length; i++) names[i] = _library.presets[i].name;
            return names;
        }

        void SavePreset()
        {
            if (_points.Count == 0)
            {
                ShowNotification(new GUIContent("没有路径点可保存"));
                return;
            }
            string name = string.IsNullOrEmpty(_presetName.Trim())
                ? $"预设{System.DateTime.Now:HHmmss}" : _presetName.Trim();

            SavePreset(name, GenerateText());
            _library = EnsureLibrary();
            _presetIndex = _library.presets.FindIndex(p => p.name == name);
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
        }

        /// <summary>内置运镜模板菜单：套用 = 整段替换当前编排（角色占位按场景里的第一个角色填）</summary>
        void ShowTemplateMenu(Rect rect)
        {
            var ids = SceneCharacterIds();
            string character = ids.Length > 0 ? ids[0] : null;
            var menu = new GenericMenu();
            foreach (var entry in VNCamseqTemplates.All)
            {
                string text = VNCamseqTemplates.Resolve(entry.text, character);
                menu.AddItem(new GUIContent(entry.name), false, () =>
                {
                    CommitUndo();      // 整段替换，撤销时要能一步回到套用前
                    _pasteText = text;
                    ParseText();
                });
            }
            menu.DropDown(rect);
        }

        void LoadPreset()
        {
            if (_library == null || _presetIndex < 0 || _presetIndex >= _library.presets.Count) return;
            CommitUndo();          // 整段替换，撤销时要能一步回到载入前
            _pasteText = _library.presets[_presetIndex].camseqText;
            ParseText();
            _presetName = _library.presets[_presetIndex].name;
        }

        void DeletePreset()
        {
            if (_library == null || _presetIndex < 0 || _presetIndex >= _library.presets.Count) return;
            string name = _library.presets[_presetIndex].name;
            if (!EditorUtility.DisplayDialog("删除预设", $"删除「{name}」？", "删除", "取消")) return;
            _library.presets.RemoveAt(_presetIndex);
            _presetIndex = 0;
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssets();
        }

        static Vector2 Round(Vector2 v) =>
            new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));

        // 画布坐标（中心原点，y 向上）↔ GUI 像素
        static Vector2 CanvasToGui(Rect rect, Vector2 canvas)
        {
            return new Vector2(
                rect.x + (canvas.x + CanvasHalf.x) / (CanvasHalf.x * 2f) * rect.width,
                rect.y + (1f - (canvas.y + CanvasHalf.y) / (CanvasHalf.y * 2f)) * rect.height);
        }

        static Vector2 GuiToCanvas(Rect rect, Vector2 gui)
        {
            return new Vector2(
                (gui.x - rect.x) / rect.width * CanvasHalf.x * 2f - CanvasHalf.x,
                (1f - (gui.y - rect.y) / rect.height) * CanvasHalf.y * 2f - CanvasHalf.y);
        }

        void DrawCanvasFrame(Rect rect, Vector2 center, Vector2 half, Color color, float thickness)
            => DrawRectOutline(FrameGuiRect(rect, center, half), color, thickness);

        /// <summary>取景框（画布坐标的中心 + 半尺寸）在 GUI 里占的矩形</summary>
        static Rect FrameGuiRect(Rect canvasRect, Vector2 center, Vector2 half)
        {
            var tl = CanvasToGui(canvasRect, new Vector2(center.x - half.x, center.y + half.y));
            var br = CanvasToGui(canvasRect, new Vector2(center.x + half.x, center.y - half.y));
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        static void DrawRectOutline(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        void DrawDottedLine(Rect rect, Vector2 fromCanvas, Vector2 toCanvas, Color color)
        {
            const int dots = 24;
            for (int i = 0; i <= dots; i++)
            {
                var p = CanvasToGui(rect, Vector2.Lerp(fromCanvas, toCanvas, i / (float)dots));
                EditorGUI.DrawRect(new Rect(p.x - 1f, p.y - 1f, 2f, 2f), color);
            }
        }

        // ==================================================================
        // 构图辅助线
        // ==================================================================

        void ShowGuidesMenu(Rect rect)
        {
            var menu = new GenericMenu();
            void Toggle(string label, Guides bit) =>
                menu.AddItem(new GUIContent(label), (_guides & bit) != 0,
                    () => SetGuides(_guides ^ bit));

            Toggle("三分线", Guides.Thirds);
            Toggle("中心十字", Guides.Center);
            Toggle("安全区(90%)", Guides.SafeArea);
            Toggle("对话框遮挡区", Guides.DialogueBox);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("全开"), false, () => SetGuides(Guides.All));
            menu.AddItem(new GUIContent("全关"), false, () => SetGuides(Guides.None));
            menu.DropDown(rect);
        }

        void SetGuides(Guides value)
        {
            _guides = value;
            EditorPrefs.SetInt(GuidesPrefKey, (int)value);
            Repaint();
        }

        /// <summary>
        /// 在「一屏画面」上画构图辅助线。
        /// <paramref name="frame"/> = 那一屏在 GUI 里的矩形：整图模式下是选中路径点的
        /// 取景框（辅助线跟着框缩放平移），镜头视角模式下就是整个画布。
        /// <paramref name="clip"/> = 画布矩形，超出的部分裁掉（zoom&lt;1 时取景框比画布还大）。
        ///
        /// 【为什么对话框遮挡区按比例画在 frame 里】对话框是 Canvas 下 ZoomRoot 的**兄弟**，
        /// 不随镜头缩放平移 —— 它在玩家眼里永远压着屏幕底部那一条。所以它要按"占一屏的
        /// 比例"落在取景框内，而不是画布上的某个固定位置。
        /// </summary>
        void DrawCompositionGuides(Rect frame, Rect clip)
        {
            if (_guides == Guides.None || frame.width < 4f || frame.height < 4f) return;

            if ((_guides & Guides.Thirds) != 0)
            {
                var c = new Color(1f, 1f, 1f, 0.3f);
                for (int i = 1; i <= 2; i++)
                {
                    float t = i / 3f;
                    VLine(frame.x + frame.width * t, frame.yMin, frame.yMax, c, clip);
                    HLine(frame.y + frame.height * t, frame.xMin, frame.xMax, c, clip);
                }
            }

            if ((_guides & Guides.Center) != 0)
            {
                var c = new Color(0.45f, 1f, 0.7f, 0.45f);
                VLine(frame.center.x, frame.yMin, frame.yMax, c, clip);
                HLine(frame.center.y, frame.xMin, frame.xMax, c, clip);
            }

            if ((_guides & Guides.SafeArea) != 0)
            {
                float w = frame.width * 0.9f, h = frame.height * 0.9f;
                var safe = new Rect(frame.center.x - w * 0.5f, frame.center.y - h * 0.5f, w, h);
                DrawRectOutlineClipped(safe, new Color(0.4f, 1f, 0.55f, 0.5f), 1f, clip);
            }

            if ((_guides & Guides.DialogueBox) != 0)
            {
                var band = DialogueBandFractions();
                var r = Rect.MinMaxRect(
                    frame.x + band.xMin * frame.width, frame.y + band.yMin * frame.height,
                    frame.x + band.xMax * frame.width, frame.y + band.yMax * frame.height);
                DrawRectClipped(r, new Color(1f, 0.35f, 0.35f, 0.15f), clip);
                DrawRectOutlineClipped(r, new Color(1f, 0.45f, 0.45f, 0.55f), 1f, clip);
            }
        }

        /// <summary>
        /// 对话框在一屏里占的比例（x 自左、y 自上，0~1）。
        /// 优先量场景里真实的对话框（换了皮肤/改了尺寸都跟得上）；
        /// 量不到就退回演示场景生成器的默认布局（1920×1080 下 x 5%~95%、
        /// 底边上方 28px 起、高 230px）。
        /// </summary>
        static Rect DialogueBandFractions()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var box = stage != null && stage.dialogue != null
                ? stage.dialogue : Object.FindFirstObjectByType<VNDialogueBox>();
            var rt = box != null ? box.transform as RectTransform : null;
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            var root = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;

            if (rt != null && root != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);            // 0=左下 1=左上 2=右上 3=右下
                var a = root.InverseTransformPoint(corners[0]);
                var b = root.InverseTransformPoint(corners[2]);
                var cr = root.rect;
                if (cr.width > 1f && cr.height > 1f)
                {
                    float x0 = Mathf.InverseLerp(cr.xMin, cr.xMax, Mathf.Min(a.x, b.x));
                    float x1 = Mathf.InverseLerp(cr.xMin, cr.xMax, Mathf.Max(a.x, b.x));
                    // GUI 的 y 自上而下，画布的 y 自下而上 → 上下对调
                    float y0 = 1f - Mathf.InverseLerp(cr.yMin, cr.yMax, Mathf.Max(a.y, b.y));
                    float y1 = 1f - Mathf.InverseLerp(cr.yMin, cr.yMax, Mathf.Min(a.y, b.y));
                    if (x1 > x0 && y1 > y0)
                        return Rect.MinMaxRect(Mathf.Clamp01(x0), Mathf.Clamp01(y0),
                                               Mathf.Clamp01(x1), Mathf.Clamp01(y1));
                }
            }

            const float h = 230f / 1080f, bottom = 28f / 1080f;
            return Rect.MinMaxRect(0.05f, 1f - bottom - h, 0.95f, 1f - bottom);
        }

        static void DrawRectClipped(Rect r, Color c, Rect clip)
        {
            var i = Rect.MinMaxRect(
                Mathf.Max(r.xMin, clip.xMin), Mathf.Max(r.yMin, clip.yMin),
                Mathf.Min(r.xMax, clip.xMax), Mathf.Min(r.yMax, clip.yMax));
            if (i.width > 0f && i.height > 0f) EditorGUI.DrawRect(i, c);
        }

        static void DrawRectOutlineClipped(Rect r, Color c, float t, Rect clip)
        {
            DrawRectClipped(new Rect(r.x, r.y, r.width, t), c, clip);
            DrawRectClipped(new Rect(r.x, r.yMax - t, r.width, t), c, clip);
            DrawRectClipped(new Rect(r.x, r.y, t, r.height), c, clip);
            DrawRectClipped(new Rect(r.xMax - t, r.y, t, r.height), c, clip);
        }

        static void VLine(float x, float y0, float y1, Color c, Rect clip) =>
            DrawRectClipped(new Rect(x, y0, 1f, y1 - y0), c, clip);

        static void HLine(float y, float x0, float x1, Color c, Rect clip) =>
            DrawRectClipped(new Rect(x0, y, x1 - x0, 1f), c, clip);

        // ==================================================================
        // 预览插值（与运行时同一套公式）
        // ==================================================================

        struct CamState { public Vector2 offset; public float zoom; }

        Vector2 PreviewPoint(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Anchor: return AnchorPositions[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Coords: return w.coords;
                case PointType.Character:
                {
                    float height = 880f;
                    Vector2 offset = Vector2.zero;
                    var stage = Object.FindFirstObjectByType<VNStage>();
                    if (stage != null)
                    {
                        var def = stage.characters.Find(c => c != null && c.id == w.charId);
                        if (def != null)
                        {
                            height = stage.characterHeight * Mathf.Max(0.05f, def.sizeScale);
                            offset = def.positionOffset;
                        }
                    }
                    var basePos = new Vector2(SlotX[Mathf.Clamp(w.slotIndex, 0, 2)], -60f) + offset;
                    float frac = PartFracs[Mathf.Clamp(w.partIndex, 0, PartFracs.Length - 1)];
                    return basePos + new Vector2(0f, height * frac);
                }
            }
            return Vector2.zero;
        }

        /// <summary>
        /// 按下标取镜头状态：<c>stay</c> 点沿用**前面最近一个真点位**的位置与 zoom
        /// （与运行时 CamseqCo 里 lastPoint/lastZoom 那套是同一条规则）。
        /// </summary>
        CamState TargetState(int index)
        {
            int i = Mathf.Clamp(index, 0, _points.Count - 1);
            while (i > 0 && _points[i].type == PointType.Stay) i--;
            return TargetState(_points[i]);
        }

        CamState TargetState(Waypoint w)
        {
            float zoom = Mathf.Max(0.1f, w.zoom);
            return new CamState
            {
                zoom = zoom,
                offset = VNCamera.ComputeOffset(PreviewPoint(w), zoom, CanvasHalf, Overscan, true),
            };
        }

        /// <summary>预览时间轴上的一段：补间段沿缓动移动；叠化段镜头瞬切、旧画面淡出</summary>
        struct Segment
        {
            public CamState target;
            public float duration;
            public bool isFade;
            public bool isHold; // hold 段：停在 target 不动（不参与默认缓动的首/末判定）
            public Ease ease;   // 补间段缓动（isFade / isHold 时无意义）
        }

        /// <summary>
        /// 把 开场fade + 路径点(xfade 覆盖) + 收尾fade 展开成时间轴段列表。
        /// 组内缓动默认与运行时一致：叠化段把连续补间点分成独立组，
        /// 每组 首 InSine / 中 Linear / 末 OutSine（单段 InOutSine）。
        /// start:cut 无需特殊段——首点本来就是时长 0 的瞬切（运行时并入 bg 转场）。
        /// </summary>
        List<Segment> BuildSegments()
        {
            var segs = new List<Segment>();
            var pointOf = new List<int>();   // 各段对应的 _points 下标（收尾段 = -1）

            int start = 0;
            if (_points.Count > 0 && _startMode == StartMode.Fade)
            {
                segs.Add(new Segment
                {
                    target = TargetState(0),
                    duration = Mathf.Max(0.05f, _startFade),
                    isFade = true,
                });
                pointOf.Add(0);
                AddHoldSegment(segs, pointOf, 0);
                start = 1;
            }
            for (int i = start; i < _points.Count; i++)
            {
                var w = _points[i];
                if (w.fade > 0.001f)
                    segs.Add(new Segment
                        { target = TargetState(i), duration = w.fade, isFade = true });
                else
                    segs.Add(new Segment
                        { target = TargetState(i), duration = Mathf.Max(0f, w.duration) });
                pointOf.Add(i);
                AddHoldSegment(segs, pointOf, i);
            }
            if (_endFade)
            {
                segs.Add(new Segment
                {
                    target = new CamState { offset = Vector2.zero, zoom = 1f },
                    duration = Mathf.Max(0.05f, _endFadeDur),
                    isFade = true,
                });
                pointOf.Add(-1);
            }

            // 按叠化段切组，组内分配默认缓动
            int g = 0;
            while (g < segs.Count)
            {
                if (segs[g].isFade) { g++; continue; }
                int gEnd = g;
                while (gEnd < segs.Count && !segs[gEnd].isFade) gEnd++;

                int firstMove = -1, lastMove = -1, moveCount = 0;
                for (int k = g; k < gEnd; k++)
                {
                    // hold 段只是停顿，不算"移动段"——否则默认缓动的首/末会算错，
                    // 与运行时 BuildSegment（只看 duration 字段）对不上
                    if (!segs[k].isHold && segs[k].duration > 0.001f)
                    {
                        if (firstMove < 0) firstMove = k;
                        lastMove = k;
                        moveCount++;
                    }
                }
                for (int k = g; k < gEnd; k++)
                {
                    var s = segs[k];
                    if (s.isHold) continue;
                    int pi = pointOf[k];
                    if (pi >= 0 && _points[pi].easeIndex > 0 &&
                        System.Enum.TryParse(EaseNames[_points[pi].easeIndex], true, out Ease custom))
                        s.ease = custom;
                    else
                        s.ease = moveCount <= 1 ? Ease.InOutSine
                            : k == firstMove ? Ease.InSine
                            : k == lastMove ? Ease.OutSine
                            : Ease.Linear;
                    segs[k] = s;
                }
                g = gEnd;
            }
            return segs;
        }

        /// <summary>
        /// 该点带 hold（或 shake）就补一段「停在原地」的时间轴段
        /// （运行时是 Sequence 里的 Interval）。
        /// 停顿 = max(hold, 震动时长)，与运行时 VNCamera.BuildSegment 同一条规则——
        /// 这里算短了，预览进度条就会和实机对不上。震动本身不在预览里模拟。
        /// </summary>
        void AddHoldSegment(List<Segment> segs, List<int> pointOf, int index)
        {
            var w = _points[index];
            float stall = w.hold;
            if (VNShakeSpec.TryParse(w.shake, out VNShakeSpec spec) && spec.Valid)
                stall = Mathf.Max(stall, spec.duration);
            if (stall <= 0.001f) return;
            segs.Add(new Segment
                { target = TargetState(index), duration = stall, isHold = true });
            pointOf.Add(index);
        }

        float TotalDuration()
        {
            float t = 0f;
            foreach (var s in BuildSegments()) t += s.duration;
            return t;
        }

        /// <summary>某时刻的预览状态：镜头状态 + 叠化中的旧画面（画布上画橙色残框）</summary>
        struct PreviewState
        {
            public CamState state;
            public bool fading;
            public CamState fadeFrom;
            public float ghostAlpha;   // 旧画面剩余不透明度（按运行时 InOutSine 淡出）
        }

        PreviewState PreviewAtTime(float time)
        {
            var prev = new CamState { offset = Vector2.zero, zoom = 1f };
            var ps = new PreviewState { state = prev };
            if (_points.Count == 0) return ps;

            float t = time;
            foreach (var s in BuildSegments())
            {
                if (s.duration <= 0.001f)
                {
                    prev = s.target; // 瞬切
                    ps.state = prev;
                    continue;
                }
                if (t >= s.duration)
                {
                    t -= s.duration;
                    prev = s.target;
                    ps.state = prev;
                    continue;
                }
                if (s.isHold)
                {
                    ps.state = s.target;   // 停在原地
                    return ps;
                }
                if (s.isFade)
                {
                    // 叠化段：镜头开段即瞬切到目标，旧画面 InOutSine 淡出
                    float eased = EaseManager.Evaluate(Ease.InOutSine, null, t, s.duration, 1.70158f, 0f);
                    ps.state = s.target;
                    ps.fading = true;
                    ps.fadeFrom = prev;
                    ps.ghostAlpha = 1f - eased;
                    return ps;
                }
                float k = EaseManager.Evaluate(s.ease, null, t, s.duration, 1.70158f, 0f);
                ps.state = new CamState
                {
                    offset = Vector2.LerpUnclamped(prev.offset, s.target.offset, k),
                    zoom = Mathf.LerpUnclamped(prev.zoom, s.target.zoom, k),
                };
                return ps;
            }
            return ps;
        }

        // ==================================================================
        // 文本生成 / 解析
        // ==================================================================

        string PointToken(Waypoint w)
        {
            switch (w.type)
            {
                case PointType.Stay:
                    return VNCamWaypointDef.StayToken;
                case PointType.Anchor:
                    return AnchorTokens[Mathf.Clamp(w.anchorIndex, 0, 8)];
                case PointType.Character:
                    return w.partIndex > 0 ? $"{w.charId}:{PartTokens[w.partIndex]}" : w.charId;
                default:
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0:0.#},{1:0.#}", w.coords.x, w.coords.y);
            }
        }

        string GenerateText()
        {
            var sb = new StringBuilder("camseq");
            if (_startMode == StartMode.Cut)
            {
                sb.Append(" start:cut");
            }
            else if (_startMode == StartMode.Fade)
            {
                sb.Append(" start:fade");
                if (Mathf.Abs(_startFade - 0.6f) > 0.001f)
                    sb.Append(" startfade:")
                      .Append(_startFade.ToString("0.##", CultureInfo.InvariantCulture));
            }
            if (_endFade)
            {
                sb.Append(" end:fade");
                if (Mathf.Abs(_endFadeDur - 0.6f) > 0.001f)
                    sb.Append(" endfade:")
                      .Append(_endFadeDur.ToString("0.##", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');

            foreach (var w in _points)
            {
                sb.Append("> ").Append(PointToken(w));
                // stay 行没有 zoom（沿用上一个点），数字位前移：唯一的数字就是时长。
                // 这里多写一个 zoom 出去，运行时会把它当成时长，整条运镜的节奏就错了
                if (w.type != PointType.Stay)
                    sb.Append(' ').Append(w.zoom.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(' ').Append(w.duration.ToString("0.##", CultureInfo.InvariantCulture));
                if (w.easeIndex > 0) sb.Append(" ease:").Append(EaseNames[w.easeIndex]);
                if (w.fade > 0.001f)
                    sb.Append(" xfade:").Append(w.fade.ToString("0.##", CultureInfo.InvariantCulture));
                if (w.hold > 0.001f)
                    sb.Append(" hold:").Append(w.hold.ToString("0.##", CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(w.shake)) sb.Append(" shake:").Append(w.shake);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>silent = 绑定载入时用，不弹通知也不因空序列而放弃（允许清成 0 个点）</summary>
        void ParseText(bool silent = false) => ParseTextFrom(_pasteText, silent);

        /// <summary>撤销恢复走这个重载：不碰下方那个粘贴框的内容</summary>
        void ParseTextFrom(string text, bool silent)
        {
            var commands = VNScriptParser.Parse(text);
            VNScriptCommand camseq = null;
            foreach (var c in commands)
                if (c.keyword == "camseq" && c.camPoints != null &&
                    (silent || c.camPoints.Count > 0))
                {
                    camseq = c;
                    break;
                }
            if (camseq == null)
            {
                if (!silent) ShowNotification(new GUIContent("没有找到含路径点的 camseq 块"));
                return;
            }

            // camseq 级 start:/end: 选项
            string startKw = camseq.Kw("start");
            _startMode = startKw == "cut" ? StartMode.Cut
                       : startKw == "fade" ? StartMode.Fade : StartMode.None;
            _startFade = camseq.KwF("startfade", 0.6f);
            _endFade = camseq.Kw("end") == "fade";
            _endFadeDur = camseq.KwF("endfade", 0.6f);

            _points.Clear();
            foreach (var def in camseq.camPoints)
            {
                var w = new Waypoint
                {
                    zoom = def.zoom, duration = def.duration,
                    fade = def.fade, hold = def.hold, shake = def.shake ?? "",
                };

                int anchor = System.Array.IndexOf(AnchorTokens, def.point.ToLower());
                if (def.point.ToLower() == "center" || def.point.ToLower() == "origin"
                    || def.point.ToLower() == "reset") anchor = 4;

                if (VNCamWaypointDef.IsStay(def.point))
                {
                    // 原地：没有自己的点位与 zoom，其余字段（ease/hold/shake）照常走下面
                    w.type = PointType.Stay;
                }
                else if (anchor >= 0)
                {
                    w.type = PointType.Anchor;
                    w.anchorIndex = anchor;
                }
                else if (def.point.Contains(","))
                {
                    w.type = PointType.Coords;
                    var parts = def.point.Split(',');
                    float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.x);
                    if (parts.Length > 1)
                        float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w.coords.y);
                }
                else
                {
                    w.type = PointType.Character;
                    int colon = def.point.IndexOf(':');
                    w.charId = colon > 0 ? def.point.Substring(0, colon) : def.point;
                    if (colon > 0)
                    {
                        int part = System.Array.IndexOf(PartTokens, def.point.Substring(colon + 1).ToLower());
                        w.partIndex = Mathf.Max(0, part);
                    }
                }

                if (!string.IsNullOrEmpty(def.ease))
                {
                    for (int i = 1; i < EaseNames.Length; i++)
                        if (string.Equals(EaseNames[i], def.ease, System.StringComparison.OrdinalIgnoreCase))
                        {
                            w.easeIndex = i;
                            break;
                        }
                }
                _points.Add(w);
            }
            _scrub = 0f;
            if (!silent) ShowNotification(new GUIContent($"已载入 {_points.Count} 个路径点"));
        }

        // ==================================================================
        // 场景查询
        // ==================================================================

        string[] SceneCharacterIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage == null) return new string[0];
            var ids = new List<string>();
            foreach (var c in stage.characters)
                if (c != null && !string.IsNullOrEmpty(c.id)) ids.Add(c.id);
            return ids.ToArray();
        }

        /// <summary>
        /// 画布底图，三级回退：
        ///   ① 工具栏手动指定的背景（分支里推算不准时的兜底）
        ///   ② 绑定行推算出的背景 / CG（默认，切行自动跟着换）
        ///   ③ 场景里 VNStage 当前挂着的那张（没绑定剧本时的老行为）
        /// </summary>
        Sprite CanvasBackdrop()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();

            if (!string.IsNullOrEmpty(_bgOverrideId) && stage != null)
                foreach (var b in stage.backgrounds)
                    if (b != null && b.id == _bgOverrideId) return b.sprite;

            var info = RowStage;
            if (info != null && info.backdrop != null) return info.backdrop;

            if (stage != null && stage.backgroundImage != null && stage.backgroundImage.sprite != null)
                return stage.backgroundImage.sprite;
            return null;
        }

        string[] SceneBackgroundIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            if (stage == null) return new string[0];
            var ids = new List<string>();
            foreach (var b in stage.backgrounds)
                if (b != null && !string.IsNullOrEmpty(b.id)) ids.Add(b.id);
            return ids.ToArray();
        }
    }

    /// <summary>
    /// 镜头预设库：以 camseq 文本形式保存常用运镜（存/取都走文本双向通道，
    /// 与手写剧本 100% 一致）。资产：Assets/VNEffects/CamseqPresets.asset。
    /// </summary>
    public class VNCamseqPresetLibrary : ScriptableObject
    {
        [System.Serializable]
        public class Preset
        {
            public string name;
            [TextArea(3, 10)] public string camseqText;
        }

        public List<Preset> presets = new List<Preset>();
    }
}
