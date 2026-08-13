using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>AI 一轮回复解析后的结果，可以直接喂给舞台演出。</summary>
    public class VNAiTurn
    {
        public string reply;            // 台词
        public string emotion;          // 表情名（已校验在白名单内）
        public string mark;             // 漫符英文正名；null = 这轮不出符号
        public int affectionDelta;      // 已按人格的 affectionClamp 钳过
        public List<VNAiOption> options = new List<VNAiOption>();
        public bool shouldEnd;          // AI 认为话题可以收尾了
    }

    /// <summary>一个候选回复。tone 是隐藏标签，可用来统计玩家倾向。</summary>
    public struct VNAiOption
    {
        public string text;
        public string tone;
    }

    /// <summary>组装 system prompt 时注入的运行时上下文（全部可留空）。</summary>
    public struct VNAiContext
    {
        public string playerName;       // 玩家名（剧本里「我」的称呼）
        public string topic;            // 本次话题引导：剧本 topic:
        public string place;            // 当前场景描述（背景 id / 自由文本）
        public string affectionText;    // 当前好感度的人话描述，如「好感 42 / 100，算是熟了」
        public string memory;           // 往期聊天摘要（跨场景记忆）
        public int turnsLeft;           // 还剩几轮（让 AI 自己把节奏收住）
    }

    /// <summary>
    /// 一场 AI 自由聊天的会话状态与全部纯逻辑：
    /// system prompt 组装 / JSON Schema 生成 / 历史裁剪 / 响应解析与钳制。
    ///
    /// ★ 刻意不继承 MonoBehaviour ★
    ///   这一层是最容易出 bug 的地方（提示词拼错、JSON 解析漏字段、好感没钳住），
    ///   不碰 Unity 生命周期就能在 EditMode 测试里直接跑，
    ///   不用每验证一次就进 Play Mode 等域重载。
    ///   同项目里 VNBadmintonBallistics / VNPhotoScore 也是这个思路。
    ///
    /// 表现层（VNAiTalkModule）只负责：拿 BuildRequest() 的结果去发、
    /// 把 TryParseTurn() 的结果去演，不做任何提示词或解析工作。
    /// </summary>
    public class VNAiConversation
    {
        public readonly VNAiPersonaDef persona;
        readonly List<VNAiMessage> _history = new List<VNAiMessage>();
        readonly List<string> _emotions;
        readonly List<string> _marks;
        readonly string _schema;

        /// <summary>已完成的轮数（一轮 = 玩家说一句 + 她回一句）</summary>
        public int TurnCount { get; private set; }

        /// <summary>玩家历次选择的语气标签，可用来统计倾向写 flag</summary>
        public readonly List<string> pickedTones = new List<string>();

        public VNAiConversation(VNAiPersonaDef persona)
        {
            this.persona = persona != null
                ? persona
                : throw new ArgumentNullException(nameof(persona));
            _emotions = persona.ResolveEmotions();
            _marks = persona.ResolveMarks();
            _schema = BuildSchema(_emotions, _marks, persona.optionTones);
        }

        // ──────────────── 对外：组装请求 ────────────────

        /// <summary>
        /// 组装一次请求。playerSaid 为空 = 开场（用人格的 opening 或 topic 起头）。
        /// </summary>
        public VNAiRequest BuildRequest(string playerSaid, VNAiContext ctx)
        {
            if (!string.IsNullOrEmpty(playerSaid))
                _history.Add(VNAiMessage.Player(playerSaid));
            else if (_history.Count == 0)
                _history.Add(VNAiMessage.Player(OpeningPrompt(ctx)));

            TrimHistory();

            var req = new VNAiRequest
            {
                model = persona.ResolveModel(),
                systemInstruction = BuildSystemInstruction(persona, ctx, _emotions, _marks),
                responseSchemaJson = _schema,
                thinking = persona.thinking,
                safety = persona.safety,
                temperature = persona.temperature,
                maxOutputTokens = persona.maxOutputTokens,
            };
            req.history.AddRange(_history);
            return req;
        }

        /// <summary>把她这轮的回复记进历史（下一轮请求要带上）。</summary>
        public void RecordReply(VNAiTurn turn)
        {
            if (turn == null) return;
            _history.Add(VNAiMessage.Model(turn.reply ?? ""));
            TurnCount++;
        }

        /// <summary>记录玩家选了哪个语气（统计倾向用）。</summary>
        public void RecordPick(VNAiOption picked)
        {
            if (!string.IsNullOrEmpty(picked.tone)) pickedTones.Add(picked.tone);
        }

        /// <summary>
        /// 历史裁剪：只留最近 N 轮。发给模型的 token 随轮数线性增长，
        /// 不裁的话第 20 轮的输入会是第 1 轮的十几倍，钱和延迟都顶不住。
        /// 裁掉的部分不做摘要——跨场景记忆走 VNAiContext.memory，那是另一条路。
        /// </summary>
        void TrimHistory()
        {
            int max = Mathf.Max(2, persona.historyTurns * 2);   // 一轮两条消息
            if (_history.Count <= max) return;
            _history.RemoveRange(0, _history.Count - max);

            // 历史必须以 user 开头，否则 Gemini 会 400
            while (_history.Count > 0 && !_history[0].fromPlayer)
                _history.RemoveAt(0);
        }

        string OpeningPrompt(VNAiContext ctx)
        {
            if (!string.IsNullOrWhiteSpace(persona.opening)) return persona.opening.Trim();
            if (!string.IsNullOrWhiteSpace(ctx.topic))
                return $"（{ctx.topic}）请你先开口说第一句话。";
            return "（我们碰面了）请你先开口说第一句话。";
        }

        // ──────────────── 纯静态：提示词 ────────────────

        /// <summary>
        /// 组装 system prompt。顺序是有讲究的：
        /// 身份 → 说话方式 → 关系 → 当前处境 → 输出规则 → 边界，
        /// 边界放最后是因为越靠后的指令实际权重越高。
        /// </summary>
        public static string BuildSystemInstruction(
            VNAiPersonaDef p, VNAiContext ctx,
            List<string> emotions, List<string> marks)
        {
            var sb = new StringBuilder(1024);
            string name = string.IsNullOrEmpty(p.DisplayName) ? p.id : p.DisplayName;
            string me = string.IsNullOrWhiteSpace(ctx.playerName) ? "我" : ctx.playerName.Trim();

            sb.Append("你在一款视觉小说里扮演「").Append(name).AppendLine("」。");
            sb.AppendLine("你不是助手，不要提供帮助或建议，只要像这个角色一样自然地对话。");
            sb.AppendLine();

            Section(sb, "身份与性格", p.persona);
            Section(sb, "说话方式", p.speechStyle);
            Section(sb, $"你和「{me}」的关系", p.relationship);

            // ── 当前处境（每轮都在变，所以单独一段）──
            var now = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(ctx.place)) now.Append("场景：").AppendLine(ctx.place.Trim());
            if (!string.IsNullOrWhiteSpace(ctx.affectionText)) now.Append("当前关系：").AppendLine(ctx.affectionText.Trim());
            if (!string.IsNullOrWhiteSpace(ctx.topic)) now.Append("这次想聊的：").AppendLine(ctx.topic.Trim());
            if (!string.IsNullOrWhiteSpace(ctx.memory)) now.Append("你记得的往事：").AppendLine(ctx.memory.Trim());
            if (ctx.turnsLeft > 0)
                now.Append("这场对话大约还剩 ").Append(ctx.turnsLeft)
                   .AppendLine(" 轮，请自然地把话题引向收尾，别突兀地结束。");
            Section(sb, "此刻的情况", now.ToString());

            // ── 输出规则 ──
            sb.AppendLine("【输出规则】");
            sb.Append("1. reply：你的台词，1~2 句，不超过 ").Append(p.maxReplyChars)
              .AppendLine(" 字。像真人说话，不要旁白、不要动作描写、不要加引号。");
            // 实测会混出「才、才沒有」这种繁体，和游戏其余文本对不上，必须显式约束
            sb.AppendLine("   全部文字使用简体中文，禁止出现繁体字。");
            sb.Append("2. emotion：从这些里挑一个最贴切的 —— ")
              .AppendLine(string.Join(" / ", emotions));
            sb.Append("3. mark：漫画符号，不需要就填 ").Append(VNAiPersonaDef.NoMark)
              .Append("。可选 —— ").AppendLine(string.Join(" / ", marks));
            sb.Append("4. affection_delta：这一轮她对「").Append(me).Append("」的好感变化，整数，范围 -")
              .Append(p.affectionClamp).Append(" ~ +").Append(p.affectionClamp)
              .AppendLine("。多数轮次应该是 0；只有明显打动或惹恼她时才给非零值。");
            sb.Append("5. options：正好 3 个候选回复，全部以「").Append(me)
              .AppendLine("」的第一人称口吻写，每个不超过 25 字。三个的语气必须分别是：");
            var tones = p.optionTones;
            for (int i = 0; i < tones.Count; i++)
                sb.Append("   - ").Append(tones[i]).AppendLine();
            sb.AppendLine("   三个选项要给出**实质不同**的走向，不要只是同一句话的三种说法。");
            sb.AppendLine("6. should_end：这个话题自然聊完了就填 true，否则 false。");
            sb.AppendLine();

            sb.AppendLine("【绝对边界】");
            sb.AppendLine(string.IsNullOrWhiteSpace(p.boundaries) ? "（无）" : p.boundaries.Trim());
            sb.Append("只输出 JSON，不要任何额外文字、不要代码块围栏。");

            return sb.ToString();
        }

        static void Section(StringBuilder sb, string title, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return;
            sb.Append('【').Append(title).AppendLine("】");
            sb.AppendLine(body.Trim());
            sb.AppendLine();
        }

        // ──────────────── 纯静态：JSON Schema ────────────────

        /// <summary>
        /// 生成结构化输出 schema。emotion / mark / tone 一律做成 enum，
        /// 值从角色资产实时取——这样 AI 物理上编不出不存在的表情名，
        /// 换角色也自动适配，不用手同步两份列表。
        ///
        /// 注意：Gemini 的 schema 子集**不支持 minimum / maximum**，
        /// 所以 affection_delta 的范围只能靠提示词说 + 代码 Clamp 双保险
        /// （实测不写范围时它会给出 +5 这种值）。
        /// </summary>
        public static string BuildSchema(List<string> emotions, List<string> marks, List<string> tones)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"OBJECT\",\"properties\":{");
            sb.Append("\"reply\":{\"type\":\"STRING\"},");
            sb.Append("\"emotion\":{\"type\":\"STRING\",\"enum\":").Append(JsonStringArray(emotions)).Append("},");
            sb.Append("\"mark\":{\"type\":\"STRING\",\"enum\":").Append(JsonStringArray(marks)).Append("},");
            sb.Append("\"affection_delta\":{\"type\":\"INTEGER\"},");
            sb.Append("\"options\":{\"type\":\"ARRAY\",\"minItems\":3,\"maxItems\":3,\"items\":{");
            sb.Append("\"type\":\"OBJECT\",\"properties\":{");
            sb.Append("\"text\":{\"type\":\"STRING\"},");
            sb.Append("\"tone\":{\"type\":\"STRING\",\"enum\":").Append(JsonStringArray(tones)).Append('}');
            sb.Append("},\"required\":[\"text\",\"tone\"],\"propertyOrdering\":[\"text\",\"tone\"]}},");
            sb.Append("\"should_end\":{\"type\":\"BOOLEAN\"}");
            sb.Append("},\"required\":[\"reply\",\"emotion\",\"mark\",\"affection_delta\",\"options\",\"should_end\"],");
            sb.Append("\"propertyOrdering\":[\"reply\",\"emotion\",\"mark\",\"affection_delta\",\"options\",\"should_end\"]}");
            return sb.ToString();
        }

        static string JsonStringArray(List<string> items)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            if (items != null)
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    VNAiClient.Esc(sb, items[i]);
                }
            sb.Append(']');
            return sb.ToString();
        }

        // ──────────────── 纯静态：响应解析 ────────────────

        /// <summary>
        /// 解析模型返回的内层 JSON。永远不信任模型输出：
        /// 白名单外的表情降级到第一个合法表情、好感强制钳制、
        /// 选项不足 3 个补齐——宁可演出打折，也不能让模块崩掉或数值失控。
        /// </summary>
        public bool TryParseTurn(string json, out VNAiTurn turn, out string error)
            => TryParseTurn(json, persona, _emotions, _marks, out turn, out error);

        public static bool TryParseTurn(
            string json, VNAiPersonaDef p,
            List<string> emotions, List<string> marks,
            out VNAiTurn turn, out string error)
        {
            turn = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json)) { error = "回复为空"; return false; }

            // 偶发会裹一层 ```json 围栏，剥掉再解析
            string cleaned = StripFence(json);

            RawTurn raw;
            try { raw = JsonUtility.FromJson<RawTurn>(cleaned); }
            catch (Exception e) { error = $"JSON 解析失败：{e.Message}"; return false; }
            if (raw == null) { error = "JSON 解析结果为空"; return false; }
            if (string.IsNullOrWhiteSpace(raw.reply)) { error = "reply 字段为空"; return false; }

            turn = new VNAiTurn
            {
                reply = raw.reply.Trim(),
                shouldEnd = raw.should_end,
                affectionDelta = Mathf.Clamp(raw.affection_delta, -p.affectionClamp, p.affectionClamp),
            };

            if (raw.affection_delta != turn.affectionDelta)
                Debug.LogWarning($"[VNAi] 好感变化 {raw.affection_delta} 超出人格「{p.id}」" +
                                 $"设定的 ±{p.affectionClamp}，已钳制为 {turn.affectionDelta}");

            // 表情：白名单外降级到第一个（schema 已经约束过，这里是防御）
            turn.emotion = PickWhitelisted(raw.emotion, emotions, out bool emotionFallback);
            if (emotionFallback && !string.IsNullOrEmpty(raw.emotion))
                Debug.LogWarning($"[VNAi] 表情「{raw.emotion}」不在白名单里，降级为「{turn.emotion}」");

            // 漫符：none / 空 / 非法 → 这轮不出符号
            turn.mark = null;
            if (!string.IsNullOrWhiteSpace(raw.mark) &&
                !string.Equals(raw.mark.Trim(), VNAiPersonaDef.NoMark, StringComparison.OrdinalIgnoreCase) &&
                VNCharacterMarks.TryParse(raw.mark.Trim(), out VNMarkKind kind))
            {
                string canonical = VNCharacterMarks.NameOf(kind);
                if (marks == null || marks.Contains(canonical)) turn.mark = canonical;
            }

            // 选项：不足 3 个补齐，多的截断
            var tones = p.optionTones;
            if (raw.options != null)
                foreach (var o in raw.options)
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.text)) continue;
                    turn.options.Add(new VNAiOption { text = o.text.Trim(), tone = o.tone });
                    if (turn.options.Count == 3) break;
                }
            while (turn.options.Count < 3)
            {
                int i = turn.options.Count;
                turn.options.Add(new VNAiOption
                {
                    text = "……",
                    tone = tones != null && i < tones.Count ? tones[i] : "",
                });
                Debug.LogWarning($"[VNAi] 候选回复不足 3 个，已用「……」补齐第 {i + 1} 项");
            }

            if (turn.reply.Length > p.maxReplyChars * 2)
                Debug.LogWarning($"[VNAi] 台词 {turn.reply.Length} 字，远超设定的 " +
                                 $"{p.maxReplyChars} 字，打字机会偏慢（去 persona 里加强长度约束）");

            return true;
        }

        static string PickWhitelisted(string value, List<string> whitelist, out bool fallback)
        {
            fallback = true;
            if (whitelist == null || whitelist.Count == 0) return value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                string v = value.Trim();
                foreach (string w in whitelist)
                    if (string.Equals(w, v, StringComparison.Ordinal)) { fallback = false; return w; }
            }
            return whitelist[0];
        }

        /// <summary>剥掉 ```json ... ``` 围栏（结构化模式下少见，但便宜的保险）</summary>
        static string StripFence(string s)
        {
            string t = s.Trim();
            if (!t.StartsWith("```")) return t;
            int nl = t.IndexOf('\n');
            if (nl < 0) return t;
            t = t.Substring(nl + 1);
            int fence = t.LastIndexOf("```", StringComparison.Ordinal);
            return (fence >= 0 ? t.Substring(0, fence) : t).Trim();
        }

        /// <summary>兜底轮：断网 / 被拦 / 解析失败时用，保证模块永远有东西可演。</summary>
        public VNAiTurn BuildFallbackTurn()
        {
            var t = new VNAiTurn
            {
                reply = Pick(persona.fallbackLines, "……（她没有回答）"),
                emotion = _emotions.Count > 0 ? _emotions[0] : "默认",
                mark = null,
                affectionDelta = 0,
                shouldEnd = false,
            };
            var tones = persona.optionTones;
            for (int i = 0; i < 3; i++)
            {
                string text = persona.fallbackOptions != null && i < persona.fallbackOptions.Count
                    ? persona.fallbackOptions[i] : "……";
                t.options.Add(new VNAiOption
                {
                    text = text,
                    tone = tones != null && i < tones.Count ? tones[i] : "",
                });
            }
            return t;
        }

        static string Pick(List<string> list, string def)
        {
            if (list == null || list.Count == 0) return def;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        // JsonUtility 映射（字段名必须与 schema 一致）
        [Serializable] class RawTurn
        {
            public string reply;
            public string emotion;
            public string mark;
            public int affection_delta;
            public RawOption[] options;
            public bool should_end;
        }
        [Serializable] class RawOption { public string text; public string tone; }
    }
}
