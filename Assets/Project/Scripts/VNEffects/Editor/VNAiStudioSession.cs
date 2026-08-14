using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>一轮试聊的全部展示数据。[Serializable] 是为了跟着窗口活过域重载。</summary>
    [Serializable]
    public class VNAiStudioTurn
    {
        public string playerSaid;        // 进入这一轮的玩家发言（第一轮为空 = 开场）
        public string reply;
        public string emotion;
        public string mark;
        public int affectionDelta;
        public bool shouldEnd;
        public bool degraded;            // 走了兜底台词

        public List<string> optionTexts = new List<string>();
        public List<string> optionTones = new List<string>();
        public int pickedIndex = -1;

        public float seconds;
        public int promptTokens, outputTokens, thoughtsTokens;
        public double costUsd;

        public string systemPrompt;      // 这一轮实际发出去的那份（逐轮都留，能看到它怎么变）
        public string rawJson;           // 模型返回的原始 JSON
        public string failure;           // 空 = 正常
        public string errorMessage;
    }

    /// <summary>
    /// 试聊会话的驱动层：发请求、解析、记轮次。窗口只管画。
    ///
    /// 【域重载后怎么活下来】
    ///   `VNAiConversation` 内部的历史是 private List，序列化不了；但轮次记录
    ///   （playerSaid + reply）序列化得了，而这两样**足以重建历史**——
    ///   对每一轮依次调 BuildRequest(playerSaid) + RecordReply(reply) 即可，
    ///   前者组装时会把 user 追加进历史、后者追加 model，顺序与真实聊天完全一致。
    ///   所以重载后不用重聊，Rebuild 一下就能接着发。
    ///
    /// 【为什么 ctx 是回调而不是字段】
    ///   turnsLeft 每轮递减、topic 只在第一轮注入、记忆要不要注入是窗口上的开关——
    ///   这些属于「窗口的策略」，塞进会话层会让两边都难改。
    /// </summary>
    public class VNAiStudioSession
    {
        VNAiConversation _convo;
        VNAiEditorCoroutine _running;
        Func<int, VNAiContext> _ctxFor;
        VNAiPersonaDef _persona;

        public readonly List<VNAiStudioTurn> turns = new List<VNAiStudioTurn>();

        /// <summary>正在等模型回复</summary>
        public bool IsBusy => _running != null && _running.IsRunning;

        /// <summary>会话已开场（可以继续发）</summary>
        public bool IsLive => _convo != null;

        /// <summary>本场累计（顶栏那几个数字）</summary>
        public float TotalSeconds { get; private set; }
        public int TotalPromptTokens { get; private set; }
        public int TotalOutputTokens { get; private set; }
        public double TotalCostUsd { get; private set; }
        public int AffectionTotal { get; private set; }

        public VNAiConversation Conversation => _convo;
        public VNAiPersonaDef Persona => _persona;

        /// <summary>每次收到回复 / 出错后回调，窗口用来 Repaint</summary>
        public Action onChanged;

        // ──────────────── 开场 / 结束 ────────────────

        public void Begin(VNAiPersonaDef persona, Func<int, VNAiContext> ctxFor)
        {
            Abort();
            turns.Clear();
            TotalSeconds = 0; TotalPromptTokens = 0; TotalOutputTokens = 0;
            TotalCostUsd = 0; AffectionTotal = 0;

            _persona = persona;
            _ctxFor = ctxFor;
            _convo = new VNAiConversation(persona);
            Send(null);                       // 开场：让她先说第一句
        }

        public void Abort()
        {
            _running?.Stop();
            _running = null;
        }

        public void Clear()
        {
            Abort();
            _convo = null;
            turns.Clear();
            TotalSeconds = 0; TotalPromptTokens = 0; TotalOutputTokens = 0;
            TotalCostUsd = 0; AffectionTotal = 0;
        }

        // ──────────────── 发一轮 ────────────────

        /// <summary>playerSaid 为空 = 开场轮。</summary>
        public void Send(string playerSaid)
        {
            if (_convo == null || IsBusy) return;
            _running = VNAiEditorCoroutine.Start(SendCo(playerSaid));
        }

        IEnumerator SendCo(string playerSaid)
        {
            var ctx = _ctxFor != null ? _ctxFor(turns.Count) : new VNAiContext();
            var req = _convo.BuildRequest(playerSaid, ctx);

            var rec = new VNAiStudioTurn
            {
                playerSaid = playerSaid,
                systemPrompt = req.systemInstruction,
            };

            VNAiResult res = null;
            yield return VNAiClient.Send(req, r => res = r);
            while (res == null) yield return null;

            rec.seconds = res.elapsedSeconds;
            rec.promptTokens = res.promptTokens;
            rec.outputTokens = res.outputTokens;
            rec.thoughtsTokens = res.thoughtsTokens;
            rec.costUsd = res.EstimatedCostUsd;
            rec.rawJson = res.text;

            TotalSeconds += res.elapsedSeconds;
            TotalPromptTokens += res.promptTokens;
            TotalOutputTokens += res.outputTokens;
            TotalCostUsd += res.EstimatedCostUsd;

            VNAiTurn turn;
            if (!res.ok)
            {
                rec.failure = res.failure.ToString();
                rec.errorMessage = res.errorMessage;
                rec.degraded = true;
                turn = _convo.BuildFallbackTurn();
            }
            else if (!_convo.TryParseTurn(res.text, out turn, out string parseError))
            {
                rec.failure = "ParseError";
                rec.errorMessage = parseError;
                rec.degraded = true;
                turn = _convo.BuildFallbackTurn();
            }

            rec.reply = turn.reply;
            rec.emotion = turn.emotion;
            rec.mark = turn.mark;
            rec.affectionDelta = turn.affectionDelta;
            rec.shouldEnd = turn.shouldEnd;
            foreach (var o in turn.options)
            {
                rec.optionTexts.Add(o.text);
                rec.optionTones.Add(o.tone);
            }

            AffectionTotal += turn.affectionDelta;
            _convo.RecordReply(turn);
            turns.Add(rec);

            _running = null;
            onChanged?.Invoke();
        }

        /// <summary>玩家点了第 index 个候选回复 → 记下倾向并发下一轮。</summary>
        public void Pick(int turnIndex, int optionIndex)
        {
            if (IsBusy || turnIndex < 0 || turnIndex >= turns.Count) return;
            var t = turns[turnIndex];
            if (optionIndex < 0 || optionIndex >= t.optionTexts.Count) return;

            // 点的不是最后一轮 = 想从这里换个走向重来，先把后面的都丢掉
            if (turnIndex != turns.Count - 1 && !BranchFrom(turnIndex)) return;

            t.pickedIndex = optionIndex;
            _convo.RecordPick(new VNAiOption
            {
                text = t.optionTexts[optionIndex],
                tone = optionIndex < t.optionTones.Count ? t.optionTones[optionIndex] : "",
            });
            Send(t.optionTexts[optionIndex]);
        }

        /// <summary>自由输入：绕开三选一，直接扔一句话给她（调提示词时最好用）。</summary>
        public void SendFreeform(string text)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(text)) return;
            if (turns.Count > 0) turns[turns.Count - 1].pickedIndex = -1;
            Send(text.Trim());
        }

        // ──────────────── 重跑 / 分岔 ────────────────

        /// <summary>
        /// 重跑最后一轮：同样的输入再发一次，看输出的方差。
        /// 调 temperature 时这是唯一能判断「改动有没有效果」的办法——
        /// 单看一次结果分不清是参数起作用还是模型本来就会飘。
        /// </summary>
        public void RerollLast()
        {
            if (IsBusy || turns.Count == 0) return;
            string said = turns[turns.Count - 1].playerSaid;
            if (!BranchFrom(turns.Count - 1)) return;
            Send(said);
        }

        /// <summary>
        /// 回退到第 turnIndex 轮之前（丢掉它和它之后的全部轮次），
        /// 之后调用方可以换个说法重新发。返回是否成功。
        /// </summary>
        public bool BranchFrom(int turnIndex)
        {
            if (IsBusy || _convo == null) return false;
            if (turnIndex < 0 || turnIndex >= turns.Count) return false;

            _convo.TruncateToTurn(turnIndex);

            for (int i = turns.Count - 1; i >= turnIndex; i--)
            {
                var t = turns[i];
                TotalSeconds -= t.seconds;
                TotalPromptTokens -= t.promptTokens;
                TotalOutputTokens -= t.outputTokens;
                TotalCostUsd -= t.costUsd;
                AffectionTotal -= t.affectionDelta;
                turns.RemoveAt(i);
            }
            // 花掉的钱不会因为回退而退回来，但「这一场的累计」按现存轮次算才对得上，
            // 所以上面减掉了。真实开销以日志里逐轮的 costUsd 之和为准。
            if (turns.Count > 0) turns[turns.Count - 1].pickedIndex = -1;
            return true;
        }

        /// <summary>
        /// 从轮次记录把累计数算回来。域重载后会话对象是新建的，
        /// 统计字段全是 0，但轮次记录跟着窗口活下来了。
        /// </summary>
        public void RecomputeTotalsFromTurns()
        {
            TotalSeconds = 0; TotalPromptTokens = 0; TotalOutputTokens = 0;
            TotalCostUsd = 0; AffectionTotal = 0;
            foreach (var t in turns)
            {
                TotalSeconds += t.seconds;
                TotalPromptTokens += t.promptTokens;
                TotalOutputTokens += t.outputTokens;
                TotalCostUsd += t.costUsd;
                AffectionTotal += t.affectionDelta;
            }
        }

        // ──────────────── 域重载后重建 ────────────────

        /// <summary>
        /// 用已序列化的轮次记录重建会话历史，让重载后能接着聊。
        /// 不发任何请求：BuildRequest 只是组装，RecordReply 只是追加。
        /// </summary>
        public void Rebuild(VNAiPersonaDef persona, Func<int, VNAiContext> ctxFor)
        {
            _persona = persona;
            _ctxFor = ctxFor;
            _convo = new VNAiConversation(persona);
            if (turns.Count == 0) { _convo = null; return; }

            for (int i = 0; i < turns.Count; i++)
            {
                var t = turns[i];
                var ctx = ctxFor != null ? ctxFor(i) : new VNAiContext();
                _convo.BuildRequest(t.playerSaid, ctx);         // 追加 user（首轮补开场引导）
                _convo.RecordReply(new VNAiTurn { reply = t.reply });   // 追加 model

                if (t.pickedIndex >= 0 && t.pickedIndex < t.optionTones.Count)
                    _convo.RecordPick(new VNAiOption
                    {
                        text = t.optionTexts[t.pickedIndex],
                        tone = t.optionTones[t.pickedIndex],
                    });
            }
        }

        // ──────────────── 收场总结（记忆 + 日记）────────────────

        /// <summary>
        /// 发一次总结请求。成功回调给出 摘要/话题/关键事实/日记，
        /// 由窗口决定要不要收进记忆池。
        /// </summary>
        public void RequestSummary(VNAiContext ctx, string playerName,
                                   Action<VNAiConversation.VNAiSessionSummary, string> onDone)
        {
            if (_convo == null || IsBusy || turns.Count == 0)
            {
                onDone?.Invoke(null, "还没有可总结的对话");
                return;
            }
            _running = VNAiEditorCoroutine.Start(SummaryCo(ctx, playerName, onDone));
        }

        IEnumerator SummaryCo(VNAiContext ctx, string playerName,
                              Action<VNAiConversation.VNAiSessionSummary, string> onDone)
        {
            var req = _convo.BuildSummaryRequest(ctx, playerName);

            VNAiResult res = null;
            yield return VNAiClient.Send(req, r => res = r);
            while (res == null) yield return null;

            TotalSeconds += res.elapsedSeconds;
            TotalPromptTokens += res.promptTokens;
            TotalOutputTokens += res.outputTokens;
            TotalCostUsd += res.EstimatedCostUsd;

            _running = null;

            if (!res.ok)
            {
                onDone?.Invoke(null, $"{res.failure}：{res.errorMessage}");
            }
            else if (!VNAiConversation.TryParseSummary(res.text, out var summary, out string err))
            {
                onDone?.Invoke(null, err);
            }
            else
            {
                onDone?.Invoke(summary, null);
            }
            onChanged?.Invoke();
        }
    }
}
