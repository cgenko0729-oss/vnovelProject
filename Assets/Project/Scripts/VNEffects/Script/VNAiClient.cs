using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VNEffects
{
    /// <summary>对话历史里的一条消息。Gemini 的角色名是 user / model（不是 assistant）。</summary>
    public struct VNAiMessage
    {
        public bool fromPlayer;   // true = 玩家（user），false = 角色（model）
        public string text;

        public static VNAiMessage Player(string t) => new VNAiMessage { fromPlayer = true, text = t };
        public static VNAiMessage Model(string t) => new VNAiMessage { fromPlayer = false, text = t };
    }

    /// <summary>
    /// 内容安全阈值。Gemini 允许按类别放宽——恋爱向的暧昧对话默认阈值容易被误挡，
    /// 所以本项目默认用 BlockOnlyHigh。注意这**不是**关掉审核，只是把线抬高。
    /// </summary>
    public enum VNAiSafety
    {
        [InspectorName("默认（Google 默认阈值，最严）")] Default = 0,
        [InspectorName("仅拦截高危（推荐，恋爱向对话不易误挡）")] BlockOnlyHigh = 1,
    }

    /// <summary>
    /// 思考档位。实测 gemini-3.5-flash-lite：不写和 minimal 都是 0 思考 token、约 0.8s；
    /// low/medium/high 约多 100+ token、约 1.2s。聊天用 Minimal 即可。
    /// （没有 "off" 和 "dynamic"，填了会 400。）
    /// </summary>
    public enum VNAiThinking
    {
        [InspectorName("minimal（推荐：不思考，最快）")] Minimal = 0,
        [InspectorName("low")] Low = 1,
        [InspectorName("medium")] Medium = 2,
        [InspectorName("high")] High = 3,
    }

    /// <summary>一次请求的全部输入。由 VNAiConversation 组装，VNAiClient 只负责发。</summary>
    public class VNAiRequest
    {
        public string model = VNAiClient.DefaultModel;
        public string systemInstruction;
        public List<VNAiMessage> history = new List<VNAiMessage>();

        /// <summary>
        /// 结构化输出的 JSON Schema（原样内嵌的 JSON 文本）。
        /// 留空 = 纯文本模式（连通性自检用）。
        /// </summary>
        public string responseSchemaJson;

        public VNAiThinking thinking = VNAiThinking.Minimal;
        public VNAiSafety safety = VNAiSafety.BlockOnlyHigh;
        public int maxOutputTokens = 1024;
        public float temperature = 1f;

        public int timeoutSeconds = 30;
        public int maxRetries = 2;     // 只对 429 / 5xx / 网络错误重试
    }

    /// <summary>请求为什么结束——决定模块该走正常分支还是兜底分支。</summary>
    public enum VNAiFailure
    {
        None = 0,
        NoKey,          // 没配 key
        Network,        // 断网 / DNS / 超时
        Auth,           // 401 / 403，key 无效或没开通
        RateLimited,    // 429，重试后仍失败
        Server,         // 5xx
        Blocked,        // 被内容安全拦下（prompt 或输出）
        Truncated,      // MAX_TOKENS，JSON 被截断
        BadResponse,    // 200 但结构不对/解析不出
    }

    /// <summary>一次请求的结果。</summary>
    public class VNAiResult
    {
        public bool ok;
        public string text;             // 模型输出的正文（结构化模式下即那串 JSON）
        public VNAiFailure failure;
        public string errorMessage;     // 给开发者看的人话，已剔除 key
        public string finishReason;     // STOP / MAX_TOKENS / SAFETY ...
        public int promptTokens, outputTokens, thoughtsTokens, totalTokens;
        public long httpCode;
        public float elapsedSeconds;

        /// <summary>估算本次花费（美元）。Flash Lite: $0.30 / $2.50 每百万 token。</summary>
        public double EstimatedCostUsd =>
            promptTokens * 0.30 / 1e6 + (outputTokens + thoughtsTokens) * 2.50 / 1e6;
    }

    /// <summary>
    /// Gemini generateContent 的协程封装。
    ///
    /// ★ 全项目唯一碰 HTTP 的文件 ★
    ///   换模型 / 换供应商 / 改成走自建中转服务器，都只改这一个文件，
    ///   上层（VNAiConversation / VNAiTalkModule）一行都不用动。
    ///
    /// 用协程而不是 async/await：与项目现有 IEnumerator 风格一致，
    /// 且 Runner 的 EventCo 本来就是「while(result==null) yield return null」轮询，
    /// 天然容得下一个要等 1~2 秒的网络请求，不阻塞主线程。
    ///
    /// 实测契约（2026-08，gemini-3.5-flash-lite，逐项 curl 验证过）：
    ///   - 端点 v1beta，鉴权走 x-goog-api-key 请求头（不用 ?key= 查询参数，
    ///     那会把 key 写进各种日志和 URL 历史）
    ///   - thinkingLevel 必须放在 generationConfig.thinkingConfig 里面；
    ///     放外层 400 Unknown name；取值只有 minimal/low/medium/high
    ///   - responseSchema 支持 enum / minItems / maxItems / propertyOrdering
    ///   - 被安全拦下时 candidates[0].content.parts 是空的，直接取 parts[0] 会空引用
    /// </summary>
    public static class VNAiClient
    {
        public const string DefaultModel = "gemini-3.5-flash-lite";
        const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

        /// <summary>
        /// 发一次请求。onDone 必定被回调一次（成功或失败），调用方据此结束等待。
        /// 用法：yield return StartCoroutine(VNAiClient.Send(req, r =&gt; result = r));
        /// </summary>
        public static IEnumerator Send(VNAiRequest req, Action<VNAiResult> onDone)
        {
            var result = new VNAiResult();
            float t0 = Time.realtimeSinceStartup;

            if (req == null)
            {
                Finish(result, VNAiFailure.BadResponse, "请求对象为空", t0, onDone);
                yield break;
            }

            if (!VNAiKey.TryGet(out string key, out string _))
            {
                Finish(result, VNAiFailure.NoKey, VNAiKey.MissingKeyMessage(), t0, onDone);
                yield break;
            }

            string url = string.Format(Endpoint,
                string.IsNullOrEmpty(req.model) ? DefaultModel : req.model);
            byte[] body = Encoding.UTF8.GetBytes(BuildBody(req));

            int attempt = 0;
            while (true)
            {
                attempt++;
                using (var www = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
                {
                    www.uploadHandler = new UploadHandlerRaw(body);
                    www.downloadHandler = new DownloadHandlerBuffer();
                    www.SetRequestHeader("Content-Type", "application/json");
                    www.SetRequestHeader("x-goog-api-key", key);   // ← key 只出现在这里
                    www.timeout = Mathf.Max(5, req.timeoutSeconds);

                    yield return www.SendWebRequest();

                    result.httpCode = www.responseCode;
                    bool transportError = www.result == UnityWebRequest.Result.ConnectionError ||
                                          www.result == UnityWebRequest.Result.DataProcessingError;
                    string payload = www.downloadHandler != null ? www.downloadHandler.text : null;

                    // 可重试：网络错误 / 429 / 5xx
                    bool retryable = transportError ||
                                     www.responseCode == 429 ||
                                     www.responseCode >= 500;
                    if (retryable && attempt <= Mathf.Max(0, req.maxRetries))
                    {
                        float wait = Mathf.Pow(2f, attempt - 1) * 1.2f;   // 1.2s / 2.4s
                        Debug.LogWarning($"[VNAi] 第 {attempt} 次请求失败" +
                                         $"（HTTP {www.responseCode}），{wait:0.0}s 后重试");
                        yield return new WaitForSecondsRealtime(wait);
                        continue;
                    }

                    if (transportError)
                    {
                        Finish(result, VNAiFailure.Network,
                               $"网络错误：{www.error}（检查网络代理是否能访问 Google）", t0, onDone);
                        yield break;
                    }

                    if (www.responseCode == 401 || www.responseCode == 403)
                    {
                        Finish(result, VNAiFailure.Auth,
                               $"鉴权失败（HTTP {www.responseCode}）：key 无效、已吊销，" +
                               $"或该 key 未开通 Gemini API。来源：{VNAiKey.Source}", t0, onDone);
                        yield break;
                    }

                    if (www.responseCode == 429)
                    {
                        Finish(result, VNAiFailure.RateLimited,
                               "触发速率限制（429），重试后仍失败", t0, onDone);
                        yield break;
                    }

                    if (www.responseCode >= 500)
                    {
                        Finish(result, VNAiFailure.Server,
                               $"服务端错误（HTTP {www.responseCode}）", t0, onDone);
                        yield break;
                    }

                    if (www.responseCode != 200)
                    {
                        Finish(result, VNAiFailure.BadResponse,
                               $"HTTP {www.responseCode}：{Brief(payload)}", t0, onDone);
                        yield break;
                    }

                    ParseSuccess(payload, result, t0, onDone);
                    yield break;
                }
            }
        }

        // ── 响应解析 ───────────────────────────────────────────────

        static void ParseSuccess(string payload, VNAiResult result, float t0, Action<VNAiResult> onDone)
        {
            GeminiResponse resp;
            try { resp = JsonUtility.FromJson<GeminiResponse>(payload); }
            catch (Exception e)
            {
                Finish(result, VNAiFailure.BadResponse,
                       $"响应不是合法 JSON（{e.GetType().Name}）：{Brief(payload)}", t0, onDone);
                return;
            }

            if (resp == null)
            {
                Finish(result, VNAiFailure.BadResponse, $"响应为空：{Brief(payload)}", t0, onDone);
                return;
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
                Finish(result, VNAiFailure.Blocked,
                       $"请求被内容安全拦截（blockReason={resp.promptFeedback.blockReason}）", t0, onDone);
                return;
            }

            if (resp.candidates == null || resp.candidates.Length == 0)
            {
                Finish(result, VNAiFailure.BadResponse, $"响应没有 candidates：{Brief(payload)}", t0, onDone);
                return;
            }

            var cand = resp.candidates[0];
            result.finishReason = cand.finishReason;

            // 输出侧被拦：finishReason=SAFETY，此时 content.parts 是空的
            if (cand.finishReason == "SAFETY" || cand.finishReason == "PROHIBITED_CONTENT" ||
                cand.finishReason == "BLOCKLIST" || cand.finishReason == "RECITATION")
            {
                Finish(result, VNAiFailure.Blocked,
                       $"回复被内容安全拦截（finishReason={cand.finishReason}）", t0, onDone);
                return;
            }

            string text = null;
            if (cand.content != null && cand.content.parts != null)
                foreach (var p in cand.content.parts)
                    if (p != null && !string.IsNullOrEmpty(p.text)) { text = p.text; break; }

            if (string.IsNullOrEmpty(text))
            {
                Finish(result, VNAiFailure.BadResponse,
                       $"回复正文为空（finishReason={cand.finishReason}）", t0, onDone);
                return;
            }

            // MAX_TOKENS 时结构化 JSON 一定是残缺的，别让上层去解析半截 JSON
            if (cand.finishReason == "MAX_TOKENS")
            {
                result.text = text;
                Finish(result, VNAiFailure.Truncated,
                       "输出超长被截断（调大 maxOutputTokens，或在提示词里限制台词长度）", t0, onDone);
                return;
            }

            result.ok = true;
            result.text = text;
            result.failure = VNAiFailure.None;
            result.elapsedSeconds = Time.realtimeSinceStartup - t0;
            onDone?.Invoke(result);
        }

        static void Finish(VNAiResult r, VNAiFailure f, string msg, float t0, Action<VNAiResult> onDone)
        {
            r.ok = false;
            r.failure = f;
            r.errorMessage = msg;
            r.elapsedSeconds = Time.realtimeSinceStartup - t0;
            onDone?.Invoke(r);
        }

        /// <summary>报错时只带一小段响应，避免刷屏（也避免把长文本吐进 Console）</summary>
        static string Brief(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length <= 300 ? s : s.Substring(0, 300) + "…";
        }

        // ── 请求体拼装 ─────────────────────────────────────────────

        /// <summary>
        /// 手拼 JSON 而不是 JsonUtility：responseSchema 是动态生成的嵌套结构
        /// （enum 值要按角色的表情表现算），JsonUtility 既不支持 Dictionary
        /// 也没法原样内嵌一段 JSON 文本。所有外部文本一律走 Esc() 转义。
        /// </summary>
        internal static string BuildBody(VNAiRequest req)
        {
            var sb = new StringBuilder(1024);
            sb.Append('{');

            if (!string.IsNullOrEmpty(req.systemInstruction))
            {
                sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":");
                Esc(sb, req.systemInstruction);
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
                    Esc(sb, m.text ?? "");
                    sb.Append("}]}");
                }
            }
            sb.Append("],");

            sb.Append("\"generationConfig\":{");
            sb.Append("\"maxOutputTokens\":").Append(Mathf.Max(16, req.maxOutputTokens));
            sb.Append(",\"temperature\":").Append(req.temperature.ToString("0.##",
                System.Globalization.CultureInfo.InvariantCulture));
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

        /// <summary>
        /// JSON 字符串转义。中文不转 \u（UTF-8 直传即可，还省 token），
        /// 但控制字符必须转，否则拼出来的就是非法 JSON。
        /// </summary>
        internal static void Esc(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (!string.IsNullOrEmpty(s))
            {
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        case '\b': sb.Append("\\b"); break;
                        case '\f': sb.Append("\\f"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
            }
            sb.Append('"');
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
