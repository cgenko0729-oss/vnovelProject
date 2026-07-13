using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 剧本解释器（P0+P1+P2）：逐条执行 VNScriptParser 解析出的命令。
    ///   - 默认同步执行（等待演出完成），行尾 @ = 异步不等待
    ///   - 台词行：等打字机播完 + 玩家点击/Enter/空格推进（打字中按键 = 催促）
    ///   - P1：label/jump/choice/flag/if 分支
    ///   - P2：F5 快速存档 / F9 快速读档、H(或滚轮上滑) 回想、A 自动模式、S 快进
    /// </summary>
    public class VNScriptRunner : MonoBehaviour
    {
        [Tooltip("舞台管理器")]
        public VNStage stage;

        [Tooltip("剧本文件（.vn.txt）")]
        public TextAsset script;

        [Tooltip("启动时自动播放")]
        public bool playOnStart = true;

        [Header("Auto / Skip")]
        [Tooltip("自动模式：打字完后的基础等待秒数（另按字数追加）")]
        public float autoDelay = 1.4f;

        [Tooltip("快进时的演出加速倍率（DOTween 全局 timeScale）")]
        public float skipTimeScale = 4f;

        List<VNScriptCommand> _commands;
        readonly Dictionary<string, int> _labels = new Dictionary<string, int>();
        int _index;
        bool _running;
        bool _advance;
        Coroutine _co;

        VNBacklog _backlog;
        bool _auto;
        bool _skip;
        bool _waitingAtSay;   // 只有停在台词上时才允许存档
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
            if (playOnStart && script != null) Play(script);
            IsInitialized = true;
        }

        // ------------------------------------------------------------------
        // 播放控制
        // ------------------------------------------------------------------

        public void Play(TextAsset asset) => Play(asset.text);

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
            var characters = new Dictionary<string, VNSaveData.CharSave>();
            var loopingSe = new HashSet<string>();
            var volumes = new Dictionary<string, float>();
            string focus = null;
            VNScriptCommand lastCameraCut = null;
            bool hasBranching = false;

            VNFlags.Clear();
            for (int i = 0; i < exclusiveIndex && i < _commands.Count; i++)
            {
                VNScriptCommand cmd = _commands[i];
                switch (cmd.keyword)
                {
                    case "bg":
                        snapshot.backgroundId = cmd.Arg(0);
                        break;
                    case "weather":
                        snapshot.weather = cmd.Arg(0, VNWeather.None.ToString());
                        break;
                    case "mood":
                        snapshot.mood = cmd.Arg(0, VNMood.Neutral.ToString());
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
                        snapshot.bgm = cmd.Arg(0, "play") == "stop" ? null : cmd.Arg(1);
                        break;
                    case "se":
                        if (cmd.Arg(0) == "stop") loopingSe.Remove(cmd.Arg(1));
                        else if (cmd.args.Contains("loop")) loopingSe.Add(cmd.Arg(0));
                        break;
                    case "volume":
                        volumes[cmd.Arg(0, "bgm")] = cmd.ArgF(1, 1f);
                        break;
                    case "fx":
                    {
                        string name = cmd.Arg(0);
                        string value = cmd.Arg(1);
                        if (name == "focus") focus = value == "off" ? null : value;
                        else if (value == "off") snapshot.fxOn.Remove(name);
                        else if (!snapshot.fxOn.Contains(name)) snapshot.fxOn.Add(name);
                        break;
                    }
                    case "flag":
                        ApplyDebugFlag(cmd);
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
                foreach (string id in loopingSe)
                    stage.vnAudio.PlaySe(id, true);
            }
            if (!string.IsNullOrEmpty(focus)) stage.Fx("focus", focus);
            RestoreDebugCamera(lastCameraCut);

            if (hasBranching)
                Debug.LogWarning("[VNScript] 前置状态包含 choice/jump/if；调试重建按文件顺序处理，" +
                                 "不会推断之前的玩家选择路径");
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

        static void ApplyDebugFlag(VNScriptCommand cmd)
        {
            string name = cmd.Arg(0);
            string value = cmd.Arg(1);
            if (string.IsNullOrEmpty(name)) return;
            if (string.IsNullOrEmpty(value)) VNFlags.Apply(name);
            else if (value.StartsWith("+") || value.StartsWith("-"))
                VNFlags.Apply(name + value);
            else if (int.TryParse(value, out int parsed)) VNFlags.Set(name, parsed);
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
            _commands = VNScriptParser.Parse(source);

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
            if (!_waitingAtSay)
            {
                VNToast.Show("演出进行中，此刻不能存档");
                return;
            }
            var data = new VNSaveData
            {
                commandIndex = _currentSayIndex,
                lastLine = _lastSayText,
            };
            stage.CaptureSnapshot(data);
            VNSaveSystem.Save(slot, data);
            VNToast.Show($"已保存（槽位 {slot}）");
        }

        public void LoadFrom(int slot)
        {
            var data = VNSaveSystem.Load(slot);
            if (data == null)
            {
                VNToast.Show($"槽位 {slot} 没有存档");
                return;
            }
            SetSkip(false);
            SetAuto(false);
            Stop();
            stage.RestoreSnapshot(data);
            VNToast.Show($"已读取（槽位 {slot}）");
            ResumeAt(data.commandIndex);
        }

        // ------------------------------------------------------------------
        // 模式
        // ------------------------------------------------------------------

        public void SetAuto(bool on)
        {
            _auto = on;
            if (on) SetSkip(false);
            UpdateModeLabel();
            VNToast.Show(on ? "自动模式 开" : "自动模式 关");
        }

        public void SetSkip(bool on)
        {
            if (_skip == on) return;
            _skip = on;
            if (on) _auto = false;
            DOTween.timeScale = on ? skipTimeScale : 1f;
            UpdateModeLabel();
            VNToast.Show(on ? "快进 开" : "快进 关");
        }

        void UpdateModeLabel() =>
            VNToast.SetMode(_skip ? "SKIP ▶▶" : _auto ? "AUTO ▶" : null);

        void OnDestroy()
        {
            if (_skip) DOTween.timeScale = 1f; // 别把加速留给别的场景
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            // 回想面板打开期间：只处理关闭，不推进剧情
            if (_backlog != null && _backlog.IsOpen)
            {
                if (kb.hKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)
                    _backlog.Close();
                return;
            }

            if (kb.hKey.wasPressedThisFrame ||
                (mouse != null && mouse.scroll.ReadValue().y > 0.1f))
            {
                _backlog?.Open();
                return;
            }

            if (kb.f5Key.wasPressedThisFrame) { SaveTo(1); return; }
            if (kb.f9Key.wasPressedThisFrame) { LoadFrom(1); return; }
            if (kb.aKey.wasPressedThisFrame) { SetAuto(!_auto); return; }
            if (kb.sKey.wasPressedThisFrame) { SetSkip(!_skip); return; }

            if (!_running) return;

            bool pressed =
                kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame
                || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (!pressed) return;

            // 手动推进会顺手退出快进（惯例）
            if (_skip) SetSkip(false);

            if (stage != null && stage.dialogue != null && stage.dialogue.IsTyping)
                stage.dialogue.CompleteTyping();
            else
                _advance = true;
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
                    stage.mood?.SetMood(
                        VNScriptParser.ParseEnum(cmd.Arg(0), VNMood.Neutral, cmd.line));
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
                    // bgm play 黄昏之歌 [fade:2] / bgm stop [fade:3]
                    string sub = cmd.Arg(0, "play");
                    float fade = 1.5f;
                    if (float.TryParse(cmd.Kw("fade"), out float f)) fade = f;
                    if (sub == "stop") stage.vnAudio?.StopBgm(fade);
                    else if (sub == "play") stage.vnAudio?.PlayBgm(cmd.Arg(1), fade, cmd.line);
                    else Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：bgm 用法为 bgm play <id> 或 bgm stop");
                    return null;
                }

                case "se":
                {
                    // se 雨声 loop / se 心跳 / se stop 雨声
                    if (cmd.Arg(0) == "stop")
                        stage.vnAudio?.StopSe(cmd.Arg(1));
                    else
                        stage.vnAudio?.PlaySe(cmd.Arg(0), cmd.args.Contains("loop"), cmd.line);
                    return null;
                }

                case "voice":
                    stage.vnAudio?.PlayVoice(cmd.Arg(0), cmd.line);
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

                case "flag":
                {
                    // flag 名字 / flag 名字 3 / flag 名字 +1（也支持 flag 名字+1）
                    string name = cmd.Arg(0);
                    string value = cmd.Arg(1);
                    if (string.IsNullOrEmpty(value))
                        VNFlags.Apply(name);
                    else if (value.StartsWith("+") || value.StartsWith("-"))
                        VNFlags.Apply(name + value);
                    else if (int.TryParse(value, out int v))
                        VNFlags.Set(name, v);
                    else
                        Debug.LogWarning($"[VNScript] 第 {cmd.line} 行：flag 值「{value}」无法识别");
                    return null;
                }

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

                case "choice":
                    return ChoiceCo(cmd);

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
            stage.Say(cmd.speaker, cmd.expression, cmd.text);
            _lastSayText = cmd.text;
            _backlog?.Record(stage.GetDisplayName(cmd.speaker), cmd.text);

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
            float autoWait = autoDelay + cmd.text.Length * 0.045f;
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

            var texts = new string[cmd.options.Count];
            for (int i = 0; i < texts.Length; i++) texts[i] = cmd.options[i].text;

            int chosen = -1;
            stage.choicePanel.Show(texts, i => chosen = i);
            while (chosen < 0) yield return null;

            var opt = cmd.options[chosen];
            _backlog?.Record("选择", opt.text);
            if (!string.IsNullOrEmpty(opt.flagOp)) VNFlags.Apply(opt.flagOp);
            if (!string.IsNullOrEmpty(opt.jumpLabel)) JumpTo(opt.jumpLabel, opt.line);
            // 无跳转目标 = 顺序继续（choice 块后的下一条命令）
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
