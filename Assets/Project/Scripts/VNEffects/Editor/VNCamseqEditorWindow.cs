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

        // ---- 路径点标识 ----
        // 取景框长得都一样，光看框根本答不出「从哪个开始、到哪个结束」。
        // 颜色（起点绿→中间蓝→终点红）+ 序号牌 + 流向箭头三件套一起上才认得出。
        static readonly Color PointStartColor = new Color(0.35f, 0.85f, 0.45f);
        static readonly Color PointMidColor = new Color(0.35f, 0.72f, 1f);
        static readonly Color PointEndColor = new Color(1f, 0.42f, 0.38f);
        static readonly Color PointSelectedColor = new Color(1f, 0.85f, 0.2f);
        const float FaintAlpha = 0.16f;   // 非选中取景框：淡到不抢视线，定位靠序号牌
        const float StayInset = 5f;       // stay 框每叠一个就往里缩这么多 GUI 像素

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

        // 各路径点序号牌这一帧占的位置：既用于互相避让，也让点牌子 = 选中该点
        // （框重叠时中心点根本点不准，牌子是唯一稳定的命中区）
        readonly List<Rect> _badgeRects = new List<Rect>();

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

        // ---- 画布尺寸（底边分隔条可拖，高度存 EditorPrefs）----
        const string CanvasHeightPrefKey = "VNCamseq.CanvasHeight";
        const float CanvasMinHeight = 110f;
        const float CanvasMaxHeight = 1600f;
        const float SplitterHeight = 6f;

        // 0 = 自动（按窗口宽度的 16:9，也就是老行为）；>0 = 手动拖出来的高度
        float _canvasHeight;
        bool _draggingCanvasSplit;
        float _splitStartY, _splitStartHeight;

        // ---- 左右分栏（左＝画布/时间轴，右＝路径点列表）----
        const string ColumnWidthPrefKey = "VNCamseq.LeftColumnWidth";
        const float SplitLayoutMinWidth = 490f;   // 窄于此宽度自动退回上下布局
        const float LeftColumnMin = 320f;
        // 左栏在画布下面还有：高度分隔条 + 进度条 + 时间轴，竖条高度要把它们算进去
        const float LeftColumnExtra = SplitterHeight + 2f + 20f + RulerHeight + TrackHeight + 5f;
        const float RightColumnMin = 380f;        // 单行路径点排下来大约要这么宽

        float _leftColumnWidth;                   // 0 = 默认（可用宽度的 58%）
        bool _draggingColumnSplit;
        float _splitStartX, _splitStartColumnWidth;

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

        // ---- 时间轴轨道 ----
        const float RulerHeight = 14f;    // 顶部刻度条：点/拖 = 移播放头
        const float TrackHeight = 30f;    // 段块区：点 = 选中路径点，拖右边界 = 改时长
        const float MinSegWidth = 14f;    // 每段保底宽度（0 秒瞬切段也要点得中）
        const float EdgeGrab = 5f;        // 段右边界的命中半径
        const float SnapStep = 0.1f;      // 拖动吸附步长（按住 Ctrl 自由）

        // 拖动中的状态。**像素/秒必须在按下时冻结**：总时长一变整条轨道就重新铺满，
        // 边界会从鼠标底下跑掉，越拖越对不上
        int _dragSegIndex = -1;
        SegKind _dragSegKind;
        int _dragSegPoint = -1;
        float _dragPxPerSec, _dragStartValue, _dragStartMouseX;
        bool _draggingRuler;

        VNCamseqPresetLibrary _library;
        // 底部「文本与说明」折叠（生成文本 / 解析文本 / 使用说明三块合一）
        const string TextFoldPrefKey = "VNCamseq.TextFold";
        bool _textFold;

        public const string LibraryPath = "Assets/VNEffects/CamseqPresets.asset";

        [MenuItem("Tools/VN Effects/Camera Sequence Editor")]
        static void Open()
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");
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

        /// <summary>
        /// 绑定状态条：只留「哪一行 + 有没有未应用改动」，其余全部收进 ⚙ 菜单。
        /// 原来 7 个控件平铺在一条 21px 的 toolbar 上，而其中
        /// 「从剧本重载 / 底图 / 立绘」都是一天点一次的东西，天天占着视线。
        /// </summary>
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
                    DrawSettingsButton();   // 没绑定剧本时也要能改底图/立绘
                    return;
                }

                // 未应用改动没有独立按钮了（应用在 ⚙ 菜单里），所以状态文字必须够显眼：
                // 橙色 + 加粗 + 「（未应用）」三重提示，漏看就等于改动没进剧本
                bool dirty = LinkDirty;
                var prevContent = GUI.contentColor;
                if (dirty) GUI.contentColor = new Color(1f, 0.75f, 0.25f);
                if (GUILayout.Button(
                        new GUIContent($"◆ {_linkedEditor.ScenarioDisplayName} 第 {_linkedRow + 1} 行"
                                       + (dirty ? "（未应用）" : "")
                                       + (_linkLocked ? "  [已锁定]" : ""),
                            "点一下把剧本编辑器里的这一行滚到眼前"),
                        dirty ? EditorStyles.boldLabel : EditorStyles.label,
                        GUILayout.Width(280f)))
                    _linkedEditor.FocusRow(_linkedRow);
                GUI.contentColor = prevContent;

                GUILayout.FlexibleSpace();
                DrawSettingsButton();
            }
        }

        void DrawSettingsButton()
        {
            var r = GUILayoutUtility.GetRect(new GUIContent("⚙ 设置 ▾"),
                EditorStyles.toolbarButton, GUILayout.Width(70f));
            if (GUI.Button(r, new GUIContent("⚙ 设置 ▾",
                    "绑定方式（跟随/实时回写/应用/重载）与画布底图、立绘显示"),
                    EditorStyles.toolbarButton))
                ShowSettingsMenu(r);
        }

        /// <summary>绑定与画布设置：全部低频操作收在这里，勾选态即当前状态</summary>
        void ShowSettingsMenu(Rect rect)
        {
            var menu = new GenericMenu();

            if (HasLink)
            {
                menu.AddItem(new GUIContent("跟随剧本选中行"), !_linkLocked, () =>
                {
                    _linkLocked = !_linkLocked;
                    Repaint();
                });
                menu.AddItem(new GUIContent("实时回写剧本"), _liveApply, () =>
                {
                    _liveApply = !_liveApply;
                    if (_liveApply) ApplyToLink();
                    Repaint();
                });
                menu.AddSeparator("");
                if (LinkDirty)
                    menu.AddItem(new GUIContent("应用回剧本"), false, () =>
                    {
                        ApplyToLink();
                        Repaint();
                    });
                else
                    menu.AddDisabledItem(new GUIContent("应用回剧本（没有未应用的改动）"));
                menu.AddItem(new GUIContent("从剧本重载（丢弃这里的改动）"), false, () =>
                {
                    LoadFromLink();
                    _linkLocked = false;
                    Repaint();
                });
                menu.AddSeparator("");
            }

            // GenericMenu 把 '/' 当子菜单分隔符，素材 id 里真带斜杠会被切成两级
            string follow = _rowStage != null && _rowStage.backdrop != null
                ? $"底图/跟随剧本（{MenuSafe(_rowStage.cgId ?? _rowStage.bgId)}）"
                : "底图/跟随剧本";
            menu.AddItem(new GUIContent(follow), string.IsNullOrEmpty(_bgOverrideId), () =>
            {
                _bgOverrideId = "";
                Repaint();
            });
            foreach (var id in BackgroundIds())
            {
                string picked = id;
                menu.AddItem(new GUIContent("底图/" + MenuSafe(id)), _bgOverrideId == picked, () =>
                {
                    _bgOverrideId = picked;
                    Repaint();
                });
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("显示立绘"), _showPortraits, () =>
            {
                _showPortraits = !_showPortraits;
                Repaint();
            });
            menu.DropDown(rect);
        }

        static string MenuSafe(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace('/', '_');

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
            // minSize 必须在这里设，不能只写在 Open()/OpenLinked() 里：
            // 它是窗口自己序列化的属性，已经开着的窗口不会因为那两行改动而更新，
            // 结果就是「代码里写了 360，窗口还是拖不到 720 以下」
            minSize = new Vector2(460f, 300f);

            _list = new ReorderableList(_points, typeof(Waypoint), true, true, true, true)
            {
                drawHeaderCallback = r => GUI.Label(r,
                    "路径点（拖手柄排序 | 时长 0 = 瞬切 | ⋯ = 缓动 / 叠化 / 停留 / 震屏）"),
                elementHeightCallback = _ => EditorGUIUtility.singleLineHeight + 6f,
                drawElementCallback = DrawElement,
                onAddCallback = l => _points.Add(new Waypoint()),
            };
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);

            // 纯外观偏好 → EditorPrefs（关窗重开、换项目窗口都还在）
            _guides = (Guides)EditorPrefs.GetInt(GuidesPrefKey, (int)DefaultGuides);
            _canvasHeight = EditorPrefs.GetFloat(CanvasHeightPrefKey, 0f);
            _leftColumnWidth = EditorPrefs.GetFloat(ColumnWidthPrefKey, 0f);
            _textFold = EditorPrefs.GetBool(TextFoldPrefKey, false);
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

            // 左看右改：左栏画布/进度条/时间轴，右栏路径点列表。
            // 窗口窄到右栏排不下时自动退回上下布局（挤成一团比滚动更难用）
            float avail = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 22f);
            if (avail >= SplitLayoutMinWidth)
            {
                float left = LeftColumnWidth(avail);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUILayout.VerticalScope(GUILayout.Width(left)))
                        DrawViewColumn(left);
                    DrawColumnSplitter(avail, CanvasPixelHeight(left) + LeftColumnExtra);
                    using (new EditorGUILayout.VerticalScope())
                        DrawEditColumn();
                }
            }
            else
            {
                DrawViewColumn(avail);
                GUILayout.Space(6f);
                DrawEditColumn();
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

            GUILayout.Space(4f);
            DrawTextSection();

            EditorGUILayout.EndScrollView();

            // 实时回写：放在最后，确保这一帧的所有编辑都已落到 _points 上
            if (_liveApply && HasLink) ApplyToLink();
        }

        /// <summary>
        /// 底部「文本与说明」：生成文本 / 解析文本 / 使用说明三块折进一个折叠。
        /// 三个多行文本框常年占掉半个窗口，而它们都是偶尔用一次的东西。
        /// 「生成文本 → 剪贴板」放在标题行右边，**收起状态也点得到**（最常用的出口）。
        /// </summary>
        void DrawTextSection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool fold = EditorGUILayout.Foldout(_textFold,
                    "文本与说明（生成 / 解析 / 使用说明）", true);
                if (fold != _textFold)
                {
                    _textFold = fold;
                    EditorPrefs.SetBool(TextFoldPrefKey, fold);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("生成文本 → 剪贴板",
                        "按当前编排生成 camseq 文本并复制到剪贴板（绑定了剧本行时不用它，改动会自动回写）"),
                        EditorStyles.miniButton, GUILayout.Width(126f)))
                {
                    _generatedText = GenerateText();
                    EditorGUIUtility.systemCopyBuffer = _generatedText;
                    ShowNotification(new GUIContent("已复制到剪贴板"));
                }
            }
            if (!_textFold) return;

            GUILayout.Label("生成的剧本文本（粘贴进 .vn.txt）：", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.IsNullOrEmpty(_generatedText)
                ? "（点右上「生成文本 → 剪贴板」）" : _generatedText, GUILayout.MinHeight(70f));

            GUILayout.Space(6f);
            GUILayout.Label("解析已有 camseq 文本（粘贴后点「解析载入」）：", EditorStyles.boldLabel);
            _pasteText = EditorGUILayout.TextArea(_pasteText, GUILayout.MinHeight(60f));
            if (GUILayout.Button("解析载入"))
            {
                CommitUndo();
                ParseText();
            }

            EditorGUILayout.HelpBox(
                "画布：点取景框/序号牌 = 选中；**Ctrl + 拖 = 移动点位**（裸拖只选中，" +
                "防止把调好的取景推走）；拖选中框的四角 = 改 zoom。\n" +
                "布局：画布底边可上下拖改大小、左右分栏的竖条可拖改栏宽，双击任一条复位；" +
                "窗口拉窄到 900px 以下自动退回上下布局。\n" +
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
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(_playing ? "■ 停止" : "▶ 预览",
                        EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    _playing = !_playing;
                    if (_playing)
                    {
                        if (_scrub >= TotalDuration() - 0.001f) _scrub = 0f;
                        _lastUpdateTime = EditorApplication.timeSinceStartup;
                    }
                }

                using (new EditorGUI.DisabledScope(!CanUndo))
                    if (GUILayout.Button(new GUIContent("↶", "撤销（Ctrl+Z，只作用于本窗口）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformUndo();
                using (new EditorGUI.DisabledScope(!CanRedo))
                    if (GUILayout.Button(new GUIContent("↷", "重做（Ctrl+Y / Ctrl+Shift+Z）"),
                            EditorStyles.toolbarButton, GUILayout.Width(26f)))
                        PerformRedo();

                GUILayout.Space(8f);

                bool cameraView = GUILayout.Toggle(_cameraView,
                    new GUIContent(_cameraView ? "镜头视角" : "整图",
                        "整图 = 全景 + 取景框（可拖点编辑）\n" +
                        "镜头视角 = 画布直接显示镜头里看到的画面，拖进度条 / ▶ 就是运镜动画（只看不改）"),
                    EditorStyles.toolbarButton, GUILayout.Width(58f));
                if (cameraView != _cameraView) _cameraView = cameraView;

                var guidesRect = GUILayoutUtility.GetRect(
                    new GUIContent("辅助线 ▾"), EditorStyles.toolbarButton, GUILayout.Width(62f));
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
                    EditorStyles.toolbarButton, GUILayout.Width(66f));
                if (newPreview != _scenePreviewing)
                {
                    if (newPreview) StartScenePreview();
                    else StopScenePreview();
                }

                if (GUILayout.Button(new GUIContent("捕获镜头",
                        "把场景里 ZoomRoot 的当前状态反推成一个路径点"),
                        EditorStyles.toolbarButton, GUILayout.Width(66f)))
                    CaptureCurrentCamera();

                var presetRect = GUILayoutUtility.GetRect(
                    new GUIContent("预设 ▾"), EditorStyles.toolbarButton, GUILayout.Width(56f));
                if (GUI.Button(presetRect,
                        new GUIContent("预设 ▾", "载入 / 存为 / 删除镜头预设（资产：" + LibraryPath + "）"),
                        EditorStyles.toolbarButton))
                    ShowPresetMenu(presetRect);

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_points.Count} 个路径点", EditorStyles.miniLabel);
            }
        }


        // 单行布局的固定宽度：窗口拉窄时先压目标区，右边这几格永远在原位
        // （以前是两行 × N 个点，十来个点就把列表撑得比画布还高）
        const float NumWidth = 26f, TypeWidth = 66f, ZoomFieldWidth = 44f,
                    SecFieldWidth = 40f, MoreWidth = 46f;

        void DrawElement(Rect rect, int index, bool active, bool focused)
        {
            var w = _points[index];
            float line = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 3f;
            bool stay = w.type == PointType.Stay;

            // 从右往左钉死固定块：⋯ → 秒 → zoom，剩下的宽度全归目标区
            float right = rect.xMax;

            var moreRect = new Rect(right - MoreWidth, y, MoreWidth, line);
            right -= MoreWidth + 4f;

            var secField = new Rect(right - SecFieldWidth, y, SecFieldWidth, line);
            var secLabel = new Rect(secField.x - 20f, y, 20f, line);
            right = secLabel.x - 4f;

            var zoomField = new Rect(right - ZoomFieldWidth, y, ZoomFieldWidth, line);
            right = zoomField.x;

            // 宽度富裕才补一条 zoom 滑条（窄窗口下它是第一个被牺牲的，数字框始终在）
            float sliderW = Mathf.Clamp(rect.width - 480f, 0f, 90f);
            var zoomSlider = new Rect(right - sliderW - 2f, y, sliderW, line);
            if (sliderW >= 40f) right = zoomSlider.x;

            var zoomLabel = new Rect(right - 34f, y, 34f, line);
            right = zoomLabel.x - 6f;

            // ---- 左半：编号 + 类型 + 目标 ----
            float x = rect.x;
            GUI.Label(new Rect(x, y, NumWidth, line), $"{index + 1}.");
            x += NumWidth;
            w.type = (PointType)EditorGUI.EnumPopup(new Rect(x, y, TypeWidth, line), w.type);
            x += TypeWidth + 4f;

            float remain = Mathf.Max(40f, right - x);
            switch (w.type)
            {
                case PointType.Stay:
                    GUI.Label(new Rect(x, y, remain, line),
                        new GUIContent("沿用上一个点（位置与 zoom 都不变）",
                            "原地：镜头一动不动，专门用来在序列中间插一段震动或停顿。\n" +
                            "画布上按连续第几个 stay 往里缩一圈画虚线框，不与上一个点抢位置。"),
                        EditorStyles.miniLabel);
                    break;

                case PointType.Anchor:
                    w.anchorIndex = EditorGUI.Popup(
                        new Rect(x, y, Mathf.Min(remain, 130f), line), w.anchorIndex, AnchorTokens);
                    break;

                case PointType.Character:
                {
                    // 角色 id 最长，部位/站位是短词：宽裕时按 剩余/62/58 分，
                    // 挤到放不下就退回三等分（宁可都窄一点，也不让站位被压成一个箭头）
                    float partW = 62f, slotW = 58f;
                    float idW = remain - partW - slotW - 8f;
                    if (idW < 70f) { idW = partW = slotW = (remain - 8f) / 3f; }

                    var ids = CharacterIds();
                    var idRect = new Rect(x, y, idW, line);
                    if (ids.Length > 0)
                    {
                        int cur = System.Array.IndexOf(ids, w.charId);
                        int sel = EditorGUI.Popup(idRect, Mathf.Max(0, cur), ids);
                        w.charId = ids[sel];
                    }
                    else
                    {
                        w.charId = EditorGUI.TextField(idRect, w.charId);
                    }
                    w.partIndex = EditorGUI.Popup(
                        new Rect(x + idW + 4f, y, partW, line), w.partIndex, PartTokens);
                    w.slotIndex = EditorGUI.Popup(
                        new Rect(x + idW + partW + 8f, y, slotW, line), w.slotIndex, SlotNames);
                    break;
                }

                case PointType.Coords:
                    w.coords = EditorGUI.Vector2Field(
                        new Rect(x, y, Mathf.Min(remain, 200f), line), GUIContent.none, w.coords);
                    break;
            }

            // ---- 右半：zoom / 秒 / ⋯ ----
            GUI.Label(zoomLabel, new GUIContent("zoom", stay
                ? "原地点没有自己的 zoom，沿用上一个点" : "取景倍率：1 = 全图，越大越推近"));
            if (stay)
            {
                // 禁用占位而不是留空：切换点位类型时右边这几格不会左右横跳
                using (new EditorGUI.DisabledScope(true))
                {
                    if (sliderW >= 40f) EditorGUI.LabelField(zoomSlider, "沿用", EditorStyles.miniLabel);
                    EditorGUI.LabelField(zoomField, "—", EditorStyles.miniLabel);
                }
            }
            else
            {
                // 滑条只覆盖常用区间 0.5~3，数字框不设这个上限（运行时本来就不限）。
                // 但 HorizontalSlider 会把传进去的值 clamp 后原样返回，直接写回就会
                // 把手敲的 4 拉回 3 —— 所以只在真的拖动了滑条时才采纳它的值。
                if (sliderW >= 40f)
                {
                    EditorGUI.BeginChangeCheck();
                    float dragged = GUI.HorizontalSlider(
                        new Rect(zoomSlider.x, zoomSlider.y + 4f, zoomSlider.width, zoomSlider.height),
                        Mathf.Clamp(w.zoom, 0.5f, 3f), 0.5f, 3f);
                    if (EditorGUI.EndChangeCheck()) w.zoom = dragged;
                }
                w.zoom = Mathf.Clamp(EditorGUI.FloatField(zoomField, w.zoom), 0.1f, 10f);
            }

            GUI.Label(secLabel, new GUIContent("秒", "移动到本点的时长；0 = 瞬切"));
            w.duration = Mathf.Max(0f, EditorGUI.FloatField(secField, w.duration));

            DrawMoreButton(moreRect, w, index);
        }

        /// <summary>
        /// 「⋯」按钮：ease / xfade / hold / 震 四项折进弹出面板。
        /// **按钮上必须看得出"设过没有"**——折起来的参数是看不见的，四个点长一样
        /// 就等于把这四项藏没了，而 xfade 与 hold 会直接改变整段运镜的时间轴长度。
        /// 所以设过的项在按钮上留一个字母标记（E/X/H/震），并把按钮染色。
        /// </summary>
        void DrawMoreButton(Rect rect, Waypoint w, int index)
        {
            var tags = new StringBuilder();
            var tip = new StringBuilder("缓动 / 叠化 / 停留 / 震屏（点开编辑）");
            if (w.easeIndex > 0)
            {
                tags.Append('E');
                tip.Append("\nease：").Append(EaseNames[w.easeIndex]);
            }
            if (w.fade > 0.0001f)
            {
                tags.Append('X');
                tip.Append("\nxfade：").Append(w.fade.ToString("0.##")).Append(" 秒（叠化代替平移）");
            }
            if (w.hold > 0.0001f)
            {
                tags.Append('H');
                tip.Append("\nhold：").Append(w.hold.ToString("0.##")).Append(" 秒停留");
            }
            if (!string.IsNullOrEmpty(w.shake))
            {
                tags.Append('震');
                tip.Append("\nshake：").Append(w.shake);
            }

            bool any = tags.Length > 0;
            var prev = GUI.backgroundColor;
            if (any) GUI.backgroundColor = new Color(0.5f, 0.85f, 1f);
            if (GUI.Button(rect, new GUIContent(any ? tags.ToString() : "⋯", tip.ToString()),
                    EditorStyles.miniButton))
                PopupWindow.Show(rect, new WaypointDetailPopup(this, w, index));
            GUI.backgroundColor = prev;
        }

        /// <summary>路径点的次要参数面板（zoom 滑条 + ease / xfade / hold / 震）</summary>
        class WaypointDetailPopup : PopupWindowContent
        {
            readonly VNCamseqEditorWindow _owner;
            readonly Waypoint _wp;
            readonly int _index;

            public WaypointDetailPopup(VNCamseqEditorWindow owner, Waypoint wp, int index)
            {
                _owner = owner;
                _wp = wp;
                _index = index;
            }

            public override Vector2 GetWindowSize() => new Vector2(336f, 172f);

            public override void OnGUI(Rect rect)
            {
                bool stay = _wp.type == PointType.Stay;
                // labelWidth 是全局的，改完必须还原，否则主窗口的字段标签跟着一起变宽
                float prevLabel = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 88f;

                using (new GUILayout.AreaScope(
                    new Rect(8f, 6f, rect.width - 16f, rect.height - 12f)))
                {
                    GUILayout.Label($"第 {_index + 1} 个路径点", EditorStyles.boldLabel);

                    // 同行内滑条：拖了才写回，免得把手敲的超范围 zoom 悄悄拉回 3
                    using (new EditorGUI.DisabledScope(stay))
                    {
                        EditorGUI.BeginChangeCheck();
                        float dragged = EditorGUILayout.Slider(
                            new GUIContent("zoom", "取景倍率：1 = 全图，越大越推近"),
                            Mathf.Clamp(_wp.zoom, 0.5f, 3f), 0.5f, 3f);
                        if (EditorGUI.EndChangeCheck()) _wp.zoom = dragged;
                    }

                    _wp.easeIndex = EditorGUILayout.Popup(
                        new GUIContent("缓动 ease",
                            "留「(默认)」则按组自动分配：单段 InOutSine；" +
                            "多段首 InSine / 中 Linear / 末 OutSine"),
                        _wp.easeIndex, EaseNames);

                    _wp.fade = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("叠化 xfade", "叠化到本点的秒数（>0 时代替平移/瞬切）"),
                        _wp.fade));

                    _wp.hold = Mathf.Max(0f, EditorGUILayout.FloatField(
                        new GUIContent("停留 hold", "到达本点后停留的秒数（0 = 不停，直接走下一段）"),
                        _wp.hold));

                    var shakeRect = EditorGUILayout.GetControlRect();
                    EditorGUI.LabelField(
                        new Rect(shakeRect.x, shakeRect.y, EditorGUIUtility.labelWidth, shakeRect.height),
                        new GUIContent("震屏 shake", VNCamShakeUi.Tooltip));
                    float sx = shakeRect.x + EditorGUIUtility.labelWidth + 2f;
                    _wp.shake = VNCamShakeUi.Draw(
                        new Rect(sx, shakeRect.y, shakeRect.xMax - sx, shakeRect.height), _wp.shake);

                    GUILayout.Space(4f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(new GUIContent("清掉这四项",
                                "把 ease / xfade / hold / 震 还原成默认（zoom 不动）"),
                                EditorStyles.miniButton, GUILayout.Width(92f)))
                        {
                            _wp.easeIndex = 0;
                            _wp.fade = 0f;
                            _wp.hold = 0f;
                            _wp.shake = "";
                            GUI.changed = true;
                        }
                    }
                }

                EditorGUIUtility.labelWidth = prevLabel;

                // 面板是独立窗口：改完要叫主窗口重画，画布取景框、预览总时长
                // 与 OnGUI 末尾的实时回写全都在那边
                if (GUI.changed) _owner.Repaint();
            }
        }


        // ==================================================================
        // 两栏布局
        // ==================================================================

        /// <summary>左栏：画布 + 进度条 + 时间轴（全是「看」的东西，编点位时它们要一直在眼前）</summary>
        void DrawViewColumn(float width)
        {
            DrawCanvasArea(width);
            GUILayout.Space(2f);

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

            DrawTimeline();
        }

        /// <summary>右栏：路径点列表 + 它的按钮行</summary>
        void DrawEditColumn()
        {
            _list.DoLayoutList();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ 瞬切起手（时长0）"))
                    _points.Insert(0, new Waypoint
                        { type = PointType.Anchor, anchorIndex = 2, zoom = 1.8f, duration = 0f });
                if (GUILayout.Button("+ 回原点收尾"))
                    _points.Add(new Waypoint
                        { type = PointType.Anchor, anchorIndex = 4, zoom = 1f, duration = 1f });
                var templateRect = GUILayoutUtility.GetRect(
                    new GUIContent("内置模板 ▾"), GUI.skin.button);
                if (GUI.Button(templateRect, "内置模板 ▾"))
                    ShowTemplateMenu(templateRect);
            }
        }

        float LeftColumnWidth(float avail)
        {
            float wanted = _leftColumnWidth > 0f ? _leftColumnWidth : avail * 0.58f;
            return Mathf.Clamp(wanted, LeftColumnMin,
                Mathf.Max(LeftColumnMin, avail - RightColumnMin - SplitterHeight));
        }

        /// <summary>
        /// 左右栏之间的竖条。高度按左栏内容算死，**绝不能用 ExpandHeight** ——
        /// 那会把整个 HorizontalScope 撑到滚动区的全部高度，
        /// 底下的开场/收尾与折叠区被顶到窗口最下面，中间空出一大片。
        /// </summary>
        void DrawColumnSplitter(float avail, float height)
        {
            var r = GUILayoutUtility.GetRect(SplitterHeight, height,
                GUILayout.Width(SplitterHeight), GUILayout.Height(height));
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(r.center.x - 1f, r.y + r.height * 0.5f - 14f, 2f, 28f),
                new Color(1f, 1f, 1f, _draggingColumnSplit ? 0.75f : 0.32f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            bool inside = r.Contains(e.mousePosition);

            // 双击复位。必须排在 MouseDown 之前，否则第一下就被 Use 掉，clickCount 到不了 2
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && inside)
            {
                _leftColumnWidth = 0f;
                EditorPrefs.SetFloat(ColumnWidthPrefKey, 0f);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                _draggingColumnSplit = true;
                _splitStartX = e.mousePosition.x;
                _splitStartColumnWidth = LeftColumnWidth(avail);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingColumnSplit)
            {
                _leftColumnWidth = Mathf.Clamp(
                    _splitStartColumnWidth + (e.mousePosition.x - _splitStartX),
                    LeftColumnMin, Mathf.Max(LeftColumnMin, avail - RightColumnMin - SplitterHeight));
                e.Use();
                Repaint();
            }
            else if (_draggingColumnSplit &&
                     (e.type == EventType.MouseUp || e.type == EventType.Ignore))
            {
                _draggingColumnSplit = false;
                EditorPrefs.SetFloat(ColumnWidthPrefKey, _leftColumnWidth);
                e.Use();
                Repaint();
            }
        }

        // ==================================================================
        // 迷你画布
        // ==================================================================

        /// <summary>
        /// 画布 + 底边分隔条。画布**严格保持 16:9**（取景框、辅助线、立绘比例全靠它），
        /// 高度由分隔条决定、宽度反算，窗口比画布宽时靠左放。
        /// 高度 0 = 自动（按窗口宽度铺满，也就是加分隔条之前的老行为）。
        /// </summary>
        /// <summary>画布这一刻的像素高度（栏很窄时按宽度反过来限高，否则画布顶出所在栏）</summary>
        float CanvasPixelHeight(float availWidth)
        {
            float viewWidth = Mathf.Max(120f, availWidth);
            float height = _canvasHeight > 0f
                ? Mathf.Clamp(_canvasHeight, CanvasMinHeight, CanvasMaxHeight)
                : viewWidth * 9f / 16f;
            return Mathf.Min(height, viewWidth * 9f / 16f);
        }

        void DrawCanvasArea(float availWidth)
        {
            float height = CanvasPixelHeight(availWidth);

            var area = GUILayoutUtility.GetRect(10f, height + SplitterHeight,
                GUILayout.ExpandWidth(true));
            float width = Mathf.Min(area.width, height * 16f / 9f);

            DrawCanvas(new Rect(area.x, area.y, width, height));
            DrawCanvasSplitter(new Rect(area.x, area.y + height, width, SplitterHeight), height);
        }

        void DrawCanvasSplitter(Rect r, float currentHeight)
        {
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(r.center.x - 14f, r.y + r.height * 0.5f - 1f, 28f, 2f),
                new Color(1f, 1f, 1f, _draggingCanvasSplit ? 0.75f : 0.32f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

            var e = Event.current;
            bool inside = r.Contains(e.mousePosition);

            // 双击复位成自动高度。判断必须排在下面的 MouseDown 之前，
            // 不然第一下按下就把事件 Use 掉了，clickCount 永远到不了 2
            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2 && inside)
            {
                SetCanvasHeight(0f, true);
                e.Use();
                Repaint();
                return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inside)
            {
                _draggingCanvasSplit = true;
                _splitStartY = e.mousePosition.y;
                _splitStartHeight = currentHeight;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _draggingCanvasSplit)
            {
                // 拖动中只改内存值：EditorPrefs 写的是注册表，每帧写既慢又没必要
                SetCanvasHeight(_splitStartHeight + (e.mousePosition.y - _splitStartY), false);
                e.Use();
                Repaint();
            }
            else if (_draggingCanvasSplit &&
                     (e.type == EventType.MouseUp || e.type == EventType.Ignore))
            {
                _draggingCanvasSplit = false;
                EditorPrefs.SetFloat(CanvasHeightPrefKey, _canvasHeight);
                e.Use();
                Repaint();
            }
        }

        void SetCanvasHeight(float height, bool persist)
        {
            _canvasHeight = height <= 0f
                ? 0f : Mathf.Clamp(height, CanvasMinHeight, CanvasMaxHeight);
            if (persist) EditorPrefs.SetFloat(CanvasHeightPrefKey, _canvasHeight);
        }

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

            // 各路径点取景框 + 序号牌 + 流向箭头
            DrawWaypointFrames(rect);

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
        /// <summary>
        /// 画全部路径点：取景框 + 序号牌 + 流向箭头。
        ///
        /// 【为什么分四遍画】所有框长得一样时根本分不清谁是谁，所以：
        /// ① 路径线与箭头垫在最底（被框压住无所谓）；
        /// ② 非选中框压到 alpha 0.16，只当「其他点大概在哪」的参考；
        /// ③ 选中框实线高亮，必须画在淡框之后——同一遍里画会被后面的淡框糊掉边；
        /// ④ 序号牌最后画，永远不透明、永远夹在画布内，它才是认框的唯一可靠线索。
        ///
        /// stay 点与前一个真点的取景框完全重合，同尺寸画只会互相遮住，
        /// 因此按「连续第几个 stay」逐个往里缩一圈并改虚线；序号照常给，
        /// 画布上的编号就不会跳号了（以前 stay 直接跳过，只看得到 1、3）。
        /// </summary>
        void DrawWaypointFrames(Rect rect)
        {
            int count = _points.Count;
            if (count == 0) return;

            var frames = new Rect[count];
            var centers = new Vector2[count];
            var stays = new bool[count];
            int stayRun = 0;

            for (int i = 0; i < count; i++)
            {
                stays[i] = _points[i].type == PointType.Stay;
                stayRun = stays[i] ? stayRun + 1 : 0;

                var st = TargetState(i);             // stay 自动沿用前面最近的真点
                centers[i] = -st.offset / st.zoom;
                frames[i] = FrameGuiRect(rect, centers[i], CanvasHalf / st.zoom);
                if (stays[i]) frames[i] = Inset(frames[i], StayInset * stayRun);
            }

            // ① 路径线（只连真点：stay 原地不动，连出来是个零长度段）
            Vector2? prev = null;
            for (int i = 0; i < count; i++)
            {
                if (stays[i]) continue;
                if (prev.HasValue) DrawFlowLine(rect, prev.Value, centers[i]);
                prev = centers[i];
            }

            // ② 非选中框
            for (int i = 0; i < count; i++)
            {
                if (i == _list.index) continue;
                var c = PointColor(i);
                c.a = FaintAlpha;
                if (stays[i]) DrawDashedRect(frames[i], c, 1f);
                else DrawRectOutline(frames[i], c, 1f);
            }

            // ③ 选中框
            if (_list.index >= 0 && _list.index < count)
            {
                if (stays[_list.index]) DrawDashedRect(frames[_list.index], PointSelectedColor, 2f);
                else DrawRectOutline(frames[_list.index], PointSelectedColor, 2f);
            }

            // ④ 序号牌
            _badgeRects.Clear();
            for (int i = 0; i < count; i++)
                _badgeRects.Add(DrawPointBadge(rect, frames[i], i, stays[i]));
        }

        /// <summary>起点绿 / 终点红 / 中间蓝（只有一个点时按起点算）</summary>
        Color PointColor(int index) =>
            index <= 0 ? PointStartColor
            : index >= _points.Count - 1 ? PointEndColor : PointMidColor;

        static GUIStyle _badgeStyle;

        /// <summary>
        /// 取景框左上角的序号牌。位置**强制夹在画布内**：zoom&lt;1 时取景框比画布还大、
        /// 镜头推到边角时整块框会移出画布，牌子跟着跑出去就等于没有编号可认了。
        /// </summary>
        Rect DrawPointBadge(Rect canvasRect, Rect frame, int index, bool stay)
        {
            if (_badgeStyle == null)
                _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                };

            bool selected = index == _list.index;
            string label = (index + 1).ToString() + (stay ? " 停" : "");
            float w = 12f + label.Length * 7.5f + (stay ? 6f : 0f);
            const float h = 15f;
            var r = new Rect(
                Mathf.Clamp(frame.x + 3f, canvasRect.x + 2f, canvasRect.xMax - w - 2f),
                Mathf.Clamp(frame.y + 3f, canvasRect.y + 2f, canvasRect.yMax - h - 2f),
                w, h);

            // 避让：zoom 相近的点取景框几乎重合，牌子会叠成一摞谁也看不见。
            // 撞上就往下挪一格，挪到画布底就放弃（宁可叠着也不能跑出画布）
            for (int guard = 0; guard < 8; guard++)
            {
                bool hit = false;
                foreach (var taken in _badgeRects)
                    if (taken.Overlaps(r)) { hit = true; break; }
                if (!hit) break;
                if (r.yMax + h + 3f > canvasRect.yMax) break;
                r.y += h + 3f;
            }

            // 投影：亮背景（雪景/白墙）上没有它牌子会糊进去
            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, r.width, r.height),
                new Color(0f, 0f, 0f, 0.45f));
            EditorGUI.DrawRect(r, selected ? PointSelectedColor : PointColor(index));
            if (selected) DrawRectOutline(r, Color.white, 1f);

            var prevColor = _badgeStyle.normal.textColor;
            _badgeStyle.normal.textColor = selected ? Color.black : Color.white; // 黄底白字会糊
            GUI.Label(r, label, _badgeStyle);
            _badgeStyle.normal.textColor = prevColor;
            return r;
        }

        /// <summary>两点之间的流向：虚线 + 中点箭头（箭头才是「从哪到哪」的答案）</summary>
        void DrawFlowLine(Rect rect, Vector2 fromCanvas, Vector2 toCanvas)
        {
            DrawDottedLine(rect, fromCanvas, toCanvas, new Color(1f, 1f, 1f, 0.4f));
            if (Event.current.type != EventType.Repaint) return;   // Handles 只在重绘时有效

            var a = CanvasToGui(rect, fromCanvas);
            var b = CanvasToGui(rect, toCanvas);
            var dir = b - a;
            if (dir.sqrMagnitude < 100f) return;   // 两点几乎重合：箭头没有方向可言
            dir.Normalize();
            var mid = (a + b) * 0.5f;
            var side = new Vector2(-dir.y, dir.x);

            Handles.BeginGUI();
            Handles.color = new Color(1f, 1f, 1f, 0.85f);
            Handles.DrawAAConvexPolygon(
                (Vector3)(mid + dir * 8f),
                (Vector3)(mid - dir * 4f + side * 5f),
                (Vector3)(mid - dir * 4f - side * 5f));
            Handles.EndGUI();
        }

        /// <summary>虚线矩形（stay 点专用：与上一个点重合，实线画出来分不清是谁）</summary>
        static void DrawDashedRect(Rect r, Color c, float t)
        {
            const float dash = 6f, gap = 4f;
            for (float x = r.x; x < r.xMax; x += dash + gap)
            {
                float w = Mathf.Min(dash, r.xMax - x);
                EditorGUI.DrawRect(new Rect(x, r.y, w, t), c);
                EditorGUI.DrawRect(new Rect(x, r.yMax - t, w, t), c);
            }
            for (float y = r.y; y < r.yMax; y += dash + gap)
            {
                float hh = Mathf.Min(dash, r.yMax - y);
                EditorGUI.DrawRect(new Rect(r.x, y, t, hh), c);
                EditorGUI.DrawRect(new Rect(r.xMax - t, y, t, hh), c);
            }
        }

        static Rect Inset(Rect r, float d) => new Rect(
            r.x + d, r.y + d, Mathf.Max(6f, r.width - d * 2f), Mathf.Max(6f, r.height - d * 2f));

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

                    var def = FindCharDef(c.id, stage);
                    float height = CharacterHeight(stage, def);
                    Vector2 offset = def != null ? def.positionOffset : Vector2.zero;
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

            // 光标是「现在能不能移动」的唯一提示，所以 Ctrl 一按下就得立刻换形状
            if ((e.type == EventType.KeyDown || e.type == EventType.KeyUp) &&
                (e.keyCode == KeyCode.LeftControl || e.keyCode == KeyCode.RightControl))
                Repaint();

            DrawCanvasCursors(rect, e.control);

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

                // 0) 序号牌命中 → 直接选中那个点。排在最前面：框重叠时按中心距离
                //    找最近点会选错人，而牌子是画布上唯一一定不重叠、一定在画布内的把手
                for (int i = 0; i < _badgeRects.Count && i < _points.Count; i++)
                {
                    if (!_badgeRects[i].Contains(e.mousePosition)) continue;
                    _list.index = i;
                    _dragMode = DragMode.None;   // 牌子只用来选，拖动仍走取景框
                    e.Use();
                    Repaint();
                    return;
                }

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
                    // 误触保护：裸拖只选中，**Ctrl + 拖**才移动点位。
                    // 画布上最常做的事是「选中某个点看它的取景」，而按在框上稍微一晃
                    // 就把调好的位置推走了 —— 代价太不对等，所以移动要多按一个键
                    _dragMode = e.control ? DragMode.Center : DragMode.None;
                }
                // 空白处点击不再有任何效果 —— 以前是「点一下就把选中点瞬移过去」，
                // 连拖都不用拖，误伤率最高的一条。移动点位现在只有 Ctrl+拖这一条路
                e.Use();
                Repaint();
            }
            // _dragMode 的判断不能省：拖分隔条时鼠标会扫进画布，这里无条件 Use
            // 就会把分隔条的拖动事件吃掉，分隔条直接卡住不动
            else if (e.type == EventType.MouseDrag && e.button == 0 &&
                     _dragMode != DragMode.None && hasSelection &&
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

        /// <summary>
        /// 画布上的光标提示。移动点位要按住 Ctrl，而这个键唯一的提示就是光标：
        /// 按住 Ctrl 时整块画布变四向移动箭头（松开就恢复，一眼知道当前处于哪种模式）。
        /// 四角的 zoom 热区不挑修饰键，所以一直是斜向拉伸光标，并且**注册在后面**
        /// —— 同一处叠了多个 cursorRect 时，后注册的那个赢。
        /// </summary>
        void DrawCanvasCursors(Rect rect, bool ctrl)
        {
            if (ctrl) EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);

            int sel = _list != null ? _list.index : -1;
            if (sel < 0 || sel >= _points.Count || _points[sel].type == PointType.Stay) return;

            var st = TargetState(sel);
            var center = -st.offset / st.zoom;
            var half = CanvasHalf / st.zoom;
            for (int cx = -1; cx <= 1; cx += 2)
            for (int cy = -1; cy <= 1; cy += 2)
            {
                var g = CanvasToGui(rect, center + new Vector2(half.x * cx, half.y * cy));
                var hot = RectIntersect(new Rect(g.x - 10f, g.y - 10f, 20f, 20f), rect);
                if (hot.width < 2f || hot.height < 2f) continue;   // 角在画布外，别乱设光标
                EditorGUIUtility.AddCursorRect(hot,
                    cx * cy > 0 ? MouseCursor.ResizeUpRight : MouseCursor.ResizeUpLeft);
            }
        }

        static Rect RectIntersect(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.x, b.x), y1 = Mathf.Max(a.y, b.y);
            float x2 = Mathf.Min(a.xMax, b.xMax), y2 = Mathf.Min(a.yMax, b.yMax);
            return new Rect(x1, y1, Mathf.Max(0f, x2 - x1), Mathf.Max(0f, y2 - y1));
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

        /// <summary>预设菜单：已存预设点一下就载入，下面两项管存/删（原来平铺 5 个控件占了半行）</summary>
        void ShowPresetMenu(Rect rect)
        {
            _library = AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(LibraryPath);
            var menu = new GenericMenu();

            int count = _library != null ? _library.presets.Count : 0;
            if (count == 0)
                menu.AddDisabledItem(new GUIContent("（还没有预设）"));
            else
                for (int i = 0; i < count; i++)
                {
                    int index = i;
                    menu.AddItem(new GUIContent(MenuSafe(_library.presets[i].name)), false,
                        () => LoadPresetAt(index));
                }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("存为预设…"), false, () =>
            {
                if (_points.Count == 0)
                {
                    ShowNotification(new GUIContent("没有路径点可保存"));
                    return;
                }
                // 名字走独立小窗口：GenericMenu 的回调不在 OnGUI 里，
                // 这里调 PopupWindow.Show 会报 "GUI functions outside OnGUI"
                VNCamseqNamePopup.Open($"预设{System.DateTime.Now:HHmmss}", SavePresetNamed);
            });
            for (int i = 0; i < count; i++)
            {
                int index = i;
                menu.AddItem(new GUIContent("删除/" + MenuSafe(_library.presets[i].name)), false,
                    () => DeletePresetAt(index));
            }

            menu.DropDown(rect);
        }

        void SavePresetNamed(string name)
        {
            name = string.IsNullOrEmpty(name) ? "" : name.Trim();
            if (name.Length == 0 || _points.Count == 0) return;
            SavePreset(name, GenerateText());
            _library = EnsureLibrary();
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
            Repaint();
        }

        /// <summary>内置运镜模板菜单：套用 = 整段替换当前编排（角色占位按场景里的第一个角色填）</summary>
        void ShowTemplateMenu(Rect rect)
        {
            var ids = CharacterIds();
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

        void LoadPresetAt(int index)
        {
            if (_library == null || index < 0 || index >= _library.presets.Count) return;
            CommitUndo();          // 整段替换，撤销时要能一步回到载入前
            _pasteText = _library.presets[index].camseqText;
            ParseText();
            Repaint();
        }

        void DeletePresetAt(int index)
        {
            if (_library == null || index < 0 || index >= _library.presets.Count) return;
            string name = _library.presets[index].name;
            if (!EditorUtility.DisplayDialog("删除预设", $"删除「{name}」？", "删除", "取消")) return;
            _library.presets.RemoveAt(index);
            EditorUtility.SetDirty(_library);
            AssetDatabase.SaveAssets();
            Repaint();
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
        // 时间轴轨道
        // ==================================================================

        /// <summary>
        /// 把 BuildSegments() 的结果画成一排色块：段宽 = 时长，颜色 = 段类型。
        ///
        /// 上层 14px 是刻度条（点/拖 = 移播放头），下层是段块（点 = 选中路径点、
        /// 拖右边界 = 改时长）。两层分开是为了让「移播放头」和「改时长」永不打架 ——
        /// 同一块区域既要 scrub 又要拖边界的话，每一次点击都得猜用户想干嘛。
        /// </summary>
        void DrawTimeline()
        {
            var segs = BuildSegments();
            var area = GUILayoutUtility.GetRect(10f, RulerHeight + TrackHeight + 3f,
                GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.15f));

            if (segs.Count == 0)
            {
                GUI.Label(new Rect(area.x + 6f, area.y + 6f, area.width, 18f),
                    "还没有路径点 —— 在画布上点一下，或用「内置模板 ▾」起个头",
                    EditorStyles.miniLabel);
                return;
            }

            var ruler = new Rect(area.x, area.y, area.width, RulerHeight);
            var track = new Rect(area.x, area.y + RulerHeight + 1f, area.width, TrackHeight);
            var widths = SegmentWidths(segs, track.width);

            float x = track.x;
            for (int i = 0; i < segs.Count && x < track.xMax; i++)
            {
                float w = Mathf.Min(widths[i], track.xMax - x);
                DrawSegmentBlock(new Rect(x, track.y, w, track.height), segs[i], widths[i]);
                x += widths[i];
            }

            DrawTimelineRuler(ruler, segs, widths);

            // 播放头：贯穿两层，顶端一个小三角（细线在色块上容易看丢）
            float headX = Mathf.Clamp(TimeToX(segs, widths, track.x, _scrub),
                area.x, area.xMax - 1f);
            EditorGUI.DrawRect(new Rect(headX, area.y, 1f, area.height), Color.white);
            EditorGUI.DrawRect(new Rect(headX - 3f, area.y, 7f, 3f), Color.white);

            HandleTimelineInput(ruler, track, segs, widths);
        }

        /// <summary>
        /// 每段占的像素宽。**保底 MinSegWidth**：0 秒瞬切段（camseq 起手最常见的写法）
        /// 严格按时长算就是 0 像素，既看不见也点不中。
        /// 拖动中改用按下瞬间冻结的像素/秒，不再按新的总时长重新铺满。
        /// </summary>
        float[] SegmentWidths(List<Segment> segs, float avail)
        {
            int n = segs.Count;
            var widths = new float[n];
            if (n == 0) return widths;

            if (_dragSegIndex >= 0 && _dragPxPerSec > 0.01f)
            {
                // 公式必须与下面的铺满分支**逐项等价**（保底宽 + 时长×像素每秒），
                // 否则按下鼠标的那一帧整条轨道会整体跳一下 —— 冻结比例就是为了不跳
                for (int i = 0; i < n; i++)
                    widths[i] = MinSegWidth + Mathf.Max(0f, segs[i].duration) * _dragPxPerSec;
                return widths;
            }

            float minTotal = n * MinSegWidth;
            if (avail <= minTotal)
            {
                for (int i = 0; i < n; i++) widths[i] = avail / n;
                return widths;
            }

            float total = 0f;
            foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
            float extra = avail - minTotal;
            for (int i = 0; i < n; i++)
                widths[i] = MinSegWidth + (total > 0.0001f
                    ? Mathf.Max(0f, segs[i].duration) / total * extra
                    : extra / n);
            return widths;
        }

        static Color SegColor(Segment s)
        {
            switch (s.kind)
            {
                case SegKind.Hold: return new Color(0.82f, 0.58f, 0.22f);   // 停留 = 橙
                case SegKind.Xfade: return new Color(0.55f, 0.42f, 0.80f);  // 叠化 = 紫
                case SegKind.StartFade:
                case SegKind.EndFade: return new Color(0.40f, 0.44f, 0.52f); // 开场/收尾 = 灰蓝
                default:
                    return s.duration <= 0.001f
                        ? new Color(0.52f, 0.55f, 0.60f)                     // 瞬切 = 灰
                        : new Color(0.24f, 0.50f, 0.78f);                    // 补间 = 蓝
            }
        }

        void DrawSegmentBlock(Rect r, Segment s, float fullWidth)
        {
            bool selected = s.pointIndex >= 0 && s.pointIndex == _list.index;
            var color = SegColor(s);
            if (selected) color = Color.Lerp(color, Color.white, 0.28f);

            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y, Mathf.Max(1f, r.width - 2f), r.height), color);

            // 左边缘 3px 用画布序号牌的颜色（起绿 / 中蓝 / 终红）：
            // 时间轴和画布是两个视图，靠这条色边才对得上「这一段是哪个框」
            if (s.pointIndex >= 0 && r.width > 4f)
                EditorGUI.DrawRect(new Rect(r.x + 1f, r.y, 3f, r.height), PointColor(s.pointIndex));
            if (selected) DrawRectOutline(r, Color.white, 1f);

            string label = SegLabel(s, fullWidth);
            if (label.Length > 0)
                GUI.Label(new Rect(r.x + 5f, r.y + 6f, Mathf.Max(10f, r.width - 6f), 16f),
                    label, EditorStyles.whiteMiniLabel);
        }

        /// <summary>段块上的字：宽度不够就一层层砍（全文 → 只留头 → 什么都不画）</summary>
        string SegLabel(Segment s, float width)
        {
            string head;
            switch (s.kind)
            {
                case SegKind.StartFade: head = "开场叠化"; break;
                case SegKind.EndFade: head = "收尾叠化"; break;
                case SegKind.Xfade: head = $"{s.pointIndex + 1} 叠"; break;
                case SegKind.Hold:
                    // 停顿 = max(hold, 震动时长)。被震撑起来的那种要标出来，
                    // 否则会以为是自己写的 hold，怎么拖都拖不短
                    var w = s.pointIndex >= 0 && s.pointIndex < _points.Count
                        ? _points[s.pointIndex] : null;
                    bool byShake = w != null && VNShakeSpec.TryParse(w.shake, out VNShakeSpec sp)
                                   && sp.Valid && sp.duration >= w.hold - 0.001f;
                    head = $"{s.pointIndex + 1} {(byShake ? "震" : "停")}";
                    break;
                default:
                    head = s.duration <= 0.001f
                        ? $"{s.pointIndex + 1} ◆" : $"{s.pointIndex + 1}";
                    break;
            }
            if (width > 62f) return $"{head} {s.duration:0.##}s";
            if (width > 26f) return head;
            return "";
        }

        void DrawTimelineRuler(Rect ruler, List<Segment> segs, float[] widths)
        {
            EditorGUI.DrawRect(ruler, new Color(0.18f, 0.18f, 0.21f));

            float total = 0f;
            foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
            if (total <= 0.001f) return;

            // 段有保底宽度，所以刻度间距天生不均匀 —— 这是对的，它反映的是
            // 「屏幕上这一段占多宽」，与段块严格对齐才不会看串
            float step = total <= 3f ? 0.5f : total <= 10f ? 1f : 2f;
            var tickColor = new Color(1f, 1f, 1f, 0.25f);
            for (float t = 0f; t <= total + 0.0001f; t += step)
            {
                float tx = TimeToX(segs, widths, ruler.x, t);
                if (tx > ruler.xMax) break;
                EditorGUI.DrawRect(new Rect(tx, ruler.y + 7f, 1f, 7f), tickColor);
                GUI.Label(new Rect(tx + 2f, ruler.y - 2f, 40f, 16f),
                    $"{t:0.#}s", EditorStyles.miniLabel);
            }
        }

        /// <summary>时间 → 轨道上的 x（按段逐个换算，与保底宽度一致）</summary>
        static float TimeToX(List<Segment> segs, float[] widths, float trackX, float time)
        {
            float x = trackX, t = Mathf.Max(0f, time);
            for (int i = 0; i < segs.Count; i++)
            {
                float d = Mathf.Max(0f, segs[i].duration);
                if (d > 0.0001f && t < d) return x + widths[i] * (t / d);
                t -= d;
                x += widths[i];
            }
            return x;
        }

        static float XToTime(List<Segment> segs, float[] widths, float trackX, float x)
        {
            float cursor = trackX, t = 0f;
            for (int i = 0; i < segs.Count; i++)
            {
                float d = Mathf.Max(0f, segs[i].duration);
                if (x < cursor + widths[i])
                    return t + (widths[i] > 0.01f ? (x - cursor) / widths[i] * d : 0f);
                t += d;
                cursor += widths[i];
            }
            return t;
        }

        // ==================================================================
        // 时间轴交互（拖时长 / 移播放头 / 右键菜单）
        // ==================================================================

        /// <summary>这一段的时长是不是可以拖的（每种段拖的是不同字段）</summary>
        static bool CanDragSegment(Segment s)
        {
            if (s.kind == SegKind.StartFade || s.kind == SegKind.EndFade) return true;
            return s.pointIndex >= 0;
        }

        float DragValueOf(Segment s)
        {
            switch (s.kind)
            {
                case SegKind.StartFade: return _startFade;
                case SegKind.EndFade: return _endFadeDur;
            }
            if (s.pointIndex < 0 || s.pointIndex >= _points.Count) return 0f;
            var w = _points[s.pointIndex];
            return s.kind == SegKind.Xfade ? w.fade
                : s.kind == SegKind.Hold ? w.hold : w.duration;
        }

        void HandleTimelineInput(Rect ruler, Rect track, List<Segment> segs, float[] widths)
        {
            var e = Event.current;

            // ---- 拖动中：不管鼠标跑到哪都要接着响应，直到松手 ----
            if (_dragSegIndex >= 0 || _draggingRuler)
            {
                if (e.type == EventType.MouseDrag)
                {
                    if (_draggingRuler)
                        _scrub = Mathf.Max(0f, XToTime(segs, widths, track.x, e.mousePosition.x));
                    else
                        ApplyDragValue(_dragStartValue
                            + (e.mousePosition.x - _dragStartMouseX) / _dragPxPerSec);
                    e.Use();
                    Repaint();
                    return;
                }
                if (e.type == EventType.MouseUp || e.type == EventType.Ignore)
                {
                    _dragSegIndex = -1;
                    _draggingRuler = false;
                    e.Use();
                    Repaint();
                    return;
                }
            }

            // ---- 边界处的横向拉伸光标（不用点开就知道这里能拖）----
            float x = track.x;
            for (int i = 0; i < segs.Count; i++)
            {
                x += widths[i];
                if (CanDragSegment(segs[i]))
                    EditorGUIUtility.AddCursorRect(
                        new Rect(x - EdgeGrab, track.y, EdgeGrab * 2f, track.height),
                        MouseCursor.ResizeHorizontal);
            }

            if (e.type != EventType.MouseDown) return;

            // ---- 刻度条：点/拖 = 移播放头 ----
            if (ruler.Contains(e.mousePosition) && e.button == 0)
            {
                _scrub = Mathf.Max(0f, XToTime(segs, widths, track.x, e.mousePosition.x));
                _playing = false;
                _draggingRuler = true;
                e.Use();
                Repaint();
                return;
            }

            if (!track.Contains(e.mousePosition)) return;

            // ---- 段块：先看边界（拖时长），再看块body（选中 / 右键菜单）----
            x = track.x;
            for (int i = 0; i < segs.Count; i++)
            {
                float left = x;
                x += widths[i];

                if (e.button == 0 && CanDragSegment(segs[i]) &&
                    Mathf.Abs(e.mousePosition.x - x) <= EdgeGrab)
                {
                    float total = 0f;
                    foreach (var seg in segs) total += Mathf.Max(0f, seg.duration);
                    // 冻结像素/秒：按「除去保底宽度后剩下的像素」算，与铺满时的比例一致
                    float usable = track.width - segs.Count * MinSegWidth;
                    _dragPxPerSec = total > 0.01f && usable > 20f
                        ? usable / total : 120f;
                    _dragSegIndex = i;
                    _dragSegKind = segs[i].kind;
                    _dragSegPoint = segs[i].pointIndex;
                    _dragStartValue = DragValueOf(segs[i]);
                    _dragStartMouseX = e.mousePosition.x;
                    if (segs[i].pointIndex >= 0) _list.index = segs[i].pointIndex;
                    e.Use();
                    Repaint();
                    return;
                }

                if (e.mousePosition.x < left || e.mousePosition.x >= x) continue;

                if (e.button == 1)
                {
                    ShowSegmentMenu(segs[i]);
                    e.Use();
                    return;
                }
                if (e.button == 0)
                {
                    // 只选中，不动播放头 —— 播放头归刻度条管
                    if (segs[i].pointIndex >= 0) _list.index = segs[i].pointIndex;
                    e.Use();
                    Repaint();
                    return;
                }
            }
        }

        /// <summary>把拖出来的秒数写回对应字段（吸附 0.1 秒，按住 Ctrl 自由）</summary>
        void ApplyDragValue(float value)
        {
            value = Mathf.Max(0f, value);
            if (!Event.current.control) value = Mathf.Round(value / SnapStep) * SnapStep;
            value = Mathf.Round(value * 1000f) / 1000f;

            switch (_dragSegKind)
            {
                case SegKind.StartFade:
                    _startFade = Mathf.Max(0.05f, value);
                    return;
                case SegKind.EndFade:
                    _endFadeDur = Mathf.Max(0.05f, value);
                    return;
            }

            if (_dragSegPoint < 0 || _dragSegPoint >= _points.Count) return;
            var w = _points[_dragSegPoint];
            switch (_dragSegKind)
            {
                // xfade 拖到 0 会让这一段从「叠化」变回「补间」，段的身份在拖动途中
                // 突变很难受，所以留一个 0.05 的地板；要取消叠化走右键菜单
                case SegKind.Xfade: w.fade = Mathf.Max(0.05f, value); break;
                case SegKind.Hold: w.hold = value; break;
                default: w.duration = value; break;
            }
        }

        void ShowSegmentMenu(Segment s)
        {
            var menu = new GenericMenu();

            if (s.kind == SegKind.StartFade)
            {
                menu.AddItem(new GUIContent("关掉开场叠化"), false, () =>
                {
                    _startMode = StartMode.None;
                    Repaint();
                });
                menu.ShowAsContext();
                return;
            }
            if (s.kind == SegKind.EndFade)
            {
                menu.AddItem(new GUIContent("关掉收尾叠化"), false, () =>
                {
                    _endFade = false;
                    Repaint();
                });
                menu.ShowAsContext();
                return;
            }

            int pi = s.pointIndex;
            if (pi < 0 || pi >= _points.Count) return;
            var w = _points[pi];

            foreach (float v in new[] { 0f, 0.3f, 0.5f, 0.8f, 1.2f, 2f })
            {
                float value = v;
                menu.AddItem(new GUIContent($"时长/{(value <= 0f ? "0（瞬切）" : value + " 秒")}"),
                    Mathf.Approximately(w.duration, value), () =>
                    {
                        w.duration = value;
                        Repaint();
                    });
            }
            menu.AddSeparator("");

            if (w.hold > 0.0001f)
                menu.AddItem(new GUIContent("清掉停留 hold"), false, () =>
                {
                    w.hold = 0f;
                    Repaint();
                });
            if (!string.IsNullOrEmpty(w.shake))
                menu.AddItem(new GUIContent("清掉震屏 shake"), false, () =>
                {
                    w.shake = "";
                    Repaint();
                });
            if (w.fade > 0.0001f)
                menu.AddItem(new GUIContent("清掉叠化 xfade"), false, () =>
                {
                    w.fade = 0f;
                    Repaint();
                });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("在后面插入一个同位置的点"), false, () =>
            {
                var copy = CloneWaypoint(w);
                copy.hold = 0f;
                copy.fade = 0f;
                copy.shake = "";          // 副参数不复制：连震两下几乎都是误操作
                _points.Insert(pi + 1, copy);
                _list.index = pi + 1;
                Repaint();
            });
            menu.AddItem(new GUIContent("删除这个路径点"), false, () =>
            {
                _points.RemoveAt(pi);
                _list.index = Mathf.Clamp(_list.index, -1, _points.Count - 1);
                Repaint();
            });

            menu.ShowAsContext();
        }

        static Waypoint CloneWaypoint(Waypoint src) => new Waypoint
        {
            type = src.type,
            anchorIndex = src.anchorIndex,
            charId = src.charId,
            partIndex = src.partIndex,
            slotIndex = src.slotIndex,
            coords = src.coords,
            zoom = src.zoom,
            duration = src.duration,
            easeIndex = src.easeIndex,
            fade = src.fade,
            hold = src.hold,
            shake = src.shake,
        };

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
                    var stage = Object.FindFirstObjectByType<VNStage>();
                    var def = FindCharDef(w.charId, stage);
                    float height = CharacterHeight(stage, def);
                    Vector2 offset = def != null ? def.positionOffset : Vector2.zero;
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

        /// <summary>时间轴上一段的来源（轨道按它上色，拖动时决定改哪个字段）</summary>
        enum SegKind { Move, Xfade, Hold, StartFade, EndFade }

        /// <summary>预览时间轴上的一段：补间段沿缓动移动；叠化段镜头瞬切、旧画面淡出</summary>
        struct Segment
        {
            public CamState target;
            public float duration;
            public SegKind kind;
            public int pointIndex;   // 来源路径点下标；收尾叠化 = -1
            public Ease ease;        // 补间段缓动（叠化 / hold 段无意义）

            public bool isFade => kind == SegKind.Xfade
                                  || kind == SegKind.StartFade || kind == SegKind.EndFade;
            public bool isHold => kind == SegKind.Hold;
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

            int start = 0;
            if (_points.Count > 0 && _startMode == StartMode.Fade)
            {
                segs.Add(new Segment
                {
                    target = TargetState(0),
                    duration = Mathf.Max(0.05f, _startFade),
                    kind = SegKind.StartFade,
                    pointIndex = 0,
                });
                AddHoldSegment(segs, 0);
                start = 1;
            }
            for (int i = start; i < _points.Count; i++)
            {
                var w = _points[i];
                if (w.fade > 0.001f)
                    segs.Add(new Segment
                    {
                        target = TargetState(i), duration = w.fade,
                        kind = SegKind.Xfade, pointIndex = i,
                    });
                else
                    segs.Add(new Segment
                    {
                        target = TargetState(i), duration = Mathf.Max(0f, w.duration),
                        kind = SegKind.Move, pointIndex = i,
                    });
                AddHoldSegment(segs, i);
            }
            if (_endFade)
            {
                segs.Add(new Segment
                {
                    target = new CamState { offset = Vector2.zero, zoom = 1f },
                    duration = Mathf.Max(0.05f, _endFadeDur),
                    kind = SegKind.EndFade,
                    pointIndex = -1,
                });
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
                    int pi = s.pointIndex;
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
        void AddHoldSegment(List<Segment> segs, int index)
        {
            var w = _points[index];
            float stall = w.hold;
            if (VNShakeSpec.TryParse(w.shake, out VNShakeSpec spec) && spec.Valid)
                stall = Mathf.Max(stall, spec.duration);
            if (stall <= 0.001f) return;
            segs.Add(new Segment
            {
                target = TargetState(index), duration = stall,
                kind = SegKind.Hold, pointIndex = index,
            });
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

        // ---- 内容库来源：VNGameConfig 优先，留空才回退场景组件 ----
        // 与运行时 VNGameConfig.ApplyList 的覆盖语义一致。编辑期场景里的
        // characters/backgrounds 常年落后于配置资产（配置只在运行时才合并进 VNStage），
        // 只读场景会让配置里登记的角色查不到定义 → 画布退回 fallback 的 880/offset0，
        // sizeScale≠1 的角色（小雪 2.1、亚里沙 1.9）在画布上大小、脚底位置全错。
        static VNGameConfig _configCache;

        static VNGameConfig Config
        {
            get
            {
                if (_configCache == null)
                {
                    var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
                    _configCache = cfg != null
                        ? cfg : Resources.Load<VNGameConfig>(VNGameConfig.ResourcesName);
                }
                return _configCache;
            }
        }

        static List<T> PickLibrary<T>(List<T> fromConfig, List<T> fromScene) =>
            fromConfig != null && fromConfig.Count > 0 ? fromConfig : fromScene;

        /// <summary>角色定义：取错就画错大小（高度 = characterHeight × sizeScale）</summary>
        static VNCharacterDef FindCharDef(string id, VNStage stage)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var list = PickLibrary(Config != null ? Config.characters : null,
                stage != null ? stage.characters : null);
            return list?.Find(c => c != null && c.id == id);
        }

        /// <summary>立绘显示高度（与 VNStage.HeightFor 同一条公式）</summary>
        static float CharacterHeight(VNStage stage, VNCharacterDef def)
        {
            float baseHeight = stage != null ? stage.characterHeight : 880f;
            return baseHeight * (def != null ? Mathf.Max(0.05f, def.sizeScale) : 1f);
        }

        string[] CharacterIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var list = PickLibrary(Config != null ? Config.characters : null,
                stage != null ? stage.characters : null);
            if (list == null) return new string[0];
            var ids = new List<string>();
            foreach (var c in list)
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

            if (!string.IsNullOrEmpty(_bgOverrideId))
            {
                var lib = PickLibrary(Config != null ? Config.backgrounds : null,
                    stage != null ? stage.backgrounds : null);
                if (lib != null)
                    foreach (var b in lib)
                        if (b != null && b.id == _bgOverrideId) return b.sprite;
            }

            var info = RowStage;
            if (info != null && info.backdrop != null) return info.backdrop;

            if (stage != null && stage.backgroundImage != null && stage.backgroundImage.sprite != null)
                return stage.backgroundImage.sprite;
            return null;
        }

        string[] BackgroundIds()
        {
            var stage = Object.FindFirstObjectByType<VNStage>();
            var lib = PickLibrary(Config != null ? Config.backgrounds : null,
                stage != null ? stage.backgrounds : null);
            if (lib == null) return new string[0];
            var ids = new List<string>();
            foreach (var b in lib)
                if (b != null && !string.IsNullOrEmpty(b.id)) ids.Add(b.id);
            return ids.ToArray();
        }
    }

    /// <summary>
    /// 「存为预设…」的名字输入小窗。
    /// 用独立窗口而不是 PopupWindow，是因为它由 GenericMenu 的回调打开 ——
    /// 那个回调不在 OnGUI 里，调 PopupWindow.Show 会报「GUI functions outside OnGUI」。
    /// </summary>
    public class VNCamseqNamePopup : EditorWindow
    {
        string _name = "";
        System.Action<string> _onConfirm;   // 委托不序列化：域重载后直接关掉重开就好

        public static void Open(string initial, System.Action<string> onConfirm)
        {
            var win = CreateInstance<VNCamseqNamePopup>();
            win.titleContent = new GUIContent("存为预设");
            win._name = initial ?? "";
            win._onConfirm = onConfirm;
            win.position = new Rect(
                Screen.currentResolution.width * 0.5f - 160f,
                Screen.currentResolution.height * 0.5f - 40f, 320f, 78f);
            win.ShowUtility();
        }

        void OnGUI()
        {
            if (_onConfirm == null) { Close(); return; }   // 域重载后失效

            GUILayout.Space(8f);
            GUI.SetNextControlName("presetName");
            _name = EditorGUILayout.TextField("预设名", _name);
            GUI.FocusControl("presetName");

            // Enter 直接确认（这个窗口只有一个输入框，多按一次鼠标没意义）
            var e = Event.current;
            bool enter = e.type == EventType.KeyDown &&
                         (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);

            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("取消", GUILayout.Width(70f))) Close();
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_name)))
                    if (GUILayout.Button("保存", GUILayout.Width(70f)) ||
                        (enter && !string.IsNullOrWhiteSpace(_name)))
                    {
                        _onConfirm(_name);
                        Close();
                    }
            }
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
