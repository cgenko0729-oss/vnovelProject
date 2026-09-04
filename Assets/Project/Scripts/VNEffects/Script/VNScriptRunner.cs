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
    public class VNScriptRunner : MonoBehaviour
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

        // ------------------------------------------------------------------
        // SNS 静默重建（调试「从选中行播放」用；读档直接走存档里的消息列表）
        // ------------------------------------------------------------------

        /// <summary>把 sns 命令按运行时语义写进快照（只填数据，不建 UI 不播动画）</summary>
        static void ReplaySnsCommand(VNSaveData snapshot, VNScriptCommand cmd)
        {
            switch (cmd.Arg(0, "").ToLower())
            {
                case "open":
                    snapshot.snsOpen = true;
                    snapshot.snsPeerId = cmd.Arg(1);
                    snapshot.snsSessionId = cmd.Kw("id", cmd.Arg(1));
                    snapshot.snsTitle = cmd.Kw("title");
                    snapshot.snsPlayerAlias = cmd.Kw("me");
                    snapshot.snsMessages.Clear();
                    break;

                case "close":
                    snapshot.snsOpen = false;
                    snapshot.snsMessages.Clear();
                    break;

                case "voice":
                    if (!snapshot.snsOpen) break;
                    AddSnsMessage(snapshot, cmd.Arg(1), VNSnsMessage.KindVoice,
                        cmd.Kw("text"), cmd.Arg(2), false);
                    break;

                case "image":
                    if (!snapshot.snsOpen) break;
                    AddSnsMessage(snapshot, cmd.Arg(1), VNSnsMessage.KindImage,
                        null, cmd.Arg(2), cmd.Kw("unlock", "yes") != "no");
                    break;

                case "time":
                case "system":
                    if (!snapshot.snsOpen) break;
                    AddSnsMessage(snapshot, "",
                        cmd.Arg(0) == "time" ? VNSnsMessage.KindTime : VNSnsMessage.KindSystem,
                        JoinArgs(cmd, 1), null, false);
                    break;

                case "read":
                    for (int i = snapshot.snsMessages.Count - 1; i >= 0; i--)
                    {
                        if (snapshot.snsMessages[i].sender != VNSnsView.PlayerSender) continue;
                        snapshot.snsMessages[i].read = true;
                        break;
                    }
                    break;

                // typing 是纯演出，不产生消息；reply 的玩家选择在外层按分支处理
            }
        }

        /// <summary>SNS 打开期间的台词行 → 一条气泡消息</summary>
        static void ReplaySnsSay(VNSaveData snapshot, VNScriptCommand cmd)
        {
            string text = VNScriptLocale.TextOf(cmd);
            if (string.IsNullOrEmpty(cmd.speaker))
                AddSnsMessage(snapshot, "", VNSnsMessage.KindSystem, text, null, false);
            else
                AddSnsMessage(snapshot, cmd.speaker, VNSnsMessage.KindText, text, null, false);
        }

        static void AddSnsMessage(VNSaveData snapshot, string sender, string kind,
            string text, string assetId, bool unlock)
        {
            snapshot.snsMessages.Add(new VNSnsMessage
            {
                id = snapshot.snsMessages.Count + 1,
                sessionId = snapshot.snsSessionId,
                sender = VNSnsView.IsPlayerSender(sender, snapshot.snsPlayerAlias)
                    ? VNSnsView.PlayerSender : sender,
                kind = kind,
                text = text,
                assetId = assetId,
                unlock = unlock,
            });
        }

        /// <summary>把第 from 个位置参数起拼回原始自由文本（sns time / sns system 用）</summary>
        static string JoinArgs(VNScriptCommand cmd, int from)
        {
            if (cmd.args.Count <= from) return "";
            return string.Join(" ", cmd.args.GetRange(from, cmd.args.Count - from));
        }

        void RebuildShowState(Dictionary<string, VNSaveData.CharSave> characters,
            VNScriptCommand cmd)
        {
            string id = cmd.Arg(0);
            if (string.IsNullOrEmpty(id)) return;
            string at = cmd.Kw("at");
            VNSaveData.CharSave existing = null;
            bool keepPosition = string.IsNullOrEmpty(at) &&
                characters.TryGetValue(id, out existing);
            float x;
            if (keepPosition)
                x = existing.x;
            else
                x = DebugSlotX(string.IsNullOrEmpty(at) ? "center" : at);
            var def = stage.characters.Find(character => character != null && character.id == id);
            if (def != null && !keepPosition)
                x += def.positionOffset.x;
            // 已在场的角色重播 show 不会清掉常驻漫符（与运行时 VNStage.Show 一致），所以要带过来
            characters.TryGetValue(id, out var previous);
            characters[id] = new VNSaveData.CharSave
            {
                id = id, x = x, expr = cmd.Kw("expr"),
                marks = previous != null ? previous.marks : null,
                // 日常向预设登场的角色不开周期扫光，重建时也要一致
                casualEntrance = VNEntranceAnimator.IsCasual(
                    VNScriptParser.ParseEnum(cmd.Kw("with"), VNEntrancePreset.Crossfade, 0)),
            };
        }

        /// <summary>
        /// 漫符的静默重放：只有 keep 符号是持续状态需要重建，
        /// 一次性符号播完就没了，重建时直接忽略。
        /// </summary>
        /// <summary>
        /// overlay 命令的静默重放（编辑器「从选中行播放」重建前置状态用）。
        /// 直接改快照串，格式与 VNCharacterOverlay.Serialize 一致。
        /// </summary>
        void RebuildOverlayState(Dictionary<string, VNSaveData.CharSave> characters,
            VNScriptCommand cmd)
        {
            if (!characters.TryGetValue(cmd.Arg(0), out var character)) return;

            string layer = cmd.Arg(1);
            if (string.IsNullOrEmpty(layer) || layer == "clear" || layer == "off")
            {
                character.overlays = null;
                return;
            }

            float strength = Mathf.Clamp01(cmd.ArgF(2, 1f));
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(character.overlays))
                foreach (var p in character.overlays.Split('|'))
                {
                    int eq = p.IndexOf('=');
                    if (eq > 0 && p.Substring(0, eq) != layer) parts.Add(p);
                }
            if (strength > 0.001f)
                parts.Add(layer + "=" + strength.ToString("0.###"));

            character.overlays = parts.Count > 0 ? string.Join("|", parts) : null;
        }

        void RebuildMarkState(Dictionary<string, VNSaveData.CharSave> characters,
            VNScriptCommand cmd)
        {
            if (!characters.TryGetValue(cmd.Arg(0), out var character)) return;

            string name = cmd.Arg(1);
            string mode = cmd.Arg(2);
            var kept = new List<string>();
            if (!string.IsNullOrEmpty(character.marks))
                kept.AddRange(character.marks.Split(','));

            if (string.IsNullOrEmpty(name) || name == "clear")
            {
                character.marks = null;
                return;
            }
            if (!VNCharacterMarks.TryParse(name, out var kind)) return;

            string canonical = VNCharacterMarks.NameOf(kind);
            kept.Remove(canonical);
            if (mode == "keep") kept.Add(canonical);
            character.marks = kept.Count > 0 ? string.Join(",", kept) : null;
        }

        void RebuildMoveState(Dictionary<string, VNSaveData.CharSave> characters,
            VNScriptCommand cmd)
        {
            if (!characters.TryGetValue(cmd.Arg(0), out var character)) return;
            float x = DebugSlotX(cmd.Arg(1, "center"));
            var def = stage.characters.Find(item => item != null && item.id == character.id);
            if (def != null) x += def.positionOffset.x;
            character.x = x;
        }

        static float DebugSlotX(string at)
        {
            switch (at)
            {
                case "left": return -380f;
                case "right": return 380f;
                case "center": return 0f;
                default: return float.TryParse(at, out float x) ? x : 0f;
            }
        }

        /// <summary>
        /// flag 命令共用实现（运行时执行与调试重建静默重放）。
        ///   flag 名字 / flag 名字 3 / flag 名字 +1 / flag 名字 rand:1-100
        /// rand:min-max = 闭区间随机取整写入。注意：调试重建重放时会重新掷骰，
        /// 重建出的分支状态可能与实际游玩不同（与 event 结果不重放同类限制）。
        /// </summary>
        static void ApplyFlagCommand(VNScriptCommand cmd, bool silent)
        {
            string name = cmd.Arg(0);
            if (string.IsNullOrEmpty(name))
            {
                if (!silent) Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：flag 缺少名字");
                return;
            }

            string rand = cmd.Kw("rand");
            if (!string.IsNullOrEmpty(rand))
            {
                if (TryParseRandRange(rand, out int lo, out int hi))
                    VNFlags.Set(name, Random.Range(lo, hi + 1));
                else if (!silent)
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：rand 区间「{rand}」" +
                                     "应为「min-max」（如 1-100）");
                return;
            }

            string value = cmd.Arg(1);
            if (string.IsNullOrEmpty(value)) VNFlags.Apply(name);
            else if (value.StartsWith("+") || value.StartsWith("-"))
                VNFlags.Apply(name + value);
            else if (int.TryParse(value, out int parsed)) VNFlags.Set(name, parsed);
            else if (!silent)
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：flag 值「{value}」无法识别");
        }

        /// <summary>解析 rand 区间串「min-max」（允许负数，如 -5-5）</summary>
        static bool TryParseRandRange(string s, out int lo, out int hi)
        {
            lo = hi = 0;
            int sep = s.IndexOf('-', 1); // 从第 2 个字符起找分隔符，兼容负数下限
            if (sep <= 0 || sep >= s.Length - 1) return false;
            if (!int.TryParse(s.Substring(0, sep), out lo)) return false;
            if (!int.TryParse(s.Substring(sep + 1), out hi)) return false;
            if (hi < lo) { int t = lo; lo = hi; hi = t; }
            return true;
        }

        void RestoreDebugCamera(VNScriptCommand command)
        {
            if (stage.vnCamera == null) return;
            if (command == null)
            {
                stage.vnCamera.SnapReset();
                return;
            }

            var point = stage.ResolveCamPoint(command.Arg(0), command.line);
            if (!point.HasValue) return;
            stage.vnCamera.Cut(point.Value, command.ArgF(1, 1.5f));
        }

        /// <summary>解析剧本并预扫描 label 表（允许向前跳转）</summary>
        void Prepare(string source)
        {
            Stop();
            _callStack.Clear();
            _currentParameters.Clear();
            LoadCommands(source);
        }

        void LoadCommands(string source)
        {
            _commands = VNScriptParser.Parse(source);

            // 剧本全文预热进 TMP 动态字体图集：光栅化成本挪到加载期，台词零卡顿
            VNFont.Prewarm(source);

            // 非中文语言：给台词/选项标注译文（缺译回退中文，命令流不变）
            VNScriptLocale.Apply(_commands, script != null ? script.name : null);

            BuildLabelMap(_commands, _labels);
        }

        static void BuildLabelMap(
            List<VNScriptCommand> commands,
            Dictionary<string, int> destination)
        {
            destination.Clear();
            for (int i = 0; i < commands.Count; i++)
            {
                if (commands[i].keyword != "label") continue;
                string name = commands[i].Arg(0);
                if (string.IsNullOrEmpty(name))
                    Debug.LogError($"[VNScript] 第 {commands[i].line} 行：label 缺少名字");
                else if (destination.ContainsKey(name))
                    Debug.LogError($"[VNScript] 第 {commands[i].line} 行：label「{name}」重复定义");
                else
                    destination[name] = i;
            }
        }

        bool SwitchChapter(string chapterName, int fromLine)
        {
            TextAsset target = FindChapter(chapterName);

            if (target == null)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：找不到章节「{chapterName}」，请在 VNScriptRunner 的 Chapters 列表中登记该剧本");
                return false;
            }

            script = target;
            _callStack.Clear(); // chapter 是尾调用式流程切换，不保留子程序返回点
            _currentParameters.Clear();
            LoadCommands(target.text);
            _index = 0;
            _currentSayIndex = 0;
            Debug.Log($"[VNScript] 已切换到章节「{target.name}」");
            return true;
        }

        static string NormalizeChapterName(string name)
        {
            return VNStoryAddress.NormalizeFile(name);
        }

        /// <summary>
        /// 按 id 找过场资产（interlude 命令用）。库只在 VNGameConfig 里，
        /// 不像角色/背景那样在场景组件上也有一份——过场是纯资产数据，没有场景侧配置。
        /// </summary>
        static VNInterludeDef FindInterlude(string id, int line)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[VNScript] 第 {line} 行：interlude 缺少过场 id");
                return null;
            }
            var config = VNGameConfig.Active;
            if (config != null && config.interludes != null)
            {
                foreach (var def in config.interludes)
                {
                    if (def == null) continue;
                    // id 留空时按资产文件名认，和 chapter 的容错保持一致
                    string key = string.IsNullOrEmpty(def.id) ? def.name : def.id;
                    if (key == id) return def;
                }
            }
            Debug.LogError($"[VNScript] 第 {line} 行：找不到过场「{id}」，" +
                           "请在 VNGameConfig 的「过场库」里登记该 VNInterludeDef 资产");
            return null;
        }

        /// <summary>按 id 找教程资产（tutorial 命令用）。同 interlude：库只在 VNGameConfig 里。</summary>
        static VNTutorialDef FindTutorial(string id, int line)
        {
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogError($"[VNScript] 第 {line} 行：tutorial 缺少教程 id");
                return null;
            }
            var def = VNTutorialPlayer.Find(id);
            if (def == null)
                Debug.LogError($"[VNScript] 第 {line} 行：找不到教程「{id}」，" +
                               "请在 VNGameConfig 的「教程库」里登记该 VNTutorialDef 资产");
            return def;
        }

        TextAsset FindChapter(string chapterName)
        {
            string wanted = NormalizeChapterName(chapterName);
            if (script != null && NormalizeChapterName(script.name) == wanted)
                return script;
            if (_entryScript != null && NormalizeChapterName(_entryScript.name) == wanted)
                return _entryScript;
            foreach (var chapter in chapters)
                if (chapter != null && NormalizeChapterName(chapter.name) == wanted)
                    return chapter;
            return null;
        }

        /// <summary>从指定命令索引开始（读档恢复用）</summary>
        public void ResumeAt(int index)
        {
            if (_commands == null)
            {
                if (script == null)
                {
                    Debug.LogError("[VNScript] 没有剧本可播放");
                    return;
                }
                Prepare(script.text);
            }
            Stop();
            _titleMenu?.NotifyGameplayStarted(); // 任何入口开始播放都顺手收起标题层
            _index = Mathf.Clamp(index, 0, _commands.Count);
            _currentSayIndex = _index;
            _co = StartCoroutine(Run());
        }

        /// <summary>标题菜单「开始游戏」：清空全部 flag，从入口剧本头开始。</summary>
        public void StartNewGame()
        {
            var entry = _entryScript != null ? _entryScript : script;
            if (entry == null)
            {
                Debug.LogError("[VNScript] 没有入口剧本可播放（检查 VNScriptRunner.script / VNGameConfig.entryScript）");
                return;
            }
            VNFlags.Clear(); // 新游戏 = 干净的世界状态（call 栈由 Play → Prepare 清）
            Play(entry);
        }

        public void Stop()
        {
            if (_co != null) StopCoroutine(_co);
            _co = null;
            _running = false;
            _waitingAtSay = false;
            _voicePendingForNextSay = false;
            CleanupActiveEvent();
            _secretPhoto?.ForceClose(); // 读档 / 重播时不能把镜头留在玩家推的位置
            stage?.StopSpeaking();
        }

        /// <summary>剧本中断时清理进行中的事件模块（正常结束由 EventCo 自己收尾）</summary>
        void CleanupActiveEvent()
        {
            // 教程要先收：它可能是被模块启动的，模块一销毁就没人来解除暂停了，
            // 而暂停不解除 = 整个游戏永久卡死（比暗幕留在屏幕上严重得多）
            (stage != null && stage.tutorial != null
                ? stage.tutorial : VNTutorialPlayer.Instance)?.CancelImmediate();
            VNPause.ReleaseAll(); // 兜底：任何漏掉的持有者都在这里被清掉

            if (_activeEventModule != null)
            {
                _activeEventModule.CancelForDebug();
                Destroy(_activeEventModule.gameObject);
                _activeEventModule = null;
            }
            if (_eventActive)
            {
                _eventActive = false;
                stage?.dialogue?.Show();
            }
        }

        /// <summary>
        /// time 命令（养成日程）：状态全在 VNFlags（月份 / 剩余月数），日历 HUD 自动刷新。
        ///   time set 9 [remain:36]                进入养成模式：设月份与剩余月数
        ///   time pass [months:N] [refill:off|名]  过月：月份 +N（1~12 循环）、剩余月数 -N、
        ///                                         行动力回满（refill:off 关闭，或指定其他属性）
        /// silent = 调试重建静默重放（不弹 Toast）。
        /// </summary>
        void ApplyTimeCommand(VNScriptCommand cmd, bool silent)
        {
            string op = cmd.Arg(0, "pass");
            switch (op)
            {
                case "set":
                {
                    int month = Mathf.Clamp((int)cmd.ArgF(1, 1f), 1, 12);
                    VNFlags.Set(VNCalendarHud.MonthFlag, month);
                    if (cmd.Kw("remain") != null)
                        VNFlags.Set(VNCalendarHud.RemainFlag,
                            Mathf.Max(0, (int)cmd.KwF("remain", 0f)));
                    // 「月序」只初始化不重置：中途再 time set 一次不该让限时任务的接取月错位
                    if (!VNFlags.All.ContainsKey(VNQuestEngine.MonthSerialFlag))
                        VNFlags.Set(VNQuestEngine.MonthSerialFlag, 0);
                    break;
                }

                case "pass":
                {
                    int months = Mathf.Max(1, (int)cmd.KwF("months", 1f));
                    int month = VNFlags.Get(VNCalendarHud.MonthFlag);
                    if (month <= 0) month = 1;
                    month = (month - 1 + months) % 12 + 1;
                    VNFlags.Set(VNCalendarHud.MonthFlag, month);

                    // 单调递增的「月序」：任务限时与日常冷却的唯一时间基准。
                    // 日历「月份」在 1~12 里循环，11 月接的 3 个月期限任务到期该是次年 2 月，
                    // 拿「月份>=14」去判永远不成立——所以另记一份绝对月计数。
                    VNFlags.Add(VNQuestEngine.MonthSerialFlag, months);

                    if (VNFlags.All.ContainsKey(VNCalendarHud.RemainFlag))
                        VNFlags.Set(VNCalendarHud.RemainFlag,
                            Mathf.Max(0, VNFlags.Get(VNCalendarHud.RemainFlag) - months));

                    // 行动力回满（有属性定义才知道满值是多少；refill:off 关闭）
                    string refill = cmd.Kw("refill", "行动力");
                    if (refill != "off" && _statsHud != null)
                    {
                        var def = _statsHud.Find(refill);
                        if (def != null && def.useClamp)
                            VNFlags.Set(def.id, def.maxValue);
                    }

                    if (!silent) VNToast.Show(VNLocale.T("time.toastMonth", month), 2f);
                    break;
                }

                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知 time 操作「{op}」" +
                                     "（set/pass）");
                    break;
            }
        }

        bool JumpTo(string address, int fromLine)
        {
            if (!VNStoryAddress.TryParse(address, out string file, out string label,
                    out string addressError))
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：{addressError}");
                return false;
            }

            if (file == null)
            {
                if (_labels.TryGetValue(label, out int localIndex))
                {
                    _index = localIndex;
                    return true;
                }
                Debug.LogError($"[VNScript] 第 {fromLine} 行：跳转目标 label「{label}」不存在");
                return false;
            }

            TextAsset target = FindChapter(file);
            if (target == null)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：找不到剧本「{file}」，" +
                               "请在 VNScriptRunner 的 Chapters 列表中登记该剧本");
                return false;
            }

            // 先在临时结构中解析并确认 label；失败时不改变当前文件、命令索引或 label 表。
            var targetCommands = VNScriptParser.Parse(target.text);
            var targetLabels = new Dictionary<string, int>();
            BuildLabelMap(targetCommands, targetLabels);
            if (!targetLabels.TryGetValue(label, out int targetIndex))
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：剧本「{target.name}」中不存在 label「{label}」");
                return false;
            }

            VNFont.Prewarm(target.text);
            VNScriptLocale.Apply(targetCommands, target.name);
            script = target;
            _commands = targetCommands;
            _labels.Clear();
            foreach (var pair in targetLabels) _labels[pair.Key] = pair.Value;
            _index = targetIndex;
            _currentSayIndex = targetIndex;
            Debug.Log($"[VNScript] 已限定跳转到「{target.name}::{label}」");
            return true;
        }

        bool CallTo(VNScriptCommand command)
        {
            if (command.isAsync)
            {
                Debug.LogError($"[VNScript] 第 {command.line} 行：call 不能使用行尾 @ 异步执行");
                return false;
            }
            if (_callStack.Count >= MaxCallDepth)
            {
                Debug.LogError($"[VNScript] 第 {command.line} 行：call 嵌套超过上限 {MaxCallDepth}，" +
                               "可能存在无终止递归");
                return false;
            }

            var frame = new VNCallFrame
            {
                returnScript = script,
                returnCommands = _commands,
                returnIndex = _index,
                sourceLine = command.line,
                returnParameters = _currentParameters,
            };
            if (!JumpTo(command.Arg(0), command.line)) return false;
            if (!TryBindCallParameters(command, out Dictionary<string, string> calleeParameters))
            {
                RestoreExecutionFrame(frame);
                return false;
            }
            _callStack.Add(frame);
            _currentParameters = calleeParameters;
            return true;
        }

        bool TryBindCallParameters(
            VNScriptCommand call,
            out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>();
            var declared = new HashSet<string>();
            bool hasError = false;

            // JumpTo 后 _index 指向目标 label；params 必须是其后的第一条有效命令。
            VNScriptCommand declaration = _index + 1 < _commands.Count
                ? _commands[_index + 1] : null;
            if (declaration != null && declaration.keyword == "params")
            {
                if (declaration.args.Count == 0 && declaration.kwargs.Count == 0)
                {
                    Debug.LogError($"[VNScript] 第 {declaration.line} 行：params 至少需要一个参数名");
                    hasError = true;
                }
                foreach (var invalid in declaration.kwargs)
                {
                    Debug.LogError($"[VNScript] 第 {declaration.line} 行：params 声明「{invalid.Key}:{invalid.Value}」" +
                                   "无效，请使用 名字 或 名字=默认值");
                    hasError = true;
                }
                foreach (string token in declaration.args)
                {
                    int equals = token.IndexOf('=');
                    string name = (equals >= 0 ? token.Substring(0, equals) : token).Trim();
                    string defaultValue = equals >= 0 ? token.Substring(equals + 1) : null;
                    if (name.Length == 0 || equals == 0 || token.Contains(","))
                    {
                        Debug.LogError($"[VNScript] 第 {declaration.line} 行：params 参数「{token}」无效");
                        hasError = true;
                        continue;
                    }
                    if (!declared.Add(name))
                    {
                        Debug.LogError($"[VNScript] 第 {declaration.line} 行：params 重复声明「{name}」");
                        hasError = true;
                        continue;
                    }

                    if (call.kwargs.TryGetValue(name, out string supplied))
                        values[name] = supplied;
                    else if (defaultValue != null)
                        values[name] = defaultValue;
                    else
                    {
                        Debug.LogError($"[VNScript] 第 {call.line} 行：call 缺少必需参数「{name}」");
                        hasError = true;
                    }
                }
            }

            foreach (var pair in call.kwargs)
            {
                if (pair.Key.Contains(",") || pair.Value.Contains(","))
                {
                    Debug.LogError($"[VNScript] 第 {call.line} 行：call 参数「{pair.Key}:{pair.Value}」" +
                                   "不能包含逗号");
                    hasError = true;
                    continue;
                }
                if (!declared.Contains(pair.Key))
                    Debug.LogWarning($"[VNScript] 第 {call.line} 行：目标未声明参数「{pair.Key}」，已忽略");
            }
            for (int i = 1; i < call.args.Count; i++)
                Debug.LogWarning($"[VNScript] 第 {call.line} 行：call 多余位置参数「{call.args[i]}」，已忽略；" +
                                 "请使用 名字:值");
            return !hasError;
        }

        void RestoreExecutionFrame(VNCallFrame frame)
        {
            script = frame.returnScript;
            _commands = frame.returnCommands;
            _currentParameters = frame.returnParameters ?? new Dictionary<string, string>();
            VNScriptLocale.Apply(_commands, script != null ? script.name : null);
            BuildLabelMap(_commands, _labels);
            _index = Mathf.Clamp(frame.returnIndex, 0, _commands.Count);
            _currentSayIndex = _index;
            if (script != null) VNFont.Prewarm(script.text);
        }

        bool ReturnFromCall(int fromLine, bool isAsync)
        {
            if (isAsync)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：return 不能使用行尾 @ 异步执行");
                return false;
            }
            if (_callStack.Count == 0)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：return 没有对应的 call，停止当前剧本");
                _currentParameters.Clear();
                _index = _commands.Count;
                return false;
            }

            int last = _callStack.Count - 1;
            VNCallFrame frame = _callStack[last];
            if (frame.returnCommands == null)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：call 返回点已损坏，停止当前剧本");
                _callStack.Clear();
                _currentParameters.Clear();
                _index = _commands.Count;
                return false;
            }

            _callStack.RemoveAt(last);
            RestoreExecutionFrame(frame);
            Debug.Log($"[VNScript] 已从子程序返回（call 第 {frame.sourceLine} 行）");
            return true;
        }

        static Dictionary<string, string> ReadParameters(
            List<string> names,
            List<string> values)
        {
            var result = new Dictionary<string, string>();
            if (names == null || values == null) return result;
            for (int i = 0; i < names.Count && i < values.Count; i++)
                if (!string.IsNullOrEmpty(names[i])) result[names[i]] = values[i] ?? string.Empty;
            return result;
        }

        static void WriteParameters(
            Dictionary<string, string> source,
            List<string> names,
            List<string> values)
        {
            names.Clear();
            values.Clear();
            if (source == null) return;
            foreach (var pair in source)
            {
                names.Add(pair.Key);
                values.Add(pair.Value);
            }
        }

        void CaptureCallStack(VNSaveData data)
        {
            data.callStack.Clear();
            WriteParameters(_currentParameters, data.parameterNames, data.parameterValues);
            foreach (VNCallFrame frame in _callStack)
            {
                var saved = new VNSaveData.CallFrameSave
                {
                    chapter = frame.returnScript != null ? frame.returnScript.name : null,
                    returnIndex = frame.returnIndex,
                    sourceLine = frame.sourceLine,
                };
                WriteParameters(frame.returnParameters, saved.parameterNames, saved.parameterValues);
                data.callStack.Add(saved);
            }
        }

        void RestoreCallStack(VNSaveData data)
        {
            _callStack.Clear();
            _currentParameters = ReadParameters(data.parameterNames, data.parameterValues);
            if (data.callStack == null || data.callStack.Count == 0) return;
            if (data.callStack.Count > MaxCallDepth)
            {
                Debug.LogError($"[VNSave] call 栈深度 {data.callStack.Count} 超过上限 {MaxCallDepth}，已忽略");
                return;
            }

            foreach (VNSaveData.CallFrameSave saved in data.callStack)
            {
                TextAsset target = FindChapter(saved.chapter);
                if (target == null)
                {
                    Debug.LogError($"[VNSave] 无法恢复 call 返回文件「{saved.chapter}」，call 栈已忽略");
                    _callStack.Clear();
                    return;
                }
                var commands = VNScriptParser.Parse(target.text);
                if (saved.returnIndex < 0 || saved.returnIndex > commands.Count)
                {
                    Debug.LogError($"[VNSave] call 返回索引 {saved.returnIndex} 超出剧本「{target.name}」范围，" +
                                   "call 栈已忽略");
                    _callStack.Clear();
                    return;
                }
                VNScriptLocale.Apply(commands, target.name);
                _callStack.Add(new VNCallFrame
                {
                    returnScript = target,
                    returnCommands = commands,
                    returnIndex = saved.returnIndex,
                    sourceLine = saved.sourceLine,
                    returnParameters = ReadParameters(
                        saved.parameterNames, saved.parameterValues),
                });
            }
        }

        VNScriptCommand ResolveParameters(VNScriptCommand source)
        {
            // 定义本身必须稳定；label/params 不能通过参数动态改名。
            if (source.keyword == "label" || source.keyword == "params") return source;
            if (!HasParameterPlaceholder(source)) return source;

            var missing = new HashSet<string>();
            string Replace(string value) => VNParameterInterpolator.Interpolate(
                value,
                _currentParameters,
                name => missing.Add(name));

            var command = new VNScriptCommand
            {
                keyword = source.keyword,
                isAsync = source.isAsync,
                line = source.line,
                speaker = Replace(source.speaker),
                expression = Replace(source.expression),
                text = Replace(source.text),
                localizedText = Replace(source.localizedText),
            };
            foreach (string arg in source.args) command.args.Add(Replace(arg));
            foreach (var pair in source.kwargs) command.kwargs[pair.Key] = Replace(pair.Value);

            if (source.options != null)
            {
                command.options = new List<VNChoiceOption>();
                foreach (VNChoiceOption option in source.options)
                    command.options.Add(new VNChoiceOption
                    {
                        text = Replace(option.text),
                        localizedText = Replace(option.localizedText),
                        flagOp = Replace(option.flagOp),
                        condition = Replace(option.condition),
                        costOp = Replace(option.costOp),
                        jumpLabel = Replace(option.jumpLabel),
                        line = option.line,
                    });
            }
            if (source.camPoints != null)
            {
                command.camPoints = new List<VNCamWaypointDef>();
                foreach (VNCamWaypointDef point in source.camPoints)
                    command.camPoints.Add(new VNCamWaypointDef
                    {
                        point = Replace(point.point),
                        zoom = point.zoom,
                        duration = point.duration,
                        ease = Replace(point.ease),
                        fade = point.fade,
                        hold = point.hold,
                        shake = point.shake,
                        line = point.line,
                    });
            }

            foreach (string name in missing)
                Debug.LogError($"[VNScript] 第 {source.line} 行：找不到 call 参数「{name}」，" +
                               "占位符保持原样");
            return command;
        }

        static bool HasParameterPlaceholder(VNScriptCommand command)
        {
            bool Has(string value) => value != null &&
                                      value.IndexOf("${", System.StringComparison.Ordinal) >= 0;
            if (Has(command.speaker) || Has(command.expression) || Has(command.text) ||
                Has(command.localizedText)) return true;
            foreach (string arg in command.args) if (Has(arg)) return true;
            foreach (var pair in command.kwargs) if (Has(pair.Value)) return true;
            if (command.options != null)
                foreach (VNChoiceOption option in command.options)
                    if (Has(option.text) || Has(option.localizedText) || Has(option.flagOp) ||
                        Has(option.condition) || Has(option.costOp) || Has(option.jumpLabel))
                        return true;
            if (command.camPoints != null)
                foreach (VNCamWaypointDef point in command.camPoints)
                    if (Has(point.point) || Has(point.ease)) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // 存档 / 读档
        // ------------------------------------------------------------------

        public void SaveTo(int slot)
        {
            SaveTo(slot, null);
        }

        public void SaveTo(int slot, Texture2D thumbnail) => SaveTo(slot, thumbnail, false);

        public void SaveTo(int slot, Texture2D thumbnail, bool quick)
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            var data = new VNSaveData
            {
                commandIndex = _currentSayIndex,
                chapter = script != null ? script.name : null,
                lastLine = _lastSayText,
            };
            CaptureCallStack(data);
            stage.CaptureSnapshot(data);
            // 只存锁定隐藏：普通隐藏玩家一碰就还原，存了反而让读档后界面莫名消失
            data.uiHidden = _uiHideLocked ? VNUiPartsUtil.ToToken(_uiHiddenParts) : "";
            VNSaveSystem.Save(slot, data, thumbnail);
            VNToast.Show(quick ? VNLocale.T("runner.quickSaved")
                               : VNLocale.T("runner.saved", slot));
        }

        public void LoadFrom(int slot) => LoadFrom(slot, false);

        public void LoadFrom(int slot, bool quick)
        {
            var data = VNSaveSystem.Load(slot);
            if (data == null)
            {
                VNToast.Show(quick ? VNLocale.T("runner.noQuickSave")
                                   : VNLocale.T("runner.slotEmpty", slot));
                return;
            }
            SetSkip(false);
            SetAuto(false);
            Stop();
            if (!string.IsNullOrEmpty(data.chapter) && !SwitchChapter(data.chapter, 0))
            {
                Debug.LogError($"[VNSave] 当前章节「{data.chapter}」无法恢复，已中止读档");
                return;
            }
            RestoreCallStack(data); // 旧存档字段为空，自动得到空栈
            stage.RestoreSnapshot(data);
            RestoreUiHidden(data);  // 旧存档字段为空 = 界面全开，语义正确
            VNToast.Show(quick ? VNLocale.T("runner.quickLoaded")
                               : VNLocale.T("runner.loaded", slot));
            ResumeAt(data.commandIndex);
        }

        // ------------------------------------------------------------------
        // 快速存读档（Q / L，专用槽 0，不在 20 槽面板里显示）
        // ------------------------------------------------------------------

        /// <summary>快速存档专用槽（面板网格只显示 1..SlotCount，0 不可见）</summary>
        public const int QuickSaveSlot = 0;

        Coroutine _quickSaveCo;

        /// <summary>Q 键 / 快存按钮：同 F5 的截图管线，但直接落盘不开面板不暂停。</summary>
        public void QuickSave()
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            if (_quickSaveCo != null) return; // 截图进行中，忽略连按
            _quickSaveCo = StartCoroutine(QuickSaveCo());
        }

        /// <summary>L 键 / 快读按钮：读取快速存档；没有则提示。</summary>
        public void QuickLoad()
        {
            CancelSaveCapture();
            LoadFrom(QuickSaveSlot, true);
        }

        IEnumerator QuickSaveCo()
        {
            var capture = stage != null && stage.vnCamera != null
                ? stage.vnCamera.cameraFade : null;
            if (capture == null) capture = FindFirstObjectByType<VNCameraFade>();
            if (capture == null)
                capture = new GameObject("SaveThumbnailCapture").AddComponent<VNCameraFade>();

            Texture2D thumbnail = null;
            yield return capture.CaptureThumbnailCo(320, 180, texture => thumbnail = texture);
            _quickSaveCo = null;

            if (!_waitingAtSay) // 截图那一两帧里演出推进了：作废，避免存到不可恢复点
            {
                if (thumbnail != null) Destroy(thumbnail);
                yield break;
            }
            SaveTo(QuickSaveSlot, thumbnail, true);
            if (thumbnail != null) Destroy(thumbnail); // PNG 已落盘，纹理即可释放
        }

        void EnsureSaveLoadPanel()
        {
            if (_saveLoadPanel == null)
            {
                _saveLoadPanel = FindFirstObjectByType<VNSaveLoadPanel>();
                if (_saveLoadPanel == null)
                    _saveLoadPanel = new GameObject("VNSaveLoadPanel").AddComponent<VNSaveLoadPanel>();
            }
            _saveLoadPanel.Initialize(this);
        }

        void EnsureQuickToolbar()
        {
            if (stage == null || stage.dialogue == null) return;
            if (_quickToolbar == null)
            {
                _quickToolbar = stage.dialogue.GetComponent<VNQuickToolbar>();
                if (_quickToolbar == null)
                    _quickToolbar = stage.dialogue.gameObject.AddComponent<VNQuickToolbar>();
            }
            _quickToolbar.Initialize(this);
        }

        void EnsureConfigPanel()
        {
            if (_configPanel == null)
            {
                _configPanel = FindFirstObjectByType<VNConfigPanel>();
                if (_configPanel == null)
                    _configPanel = new GameObject("VNConfigPanel").AddComponent<VNConfigPanel>();
            }
            _configPanel.Initialize(this, stage);
        }

        /// <summary>F5 / 保存页签入口：先隐藏 UI 并截取游戏画面，再显示 20 槽网格。</summary>
        public void RequestSavePanel()
        {
            if (!_waitingAtSay)
            {
                VNToast.Show(VNLocale.T("runner.cannotSaveNow"));
                return;
            }
            EnsureSaveLoadPanel();
            PauseForSaveLoadMenu();
            _saveLoadPanel.PrepareForSaveCapture();

            if (_saveCaptureCo != null) StopCoroutine(_saveCaptureCo);
            int token = ++_saveCaptureToken;
            _saveCaptureCo = StartCoroutine(CaptureSaveThumbnailCo(token));
        }

        /// <summary>F9 / 读取页签入口。</summary>
        public void RequestLoadPanel()
        {
            EnsureSaveLoadPanel();
            CancelSaveCapture();
            PauseForSaveLoadMenu();
            _saveLoadPanel.OpenLoad();
        }

        public void RequestBacklog()
        {
            if (_backlog == null) return;
            _backlog.Open();
        }

        public void RequestQuestLog()
        {
            if (_questLog == null || _eventActive) return;
            _questLog.Toggle();
        }

        /// <summary>日记本（D 键）。事件进行中不开，同其他面板。</summary>
        public void RequestDiary()
        {
            if (_eventActive) return;
            if (_diaryPanel == null)
            {
                _diaryPanel = FindFirstObjectByType<VNAiDiaryPanel>();
                if (_diaryPanel == null)   // 没人手工摆也能用，同任务日志
                    _diaryPanel = new GameObject("VNAiDiaryPanel")
                        .AddComponent<VNAiDiaryPanel>();
            }
            _diaryPanel.Toggle();
        }

        public void RequestStatsPanel()
        {
            if (_statsHud == null || _eventActive) return;
            _statsHud.Toggle();
        }

        public void RequestInventory()
        {
            if (_inventory == null || _eventActive) return;
            _inventory.Toggle();
        }

        public void RequestCgGallery()
        {
            if (_cgGallery == null || _eventActive) return;
            _cgGallery.Toggle();
        }

        public void RequestConfigPanel()
        {
            EnsureConfigPanel();
            PauseForSaveLoadMenu();
            _configPanel.Open();
        }

        /// <summary>整块界面隐藏/恢复（右键、快捷条的「隐藏UI」按钮）。不锁定，玩家一碰就还原。</summary>
        public void SetInterfaceHidden(bool hidden) => SetUiHidden(VNUiParts.All, hidden, false);

        /// <summary>
        /// 按部件隐藏/恢复界面。
        /// locked = 剧本 hideHUD keep：隐藏后玩家点击只推进台词、不弹回界面，
        /// 要等剧本写 hideHUD off（或读到一个没锁定的存档）才恢复。
        /// 部件留空按全部处理，这样老剧本里光秃秃的一行 hideHUD 语义不变。
        /// </summary>
        public void SetUiHidden(VNUiParts parts, bool hidden, bool locked)
        {
            if (parts == VNUiParts.None) parts = VNUiParts.All;
            if (hidden)
            {
                _uiHiddenParts |= parts;
                // 锁定是整体状态而不是按部件的：混着写时以最后一条为准，
                // 不然「keep 藏属性栏 + 普通藏对话框」要不要拦输入根本说不清。
                _uiHideLocked = locked;
            }
            else
            {
                _uiHiddenParts &= ~parts;
                if (_uiHiddenParts == VNUiParts.None) _uiHideLocked = false;
            }
            ApplyUiHidden();
        }

        /// <summary>把当前的隐藏状态写到各 UI 上（事件进行中时养成 HUD 一律不显示）</summary>
        void ApplyUiHidden()
        {
            // 偷拍模式期间整套界面临时藏起来，但**不写进 _uiHiddenParts**：
            // 退出时按剧本/玩家原本的隐藏状态还原（hideHUD keep 的段落不能被它弹回 UI）
            if (stage != null && stage.dialogue != null)
                stage.dialogue.SetInterfaceVisible(
                    !_secretPhotoActive && (_uiHiddenParts & VNUiParts.Dialogue) == 0);
            ApplyGameplayHudVisible(!_eventActive && !_secretPhotoActive);
        }

        // ------------------------------------------------------------------
        // 秘密偷拍模式（VNSecretPhotoMode 回调用）
        // ------------------------------------------------------------------

        public VNStatsHud StatsHud => _statsHud;

        /// <summary>右上角相机图标此刻该不该显示：没有别的面板/事件/标题盖着</summary>
        public bool IsSecretPhotoIconAllowed()
        {
            if (!_running || _eventActive) return false;
            if (_titleMenu != null && _titleMenu.IsOpen) return false;
            if (stage != null && stage.IsSnsOpen) return false;
            if (_uiHidden && !_uiHideLocked) return false; // 右键藏 UI 期间图标也一起藏
            if (_configPanel != null && _configPanel.IsOpen) return false;
            if (_saveLoadPanel != null && _saveLoadPanel.IsOpen) return false;
            if (_backlog != null && _backlog.IsOpen) return false;
            if (_questLog != null && _questLog.IsOpen) return false;
            if (_diaryPanel != null && _diaryPanel.IsOpen) return false;
            if (_statsHud != null && _statsHud.IsOpen) return false;
            if (_inventory != null && _inventory.IsOpen) return false;
            if (_cgGallery != null && _cgGallery.IsOpen) return false;
            return true;
        }

        /// <summary>能不能进偷拍模式：图标条件 + 停在台词上（与存档同一条规则）+ 没被教程冻住</summary>
        public bool CanOpenSecretPhoto() =>
            IsSecretPhotoIconAllowed() && _waitingAtSay && !VNPause.IsPaused;

        /// <summary>偷拍模式进/出：临时隐藏对话框与 HUD，退出按原状态还原</summary>
        public void SetSecretPhotoActive(bool active)
        {
            if (_secretPhotoActive == active) return;
            _secretPhotoActive = active;
            ApplyUiHidden();
        }

        /// <summary>
        /// 剧本之外插一句话（偷拍被发现时她的反应）：直接写对话框 + 进回想，
        /// 不走 RunInlineCo——那会嵌套一层 SayCo 把 _waitingAtSay 清掉。
        /// 玩家点一下就推进原剧本的下一句。
        /// </summary>
        public void SayOutOfScript(string speakerId, string text)
        {
            if (stage == null || string.IsNullOrEmpty(text)) return;
            stage.Say(speakerId, null, text);
            _backlog?.Record(stage.GetDisplayName(speakerId), text);
        }

        /// <summary>
        /// 常驻养成 HUD（属性条 / 日历）的显隐。事件模块期间一律藏掉（allowed = false）——
        /// 小游戏自己会画满屏 UI，顶上再压一条属性条只是噪音。
        /// 事件结束时按 <see cref="_uiHiddenParts"/> 恢复，不会把玩家/剧本藏起来的又翻出来。
        /// </summary>
        void ApplyGameplayHudVisible(bool allowed)
        {
            _statsHud?.SetHudVisible(allowed && (_uiHiddenParts & VNUiParts.Stats) == 0);
            _calendarHud?.SetVisible(allowed && (_uiHiddenParts & VNUiParts.Calendar) == 0);
        }

        /// <summary>hideHUD [off] [keep] [dialogue|stats|calendar|all]… 的参数解析（运行与调试重建共用）</summary>
        static void ParseHideHudArgs(VNScriptCommand cmd, out VNUiParts parts,
                                     out bool hide, out bool locked)
        {
            parts = VNUiParts.None;
            hide = true;
            locked = false;
            foreach (var token in cmd.args)
            {
                if (string.IsNullOrEmpty(token)) continue;
                string lower = token.Trim().ToLowerInvariant();
                if (lower == "off" || token == "显示" || token == "恢复") { hide = false; continue; }
                if (lower == "keep" || lower == "lock" || token == "保持" || token == "锁定")
                {
                    locked = true;
                    continue;
                }
                var part = VNUiPartsUtil.Parse(token);
                if (part != VNUiParts.None) { parts |= part; continue; }
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：hideHUD 不认识的参数「{token}」" +
                                 "（可用 off / keep / dialogue / stats / calendar / all）");
            }
        }

        /// <summary>读档 / 调试重建：把存档里的锁定隐藏状态放回去</summary>
        void RestoreUiHidden(VNSaveData data)
        {
            // 存档里只会有锁定的隐藏（非锁定的一碰就还原，是瞬态不存），
            // 所以取回来非空就一定是锁定态。
            _uiHiddenParts = VNUiPartsUtil.FromToken(data.uiHidden);
            _uiHideLocked = _uiHiddenParts != VNUiParts.None;
            ApplyUiHidden();
        }

        IEnumerator CaptureSaveThumbnailCo(int token)
        {
            VNCameraFade capture = stage != null && stage.vnCamera != null
                ? stage.vnCamera.cameraFade : null;
            if (capture == null) capture = FindFirstObjectByType<VNCameraFade>();
            if (capture == null)
                capture = new GameObject("SaveThumbnailCapture").AddComponent<VNCameraFade>();

            Texture2D thumbnail = null;
            yield return capture.CaptureThumbnailCo(320, 180, texture => thumbnail = texture);
            _saveCaptureCo = null;
            if (token != _saveCaptureToken || _saveLoadPanel == null || !_menuPaused)
            {
                if (thumbnail != null) Destroy(thumbnail);
                yield break;
            }
            _saveLoadPanel.OpenSave(thumbnail);
        }

        void PauseForSaveLoadMenu()
        {
            if (_menuPaused) return;
            _menuPaused = true;
            _timeScaleBeforeMenu = Time.timeScale;
            Time.timeScale = 0f;
            if (_auto) SetAuto(false);
            if (_skip) SetSkip(false);
        }

        public void OnSaveLoadPanelClosed()
        {
            CancelSaveCapture();
            if (!_menuPaused) return;
            Time.timeScale = _timeScaleBeforeMenu;
            _menuPaused = false;
        }

        public void OnConfigPanelClosed() => OnSaveLoadPanelClosed();

        void CancelSaveCapture()
        {
            _saveCaptureToken++;
            if (_saveCaptureCo == null) return;
            StopCoroutine(_saveCaptureCo);
            _saveCaptureCo = null;
        }

        public void LoadFromPanel(int slot)
        {
            _saveLoadPanel?.Close();
            LoadFrom(slot);
        }

        // ------------------------------------------------------------------
        // 模式
        // ------------------------------------------------------------------

        public void SetAuto(bool on)
        {
            _auto = on;
            if (on) SetSkip(false);
            UpdateModeLabel();
            VNToast.Show(VNLocale.T(on ? "runner.autoOn" : "runner.autoOff"));
        }

        public void SetSkip(bool on)
        {
            if (_skip == on) return;
            _skip = on;
            if (on) _auto = false;
            DOTween.timeScale = on ? skipTimeScale : 1f;
            UpdateModeLabel();
            VNToast.Show(VNLocale.T(on ? "runner.skipOn" : "runner.skipOff"));
        }

        void UpdateModeLabel() =>
            VNToast.SetMode(_skip ? "SKIP ▶▶" : _auto ? "AUTO ▶" : null);

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLocaleChanged;
            if (_skip) DOTween.timeScale = 1f; // 别把加速留给别的场景
            if (_menuPaused) Time.timeScale = _timeScaleBeforeMenu;
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (_eventActive) return; // 事件模块进行中：输入全部交给模块

            // 教程讲解中（VNPause）：全局快捷键一律屏蔽。
            // 不加这一条的话，剧情层弹出的教程盖着屏幕，F5 存档 / H 回想 /
            // A / S / I / C / G / J 还全都能按 —— 存出来的档还会卡在教程半截。
            if (VNPause.IsPaused) return;

            // 偷拍模式打开期间：输入全部归它（ESC / 空格 / 滚轮 / 拖动），
            // 这里直接 return 也就顺带挡掉了 F5/F9/Q/L 与推进
            if (_secretPhoto != null && _secretPhoto.IsOpen) return;

            // SNS 手机聊天：等玩家挑回复时输入全部交给面板（同 event，顺带挡掉存档）
            bool snsOpen = stage != null && stage.IsSnsOpen;
            if (snsOpen && stage.sns.IsBlockingInput) return;

            // 隐藏 UI 后，第一次操作只恢复界面，不会顺便推进台词。
            // 但 hideHUD keep 的锁定隐藏不吃这一条——那正是「点了也一直藏着」的意思，
            // 输入照常往下走（台词继续推进，只是看不见对话框）。
            if (_uiHidden && !_uiHideLocked)
            {
                bool restore = kb.uKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                               kb.spaceKey.wasPressedThisFrame ||
                               (mouse != null && (mouse.leftButton.wasPressedThisFrame ||
                                                  mouse.rightButton.wasPressedThisFrame));
                if (restore) SetInterfaceHidden(false);
                return;
            }

            if (_configPanel != null && _configPanel.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _configPanel.Close();
                return;
            }

            // 存读档界面打开期间只响应界面快捷键，不推进剧情。
            if (_saveLoadPanel != null && _saveLoadPanel.IsOpen)
            {
                if (kb.escapeKey.wasPressedThisFrame) _saveLoadPanel.Close();
                else if (kb.f5Key.wasPressedThisFrame) RequestSavePanel();
                else if (kb.f9Key.wasPressedThisFrame) RequestLoadPanel();
                return;
            }

            // 回想面板打开期间：只处理关闭，不推进剧情
            if (_backlog != null && _backlog.IsOpen)
            {
                if (kb.hKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _backlog.Close();
                return;
            }

            // 任务日志打开期间：只处理关闭，不推进剧情
            if (_questLog != null && _questLog.IsOpen)
            {
                if (kb.jKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _questLog.Close();
                return;
            }

            // 日记本打开期间：同上
            if (_diaryPanel != null && _diaryPanel.IsOpen)
            {
                if (kb.dKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _diaryPanel.Close();
                return;
            }

            // 属性面板打开期间：只处理关闭，不推进剧情
            if (_statsHud != null && _statsHud.IsOpen)
            {
                if (kb.cKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _statsHud.Close();
                return;
            }

            // 物品栏打开期间：只处理关闭，不推进剧情
            if (_inventory != null && _inventory.IsOpen)
            {
                if (kb.iKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _inventory.Close();
                return;
            }

            // CG 鉴赏打开期间：只处理翻页与关闭，不推进剧情。
            // 比其他面板多一层——全屏浏览时 ←→ 翻差分、Esc/G 先退回网格。
            if (_cgGallery != null && _cgGallery.IsOpen)
            {
                if (_cgGallery.IsViewerOpen)
                {
                    if (kb.escapeKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame)
                        _cgGallery.CloseViewer();
                    else if (kb.rightArrowKey.wasPressedThisFrame ||
                             kb.downArrowKey.wasPressedThisFrame)
                        _cgGallery.ViewerNext();
                    else if (kb.leftArrowKey.wasPressedThisFrame ||
                             kb.upArrowKey.wasPressedThisFrame)
                        _cgGallery.ViewerPrev();
                }
                else if (kb.gKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                {
                    _cgGallery.Close();
                }
                return;
            }

            // 标题菜单打开期间：按钮走 EventSystem，游戏快捷键与推进全部屏蔽。
            // （读档/设置/画廊面板叠在标题之上时，它们的关闭键由上面各自的分支处理。）
            if (_titleMenu != null && _titleMenu.IsOpen) return;

            // SNS 打开期间屏蔽会打架的快捷键：
            // 滚轮要留给聊天记录滚动、聊天消息不进回想、隐藏 UI 也没有意义。
            // 存读档（F5/F9/Q/L）照常可用——气泡停顿处就是合法存档点。
            if (!snsOpen)
            {
                // 滚轮上滑开回想是可关的（设置面板「滚轮打开回想」）——有人嫌误触；
                // 关掉后 H 键照常。
                if (kb.hKey.wasPressedThisFrame ||
                    (VNConfigPanel.WheelOpensBacklog &&
                     mouse != null && mouse.scroll.ReadValue().y > 0.1f))
                {
                    _backlog?.Open();
                    return;
                }

                if (kb.jKey.wasPressedThisFrame)
                {
                    _questLog?.Open();
                    return;
                }

                if (kb.dKey.wasPressedThisFrame)
                {
                    RequestDiary();   // 面板按需创建，所以走 Request 而不是直接 Open
                    return;
                }

                if (kb.cKey.wasPressedThisFrame)
                {
                    _statsHud?.Open();
                    return;
                }

                if (kb.iKey.wasPressedThisFrame)
                {
                    _inventory?.Open();
                    return;
                }

                if (kb.gKey.wasPressedThisFrame)
                {
                    _cgGallery?.Open();
                    return;
                }

                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    SetInterfaceHidden(true);
                    return;
                }
            }

            if (kb.f5Key.wasPressedThisFrame) { RequestSavePanel(); return; }
            if (kb.f9Key.wasPressedThisFrame) { RequestLoadPanel(); return; }
            if (kb.qKey.wasPressedThisFrame) { QuickSave(); return; }
            if (kb.lKey.wasPressedThisFrame) { QuickLoad(); return; }
            if (!snsOpen && kb.aKey.wasPressedThisFrame) { SetAuto(!_auto); return; }
            if (!snsOpen && kb.sKey.wasPressedThisFrame) { SetSkip(!_skip); return; }

            if (!_running) return;

            // 左键推进：整个画面都是 uGUI（背景/立绘/对话框都是 Canvas 里的 Image），
            // IsPointerOverGameObject() 恒为 true 会把点击全部拦掉；
            // 只有点在可交互控件（按钮/滑条等 Selectable）上才不推进。
            // 点击喷水模式（liquid click on）期间左键归喷水，不推进台词。
            // Enter/Space 一定要留着：玩家没有别的出路时会被卡死在这一句里。
            bool liquidClick = stage != null && stage.liquidSplash != null &&
                               stage.liquidSplash.clickMode;
            bool pressed = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame ||
                           (!liquidClick && mouse != null && mouse.leftButton.wasPressedThisFrame &&
                            !IsPointerOverInteractiveUi(mouse));
            if (!pressed) return;

            // 手动推进会顺手退出快进（惯例）
            if (_skip) SetSkip(false);

            // SNS 气泡没有打字机（对话框此时是隐藏的），点击一律等于推进
            if (!snsOpen && stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
                stage.dialogue.CompleteTyping();
            else
                _advance = true;
        }

        static readonly List<RaycastResult> _pointerRaycastResults = new List<RaycastResult>();

        /// <summary>
        /// 指针是否落在可交互控件上（Selectable：按钮/滑条/输入框等）。
        /// 用射线命中链向上找 Selectable，而不是 IsPointerOverGameObject ——
        /// 后者对任何 raycastTarget 都为 true，本项目全屏皆 UI，会拦掉一切点击。
        /// </summary>
        /// <summary>
        /// 外部（事件模块）请求推进当前正在等待的台词。
        ///
        /// **为什么必须有这个入口**：Update 第一行就是 `if (_eventActive) return;`
        /// —— 事件模块进行中，输入全部交给模块。于是模块内部用 RunInlineCo 播的
        /// 阻塞台词会死等 `_advance`，玩家点破屏幕也过不去。模块在阻塞期间
        /// 自己检测推进输入，转发到这里。
        ///
        /// 语义与 Update 里的手动推进一致：还在打字就先补完，打完了才真推进。
        /// </summary>
        public void RequestAdvance()
        {
            if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
            {
                stage.dialogue.CompleteTyping();
                return;
            }
            if (_waitingAtSay) _advance = true;
        }

        /// <summary>当前是否正卡在某句台词上等玩家推进（模块判断要不要转发输入）</summary>
        public bool IsWaitingAtSay => _waitingAtSay;

        static bool IsPointerOverInteractiveUi(Mouse mouse)
        {
            if (EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current)
            {
                position = mouse.position.ReadValue(),
            };
            _pointerRaycastResults.Clear();
            EventSystem.current.RaycastAll(data, _pointerRaycastResults);
            foreach (var hit in _pointerRaycastResults)
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------
        // 主循环
        // ------------------------------------------------------------------

        IEnumerator Run()
        {
            _running = true;
            while (_index < _commands.Count)
            {
                // 编辑器调试暂停：卡在两条命令之间；单步 = 放行一条后自动卡回来
                while (_debugPaused)
                {
                    if (_debugStepRequested) { _debugStepRequested = false; break; }
                    yield return null;
                }

                var cmd = ResolveParameters(_commands[_index++]);
                IEnumerator co = null;
                try
                {
                    co = Dispatch(cmd);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[VNScript] 第 {cmd.line} 行执行出错（{cmd.keyword}）：{e.Message}");
                }
                if (co == null) continue;
                if (cmd.isAsync) StartCoroutine(co);
                else yield return StartCoroutine(co);
            }
            if (_callStack.Count > 0)
            {
                VNCallFrame frame = _callStack[_callStack.Count - 1];
                Debug.LogError($"[VNScript] 子程序播放到文件末尾但没有 return（call 第 {frame.sourceLine} 行），" +
                               "已停止并清空调用栈");
                _callStack.Clear();
                _currentParameters.Clear();
            }
            _running = false;
            SetSkip(false);
            _voicePendingForNextSay = false;
            stage?.StopSpeaking();
            Debug.Log("[VNScript] 剧本播放结束");
        }

        IEnumerator Dispatch(VNScriptCommand cmd)
        {
            switch (cmd.keyword)
            {
                case "say":
                    _currentSayIndex = _index - 1;
                    return SayCo(cmd);

                case "wait":
                    return WaitCo(cmd.ArgF(0, 0.5f));

                case "bg":
                    // via:black = 回到老的「先被纯色盖住再散开」；不写 = 新图直接过渡出来
                    return WaitTween(stage.SetBackground(
                        cmd.Arg(0), cmd.Kw("transition"), cmd.line, PrecutFor(cmd),
                        cmd.Kw("via") == "black"));

                case "cg":
                    // cg <id> [transition:Type] [chars:keep] [fx:keep] [via:black] / cg off [...]
                    if (cmd.Arg(0) == "off")
                        return WaitTween(stage.HideCg(cmd.Kw("transition"), cmd.line,
                            cmd.Kw("via") == "black"));
                    return WaitTween(stage.ShowCg(cmd.Arg(0), cmd.Kw("transition"),
                        cmd.Kw("chars") == "keep", cmd.Kw("fx") == "keep", cmd.line,
                        false, cmd.Kw("via") == "black"));

                case "show":
                    // show <角色> [at:] [expr:] [with:预设] [from:方向] [dur:秒]
                    return WaitTween(stage.Show(cmd.Arg(0), cmd.Kw("at"),
                        cmd.Kw("expr"), cmd.Kw("with"),
                        cmd.Kw("from"), cmd.KwF("dur", 0f), cmd.line));

                case "hide":
                    // hide <角色> [with:预设] [to:方向] [dur:秒]
                    return WaitTween(stage.Hide(cmd.Arg(0), cmd.Kw("with", "fade"),
                        cmd.Kw("to"), cmd.KwF("dur", 0f), cmd.line));

                case "emote":
                    return WaitTween(stage.Emote(cmd.Arg(0), cmd.Arg(1), cmd.line));

                case "mark":
                    // mark <角色> <符号|clear> [keep|off] [pos:x,y] [size:1.2] [dur:1.2]
                    return WaitTween(stage.Mark(cmd.Arg(0), cmd.Arg(1), cmd.Arg(2),
                        ParseMarkPos(cmd.Kw("pos"), cmd.line),
                        cmd.KwF("size", 1f), cmd.KwF("dur", 1.1f), cmd.line));

                case "imprint":
                {
                    // imprint <角色> <痕迹id|clear> [pos:x,y] [size:] [life:秒] [rot:度]
                    // 立绘痕迹（掌印等）：pos 是立绘归一化坐标 (0,0)=立绘中心，
                    // 与部位框/markAnchor 同一套。临时演出，会自己褪色消失，不进存档
                    Vector2 ipos = ParseMarkPos(cmd.Kw("pos"), cmd.line) ?? Vector2.zero;
                    stage.Imprint(cmd.Arg(1), cmd.Arg(0), ipos,
                        cmd.KwF("size", 1f), cmd.KwF("life", 0f), cmd.KwF("rot", 0f),
                        null, cmd.line);
                    return null;
                }

                case "overlay":
                    // overlay <角色> <层id|clear> [强度 0~1] [time:秒]
                    // 情绪叠加层（潮红/汗/泪）；层在 VNCharacterDef.overlays 登记。
                    // 强度省略 = 1；瞬发不等待（要等就自己接 wait）
                    stage.SetOverlay(cmd.Arg(0), cmd.Arg(1), cmd.ArgF(2, 1f),
                        cmd.KwF("time", 0.35f), cmd.line);
                    return null;

                case "weather":
                {
                    // weather <id> [density:] [wind:] [speed:] [size:]
                    //   id：petals/sakura/落樱 · maple/枫叶 · ginkgo/银杏 · leaves/落叶 ·
                    //       bamboo/竹叶 · Rain · Snow · Fireflies · none
                    //       （也可以是 VNGameConfig 飘落天气库里登记的自定义 id）
                    //   覆盖参数留空 = 用资产里的值；wind 可为负（向左吹）
                    float wind = cmd.KwF("wind", float.NaN);
                    stage.SetWeather(cmd.Arg(0), cmd.KwF("density", 0f),
                        float.IsNaN(wind) ? 0f : wind, !float.IsNaN(wind),
                        cmd.KwF("speed", 0f), cmd.KwF("size", 0f));
                    return null;
                }

                case "liquid":
                    // liquid splash|spray|click|wet|dry|cover [on|off] [x:] [y:] [type:] …
                    stage.Liquid(cmd.Arg(0, "splash"), ParseLiquidArgs(cmd), cmd.line);
                    return null;

                case "mood":
                    // 走 VNStage 包装：Memory（回忆）色调自动联动电影黑边
                    stage.SetMood(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNMood.Neutral, cmd.line));
                    return null;

                case "letterbox":
                    // letterbox on|off [height:130] [time:0.7]
                    stage.SetLetterbox(cmd.Arg(0, "on") != "off",
                        cmd.KwF("height", -1f), cmd.KwF("time", -1f));
                    return null;

                case "reset":
                    if (cmd.Arg(0) == "effects" || cmd.Arg(0) == "all")
                        stage.ResetEffects();
                    else
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：reset 用法为「reset effects」");
                    return null;

                case "bgscroll":
                {
                    // bgscroll on|off [speed:] [dir:] [mode:repeat|mirror] [time:]
                    bool on = cmd.Arg(0, "on") != "off";
                    float? speed = cmd.kwargs.ContainsKey("speed")
                        ? cmd.KwF("speed", VNBackgroundScroll.DefaultSpeed) : (float?)null;
                    float? dir = VNBackgroundScroll.ParseDirection(cmd.Kw("dir"));
                    if (dir == null && !string.IsNullOrEmpty(cmd.Kw("dir")))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgscroll dir" +
                                         $"「{cmd.Kw("dir")}」认不出，应为 left/right/up/down 或角度");
                    var mode = VNBackgroundScroll.ParseMode(cmd.Kw("mode"));
                    if (mode == null && !string.IsNullOrEmpty(cmd.Kw("mode")))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgscroll mode" +
                                         $"「{cmd.Kw("mode")}」认不出，应为 repeat 或 mirror");
                    float? fade = cmd.kwargs.ContainsKey("time")
                        ? cmd.KwF("time", VNBackgroundScroll.DefaultFade) : (float?)null;
                    stage.SetBackgroundScroll(on, speed, dir, mode, fade, cmd.line);
                    return null;
                }

                case "shake":
                {
                    var level = cmd.Arg(0, "medium").ToLower();
                    var l = level == "light" ? VNShakeLevel.Light
                          : level == "heavy" ? VNShakeLevel.Heavy
                          : VNShakeLevel.Medium;
                    stage.screenShake?.Shake(l);
                    return null;
                }

                case "portrait":
                    // portrait on / portrait off：对话头像全局开关
                    stage.SetPortraitEnabled(cmd.Arg(0, "on") != "off");
                    return null;

                case "ui":
                    // ui dialogue|choice <皮肤id|default>：切换对话框/选项面板皮肤
                    // ui name <样式|default>：切换名字（说话人）的装饰样式
                    stage.SetUiSkin(cmd.Arg(0), cmd.Arg(1, "default"), cmd.line);
                    return null;

                case "sns":
                    return SnsCo(cmd);

                case "camera":
                    return CameraCo(cmd);

                case "camseq":
                    return CamseqCo(cmd);

                case "camcut":
                {
                    // camcut <目标点> [zoom]
                    var p = stage.ResolveCamPoint(cmd.Arg(0), cmd.line);
                    if (p.HasValue)
                        stage.vnCamera?.Cut(p.Value, cmd.ArgF(1, 1.5f));
                    return null;
                }

                case "camto":
                {
                    // camto <目标点> [zoom] [秒] [ease:名]
                    var p = stage.ResolveCamPoint(cmd.Arg(0), cmd.line);
                    if (!p.HasValue || stage.vnCamera == null) return null;
                    return WaitTween(stage.vnCamera.GoTo(p.Value,
                        cmd.ArgF(1, 1.4f), cmd.ArgF(2, 0.8f),
                        ParseEase(cmd.Kw("ease"), Ease.InOutSine)));
                }

                case "transition":
                    if (stage.transition == null) return null;
                    return WaitTween(stage.transition.Play(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNTransition.NoiseDissolve, cmd.line)));

                case "interlude":
                {
                    // interlude <过场id> [time:秒]
                    // 快进时整段跳过（连语音都不放）：章节卡本来就是给正常速度看的，
                    // 而 1.5 秒的固定停留在 SKIP 里是纯粹的卡顿。
                    if (_skip) return null;
                    VNInterludeDef interlude = FindInterlude(cmd.Arg(0), cmd.line);
                    if (interlude == null || stage.interlude == null) return null;
                    return stage.interlude.PlayCo(interlude, cmd.KwF("time", -1f),
                        stage.vnAudio);
                }

                case "tutorial":
                {
                    // tutorial <教程id> [force:on]
                    // 默认「看过就跳过」（记录是全局的，读旧档不会重看）；
                    // force:on 强制重看，帮助菜单/作者点名讲解用。
                    // 快进时整段跳过：教学是给正常速度看的，SKIP 里只会是干扰。
                    if (_skip) return null;
                    var player = stage.tutorial != null ? stage.tutorial : VNTutorialPlayer.Instance;
                    if (player == null) return null;
                    var def = FindTutorial(cmd.Arg(0), cmd.line);
                    if (def == null) return null;
                    string forceArg = cmd.Kw("force");
                    bool force = !string.IsNullOrEmpty(forceArg) &&
                                 forceArg != "off" && forceArg != "false" && forceArg != "0";
                    return player.PlayCo(def, force);
                }

                case "sakura":
                    stage.sakura?.Play();
                    return null;

                case "move":
                    // move 亚里沙 left 0.6
                    return WaitTween(stage.Move(cmd.Arg(0), cmd.Arg(1, "center"),
                        cmd.ArgF(2, 0.6f), cmd.line));

                case "bgm":
                {
                    // bgm play 黄昏之歌 [fade:2] [vol:0.6] / bgm stop [fade:3]
                    string sub = cmd.Arg(0, "play");
                    float fade = 1.5f;
                    if (float.TryParse(cmd.Kw("fade"), out float f)) fade = f;
                    if (sub == "stop") stage.vnAudio?.StopBgm(fade);
                    else if (sub == "play")
                        stage.vnAudio?.PlayBgm(cmd.Arg(1), fade, cmd.KwF("vol", 1f), cmd.line);
                    else Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgm 用法为 bgm play <id> 或 bgm stop");
                    return null;
                }

                case "se":
                {
                    // se 雨声 loop [vol:0.5] / se 心跳 [vol:0.3] / se stop 雨声
                    if (cmd.Arg(0) == "stop")
                        stage.vnAudio?.StopSe(cmd.Arg(1));
                    else
                        stage.vnAudio?.PlaySe(cmd.Arg(0), cmd.args.Contains("loop"),
                            cmd.KwF("vol", 1f), cmd.line);
                    return null;
                }

                case "voice":
                    _voicePendingForNextSay = stage.vnAudio != null &&
                        stage.vnAudio.PlayVoice(cmd.Arg(0), cmd.KwF("vol", 1f), cmd.line);
                    return null;

                case "volume":
                    stage.vnAudio?.SetVolume(cmd.Arg(0), cmd.ArgF(1, 1f), cmd.line);
                    return null;

                case "fx":
                    stage.Fx(cmd.Arg(0), cmd.Arg(1), cmd.line);
                    return null;

                // ---- P1 分支系统 ----
                case "label":
                    return null; // 只是位置标记

                case "jump":
                    JumpTo(cmd.Arg(0), cmd.line);
                    return null;

                case "call":
                    if (string.IsNullOrEmpty(cmd.Arg(0)))
                        Debug.LogError($"[VNScript] 第 {cmd.line} 行：call 缺少目标 label");
                    else
                        CallTo(cmd);
                    return null;

                case "return":
                    ReturnFromCall(cmd.line, cmd.isAsync);
                    return null;

                case "params":
                    return null; // 声明已在 call 进入目标时绑定；运行到此处无需再执行

                case "chapter":
                    if (string.IsNullOrEmpty(cmd.Arg(0)))
                        Debug.LogError($"[VNScript] 第 {cmd.line} 行：chapter 缺少章节文件名");
                    else
                        SwitchChapter(cmd.Arg(0), cmd.line);
                    return null;

                case "flag":
                    // flag 名字 / flag 名字 3 / flag 名字 +1 / flag 名字 rand:1-100
                    ApplyFlagCommand(cmd, false);
                    return null;

                case "if":
                {
                    // 从最后一个 jump 向前重组条件，因此独立 if 可安全使用空格与括号。
                    int jumpIndex = -1;
                    for (int i = cmd.args.Count - 2; i >= 1; i--)
                        if (cmd.args[i] == "jump")
                        {
                            jumpIndex = i;
                            break;
                        }
                    if (jumpIndex <= 0 || jumpIndex + 1 >= cmd.args.Count)
                    {
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：if 语法应为「if 条件表达式 jump 标签」");
                        return null;
                    }
                    string cond = string.Join(" ", cmd.args.GetRange(0, jumpIndex));
                    if (VNFlags.Evaluate(cond, cmd.line))
                        JumpTo(cmd.args[jumpIndex + 1], cmd.line);
                    return null;
                }

                case "stat":
                    // stat 名字 +5 / stat 名字 -3 / stat 名字 500（按 VNStatDef 钳制 + 飘字）
                    _statsHud?.Apply(cmd.Arg(0), cmd.Arg(1), false, cmd.line);
                    return null;

                case "time":
                    // time set <月份> [remain:N] / time pass [months:N] [refill:off|属性名]
                    ApplyTimeCommand(cmd, false);
                    return null;

                case "choice":
                    return ChoiceCo(cmd);

                case "event":
                    return EventCo(cmd);

                case "quest":
                    // quest start|stage|done|fail <id> [阶段]
                    _questLog?.Apply(cmd.Arg(0, "start"), cmd.Arg(1),
                        (int)cmd.ArgF(2, 0f), false, cmd.line);
                    return null;
                case "hideHUD":
                {
                    ParseHideHudArgs(cmd, out var uiParts, out bool uiHide, out bool uiLock);
                    SetUiHidden(uiParts, uiHide, uiLock);
                    return null;
                }

                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知命令「{cmd.keyword}」");
                    return null;
            }
        }

        // ------------------------------------------------------------------
        // 等待原语
        // ------------------------------------------------------------------

        IEnumerator SayCo(VNScriptCommand cmd)
        {
            // SNS 模式：台词不进对话框，改成手机聊天气泡（呈现层不同，语义完全一样，
            // 因此存档点/分支/翻译表全部沿用普通台词的机制）
            if (stage != null && stage.IsSnsOpen) return SnsSayCo(cmd);
            return NormalSayCo(cmd);
        }

        IEnumerator NormalSayCo(VNScriptCommand cmd)
        {
            bool followVoice = _voicePendingForNextSay;
            _voicePendingForNextSay = false;
            string sayText = VNScriptLocale.TextOf(cmd); // 当前语言的译文（缺译回退中文）
            stage.Say(cmd.speaker, cmd.expression, sayText, followVoice);
            _lastSayText = sayText;
            _backlog?.Record(stage.GetDisplayName(cmd.speaker), sayText);

            yield return null; // 让打字机先启动
            if (_skip && stage.dialogue != null) stage.dialogue.CompleteTyping();
            while (stage.dialogue != null && stage.dialogue.IsTyping)
            {
                if (_skip) stage.dialogue.CompleteTyping();
                yield return null;
            }

            _waitingAtSay = true;
            _advance = false;
            float doneTime = Time.time;
            float autoWait = autoDelay + sayText.Length * 0.045f;
            while (!_advance)
            {
                if (_backlog == null || !_backlog.IsOpen)
                {
                    if (_skip && Time.time - doneTime > 0.07f) break;
                    if (_auto && Time.time - doneTime > autoWait) break;
                }
                yield return null;
            }
            _waitingAtSay = false;
            _advance = false;
        }

        /// <summary>
        /// SNS 模式下的台词：渲染成聊天气泡后等玩家推进。
        /// 与普通台词的差别只有呈现——没有打字机、不进回想（聊天窗本身就是历史记录）、
        /// 不吃 Auto/Skip（sns open 时已关闭并屏蔽）。存档点语义完全一致。
        /// </summary>
        IEnumerator SnsSayCo(VNScriptCommand cmd)
        {
            _voicePendingForNextSay = false; // SNS 里的语音走 sns voice 气泡
            var view = stage.sns;
            string sayText = VNScriptLocale.TextOf(cmd);
            _lastSayText = sayText;

            if (string.IsNullOrEmpty(cmd.speaker))
                view.AppendNotice(VNSnsMessage.KindSystem, sayText); // 无名牌旁白 = 居中提示
            else
                view.AppendText(cmd.speaker, sayText);

            _waitingAtSay = true;
            _advance = false;
            while (!_advance) yield return null;
            _waitingAtSay = false;
            _advance = false;
        }

        /// <summary>
        /// sns 命令：手机聊天界面。
        ///   sns open &lt;角色id&gt; [id:会话id] [title:标题] [me:玩家说话者名]
        ///   sns close
        ///   sns voice &lt;发送者&gt; &lt;语音id&gt; [text:文字稿]
        ///   sns image &lt;发送者&gt; &lt;CG id&gt; [unlock:yes|no]
        ///   sns typing [秒]
        ///   sns read
        ///   sns time &lt;自由文本&gt; / sns system &lt;自由文本&gt;
        ///   sns reply [timeout:秒] [late:标签] [lateflag:好感-1] + 「* 候选回复」子行
        /// </summary>
        IEnumerator SnsCo(VNScriptCommand cmd)
        {
            var view = stage != null ? stage.sns : null;
            if (view == null)
            {
                Debug.LogError($"[VNScript] 第 {cmd.line} 行：VNStage 未连线 sns（VNSnsView）");
                yield break;
            }

            string sub = cmd.Arg(0, "").ToLower();
            if (sub != "open" && !view.IsOpen)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：sns {sub} 之前没有 sns open，已忽略");
                yield break;
            }

            switch (sub)
            {
                case "open":
                {
                    string peer = cmd.Arg(1);
                    if (string.IsNullOrEmpty(peer))
                    {
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：sns open 需要对方角色 id");
                        yield break;
                    }
                    SetSkip(false); // SNS 模式不做快进/自动：节奏本身就是演出
                    SetAuto(false);
                    view.Open(stage, peer, cmd.Kw("id"), cmd.Kw("title"), cmd.Kw("me"));
                    break;
                }

                case "close":
                    view.Close();
                    break;

                case "voice":
                    if (string.IsNullOrEmpty(cmd.Arg(2)))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：" +
                                         "sns voice 用法为「sns voice <发送者> <语音id>」");
                    else
                        view.AppendVoice(cmd.Arg(1), cmd.Arg(2), cmd.Kw("text"));
                    break;

                case "image":
                    if (string.IsNullOrEmpty(cmd.Arg(2)))
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：" +
                                         "sns image 用法为「sns image <发送者> <CG id>」");
                    else
                        view.AppendImage(cmd.Arg(1), cmd.Arg(2), cmd.Kw("unlock", "yes") != "no");
                    break;

                case "time":
                case "system":
                    view.AppendNotice(sub == "time"
                        ? VNSnsMessage.KindTime : VNSnsMessage.KindSystem, JoinArgs(cmd, 1));
                    break;

                case "read":
                    view.MarkRead();
                    break;

                case "typing":
                    yield return view.TypingCo(Mathf.Max(0.1f, cmd.ArgF(1, 1.4f)));
                    break;

                case "reply":
                    yield return SnsReplyCo(cmd, view);
                    break;

                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知的 sns 子命令「{sub}」" +
                                     "（open/close/voice/image/typing/read/time/system/reply）");
                    break;
            }
        }

        /// <summary>
        /// sns reply：候选回复面板。选项行语法同 choice（支持 if: / flag: / -&gt; 标签），
        /// 但不支持 cost:。timeout: 需配 late: 指定"已读不回"的去向。
        /// </summary>
        IEnumerator SnsReplyCo(VNScriptCommand cmd, VNSnsView view)
        {
            if (cmd.options == null || cmd.options.Count == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：sns reply 下面没有任何「* 回复」行");
                yield break;
            }

            SetSkip(false); // 到玩家抉择必停，同 choice

            var visible = new List<int>();
            for (int i = 0; i < cmd.options.Count; i++)
            {
                var candidate = cmd.options[i];
                if (!string.IsNullOrEmpty(candidate.costOp))
                    Debug.LogWarning($"[VNScript] 第 {candidate.line} 行：" +
                                     "sns reply 不支持 cost:，该参数被忽略");
                if (!string.IsNullOrEmpty(candidate.condition) &&
                    !VNFlags.Evaluate(candidate.condition, candidate.line)) continue;
                visible.Add(i);
            }
            if (visible.Count == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：sns reply 所有回复的 if: 条件都不满足，" +
                                 "为避免卡死改为全部显示");
                for (int i = 0; i < cmd.options.Count; i++) visible.Add(i);
            }

            var texts = new List<string>();
            foreach (int at in visible) texts.Add(VNScriptLocale.TextOf(cmd.options[at]));

            float timeout = cmd.KwF("timeout", 0f);
            string lateLabel = cmd.Kw("late");
            if (timeout > 0.01f && string.IsNullOrEmpty(lateLabel))
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：sns reply 的 timeout: 需要配 " +
                                 "late:<标签> 指定超时（已读不回）的去向，本次按不限时处理");
                timeout = 0f;
            }

            int picked = -1;
            yield return view.ReplyCo(texts, timeout, i => picked = i);

            if (picked < 0) // 超时 = 已读不回
            {
                string lateFlag = cmd.Kw("lateflag");
                if (!string.IsNullOrEmpty(lateFlag)) VNFlags.Apply(lateFlag);
                if (!string.IsNullOrEmpty(lateLabel)) JumpTo(lateLabel, cmd.line);
                yield break;
            }

            var opt = cmd.options[visible[picked]];
            if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
            if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
        }

        IEnumerator WaitCo(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime * (_skip ? skipTimeScale : 1f); // 快进时停顿也加速
                yield return null;
            }
        }

        static IEnumerator WaitTween(Tween t)
        {
            if (t == null) yield break;
            yield return t.WaitForCompletion();
        }

        IEnumerator ChoiceCo(VNScriptCommand cmd)
        {
            if (cmd.options == null || cmd.options.Count == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：choice 下面没有任何「* 选项」行");
                yield break;
            }
            if (stage.choicePanel == null)
            {
                Debug.LogError($"[VNScript] 第 {cmd.line} 行：VNStage 未连线 choicePanel");
                yield break;
            }

            SetSkip(false); // 到选项必停，玩家必须亲自选

            // if: 条件不满足的选项直接隐藏（visible = 原始索引映射表）
            var visible = new List<int>();
            for (int i = 0; i < cmd.options.Count; i++)
            {
                var candidate = cmd.options[i];
                if (!string.IsNullOrEmpty(candidate.condition) &&
                    !VNFlags.Evaluate(candidate.condition, candidate.line)) continue;
                visible.Add(i);
            }
            if (visible.Count == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：choice 所有选项的 if: 条件都不满足，" +
                                 "为避免卡死改为全部显示");
                for (int i = 0; i < cmd.options.Count; i++) visible.Add(i);
            }

            // cost: 花费展示与付得起判定（付不起 = 置灰）
            var panelOptions = new VNChoicePanel.Option[visible.Count];
            bool anyInteractable = false;
            for (int k = 0; k < visible.Count; k++)
            {
                var candidate = cmd.options[visible[k]];
                var po = new VNChoicePanel.Option
                    { text = VNScriptLocale.TextOf(candidate) }; // 显示译文；匹配按索引，不受影响
                if (!string.IsNullOrEmpty(candidate.costOp) && _statsHud != null)
                {
                    po.costLabel = _statsHud.FormatCostLabel(candidate.costOp);
                    po.interactable = _statsHud.CanAfford(candidate.costOp);
                }
                anyInteractable |= po.interactable;
                panelOptions[k] = po;
            }
            if (!anyInteractable)
            {
                Debug.LogError($"[VNScript] 第 {cmd.line} 行：choice 所有可见选项都付不起 cost:，" +
                               "为避免卡死全部解禁——请给玩家留一个免费选项");
                foreach (var po in panelOptions) po.interactable = true;
            }

            int chosen = -1;
            stage.choicePanel.Show(panelOptions, i => chosen = i);
            while (chosen < 0) yield return null;

            var opt = cmd.options[visible[chosen]];
            _backlog?.Record(VNLocale.T("backlog.choice"), VNScriptLocale.TextOf(opt));
            if (!string.IsNullOrEmpty(opt.costOp)) _statsHud?.ApplyCost(opt.costOp, opt.line);
            if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
            if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
            // 无跳转目标 = 顺序继续（choice 块后的下一条命令）
        }

        /// <summary>
        /// event 命令：暂停剧本 → 调起事件模块（地图/战斗/迷你游戏）→ 按结果分支。
        /// 结果名匹配「* 结果行」跳转；整数结果同时写入 flag「事件结果」；
        /// 事件期间禁用全部剧本快捷键，存档天然被"仅台词处可存"挡住。
        /// </summary>
        IEnumerator EventCo(VNScriptCommand cmd)
        {
            string id = cmd.Arg(0);
            if (string.IsNullOrEmpty(id))
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：event 需要模块 id");
                yield break;
            }
            if (stage == null || stage.eventRegistry == null)
            {
                Debug.LogError($"[VNScript] 第 {cmd.line} 行：VNStage 未连线 eventRegistry，" +
                               "无法执行 event（重建剧本演示场景或手动挂 VNEventRegistry）");
                yield break;
            }

            var canvas = stage.characterLayer != null
                ? stage.characterLayer.GetComponentInParent<Canvas>() : null;
            if (canvas != null) canvas = canvas.rootCanvas;

            SetSkip(false); // 到玩法必停，同 choice
            SetAuto(false);

            var module = stage.eventRegistry.Create(id, canvas, cmd.line);
            if (module == null) yield break; // 模块缺失：告警后顺序继续

            _eventActive = true;
            _activeEventModule = module;
            stage.dialogue?.HideBox();
            ApplyGameplayHudVisible(false);

            var outcomes = new List<string>();
            if (cmd.options != null)
                foreach (var opt in cmd.options) outcomes.Add(opt.text);
            var ctx = new VNEventContext
            {
                eventId = id,
                stage = stage,
                kwargs = cmd.kwargs,
                outcomes = outcomes,
                line = cmd.line,
            };
            string result = null;
            module.Launch(ctx, r => result = r ?? "");
            while (result == null) yield return null;

            _activeEventModule = null;
            bool recordInBacklog = module.RecordInBacklog; // 销毁前读取
            Destroy(module.gameObject);
            stage.dialogue?.Show();
            ApplyGameplayHudVisible(true);
            _eventActive = false;

            if (recordInBacklog)
                _backlog?.Record(VNLocale.T("backlog.event"), $"{id} → {result}");
            if (int.TryParse(result, out int numeric))
                VNFlags.Set("事件结果", numeric);

            if (cmd.options == null || cmd.options.Count == 0) yield break;
            foreach (var opt in cmd.options)
            {
                if (opt.text != result) continue;
                if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
                if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
                yield break;
            }
            Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：事件「{id}」返回结果" +
                             $"「{result}」没有对应的「* 结果行」，顺序继续");
        }

        IEnumerator CameraCo(VNScriptCommand cmd)
        {
            if (stage.vnCamera == null) yield break;
            string move = cmd.Arg(0, "reset").ToLower();
            Tween t = null;
            switch (move)
            {
                case "pushin":
                    t = stage.vnCamera.PushIn(cmd.ArgF(1, 1.06f), cmd.ArgF(2, 4f), FocusOf(cmd));
                    break;
                case "snapzoom":
                    t = stage.vnCamera.SnapZoom(cmd.ArgF(1, 1.12f), 0.16f, FocusOf(cmd), stage.screenShake);
                    break;
                case "pan":
                {
                    // camera pan 亚里沙 / camera pan 380
                    Vector2 target;
                    var c = stage.Get(cmd.Arg(1));
                    if (c != null) target = c.rect.anchoredPosition;
                    else target = new Vector2(cmd.ArgF(1, 0f), 0f);
                    t = stage.vnCamera.Pan(target, 0.6f, cmd.ArgF(2, 1.2f));
                    break;
                }
                case "dolly":
                    t = stage.vnCamera.DollyZoom(cmd.ArgF(1, 1.3f), cmd.ArgF(2, 3f));
                    break;
                case "reset":
                    t = stage.vnCamera.ResetCamera(cmd.ArgF(1, 1f));
                    break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：未知运镜「{move}」");
                    break;
            }
            if (t != null) yield return t.WaitForCompletion();
        }

        static Ease ParseEase(string name, Ease def)
        {
            if (!string.IsNullOrEmpty(name) &&
                System.Enum.TryParse(name, true, out Ease e)) return e;
            return def;
        }

        /// <summary>mark 的 pos:x,y（相对立绘尺寸的归一化偏移）；没写或写坏 = 用角色资产的锚点</summary>
        static Vector2? ParseMarkPos(string value, int line)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var parts = value.Split(',');
            if (parts.Length == 2 &&
                float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y))
                return new Vector2(x, y);

            Debug.LogWarning($"[VNScript] 第 {line} 行：mark 的 pos 应写成 pos:0.2,0.36（两个数字、逗号分隔、不能有空格），" +
                             $"当前为「{value}」，本次改用角色资产里的默认锚点");
            return null;
        }

        /// <summary>
        /// 解析 liquid 命令的参数。运行时执行与"从选中行播放"的状态重建共用它，
        /// 两边各写一份迟早会漂移出不一致的默认值。
        /// 开关位取第二个位置参数（liquid spray on / liquid spray off），省略视作 on。
        /// </summary>
        static VNLiquidArgs ParseLiquidArgs(VNScriptCommand cmd)
        {
            var a = VNLiquidArgs.Default;
            a.type = cmd.Kw("type");

            string sw = cmd.Arg(1);
            a.on = sw != "off" && sw != "false" && sw != "0";

            a.x = cmd.KwF("x", a.x);
            a.y = cmd.KwF("y", a.y);
            a.power = cmd.KwF("power", a.power);
            a.dir = cmd.KwF("dir", a.dir);
            a.spread = cmd.KwF("spread", a.spread);
            a.rate = cmd.KwF("rate", a.rate);
            a.screen = cmd.KwF("screen", a.screen);
            a.amount = cmd.KwF("amount", a.amount);
            return a;
        }

        /// <summary>已由 bg 转场在盖屏瞬间应用过首镜头的 camseq（该 camseq 要跳过首点）</summary>
        VNScriptCommand _precutDone;

        /// <summary>
        /// camseq start:cut 与 bg 转场的衔接：若紧跟本条 bg 的命令是 start:cut
        /// 且首点为瞬切（时长 0）的 camseq，返回一个"转场盖住画面瞬间执行的
        /// 首镜头瞬切"动作（与换背景图同帧 → 转场揭示时画面已在首镜头视角）。
        /// </summary>
        System.Action PrecutFor(VNScriptCommand bgCmd)
        {
            if (bgCmd.isAsync) return null;
            if (string.IsNullOrEmpty(bgCmd.Kw("transition"))) return null;
            if (stage.vnCamera == null) return null;
            if (_commands == null || _index >= _commands.Count) return null;

            var next = _commands[_index];
            if (next.keyword != "camseq" || next.Kw("start") != "cut") return null;
            if (next.camPoints == null || next.camPoints.Count == 0) return null;

            var first = next.camPoints[0];
            if (first.duration > 0.001f)
            {
                Debug.LogWarning($"[VNScript] 第 {next.line} 行：start:cut 要求首个路径点时长为 0，" +
                                 "已按普通 camseq 执行");
                return null;
            }
            return () =>
            {
                var p = stage.ResolveCamPoint(first.point, first.line);
                if (!p.HasValue) return;
                stage.vnCamera.Cut(p.Value, Mathf.Max(0.1f, first.zoom));
                _precutDone = next;
            };
        }

        IEnumerator CamseqCo(VNScriptCommand cmd)
        {
            if (stage.vnCamera == null) yield break;
            if (cmd.camPoints == null || cmd.camPoints.Count == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：camseq 下面没有任何「> 路径点」行");
                yield break;
            }

            // start:cut 的首点已在上一条 bg 转场盖屏时应用过 → 跳过它
            bool skipFirst = _precutDone == cmd;
            if (skipFirst) _precutDone = null;

            // 执行时才解析点位（角色可能刚移动过）
            var list = new List<VNCamera.Waypoint>();
            // stay 要沿用「上一个点」的位置与 zoom。哪怕首点被 skipFirst 跳过不进 list，
            // 也得先把它解析出来当基准——否则 start:cut 后面第一个 stay 就没得沿用
            Vector2? lastPoint = null;
            float lastZoom = 1f;
            for (int i = 0; i < cmd.camPoints.Count; i++)
            {
                var def = cmd.camPoints[i];
                Vector2 resolved;
                float zoom;
                if (VNCamWaypointDef.IsStay(def.point))
                {
                    if (!lastPoint.HasValue)
                    {
                        Debug.LogWarning($"[VNScript] 第 {def.line} 行：stay 没有「上一个点」" +
                                         "可沿用（不能当第一个路径点），已跳过");
                        continue;
                    }
                    resolved = lastPoint.Value;
                    zoom = lastZoom;
                }
                else
                {
                    var p = stage.ResolveCamPoint(def.point, def.line);
                    if (!p.HasValue) continue; // 已告警，跳过该点
                    resolved = p.Value;
                    zoom = def.zoom;
                }
                lastPoint = resolved;
                lastZoom = zoom;

                if (skipFirst && i == 0) continue; // 首点已由 bg 转场盖屏时应用过

                bool easeSet = System.Enum.TryParse(def.ease, true, out Ease easeVal);
                // 认不出的 shake 值 parser 已经告警过并置空，这里拿到的一定是合法的
                VNShakeSpec.TryParse(def.shake, out VNShakeSpec shakeSpec);
                list.Add(new VNCamera.Waypoint
                {
                    point = resolved,
                    zoom = zoom,
                    duration = def.duration,
                    ease = easeVal,
                    easeSet = easeSet,
                    fade = def.fade,
                    hold = def.hold,
                    shake = shakeSpec,
                });
            }

            // start:fade = 开始时从当前画面叠化到首镜头；end:fade = 走完后叠化回复位全图
            float startFade = cmd.Kw("start") == "fade" ? cmd.KwF("startfade", 0.6f) : 0f;
            float endFade = cmd.Kw("end") == "fade" ? cmd.KwF("endfade", 0.6f) : 0f;

            // mode: 缩放模式（谁跟着 zoom 缩放）。整段一个模式——逐点切换会让立绘尺寸跳变
            var mode = VNCamZoomMode.Both;
            string modeArg = cmd.Kw("mode");
            if (!string.IsNullOrEmpty(modeArg) &&
                !System.Enum.TryParse(modeArg, true, out mode))
            {
                Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：认不出的 mode:{modeArg}，" +
                                 "已按 both 处理（可用 both/depth/bg/char）");
                mode = VNCamZoomMode.Both;
            }

            if (list.Count == 0 && endFade <= 0f) yield break;
            yield return stage.vnCamera.PlayPathCo(list, startFade, endFade, stage.screenShake, mode);
        }

        /// <summary>camera 命令的 focus:角色id 参数 → 该角色的画布坐标</summary>
        Vector2? FocusOf(VNScriptCommand cmd)
        {
            var id = cmd.Kw("focus");
            if (string.IsNullOrEmpty(id)) return null;
            var c = stage.Get(id);
            return c != null ? c.rect.anchoredPosition : (Vector2?)null;
        }
    }
}
