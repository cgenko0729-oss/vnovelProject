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
    /// 菜单：Tools → VN Effects → Scenario Editor
    ///
    /// 【域重载存活】窗口进 Play Mode 会被 Unity 序列化后重建（domain reload），
    /// 普通字段一律清空。文档正文/路径/脏标记/撤销栈全部走 [SerializeField]，
    /// 由 ISerializationCallbackReceiver 在重载前把 _doc 拍成文本、OnEnable 里再解析回来。
    /// 新增任何"关掉窗口也不该丢"的状态时，记得一并加进 OnBeforeSerialize / OnEnable。
    /// </summary>
    public class VNScenarioEditorWindow : EditorWindow, ISerializationCallbackReceiver
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

        System.DateTime _fileTime;
        bool _stagePreview = true;
        bool _followPlayback = true;
        bool _hideRawRows;

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
                { "mark", "漫符" }, { "sns", "手机聊天" }, { "liquid", "液体喷溅" },
                { "hideHUD", "隐藏界面" },
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

        static readonly Dictionary<string, string> EmoteTranslations =
            new Dictionary<string, string>
            {
                { "Surprise", "惊讶" }, { "Angry", "生气" }, { "Shy", "害羞" },
                { "Dejected", "沮丧" }, { "Recover", "恢复" }, { "Nod", "点头" },
                { "HeadShake", "摇头" },
            };

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

        [MenuItem("Tools/VN Effects/Scenario Editor")]
        static void Open()
        {
            var win = GetWindow<VNScenarioEditorWindow>("Scenario Editor");
            win.minSize = new Vector2(960f, 560f);
        }

        void OnEnable()
        {
            LoadCategoryColors();
            _stagePreview = EditorPrefs.GetBool(StagePreviewPref, true);
            _followPlayback = EditorPrefs.GetBool(FollowPlaybackPref, true);
            _hideRawRows = EditorPrefs.GetBool(HideRawPref, false);
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

        // ------------------------------------------------------------------
        // 数据源
        // ------------------------------------------------------------------

        void RefreshSources()
        {
            var ids = new List<string>();
            _ctx.expressions.Clear();
            _characterPreviews.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:VNCharacterDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                ids.Add(def.id);
                var exprs = new List<string>();
                var previews = new List<SpritePreviewItem>();
                foreach (var e in def.expressions)
                {
                    if (string.IsNullOrEmpty(e.name)) continue;
                    exprs.Add(e.name);
                    previews.Add(new SpritePreviewItem(e.name, e.sprite));
                }
                _ctx.expressions[def.id] = exprs.ToArray();
                _characterPreviews.Add(new CharacterPreviewItem(def.id, previews));
            }
            _ctx.characterIds = ids.ToArray();

            // ★ 素材库按**与运行时同一套覆盖语义**取数：VNGameConfig 里填了就用它的，
            //   留空才回退场景组件。以前这里只读场景上的 VNStage / VNAudio，于是
            //   在 VNGameConfig（或素材浏览器）里新登记的素材**在下拉里根本搜不到** ——
            //   而项目的铁律恰恰是「配置进资产，不进场景」，两边对不上。
            var stage = FindFirstObjectByType<VNStage>();
            var audio = FindFirstObjectByType<VNAudio>();
            var config = LoadGameConfig();

            _backgroundPreviews.Clear();
            _cgPreviews.Clear();

            var bgSrc = PickLibrary(config != null ? config.backgrounds : null,
                                    stage != null ? stage.backgrounds : null);
            if (bgSrc != null)
            {
                var bgs = new List<string>();
                foreach (var b in bgSrc)
                {
                    if (b == null || string.IsNullOrEmpty(b.id)) continue;
                    bgs.Add(b.id);
                    _backgroundPreviews.Add(new SpritePreviewItem(b.id, b.sprite));
                }
                _ctx.backgroundIds = bgs.ToArray();
            }
            else _ctx.backgroundIds = System.Array.Empty<string>();

            var cgSrc = PickLibrary(config != null ? config.cgLibrary : null,
                                    stage != null ? stage.cgLibrary : null);
            if (cgSrc != null)
            {
                var cgs = new List<string>();
                foreach (var c in cgSrc)
                {
                    if (c == null || string.IsNullOrEmpty(c.id)) continue;
                    cgs.Add(c.id);
                    _cgPreviews.Add(new SpritePreviewItem(c.id, c.sprite));
                }
                _ctx.cgIds = cgs.ToArray();
            }
            else _ctx.cgIds = System.Array.Empty<string>();

            var bgmSrc = PickLibrary(config != null ? config.bgmLibrary : null,
                                     audio != null ? audio.bgmLibrary : null);
            var seSrc = PickLibrary(config != null ? config.seLibrary : null,
                                    audio != null ? audio.seLibrary : null);
            var voiceSrc = PickLibrary(config != null ? config.voiceLibrary : null,
                                       audio != null ? audio.voiceLibrary : null);
            if (bgmSrc != null || seSrc != null || voiceSrc != null)
            {
                // 旧混合库的条目三个通道都能用，因此并入每个候选列表
                var legacy = audio != null ? audio.library : null;
                _ctx.bgmIds = CollectAudioIds(bgmSrc, legacy, _bgmClips);
                _ctx.seIds = CollectAudioIds(seSrc, legacy, _seClips);
                _ctx.voiceIds = CollectAudioIds(voiceSrc, legacy, _voiceClips);
            }
            else
            {
                _ctx.bgmIds = System.Array.Empty<string>();
                _ctx.seIds = System.Array.Empty<string>();
                _ctx.voiceIds = System.Array.Empty<string>();
                _bgmClips.Clear();
                _seClips.Clear();
                _voiceClips.Clear();
            }

            var eventRegistry = FindFirstObjectByType<VNEventRegistry>();
            _ctx.eventIds = eventRegistry != null
                ? new List<string>(eventRegistry.Ids).ToArray()
                : System.Array.Empty<string>();

            var questIds = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:VNQuestDef"))
            {
                var quest = AssetDatabase.LoadAssetAtPath<VNQuestDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (quest != null && !string.IsNullOrEmpty(quest.id))
                    questIds.Add(quest.id);
            }
            _ctx.questIds = questIds.ToArray();

            // 天气候选：内置叶型正名 + 雨雪萤火虫 + None，再补上项目里的 VNWeatherDef 资产 id。
            // （中文别名 落樱/枫叶/… 剧本里照样能写，只是不塞进下拉免得列表太长）
            var weatherIds = new List<string> { "None" };
            foreach (VNLeafShape shape in System.Enum.GetValues(typeof(VNLeafShape)))
                weatherIds.Add(VNWeatherDef.DefaultId(shape));
            weatherIds.Add(VNWeather.Rain.ToString());
            weatherIds.Add(VNWeather.Snow.ToString());
            weatherIds.Add(VNWeather.Fireflies.ToString());
            foreach (var guid in AssetDatabase.FindAssets("t:VNWeatherDef"))
            {
                var wd = AssetDatabase.LoadAssetAtPath<VNWeatherDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (wd != null && !string.IsNullOrEmpty(wd.id) && !weatherIds.Contains(wd.id))
                    weatherIds.Add(wd.id);
            }
            _ctx.weatherIds = weatherIds.ToArray();

            // UI 皮肤候选：VNGameConfig 登记的 id（default 由 OptionsFor 统一补在最前）
            var dialogueSkins = new List<string>();
            var choiceSkins = new List<string>();
            var uiCfg = VNGameConfig.Active;
            if (uiCfg != null)
            {
                foreach (var e in uiCfg.dialogueSkins)
                    if (e != null && !string.IsNullOrEmpty(e.id)) dialogueSkins.Add(e.id);
                foreach (var e in uiCfg.choiceSkins)
                    if (e != null && !string.IsNullOrEmpty(e.id)) choiceSkins.Add(e.id);
            }
            _ctx.dialogueSkinIds = dialogueSkins.ToArray();
            _ctx.choiceSkinIds = choiceSkins.ToArray();

            _ctx.scenarioLabels.Clear();
            _ctx.scenarioPaths.Clear();
            var qualifiedLabels = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:TextAsset", new[] { "Assets/Scenarios" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".vn.txt", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var scenario = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (scenario == null) continue;
                string file = Path.GetFileName(path);
                string normalized = VNStoryAddress.NormalizeFile(file);
                var labels = VNScenarioDoc.Parse(scenario.text).CollectLabels().ToArray();
                _ctx.scenarioLabels[normalized] = labels;
                _ctx.scenarioPaths[normalized] = path;
                string displayFile = VNStoryAddress.NormalizeFile(file);
                foreach (string label in labels)
                    qualifiedLabels.Add(displayFile + "::" + label);
            }
            qualifiedLabels.Sort();
            _ctx.qualifiedLabelIds = qualifiedLabels.ToArray();

            _validatedVersion = -1; // 数据源变了要重新校验
        }

        /// <summary>通道专属库 + 旧混合库合并去重后的 id 列表（保持登记顺序）。
        /// clips 非空时同步填充 id → AudioClip 映射（行内试听用）。</summary>
        /// <summary>
        /// 编辑期取 VNGameConfig 资产（不走 VNGameConfig.Active 的运行时缓存，
        /// 免得受 Play Mode 进出清缓存的时机影响）。
        /// </summary>
        static VNGameConfig LoadGameConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            return cfg != null ? cfg : Resources.Load<VNGameConfig>(VNGameConfig.ResourcesName);
        }

        /// <summary>
        /// 覆盖语义的取数：config 侧非空就用它，否则回退场景组件侧。
        /// 与 VNGameConfig.ApplyList 的运行时规则保持一致（填了才覆盖，留空不动）。
        /// </summary>
        static List<T> PickLibrary<T>(List<T> fromConfig, List<T> fromScene)
        {
            if (fromConfig != null && fromConfig.Count > 0) return fromConfig;
            return fromScene;
        }

        static string[] CollectAudioIds(List<VNAudio.AudioEntry> channelLib,
            List<VNAudio.AudioEntry> legacyLib, Dictionary<string, AudioClip> clips = null)
        {
            clips?.Clear();
            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var lib in new[] { channelLib, legacyLib })
            {
                if (lib == null) continue;
                foreach (var e in lib)
                    if (e != null && !string.IsNullOrEmpty(e.id) && seen.Add(e.id))
                    {
                        ids.Add(e.id);
                        if (clips != null && e.clip != null) clips[e.id] = e.clip;
                    }
            }
            return ids.ToArray();
        }

        void ValidateIfNeeded()
        {
            if (_validatedVersion == _version) return;
            _validatedVersion = _version;
            _issues = _doc.Validate(_ctx);
            _rowHasError.Clear();
            foreach (var issue in _issues)
            {
                if (issue.isError) _rowHasError[issue.rowIndex] = true;
                else if (!_rowHasError.ContainsKey(issue.rowIndex))
                    _rowHasError[issue.rowIndex] = false;
            }
            _labels = _doc.CollectLabels();
            _flags = _doc.CollectFlags();
            var ops = new List<string>();
            foreach (var f in _flags) { ops.Add(f + "+1"); ops.Add(f + "-1"); ops.Add(f); }
            _flagOps = ops.ToArray();
        }

        // ------------------------------------------------------------------
        // 文件
        // ------------------------------------------------------------------

        void OpenFile()
        {
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved changes?", "Discard", "Cancel")) return;
            string dir = Path.Combine(Application.dataPath, "Scenarios");
            if (!Directory.Exists(dir)) dir = Application.dataPath;
            string p = EditorUtility.OpenFilePanel("Open scenario (.vn.txt)", dir, "txt");
            if (string.IsNullOrEmpty(p)) return;
            LoadFile(p);
        }

        void LoadFile(string absolutePath)
        {
            _path = absolutePath;
            _doc = VNScenarioDoc.Parse(File.ReadAllText(absolutePath));
            _fileTime = File.GetLastWriteTimeUtc(absolutePath);
            _dirty = false;
            _externalChanged = false;
            _undoStack.Clear();
            _redoStack.Clear();
            RebindList();
            Bump();
        }

        void SaveFile(bool saveAs)
        {
            string p = _path;
            if (saveAs || string.IsNullOrEmpty(p))
            {
                string dir = Path.Combine(Application.dataPath, "Scenarios");
                if (!Directory.Exists(dir)) dir = Application.dataPath;
                p = EditorUtility.SaveFilePanel("Save scenario", dir, "NewScenario.vn", "txt");
                if (string.IsNullOrEmpty(p)) return;
            }
            File.WriteAllText(p, _doc.GenerateText(), new UTF8Encoding(false));
            _path = p;
            _fileTime = File.GetLastWriteTimeUtc(p);
            _dirty = false;
            _externalChanged = false;

            // 项目内文件刷新导入
            string assets = Application.dataPath.Replace('\\', '/');
            string norm = p.Replace('\\', '/');
            if (norm.StartsWith(assets))
                AssetDatabase.ImportAsset("Assets" + norm.Substring(assets.Length));
            ShowNotification(new GUIContent("Saved"));
        }

        void CheckExternalChange()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;
            if (File.GetLastWriteTimeUtc(_path) == _fileTime) return;
            if (!_dirty)
            {
                LoadFile(_path); // 没有本地改动 → 静默重载
                ShowNotification(new GUIContent("Reloaded (changed on disk)"));
            }
            else _externalChanged = true;
        }

        // ------------------------------------------------------------------
        // 撤销
        // ------------------------------------------------------------------

        void Bump() { _version++; _dirty = true; }

        void PushUndo(string snapshot)
        {
            if (string.IsNullOrEmpty(snapshot)) return;
            if (_undoStack.Count > 0 && _undoStack[_undoStack.Count - 1] == snapshot) return;
            _undoStack.Add(snapshot);
            if (_undoStack.Count > 100) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        void MarkStructural() => PushUndo(_frameSnapshot);

        void LoadFromText(string text)
        {
            _doc = VNScenarioDoc.Parse(text);
            // 行对象整批换掉了，按旧行引用记的编辑状态全部作废
            _popupResults.Clear();
            _customEdit.Clear();
            _pendingNewRow = null;
            RebindList();
            Bump();
        }

        void HandleUndoKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || !(e.control || e.command)) return;
            if (EditorGUIUtility.editingTextField) return; // 文本框内用系统自带撤销

            if (e.keyCode == KeyCode.Z && _undoStack.Count > 0)
            {
                _redoStack.Add(_doc.GenerateText());
                string s = _undoStack[_undoStack.Count - 1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Y && _redoStack.Count > 0)
            {
                _undoStack.Add(_doc.GenerateText());
                string s = _redoStack[_redoStack.Count - 1];
                _redoStack.RemoveAt(_redoStack.Count - 1);
                LoadFromText(s);
                e.Use();
            }
        }

        // ------------------------------------------------------------------
        // 快捷键（走 ShortcutManager：窗口作用域，用户可在 Edit → Shortcuts 里改键位）
        // ------------------------------------------------------------------

        [Shortcut("VN/Scenario Editor/Save", typeof(VNScenarioEditorWindow),
            KeyCode.S, ShortcutModifiers.Action)]
        static void ShortcutSave(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.SaveFile(false);
        }

        // 注意：ShortcutManager 不接受 Return/Enter 当绑定键（会被忽略并刷警告），
        // 所以主键位用 F5/F6，Ctrl+Enter 那套在 HandleShortcutKeys 里走 IMGUI 自己收。
        [Shortcut("VN/Scenario Editor/Play From Selected Row", typeof(VNScenarioEditorWindow),
            KeyCode.F5)]
        static void ShortcutPlaySelected(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.PlayFromSelectedRow();
        }

        [Shortcut("VN/Scenario Editor/Replay Last Line", typeof(VNScenarioEditorWindow),
            KeyCode.F6)]
        static void ShortcutReplayLast(ShortcutArguments args)
        {
            if (args.context is VNScenarioEditorWindow w) w.ReplayLastLine();
        }

        /// <summary>ShortcutManager 收不了的 Ctrl+Enter / Ctrl+Shift+Enter，在 IMGUI 里补</summary>
        void HandleShortcutKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (!(e.control || e.command) || e.alt) return;

            if (e.shift) ReplayLastLine();
            else PlayFromSelectedRow();
            e.Use();
        }

        /// <summary>
        /// Ctrl+E 命令面板。PopupWindow.Show 只能在 OnGUI 里调（要 GUI 坐标换算），
        /// 所以这里只置旗标，真正弹出在 DrawEditTab 末尾。
        /// 键位没选 Ctrl+K：那是 Unity Search 的默认绑定，Shortcuts 面板会标冲突。
        /// </summary>
        [Shortcut("VN/Scenario Editor/Command Palette", typeof(VNScenarioEditorWindow),
            KeyCode.E, ShortcutModifiers.Action)]
        static void ShortcutCommandPalette(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            w._tab = Tab.Edit;
            w._pendingPalette = true;
            w.Repaint();
        }

        [Shortcut("VN/Scenario Editor/Toggle Debug Pause", typeof(VNScenarioEditorWindow),
            KeyCode.F8)]
        static void ShortcutTogglePause(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            VNScriptRunner runner = w.ResolveRunner();
            if (runner == null) return;
            runner.SetDebugPaused(!runner.IsDebugPaused);
            w.Repaint();
        }

        [Shortcut("VN/Scenario Editor/Debug Step", typeof(VNScenarioEditorWindow),
            KeyCode.F10)]
        static void ShortcutStep(ShortcutArguments args)
        {
            if (!(args.context is VNScenarioEditorWindow w)) return;
            VNScriptRunner runner = w.ResolveRunner();
            if (runner == null) return;
            runner.RequestDebugStep();
            w.Repaint();
        }

        /// <summary>Enter = 在选区下方插一条台词行，Shift+Enter = 插在上方（文本框内不抢键）</summary>
        void HandleInsertKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || _tab != Tab.Edit) return;
            if (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            if (e.control || e.command || e.alt) return;
            // 正在文本框里打字：这次 Enter 交给文本框结束编辑，再按一次才插入
            if (EditorGUIUtility.editingTextField) return;

            var selected = SelectedRowIndices();
            if (selected.Count == 0)
                _pendingInsertAt = _doc.rows.Count;
            else
                _pendingInsertAt = e.shift
                    ? selected[0]
                    : selected[selected.Count - 1] + 1;
            e.Use();
            Repaint();
        }

        /// <summary>
        /// 搜索弹窗 / 命令面板攒好的行，在下一个 Layout 事件里插进文档。
        /// 位置语义与 Enter 一致：选区下方（Shift = 上方），没有选区就追加到末尾。
        /// </summary>
        void ApplyPendingNewRow()
        {
            if (_pendingNewRow == null) return;
            VNRow row = _pendingNewRow;
            bool above = _pendingNewRowAbove;
            _pendingNewRow = null;

            var selected = SelectedRowIndices();
            int at;
            if (selected.Count == 0) at = _doc.rows.Count;
            else at = above ? selected[0] : selected[selected.Count - 1] + 1;
            at = Mathf.Clamp(at, 0, _doc.rows.Count);

            MarkStructural();
            _doc.rows.Insert(at, row);
            _list.ClearSelection();
            _list.index = at;
            _list.Select(at, true);
            if (row.kind == VNRowKind.Say) _pendingFocusRow = at;   // 台词行直接进输入状态
            ScrollRowIntoView(at);
            Bump();
        }

        /// <summary>在下一个 Layout 事件里真正插行（改列表长度必须避开其它 IMGUI 事件）</summary>
        void ApplyPendingInsert()
        {
            if (_pendingInsertAt < 0) return;
            int at = Mathf.Clamp(_pendingInsertAt, 0, _doc.rows.Count);
            _pendingInsertAt = -1;

            MarkStructural();
            _doc.rows.Insert(at, NewSayRow());
            _list.ClearSelection();
            _list.index = at;
            _list.Select(at, true);
            _pendingFocusRow = at;
            ScrollRowIntoView(at);
            Bump();
        }

        /// <summary>目标行已经在可视区内就不动滚动条，避免插一行画面就跳一下</summary>
        void ScrollRowIntoView(int index)
        {
            if (index < 0 || index >= _doc.rows.Count) return;
            float top = 0f;
            for (int i = 0; i < index; i++) top += RowHeight(_doc.rows[i]);
            float bottom = top + RowHeight(_doc.rows[index]);
            float viewH = Mathf.Max(120f, position.height - 130f);
            if (top < _scroll.y)
                _pendingScrollY = Mathf.Max(0f, top - 40f);
            else if (bottom > _scroll.y + viewH)
                _pendingScrollY = Mathf.Max(0f, bottom - viewH + 40f);
        }

        // ------------------------------------------------------------------
        // GUI
        // ------------------------------------------------------------------

        void OnGUI()
        {
            if (Event.current.type == EventType.Layout)
            {
                ApplyPendingInsert();
                ApplyPendingNewRow();
            }

            // 帧首快照（撤销用：结构操作要拿"改动前"的文本）
            if (Event.current.type == EventType.Layout && _frameSnapshotVersion != _version)
            {
                _frameSnapshot = _doc.GenerateText();
                _frameSnapshotVersion = _version;
                _docText = _frameSnapshot;  // 域重载存活的兜底同步
                // 行数变了，跟随高亮的 UI 行号要跟着重算
                if (_playingLine > 0) _playingRow = RowForSourceLine(_playingLine);
            }

            HandleUndoKeys();
            HandleShortcutKeys();  // 必须在 HandleInsertKeys 之前：都盯着 Enter
            HandleInsertKeys();
            ValidateIfNeeded();
            DrawToolbar();
            if (_showCategoryColors)
            {
                bool previousChanged = GUI.changed;
                DrawCategoryColorSettings();
                GUI.changed = previousChanged;
            }

            if (_externalChanged)
            {
                EditorGUILayout.HelpBox(
                    "File changed on disk while you have unsaved edits.", MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Reload from disk (discard my edits)"))
                        LoadFile(_path);
                    if (GUILayout.Button("Keep mine (will overwrite on save)"))
                    {
                        _externalChanged = false;
                        _fileTime = File.GetLastWriteTimeUtc(_path);
                    }
                }
            }

            switch (_tab)
            {
                case Tab.Edit: DrawEditTab(); break;
                case Tab.Text: DrawTextTab(); break;
                case Tab.Issues: DrawIssuesTab(); break;
            }

            if (GUI.changed)
            {
                Bump();
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastUndoPush > 1.0)
                {
                    PushUndo(_frameSnapshot);
                    _lastUndoPush = now;
                }
            }
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(46f)))
                    OpenFile();
                using (new EditorGUI.DisabledScope(!_dirty && !string.IsNullOrEmpty(_path)))
                {
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                        SaveFile(false);
                }
                if (GUILayout.Button("Save As", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                    SaveFile(true);
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_path)))
                {
                    if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                    {
                        if (!_dirty || EditorUtility.DisplayDialog("Reload",
                                "Discard unsaved changes?", "Discard", "Cancel"))
                            LoadFile(_path);
                    }
                }
                if (GUILayout.Button("Refresh Sources", EditorStyles.toolbarButton, GUILayout.Width(104f)))
                    RefreshSources();
                bool previousChanged = GUI.changed;
                _showCategoryColors = GUILayout.Toggle(_showCategoryColors, "分类颜色",
                    EditorStyles.toolbarButton, GUILayout.Width(64f));
                bool stagePreview = GUILayout.Toggle(_stagePreview,
                    new GUIContent("舞台一览",
                        "每行左侧显示当前背景缩略图与在场角色的站位色块（按文件顺序推算）"),
                    EditorStyles.toolbarButton, GUILayout.Width(64f));
                if (stagePreview != _stagePreview)
                {
                    _stagePreview = stagePreview;
                    EditorPrefs.SetBool(StagePreviewPref, stagePreview);
                }
                bool hideRaw = GUILayout.Toggle(_hideRawRows,
                    new GUIContent("隐注释/空行",
                        "把空行与 # 注释折成零高度，只剩台词和命令。\n" +
                        "行号与所有编辑操作不受影响；想改注释就关掉它，或去 Text 页签改。"),
                    EditorStyles.toolbarButton, GUILayout.Width(78f));
                if (hideRaw != _hideRawRows)
                {
                    _hideRawRows = hideRaw;
                    EditorPrefs.SetBool(HideRawPref, hideRaw);
                    if (hideRaw) MoveSelectionOffHiddenRow();
                }
                GUI.changed = previousChanged;

                GUILayout.Space(8f);
                string name = string.IsNullOrEmpty(_path) ? "(untitled)" : Path.GetFileName(_path);
                GUILayout.Label(name + (_dirty ? " *" : ""), EditorStyles.miniBoldLabel);

                GUILayout.FlexibleSpace();

                // label 快速跳转（本文件 + Assets/Scenarios 下的跨文件地址）
                if ((_labels.Count > 0 || _ctx.qualifiedLabelIds.Length > 0) &&
                    GUILayout.Button("Go to label ▾", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    var menu = new GenericMenu();
                    foreach (var l in _labels)
                    {
                        string label = l;
                        menu.AddItem(new GUIContent(label), false, () => SelectLabelRow(label));
                    }
                    if (_labels.Count > 0 && _ctx.qualifiedLabelIds.Length > 0)
                        menu.AddSeparator("");
                    foreach (string address in _ctx.qualifiedLabelIds)
                    {
                        string capturedAddress = address;
                        menu.AddItem(new GUIContent("跨文件/" + address), false,
                            () => SelectStoryAddress(capturedAddress));
                    }
                    menu.ShowAsContext();
                }

                int errors = 0, warns = 0;
                foreach (var i in _issues) { if (i.isError) errors++; else warns++; }
                DrawTabButton(Tab.Edit, "Edit");
                DrawTabButton(Tab.Text, "Text");
                DrawTabButton(Tab.Issues, $"Issues ({errors}E/{warns}W)");
            }
        }

        /// <summary>
        /// 刚开启隐藏时选中行正好被藏了：往下挪到第一个可见行。
        /// 否则 Duplicate / [-] / 播放会作用在一个看不见的行上。
        /// </summary>
        void MoveSelectionOffHiddenRow()
        {
            int index = _list.index;
            if (index < 0 || index >= _doc.rows.Count) return;
            if (!IsHiddenRow(_doc.rows[index])) return;

            for (int i = index; i < _doc.rows.Count; i++)
            {
                if (IsHiddenRow(_doc.rows[i])) continue;
                _list.ClearSelection();
                _list.index = i;
                _list.Select(i, true);
                return;
            }
            // 后面全是隐藏行，往前找
            for (int i = index - 1; i >= 0; i--)
            {
                if (IsHiddenRow(_doc.rows[i])) continue;
                _list.ClearSelection();
                _list.index = i;
                _list.Select(i, true);
                return;
            }
        }

        void DrawCategoryColorSettings()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("主下拉分类颜色", EditorStyles.miniBoldLabel,
                        GUILayout.Width(92f));
                    foreach (string category in ColorCategoryIds)
                        DrawCategoryColorField(category);
                    if (GUILayout.Button("恢复默认", GUILayout.Width(64f)))
                    {
                        foreach (string category in ColorCategoryIds)
                        {
                            _categoryColors[category] = DefaultCategoryColor(category);
                            SaveCategoryColor(category);
                        }
                        Repaint();
                    }
                }
            }
        }

        void DrawCategoryColorField(string category)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(86f)))
            {
                GUILayout.Label(ColorCategoryLabels[category], EditorStyles.centeredGreyMiniLabel);
                EditorGUI.BeginChangeCheck();
                Color value = EditorGUILayout.ColorField(GUIContent.none,
                    CategoryColor(category), true, false, false, GUILayout.Width(82f));
                if (EditorGUI.EndChangeCheck())
                {
                    _categoryColors[category] = value;
                    SaveCategoryColor(category);
                    Repaint();
                }
            }
        }

        void DrawTabButton(Tab tab, string label)
        {
            bool on = GUILayout.Toggle(_tab == tab, label, EditorStyles.toolbarButton,
                GUILayout.Width(label.Length * 7f + 18f));
            if (on) _tab = tab;
        }

        void SelectLabelRow(string label)
        {
            for (int i = 0; i < _doc.rows.Count; i++)
            {
                var r = _doc.rows[i];
                if (r.kind == VNRowKind.Command && r.keyword == "label" && r.Get("name") == label)
                {
                    FocusRow(i);
                    return;
                }
            }
        }

        void SelectStoryAddress(string address)
        {
            if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                return;
            if (file == null)
            {
                SelectLabelRow(label);
                return;
            }
            if (_dirty && !EditorUtility.DisplayDialog("Unsaved changes",
                    "Discard unsaved changes and open the target scenario?", "Discard", "Cancel"))
                return;
            if (!_ctx.scenarioPaths.TryGetValue(VNStoryAddress.NormalizeFile(file), out string path))
                return;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            LoadFile(Path.GetFullPath(Path.Combine(projectRoot, path)));
            SelectLabelRow(label);
        }

        /// <summary>把某一行切到编辑页、选中并滚进视野（Issues 面板 / 镜头编排窗口共用）</summary>
        public void FocusRow(int index)
        {
            _tab = Tab.Edit;
            _list.index = index;
            float y = 0f;
            for (int i = 0; i < index && i < _doc.rows.Count; i++)
                y += RowHeight(_doc.rows[i]);
            _pendingScrollY = Mathf.Max(0f, y - 120f);
            Repaint();
        }

        // ------------------------------------------------------------------
        // Edit 页签
        // ------------------------------------------------------------------

        void DrawEditTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count))
                {
                    if (GUILayout.Button(new GUIContent("Duplicate",
                            "复制选中行（支持 Shift/Ctrl 多选，整块插到选区之后）"),
                            GUILayout.Width(72f)))
                    {
                        var selected = SelectedRowIndices();
                        if (selected.Count > 0)
                        {
                            MarkStructural();
                            int insertAt = selected[selected.Count - 1] + 1;
                            for (int i = 0; i < selected.Count; i++)
                                _doc.rows.Insert(insertAt + i,
                                    _doc.rows[selected[i]].Clone());
                            _list.ClearSelection();
                            for (int i = 0; i < selected.Count; i++)
                                _list.Select(insertAt + i, true);
                            _list.index = insertAt + selected.Count - 1;
                            Bump();
                        }
                    }
                }
                // Play Mode 中不禁用：走热重播，原地生效不退出 Play Mode。
                // 只有正在切换 Play Mode 的那一小段时间按钮才灰掉。
                bool switchingPlaymode =
                    EditorApplication.isPlayingOrWillChangePlaymode &&
                    !EditorApplication.isPlaying;
                using (new EditorGUI.DisabledScope(
                    _list.index < 0 || _list.index >= _doc.rows.Count || switchingPlaymode))
                {
                    if (GUILayout.Button(new GUIContent(
                            EditorApplication.isPlaying ? "▶ 播放选中行（热）" : "▶ 从选中行播放",
                            EditorApplication.isPlaying
                                ? "热重播：直接用当前（未保存也算）文本从选中行重跑，不退出 Play Mode。Ctrl+Enter"
                                : "先自动保存，再进入 Play Mode 并从当前行或下一条有效命令开始播放。Ctrl+Enter"),
                            GUILayout.Width(126f)))
                        PlayFromSelectedRow();
                }
                bool previousChanged = GUI.changed;
                _rebuildStateBeforePlay = GUILayout.Toggle(_rebuildStateBeforePlay,
                    new GUIContent("重建前置状态", "播放前静默恢复目标行之前的舞台状态"),
                    GUILayout.Width(96f));
                bool follow = GUILayout.Toggle(_followPlayback,
                    new GUIContent("跟随播放",
                        "Play Mode 中高亮正在执行的那一行，并在它滚出可视区时自动滚过去"),
                    GUILayout.Width(72f));
                if (follow != _followPlayback)
                {
                    _followPlayback = follow;
                    EditorPrefs.SetBool(FollowPlaybackPref, follow);
                }
                GUI.changed = previousChanged;

                DrawPlaybackControls();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_doc.rows.Count} rows", EditorStyles.miniLabel);
            }

            if (_stagePreview) RebuildStageStatesIfNeeded();

            if (_pendingScrollY >= 0f)
            {
                _scroll.y = _pendingScrollY;
                _pendingScrollY = -1f;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _list.DoLayoutList();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.HelpBox(
                "热重载调试：F5 / Ctrl+Enter = 从选中行播放（Play Mode 中是原地热重播，不退出播放；" +
                "编辑期则自动保存后进 Play Mode），F6 / Ctrl+Shift+Enter = 重播上次那一行，" +
                "Ctrl+S = 保存，F8 = 暂停/继续，F10 = 单步一条命令。" +
                "F5/F6/F8/F10/Ctrl+S 可在 Edit → Shortcuts → VN/Scenario Editor 里改键位。\n" +
                "Enter = 在选中行下方插入台词行（自动聚焦，可直接打字），Shift+Enter = 插在上方；" +
                "在文本框里打字时第一下 Enter 只是结束编辑，再按一次才插入。\n" +
                "打字找命令：Ctrl+E = 命令面板（选命令 → 逐个问必填参数 → 可选参数菜单 → " +
                "Enter 插入，Shift+Enter 插到上方，Tab 跳过参数，Esc 取消）；" +
                "右键点行首的命令按钮 = 打字换这一行的命令（左键仍是原来的分类菜单）；" +
                "底部 [+] 与各参数下拉也都能打字筛选。\n" +
                "Shift+click = select range, Ctrl+click = toggle select; " +
                "drag moves all selected rows, [-] / Duplicate act on the whole selection. " +
                "Drag handle to reorder. [+] adds after selection. \"@\" = async (do not wait). " +
                "Popups list registered ids; pick \"custom…\" to type a free value. " +
                "camseq waypoint lines are kept as text in this batch " +
                "(use Tools → VN Effects → Camera Sequence Editor and paste).",
                MessageType.Info);

            if (_pendingPalette && Event.current.type == EventType.Repaint)
            {
                _pendingPalette = false;
                ShowCommandPalette();
            }
        }

        /// <summary>
        /// 播放控制条（只在 Play Mode 有意义，编辑期整组灰掉）。
        /// 暂停是「命令级」的：卡在两条命令之间，已经跑起来的补间/打字机不会冻结——
        /// 想要真·定格画面请用 Unity 自带的暂停按钮。
        /// </summary>
        void DrawPlaybackControls()
        {
            VNScriptRunner runner = ResolveRunner();
            bool live = EditorApplication.isPlaying && runner != null;

            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(!live))
            {
                bool paused = live && runner.IsDebugPaused;
                if (GUILayout.Button(new GUIContent(paused ? "▶" : "❚❚",
                        paused ? "继续（F8）" : "暂停在下一条命令之前（F8）"),
                        GUILayout.Width(30f)))
                    runner.SetDebugPaused(!paused);

                if (GUILayout.Button(new GUIContent("⏭",
                        "单步：放行一条命令后重新暂停（F10）"), GUILayout.Width(28f)))
                    runner.RequestDebugStep();

                using (new EditorGUI.DisabledScope(
                    !live || (_playingLine <= 0 && _lastPlayedLine <= 0)))
                {
                    if (GUILayout.Button(new GUIContent("⟳",
                            "重播当前这一行（Ctrl+Shift+Enter 是重播上次播放的那一行）"),
                            GUILayout.Width(28f)))
                        ReplayPlayingLine();
                    if (GUILayout.Button(new GUIContent("⏮",
                            "退回上一条命令并从它开始播"), GUILayout.Width(28f)))
                        PlayPreviousCommand();
                }

                if (live && _playingLine > 0)
                    GUILayout.Label($"L{_playingLine}" + (paused ? " 已暂停" : ""),
                        EditorStyles.miniLabel, GUILayout.Width(74f));
            }
        }

        void PlayFromSelectedRow()
        {
            if (_list.index < 0 || _list.index >= _doc.rows.Count) return;
            PlayFromSourceLine(SourceLineForRow(_list.index));
        }

        /// <summary>「重播上次那一行」：没播过就退回选中行</summary>
        void ReplayLastLine()
        {
            if (_lastPlayedLine > 0) PlayFromSourceLine(_lastPlayedLine);
            else PlayFromSelectedRow();
        }

        /// <summary>正在播的那一行重来一遍（跟随高亮指到哪就重播哪）</summary>
        void ReplayPlayingLine()
        {
            PlayFromSourceLine(_playingLine > 0 ? _playingLine : _lastPlayedLine);
        }

        /// <summary>回退到当前行之前的那一条命令并从它开始播</summary>
        void PlayPreviousCommand()
        {
            int from = _playingLine > 0 ? _playingLine : _lastPlayedLine;
            if (from <= 0) return;
            var commands = VNScriptParser.Parse(_doc.GenerateText());
            int previous = -1;
            foreach (var command in commands)
            {
                if (command.line >= from) break;
                previous = command.line;
            }
            if (previous < 0)
            {
                ShowNotification(new GUIContent("已经是第一条命令"));
                return;
            }
            PlayFromSourceLine(previous);
        }

        /// <summary>
        /// 统一播放入口：校验 → 静默自动保存 → Play Mode 中热重播 / 否则冷启动 Bridge。
        /// Play Mode 中走热路径时完全不触发域重载，改一行到看到效果约等于一次 Repaint。
        /// </summary>
        void PlayFromSourceLine(int sourceLine)
        {
            if (sourceLine <= 0) return;

            foreach (var issue in _issues)
            {
                if (!issue.isError) continue;
                _tab = Tab.Issues;
                EditorUtility.DisplayDialog("无法开始调试",
                    "剧本仍有错误，请先在 Issues 页修正。", "确定");
                return;
            }

            string source = _doc.GenerateText();
            var commands = VNScriptParser.Parse(source);
            bool found = false;
            foreach (var command in commands)
            {
                if (command.line < sourceLine) continue;
                found = true;
                break;
            }
            if (!found)
            {
                EditorUtility.DisplayDialog("无法开始调试",
                    $"第 {sourceLine} 行之后没有可播放的命令。", "确定");
                return;
            }

            AutoSaveBeforePlay();
            _lastPlayedLine = sourceLine;

            if (EditorApplication.isPlaying)
            {
                HotReplay(source, sourceLine);
                return;
            }
            VNPlayFromLineBridge.Request(source, sourceLine, _rebuildStateBeforePlay,
                ProjectRelativePath());
        }

        /// <summary>
        /// 播放前静默把改动写盘，省得「进 Play Mode 前忘了存」。
        /// 未命名（没有路径）不弹保存框，直接拿内存文本播；
        /// 磁盘已被别处改过（_externalChanged）也不写，避免静默覆盖掉别人的改动。
        /// </summary>
        void AutoSaveBeforePlay()
        {
            if (!_dirty || string.IsNullOrEmpty(_path) || _externalChanged) return;
            SaveFile(false);
        }

        /// <summary>Play Mode 中原地重播：不退出 Play Mode，不触发域重载</summary>
        void HotReplay(string source, int sourceLine)
        {
            VNScriptRunner runner = ResolveRunner();
            if (runner == null || !runner.IsInitialized)
            {
                Debug.LogError("[VNScript] 热重播失败：当前场景里找不到已初始化的 VNScriptRunner");
                ShowNotification(new GUIContent("场景里没有 VNScriptRunner"));
                return;
            }
            // 让 Runner 知道现在调试的是哪个剧本：翻译查表与 chapter/跨文件 jump 都按它算
            runner.SetDebugScript(OpenFileAsset());
            runner.PlayFromSourceLine(source, sourceLine, _rebuildStateBeforePlay);
            Repaint();
        }

        VNScriptRunner ResolveRunner()
        {
            if (!EditorApplication.isPlaying) return _runnerCache = null;
            if (_runnerCache == null)
                _runnerCache = Object.FindFirstObjectByType<VNScriptRunner>();
            return _runnerCache;
        }

        /// <summary>Runner 正在播的文件 == 本窗口打开的文件？（跨文件 jump 后行号就不通用了）</summary>
        bool IsRunnerOnOpenFile(VNScriptRunner runner)
        {
            if (string.IsNullOrEmpty(_path)) return true; // 未命名文档：只能相信是它
            string running = runner.CurrentScriptName;
            if (string.IsNullOrEmpty(running)) return true;
            return VNStoryAddress.NormalizeFile(running) ==
                   VNStoryAddress.NormalizeFile(Path.GetFileName(_path));
        }

        /// <summary>当前文件的 "Assets/..." 相对路径（不在工程内时返回 null）</summary>
        string ProjectRelativePath()
        {
            if (string.IsNullOrEmpty(_path)) return null;
            string assets = Application.dataPath.Replace('\\', '/');
            string norm = _path.Replace('\\', '/');
            return norm.StartsWith(assets) ? "Assets" + norm.Substring(assets.Length) : null;
        }

        TextAsset OpenFileAsset()
        {
            string relative = ProjectRelativePath();
            return string.IsNullOrEmpty(relative)
                ? null : AssetDatabase.LoadAssetAtPath<TextAsset>(relative);
        }

        int SourceLineForRow(int rowIndex)
        {
            int line = 1;
            for (int i = 0; i < rowIndex && i < _doc.rows.Count; i++)
            {
                VNRow row = _doc.rows[i];
                line++;
                if (row.options != null) line += row.options.Count;
                if (row.camLines != null) line += row.camLines.Count;
            }
            return line;
        }

        /// <summary>
        /// SourceLineForRow 的逆运算：物理行 → UI 行号（播放跟随高亮用）。
        /// choice 的选项行 / camseq 的路径点行都算进它们所属的那一行。
        /// </summary>
        int RowForSourceLine(int sourceLine)
        {
            int line = 1;
            for (int i = 0; i < _doc.rows.Count; i++)
            {
                VNRow row = _doc.rows[i];
                int span = 1;
                if (row.options != null) span += row.options.Count;
                if (row.camLines != null) span += row.camLines.Count;
                if (sourceLine < line + span) return i;
                line += span;
            }
            return -1;
        }

        float RowHeight(VNRow r)
        {
            if (IsHiddenRow(r)) return 0f;
            int lines = 1;
            if (r.options != null) lines += r.options.Count;
            if (r.camLines != null) lines += r.camLines.Count;
            return lines * LineH2 + 6f;
        }

        /// <summary>
        /// 「隐注释/空行」开着时这一行是否折成零高度。
        ///
        /// 只认空行与 `#` 注释——VNRowKind.Raw 还兜着两种语法残留：
        /// 前面没有 choice 的孤儿 `*` 选项行、前面没有 camseq 的孤儿 `>` 路径点行。
        /// 那两种一旦藏起来就再也找不回来了（Issues 面板定位过去也是一片空白），
        /// 所以必须留在列表里显形。
        /// </summary>
        bool IsHiddenRow(VNRow r)
        {
            if (!_hideRawRows || r.kind != VNRowKind.Raw) return false;
            string text = r.raw == null ? "" : r.raw.TrimStart();
            return text.Length == 0 || text.StartsWith("#");
        }

        Rect SubLine(Rect rect, int line) => new Rect(
            rect.x + 14f, rect.y + 3f + line * LineH2, rect.width - 16f, LineH2 - 3f);

        void DrawRow(Rect rect, int index, bool active, bool focused)
        {
            if (index < 0 || index >= _doc.rows.Count) return;
            var r = _doc.rows[index];
            if (IsHiddenRow(r)) return;   // 零高度，什么都别画

            // 播放跟随：正在执行的这一行铺一层淡蓝底 + 左侧竖条
            if (index == _playingRow)
            {
                EditorGUI.DrawRect(new Rect(rect.x, rect.y - 1f, rect.width, rect.height + 2f),
                    new Color(0.30f, 0.62f, 1f, 0.16f));
                EditorGUI.DrawRect(new Rect(rect.x - 3f, rect.y - 1f, 3f, rect.height + 2f),
                    new Color(0.35f, 0.70f, 1f, 0.95f));
            }

            // 校验状态圆点
            if (_rowHasError.TryGetValue(index, out bool isErr))
                EditorGUI.DrawRect(new Rect(rect.x + 2f, rect.y + 8f, 7f, 7f),
                    isErr ? new Color(0.95f, 0.3f, 0.25f) : new Color(0.95f, 0.75f, 0.2f));

            // 舞台一览小格：当前背景缩略图 + 在场角色站位色块，其余内容整体右移
            if (_stagePreview)
            {
                if (index < _stageStates.Count)
                    DrawStageCell(new Rect(rect.x + 11f, rect.y + 4f,
                        StageCellW - 14f, LineH2 - 6f), _stageStates[index]);
                rect = new Rect(rect.x + StageCellW, rect.y,
                    rect.width - StageCellW, rect.height);
            }

            var line0 = SubLine(rect, 0);
            switch (r.kind)
            {
                case VNRowKind.Raw: DrawRawRow(line0, r); break;
                case VNRowKind.Say: DrawSayRow(line0, r, index); break;
                case VNRowKind.Command: DrawCommandRow(rect, line0, r, index); break;
            }
        }

        void DrawRawRow(Rect rect, VNRow r)
        {
            var style = new GUIStyle(EditorStyles.textField)
                { fontStyle = FontStyle.Italic };
            style.normal.textColor = new Color(0.55f, 0.6f, 0.55f);
            string nv = EditorGUI.TextField(rect, r.raw, style);
            if (nv != r.raw) r.raw = nv;
            if (string.IsNullOrEmpty(r.raw))
                GUI.Label(rect, " (blank line)", EditorStyles.centeredGreyMiniLabel);
        }

        void DrawSayRow(Rect rect, VNRow r, int index)
        {
            float x = rect.x;
            var typeRect = new Rect(x, rect.y, 128f, rect.height);
            // 右键 = 打字搜索。按钮本身照画不误：IMGUI 的控件 id 是按调用顺序分配的，
            // 少画一个控件会让同一帧后面的控件全部错位
            bool searchType = ConsumeRightClick(typeRect);
            if (CategoryPopupButton(typeRect, "say（对白）", "Dialogue"))
                ShowRowTypeMenu(typeRect, r);
            if (searchType) ShowRowTypeSearch(typeRect, r);
            x += 132f;

            // 说话者
            r.speaker = CharacterPopup(new Rect(x, rect.y, 110f, rect.height),
                r.speaker, r.expression, (r, "say.speaker"),
                _ => r.expression = "");
            x += 114f;

            // 表情
            r.expression = ExpressionPopup(new Rect(x, rect.y, 92f, rect.height),
                r.expression, r.speaker, (r, "say.expr"));
            x += 96f;

            float asyncW = 26f;
            bool focusMe = _pendingFocusRow == index;
            if (focusMe) GUI.SetNextControlName(SayFocusControl);
            r.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - asyncW - 2f, rect.height), r.text);
            // 控件画完后再抢焦点（IMGUI 只认已经存在的控件名）
            if (focusMe && Event.current.type == EventType.Repaint)
            {
                _pendingFocusRow = -1;
                EditorGUI.FocusTextInControl(SayFocusControl);
                Repaint();
            }
            r.isAsync = GUI.Toggle(new Rect(rect.xMax - asyncW, rect.y, asyncW, rect.height),
                r.isAsync, "@", EditorStyles.miniButton);
        }

        void DrawCommandRow(Rect fullRect, Rect line0, VNRow r, int index)
        {
            float x = line0.x;

            // 关键字下拉
            var keywordRect = new Rect(x, line0.y, 128f, line0.height);
            bool searchKeyword = ConsumeRightClick(keywordRect);   // 右键 = 打字搜索
            if (CategoryPopupButton(keywordRect, CommandDisplayName(r.keyword),
                    CommandCategory(r.keyword)))
                ShowRowTypeMenu(keywordRect, r);
            if (searchKeyword) ShowRowTypeSearch(keywordRect, r);
            x += 132f;

            var def = VNScenarioSchema.Find(r.keyword);
            float asyncW = 26f;
            // 行尾按钮区：choice 的 "+ option" / camseq 的 "🎬 预设▾ + wp"
            float tailW = 0f;
            if (r.options != null) tailW = 78f;
            else if (r.camLines != null) tailW = CamseqHeaderButtonsWidth;
            float avail = line0.xMax - x - asyncW - tailW - 4f;

            if (def != null && def.parameters.Length > 0)
                DrawParams(new Rect(x, line0.y, avail, line0.height), r, def);
            else if (def != null)
                GUI.Label(new Rect(x, line0.y, avail, line0.height),
                    def.hint, EditorStyles.centeredGreyMiniLabel);

            r.isAsync = GUI.Toggle(new Rect(line0.xMax - asyncW, line0.y, asyncW, line0.height),
                r.isAsync, "@", EditorStyles.miniButton);

            // ---- choice 选项行 ----
            if (r.options != null)
            {
                for (int i = 0; i < r.options.Count; i++)
                    DrawChoiceOption(SubLine(fullRect, 1 + i), r, i);
                // header 右侧的 + option
                var addRect = new Rect(line0.xMax - asyncW - 78f, line0.y, 74f, line0.height);
                if (GUI.Button(addRect, "+ option", EditorStyles.miniButton))
                {
                    MarkStructural();
                    r.options.Add(new VNChoiceOptionRow());
                    Bump();
                }
            }

            // ---- camseq 路径点行 ----
            if (r.camLines != null)
            {
                for (int i = 0; i < r.camLines.Count; i++)
                    if (DrawCamWaypointRow(SubLine(fullRect, 1 + i), r, i)) break;
                DrawCamseqHeaderButtons(line0, asyncW, r, index);
            }
        }

        // ------------------------------------------------------------------
        // camseq：路径点行 + header 上的镜头编排 / 预设入口
        // ------------------------------------------------------------------

        // 「编排」40 + 「预设▾」52 + 「+ wp」50 + 间距（emoji 在 IMGUI 默认字体里是方块，用汉字）
        const float CamseqHeaderButtonsWidth = 40f + 4f + 52f + 4f + 50f + 4f;

        void DrawCamseqHeaderButtons(Rect line0, float asyncW, VNRow r, int index)
        {
            float x = line0.xMax - asyncW - CamseqHeaderButtonsWidth + 4f;

            bool linked = VNCamseqEditorWindow.IsLinkedTo(this, index);
            var prevBg = GUI.backgroundColor;
            if (linked) GUI.backgroundColor = new Color(1f, 0.85f, 0.35f);
            if (GUI.Button(new Rect(x, line0.y, 40f, line0.height),
                    new GUIContent("编排", linked
                        ? "镜头编排窗口正在编辑这一行（再点一次把窗口调到前面）"
                        : "在镜头编排窗口里可视化编辑这段运镜（画布拖点、时间轴预览，改动回写本行）"),
                    EditorStyles.miniButton))
                VNCamseqEditorWindow.OpenLinked(this, index);
            GUI.backgroundColor = prevBg;
            x += 44f;

            var presetRect = new Rect(x, line0.y, 52f, line0.height);
            if (GUI.Button(presetRect,
                    new GUIContent("预设▾", "套用内置运镜模板或已保存的预设（会覆盖本行的全部路径点）"),
                    EditorStyles.miniButton))
                ShowCamseqPresetMenu(presetRect, r, index);
            x += 56f;

            if (GUI.Button(new Rect(x, line0.y, 50f, line0.height),
                    new GUIContent("+ wp", "追加一个路径点"), EditorStyles.miniButton))
            {
                MarkStructural();
                r.camLines.Add(new VNCamWaypoint
                {
                    point = "middle", zoom = 1.4f, duration = 0.8f,
                }.Format());
                Bump();
            }
        }

        /// <summary>
        /// 画一行路径点。能解析的走字段化控件，解析不了的退回纯文本框并标黄
        /// （旧剧本的手写行、未来新语法都不会被吞掉）。返回 true = 本行被删掉，
        /// 调用方要立刻停止遍历。
        /// </summary>
        bool DrawCamWaypointRow(Rect rect, VNRow r, int i)
        {
            GUI.Label(new Rect(rect.x, rect.y, 12f, rect.height), "›", EditorStyles.miniLabel);
            var delRect = new Rect(rect.xMax - 20f, rect.y, 20f, rect.height);
            var body = new Rect(rect.x + 12f, rect.y, rect.xMax - 24f - rect.x - 12f, rect.height);

            if (VNCamWaypoint.TryParse(r.camLines[i], out var wp))
            {
                if (DrawCamWaypointFields(body, r, i, wp))
                    r.camLines[i] = wp.Format();
            }
            else
            {
                // 解析不了：原样文本 + 黄底提醒，鼠标悬停说明为什么
                var style = new GUIStyle(EditorStyles.textField);
                style.normal.textColor = new Color(0.95f, 0.8f, 0.25f);
                EditorGUI.DrawRect(body, new Color(0.85f, 0.65f, 0.1f, 0.12f));
                string nv = EditorGUI.TextField(body, r.camLines[i], style);
                if (nv != r.camLines[i]) r.camLines[i] = nv;
                GUI.Label(body, new GUIContent("", "这一行认不出来，暂按纯文本保留。\n" +
                    "语法：> 目标点 [zoom] [时长] [ease:名] [xfade:秒] [hold:秒] " +
                    "[shake:等级|强度,秒数]\n" +
                    "改成合法写法后会自动变回字段化控件。"));
            }

            if (GUI.Button(delRect, "x", EditorStyles.miniButton))
            {
                MarkStructural();
                r.camLines.RemoveAt(i);
                Bump();
                return true;
            }
            return false;
        }

        /// <summary>字段化的一行；返回 true = 有改动，调用方负责写回文本</summary>
        bool DrawCamWaypointFields(Rect rect, VNRow r, int i, VNCamWaypoint wp)
        {
            EditorGUI.BeginChangeCheck();
            float x = rect.x;

            // 点位类型（由 point token 反推；切换时给一个合理的初值）
            var kind = wp.Kind;
            var newKind = (VNCamPointKind)EditorGUI.Popup(
                new Rect(x, rect.y, 56f, rect.height), (int)kind, CamPointKindNames);
            x += 60f;
            if (newKind != kind)
            {
                wp.point = newKind == VNCamPointKind.Anchor ? "middle"
                    : newKind == VNCamPointKind.Coords ? "0,0"
                    : newKind == VNCamPointKind.Stay ? VNCamWaypointDef.StayToken
                    : (_ctx.characterIds.Length > 0 ? _ctx.characterIds[0] : "");
                // 切到「原地」默认时长 0（画面纹丝不动）；切出去时给回常用默认值
                if (newKind == VNCamPointKind.Stay) wp.duration = 0f;
                else if (kind == VNCamPointKind.Stay) wp.duration = VNCamWaypoint.DefaultDuration;
                kind = newKind;
            }

            // 尾部固定宽度：zoom / 秒 / ease / xfade / hold / 震
            const float tailW = 34f + 48f + 4f + 20f + 42f + 4f + 76f + 4f + 34f + 40f
                                + 4f + 32f + 40f + 4f + 24f + 118f;
            float targetW = Mathf.Max(90f, rect.xMax - x - tailW - 4f);
            DrawCamPointTarget(new Rect(x, rect.y, targetW, rect.height), r, i, wp, kind);
            x = rect.xMax - tailW;

            GUI.Label(new Rect(x, rect.y, 34f, rect.height),
                new GUIContent("zoom", "取景倍率：1 = 全图，越大越推近"), EditorStyles.miniLabel);
            x += 34f;
            if (kind == VNCamPointKind.Stay)
            {
                // 原地点的 zoom 沿用上一个点，这里给个禁用占位——留个能敲的框只会误导
                using (new EditorGUI.DisabledScope(true))
                    EditorGUI.TextField(new Rect(x, rect.y, 48f, rect.height), "沿用");
            }
            else
            {
                wp.zoom = EditorGUI.FloatField(new Rect(x, rect.y, 48f, rect.height), wp.zoom);
            }
            x += 52f;

            GUI.Label(new Rect(x, rect.y, 20f, rect.height),
                new GUIContent("秒", "移动到本点的时长；0 = 瞬切"), EditorStyles.miniLabel);
            x += 20f;
            wp.duration = EditorGUI.FloatField(new Rect(x, rect.y, 42f, rect.height), wp.duration);
            x += 46f;

            // 缓动/锚点/部位一律用同步 Popup：SpritePopup 那套是异步回调，
            // 会把选中值写进 VNRow.values（路径点存的是 camLines 文本，两条路径不能混）
            wp.ease = OptionalPopup(new Rect(x, rect.y, 76f, rect.height),
                wp.ease, VNScenarioSchema.EaseNames, "(默认缓动)");
            x += 80f;

            GUI.Label(new Rect(x, rect.y, 34f, rect.height),
                new GUIContent("xfade", "叠化到本点的秒数（>0 时代替平移/瞬切）"),
                EditorStyles.miniLabel);
            x += 34f;
            wp.fade = Mathf.Max(0f,
                EditorGUI.FloatField(new Rect(x, rect.y, 40f, rect.height), wp.fade));
            x += 44f;

            GUI.Label(new Rect(x, rect.y, 32f, rect.height),
                new GUIContent("hold", "到达本点后停留的秒数（0 = 不停，直接走下一段）"),
                EditorStyles.miniLabel);
            x += 32f;
            wp.hold = Mathf.Max(0f,
                EditorGUI.FloatField(new Rect(x, rect.y, 40f, rect.height), wp.hold));
            x += 44f;

            GUI.Label(new Rect(x, rect.y, 24f, rect.height),
                new GUIContent("震", VNCamShakeUi.Tooltip), EditorStyles.miniLabel);
            x += 24f;
            wp.shake = VNCamShakeUi.Draw(new Rect(x, rect.y, 118f, rect.height), wp.shake);

            return EditorGUI.EndChangeCheck();
        }

        void DrawCamPointTarget(Rect rect, VNRow r, int i, VNCamWaypoint wp, VNCamPointKind kind)
        {
            switch (kind)
            {
                case VNCamPointKind.Stay:
                    // 沿用上一个点，没有可编辑的目标——写句人话比留个空框强
                    GUI.Label(rect, new GUIContent("沿用上一个点（位置与 zoom 都不变）",
                        "原地：镜头一动不动，专门用来在序列中间插一段震动或停顿。\n" +
                        "时长写 0 就是完全静止；不能当第一个路径点（没有上一个点可沿用）。"),
                        EditorStyles.miniLabel);
                    break;

                case VNCamPointKind.Anchor:
                {
                    int at = Mathf.Max(0,
                        System.Array.IndexOf(VNCamWaypoint.Anchors, wp.point.ToLower()));
                    at = EditorGUI.Popup(rect, at, CamAnchorDisplayNames);
                    wp.point = VNCamWaypoint.Anchors[at];
                    break;
                }

                case VNCamPointKind.Character:
                {
                    VNCamWaypoint.SplitCharacter(wp.point, out string id, out string part);
                    float half = Mathf.Max(60f, rect.width * 0.58f);
                    // 角色 id 走 PopupString：它是同步返回的，custom… 还能手打场景里没有的 id
                    string newId = PopupString(new Rect(rect.x, rect.y, half, rect.height),
                        id, _ctx.characterIds, "(选角色)", (r, $"wp{i}.char"));
                    string newPart = OptionalPopup(
                        new Rect(rect.x + half + 4f, rect.y, rect.width - half - 4f, rect.height),
                        part, VNCamWaypoint.CharacterParts, "(整体)");
                    wp.point = VNCamWaypoint.JoinCharacter(newId, newPart);
                    break;
                }

                case VNCamPointKind.Coords:
                {
                    VNCamWaypoint.SplitCoords(wp.point, out float px, out float py);
                    float half = (rect.width - 22f) * 0.5f;
                    GUI.Label(new Rect(rect.x, rect.y, 10f, rect.height), "x",
                        EditorStyles.miniLabel);
                    px = EditorGUI.FloatField(
                        new Rect(rect.x + 10f, rect.y, half, rect.height), px);
                    GUI.Label(new Rect(rect.x + half + 14f, rect.y, 10f, rect.height), "y",
                        EditorStyles.miniLabel);
                    py = EditorGUI.FloatField(
                        new Rect(rect.x + half + 24f, rect.y, half - 2f, rect.height), py);
                    wp.point = VNCamWaypoint.JoinCoords(px, py);
                    break;
                }
            }
        }

        /// <summary>「(留空) + 候选」的同步下拉；返回 "" 表示选了留空项</summary>
        static string OptionalPopup(Rect rect, string value, string[] options, string emptyLabel)
        {
            var display = new string[options.Length + 1];
            display[0] = emptyLabel;
            for (int i = 0; i < options.Length; i++) display[i + 1] = options[i];

            int index = string.IsNullOrEmpty(value)
                ? 0 : System.Array.IndexOf(options, value) + 1;
            if (index < 0) index = 0;   // 认不出的值当留空显示（TryParse 已挡掉大部分）
            int picked = EditorGUI.Popup(rect, index, display);
            return picked <= 0 ? "" : options[picked - 1];
        }

        static readonly string[] CamPointKindNames = { "锚点", "角色", "坐标", "原地" };

        static readonly string[] CamAnchorDisplayNames =
        {
            "topleft（左上）", "top（上）", "topright（右上）",
            "left（左）", "middle（中）", "right（右）",
            "bottomleft（左下）", "bottom（下）", "bottomright（右下）",
        };

        void ShowCamseqPresetMenu(Rect rect, VNRow r, int index)
        {
            var menu = new GenericMenu();
            string character = _ctx.characterIds.Length > 0 ? _ctx.characterIds[0] : null;

            foreach (var entry in VNCamseqTemplates.All)
            {
                string text = VNCamseqTemplates.Resolve(entry.text, character);
                menu.AddItem(new GUIContent($"内置模板/{entry.name}"), false,
                    () => ApplyCamseqTextToRow(index, text, "套用模板"));
            }

            var library = LoadCamseqPresetLibrary();
            if (library != null && library.presets.Count > 0)
            {
                menu.AddSeparator("");
                foreach (var preset in library.presets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.name)) continue;
                    string text = preset.camseqText;
                    menu.AddItem(new GUIContent($"我的预设/{preset.name}"), false,
                        () => ApplyCamseqTextToRow(index, text, "套用预设"));
                }
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("把本行存为预设…"), false, () => SaveRowAsCamseqPreset(r));
            menu.DropDown(rect);
        }

        static VNCamseqPresetLibrary LoadCamseqPresetLibrary() =>
            AssetDatabase.LoadAssetAtPath<VNCamseqPresetLibrary>(
                VNCamseqEditorWindow.LibraryPath);

        void SaveRowAsCamseqPreset(VNRow r)
        {
            if (r.camLines == null || r.camLines.Count == 0)
            {
                ShowNotification(new GUIContent("这一行还没有路径点"));
                return;
            }
            string name = VNTextPromptWindow.Prompt("存为镜头预设", "预设名称", "");
            if (string.IsNullOrEmpty(name)) return;
            VNCamseqEditorWindow.SavePreset(name, CamseqTextOfRow(r));
            ShowNotification(new GUIContent($"已保存预设「{name}」"));
        }

        /// <summary>把一行 camseq（含 header 参数）拍成完整的 camseq 文本</summary>
        static string CamseqTextOfRow(VNRow r)
        {
            var header = new Dictionary<string, string>();
            foreach (var key in VNCamseqText.HeaderKeys)
            {
                string v = r.Get(key);
                if (!string.IsNullOrEmpty(v)) header[key] = v;
            }
            return VNCamseqText.Join(header, r.camLines ?? new List<string>());
        }

        /// <summary>套用一段 camseq 文本到指定行（预设/模板/镜头窗口回写共用）</summary>
        void ApplyCamseqTextToRow(int index, string camseqText, string what)
        {
            if (!ApplyCamseqText(index, camseqText))
            {
                ShowNotification(new GUIContent($"{what}失败：目标行不是 camseq"));
                return;
            }
            ShowNotification(new GUIContent(what + "完成"));
        }

        // ---- 供镜头编排窗口调用的公开接口 ----

        /// <summary>当前打开文件的显示名（镜头窗口的绑定条用）</summary>
        public string ScenarioDisplayName =>
            string.IsNullOrEmpty(_path) ? "(未命名)" : Path.GetFileName(_path);

        /// <summary>文档改动计数：镜头窗口靠它发现「剧本那边被改了」</summary>
        public int DocVersion => _version;

        public bool IsCamseqRow(int index) =>
            index >= 0 && index < _doc.rows.Count &&
            _doc.rows[index].kind == VNRowKind.Command &&
            _doc.rows[index].keyword == "camseq" &&
            _doc.rows[index].camLines != null;

        /// <summary>当前选中行是 camseq 时返回它的行号，否则 -1（自动跟随用）</summary>
        public int SelectedCamseqRow()
        {
            int index = _list != null ? _list.index : -1;
            return IsCamseqRow(index) ? index : -1;
        }

        /// <summary>
        /// 某一行的舞台推算快照（镜头编排窗口拿它画底图和立绘）。
        /// 数据源就是行左侧「舞台一览」那格用的同一套逐行推算，
        /// 所以两边看到的背景/在场角色永远一致。
        /// 注意它是**按文件顺序的近似**：jump / choice 分支不展开，
        /// camseq 落在分支里时可能推不准——镜头窗口那边留了手动覆盖的口子。
        /// </summary>
        public bool TryGetRowStage(int index, out VNRowStageInfo info)
        {
            info = null;
            if (index < 0 || index >= _doc.rows.Count) return false;
            RebuildStageStatesIfNeeded();
            if (index >= _stageStates.Count) return false;

            var state = _stageStates[index];
            info = new VNRowStageInfo
            {
                bgId = state.bg,
                cgId = state.cg,
                backdrop = state.cg != null
                    ? SpriteFor(_cgPreviews, state.cg)
                    : state.bg != null ? SpriteFor(_backgroundPreviews, state.bg) : null,
            };
            // CG 盖着且没 keepChars 时立绘本来就看不见，别画上去误导取景
            if (state.cg == null || state.cgKeepChars)
                foreach (var c in state.chars)
                {
                    var preview = _characterPreviews.Find(p => p.id == c.id);
                    info.characters.Add(new VNRowStageChar
                    {
                        id = c.id,
                        slot = c.slot,
                        sprite = preview != null ? preview.DefaultSprite : null,
                    });
                }
            return true;
        }

        /// <summary>读出某一行的完整 camseq 文本</summary>
        public bool TryGetCamseqText(int index, out string text)
        {
            text = null;
            if (!IsCamseqRow(index)) return false;
            text = CamseqTextOfRow(_doc.rows[index]);
            return true;
        }

        /// <summary>
        /// 把一段 camseq 文本写回指定行（header 参数 + 路径点行整体替换）。
        /// 撤销走和手动编辑一样的 1 秒节流，免得镜头窗口实时回写时刷爆撤销栈。
        /// </summary>
        public bool ApplyCamseqText(int index, string camseqText)
        {
            if (!IsCamseqRow(index)) return false;
            if (!VNCamseqText.TrySplit(camseqText, out var header, out var lines)) return false;

            var r = _doc.rows[index];
            if (CamseqTextOfRow(r) == VNCamseqText.Join(header, lines)) return true; // 无变化

            PushUndoThrottled();
            foreach (var key in VNCamseqText.HeaderKeys)
                r.Set(key, header.TryGetValue(key, out string v) ? v : "");
            r.camLines.Clear();
            r.camLines.AddRange(lines);
            Bump();
            Repaint();
            return true;
        }

        /// <summary>外部改动的撤销入口：与 OnGUI 末尾同一套 1 秒节流</summary>
        void PushUndoThrottled()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastUndoPush <= 1.0) return;
            PushUndo(_doc.GenerateText());
            _lastUndoPush = now;
        }

        void ShowRowTypeMenu(Rect rect, VNRow row)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Dialogue（对话）/say（对白）"),
                row.kind == VNRowKind.Say, () =>
                {
                    if (row.kind == VNRowKind.Say) return;
                    MarkStructural();
                    SetSayRow(row);
                });
            menu.AddSeparator("");
            foreach (var command in VNScenarioSchema.Commands)
            {
                string keyword = command.keyword;
                string path = $"{CategoryDisplayName(command.category)}/{CommandDisplayName(keyword)}";
                menu.AddItem(new GUIContent(path),
                    row.kind == VNRowKind.Command && keyword == row.keyword, () =>
                {
                    if (row.kind == VNRowKind.Command && keyword == row.keyword) return;
                    MarkStructural();
                    SetKeyword(row, keyword);
                });
            }
            menu.DropDown(rect);
        }

        /// <summary>
        /// 右键点行首命令按钮 = 打字换命令。GUI.Button 只吃左键，所以右键要自己收；
        /// ReorderableList 的选中/拖动也只认左键，这里 Use() 掉不会抢它的事件。
        /// </summary>
        static bool ConsumeRightClick(Rect rect)
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 1 ||
                !rect.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        /// <summary>右键行首按钮：打字换这一行的命令（左键那套分类菜单原样保留）</summary>
        void ShowRowTypeSearch(Rect rect, VNRow row)
        {
            PopupWindow.Show(rect, new VNSearchPopup(
                "换成哪个命令？（打字筛选）", BuildCommandItems(true, false),
                item =>
                {
                    if (item.value == "say")
                    {
                        if (row.kind == VNRowKind.Say) return;
                        MarkStructural();
                        SetSayRow(row);
                    }
                    else
                    {
                        if (row.kind == VNRowKind.Command && row.keyword == item.value) return;
                        MarkStructural();
                        SetKeyword(row, item.value);
                    }
                    Repaint();
                }));
        }

        /// <summary>底部 [+]：打字挑要加的行（命令 / 台词 / 注释 / 空行）</summary>
        void ShowAddSearch(Rect rect)
        {
            PopupWindow.Show(rect, new VNSearchPopup(
                "加一行什么？（打字筛选）", BuildCommandItems(true, true),
                item =>
                {
                    _pendingNewRow = NewRowForSearchValue(item.value);
                    _pendingNewRowAbove = false;
                    Repaint();
                }));
        }

        void ShowCommandPalette()
        {
            var activator = new Rect(position.width * 0.5f - 270f, 70f, 540f, 0f);
            PopupWindow.Show(activator, new VNCommandPalette(
                BuildCommandItems(true, false),
                BuildParamCandidates,
                NewRowForSearchValue,
                (row, above) =>
                {
                    _pendingNewRow = row;
                    _pendingNewRowAbove = above;
                    Repaint();
                }));
        }

        /// <summary>搜索候选的 value → 新行（"say" / "#" / "" 是三个特殊值）</summary>
        VNRow NewRowForSearchValue(string value)
        {
            switch (value)
            {
                case "say": return NewSayRow();
                case "#": return new VNRow { kind = VNRowKind.Raw, raw = "# " };
                case "": return new VNRow { kind = VNRowKind.Raw, raw = "" };
                default: return NewCommandRow(value);
            }
        }

        /// <summary>命令候选表：数据源就是 Schema，以后加新命令自动出现在搜索里</summary>
        List<VNSearchItem> BuildCommandItems(bool includeSay, bool includeRaw)
        {
            var items = new List<VNSearchItem>();
            if (includeSay)
                items.Add(new VNSearchItem
                {
                    value = "say",
                    title = "say（对白）",
                    subtitle = "普通台词行",
                    searchExtra = "Dialogue 对话 台词 duibai",
                    accent = CategoryColor("Dialogue"),
                });

            foreach (var command in VNScenarioSchema.Commands)
                items.Add(new VNSearchItem
                {
                    value = command.keyword,
                    title = CommandDisplayName(command.keyword),
                    subtitle = FirstLine(command.hint),
                    searchExtra = CategoryDisplayName(command.category),
                    accent = CategoryColor(command.category),
                });

            if (includeRaw)
            {
                items.Add(new VNSearchItem
                    { value = "#", title = "# 注释", searchExtra = "comment 注释" });
                items.Add(new VNSearchItem
                    { value = "", title = "（空行）", searchExtra = "blank 空行" });
            }
            return items;
        }

        /// <summary>命令面板问参数时的候选；返回 null = 这个参数是自由文本/数字，直接打</summary>
        List<VNSearchItem> BuildParamCandidates(VNRow row, VNParamDef parameter)
        {
            if (parameter == null) return null;

            string[] options = parameter.id == "say.speaker"
                ? _ctx.characterIds
                : OptionsFor(row, parameter);
            if (options == null) return null;

            string[] display = parameter.id == "say.speaker"
                ? null : DisplayOptionsFor(row, parameter, options);

            var items = new List<VNSearchItem>();
            for (int i = 0; i < options.Length; i++)
                items.Add(new VNSearchItem
                {
                    value = options[i],
                    title = display != null && display.Length == options.Length
                        ? display[i] : options[i],
                });
            return items;
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int at = text.IndexOf('\n');
            return at < 0 ? text : text.Substring(0, at);
        }

        static string CommandDisplayName(string keyword) =>
            CommandTranslations.TryGetValue(keyword, out string translation)
                ? $"{keyword}（{translation}）" : keyword;

        static string CategoryDisplayName(string category) =>
            CategoryTranslations.TryGetValue(category, out string translation)
                ? $"{category}（{translation}）" : category;

        static string CommandCategory(string keyword)
        {
            var definition = VNScenarioSchema.Find(keyword);
            return definition != null ? definition.category : "";
        }

        bool CategoryPopupButton(Rect rect, string label, string category)
        {
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = CategoryColor(category);
            bool clicked = GUI.Button(rect, label, EditorStyles.popup);
            GUI.backgroundColor = previous;
            return clicked;
        }

        Color CategoryColor(string category) =>
            _categoryColors.TryGetValue(category, out Color color) ? color : Color.white;

        void LoadCategoryColors()
        {
            _categoryColors.Clear();
            foreach (string category in ColorCategoryIds)
            {
                string html = EditorPrefs.GetString(CategoryColorPrefPrefix + category, "");
                if (!string.IsNullOrEmpty(html) &&
                    ColorUtility.TryParseHtmlString("#" + html, out Color saved))
                    _categoryColors[category] = saved;
                else
                    _categoryColors[category] = DefaultCategoryColor(category);
            }
        }

        void SaveCategoryColor(string category)
        {
            EditorPrefs.SetString(CategoryColorPrefPrefix + category,
                ColorUtility.ToHtmlStringRGBA(CategoryColor(category)));
        }

        static Color DefaultCategoryColor(string category)
        {
            switch (category)
            {
                case "Dialogue": return new Color(0.55f, 0.78f, 1f);
                case "Scene": return new Color(0.58f, 0.88f, 0.66f);
                case "Character": return new Color(1f, 0.68f, 0.78f);
                case "Camera": return new Color(0.65f, 0.72f, 1f);
                case "FX": return new Color(0.86f, 0.65f, 1f);
                case "Audio": return new Color(1f, 0.78f, 0.42f);
                case "Flow": return new Color(0.72f, 0.78f, 0.82f);
                default: return Color.white;
            }
        }

        void SetSayRow(VNRow row)
        {
            row.kind = VNRowKind.Say;
            row.keyword = "";
            row.values.Clear();
            row.extraTokens.Clear();
            row.options = null;
            row.camLines = null;
            row.speaker = "";
            row.expression = "";
            row.text = "";
            Bump();
        }

        void SetKeyword(VNRow r, string keyword)
        {
            r.kind = VNRowKind.Command;
            r.keyword = keyword;
            r.values.Clear();
            r.extraTokens.Clear();
            var def = VNScenarioSchema.Find(keyword);
            r.options = def != null && def.blockChoice
                ? (r.options ?? new List<VNChoiceOptionRow> { new VNChoiceOptionRow() })
                : null;
            r.camLines = def != null && def.blockCamseq
                ? (r.camLines ?? new List<string>()) : null;
            Bump();
        }

        void DrawChoiceOption(Rect rect, VNRow r, int i)
        {
            var o = r.options[i];
            float x = rect.x + 4f;
            GUI.Label(new Rect(x, rect.y, 12f, rect.height), "*");
            x += 14f;

            // if + cost + flag + jump + delete
            float tailW = 16f + 66f + 4f + 16f + 66f + 4f + 118f + 4f + 118f + 4f + 20f;
            o.text = EditorGUI.TextField(
                new Rect(x, rect.y, rect.xMax - x - tailW - 6f, rect.height), o.text);
            x = rect.xMax - tailW;

            GUI.Label(new Rect(x, rect.y, 16f, rect.height),
                new GUIContent("if", "if:条件——不满足则隐藏该选项（无空格，如 魅力>=20）"),
                EditorStyles.miniLabel);
            x += 16f;
            o.condition = EditorGUI.TextField(new Rect(x, rect.y, 66f, rect.height), o.condition);
            x += 70f;
            GUI.Label(new Rect(x, rect.y, 16f, rect.height),
                new GUIContent("$", "cost:花费——如 金钱-100，付不起时选项置灰，选中自动扣除并飘字"),
                EditorStyles.miniLabel);
            x += 16f;
            o.costOp = EditorGUI.TextField(new Rect(x, rect.y, 66f, rect.height), o.costOp);
            x += 70f;

            o.flagOp = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.flagOp, _flagOps, "(no flag)", (r, $"opt{i}.flag"));
            x += 122f;
            o.jump = PopupString(new Rect(x, rect.y, 118f, rect.height),
                o.jump, LabelAddressOptions(), "(continue)", (r, $"opt{i}.jump"));
            x += 122f;

            if (GUI.Button(new Rect(rect.xMax - 20f, rect.y, 20f, rect.height), "x",
                EditorStyles.miniButton))
            {
                MarkStructural();
                r.options.RemoveAt(i);
                Bump();
            }
        }

        // ---- 参数区 ----

        void DrawParams(Rect rect, VNRow r, VNCommandDef def)
        {
            // 计算总权重与标签宽
            float totalWeight = 0f;
            float labelTotal = 0f;
            foreach (var p in def.parameters)
            {
                totalWeight += p.weight;
                labelTotal += LabelWidth(p);
            }
            float fieldAvail = rect.width - labelTotal - def.parameters.Length * 4f;
            float x = rect.x;

            foreach (var p in def.parameters)
            {
                float lw = LabelWidth(p);
                if (lw > 0f)
                {
                    GUI.Label(new Rect(x, rect.y, lw, rect.height), p.label,
                        EditorStyles.miniLabel);
                    x += lw;
                }
                float w = Mathf.Max(34f, fieldAvail * p.weight / totalWeight);
                DrawParamField(new Rect(x, rect.y, w, rect.height), r, p);
                x += w + 4f;
            }
        }

        static float LabelWidth(VNParamDef p) =>
            string.IsNullOrEmpty(p.label) ? 0f : Mathf.Min(64f, p.label.Length * 7f + 6f);

        void DrawParamField(Rect rect, VNRow r, VNParamDef p)
        {
            string v = r.Get(p.id);
            string[] options = OptionsFor(r, p);

            if (r.keyword == "show" && p.source == VNParamSource.Character)
            {
                string character = CharacterPopup(rect, v, r.Get("expr"), (r, p.id),
                    _ => r.Set("expr", ""));
                if (character != v) r.Set(p.id, character);
                return;
            }

            if (r.keyword == "show" && p.source == VNParamSource.Expression)
            {
                string expression = ExpressionPopup(rect, v, r.Get(p.dependsOn), (r, p.id));
                if (expression != v) r.Set(p.id, expression);
                return;
            }

            if (p.source == VNParamSource.Background)
            {
                string background = BackgroundPopup(rect, v, (r, p.id));
                if (background != v) r.Set(p.id, background);
                return;
            }

            if (p.source == VNParamSource.Cg)
            {
                // "off" 走普通文本（关闭 CG 不需要缩略图浏览器）
                if (v == "off")
                {
                    string typed = EditorGUI.TextField(rect, v);
                    if (typed != v) r.Set(p.id, typed);
                    return;
                }
                string cg = CgPopup(rect, v, (r, p.id));
                if (cg != v) r.Set(p.id, cg);
                return;
            }

            // 音频 id 参数：左侧加 ▶ 试听小按钮（编辑器内直接预览选中的素材）
            if (p.source == VNParamSource.AudioBgm || p.source == VNParamSource.AudioSe ||
                p.source == VNParamSource.AudioVoice)
            {
                var playRect = new Rect(rect.x, rect.y, 20f, rect.height);
                rect = new Rect(playRect.xMax + 2f, rect.y,
                    Mathf.Max(34f, rect.width - 22f), rect.height);
                DrawAudioPreviewButton(playRect, p.source, v);
            }

            if (options == null)
            {
                // 自由文本 / 数字
                bool bad = p.source == VNParamSource.Number &&
                           !string.IsNullOrEmpty(v) && !float.TryParse(v, out _);
                var prev = GUI.color;
                if (bad) GUI.color = new Color(1f, 0.55f, 0.5f);
                string nv = EditorGUI.TextField(rect, v);
                GUI.color = prev;
                if (nv != v) r.Set(p.id, nv);
                if (string.IsNullOrEmpty(nv) && !string.IsNullOrEmpty(p.defaultValue))
                    GUI.Label(rect, " " + p.defaultValue, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            string nv2 = PopupString(rect, v, options, "-", (r, p.id),
                DisplayOptionsFor(r, p, options));
            if (nv2 != v) r.Set(p.id, nv2);
        }

        /// <summary>枚举类参数的中英对照显示名（写进剧本的值仍是英文）；没有对照就返回 null</summary>
        static string[] DisplayOptionsFor(VNRow r, VNParamDef p, string[] options)
        {
            if (IsTransitionParameter(r, p))
                return BuildTranslatedOptions(options, TransitionTranslations);
            if (IsEmoteParameter(r, p))
                return BuildTranslatedOptions(options, EmoteTranslations);
            if (IsMarkParameter(r, p))
                return BuildTranslatedOptions(options, MarkTranslations);
            if (r.keyword == "show" && p.id == "with")
                return BuildTranslatedOptions(options, EntranceTranslations);
            if (r.keyword == "hide" && p.id == "with")
                return BuildTranslatedOptions(options, ExitTranslations);
            if ((r.keyword == "show" && p.id == "from") ||
                (r.keyword == "hide" && p.id == "to"))
                return BuildTranslatedOptions(options, SideTranslations);
            return null;
        }

        static bool IsTransitionParameter(VNRow row, VNParamDef parameter) =>
            (row.keyword == "bg" && parameter.id == "transition") ||
            (row.keyword == "cg" && parameter.id == "transition") ||
            (row.keyword == "transition" && parameter.id == "type");

        static bool IsEmoteParameter(VNRow row, VNParamDef parameter) =>
            row.keyword == "emote" && parameter.id == "emote";

        static bool IsMarkParameter(VNRow row, VNParamDef parameter) =>
            row.keyword == "mark" && (parameter.id == "mark" || parameter.id == "mode");

        static string[] BuildTranslatedOptions(string[] options,
            Dictionary<string, string> translations)
        {
            var display = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
                display[i] = translations.TryGetValue(options[i], out string translation)
                    ? $"{options[i]}（{translation}）" : options[i];
            return display;
        }

        string[] OptionsFor(VNRow r, VNParamDef p)
        {
            switch (p.source)
            {
                case VNParamSource.Options: return p.options;
                case VNParamSource.Character: return _ctx.characterIds;
                case VNParamSource.Expression:
                    return _ctx.expressions.TryGetValue(r.Get(p.dependsOn), out var e)
                        ? e : System.Array.Empty<string>();
                case VNParamSource.Background: return _ctx.backgroundIds;
                case VNParamSource.Cg: return _ctx.cgIds;
                case VNParamSource.AudioBgm: return _ctx.bgmIds;
                case VNParamSource.AudioSe:
                    if (r.keyword == "se" && p.id == "a")
                    {
                        var withStop = new List<string> { "stop" };
                        withStop.AddRange(_ctx.seIds);
                        return withStop.ToArray();
                    }
                    return _ctx.seIds;
                case VNParamSource.AudioVoice: return _ctx.voiceIds;
                case VNParamSource.EventId: return _ctx.eventIds;
                case VNParamSource.QuestId: return _ctx.questIds;
                case VNParamSource.WeatherId: return _ctx.weatherIds;
                case VNParamSource.UiSkinId: return UiSkinOptions(r.Get(p.dependsOn));
                case VNParamSource.Label: return LabelAddressOptions();
                case VNParamSource.Flag: return _flags.ToArray();
                default: return null; // Text / Number → 文本框
            }
        }

        /// <summary>
        /// ui 命令第二参数的候选。kind=name 时列的是**内置名字样式预设**
        /// （不在 VNGameConfig 登记，与 dialogue/choice 的皮肤 id 是两套东西）。
        /// </summary>
        string[] UiSkinOptions(string kind)
        {
            var options = new List<string> { "default" };
            if (kind == "name")
            {
                foreach (var a in VNNameplateStyle.Aliases) options.Add(a.name);
            }
            else if (kind == "choice")
            {
                options.AddRange(_ctx.choiceSkinIds);
            }
            else
            {
                options.AddRange(_ctx.dialogueSkinIds);
            }
            return options.ToArray();
        }

        string[] LabelAddressOptions()
        {
            var options = new List<string>(_labels);
            options.AddRange(_ctx.qualifiedLabelIds);
            return options.ToArray();
        }

        // ---- 舞台一览小格 ----

        /// <summary>一行的舞台快照：该行播完后台上的背景/CG 与角色站位</summary>
        sealed class RowStageState
        {
            public string bg;
            public string cg;
            public bool cgKeepChars;
            public readonly List<StageChar> chars = new List<StageChar>();
        }

        struct StageChar
        {
            public string id;
            public int slot;   // 0=left 1=center 2=right
        }

        static readonly Color[] StagePalette =
        {
            new Color(0.98f, 0.55f, 0.62f), new Color(0.45f, 0.75f, 1f),
            new Color(0.55f, 0.88f, 0.55f), new Color(1f, 0.82f, 0.4f),
            new Color(0.8f, 0.62f, 1f), new Color(0.45f, 0.88f, 0.85f),
            new Color(1f, 0.68f, 0.4f), new Color(0.75f, 0.8f, 0.55f),
        };

        /// <summary>按文件顺序逐行推算舞台状态（jump/choice 分支不展开，与
        /// "重建前置状态"调试同一近似）。仅 _version 变化时重算。</summary>
        void RebuildStageStatesIfNeeded()
        {
            if (_stageStatesVersion == _version) return;
            _stageStatesVersion = _version;
            _stageStates.Clear();

            string bg = null, cg = null;
            bool cgKeepChars = false;
            var chars = new List<StageChar>();

            foreach (var r in _doc.rows)
            {
                if (r.kind == VNRowKind.Command)
                {
                    switch (r.keyword)
                    {
                        case "bg":
                            if (!string.IsNullOrEmpty(r.Get("id"))) bg = r.Get("id");
                            break;
                        case "cg":
                        {
                            string id = r.Get("id");
                            if (id == "off") { cg = null; cgKeepChars = false; }
                            else if (!string.IsNullOrEmpty(id))
                            {
                                cg = id;
                                cgKeepChars = r.Get("chars") == "keep";
                            }
                            break;
                        }
                        case "show": ApplyStageShow(chars, r); break;
                        case "hide":
                        {
                            string id = r.Get("character");
                            chars.RemoveAll(c => c.id == id);
                            break;
                        }
                        case "move": ApplyStageMove(chars, r); break;
                    }
                }

                var state = new RowStageState
                    { bg = bg, cg = cg, cgKeepChars = cgKeepChars };
                state.chars.AddRange(chars);
                _stageStates.Add(state);
            }
        }

        static void ApplyStageShow(List<StageChar> chars, VNRow r)
        {
            string id = r.Get("character");
            if (string.IsNullOrEmpty(id)) return;
            string at = r.Get("at");
            int existing = chars.FindIndex(c => c.id == id);
            if (existing >= 0)
            {
                // 已在场且没写 at → 原地换表情，站位不动（与运行时语义一致）
                if (!string.IsNullOrEmpty(at))
                    chars[existing] = new StageChar { id = id, slot = SlotIndex(at) };
                return;
            }
            chars.Add(new StageChar { id = id, slot = SlotIndex(at) });
        }

        static void ApplyStageMove(List<StageChar> chars, VNRow r)
        {
            string id = r.Get("character");
            int i = chars.FindIndex(c => c.id == id);
            if (i < 0) return;
            chars[i] = new StageChar { id = id, slot = SlotIndex(r.Get("at")) };
        }

        static int SlotIndex(string at)
        {
            switch (at)
            {
                case "left": return 0;
                case "right": return 2;
                case null: case "": case "center": return 1;
                default:
                    // 自定义 x 坐标：按左/中/右粗分桶
                    if (float.TryParse(at, out float x))
                        return x < -120f ? 0 : x > 120f ? 2 : 1;
                    return 1;
            }
        }

        Color StageCharColor(string id)
        {
            int i = System.Array.IndexOf(_ctx.characterIds, id);
            if (i < 0)
            {
                i = 0;
                foreach (char c in id) i = i * 31 + c;
                i = Mathf.Abs(i);
            }
            return StagePalette[i % StagePalette.Length];
        }

        static string SlotName(int slot) => slot == 0 ? "左" : slot == 2 ? "右" : "中";

        void DrawStageCell(Rect rect, RowStageState state)
        {
            // 背景（CG 显示时优先画 CG）缩略图
            var thumbRect = new Rect(rect.x, rect.y, 30f, rect.height);
            EditorGUI.DrawRect(thumbRect, new Color(0.08f, 0.08f, 0.08f, 1f));
            string shownId = state.cg ?? state.bg;
            Sprite sprite = state.cg != null
                ? SpriteFor(_cgPreviews, state.cg)
                : state.bg != null ? SpriteFor(_backgroundPreviews, state.bg) : null;
            if (sprite != null)
                DrawSpritePreview(new Rect(thumbRect.x + 1f, thumbRect.y + 1f,
                    thumbRect.width - 2f, thumbRect.height - 2f), sprite);
            else
                GUI.Label(thumbRect, shownId == null ? "—" : "?",
                    EditorStyles.centeredGreyMiniLabel);

            // 三个站位格（左/中/右），有人 = 角色专属色块
            bool dimmed = state.cg != null && !state.cgKeepChars; // CG 默认藏立绘
            float blockW = 7f;
            float blockH = Mathf.Min(rect.height, 13f);
            float blockY = rect.y + (rect.height - blockH) * 0.5f;
            float baseX = thumbRect.xMax + 4f;
            for (int slot = 0; slot < 3; slot++)
                EditorGUI.DrawRect(new Rect(baseX + slot * (blockW + 2f), blockY,
                    blockW, blockH), new Color(0f, 0f, 0f, 0.28f));
            foreach (var c in state.chars)
            {
                Color color = StageCharColor(c.id);
                if (dimmed) color.a = 0.3f;
                EditorGUI.DrawRect(new Rect(baseX + c.slot * (blockW + 2f) + 1f,
                    blockY + 1f, blockW - 2f, blockH - 2f), color);
            }

            // 整格 tooltip：背景/CG/在场角色一览
            var tip = new StringBuilder();
            tip.Append("背景: ").Append(string.IsNullOrEmpty(state.bg) ? "（无）" : state.bg);
            if (state.cg != null)
                tip.Append("\nCG: ").Append(state.cg)
                   .Append(state.cgKeepChars ? "（保留立绘）" : "（隐藏立绘）");
            tip.Append("\n台上: ");
            if (state.chars.Count == 0) tip.Append("（无人）");
            else
                for (int i = 0; i < state.chars.Count; i++)
                {
                    if (i > 0) tip.Append("、");
                    tip.Append(state.chars[i].id)
                       .Append('（').Append(SlotName(state.chars[i].slot)).Append('）');
                }
            tip.Append("\n（按文件顺序推算，jump/choice 分支不展开）");
            GUI.Label(rect, new GUIContent("", tip.ToString()));
        }

        // ---- 音频行内试听 ----

        AudioClip FindAudioClip(VNParamSource source, string id)
        {
            if (string.IsNullOrEmpty(id) || id == "stop") return null;
            Dictionary<string, AudioClip> clips;
            switch (source)
            {
                case VNParamSource.AudioBgm: clips = _bgmClips; break;
                case VNParamSource.AudioSe: clips = _seClips; break;
                case VNParamSource.AudioVoice: clips = _voiceClips; break;
                default: return null;
            }
            return clips.TryGetValue(id, out var clip) ? clip : null;
        }

        void DrawAudioPreviewButton(Rect rect, VNParamSource source, string id)
        {
            AudioClip clip = FindAudioClip(source, id);
            string key = source + "|" + id;
            bool playing = _previewAudioKey == key;

            // 试听按钮不能把文档标脏（同 分类颜色 开关的处理）
            bool previousChanged = GUI.changed;
            using (new EditorGUI.DisabledScope(clip == null || !VNEditorAudioPreview.Available))
            {
                var content = playing
                    ? new GUIContent("■", "停止试听")
                    : new GUIContent("▶", clip != null
                        ? $"试听「{id}」（编辑器预览，不含音量标定/循环）"
                        : "先选择一个已登记的音频 id");
                if (GUI.Button(rect, content, EditorStyles.miniButton))
                {
                    if (playing) StopAudioPreview();
                    else StartAudioPreview(key, clip);
                }
            }
            GUI.changed = previousChanged;
        }

        void StartAudioPreview(string key, AudioClip clip)
        {
            VNEditorAudioPreview.Play(clip);
            _previewAudioKey = key;
            EditorApplication.update -= PollAudioPreview;
            if (VNEditorAudioPreview.CanQueryPlaying)
                EditorApplication.update += PollAudioPreview;
            Repaint();
        }

        void StopAudioPreview()
        {
            EditorApplication.update -= PollAudioPreview;
            if (_previewAudioKey != null)
            {
                _previewAudioKey = null;
                VNEditorAudioPreview.StopAll();
                Repaint();
            }
        }

        void PollAudioPreview()
        {
            if (_previewAudioKey == null)
            {
                EditorApplication.update -= PollAudioPreview;
                return;
            }
            if (!VNEditorAudioPreview.IsPlaying())
            {
                _previewAudioKey = null;
                EditorApplication.update -= PollAudioPreview;
                Repaint();
            }
        }

        /// <summary>
        /// 下拉 + "custom…" 自由输入的通用控件。emptyLabel 对应空值。
        ///
        /// 【同步契约不能破】本函数是同步返回的：调用方拿返回值自己写回。
        /// camseq 路径点（值在 camLines 文本里）和 choice 选项行（值在 VNChoiceOptionRow
        /// 字段里）都靠这一点，所以搜索弹窗只能把选中值丢进 _popupResults，
        /// 由下一帧的本函数 return 出去——绝不能学 SpritePopup 在回调里直写 values。
        /// </summary>
        string PopupString(Rect rect, string value, string[] options, string emptyLabel,
            (VNRow, string) key, string[] displayOptions = null)
        {
            // 上一帧弹窗选的值：同步交还给调用方
            if (_popupResults.TryGetValue(key, out string picked))
            {
                _popupResults.Remove(key);
                _customEdit.Remove(key);
                GUI.changed = true;
                return picked;
            }

            bool custom = _customEdit.Contains(key) ||
                          (!string.IsNullOrEmpty(value) &&
                           System.Array.IndexOf(options, value) < 0);
            if (custom)
            {
                var tRect = new Rect(rect.x, rect.y, rect.width - 16f, rect.height);
                string nv = EditorGUI.TextField(tRect, value);
                if (GUI.Button(new Rect(rect.xMax - 15f, rect.y, 15f, rect.height), "▾",
                    EditorStyles.miniButton))
                {
                    _customEdit.Remove(key);
                    if (System.Array.IndexOf(options, nv) < 0) nv = "";
                    GUI.changed = true;
                }
                return nv;
            }

            int idx = System.Array.IndexOf(options, value);
            string label = string.IsNullOrEmpty(value) ? emptyLabel
                : (displayOptions != null && displayOptions.Length == options.Length &&
                   idx >= 0 ? displayOptions[idx] : value);
            if (GUI.Button(rect, label, EditorStyles.popup))
                ShowStringSearch(rect, options, emptyLabel, key, displayOptions);
            return value;
        }

        /// <summary>参数格的可搜下拉：选中值放进 _popupResults，下一帧由 PopupString 交还</summary>
        void ShowStringSearch(Rect rect, string[] options, string emptyLabel,
            (VNRow, string) key, string[] displayOptions)
        {
            var items = new List<VNSearchItem>
            {
                new VNSearchItem { value = "", title = emptyLabel, searchExtra = "清空 留空" },
            };
            for (int i = 0; i < options.Length; i++)
                items.Add(new VNSearchItem
                {
                    value = options[i],
                    title = displayOptions != null && displayOptions.Length == options.Length
                        ? displayOptions[i] : options[i],
                });

            PopupWindow.Show(rect, new VNSearchPopup(
                "打字筛选，或直接输入自定义值", items,
                item =>
                {
                    _popupResults[key] = item.value;
                    Repaint();
                },
                twoLine: false, allowFreeValue: true,
                onCustom: () =>
                {
                    _customEdit.Add(key);   // 切成常驻文本框（要反复改自由值时用）
                    Repaint();
                },
                width: 300f, height: 280f));
        }

        string BackgroundPopup(Rect rect, string value, (VNRow, string) key)
            => SpritePopup(rect, value, key, _backgroundPreviews,
                SpriteFor(_backgroundPreviews, value), "-", "未选择背景",
                "清除选择", "没有匹配的背景", null);

        string CgPopup(Rect rect, string value, (VNRow, string) key)
            => SpritePopup(rect, value, key, _cgPreviews,
                SpriteFor(_cgPreviews, value), "-", "未选择 CG",
                "清除选择", "没有匹配的 CG", null);

        string CharacterPopup(Rect rect, string value, string expression,
            (VNRow, string) key, System.Action<string> afterSelect)
        {
            var items = new List<SpritePreviewItem>();
            foreach (var character in _characterPreviews)
                items.Add(new SpritePreviewItem(character.id, character.DefaultSprite));
            return SpritePopup(rect, value, key, items,
                CharacterSprite(value, expression), "(narration)", "未选择角色（旁白）",
                "设为旁白", "没有匹配的角色", afterSelect);
        }

        string ExpressionPopup(Rect rect, string value, string characterId,
            (VNRow, string) key)
        {
            CharacterPreviewItem character =
                _characterPreviews.Find(item => item.id == characterId);
            var items = character != null
                ? new List<SpritePreviewItem>(character.expressions)
                : new List<SpritePreviewItem>();
            return SpritePopup(rect, value, key, items,
                CharacterSprite(characterId, value), "(default)", "使用默认表情",
                "默认表情", "没有匹配的表情", null);
        }

        Sprite CharacterSprite(string characterId, string expression)
        {
            CharacterPreviewItem character =
                _characterPreviews.Find(item => item.id == characterId);
            if (character == null) return null;
            Sprite selected = SpriteFor(character.expressions, expression);
            return selected != null ? selected : character.DefaultSprite;
        }

        static Sprite SpriteFor(List<SpritePreviewItem> items, string id)
        {
            SpritePreviewItem item = items.Find(candidate => candidate.id == id);
            return item != null ? item.sprite : null;
        }

        string SpritePopup(Rect rect, string value, (VNRow, string) key,
            List<SpritePreviewItem> items, Sprite inlineSprite, string emptyLabel,
            string emptyTooltip, string clearLabel, string noMatchesLabel,
            System.Action<string> afterSelect)
        {
            bool registered = string.IsNullOrEmpty(value) ||
                items.Exists(item => item.id == value);
            bool custom = _customEdit.Contains(key) || !registered;
            if (custom)
            {
                var textRect = new Rect(rect.x, rect.y, rect.width - 16f, rect.height);
                string edited = EditorGUI.TextField(textRect, value);
                if (GUI.Button(new Rect(rect.xMax - 15f, rect.y, 15f, rect.height), "▾",
                    EditorStyles.miniButton))
                {
                    _customEdit.Remove(key);
                    if (!items.Exists(item => item.id == edited)) edited = "";
                    GUI.changed = true;
                }
                return edited;
            }

            bool openPicker = false;
            Rect popupRect = rect;
            if (rect.width >= 76f)
            {
                float previewWidth = Mathf.Min(42f, rect.height * 2.2f);
                var previewRect = new Rect(rect.x, rect.y, previewWidth, rect.height);
                popupRect = new Rect(previewRect.xMax + 2f, rect.y,
                    rect.width - previewWidth - 2f, rect.height);

                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f, 1f));
                string tooltip = string.IsNullOrEmpty(value) ? emptyTooltip : value;
                if (inlineSprite != null)
                {
                    DrawSpritePreview(new Rect(previewRect.x + 1f, previewRect.y + 1f,
                        previewRect.width - 2f, previewRect.height - 2f), inlineSprite);
                    string path = AssetDatabase.GetAssetPath(inlineSprite);
                    if (!string.IsNullOrEmpty(path)) tooltip += "\n" + path;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    GUI.Label(previewRect, "!", EditorStyles.centeredGreyMiniLabel);
                    tooltip += "\n没有可用的 Sprite 预览";
                }

                if (GUI.Button(previewRect, new GUIContent("", tooltip), GUIStyle.none))
                    openPicker = true;
            }

            string label = string.IsNullOrEmpty(value) ? emptyLabel : value;
            if (GUI.Button(popupRect, label, EditorStyles.popup)) openPicker = true;
            if (openPicker)
                ShowSpritePicker(rect, value, key, items, clearLabel, noMatchesLabel,
                    afterSelect);
            return value;
        }

        void ShowSpritePicker(Rect activatorRect, string value, (VNRow, string) key,
            List<SpritePreviewItem> items, string clearLabel, string noMatchesLabel,
            System.Action<string> afterSelect)
        {
            PopupWindow.Show(activatorRect, new SpritePickerPopup(
                new List<SpritePreviewItem>(items), value, clearLabel, noMatchesLabel,
                selected =>
                {
                    if (PopupValue(key) == selected) return;
                    PushUndo(_doc.GenerateText());
                    SetPopupValue(key, selected);
                    _customEdit.Remove(key);
                    afterSelect?.Invoke(selected);
                    Bump();
                    Repaint();
                },
                () =>
                {
                    _customEdit.Add(key);
                    Repaint();
                }));
        }

        static string PopupValue((VNRow, string) key)
        {
            switch (key.Item2)
            {
                case "say.speaker": return key.Item1.speaker;
                case "say.expr": return key.Item1.expression;
                default: return key.Item1.Get(key.Item2);
            }
        }

        static void SetPopupValue((VNRow, string) key, string value)
        {
            switch (key.Item2)
            {
                case "say.speaker":
                    key.Item1.speaker = value;
                    key.Item1.Set(key.Item2, ""); // 清理由旧版错误回调写入的普通参数
                    break;
                case "say.expr":
                    key.Item1.expression = value;
                    key.Item1.Set(key.Item2, "");
                    break;
                default:
                    key.Item1.Set(key.Item2, value);
                    break;
            }
        }

        static void DrawSpritePreview(Rect rect, Sprite sprite)
        {
            Rect source = sprite.textureRect;
            float aspect = source.width / Mathf.Max(1f, source.height);
            Rect fitted = rect;
            if (aspect > rect.width / rect.height)
            {
                fitted.height = rect.width / aspect;
                fitted.y += (rect.height - fitted.height) * 0.5f;
            }
            else
            {
                fitted.width = rect.height * aspect;
                fitted.x += (rect.width - fitted.width) * 0.5f;
            }

            Texture2D texture = sprite.texture;
            var uv = new Rect(source.x / texture.width, source.y / texture.height,
                source.width / texture.width, source.height / texture.height);
            GUI.DrawTextureWithTexCoords(fitted, texture, uv, true);
        }

        sealed class SpritePreviewItem
        {
            public readonly string id;
            public readonly Sprite sprite;

            public SpritePreviewItem(string id, Sprite sprite)
            {
                this.id = id;
                this.sprite = sprite;
            }
        }

        sealed class CharacterPreviewItem
        {
            public readonly string id;
            public readonly List<SpritePreviewItem> expressions;

            public CharacterPreviewItem(string id, List<SpritePreviewItem> expressions)
            {
                this.id = id;
                this.expressions = expressions;
            }

            public Sprite DefaultSprite => expressions.Count > 0 ? expressions[0].sprite : null;
        }

        sealed class SpritePickerPopup : PopupWindowContent
        {
            const float CellWidth = 154f;
            const float CellHeight = 116f;
            const float PreviewHeight = 82f;

            readonly List<SpritePreviewItem> _items;
            readonly string _selected;
            readonly string _clearLabel;
            readonly string _noMatchesLabel;
            readonly System.Action<string> _onSelect;
            readonly System.Action _onCustom;
            readonly List<SpritePreviewItem> _filtered =
                new List<SpritePreviewItem>();
            Vector2 _scroll;
            string _search = "";

            public SpritePickerPopup(List<SpritePreviewItem> items, string selected,
                string clearLabel, string noMatchesLabel,
                System.Action<string> onSelect, System.Action onCustom)
            {
                _items = items;
                _selected = selected;
                _clearLabel = clearLabel;
                _noMatchesLabel = noMatchesLabel;
                _onSelect = onSelect;
                _onCustom = onCustom;
            }

            public override Vector2 GetWindowSize() => new Vector2(520f, 430f);

            public override void OnGUI(Rect rect)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    GUI.SetNextControlName("BackgroundSearch");
                    _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField);
                    if (GUILayout.Button(_clearLabel, EditorStyles.toolbarButton,
                            GUILayout.Width(72f)))
                    {
                        _onSelect("");
                        editorWindow.Close();
                        return;
                    }
                    if (GUILayout.Button("custom…", EditorStyles.toolbarButton,
                            GUILayout.Width(66f)))
                    {
                        _onCustom();
                        editorWindow.Close();
                        return;
                    }
                }

                BuildFilteredList();
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                int columns = Mathf.Max(1, Mathf.FloorToInt((rect.width - 12f) / CellWidth));
                int rows = Mathf.CeilToInt(_filtered.Count / (float)columns);
                Rect grid = GUILayoutUtility.GetRect(rect.width - 12f,
                    Mathf.Max(40f, rows * CellHeight));

                if (_filtered.Count == 0)
                {
                    GUI.Label(grid, _noMatchesLabel, EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    for (int i = 0; i < _filtered.Count; i++)
                    {
                        int column = i % columns;
                        int row = i / columns;
                        var cell = new Rect(grid.x + column * CellWidth,
                            grid.y + row * CellHeight, CellWidth - 6f, CellHeight - 6f);
                        DrawItem(cell, _filtered[i]);
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            void BuildFilteredList()
            {
                _filtered.Clear();
                foreach (var item in _items)
                {
                    if (string.IsNullOrEmpty(_search) ||
                        item.id.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        _filtered.Add(item);
                }
            }

            void DrawItem(Rect rect, SpritePreviewItem item)
            {
                bool selected = item.id == _selected;
                EditorGUI.DrawRect(rect, selected
                    ? new Color(0.25f, 0.55f, 0.9f, 0.42f)
                    : new Color(0f, 0f, 0f, 0.16f));

                var previewRect = new Rect(rect.x + 4f, rect.y + 4f,
                    rect.width - 8f, PreviewHeight);
                EditorGUI.DrawRect(previewRect, new Color(0.08f, 0.08f, 0.08f, 1f));
                if (item.sprite != null)
                    DrawSpritePreview(previewRect, item.sprite);
                else
                    GUI.Label(previewRect, "无预览", EditorStyles.centeredGreyMiniLabel);

                var labelRect = new Rect(rect.x + 4f, previewRect.yMax + 3f,
                    rect.width - 8f, 20f);
                GUI.Label(labelRect, selected ? $"✓  {item.id}" : item.id,
                    EditorStyles.centeredGreyMiniLabel);

                if (GUI.Button(rect, new GUIContent("", item.id), GUIStyle.none))
                {
                    _onSelect(item.id);
                    editorWindow.Close();
                }
            }

        }

        // ---- 添加菜单 ----

        VNRow NewSayRow() => new VNRow { kind = VNRowKind.Say };

        VNRow NewCommandRow(string keyword)
        {
            var r = new VNRow { kind = VNRowKind.Command };
            SetKeyword(r, keyword);
            if (r.camLines != null && r.camLines.Count == 0)
                r.camLines.Add(new VNCamWaypoint
                {
                    point = "middle", zoom = 1.4f, duration = 0.8f,
                }.Format());
            return r;
        }

        void InsertRow(VNRow row)
        {
            MarkStructural();
            int at = _list.index >= 0 && _list.index < _doc.rows.Count
                ? _list.index + 1 : _doc.rows.Count;
            _doc.rows.Insert(at, row);
            _list.index = at;
            Bump();
            Repaint();
        }

        // ------------------------------------------------------------------
        // Text / Issues 页签
        // ------------------------------------------------------------------

        void DrawTextTab()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Copy to clipboard", GUILayout.Width(130f)))
                {
                    EditorGUIUtility.systemCopyBuffer = _doc.GenerateText();
                    ShowNotification(new GUIContent("Copied"));
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("read-only preview of what Save will write",
                    EditorStyles.miniLabel);
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextArea(_doc.GenerateText(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        void DrawIssuesTab()
        {
            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("No issues. ✔", MessageType.Info);
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var issue in _issues)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(issue.isError ? "✕" : "⚠",
                        GUILayout.Width(18f));
                    string rowDesc = issue.rowIndex >= 0 && issue.rowIndex < _doc.rows.Count
                        ? RowSummary(_doc.rows[issue.rowIndex]) : "?";
                    GUILayout.Label($"Row {issue.rowIndex + 1} [{rowDesc}]: {issue.message}",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Select", GUILayout.Width(52f)))
                        FocusRow(issue.rowIndex);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        static string RowSummary(VNRow r)
        {
            switch (r.kind)
            {
                case VNRowKind.Say:
                    return string.IsNullOrEmpty(r.speaker) ? "narration" : r.speaker;
                case VNRowKind.Command: return r.keyword;
                default:
                    return r.raw.Length > 14 ? r.raw.Substring(0, 14) + "…" : r.raw;
            }
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
