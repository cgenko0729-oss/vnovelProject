using System.Collections.Generic;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// 把试聊台的一场会话导出成对话日志。
    ///
    /// 【为什么复用 VNAiTalkLog 而不另写一份格式】
    ///   两边**同格式**，试聊产生的会话与真实游玩记录就能互相对比，
    ///   将来写分析脚本也只用认一种结构。区别只有落盘位置：
    ///   游戏内 → AiTalkLogs/，试聊台 → AiTalkLogs/Editor/。
    /// </summary>
    public static class VNAiStudioLog
    {
        public const string SubFolder = "Editor";

        /// <summary>返回 .md 路径；没东西可写或失败返回 null。</summary>
        public static string Export(VNAiStudioSession session, VNAiPersonaDef persona,
                                    string kwargs, int maxTurns, string outcome)
        {
            if (session == null || persona == null || session.turns.Count == 0) return null;

            var log = new VNAiTalkLog();
            // Always：试聊台本来就只在编辑器里跑，但 EditorOnly 依赖 Application.isEditor，
            // 语义上这里是「明确要写」，直接给 Always 更直白
            log.Begin(persona, kwargs, session.turns[0].systemPrompt, maxTurns,
                      VNAiLogMode.Always);

            var pickedTones = new List<string>();

            for (int i = 0; i < session.turns.Count; i++)
            {
                var t = session.turns[i];

                // 日志层要的是运行时那两个类型，这里从展示数据回填一份
                var turn = new VNAiTurn
                {
                    reply = t.reply,
                    emotion = t.emotion,
                    mark = t.mark,
                    affectionDelta = t.affectionDelta,
                    shouldEnd = t.shouldEnd,
                };
                for (int k = 0; k < t.optionTexts.Count; k++)
                    turn.options.Add(new VNAiOption
                    {
                        text = t.optionTexts[k],
                        tone = k < t.optionTones.Count ? t.optionTones[k] : "",
                    });

                var res = new VNAiResult
                {
                    ok = string.IsNullOrEmpty(t.failure),
                    elapsedSeconds = t.seconds,
                    promptTokens = t.promptTokens,
                    outputTokens = t.outputTokens,
                    thoughtsTokens = t.thoughtsTokens,
                    errorMessage = t.errorMessage,
                };
                if (!res.ok) res.failure = ParseFailure(t.failure);

                log.BeginTurn(i, t.playerSaid, turn, res, t.degraded);
                log.RecordPick(t.pickedIndex);

                if (t.pickedIndex >= 0 && t.pickedIndex < t.optionTones.Count)
                    pickedTones.Add(t.optionTones[t.pickedIndex]);
            }

            log.End(outcome, session.AffectionTotal, pickedTones, null, false);
            return log.Save(SubFolder);
        }

        /// <summary>
        /// 失败类型是以字符串存在展示数据里的（那样才能跟着窗口序列化过域重载），
        /// 写日志时再翻回枚举。认不出来的（如解析失败那条自定义的 "ParseError"）
        /// 落到 BadResponse——日志里差一个精确分类，不值得让导出失败。
        /// </summary>
        static VNAiFailure ParseFailure(string s)
        {
            if (!string.IsNullOrEmpty(s) &&
                System.Enum.TryParse(s, out VNAiFailure f)) return f;
            return VNAiFailure.BadResponse;
        }
    }
}
