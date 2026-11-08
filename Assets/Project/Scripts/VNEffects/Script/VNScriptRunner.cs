using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 剧本解释器（P0+P1+P2）：逐条执行 VNScriptParser 解析出的命令。
    ///   - 默认同步执行（等待演出完成），行尾 @ = 异步不等待
    ///   - 台词行：等打字机播完 + 玩家点击/Enter/空格推进（打字中按键 = 催促）
    ///   - P1：label/jump/choice/flag/if 分支
    ///   - P2：F5/F9 打开 20 槽存读档界面、H(或滚轮上滑) 回想、A 自动模式、S 快进
    /// </summary>
    public partial class VNScriptRunner : MonoBehaviour
    {
        [Header("舞台管理器")]
        public VNStage stage;

        [Header("剧本文件（.vn.txt）")]
        public TextAsset script;

        [Header("可通过 chapter <文件名> 切换的章节剧本")]
        public List<TextAsset> chapters = new List<TextAsset>();

        [Header("启动时自动播放")]
        public bool playOnStart = true;

        [Header("Auto / Skip")]
        [Header("自动模式：打字完后的基础等待秒数（另按字数追加）")]
        public float autoDelay = 1.4f;

        [Header("快进时的演出加速倍率（DOTween 全局 timeScale）")]
        public float skipTimeScale = 4f;

        List<VNScriptCommand> _commands;
        readonly Dictionary<string, int> _labels = new Dictionary<string, int>();
        const int MaxCallDepth = 64;
        sealed class VNCallFrame
        {
            public TextAsset returnScript;
            public List<VNScriptCommand> returnCommands;
            public int returnIndex;
            public int sourceLine;
            public Dictionary<string, string> returnParameters;
        }
        readonly List<VNCallFrame> _callStack = new List<VNCallFrame>();
        Dictionary<string, string> _currentParameters = new Dictionary<string, string>();
        TextAsset _entryScript;
        int _index;
        bool _running;
        bool _advance;
        Coroutine _co;

        VNBacklog _backlog;
        VNSaveLoadPanel _saveLoadPanel;
        VNConfigPanel _configPanel;
        VNQuickToolbar _quickToolbar;
        VNQuestLog _questLog;
        VNAiDiaryPanel _diaryPanel;   // 日记本（D 键），首次按键时按需创建
        VNStatsHud _statsHud;
        VNInventory _inventory;
        VNCgGallery _cgGallery;
        VNCalendarHud _calendarHud;
        VNTitleMenu _titleMenu;
        VNSecretPhotoMode _secretPhoto;   // 秘密偷拍模式（右上角图标，解锁后出现）
        bool _secretPhotoActive;          // 拍照期间对话框/HUD 临时隐藏，不碰 _uiHiddenParts
        Coroutine _saveCaptureCo;
        int _saveCaptureToken;
        float _timeScaleBeforeMenu = 1f;
        bool _menuPaused;
        // 隐藏界面（剧本 hideHUD / 右键 / 快捷条按钮）：按部件记录 + 是否锁定。
        // 锁定 = hideHUD keep：玩家点击只推进台词，不会把界面弹回来，
        // 只有剧本写 hideHUD off 才恢复（沉浸演出用）。非锁定 = 一碰就还原的老行为。
        VNUiParts _uiHiddenParts;
        bool _uiHideLocked;
        bool _uiHidden => _uiHiddenParts != VNUiParts.None;
        bool _auto;
        bool _skip;
        bool _waitingAtSay;   // 只有停在台词上时才允许存档
        bool _eventActive;    // 事件模块进行中：输入全部交给模块，禁用快捷键
        VNEventModule _activeEventModule; // 进行中的模块（Stop 时清理用）
        bool _voicePendingForNextSay; // voice 命令一次性绑定到下一句对白的口型
        int _currentSayIndex; // 正在显示的台词命令索引（存档恢复点）
        string _lastSayText = "";

        // 编辑器调试：命令级暂停/单步（不冻结画面动画，只卡在两条命令之间）
        bool _debugPaused;
        bool _debugStepRequested;

        public bool IsRunning => _running;
        public bool IsAuto => _auto;
        public bool IsSkipping => _skip;
        public bool IsInitialized { get; private set; }
        public int CurrentLine =>
            _running && _index > 0 && _index <= _commands.Count ? _commands[_index - 1].line : 0;

        /// <summary>正在播放的剧本文件名（跨文件 jump / chapter 会更新它）</summary>
        public string CurrentScriptName => script != null ? script.name : null;

        [Header("内容总配置（留空 = 自动读 Resources/VNGameConfig）")]
        public VNGameConfig config;

        /// <summary>
        /// 从 VNGameConfig 资产覆盖入口剧本与章节列表。
        /// 章节列表是场景重建后最容易被忘记的一项（不登记 = 所有跨文件跳转直接报错），
        /// 所以生成器会扫 Assets/Scenarios 自动补，这里再兜一层资产覆盖。
        /// </summary>
        void ApplyGameConfig()
        {
            if (config != null) VNGameConfig.SetActive(config);
            var cfg = config != null ? config : VNGameConfig.Active;
            if (cfg == null) return;

            if (cfg.entryScript != null) script = cfg.entryScript;
            VNGameConfig.ApplyList(cfg.chapters, ref chapters);
        }

        void Start()
        {
            ApplyGameConfig();
            _entryScript = script;
            if (_backlog == null)
            {
                _backlog = FindFirstObjectByType<VNBacklog>();
                if (_backlog == null)
                    _backlog = new GameObject("VNBacklog").AddComponent<VNBacklog>();
            }
            if (_questLog == null)
            {
                _questLog = FindFirstObjectByType<VNQuestLog>();
                if (_questLog == null) // 没有登记定义资产也能工作（id 当标题）
                    _questLog = new GameObject("VNQuestLog").AddComponent<VNQuestLog>();
            }
            if (_statsHud == null)
            {
                _statsHud = FindFirstObjectByType<VNStatsHud>();
                if (_statsHud == null) // 没有登记定义资产也能工作（不钳制、无 HUD 条目）
                    _statsHud = new GameObject("VNStatsHud").AddComponent<VNStatsHud>();
            }
            if (_inventory == null)
            {
                _inventory = FindFirstObjectByType<VNInventory>();
                if (_inventory == null) // 没有登记商店资产也能工作（道具 id 当名字）
                    _inventory = new GameObject("VNInventory").AddComponent<VNInventory>();
            }
            if (_cgGallery == null)
            {
                _cgGallery = FindFirstObjectByType<VNCgGallery>();
                if (_cgGallery == null) // 没有 CG 素材也无害（画廊会显示"还没登记 CG"）
                    _cgGallery = new GameObject("VNCgGallery").AddComponent<VNCgGallery>();
            }
            if (_calendarHud == null)
            {
                _calendarHud = FindFirstObjectByType<VNCalendarHud>();
                if (_calendarHud == null) // 「月份」flag 不存在时它自动隐藏，常驻无害
                    _calendarHud = new GameObject("VNCalendarHud").AddComponent<VNCalendarHud>();
            }
            EnsureSaveLoadPanel();
            EnsureQuickToolbar();
            EnsureConfigPanel(); // 启动时应用 PlayerPrefs 中保存的音量、文字速度与显示模式
            if (_secretPhoto == null)
            {
                _secretPhoto = FindFirstObjectByType<VNSecretPhotoMode>();
                if (_secretPhoto == null) // 没解锁时什么都不显示，常驻无害
                    _secretPhoto = new GameObject("VNSecretPhotoMode").AddComponent<VNSecretPhotoMode>();
            }
            _secretPhoto.Initialize(this, stage);
            _titleMenu = FindFirstObjectByType<VNTitleMenu>();
            if (_titleMenu != null && _titleMenu.showOnStart)
            {
                // 标题菜单接管启动：跳过 playOnStart，由「开始/继续」按钮进入播放。
                // 编辑器"从选中行播放"不受影响——它走 ResumeAt，标题层会被自动收起。
                _titleMenu.Initialize(this, stage);
                _titleMenu.Open();
            }
            else if (playOnStart && script != null)
            {
                Play(script);
            }
            IsInitialized = true;
            VNLocale.LanguageChanged -= OnLocaleChanged; // 幂等订阅
            VNLocale.LanguageChanged += OnLocaleChanged;
        }

        /// <summary>语言切换：给已加载的命令重新标注译文（当前显示中的台词到下一句起生效）</summary>
        void OnLocaleChanged()
        {
            if (_commands != null)
                VNScriptLocale.Apply(_commands, script != null ? script.name : null);
        }

        // ------------------------------------------------------------------
        // 播放控制
        // ------------------------------------------------------------------

        public void Play(TextAsset asset)
        {
            if (asset != null)
            {
                script = asset; // 记住剧本资产：翻译表按剧本名查找
                _entryScript = asset;
            }
            Play(asset.text);
        }

        public void Play(string source)
        {
            SetDebugPaused(false); // 正常开局不继承编辑器留下的调试暂停
            Prepare(source);
            ResumeAt(0);
        }

        /// <summary>
        /// 编辑器调试入口：从指定剧本物理行或其后的第一条有效命令开始播放。
        /// 返回实际开始的物理行；找不到可执行命令时返回 -1。
        /// </summary>
        public int PlayFromSourceLine(string source, int sourceLine) =>
            PlayFromSourceLine(source, sourceLine, false);

        /// <summary>
        /// 编辑器热重载：把当前调试的剧本资产告诉 Runner。
        /// 只换 script 引用（翻译查表、chapter/跨文件 jump 的"当前文件"都按它算），
        /// 不重新加载命令——命令由紧随其后的 PlayFromSourceLine 用未保存文本装载。
        /// </summary>
        public void SetDebugScript(TextAsset asset)
        {
            if (asset != null) script = asset;
        }

        // ------------------------------------------------------------------
        // 编辑器调试：命令级暂停 / 单步
        // ------------------------------------------------------------------

        /// <summary>命令级暂停中（卡在两条命令之间，进行中的补间/打字机不受影响）</summary>
        public bool IsDebugPaused => _debugPaused;

        public void SetDebugPaused(bool paused)
        {
            _debugPaused = paused;
            if (!paused) _debugStepRequested = false;
        }

        /// <summary>放行一条命令后重新暂停（未暂停时先进入暂停态）</summary>
        public void RequestDebugStep()
        {
            _debugPaused = true;
            _debugStepRequested = true;
        }

        public int PlayFromSourceLine(string source, int sourceLine, bool rebuildState)
        {
            Prepare(source);
            int start = -1;
            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].line < Mathf.Max(1, sourceLine)) continue;
                start = i;
                break;
            }

            if (start < 0)
            {
                Debug.LogError($"[VNScript] 第 {sourceLine} 行之后没有可播放的命令");
                return -1;
            }

            int actualLine = _commands[start].line;
            if (rebuildState)
            {
                // 重放期间挂起统计与任务引擎：重放写的 flag 不是「新成绩」，
                // 否则从选中行播放一次，@累计 / @次数 就被整段剧本再刷一遍。
                // 注意重建出来的状态不含玩家历史领取的奖励——领取是玩家行为、不在剧本里，
                // 无法重放，这是已知限制（见 vn-debug）。
                VNTracker.Suspended = true;
                VNQuestEngine.Suspended = true;
                try { RebuildStateBefore(start); }
                finally
                {
                    VNTracker.Suspended = false;
                    VNQuestEngine.Suspended = false;
                }
                VNQuestEngine.RecalculateSilently();
            }
            ResumeAt(start);
            Debug.Log($"[VNScript] 调试：从第 {actualLine} 行开始播放" +
                      (rebuildState ? "（已重建前置状态）" : "（直接跳转）"));
            return actualLine;
        }

        // ------------------------------------------------------------------
        // 内嵌剧本行（事件模块在交互中途插播演出用）
        // ------------------------------------------------------------------

        /// <summary>
        /// 内嵌剧本行里**禁用**的命令。这些命令会改控制流或存档状态，而调用方
        /// （事件模块）此刻正被主协程 yield 等待着 —— 让它们跑起来会把 Runner
        /// 的 _index / _callStack / 存档状态搅乱，症状是事件结束后剧本跳到莫名其妙的地方。
        /// 演出类命令（say / voice / se / fx / mark / camseq / weather …）一律放行。
        /// </summary>
        static readonly HashSet<string> InlineBlockedKeywords = new HashSet<string>
        {
            "jump", "choice", "call", "return", "label", "event",
            "save", "load", "chapter", "endgame",
        };

        /// <summary>
        /// 执行一小段剧本行（供事件模块在交互中途插播演出）。
        /// 逐条走与主循环同一个 <see cref="Dispatch"/>，所以命令语义永远一致；
        /// 行尾 @ 的异步语义也照旧。控制流命令被 <see cref="InlineBlockedKeywords"/> 挡掉。
        /// </summary>
        public IEnumerator RunInlineCo(string lines)
        {
            if (string.IsNullOrWhiteSpace(lines)) yield break;

            List<VNScriptCommand> commands;
            try { commands = VNScriptParser.Parse(lines); }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNScript] 内嵌剧本行解析失败：{e.Message}");
                yield break;
            }

            foreach (var raw in commands)
            {
                if (raw == null) continue;
                if (InlineBlockedKeywords.Contains(raw.keyword))
                {
                    Debug.LogWarning($"[VNScript] 内嵌剧本行里不能用「{raw.keyword}」" +
                                     "（会打乱正在等待模块的剧本流程），该行已跳过");
                    continue;
                }

                var cmd = ResolveParameters(raw);
                IEnumerator co = null;
                try { co = Dispatch(cmd); }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VNScript] 内嵌剧本行执行出错（{cmd.keyword}）：{e.Message}");
                }
                if (co == null) continue;
                if (cmd.isAsync) StartCoroutine(co);
                else yield return StartCoroutine(co);
            }
        }

        void RebuildStateBefore(int exclusiveIndex)
        {
            if (stage == null)
            {
                Debug.LogError("[VNScript] 无法重建状态：VNScriptRunner.stage 未设置");
                return;
            }

            var snapshot = new VNSaveData
            {
                weather = VNWeather.None.ToString(),
                mood = VNMood.Neutral.ToString(),
                scrollSpeed = VNBackgroundScroll.DefaultSpeed,
                scrollDir = VNBackgroundScroll.DefaultDirection,
                scrollMode = VNScrollMode.Mirror.ToString(),
            };
            // Ken Burns 默认开启：先种入再按剧本重放，重建结果才与真实运行一致
            snapshot.fxOn.Add("kenburns");
            var characters = new Dictionary<string, VNSaveData.CharSave>();
            var loopingSe = new Dictionary<string, float>(); // id → 剧本 vol 参数
            var volumes = new Dictionary<string, float>();
            string focus = null;
            VNScriptCommand lastCameraCut = null;
            bool hasBranching = false;
            bool autoLetterbox = false; // 回忆自动黑边的重放状态
            bool autoRetro = false;     // 回忆自动胶片/梦境自动 CRT 的重放状态

            VNFlags.Clear();
            for (int i = 0; i < exclusiveIndex && i < _commands.Count; i++)
            {
                VNScriptCommand cmd = _commands[i];
                switch (cmd.keyword)
                {
                    case "bg":
                        snapshot.backgroundId = cmd.Arg(0);
                        break;
                    case "cg":
                        if (cmd.Arg(0) == "off") snapshot.cgId = null;
                        else
                        {
                            snapshot.cgId = cmd.Arg(0);
                            snapshot.cgKeepChars = cmd.Kw("chars") == "keep";
                            snapshot.cgKeepFx = cmd.Kw("fx") == "keep";
                            snapshot.cgFit = cmd.Kw("fit") ?? "";
                        }
                        break;
                    case "liquid":
                    {
                        // 只重放会一直持续下去的开关；splash 是一次性演出、dry 是一次性擦除，
                        // 两者都不属于"跳到这一行时画面应该是什么样"的状态
                        var la = ParseLiquidArgs(cmd);
                        var lsave = snapshot.liquid;
                        switch (cmd.Arg(0, "splash"))
                        {
                            case "spray":
                                lsave.sprayOn = la.on;
                                lsave.sprayType = la.type;
                                lsave.sprayX = la.x;
                                lsave.sprayY = la.y;
                                lsave.sprayPower = la.power;
                                lsave.sprayDirSet = !float.IsNaN(la.dir); // NaN = 朝镜头
                                lsave.sprayDir = lsave.sprayDirSet ? la.dir : 0f;
                                lsave.spraySpread = la.spread;
                                lsave.sprayRate = la.rate;
                                lsave.sprayScreen = la.screen;
                                break;
                            case "click":
                                lsave.clickOn = la.on;
                                lsave.clickType = la.type;
                                lsave.clickPower = la.power;
                                lsave.clickScreen = la.screen;
                                break;
                            case "wet":
                                lsave.wetOn = la.on;
                                lsave.wetType = la.type;
                                lsave.wetAmount = la.amount;
                                break;
                            case "cover":
                                lsave.cover = la.on;
                                break;
                        }
                        break;
                    }
                    case "weather":
                    {
                        snapshot.weather = cmd.Arg(0, VNWeather.None.ToString());
                        // 覆盖参数与运行时一致：换天气时整组重置，只保留本行显式写了的
                        snapshot.weatherDensity = cmd.KwF("density", 0f);
                        snapshot.weatherSpeed = cmd.KwF("speed", 0f);
                        snapshot.weatherSize = cmd.KwF("size", 0f);
                        float w = cmd.KwF("wind", float.NaN);
                        snapshot.weatherWindSet = !float.IsNaN(w);
                        snapshot.weatherWind = snapshot.weatherWindSet ? w : 0f;
                        break;
                    }
                    case "bgscroll":
                    {
                        // 参数留空 = 沿用上一次的设定，重建时也要照这个语义累积，
                        // 不能每条都重置成默认值（否则 bgscroll on speed:120 后面
                        // 一句 bgscroll off / on 就把速度打回 80）
                        snapshot.scrollOn = cmd.Arg(0, "on") != "off";
                        if (cmd.kwargs.ContainsKey("speed"))
                            snapshot.scrollSpeed = cmd.KwF("speed", VNBackgroundScroll.DefaultSpeed);
                        var rDir = VNBackgroundScroll.ParseDirection(cmd.Kw("dir"));
                        if (rDir.HasValue) snapshot.scrollDir = rDir.Value;
                        var rMode = VNBackgroundScroll.ParseMode(cmd.Kw("mode"));
                        if (rMode.HasValue) snapshot.scrollMode = rMode.Value.ToString();
                        break;
                    }
                    case "mood":
                    {
                        snapshot.mood = cmd.Arg(0, VNMood.Neutral.ToString());
                        var moodValue = VNScriptParser.ParseEnum(
                            snapshot.mood, VNMood.Neutral, 0);
                        // 回忆自动黑边的静默重放（与运行时 VNStage.SetMood 逻辑一致）
                        if (stage.autoMemoryLetterbox)
                        {
                            bool isMemory = moodValue == VNMood.Memory;
                            if (isMemory && !snapshot.fxOn.Contains("letterbox"))
                            {
                                snapshot.fxOn.Add("letterbox");
                                autoLetterbox = true;
                            }
                            else if (!isMemory && autoLetterbox)
                            {
                                snapshot.fxOn.Remove("letterbox");
                                autoLetterbox = false;
                            }
                        }
                        // 回忆自动胶片 / 梦境自动 CRT 的静默重放
                        if (stage.autoMoodRetroFilter)
                        {
                            bool hasRetro = snapshot.fxOn.Contains("filmgrain") ||
                                            snapshot.fxOn.Contains("crt");
                            if (moodValue == VNMood.Memory && !hasRetro)
                            {
                                snapshot.fxOn.Add("filmgrain");
                                autoRetro = true;
                            }
                            else if (moodValue == VNMood.Dream && !hasRetro)
                            {
                                snapshot.fxOn.Add("crt");
                                autoRetro = true;
                            }
                            else if (moodValue != VNMood.Memory &&
                                     moodValue != VNMood.Dream && autoRetro)
                            {
                                snapshot.fxOn.Remove("filmgrain");
                                snapshot.fxOn.Remove("crt");
                                autoRetro = false;
                            }
                        }
                        break;
                    }
                    case "letterbox":
                        autoLetterbox = false;
                        if (cmd.Arg(0, "on") == "off") snapshot.fxOn.Remove("letterbox");
                        else if (!snapshot.fxOn.Contains("letterbox"))
                            snapshot.fxOn.Add("letterbox");
                        break;
                    case "reset":
                        if (cmd.Arg(0) == "effects" || cmd.Arg(0) == "all")
                        {
                            snapshot.weather = VNWeather.None.ToString();
                            snapshot.weatherDensity = 0f;
                            snapshot.weatherSpeed = 0f;
                            snapshot.weatherSize = 0f;
                            snapshot.weatherWindSet = false;
                            snapshot.weatherWind = 0f;
                            snapshot.mood = VNMood.Neutral.ToString();
                            snapshot.liquid = new VNSaveData.LiquidSave(); // 与 ResetLiquid 一致
                            snapshot.fxOn.Clear();
                            snapshot.fxOn.Add("kenburns"); // 重置回默认开（与 ResetEffects 一致）
                            focus = null;
                            autoLetterbox = false;
                            autoRetro = false;
                        }
                        break;
                    case "portrait":
                        snapshot.portraitOff = cmd.Arg(0, "on") == "off";
                        break;
                    case "hideHUD":
                    {
                        // 只重放锁定隐藏（keep）：普通隐藏是「玩家一碰就还原」的瞬态，
                        // 从选中行播放时重建它只会让人以为界面坏了。
                        ParseHideHudArgs(cmd, out var uiParts, out bool uiHide, out bool uiLock);
                        if (uiParts == VNUiParts.None) uiParts = VNUiParts.All;
                        var current = VNUiPartsUtil.FromToken(snapshot.uiHidden);
                        if (!uiHide) current &= ~uiParts;
                        else if (uiLock) current |= uiParts;
                        snapshot.uiHidden = VNUiPartsUtil.ToToken(current);
                        break;
                    }
                    case "ui":
                    {
                        // 皮肤切换是持续状态：记入快照（default = 空 = 程序化默认）
                        string skinId = cmd.Arg(1, "default");
                        if (skinId == "default") skinId = null;
                        if (cmd.Arg(0) == "dialogue") snapshot.dialogueSkin = skinId;
                        else if (cmd.Arg(0) == "choice") snapshot.choiceSkin = skinId;
                        else if (cmd.Arg(0) == "name") snapshot.nameplateStyle = skinId;
                        break;
                    }
                    case "show":
                        RebuildShowState(characters, cmd);
                        break;
                    case "hide":
                        characters.Remove(cmd.Arg(0));
                        break;
                    case "move":
                        RebuildMoveState(characters, cmd);
                        break;
                    case "mark":
                        RebuildMarkState(characters, cmd);
                        break;
                    case "overlay":
                        RebuildOverlayState(characters, cmd);
                        break;
                    case "say":
                        if (!string.IsNullOrEmpty(cmd.expression) &&
                            characters.TryGetValue(cmd.speaker, out var speaking))
                            speaking.expr = cmd.expression;
                        // SNS 打开期间的台词就是一条气泡消息，必须一并重建
                        if (snapshot.snsOpen) ReplaySnsSay(snapshot, cmd);
                        break;
                    case "bgm":
                        bool bgmStop = cmd.Arg(0, "play") == "stop";
                        snapshot.bgm = bgmStop ? null : cmd.Arg(1);
                        snapshot.bgmVol = bgmStop ? 1f : cmd.KwF("vol", 1f);
                        break;
                    case "se":
                        if (cmd.Arg(0) == "stop") loopingSe.Remove(cmd.Arg(1));
                        else if (cmd.args.Contains("loop"))
                            loopingSe[cmd.Arg(0)] = cmd.KwF("vol", 1f);
                        break;
                    case "volume":
                        volumes[cmd.Arg(0, "bgm")] = cmd.ArgF(1, 1f);
                        break;
                    case "fx":
                    {
                        string name = cmd.Arg(0);
                        string value = cmd.Arg(1);
                        if (name == "focus") focus = value == "off" ? null : value;
                        // 一次性演出（shockwave / speedlines burst）不属于持续状态，重建时跳过
                        else if (name == "shockwave" || value == "burst") { }
                        else if (value == "off") snapshot.fxOn.Remove(name);
                        else if (!snapshot.fxOn.Contains(name)) snapshot.fxOn.Add(name);
                        // 复古滤镜互斥 + 手动接管（与运行时 VNStage.Fx 逻辑一致）
                        if (name == "filmgrain" || name == "crt")
                        {
                            autoRetro = false;
                            if (value != "off")
                                snapshot.fxOn.Remove(name == "filmgrain" ? "crt" : "filmgrain");
                        }
                        break;
                    }
                    case "flag": // 静默重放（rand 会重新掷骰，见 ApplyFlagCommand 注释）
                        ApplyFlagCommand(cmd, true);
                        break;
                    case "quest": // 静默重放（写状态不弹 Toast）
                        _questLog?.Apply(cmd.Arg(0, "start"), cmd.Arg(1),
                            (int)cmd.ArgF(2, 0f), true, cmd.line);
                        break;
                    case "stat": // 静默重放（钳制照做，不弹 Toast）
                        _statsHud?.Apply(cmd.Arg(0), cmd.Arg(1), true, cmd.line);
                        break;
                    case "time": // 静默重放（月份/剩余月数/行动力回满照做，不弹 Toast）
                        ApplyTimeCommand(cmd, true);
                        break;
                    case "camcut":
                    case "camto":
                        lastCameraCut = cmd;
                        break;
                    case "camera":
                    case "camseq":
                        lastCameraCut = null; // 动画路径状态不做推断，回到默认镜头
                        break;
                    case "sns":
                        // SNS 会话与消息列表静默重建（只填数据，不播弹出动画）；
                        // sns reply 的玩家选择无法推断，与 choice 同处理
                        if (cmd.Arg(0) == "reply") hasBranching = true;
                        else ReplaySnsCommand(snapshot, cmd);
                        break;
                    case "choice":
                    case "jump":
                    case "call":
                    case "return":
                    case "if":
                    case "event": // 事件结果无法推断，不重放，同分支处理
                        hasBranching = true;
                        break;
                }
            }

            foreach (var character in characters.Values)
                snapshot.characters.Add(character);

            stage.RestoreSnapshot(snapshot, true);
            RestoreUiHidden(snapshot);
            if (stage.vnAudio != null)
            {
                foreach (var volume in volumes)
                    stage.vnAudio.SetVolume(volume.Key, volume.Value);
                foreach (var se in loopingSe)
                    stage.vnAudio.PlaySe(se.Key, true, se.Value);
            }
            if (!string.IsNullOrEmpty(focus)) stage.Fx("focus", focus);
            RestoreDebugCamera(lastCameraCut);

            if (hasBranching)
                Debug.LogWarning("[VNScript] 前置状态包含 choice/jump/if/event；调试重建按文件顺序处理，" +
                                 "不会推断之前的玩家选择路径与事件结果");
        }

    }
}
