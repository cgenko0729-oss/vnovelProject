using System;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// Gemini（generateContent）的拼包与解包。**纯静态、不碰网络**——
    /// HTTP 全在 VNAiClient.Send 里，这里只负责「VNAiRequest → 请求体 JSON」
    /// 和「响应 JSON → VNAiResult」。
    ///
    /// 实测契约（2026-08，gemini-3.5-flash-lite，逐项 curl 验证过）：
    ///   - 端点 v1beta，鉴权走 x-goog-api-key 请求头（不用 ?key= 查询参数，
    ///     那会把 key 写进各种日志和 URL 历史）
    ///   - thinkingLevel 必须放在 generationConfig.thinkingConfig 里面；
    ///     放外层 400 Unknown name；取值只有 minimal/low/medium/high
    ///   - responseSchema 支持 enum / minItems / maxItems / propertyOrdering
    ///     （这是它相对 DeepSeek 的最大优势：格式是**硬**约束）
    ///   - 被安全拦下时 candidates[0].content.parts 是空的，直接取 parts[0] 会空引用
    /// </summary>
    internal static class VNAiClientGemini
    {
        const string EndpointFormat =
            "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        public static string Endpoint(string model) => string.Format(EndpointFormat, model);

        // ── 请求体 ─────────────────────────────────────────────────

        public static string BuildBody(VNAiRequest req)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');

            if (!string.IsNullOrEmpty(req.systemInstruction))
            {
                sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":");
                VNAiClient.Esc(sb, req.systemInstruction);
                sb.Append("}]},");
            }

            sb.Append("\"contents\":[");
            if (req.history != null)
            {
                for (int i = 0; i < req.history.Count; i++)
                {
                    var m = req.history[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"role\":\"").Append(m.fromPlayer ? "user" : "model")
                      .Append("\",\"parts\":[{\"text\":");
                    VNAiClient.Esc(sb, m.text ?? "");
                    sb.Append("}]}");
                }
            }
            sb.Append("],");

            sb.Append("\"generationConfig\":{");
            sb.Append("\"maxOutputTokens\":").Append(Mathf.Max(16, req.maxOutputTokens));
            sb.Append(",\"temperature\":").Append(VNAiClient.Num(req.temperature));
            sb.Append(",\"thinkingConfig\":{\"thinkingLevel\":\"")
              .Append(ThinkingName(req.thinking)).Append("\"}");
            if (!string.IsNullOrWhiteSpace(req.responseSchemaJson))
            {
                sb.Append(",\"responseMimeType\":\"application/json\"");
                sb.Append(",\"responseSchema\":").Append(req.responseSchemaJson);
            }
            sb.Append('}');

            if (req.safety == VNAiSafety.BlockOnlyHigh)
            {
                sb.Append(",\"safetySettings\":[");
                string[] cats = {
                    "HARM_CATEGORY_HARASSMENT", "HARM_CATEGORY_HATE_SPEECH",
                    "HARM_CATEGORY_SEXUALLY_EXPLICIT", "HARM_CATEGORY_DANGEROUS_CONTENT",
                };
                for (int i = 0; i < cats.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"category\":\"").Append(cats[i])
                      .Append("\",\"threshold\":\"BLOCK_ONLY_HIGH\"}");
                }
                sb.Append(']');
            }

            sb.Append('}');
            return sb.ToString();
        }

        static string ThinkingName(VNAiThinking t)
        {
            switch (t)
            {
                case VNAiThinking.Low: return "low";
                case VNAiThinking.Medium: return "medium";
                case VNAiThinking.High: return "high";
                default: return "minimal";
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

            GeminiResponse resp;
            try { resp = JsonUtility.FromJson<GeminiResponse>(payload); }
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

            if (resp.usageMetadata != null)
            {
                result.promptTokens = resp.usageMetadata.promptTokenCount;
                result.outputTokens = resp.usageMetadata.candidatesTokenCount;
                result.thoughtsTokens = resp.usageMetadata.thoughtsTokenCount;
                result.totalTokens = resp.usageMetadata.totalTokenCount;
            }

            // 整个 prompt 被安全策略挡下：连 candidates 都不会有
            if (resp.promptFeedback != null && !string.IsNullOrEmpty(resp.promptFeedback.blockReason))
            {
                error = $"请求被内容安全拦截（blockReason={resp.promptFeedback.blockReason}）";
                return VNAiFailure.Blocked;
            }

            if (resp.candidates == null || resp.candidates.Length == 0)
            {
                error = $"响应没有 candidates：{VNAiClient.Brief(payload)}";
                return VNAiFailure.BadResponse;
            }

            var cand = resp.candidates[0];
            result.finishReason = cand.finishReason;

            // 输出侧被拦：finishReason=SAFETY，此时 content.parts 是空的
            if (cand.finishReason == "SAFETY" || cand.finishReason == "PROHIBITED_CONTENT" ||
                cand.finishReason == "BLOCKLIST" || cand.finishReason == "RECITATION")
            {
                error = $"回复被内容安全拦截（finishReason={cand.finishReason}）";
                return VNAiFailure.Blocked;
            }

            string text = null;
            if (cand.content != null && cand.content.parts != null)
                foreach (var p in cand.content.parts)
                    if (p != null && !string.IsNullOrEmpty(p.text)) { text = p.text; break; }

            if (string.IsNullOrEmpty(text))
            {
                error = $"回复正文为空（finishReason={cand.finishReason}）";
                return VNAiFailure.BadResponse;
            }

            // MAX_TOKENS 时结构化 JSON 一定是残缺的，别让上层去解析半截 JSON
            if (cand.finishReason == "MAX_TOKENS")
            {
                result.text = text;
                error = "输出超长被截断（调大 maxOutputTokens，或在提示词里限制台词长度）";
                return VNAiFailure.Truncated;
            }

            result.text = text;
            return VNAiFailure.None;
        }

        // ── JsonUtility 用的响应映射（只声明我们要的字段，多余的会被忽略）──

        [Serializable] class GeminiResponse
        {
            public Candidate[] candidates;
            public UsageMetadata usageMetadata;
            public PromptFeedback promptFeedback;
        }
        [Serializable] class Candidate { public Content content; public string finishReason; }
        [Serializable] class Content { public Part[] parts; public string role; }
        [Serializable] class Part { public string text; }
        [Serializable] class PromptFeedback { public string blockReason; }
        [Serializable] class UsageMetadata
        {
            public int promptTokenCount;
            public int candidatesTokenCount;
            public int thoughtsTokenCount;
            public int totalTokenCount;
        }
    }
}
