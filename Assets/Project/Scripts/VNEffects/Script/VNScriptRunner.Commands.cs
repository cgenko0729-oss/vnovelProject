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
