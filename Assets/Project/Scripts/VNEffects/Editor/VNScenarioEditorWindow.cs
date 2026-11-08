using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditorInternal;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 剧本可视化编辑器（第一批）：
    ///   - 打开/保存 .vn.txt（文本仍是唯一真相，保存 = 逐行重新生成，格式规范化）
    ///   - 命令列表：拖动排序、增删、复制；台词/命令行全下拉参数编辑（消灭 typo）
    ///   - choice 块内嵌编辑；camseq 参数下拉 + 路径点行原样保留（下一批接镜头编辑器）
    ///   - 校验面板：id/label/语法检查，点击定位到行
    ///   - 文本预览页签、外部修改检测、Ctrl+Z/Y 撤销（约 1 秒粒度合并）
    ///   - 热重载调试：Play Mode 中直接重播选中行（不退出 Play Mode）、当前行高亮跟随、
    ///     命令级暂停/单步；播放前静默自动保存
    /// 菜单：Tools → VN Effects → 剧本编辑器 Scenario Editor
    ///
    /// 【域重载存活】窗口进 Play Mode 会被 Unity 序列化后重建（domain reload），
    /// 普通字段一律清空。文档正文/路径/脏标记/撤销栈全部走 [SerializeField]，
    /// 由 ISerializationCallbackReceiver 在重载前把 _doc 拍成文本、OnEnable 里再解析回来。
    /// 新增任何"关掉窗口也不该丢"的状态时，记得一并加进 OnBeforeSerialize / OnEnable。
    /// </summary>
    public partial class VNScenarioEditorWindow : EditorWindow, ISerializationCallbackReceiver
    {
        enum Tab { Edit, Text, Issues }

        const float LineH2 = 21f;   // 一个子行的高度（含间距）

        VNScenarioDoc _doc = new VNScenarioDoc();

        // ↓ 域重载存活组（Unity 会替我们保存/恢复；_doc 本体走 _docText 中转）
        [SerializeField] string _docText = "";
        [SerializeField] string _path = "";
        [SerializeField] long _fileTimeTicks;
        [SerializeField] bool _dirty;
        [SerializeField] bool _externalChanged;
        [SerializeField] Tab _tab;
        [SerializeField] bool _showCategoryColors;
        [SerializeField] bool _rebuildStateBeforePlay = true;
        [SerializeField] int _restoredListIndex = -1;
        [SerializeField] List<string> _undoStackSerialized = new List<string>();
        [SerializeField] List<string> _redoStackSerialized = new List<string>();
        [SerializeField] Vector2 _scroll;
        [SerializeField] int _lastPlayedLine = -1;   // Ctrl+Shift+Enter「重播上次那一行」
        // 搜索过滤词。跨域重载存活（进出 Play Mode 回来还在筛着，不然每次都要重打）
        [SerializeField] string _search = "";
        bool _focusSearchPending;   // Ctrl+F：下一帧把键盘焦点丢给搜索框
        const string SearchFieldName = "VNScenarioEditor.Search";

        System.DateTime _fileTime;
        bool _stagePreview = true;
        bool _followPlayback = true;
        bool _hideRawRows;
        bool _showHelp;             // 底部说明文字是否展开（默认收起，存 EditorPrefs）

        ReorderableList _list;
        float _pendingScrollY = -1f;

        // 播放跟随：Runner 报告的物理行 → UI 行号（-1 = 没在播 / 播的不是本文件）
        VNScriptRunner _runnerCache;
        int _playingLine = -1;
        int _playingRow = -1;

        // Enter / Shift+Enter 快捷插入台词行：KeyDown 期间不能改列表长度
        // （IMGUI 布局已在 Layout 事件里定好），所以只记位置，留到下一个 Layout 再插
        const string SayFocusControl = "VNScenarioEditor.NewSayRow";
        int _pendingInsertAt = -1;   // 待插入的行号
        int _pendingFocusRow = -1;   // 插完后要把键盘焦点送进去的行号

        // 搜索弹窗 / Ctrl+E 命令面板：回调跑在别的窗口的 GUI 里，改行数一律留到下一个
        // Layout 事件（和上面那套 _pendingInsertAt 同理）
        VNRow _pendingNewRow;
        bool _pendingNewRowAbove;
        bool _pendingPalette;        // Ctrl+E 请求：PopupWindow 只能在 OnGUI 里开
        // 参数格搜索弹窗的回填槽：PopupString 是同步返回的（camseq 路径点、choice 选项行
        // 的值都不在 VNRow.values 里，靠调用方自己写回），所以弹窗只能把结果放这儿，
        // 由下一帧的 PopupString 同步 return 出去，绝不能像 SpritePopup 那样回调直写 values
        readonly Dictionary<(VNRow, string), string> _popupResults =
            new Dictionary<(VNRow, string), string>();

        readonly VNScenarioSourceContext _ctx = new VNScenarioSourceContext();
        readonly List<SpritePreviewItem> _backgroundPreviews =
            new List<SpritePreviewItem>();
        readonly List<SpritePreviewItem> _cgPreviews =
            new List<SpritePreviewItem>();
        // CG 条目登记的显示方式（镜头编排底图要按它合成，否则竖图被拉伸铺满）
        readonly Dictionary<string, VNCgFitMode> _cgFits =
            new Dictionary<string, VNCgFitMode>();
        readonly List<CharacterPreviewItem> _characterPreviews =
            new List<CharacterPreviewItem>();
        List<VNIssue> _issues = new List<VNIssue>();
        readonly Dictionary<int, bool> _rowHasError = new Dictionary<int, bool>();
        List<string> _labels = new List<string>();
        List<string> _flags = new List<string>();
        string[] _flagOps = System.Array.Empty<string>();

        int _version = 1;
        int _validatedVersion = -1;

        // 自定义值编辑状态（选了 "custom…" 的参数格）
        readonly HashSet<(VNRow, string)> _customEdit = new HashSet<(VNRow, string)>();

        // 舞台一览：逐行推算"这行时台上有谁、背景是什么"（按文件顺序，jump/choice 近似）
        const string StagePreviewPref = "VNEffects.ScenarioEditor.StagePreview";
        const string FollowPlaybackPref = "VNEffects.ScenarioEditor.FollowPlayback";
        const string HideRawPref = "VNEffects.ScenarioEditor.HideRawRows";
        const string ShowHelpPref = "VNEffects.ScenarioEditor.ShowHelp";
        const float StageCellW = 70f;
        readonly List<RowStageState> _stageStates = new List<RowStageState>();
        int _stageStatesVersion = -1;

        // 音频试听：id → AudioClip（按通道分开），_previewAudioKey = 正在播的 "通道|id"
        readonly Dictionary<string, AudioClip> _bgmClips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> _seClips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, AudioClip> _voiceClips = new Dictionary<string, AudioClip>();
        string _previewAudioKey;

        // 撤销（文本快照，约 1 秒粒度合并）
        readonly List<string> _undoStack = new List<string>();
        readonly List<string> _redoStack = new List<string>();
        string _frameSnapshot = "";
        int _frameSnapshotVersion = -1;
        double _lastUndoPush;

        static readonly Dictionary<string, string> CommandTranslations =
            new Dictionary<string, string>
            {
                { "bg", "背景" }, { "cg", "CG 一枚绘" }, { "weather", "天气" }, { "mood", "氛围" },
                { "transition", "转场" }, { "show", "显示角色" }, { "hide", "隐藏角色" },
                { "emote", "角色动作" }, { "move", "移动角色" }, { "portrait", "对话头像" },
                { "camera", "镜头运动" }, { "camcut", "镜头切换" }, { "camto", "镜头移动" },
                { "camseq", "镜头序列" }, { "shake", "震动" }, { "fx", "特效" },
                { "sakura", "樱花" }, { "bgm", "背景音乐" }, { "se", "音效" },
                { "voice", "语音" }, { "volume", "音量" }, { "wait", "等待" },
                { "label", "标签" }, { "jump", "跳转" }, { "call", "调用" },
                { "return", "返回" }, { "params", "参数声明" }, { "flag", "变量" },
                { "if", "条件" }, { "choice", "选项" }, { "event", "事件" },
                { "chapter", "章节" }, { "quest", "任务" }, { "letterbox", "电影黑边" },
                { "mark", "漫符" }, { "overlay", "叠加层" }, { "imprint", "痕迹" }, { "sns", "手机聊天" }, { "liquid", "液体喷溅" },
                { "hideHUD", "隐藏界面" }, { "interlude", "过场" }, { "tutorial", "教程" },
            };

        static readonly Dictionary<string, string> CategoryTranslations =
            new Dictionary<string, string>
            {
                { "Scene", "场景" }, { "Character", "角色" }, { "Camera", "镜头" },
                { "FX", "特效" }, { "Audio", "音频" }, { "Flow", "流程" },
                { "SNS", "手机聊天" },
            };

        static readonly Dictionary<string, string> TransitionTranslations =
            new Dictionary<string, string>
            {
                { "NoiseDissolve", "噪声溶解" }, { "Blinds", "百叶窗" },
                { "Tiles", "方块翻转" }, { "CircleWipe", "圆形擦除" },
                { "InkSpread", "水墨扩散" }, { "WhiteFlash", "白色闪光" },
                { "BokehOrbs", "光斑圆球" }, { "Eyelid", "眼睑闭合" },
                { "PageCurl", "卷页" }, { "Shatter", "画面碎裂" },
                { "Ripple", "水波扩散" }, { "InkBleed", "墨水晕染" },
            };

        /// <summary>show 的 with:（登场预设）—— 前四个是日常向</summary>
        static readonly Dictionary<string, string> EntranceTranslations =
            new Dictionary<string, string>
            {
                { "Crossfade", "原地淡入·日常" },
                { "SlideIn", "滑入·日常" },
                { "StepIn", "滑入落地·日常" },
                { "WalkIn", "走入·日常" },
                { "DissolveGlow", "溶解辉光·华丽" },
                { "FadeSlideUp", "下方滑入淡入" },
                { "ScaleBounce", "弹跳登场·俏皮" },
                { "ShineReveal", "扫光登场·优雅" },
                { "FlashBloom", "爆闪登场·高潮" },
                { "AfterimageDash", "残影冲入·战斗" },
            };

        /// <summary>hide 的 with:（退场预设）</summary>
        static readonly Dictionary<string, string> ExitTranslations =
            new Dictionary<string, string>
            {
                { "Fade", "淡出下滑·日常" },
                { "Dissolve", "溶解消散" },
                { "RunOut", "跑出画面" },
                { "Sink", "下沉模糊·昏迷" },
            };

        /// <summary>show 的 from: / hide 的 to:（方向；留空 = 按站位推断）</summary>
        static readonly Dictionary<string, string> SideTranslations =
            new Dictionary<string, string>
            {
                { "left", "左" }, { "right", "右" }, { "top", "上" }, { "bottom", "下" },
            };

        /// <summary>emote 的中文名取自 VNEmoteCatalog（[VNEmote("中文")] 声明），不手写</summary>
        static Dictionary<string, string> EmoteTranslations => VNEmoteCatalog.Labels;

        static readonly Dictionary<string, string> MarkTranslations =
            new Dictionary<string, string>
            {
                { "sweat", "汗滴" }, { "anger", "井字怒气" }, { "exclaim", "感叹号" },
                { "question", "问号" }, { "heart", "爱心" }, { "note", "音符" },
                { "blush", "红晕" }, { "bulb", "灯泡" }, { "ellipsis", "省略号" },
                { "dizzy", "眩晕星" }, { "steam", "怒气蒸汽" }, { "clear", "全部清除" },
                { "keep", "常驻" }, { "off", "移除该符号" },
            };

        const string CategoryColorPrefPrefix = "VNEffects.ScenarioEditor.CategoryColor.";
        static readonly string[] ColorCategoryIds =
            { "Dialogue", "Scene", "Character", "Camera", "FX", "Audio", "Flow" };
        static readonly Dictionary<string, string> ColorCategoryLabels =
            new Dictionary<string, string>
            {
                { "Dialogue", "对话" }, { "Scene", "场景" }, { "Character", "角色" },
                { "Camera", "镜头" }, { "FX", "特效" }, { "Audio", "音频" },
                { "Flow", "流程" },
            };
        readonly Dictionary<string, Color> _categoryColors = new Dictionary<string, Color>();

        [MenuItem("Tools/VN Effects/剧本编辑器 Scenario Editor", priority = 1)]
        static void Open()
        {
            var win = GetWindow<VNScenarioEditorWindow>("Scenario Editor");
            win.minSize = MinWindowSize;
        }

        /// <summary>
        /// 窗口最小尺寸。minSize 是窗口自己序列化的属性，只改 Open() 里那行
        /// 对【已经开着的窗口】无效（会出现「代码里写了 640，窗口还是拖不到 960 以下」），
        /// 所以 OnEnable 里也要重新赋一次。
        /// </summary>
        static readonly Vector2 MinWindowSize = new Vector2(520f, 140f);

        void OnEnable()
        {
            minSize = MinWindowSize;   // 对已存在的窗口也生效，否则拖不小
            LoadCategoryColors();
            _stagePreview = EditorPrefs.GetBool(StagePreviewPref, true);
            _followPlayback = EditorPrefs.GetBool(FollowPlaybackPref, true);
            _hideRawRows = EditorPrefs.GetBool(HideRawPref, false);
            _showHelp = EditorPrefs.GetBool(ShowHelpPref, false);   // 说明文字默认收起（太占地方）
            // 素材库改动后自动重建下拉候选（必须在 OnDisable 里退订，见 VNAssetLibraryEvents）
            VNAssetLibraryEvents.Changed += OnAssetLibraryChanged;
            RestoreAfterDomainReload();
            BuildList();
            if (_restoredListIndex >= 0 && _restoredListIndex < _doc.rows.Count)
            {
                _list.index = _restoredListIndex;
                _list.Select(_restoredListIndex, true);
            }
            RefreshSources();
        }

        /// <summary>
        /// 域重载（进/出 Play Mode、脚本重编译）后把文档从序列化文本里还原回来。
        /// 没有存活文本时保持全新空文档，等同首次打开窗口。
        /// </summary>
        void RestoreAfterDomainReload()
        {
            _fileTime = _fileTimeTicks > 0
                ? new System.DateTime(_fileTimeTicks, System.DateTimeKind.Utc)
                : default;
            if (!string.IsNullOrEmpty(_docText))
                _doc = VNScenarioDoc.Parse(_docText);

            _undoStack.Clear();
            if (_undoStackSerialized != null) _undoStack.AddRange(_undoStackSerialized);
            _redoStack.Clear();
            if (_redoStackSerialized != null) _redoStack.AddRange(_redoStackSerialized);
        }

        /// <summary>域重载前的最后一刻：把 _doc 与两个撤销栈拍进可序列化字段</summary>
        public void OnBeforeSerialize()
        {
            // 注意：这里跑在序列化线程语境下，只能做纯 C# 运算，不要碰 Unity API
            if (_doc != null) _docText = _doc.GenerateText();
            _fileTimeTicks = _fileTime.Ticks;
            _restoredListIndex = _list != null ? _list.index : -1;
            _undoStackSerialized = new List<string>(_undoStack);
            _redoStackSerialized = new List<string>(_redoStack);
        }

        /// <summary>反序列化时不能碰 Unity API，实际还原挪到 OnEnable</summary>
        public void OnAfterDeserialize() { }

        void OnFocus()
        {
            RefreshSources();
            CheckExternalChange();
        }

        void OnDisable()
        {
            VNAssetLibraryEvents.Changed -= OnAssetLibraryChanged;
            StopAudioPreview();
        }

        /// <summary>素材浏览器 / VNGameConfig 登记了新素材 → 重建下拉候选并重绘。</summary>
        void OnAssetLibraryChanged()
        {
            RefreshSources();
            Repaint();
        }

        /// <summary>10Hz 轮询 Runner 的当前行，驱动播放跟随高亮（不用给运行时加事件）</summary>
        void OnInspectorUpdate()
        {
            int line = -1;
            if (EditorApplication.isPlaying)
            {
                VNScriptRunner runner = ResolveRunner();
                // 播的必须是本窗口打开的这个文件，否则行号对不上，宁可不高亮
                if (runner != null && runner.IsRunning && IsRunnerOnOpenFile(runner))
                    line = runner.CurrentLine;
            }

            if (line == _playingLine) return;
            _playingLine = line;
            _playingRow = line > 0 ? RowForSourceLine(line) : -1;
            if (_followPlayback && _playingRow >= 0 && _tab == Tab.Edit)
                ScrollRowIntoView(_playingRow);
            Repaint();
        }

        void BuildList()
        {
            _list = new ReorderableList(_doc.rows, typeof(VNRow), true, false, true, true)
            {
                multiSelect = true,   // Shift=连选 / Ctrl=点选，拖动整体移动
                elementHeightCallback = i => RowHeight(_doc.rows[i]),
                drawElementCallback = DrawRow,
                onAddDropdownCallback = (rect, list) => ShowAddSearch(rect),
                onRemoveCallback = list =>
                {
                    var selected = SelectedRowIndices();
                    if (selected.Count == 0) return;
                    MarkStructural();
                    for (int i = selected.Count - 1; i >= 0; i--)
                        _doc.rows.RemoveAt(selected[i]);
                    list.ClearSelection();
                    list.index = _doc.rows.Count == 0 ? -1
                        : Mathf.Clamp(selected[0], 0, _doc.rows.Count - 1);
                    Bump();
                },
                onReorderCallback = list => { PushUndo(_frameSnapshot); Bump(); },
            };
        }

        void RebindList()
        {
            BuildList();
            _customEdit.Clear();
            _pendingInsertAt = -1;
            _pendingFocusRow = -1;
        }

        /// <summary>当前多选的行号（升序、已过滤越界）；没有多选时退回单选 index</summary>
        List<int> SelectedRowIndices()
        {
            var selected = new List<int>();
            foreach (int i in _list.selectedIndices)
                if (i >= 0 && i < _doc.rows.Count) selected.Add(i);
            if (selected.Count == 0 && _list.index >= 0 && _list.index < _doc.rows.Count)
                selected.Add(_list.index);
            selected.Sort();
            return selected;
        }

    }

    /// <summary>
    /// 编辑器内 AudioClip 预览播放。Unity 没有公开 API，走内部类
    /// UnityEditor.AudioUtil 的反射（与 Project 窗口点音频文件的试听同源）。
    /// 版本兼容：新名 PlayPreviewClip/StopAllPreviewClips/IsPreviewClipPlaying，
    /// 旧名 PlayClip/StopAllClips 兜底；全部找不到时 Available=false，按钮置灰。
    /// </summary>
    static class VNEditorAudioPreview
    {
        static bool _resolved;
        static System.Reflection.MethodInfo _play, _stopAll, _isPlaying;

        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
            if (type == null) return;
            var playArgs = new[] { typeof(AudioClip), typeof(int), typeof(bool) };
            _play = type.GetMethod("PlayPreviewClip", Flags, null, playArgs, null)
                ?? type.GetMethod("PlayClip", Flags, null, playArgs, null);
            _stopAll = type.GetMethod("StopAllPreviewClips", Flags, null,
                    System.Type.EmptyTypes, null)
                ?? type.GetMethod("StopAllClips", Flags, null, System.Type.EmptyTypes, null);
            _isPlaying = type.GetMethod("IsPreviewClipPlaying", Flags, null,
                System.Type.EmptyTypes, null);
        }

        public static bool Available
        {
            get { Resolve(); return _play != null && _stopAll != null; }
        }

        /// <summary>能否查询"还在播吗"（查不到时 UI 不自动复位，需手动点 ■）</summary>
        public static bool CanQueryPlaying
        {
            get { Resolve(); return _isPlaying != null; }
        }

        public static void Play(AudioClip clip)
        {
            if (!Available || clip == null) return;
            _stopAll.Invoke(null, null);
            _play.Invoke(null, new object[] { clip, 0, false });
        }

        public static void StopAll()
        {
            if (Available) _stopAll.Invoke(null, null);
        }

        public static bool IsPlaying()
        {
            Resolve();
            if (_isPlaying == null) return true;
            return (bool)_isPlaying.Invoke(null, null);
        }
    }

    [InitializeOnLoad]
    static class VNPlayFromLineBridge
    {
        const string PendingKey = "VNEffects.PlayFromLine.Pending";
        const string SourceKey = "VNEffects.PlayFromLine.Source";
        const string LineKey = "VNEffects.PlayFromLine.Line";
        const string RebuildKey = "VNEffects.PlayFromLine.Rebuild";
        const string AssetKey = "VNEffects.PlayFromLine.Asset";
        static int _remainingAttempts;

        static VNPlayFromLineBridge()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void Request(string source, int sourceLine, bool rebuildState,
            string assetPath)
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(SourceKey, source);
            SessionState.SetInt(LineKey, Mathf.Max(1, sourceLine));
            SessionState.SetBool(RebuildKey, rebuildState);
            SessionState.SetString(AssetKey, assetPath ?? "");
            EditorApplication.isPlaying = true;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(PendingKey, false))
            {
                _remainingAttempts = 180;
                EditorApplication.update -= TryStartRunner;
                EditorApplication.update += TryStartRunner;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorApplication.update -= TryStartRunner;
            }
        }

        static void TryStartRunner()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= TryStartRunner;
                return;
            }

            var runner = Object.FindFirstObjectByType<VNScriptRunner>();
            if (runner == null || !runner.IsInitialized)
            {
                if (--_remainingAttempts > 0) return;
                Debug.LogError("[VNScript] 从选中行播放失败：找不到已初始化的 VNScriptRunner");
                ClearRequest();
                EditorApplication.update -= TryStartRunner;
                return;
            }

            string source = SessionState.GetString(SourceKey, "");
            int line = SessionState.GetInt(LineKey, 1);
            bool rebuildState = SessionState.GetBool(RebuildKey, true);
            string assetPath = SessionState.GetString(AssetKey, "");
            ClearRequest();
            EditorApplication.update -= TryStartRunner;
            // 先告诉 Runner 调试的是哪个剧本，翻译查表与跨文件跳转才对得上
            if (!string.IsNullOrEmpty(assetPath))
                runner.SetDebugScript(AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath));
            runner.PlayFromSourceLine(source, line, rebuildState);
        }

        static void ClearRequest()
        {
            SessionState.EraseBool(PendingKey);
            SessionState.EraseString(SourceKey);
            SessionState.EraseInt(LineKey);
            SessionState.EraseBool(RebuildKey);
            SessionState.EraseString(AssetKey);
        }
    }
}
