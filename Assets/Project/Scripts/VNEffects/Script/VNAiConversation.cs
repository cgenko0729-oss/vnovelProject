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
        public string memory;           // 往期聊天摘要（跨场记忆，VNAiMemory 组装）
        public List<string> pastTopics; // 已聊过的话题，作为**硬性回避清单**注入
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
        readonly List<string> _tones;    // 本场实际用的语气档（3~6 条）
        readonly string _schema;
        string _openingText;            // 合成的开场引导原文，拍平对话时跳过

        /// <summary>本场每轮给几个候选回复</summary>
        public int OptionCount => _tones.Count;

        /// <summary>本场实际生效的语气标签（写 flag、统计倾向都用这一份）</summary>
        public IReadOnlyList<string> Tones => _tones;

        /// <summary>已完成的轮数（一轮 = 玩家说一句 + 她回一句）</summary>
        public int TurnCount { get; private set; }

        /// <summary>玩家历次选择的语气标签，可用来统计倾向写 flag</summary>
        public readonly List<string> pickedTones = new List<string>();

        // ──────────────── 调试用：历史读取与回退 ────────────────
        //
        // 只给编辑器试聊台（VNAiStudioWindow）用，游戏内一条路径都走不到这里。
        // 目的是「她这句回得不对，我从第 3 轮换个说法重试」和「同一输入连发三次看方差」——
        // 没有这两个能力，调 temperature 与 boundaries 只能整场重聊。
        //
        // 刻意做成裁剪自己而不是让窗口另建一份消息列表：历史裁剪（TrimHistory）的语义
        // 只应该有一份实现，复制出去将来改 historyTurns 语义时两边必然走偏。

        /// <summary>发给模型的完整历史（含合成的开场引导）。只读用途。</summary>
        public IReadOnlyList<VNAiMessage> History => _history;

        /// <summary>合成的开场引导原文；null = 还没开场。拍平对话时要跳过它。</summary>
        public string OpeningText => _openingText;

        /// <summary>
        /// 回退到「第 turnIndex 轮开始之前」的状态（0 = 整场重来但保留开场引导）。
        /// 一轮 = 玩家一条 + 她一条，所以留下的消息数是 开场 + turnIndex×2 − 1 …
        /// 实际按「已完成轮数」数：保留前 turnIndex 组「她的回复」及其之前的全部消息。
        ///
        /// 返回是否真的动了历史（已经在那个点上就返回 false）。
        /// </summary>
        public bool TruncateToTurn(int turnIndex)
        {
            if (turnIndex < 0) turnIndex = 0;
            if (turnIndex >= TurnCount) return false;

            // 从头扫，数到第 turnIndex 条 model 消息为止（不含它，也不含它后面的一切）
            int kept = 0, cut = _history.Count;
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].fromPlayer) continue;
                if (kept == turnIndex) { cut = i; break; }
                kept++;
            }

            // 那条 model 消息之前紧挨着的 user 消息也要去掉——它是「触发这一轮的玩家回复」，
            // 保留下来会让下一次 BuildRequest 追加第二条 user 消息，Gemini 直接 400。
            // 例外：开场引导（第 0 条）永远保留，否则重来时她不知道该先开口。
            if (cut > 0 && _history[cut - 1].fromPlayer && cut - 1 > 0) cut--;

            if (cut >= _history.Count) return false;
            _history.RemoveRange(cut, _history.Count - cut);

            TurnCount = turnIndex;

            // 玩家实际做过几次选择 = 截断后历史里除开场引导之外的 user 消息条数。
            // 直接按 turnIndex 算会差一个：第 N 轮她说完话时，玩家还没选第 N 次。
            int picks = 0;
            for (int i = 1; i < _history.Count; i++)
                if (_history[i].fromPlayer) picks++;
            if (pickedTones.Count > picks)
                pickedTones.RemoveRange(picks, pickedTones.Count - picks);
            return true;
        }

        /// <param name="optionCount">
        /// 候选回复条数的临时覆盖（剧本 options:N）。0 = 按人格资产的扩展开关决定。
        /// </param>
        public VNAiConversation(VNAiPersonaDef persona, int optionCount = 0)
        {
            this.persona = persona != null
                ? persona
                : throw new ArgumentNullException(nameof(persona));
            _emotions = persona.ResolveEmotions();
            _marks = persona.ResolveMarks();
            _tones = persona.ResolveTones(optionCount);
            _schema = BuildSchema(_emotions, _marks, _tones);
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
            {
                // 合成的开场引导：是给模型的指令，不是玩家真说过的话。
                // 记下原文，收场总结拍平对话时要跳过它。
                _openingText = OpeningPrompt(ctx);
                _history.Add(VNAiMessage.Player(_openingText));
            }

            TrimHistory();

            var req = new VNAiRequest
            {
                provider = persona.ResolveProvider(),
                model = persona.ResolveModel(),
                systemInstruction = BuildSystemInstruction(persona, ctx, _emotions, _marks, _tones),
                responseSchemaJson = _schema,
                thinking = persona.thinking,
                safety = persona.safety,
                temperature = persona.temperature,
                maxOutputTokens = persona.maxOutputTokens,
            };
            AppendHistory(req);
            return req;
        }

        /// <summary>
        /// 把历史塞进请求。**她说过的话要不要包成 JSON，取决于供应商**。
        ///
        /// 【为什么要包】实测（deepseek-v4-flash，2026-08-17，A/B/C/D 四组对照）：
        ///   json_object 模式下，如果历史里 assistant 的消息是**纯文本**，
        ///   模型会照着「我上一条说的是纯文本」继续，可 JSON 模式又只准它出 JSON，
        ///   于是退化成吐一串**空白字符**（finish_reason=stop，content 是 20 个空格）。
        ///   第 1 轮没有 assistant 历史所以正常，**第 2 轮起必挂**——
        ///   表现出来就是「时好时坏的 BadResponse：回复正文为空」，其实是必现的。
        ///   把历史包成 JSON 后 4/4 全部正常返回完整对象。
        ///
        /// 【为什么只包 reply 一个字段】
        ///   完整还原（含 5 个选项）每条历史要多几十个 token，历史留 12 轮就是几百 token/轮；
        ///   实测只包 `{"reply":"…"}` 一样能纠正格式（E1~E3 三次全对），
        ///   而且 `_history` 仍只存台词纯文本——总结请求要拍平成对话记录、
        ///   试聊台域重载后要靠 reply 重建历史，都还是用同一份数据，不用多存一套。
        ///
        /// Gemini 那边有硬 schema，不存在这个问题，保持原样（也省这点 token）。
        /// </summary>
        void AppendHistory(VNAiRequest req)
        {
            if (VNAiProviders.SupportsResponseSchema(req.provider))
            {
                req.history.AddRange(_history);
                return;
            }

            foreach (var m in _history)
                req.history.Add(m.fromPlayer ? m : VNAiMessage.Model(WrapReplyAsJson(m.text)));
        }

        /// <summary>把一句台词包成 `{"reply":"…"}`（转义走客户端那套，中文不转 \u）。</summary>
        static string WrapReplyAsJson(string reply)
        {
            var sb = new StringBuilder(64);
            sb.Append("{\"reply\":");
            VNAiClient.Esc(sb, reply ?? "");
            sb.Append('}');
            return sb.ToString();
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
            List<string> emotions, List<string> marks, List<string> tones)
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
            if (ctx.turnsLeft > 0)
                now.Append("这场对话大约还剩 ").Append(ctx.turnsLeft)
                   .AppendLine(" 轮，请自然地把话题引向收尾，别突兀地结束。");
            Section(sb, "此刻的情况", now.ToString());

            // ── 跨场记忆：摘要给连续性，话题清单给去重 ──
            Section(sb, "你还记得的事", ctx.memory);

            if (ctx.pastTopics != null && ctx.pastTopics.Count > 0)
            {
                // 单独成段、措辞强硬。实测把话题揉进摘要里再说「别重复」效果差得多——
                // 明确列出来让模型逐个避开，才真的避得开。
                var avoid = new StringBuilder();
                avoid.AppendLine("下面这些话题**之前已经聊过了，这次不要再当作新话题提起**：");
                avoid.Append("  ").AppendLine(string.Join("、", ctx.pastTopics));
                avoid.AppendLine("要开新话题。如果非要提到旧话题，必须是「延续」而不是「重新问一遍」——");
                avoid.AppendLine("比如上次聊过值日，这次可以说「上次那袋垃圾后来怎么样了」，");
                avoid.Append("但不能再问一次「今天轮到谁值日」。");
                Section(sb, "避免重复", avoid.ToString());
            }

            // ── 输出规则 ──
            sb.AppendLine("【输出规则】");
            sb.Append("1. reply：你的台词，1~2 句，不超过 ").Append(p.maxReplyChars)
              .AppendLine(" 字。像真人说话，不要旁白、不要动作描写、不要加引号。");
            // AI 生成的台词进不了 FNV-1a 旁路翻译表（那是给写死的剧本用的），
            // 所以本地化只能靠这里切语言让它直接用目标语言写。
            // 简体那句是实测补的——不约束会混出「才、才沒有」这种繁体。
            sb.Append("   ").AppendLine(LanguageRule(tones != null ? tones.Count : 3));
            sb.Append("2. emotion：从这些里挑一个最贴切的 —— ")
              .AppendLine(string.Join(" / ", emotions));
            sb.Append("3. mark：漫画符号，不需要就填 ").Append(VNAiPersonaDef.NoMark)
              .Append("。可选 —— ").AppendLine(string.Join(" / ", marks));
            sb.Append("4. affection_delta：这一轮她对「").Append(me).Append("」的好感变化，整数，范围 -")
              .Append(p.affectionClamp).Append(" ~ +").Append(p.affectionClamp)
              .AppendLine("。多数轮次应该是 0；只有明显打动或惹恼她时才给非零值。");
            int n = tones != null ? tones.Count : 3;
            sb.Append("5. options：正好 ").Append(n).Append(" 个候选回复，全部以「").Append(me)
              .Append("」的第一人称口吻写，每个不超过 25 字。这 ").Append(n)
              .AppendLine(" 个的语气必须**按顺序**分别是：");
            if (tones != null)
                for (int i = 0; i < tones.Count; i++)
                    sb.Append("   ").Append(i + 1).Append(". ").AppendLine(tones[i]);
            sb.Append("   这 ").Append(n)
              .AppendLine(" 个选项要给出**实质不同**的走向，不要只是同一句话的几种说法。");
            if (n > 3)
                // 档位一多，模型容易把相邻两档写成近义句，这句是实测补的
                sb.AppendLine("   档位较多，务必让每一档都有自己明确的态度，" +
                              "相邻两档不要写成意思相近的句子。");
            sb.AppendLine("6. should_end：这个话题自然聊完了就填 true，否则 false。");
            sb.AppendLine();

            // ── 输出格式：只有「没有硬 schema」的家才需要 ──
            //   Gemini 走 responseSchema，键名/类型/enum/选项条数都是服务端强制的，
            //   再用提示词说一遍纯属浪费 token；
            //   DeepSeek 只有 json_object 模式（不认 json_schema），
            //   键名、类型、选项条数全靠这一段说清楚，实测不写就会漏 tone 字段。
            if (!VNAiProviders.SupportsResponseSchema(p.ResolveProvider()))
            {
                sb.AppendLine(BuildJsonFormatPrompt(emotions, marks, tones, p));
                sb.AppendLine();
            }

            sb.AppendLine("【绝对边界】");
            sb.AppendLine(string.IsNullOrWhiteSpace(p.boundaries) ? "（无）" : p.boundaries.Trim());
            // 语言约束放在**最后一行**：越靠后权重越高。
            // 实测只写在「输出规则第 1 条」时，偶发会在候选回复里混出繁体（「沒」「說」），
            // 挪到这里之后才稳住。这一条属于宁可重复也不能漏的硬约束。
            sb.AppendLine(LanguageRule(tones != null ? tones.Count : 3));
            sb.Append("只输出 JSON，不要任何额外文字、不要代码块围栏。");

            return sb.ToString();
        }

        /// <summary>
        /// 按当前语言给出「用什么语言写台词」的指令。
        /// 候选回复（options）也跟着走同一语言，否则会出现她说日文、我的选项是中文。
        /// </summary>
        static string LanguageRule(int optionCount)
        {
            switch (VNLocale.Language)
            {
                case VNLanguage.English:
                    return $"Write reply and all {optionCount} options in natural English. " +
                           "Do not use Chinese or Japanese.";
                case VNLanguage.Japanese:
                    return $"セリフと{optionCount}つの選択肢はすべて自然な日本語で書くこと。" +
                           "中国語や英語を混ぜないこと。";
                default:
                    return $"台词与 {optionCount} 个候选回复全部使用简体中文，" +
                           "禁止出现繁体字或其他语言。";
            }
        }

        /// <summary>
        /// 把 responseSchema 翻译成人话 + 一个填好的示例，给不支持硬 schema 的家用。
        ///
        /// 【为什么要给示例而不只是描述】
        ///   options 里每项是 {"text","tone"} 这种嵌套结构，光用文字描述实测会漏 tone；
        ///   给一个把 tone 全部填好的示例，模型照抄结构的成功率高得多，
        ///   顺带又把「第 N 个选项必须是第 N 种语气」这条规则重复了一遍。
        ///
        /// 【"json" 这个词必须出现】
        ///   DeepSeek 的 json_object 模式要求提示词里出现 json 字样，否则可能不生效。
        ///
        /// 注意：这段只是**软**约束。模型仍可能编出不存在的表情名或少给一个选项——
        /// 那是 TryParseTurn 的降级/补齐/钳制负责兜底的，两道防线缺一不可。
        /// </summary>
        public static string BuildJsonFormatPrompt(
            List<string> emotions, List<string> marks, List<string> tones, VNAiPersonaDef p)
        {
            int n = tones != null && tones.Count > 0 ? tones.Count : 3;
            string firstEmotion = emotions != null && emotions.Count > 0 ? emotions[0] : "平静";
            int clamp = p != null ? p.affectionClamp : 2;

            var sb = new StringBuilder(512);
            sb.AppendLine("【输出格式】");
            sb.AppendLine("只输出一个 json 对象，不要代码块围栏，不要任何解释文字。");
            sb.AppendLine("键名、类型、层级严格照下面这个例子（值换成你自己的内容）：");
            sb.Append("{\"reply\":\"你的台词\",\"emotion\":\"").Append(firstEmotion)
              .Append("\",\"mark\":\"").Append(VNAiPersonaDef.NoMark)
              .Append("\",\"affection_delta\":0,\"options\":[");
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(',');
                string tone = tones != null && i < tones.Count ? tones[i] : "普通";
                sb.Append("{\"text\":\"第").Append(i + 1).Append("个候选回复\",\"tone\":\"")
                  .Append(tone).Append("\"}");
            }
            sb.AppendLine("],\"should_end\":false}");
            sb.AppendLine("硬性要求：");
            sb.Append("  - emotion 只能是这几个之一：").AppendLine(string.Join(" / ", emotions));
            sb.Append("  - mark 只能是这几个之一：").AppendLine(string.Join(" / ", marks));
            sb.Append("  - affection_delta 是整数，范围 -").Append(clamp)
              .Append(" ~ +").Append(clamp).AppendLine("，多数时候是 0");
            sb.Append("  - options 必须正好 ").Append(n)
              .AppendLine(" 项，每项都要有 text 和 tone 两个键，tone 按上面例子里的顺序原样填");
            sb.Append("  - should_end 是布尔值 true / false，不要写成字符串");
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
            int n = tones != null && tones.Count > 0 ? tones.Count : 3;
            var sb = new StringBuilder(512);
            sb.Append("{\"type\":\"OBJECT\",\"properties\":{");
            sb.Append("\"reply\":{\"type\":\"STRING\"},");
            sb.Append("\"emotion\":{\"type\":\"STRING\",\"enum\":").Append(JsonStringArray(emotions)).Append("},");
            sb.Append("\"mark\":{\"type\":\"STRING\",\"enum\":").Append(JsonStringArray(marks)).Append("},");
            sb.Append("\"affection_delta\":{\"type\":\"INTEGER\"},");
            sb.Append("\"options\":{\"type\":\"ARRAY\",\"minItems\":").Append(n)
              .Append(",\"maxItems\":").Append(n).Append(",\"items\":{");
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
            => TryParseTurn(json, persona, _emotions, _marks, _tones, out turn, out error);

        public static bool TryParseTurn(
            string json, VNAiPersonaDef p,
            List<string> emotions, List<string> marks, List<string> tones,
            out VNAiTurn turn, out string error)
        {
            int want = tones != null && tones.Count > 0 ? tones.Count : 3;
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

            // 繁体兜底：提示词已经要求简体且放在最后一行，但实测仍会漏（见
            // VNAiTextNormalize 的注释）。玩家直接看得见的文本一律过一道。
            int tradCount = VNAiTextNormalize.CountTraditional(raw.reply);
            if (tradCount > 0)
                Debug.LogWarning($"[VNAi] 台词里有 {tradCount} 个繁体字，已自动转简体：{raw.reply}");

            turn = new VNAiTurn
            {
                reply = VNAiTextNormalize.ToSimplified(raw.reply.Trim()),
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

            // 选项：不足就补齐，多的截断——条数必须和 schema 要的一致，
            // 否则面板排版和「倾向」统计都会错位
            if (raw.options != null)
                foreach (var o in raw.options)
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.text)) continue;
                    turn.options.Add(new VNAiOption
                    {
                        text = VNAiTextNormalize.ToSimplified(o.text.Trim()),
                        tone = o.tone,
                    });
                    if (turn.options.Count == want) break;
                }
            while (turn.options.Count < want)
            {
                int i = turn.options.Count;
                turn.options.Add(new VNAiOption
                {
                    text = "……",
                    tone = tones != null && i < tones.Count ? tones[i] : "",
                });
                Debug.LogWarning($"[VNAi] 候选回复不足 {want} 个，已用「……」补齐第 {i + 1} 项");
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

        // ──────────────── 收场总结：记忆 + 日记 ────────────────

        /// <summary>
        /// 聊完之后**额外发一次请求**得到的东西：给模型自己看的记忆，
        /// 以及给玩家看的日记正文。
        /// </summary>
        public class VNAiSessionSummary
        {
            public string summary;                            // 一句话，第三人称
            public List<string> topics = new List<string>();  // 聊过的话题标签
            public List<string> facts = new List<string>();   // 她透露的具体信息
            public string diary;                              // 主角口吻的日记正文
        }

        /// <summary>
        /// 组装「总结这场对话」的请求。
        ///
        /// 为什么单独发一次而不是塞进主 schema：
        ///   - 主 schema 每轮都在用，加字段等于每轮都在算摘要，纯浪费
        ///   - ESC 提前退出时主 schema 那条路根本走不到
        ///   - 总结要的语气（第三人称、给自己看）和台词完全不同，混在一起会互相干扰
        /// 代价是一场多约 $0.001，相对一场 $0.006~0.013 可以忽略。
        /// </summary>
        public VNAiRequest BuildSummaryRequest(VNAiContext ctx, string playerName)
        {
            string me = string.IsNullOrWhiteSpace(playerName) ? "我" : playerName.Trim();
            var sb = new StringBuilder(512);

            string her = persona.DisplayName;

            // ★ 先把角色扮演的身份摘掉。这个请求复用了刚才的对话历史，
            //   模型仍处在「我就是她」的语境里；不显式切换身份，
            //   它写日记时会自动用她的口吻（实测踩过：要主角视角，出来的是她的独白）。
            sb.AppendLine("【现在停止角色扮演。】");
            sb.Append("你不再是「").Append(her)
              .AppendLine("」，而是一个旁观刚才那段对话的记录者。");
            sb.AppendLine();
            sb.Append("刚才的对话发生在「").Append(her).Append("」（女方）和「").Append(me)
              .AppendLine("」（男方，玩家扮演的主角宅男）之间。请你做两件事：");
            sb.AppendLine();
            sb.Append("1. 为**").Append(her)
              .AppendLine("**整理这次对话的记忆，供她下次见面时回忆：");
            sb.AppendLine("   - summary：一句话概括这次聊了什么、气氛如何。用第三人称，30 字以内。");
            sb.AppendLine("   - topics：这次聊到的话题标签，每个 2~6 字的名词短语");
            sb.AppendLine("     （如「值日」「数学作业」「草莓牛奶」）。只列真的聊到的，最多 6 个。");
            sb.Append("   - facts：对话中透露出的、下次仍然成立的具体信息（如「")
              .Append(me).AppendLine("答应请她喝草莓牛奶」）。没有就给空数组，最多 4 条。");
            sb.AppendLine();
            sb.Append("2. diary：**").Append(me).Append("（男方宅男）**当天晚上写的宅男日记，60~120 字。");
            sb.AppendLine();
            sb.Append("   ★ 用「我」自称时，这个「我」指的是 ").Append(me)
              .Append("，**不是** ").Append(her).AppendLine("。");
            sb.Append("   ★ 绝对不要用 ").Append(her)
              .AppendLine(" 的口吻或她的口头禅写这段日记——这是他的日记本，不是她的。");
            sb.Append("   写他眼中的").Append(her)
              .AppendLine("是什么样子、他当时心里在想什么，可以有藏不住的小心思。");
            sb.AppendLine("   不要复述对话原文，也不要写成流水账；挑最有感觉的一两个瞬间来写。");
            sb.AppendLine();
            sb.AppendLine("全部使用简体中文，禁止繁体字。只输出 JSON。");

            // 没有硬 schema 的家（DeepSeek）：键名和类型只能靠提示词说清楚
            var provider = persona.ResolveProvider();
            if (!VNAiProviders.SupportsResponseSchema(provider))
            {
                sb.AppendLine();
                sb.AppendLine("【输出格式】只输出一个 json 对象，不要代码块围栏、不要解释文字。");
                sb.AppendLine("键名与类型严格如下（不能多、不能少、不能改名）：");
                sb.AppendLine("{\"summary\":\"字符串\",\"topics\":[\"字符串\",\"…最多6个\"]," +
                              "\"facts\":[\"字符串\",\"…最多4条，没有就给 []\"],\"diary\":\"字符串\"}");
            }

            var req = new VNAiRequest
            {
                provider = provider,
                model = persona.ResolveModel(),
                systemInstruction = sb.ToString(),
                responseSchemaJson = SummarySchema,
                // 总结是归纳任务，不需要思考预算；这里固定 Minimal 不跟随人格设置，
                // 免得人格开了 High 之后每场结束都白白多花几千个思考 token
                thinking = VNAiThinking.Minimal,
                safety = persona.safety,
                temperature = 0.7f,     // 归纳要稳，不要发散
                maxOutputTokens = 512,
            };
            // ★ 关键：把对话**拍平成一段纯文本**放进单条 user 消息，
            //   而不是按 role:user/model 交替发过去。
            //   否则她的台词标着 role:model，模型看到「自己说过这些话」，
            //   会结构性地代入她的身份——实测此时无论 system prompt 怎么强调
            //   「停止角色扮演」「用男方口吻」，写出来的日记仍是她的独白。
            //   拍平之后模型只是在读一份别人的对话记录，身份代入自然消失。
            var transcript = new StringBuilder(1024);
            transcript.AppendLine("以下是这次对话的完整记录：");
            transcript.AppendLine();
            foreach (var m in _history)
            {
                // 跳过合成的开场引导——那是给模型的指令，不是玩家说过的话
                if (m.fromPlayer && !string.IsNullOrEmpty(_openingText) &&
                    m.text == _openingText) continue;
                transcript.Append(m.fromPlayer ? me : her).Append("：")
                          .AppendLine(m.text);
            }
            transcript.AppendLine();
            transcript.Append("请按上面的要求输出 JSON。");

            req.history.Add(VNAiMessage.Player(transcript.ToString()));
            return req;
        }

        const string SummarySchema =
            @"{""type"":""OBJECT"",""properties"":{
                ""summary"":{""type"":""STRING""},
                ""topics"":{""type"":""ARRAY"",""maxItems"":6,""items"":{""type"":""STRING""}},
                ""facts"":{""type"":""ARRAY"",""maxItems"":4,""items"":{""type"":""STRING""}},
                ""diary"":{""type"":""STRING""}},
              ""required"":[""summary"",""topics"",""diary""],
              ""propertyOrdering"":[""summary"",""topics"",""facts"",""diary""]}";

        /// <summary>解析总结。失败时返回 false，调用方跳过记忆与日记即可（不影响结算）。</summary>
        public static bool TryParseSummary(string json, out VNAiSessionSummary summary,
                                           out string error)
        {
            summary = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "总结为空"; return false; }

            RawSummary raw;
            try { raw = JsonUtility.FromJson<RawSummary>(StripFence(json)); }
            catch (Exception e) { error = $"总结 JSON 解析失败：{e.Message}"; return false; }
            if (raw == null) { error = "总结解析结果为空"; return false; }

            // 同台词：日记是玩家直接看的，摘要与话题会被写进存档并注入下一次提示词，
            // 全部过一道繁转简，免得繁体字在记忆里越积越多
            summary = new VNAiSessionSummary
            {
                summary = VNAiTextNormalize.ToSimplified(raw.summary?.Trim() ?? ""),
                diary = VNAiTextNormalize.ToSimplified(raw.diary?.Trim() ?? ""),
            };
            if (raw.topics != null)
                foreach (string t in raw.topics)
                    if (!string.IsNullOrWhiteSpace(t))
                        summary.topics.Add(VNAiTextNormalize.ToSimplified(t.Trim()));
            if (raw.facts != null)
                foreach (string f in raw.facts)
                    if (!string.IsNullOrWhiteSpace(f))
                        summary.facts.Add(VNAiTextNormalize.ToSimplified(f.Trim()));

            if (string.IsNullOrEmpty(summary.summary) && summary.topics.Count == 0)
            {
                error = "总结既没有 summary 也没有 topics";
                return false;
            }
            return true;
        }

        [Serializable] class RawSummary
        {
            public string summary;
            public string[] topics;
            public string[] facts;
            public string diary;
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
            // 兜底轮的条数也要和正常轮一致，否则断网那一轮面板会突然变高矮
            for (int i = 0; i < _tones.Count; i++)
            {
                string text = persona.fallbackOptions != null && i < persona.fallbackOptions.Count
                    ? persona.fallbackOptions[i] : "……";
                t.options.Add(new VNAiOption { text = text, tone = _tones[i] });
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
