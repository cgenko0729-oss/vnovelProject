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
    /// 菜单：Tools → VN Effects → 镜头编排 Camera Sequence Editor
    /// </summary>
    public partial class VNCamseqEditorWindow : EditorWindow
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

        // 顺序必须与 VNCamZoomMode 一致（下拉直接用枚举下标）
        static readonly string[] ZoomModeNames =
        {
            "both 背景+立绘一起", "depth 立绘多缩一点", "bg 只缩背景", "char 只缩立绘",
        };

        static readonly GUIContent ZoomModeLabel = new GUIContent("缩放模式",
            "谁跟着 zoom 缩放（camseq 的 mode:）：\n" +
            "both  背景+立绘同步等比放大 —— 推拉镜 TU/TB，最常用，默认\n" +
            "depth 立绘比背景多缩放（系数 0.5）—— 靠速度差造纵深，等比缩放其实是「数码变焦」\n" +
            "bg    只有背景缩放、立绘尺寸不变 —— 眩晕变焦，杀伤力极强，全篇 1~2 次\n" +
            "char  只放大立绘、背景纹丝不动 —— 强调某人的反应；也避免低分辨率背景被放糊");

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
        // 拖角 / 滑条 / 捕获镜头 / Ctrl+滚轮共用的 zoom 区间（运行时不限，手填数字框仍可到 10）。
        // 超过 3 时低分辨率背景会被放糊，用之前先在场景预览里看一眼
        const float MinZoom = 0.5f, MaxZoom = 7f;
        // 选中框四角的拖动热区半径（GUI 像素）：判定与光标共用一个数，免得两边对不上
        const float CornerHotRadius = 18f;
        // Ctrl+滚轮每一格的缩放倍率
        const float WheelZoomStep = 1.08f;

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
        [SerializeField] VNCamZoomMode _zoomMode = VNCamZoomMode.Both;  // camseq 的 mode:
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
        // Ctrl+拖动时「抓取点相对取景中心」的偏移（画布坐标）：按相对位移移动，
        // 框中心不会一按下就跳到鼠标处
        Vector2 _dragGrab;
        // 拖动起点（显示中心）：Shift 轴锁定、Δx/Δy 读数都以它为准
        Vector2 _dragStart;
        // Shift 轴锁定的方向：0 = 还没定 / 1 = 只动 X / 2 = 只动 Y
        int _dragAxis;
        // 本帧吸附上的对齐线（画布坐标），拖动时画虚线提示，松手清掉
        float? _guideX, _guideY;
        // 拖动吸附：吸到其他路径点的 X/Y 与画布中线，拖动时按住 Alt 暂停
        const string SnapPrefKey = "VNCamseq.Snap";
        bool _snapEnabled = true;
        // 吸附阈值 / 轴锁定判定距离（GUI 像素：换算成画布单位随画布大小变，手感才一致）
        const float SnapPx = 6f, AxisLockPx = 4f;
        // 方向键微调步长（画布单位），按住 Shift 用大步
        const float NudgeStep = 1f, NudgeStepBig = 10f;
        // 选中 / 拖动路径点时，进度条（连带场景预览）跳到「镜头刚到达该点」的时刻
        const string ScrubFollowPrefKey = "VNCamseq.ScrubFollow";
        bool _scrubFollow = true;

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
        // 拖动 / 缩放时取景框不超出画面：运行时允许框溢出 Overscan（背景四周的出血边），
        // 但那一圈在多数背景上是放大后的边缘像素，画布上看就是「框拖出去了」，
        // 开着它到边就自动停住，不用一点点微调
        const string KeepInsidePrefKey = "VNCamseq.KeepInside";
        bool _keepInside = true;

        public const string LibraryPath = "Assets/VNEffects/CamseqPresets.asset";

        [MenuItem("Tools/VN Effects/镜头编排 Camera Sequence Editor", priority = 4)]
        static void Open()
        {
            var win = GetWindow<VNCamseqEditorWindow>("镜头编排");
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
