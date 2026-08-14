using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>日志写在哪种场合。</summary>
    public enum VNAiLogMode
    {
        [InspectorName("仅编辑器（推荐：调试用，发行版不写盘）")] EditorOnly = 0,
        [InspectorName("总是写（发行版也写到 persistentDataPath）")] Always = 1,
        [InspectorName("关闭")] Off = 2,
    }

    /// <summary>
    /// 一场 AI 自由聊天的完整记录，聊完落盘两份文件：
    ///   &lt;时间&gt;_&lt;人格&gt;.md    人读版——直接双击就能看聊了什么、给了哪三个选项、选了哪个、花了多少钱
    ///   &lt;时间&gt;_&lt;人格&gt;.json  机读版——以后想批量统计（哪种语气选得多、哪个 topic 效果好）用这份
    ///
    /// 【为什么两份都写】
    /// 调人格时你要用眼睛看（Markdown），做数据分析时你要用脚本读（JSON）。
    /// 从 Markdown 反解结构化数据很痛苦，同时写两份的成本却近乎为零。
    ///
    /// 【为什么默认只在编辑器写】
    /// 这是开发调试功能。发行版往玩家硬盘写对话记录既占空间也涉及隐私，
    /// 真要开就把 logMode 调成 Always（会写到 persistentDataPath 而不是项目目录）。
    ///
    /// 【纯类，不继承 MonoBehaviour】
    /// 与 VNAiConversation 同理——路径拼装、Markdown 渲染、成本累加都是纯逻辑，
    /// 不碰 Unity 生命周期就能单测。
    /// </summary>
    public class VNAiTalkLog
    {
        /// <summary>编辑器下的落盘目录名（在项目根，与 Assets 平级）</summary>
        public const string FolderName = "AiTalkLogs";

        readonly Session _s = new Session();
        Turn _current;

        public bool Enabled { get; private set; }

        // ──────────────── 记录 ────────────────

        /// <summary>开场：记下这一场的全部前置信息。systemInstruction 只存首轮那一份。</summary>
        public void Begin(VNAiPersonaDef persona, VNEventContext ctx,
                          string systemInstruction, int maxTurns, VNAiLogMode mode)
        {
            Begin(persona, ctx != null ? FormatKwargs(ctx.kwargs) : null,
                  systemInstruction, maxTurns, mode);
            if (ctx != null) _s.scriptLine = ctx.line;
        }

        /// <summary>
        /// 开场（不带剧本上下文的版本）。编辑器试聊台用这个——那边没有 VNEventContext，
        /// 情境参数是窗口上手填的，直接给一行人话即可。
        /// </summary>
        public void Begin(VNAiPersonaDef persona, string kwargsText,
                          string systemInstruction, int maxTurns, VNAiLogMode mode)
        {
            Enabled = mode == VNAiLogMode.Always ||
                      (mode == VNAiLogMode.EditorOnly && Application.isEditor);
            if (!Enabled) return;

            _s.startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _s.personaId = persona != null ? persona.id : "(无人格)";
            _s.characterId = persona != null && persona.character != null
                ? persona.character.id : "";
            _s.displayName = persona != null ? persona.DisplayName : "";
            _s.model = persona != null ? persona.ResolveModel() : VNAiClient.DefaultModel;
            _s.temperature = persona != null ? persona.temperature : 0f;
            _s.maxReplyChars = persona != null ? persona.maxReplyChars : 0;
            _s.historyTurns = persona != null ? persona.historyTurns : 0;
            _s.maxTurns = maxTurns;
            _s.systemInstruction = systemInstruction;
            _s.kwargs = kwargsText ?? "";
        }

        /// <summary>
        /// 一轮开始：先把请求侧与回复侧记下来，返回的对象供之后回填玩家选了哪个。
        /// 分两步是因为「玩家选了哪个」要等她说完、选项飞出来、玩家点了才知道。
        /// </summary>
        public Turn BeginTurn(int index, string playerSaid, VNAiTurn turn,
                              VNAiResult res, bool degraded)
        {
            if (!Enabled) return null;

            var t = new Turn
            {
                index = index + 1,
                playerSaid = playerSaid ?? "",
                reply = turn != null ? turn.reply : "",
                emotion = turn != null ? turn.emotion : "",
                mark = turn != null && turn.mark != null ? turn.mark : "",
                affectionDelta = turn != null ? turn.affectionDelta : 0,
                shouldEnd = turn != null && turn.shouldEnd,
                degraded = degraded,
                pickedIndex = -1,
            };

            if (turn != null && turn.options != null)
                foreach (var o in turn.options)
                    t.options.Add(new Opt { text = o.text, tone = o.tone });

            if (res != null)
            {
                t.seconds = res.elapsedSeconds;
                t.promptTokens = res.promptTokens;
                t.outputTokens = res.outputTokens;
                t.thoughtsTokens = res.thoughtsTokens;
                t.costUsd = res.EstimatedCostUsd;
                t.failure = res.ok ? "" : res.failure.ToString();
                t.errorMessage = res.ok ? "" : res.errorMessage;

                _s.totalSeconds += res.elapsedSeconds;
                _s.totalPromptTokens += res.promptTokens;
                _s.totalOutputTokens += res.outputTokens + res.thoughtsTokens;
                _s.totalCostUsd += res.EstimatedCostUsd;
            }

            _s.turns.Add(t);
            _current = t;
            return t;
        }

        /// <summary>玩家选完之后回填。index &lt; 0 = 没给选项（最后一轮 / 提前收尾）。</summary>
        public void RecordPick(int index)
        {
            if (!Enabled || _current == null) return;
            _current.pickedIndex = index;
        }

        /// <summary>收场：结果、累计好感、写入的 flag。</summary>
        public void End(string outcome, int affectionTotal, List<string> pickedTones,
                        List<string> flagLines, bool quitByPlayer)
        {
            if (!Enabled) return;
            _s.outcome = outcome;
            _s.affectionTotal = affectionTotal;
            _s.quitByPlayer = quitByPlayer;
            _s.endedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (pickedTones != null) _s.pickedTones.AddRange(pickedTones);
            if (flagLines != null) _s.flags.AddRange(flagLines);
        }

        // ──────────────── 落盘 ────────────────

        /// <summary>
        /// 写两份文件。返回 .md 的完整路径（失败返回 null）。
        /// </summary>
        /// <param name="subFolder">
        /// 落盘目录下的子目录名。游戏内留空 = 直接写 AiTalkLogs/；
        /// 编辑器试聊台传 "Editor"，把调试产生的会话与真实游玩记录分开，
        /// 但格式完全一致，所以两边日志能互相对比、分析脚本一套就够。
        /// </param>
        public string Save(string subFolder = null)
        {
            if (!Enabled) return null;
            try
            {
                string dir = ResolveDirectory();
                if (!string.IsNullOrWhiteSpace(subFolder))
                    dir = Path.Combine(dir, subFolder.Trim());
                Directory.CreateDirectory(dir);

                string stem = $"{DateTime.Now:yyyy-MM-dd_HHmmss}_{Sanitize(_s.personaId)}";
                string mdPath = Path.Combine(dir, stem + ".md");
                string jsonPath = Path.Combine(dir, stem + ".json");

                File.WriteAllText(mdPath, RenderMarkdown(), new UTF8Encoding(true));
                File.WriteAllText(jsonPath, JsonUtility.ToJson(_s, true), new UTF8Encoding(true));
                return mdPath;
            }
            catch (Exception e)
            {
                // 日志写不出去绝不能影响玩法，只告警
                Debug.LogWarning($"[VNAiTalk] 对话日志写入失败（{e.GetType().Name}）：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 编辑器 → 项目根/AiTalkLogs（双击就能看）；
        /// Build → persistentDataPath/AiTalkLogs（项目目录在玩家机器上不存在）。
        /// </summary>
        public static string ResolveDirectory()
        {
#if UNITY_EDITOR
            // Application.dataPath = <项目根>/Assets
            string root = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(root)) return Path.Combine(root, FolderName);
#endif
            return Path.Combine(Application.persistentDataPath, FolderName);
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unnamed";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        static string FormatKwargs(Dictionary<string, string> kwargs)
        {
            if (kwargs == null || kwargs.Count == 0) return "(无)";
            var sb = new StringBuilder();
            foreach (var kv in kwargs)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            return sb.ToString();
        }

        // ──────────────── Markdown 渲染 ────────────────

        string RenderMarkdown()
        {
            var sb = new StringBuilder(4096);
            string who = string.IsNullOrEmpty(_s.displayName) ? _s.characterId : _s.displayName;

            sb.AppendLine($"# AI 自由聊天日志 · {who}");
            sb.AppendLine();
            sb.AppendLine($"- **时间**：{_s.startedAt} → {_s.endedAt}");
            sb.AppendLine($"- **人格**：`{_s.personaId}`　**角色**：`{_s.characterId}`");
            sb.AppendLine($"- **模型**：`{_s.model}`　temperature `{_s.temperature}`");
            sb.AppendLine($"- **剧本行**：第 {_s.scriptLine} 行　`event aitalk {_s.kwargs}`");
            sb.AppendLine($"- **轮数上限**：{_s.maxTurns}　**历史保留**：{_s.historyTurns} 轮" +
                          $"　**台词字数上限**：{_s.maxReplyChars}");
            if (_s.maxTurns > _s.historyTurns)
                sb.AppendLine($"  > ⚠ 轮数上限({_s.maxTurns}) > 历史保留({_s.historyTurns})：" +
                              $"第 {_s.historyTurns + 1} 轮起，最早的对话会被裁掉，她会「忘记」开头。");
            sb.AppendLine();

            sb.AppendLine("## 结果");
            sb.AppendLine();
            sb.AppendLine($"- **结果名**：`{_s.outcome}`" +
                          (_s.quitByPlayer ? "　（玩家按 ESC 提前结束）" : ""));
            sb.AppendLine($"- **累计好感**：{_s.affectionTotal:+#;-#;0}");
            sb.AppendLine($"- **实际轮数**：{CountRealTurns()} / {_s.maxTurns}");
            sb.AppendLine($"- **玩家倾向**：{(_s.pickedTones.Count > 0 ? string.Join(" → ", _s.pickedTones) : "(没选过)")}");
            if (_s.flags.Count > 0)
            {
                sb.AppendLine("- **写入的 flag**：");
                foreach (string f in _s.flags) sb.AppendLine($"  - `{f}`");
            }
            sb.AppendLine();

            sb.AppendLine("## 开销");
            sb.AppendLine();
            sb.AppendLine("| 项 | 值 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 总耗时 | {_s.totalSeconds:0.0}s |");
            sb.AppendLine($"| 输入 token | {_s.totalPromptTokens} |");
            sb.AppendLine($"| 输出 token（含思考） | {_s.totalOutputTokens} |");
            sb.AppendLine($"| 合计 token | {_s.totalPromptTokens + _s.totalOutputTokens} |");
            sb.AppendLine($"| 估算成本 | ${_s.totalCostUsd:0.000000} |");
            if (CountRealTurns() > 0)
                sb.AppendLine($"| 平均每轮 | {_s.totalSeconds / CountRealTurns():0.0}s　" +
                              $"${_s.totalCostUsd / CountRealTurns():0.000000} |");
            sb.AppendLine();
            sb.AppendLine("> 按 Gemini Flash Lite 的公开价（输入 $0.30 / 输出 $2.50 每百万 token）估算，仅供参考。");
            sb.AppendLine();

            sb.AppendLine("## 对话");
            sb.AppendLine();
            foreach (var t in _s.turns)
            {
                sb.Append($"### 第 {t.index} 轮");
                if (t.seconds > 0f) sb.Append($"　`{t.seconds:0.00}s`　`{t.promptTokens}+{t.outputTokens} tok`");
                if (t.degraded) sb.Append("　⚠ **兜底轮**");
                sb.AppendLine();
                sb.AppendLine();

                if (!string.IsNullOrEmpty(t.failure))
                    sb.AppendLine($"> ✘ 请求失败：`{t.failure}` — {t.errorMessage}").AppendLine();

                if (!string.IsNullOrEmpty(t.playerSaid))
                    sb.AppendLine($"**我**：{t.playerSaid}").AppendLine();

                sb.AppendLine($"**{who}**：{t.reply}");
                sb.AppendLine();
                sb.Append($"`表情 {Or(t.emotion, "-")}`　`漫符 {Or(t.mark, "无")}`　" +
                          $"`好感 {t.affectionDelta:+#;-#;0}`");
                if (t.shouldEnd) sb.Append("　`AI 判定收尾`");
                sb.AppendLine();
                sb.AppendLine();

                if (t.options.Count > 0)
                {
                    for (int i = 0; i < t.options.Count; i++)
                    {
                        bool picked = i == t.pickedIndex;
                        sb.AppendLine($"{(picked ? "- **→**" : "-")} `[{t.options[i].tone}]` " +
                                      $"{t.options[i].text}{(picked ? "　← 玩家选了这个" : "")}");
                    }
                    if (t.pickedIndex < 0) sb.AppendLine().AppendLine("> （本轮没有让玩家选：最后一轮或提前收尾）");
                    sb.AppendLine();
                }
            }

            if (!string.IsNullOrEmpty(_s.systemInstruction))
            {
                sb.AppendLine("---");
                sb.AppendLine();
                sb.AppendLine("## 附：首轮 system prompt");
                sb.AppendLine();
                sb.AppendLine("> 调人格时对着这段看——改了资产里的 persona / speechStyle 之后，");
                sb.AppendLine("> 直接对比两份日志就知道哪句话起了作用。只记首轮那份，");
                sb.AppendLine("> 后续轮次只有「此刻的情况」段里的剩余轮数在变。");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(_s.systemInstruction);
                sb.AppendLine("```");
            }

            return sb.ToString();
        }

        int CountRealTurns() => _s.turns.Count;
        static string Or(string s, string def) => string.IsNullOrEmpty(s) ? def : s;

        // ──────────────── JSON 结构（JsonUtility 可序列化）────────────────

        [Serializable] public class Opt { public string text; public string tone; }

        [Serializable] public class Turn
        {
            public int index;
            public string playerSaid;
            public string reply;
            public string emotion;
            public string mark;
            public int affectionDelta;
            public bool shouldEnd;
            public bool degraded;          // 走了兜底台词
            public List<Opt> options = new List<Opt>();
            public int pickedIndex;        // -1 = 本轮没让玩家选
            public float seconds;
            public int promptTokens, outputTokens, thoughtsTokens;
            public double costUsd;
            public string failure;         // 空 = 正常
            public string errorMessage;
        }

        [Serializable] public class Session
        {
            public string startedAt, endedAt;
            public string personaId, characterId, displayName, model;
            public float temperature;
            public int maxReplyChars, historyTurns, maxTurns;
            public int scriptLine;
            public string kwargs;
            public string outcome;
            public int affectionTotal;
            public bool quitByPlayer;
            public List<string> pickedTones = new List<string>();
            public List<string> flags = new List<string>();
            public float totalSeconds;
            public int totalPromptTokens, totalOutputTokens;
            public double totalCostUsd;
            public List<Turn> turns = new List<Turn>();
            public string systemInstruction;
        }
    }
}
