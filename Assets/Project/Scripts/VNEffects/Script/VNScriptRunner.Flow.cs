using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNScriptRunner
    {
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
    }
}
