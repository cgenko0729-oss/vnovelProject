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
        int _index;
        bool _running;
        bool _advance;
        Coroutine _co;

        VNBacklog _backlog;
        VNSaveLoadPanel _saveLoadPanel;
        VNConfigPanel _configPanel;
        VNQuickToolbar _quickToolbar;
        VNQuestLog _questLog;
        VNStatsHud _statsHud;
        VNInventory _inventory;
        VNCalendarHud _calendarHud;
        Coroutine _saveCaptureCo;
        int _saveCaptureToken;
        float _timeScaleBeforeMenu = 1f;
        bool _menuPaused;
        bool _uiHidden;
        bool _auto;
        bool _skip;
        bool _waitingAtSay;   // 只有停在台词上时才允许存档
        bool _eventActive;    // 事件模块进行中：输入全部交给模块，禁用快捷键
        VNEventModule _activeEventModule; // 进行中的模块（Stop 时清理用）
        bool _voicePendingForNextSay; // voice 命令一次性绑定到下一句对白的口型
        int _currentSayIndex; // 正在显示的台词命令索引（存档恢复点）
        string _lastSayText = "";

        public bool IsRunning => _running;
        public bool IsAuto => _auto;
        public bool IsSkipping => _skip;
        public bool IsInitialized { get; private set; }
        public int CurrentLine =>
            _running && _index > 0 && _index <= _commands.Count ? _commands[_index - 1].line : 0;

        void Start()
        {
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
            if (_calendarHud == null)
            {
                _calendarHud = FindFirstObjectByType<VNCalendarHud>();
                if (_calendarHud == null) // 「月份」flag 不存在时它自动隐藏，常驻无害
                    _calendarHud = new GameObject("VNCalendarHud").AddComponent<VNCalendarHud>();
            }
            EnsureSaveLoadPanel();
            EnsureQuickToolbar();
            EnsureConfigPanel(); // 启动时应用 PlayerPrefs 中保存的音量、文字速度与显示模式
            if (playOnStart && script != null) Play(script);
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
            if (asset != null) script = asset; // 记住剧本资产：翻译表按剧本名查找
            Play(asset.text);
        }

        public void Play(string source)
        {
            Prepare(source);
            ResumeAt(0);
        }

        /// <summary>
        /// 编辑器调试入口：从指定剧本物理行或其后的第一条有效命令开始播放。
        /// 返回实际开始的物理行；找不到可执行命令时返回 -1。
        /// </summary>
        public int PlayFromSourceLine(string source, int sourceLine) =>
            PlayFromSourceLine(source, sourceLine, false);

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
            if (rebuildState) RebuildStateBefore(start);
            ResumeAt(start);
            Debug.Log($"[VNScript] 调试：从第 {actualLine} 行开始播放" +
                      (rebuildState ? "（已重建前置状态）" : "（直接跳转）"));
            return actualLine;
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
                    case "weather":
                        snapshot.weather = cmd.Arg(0, VNWeather.None.ToString());
                        break;
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
                            snapshot.mood = VNMood.Neutral.ToString();
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
                    case "show":
                        RebuildShowState(characters, cmd);
                        break;
                    case "hide":
                        characters.Remove(cmd.Arg(0));
                        break;
                    case "move":
                        RebuildMoveState(characters, cmd);
                        break;
                    case "say":
                        if (!string.IsNullOrEmpty(cmd.expression) &&
                            characters.TryGetValue(cmd.speaker, out var speaking))
                            speaking.expr = cmd.expression;
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
                    case "choice":
                    case "jump":
                    case "if":
                    case "event": // 事件结果无法推断，不重放，同分支处理
                        hasBranching = true;
                        break;
                }
            }

            foreach (var character in characters.Values)
                snapshot.characters.Add(character);

            stage.RestoreSnapshot(snapshot, true);
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
            characters[id] = new VNSaveData.CharSave
                { id = id, x = x, expr = cmd.Kw("expr") };
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
            LoadCommands(source);
        }

        void LoadCommands(string source)
        {
            _commands = VNScriptParser.Parse(source);

            // 剧本全文预热进 TMP 动态字体图集：光栅化成本挪到加载期，台词零卡顿
            VNFont.Prewarm(source);

            // 非中文语言：给台词/选项标注译文（缺译回退中文，命令流不变）
            VNScriptLocale.Apply(_commands, script != null ? script.name : null);

            _labels.Clear();
            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].keyword != "label") continue;
                string name = _commands[i].Arg(0);
                if (string.IsNullOrEmpty(name))
                    Debug.LogError($"[VNScript] 第 {_commands[i].line} 行：label 缺少名字");
                else if (_labels.ContainsKey(name))
                    Debug.LogError($"[VNScript] 第 {_commands[i].line} 行：label「{name}」重复定义");
                else
                    _labels[name] = i;
            }
        }

        void SwitchChapter(string chapterName, int fromLine)
        {
            string wanted = NormalizeChapterName(chapterName);
            TextAsset target = null;

            if (script != null && NormalizeChapterName(script.name) == wanted)
                target = script;

            if (target == null)
            {
                foreach (var chapter in chapters)
                {
                    if (chapter == null || NormalizeChapterName(chapter.name) != wanted) continue;
                    target = chapter;
                    break;
                }
            }

            if (target == null)
            {
                Debug.LogError($"[VNScript] 第 {fromLine} 行：找不到章节「{chapterName}」，请在 VNScriptRunner 的 Chapters 列表中登记该剧本");
                return;
            }

            script = target;
            LoadCommands(target.text);
            _index = 0;
            _currentSayIndex = 0;
            Debug.Log($"[VNScript] 已切换到章节「{target.name}」");
        }

        static string NormalizeChapterName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            string normalized = name.Trim();
            if (normalized.EndsWith(".txt", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 4);
            if (normalized.EndsWith(".vn", System.StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 3);
            return normalized.ToLowerInvariant();
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
            _index = Mathf.Clamp(index, 0, _commands.Count);
            _currentSayIndex = _index;
            _co = StartCoroutine(Run());
        }

        public void Stop()
        {
            if (_co != null) StopCoroutine(_co);
            _co = null;
            _running = false;
            _waitingAtSay = false;
            _voicePendingForNextSay = false;
            CleanupActiveEvent();
            stage?.StopSpeaking();
        }

        /// <summary>剧本中断时清理进行中的事件模块（正常结束由 EventCo 自己收尾）</summary>
        void CleanupActiveEvent()
        {
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
                    break;
                }

                case "pass":
                {
                    int months = Mathf.Max(1, (int)cmd.KwF("months", 1f));
                    int month = VNFlags.Get(VNCalendarHud.MonthFlag);
                    if (month <= 0) month = 1;
                    month = (month - 1 + months) % 12 + 1;
                    VNFlags.Set(VNCalendarHud.MonthFlag, month);

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

        void JumpTo(string label, int fromLine)
        {
            if (_labels.TryGetValue(label, out int idx))
                _index = idx;
            else
                Debug.LogError($"[VNScript] 第 {fromLine} 行：跳转目标 label「{label}」不存在");
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
            stage.CaptureSnapshot(data);
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
            if (!string.IsNullOrEmpty(data.chapter))
                SwitchChapter(data.chapter, 0);
            stage.RestoreSnapshot(data);
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

        public void RequestConfigPanel()
        {
            EnsureConfigPanel();
            PauseForSaveLoadMenu();
            _configPanel.Open();
        }

        public void SetInterfaceHidden(bool hidden)
        {
            _uiHidden = hidden;
            if (stage != null && stage.dialogue != null)
                stage.dialogue.SetInterfaceVisible(!hidden);
            _statsHud?.SetHudVisible(!hidden);
            _calendarHud?.SetVisible(!hidden);
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

            // 隐藏 UI 后，第一次操作只恢复界面，不会顺便推进台词。
            if (_uiHidden)
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

            if (kb.hKey.wasPressedThisFrame ||
                (mouse != null && mouse.scroll.ReadValue().y > 0.1f))
            {
                _backlog?.Open();
                return;
            }

            if (kb.jKey.wasPressedThisFrame)
            {
                _questLog?.Open();
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

            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                SetInterfaceHidden(true);
                return;
            }

            if (kb.f5Key.wasPressedThisFrame) { RequestSavePanel(); return; }
            if (kb.f9Key.wasPressedThisFrame) { RequestLoadPanel(); return; }
            if (kb.qKey.wasPressedThisFrame) { QuickSave(); return; }
            if (kb.lKey.wasPressedThisFrame) { QuickLoad(); return; }
            if (kb.aKey.wasPressedThisFrame) { SetAuto(!_auto); return; }
            if (kb.sKey.wasPressedThisFrame) { SetSkip(!_skip); return; }

            if (!_running) return;

            // 左键推进：整个画面都是 uGUI（背景/立绘/对话框都是 Canvas 里的 Image），
            // IsPointerOverGameObject() 恒为 true 会把点击全部拦掉；
            // 只有点在可交互控件（按钮/滑条等 Selectable）上才不推进。
            bool pressed = kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame ||
                           (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                            !IsPointerOverInteractiveUi(mouse));
            if (!pressed) return;

            // 手动推进会顺手退出快进（惯例）
            if (_skip) SetSkip(false);

            if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
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
                var cmd = _commands[_index++];
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
                    return WaitTween(stage.SetBackground(
                        cmd.Arg(0), cmd.Kw("transition"), cmd.line, PrecutFor(cmd)));

                case "cg":
                    // cg <id> [transition:Type] [chars:keep] [fx:keep] / cg off [transition:Type]
                    if (cmd.Arg(0) == "off")
                        return WaitTween(stage.HideCg(cmd.Kw("transition"), cmd.line));
                    return WaitTween(stage.ShowCg(cmd.Arg(0), cmd.Kw("transition"),
                        cmd.Kw("chars") == "keep", cmd.Kw("fx") == "keep", cmd.line));

                case "show":
                    return WaitTween(stage.Show(cmd.Arg(0), cmd.Kw("at"),
                        cmd.Kw("expr"), cmd.Kw("with"), cmd.line));

                case "hide":
                    return WaitTween(stage.Hide(cmd.Arg(0), cmd.Kw("with", "fade"), cmd.line));

                case "emote":
                    return WaitTween(stage.Emote(cmd.Arg(0), cmd.Arg(1), cmd.line));

                case "weather":
                    stage.weather?.SetWeather(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNWeather.None, cmd.line));
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
                    // if 条件 jump 标签   （条件内不能有空格，如 好感度>=2）
                    string cond = cmd.Arg(0);
                    string action = cmd.Arg(1);
                    if (action != "jump" || string.IsNullOrEmpty(cmd.Arg(2)))
                    {
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：if 语法应为「if 条件 jump 标签」");
                        return null;
                    }
                    if (VNFlags.Evaluate(cond, cmd.line))
                        JumpTo(cmd.Arg(2), cmd.line);
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
            for (int i = skipFirst ? 1 : 0; i < cmd.camPoints.Count; i++)
            {
                var def = cmd.camPoints[i];
                var p = stage.ResolveCamPoint(def.point, def.line);
                if (!p.HasValue) continue; // 已告警，跳过该点
                bool easeSet = System.Enum.TryParse(def.ease, true, out Ease easeVal);
                list.Add(new VNCamera.Waypoint
                {
                    point = p.Value,
                    zoom = def.zoom,
                    duration = def.duration,
                    ease = easeVal,
                    easeSet = easeSet,
                    fade = def.fade,
                });
            }

            // start:fade = 开始时从当前画面叠化到首镜头；end:fade = 走完后叠化回复位全图
            float startFade = cmd.Kw("start") == "fade" ? cmd.KwF("startfade", 0.6f) : 0f;
            float endFade = cmd.Kw("end") == "fade" ? cmd.KwF("endfade", 0.6f) : 0f;

            if (list.Count == 0 && endFade <= 0f) yield break;
            yield return stage.vnCamera.PlayPathCo(list, startFade, endFade);
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
