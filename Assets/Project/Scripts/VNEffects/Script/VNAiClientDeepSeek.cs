using System;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// DeepSeek（/chat/completions，OpenAI 兼容格式）的拼包与解包。
    /// **纯静态、不碰网络**——HTTP 全在 VNAiClient.Send 里。
    ///
    /// 与 Gemini 的四个结构性差异（改这个文件前先读完）：
    ///
    /// 1. **没有硬 schema**。只有 `response_format:{"type":"json_object"}`，
    ///    不认 json_schema，也就没有 enum / minItems 这种硬约束——
    ///    模型完全可能编出不存在的表情名、给两个选项。
    ///    应对：VNAiConversation 把 schema 翻译成提示词里的格式说明，
    ///    再靠 TryParseTurn 的「越界降级 / 补齐 / 钳制」兜底。
    ///    官方还要求**提示词里必须出现 "json" 字样**，否则可能不进 JSON 模式，
    ///    格式说明段里已经写了。
    ///
    /// 2. **没有 system prompt 独立字段**。systemInstruction 要拼成
    ///    messages 的第一条 `role:"system"`；助手侧角色名是 `assistant` 不是 `model`。
    ///
    /// 3. **没有 safetySettings**。审核不可调，VNAiSafety 选什么都一样；
    ///    被拦下时 finish_reason = "content_filter"。
    ///
    /// 4. **思考只有三档**（low/high/max）+ 一个开关。VNAiThinking 四档映射见
    ///    ThinkingJson()。思考 token 走 completion_tokens_details.reasoning_tokens，
    ///    **按输出价计费**。
    ///
    /// 另外它会返回 prompt_cache_hit_tokens：命中提示缓存的输入 token 便宜约 30 倍。
    /// 我们每轮都重发整段 system prompt + 历史，正好是缓存最吃香的场景，
    /// 所以这个数要单独带回去算钱（见 VNAiPricing）。
    /// </summary>
    internal static class VNAiClientDeepSeek
    {
        const string ChatEndpoint = "https://api.deepseek.com/chat/completions";

        /// <summary>模型名在请求体里，不在 URL 里（与 Gemini 相反），参数只为签名一致。</summary>
        public static string Endpoint(string model) => ChatEndpoint;

        // ── 请求体 ─────────────────────────────────────────────────

        public static string BuildBody(VNAiRequest req)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');

            sb.Append("\"model\":");
            VNAiClient.Esc(sb, req.ResolveModel());

            sb.Append(",\"messages\":[");
            bool first = true;
            if (!string.IsNullOrEmpty(req.systemInstruction))
            {
                sb.Append("{\"role\":\"system\",\"content\":");
                VNAiClient.Esc(sb, req.systemInstruction);
                sb.Append('}');
                first = false;
            }
            if (req.history != null)
            {
                foreach (var m in req.history)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"role\":\"").Append(m.fromPlayer ? "user" : "assistant")
                      .Append("\",\"content\":");
                    VNAiClient.Esc(sb, m.text ?? "");
                    sb.Append('}');
                }
            }
            sb.Append(']');

            sb.Append(",\"max_tokens\":").Append(Mathf.Max(16, req.maxOutputTokens));
            sb.Append(",\"temperature\":").Append(VNAiClient.Num(req.temperature));
            sb.Append(",\"thinking\":").Append(ThinkingJson(req.thinking));

            // schema 发不出去，但「要 JSON」这件事还是要说
            if (!string.IsNullOrWhiteSpace(req.responseSchemaJson))
                sb.Append(",\"response_format\":{\"type\":\"json_object\"}");

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// 四档 → DeepSeek 的开关 + 三档。Minimal 直接关掉思考（聊天场景推荐）：
        /// 思考 token 按输出价计费，开了既慢又贵，而角色扮演基本用不上。
        /// </summary>
        static string ThinkingJson(VNAiThinking t)
        {
            switch (t)
            {
                case VNAiThinking.Low: return "{\"type\":\"enabled\",\"reasoning_effort\":\"low\"}";
                case VNAiThinking.Medium: return "{\"type\":\"enabled\",\"reasoning_effort\":\"high\"}";
                case VNAiThinking.High: return "{\"type\":\"enabled\",\"reasoning_effort\":\"max\"}";
                default: return "{\"type\":\"disabled\"}";
            }
        }

        // ── 响应解析 ───────────────────────────────────────────────

        /// <summary>
        /// 解析 200 响应。返回 None = 成功（result.text 已填好）；
        /// 否则返回失败类型 + 人话说明，由 VNAiClient 统一收尾。
        /// </summary>
        public static VNAiFailure Parse(string payload, VNAiResult result, out string error)
        {
            error = null;

            ChatResponse resp;
            try { resp = JsonUtility.FromJson<ChatResponse>(payload); }
            catch (Exception e)
            {
                error = $"响应不是合法 JSON（{e.GetType().Name}）：{VNAiClient.Brief(payload)}";
                return VNAiFailure.BadResponse;
            }

            if (resp == null)
            {
                error = $"响应为空：{VNAiClient.Brief(payload)}";
                return VNAiFailure.BadResponse;
            }

            if (resp.usage != null)
            {
                result.promptTokens = resp.usage.prompt_tokens;
                result.outputTokens = resp.usage.completion_tokens;
                result.totalTokens = resp.usage.total_tokens;
                result.cachedPromptTokens = resp.usage.prompt_cache_hit_tokens;
                if (resp.usage.completion_tokens_details != null)
                {
                    // 思考 token 已经含在 completion_tokens 里，这里拆出来只为显示；
                    // 单价相同（都按输出价），所以算钱时**不能再加一遍**
                    result.thoughtsTokens = resp.usage.completion_tokens_details.reasoning_tokens;
                    result.outputTokens = Mathf.Max(0,
                        result.outputTokens - result.thoughtsTokens);
                }
            }

            if (resp.choices == null || resp.choices.Length == 0)
            {
                // 官方文档提到 JSON 模式偶发返回空内容，报清楚一点便于分辨
                error = $"响应没有 choices：{VNAiClient.Brief(payload)}";
                return VNAiFailure.BadResponse;
            }

            var choice = resp.choices[0];
            result.finishReason = choice.finish_reason;

            if (choice.finish_reason == "content_filter")
            {
                error = "回复被内容安全拦截（finish_reason=content_filter）";
                return VNAiFailure.Blocked;
            }

            if (choice.finish_reason == "insufficient_system_resource")
            {
                error = "服务端资源不足（insufficient_system_resource），稍后重试";
                return VNAiFailure.Server;
            }

            string text = choice.message != null ? choice.message.content : null;

            if (string.IsNullOrWhiteSpace(text))
            {
                // 「空」通常不是真的空，而是**一串空白字符**——这是 json_object 模式的
                // 一个具体退化：历史里 assistant 的消息不是 JSON 时，模型会照着纯文本的
                // 样子继续，但 JSON 模式又不准它出纯文本，于是吐空白。
                // 已由 VNAiConversation.AppendHistory 把历史包成 JSON 解决；
                // 再遇到就先去看请求体里 assistant 那几条长什么样。
                error = $"回复正文为空（finish_reason={choice.finish_reason}，" +
                        $"content 长度 {(text == null ? 0 : text.Length)}）。" +
                        "JSON 模式下这通常意味着历史里的 assistant 消息不是 JSON，" +
                        "少数情况是官方说的偶发空内容（重试即可）";
                return VNAiFailure.BadResponse;
            }

            // 截断时结构化 JSON 一定是残缺的，别让上层去解析半截 JSON
            if (choice.finish_reason == "length")
            {
                result.text = text;
                error = "输出超长被截断（调大 maxOutputTokens，或在提示词里限制台词长度）";
                return VNAiFailure.Truncated;
            }

            result.text = text;
            return VNAiFailure.None;
        }

        // ── JsonUtility 用的响应映射（只声明我们要的字段，多余的会被忽略）──

        [Serializable] class ChatResponse
        {
            public Choice[] choices;
            public Usage usage;
        }
        [Serializable] class Choice { public Message message; public string finish_reason; }
        [Serializable] class Message { public string content; public string reasoning_content; }
        [Serializable] class Usage
        {
            public int prompt_tokens;
            public int completion_tokens;
            public int total_tokens;
            public int prompt_cache_hit_tokens;
            public int prompt_cache_miss_tokens;
            public CompletionDetails completion_tokens_details;
        }
        [Serializable] class CompletionDetails { public int reasoning_tokens; }
    }
}
