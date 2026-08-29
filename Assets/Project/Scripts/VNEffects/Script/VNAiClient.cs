using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VNEffects
{
    /// <summary>
    /// 对话历史里的一条消息。
    /// 角色名各家不同（Gemini 是 user/model，DeepSeek 是 user/assistant），
    /// 所以这里只存「谁说的」，具体名字由各家的拼包代码决定。
    /// </summary>
    public struct VNAiMessage
    {
        public bool fromPlayer;   // true = 玩家（user），false = 角色（model / assistant）
        public string text;

        public static VNAiMessage Player(string t) => new VNAiMessage { fromPlayer = true, text = t };
        public static VNAiMessage Model(string t) => new VNAiMessage { fromPlayer = false, text = t };
    }

    /// <summary>
    /// 内容安全阈值。Gemini 允许按类别放宽——恋爱向的暧昧对话默认阈值容易被误挡，
    /// 所以本项目默认用 BlockOnlyHigh。注意这**不是**关掉审核，只是把线抬高。
    /// **DeepSeek 没有这个参数**，选什么都一样（它的审核不可调）。
    /// </summary>
    public enum VNAiSafety
    {
        [InspectorName("默认（Google 默认阈值，最严）")] Default = 0,
        [InspectorName("仅拦截高危（推荐，恋爱向对话不易误挡；DeepSeek 无此参数）")] BlockOnlyHigh = 1,
    }

    /// <summary>
    /// 思考档位。实测 gemini-3.5-flash-lite：不写和 minimal 都是 0 思考 token、约 0.8s；
    /// low/medium/high 约多 100+ token、约 1.2s。聊天用 Minimal 即可。
    /// （Gemini 没有 "off" 和 "dynamic"，填了会 400。）
    ///
    /// DeepSeek 只有三档 reasoning_effort（low/high/max），映射见
    /// VNAiClientDeepSeek.ThinkingJson：Minimal→关闭思考、Low→low、Medium→high、High→max。
    /// **思考 token 按输出价计费**，聊天场景一律建议 Minimal。
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
        /// <summary>发给哪一家。默认跟随 VNGameConfig 的全局设置。</summary>
        public VNAiProvider provider = VNAiProviders.GlobalDefault;

        public string model = VNAiClient.DefaultModel;
        public string systemInstruction;
        public List<VNAiMessage> history = new List<VNAiMessage>();

        /// <summary>
        /// 结构化输出的 JSON Schema（原样内嵌的 JSON 文本）。
        /// 留空 = 纯文本模式（连通性自检用）。
        ///
        /// ★ 只有支持硬 schema 的家（Gemini）会真的把它发出去；
        ///   DeepSeek 只认 `response_format:{"type":"json_object"}`，
        ///   格式约束由 VNAiConversation 翻译进 systemInstruction。
        ///   两家都靠这个字段非空来判断「这是结构化请求」。
        /// </summary>
        public string responseSchemaJson;

        public VNAiThinking thinking = VNAiThinking.Minimal;
        public VNAiSafety safety = VNAiSafety.BlockOnlyHigh;
        public int maxOutputTokens = 1024;
        public float temperature = 1f;

        public int timeoutSeconds = 30;
        public int maxRetries = 2;     // 只对 429 / 5xx / 网络错误重试

        /// <summary>模型名留空时取这一家的默认模型。</summary>
        public string ResolveModel() =>
            string.IsNullOrWhiteSpace(model) ? VNAiProviders.DefaultModelFor(provider) : model.Trim();
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
        public string finishReason;     // STOP / MAX_TOKENS / SAFETY / length / content_filter ...
        public int promptTokens, outputTokens, thoughtsTokens, totalTokens;

        /// <summary>
        /// 输入 token 里**命中提示缓存**的部分（DeepSeek 的 prompt_cache_hit_tokens）。
        /// 已包含在 promptTokens 里，单价却便宜 30 倍，算钱要拆开。
        /// Gemini 这边恒为 0（它的隐式缓存不在响应里给这个数）。
        /// </summary>
        public int cachedPromptTokens;

        public long httpCode;
        public float elapsedSeconds;

        /// <summary>这次用的是哪个模型。**算钱要靠它**，由 Send 回填。</summary>
        public string model;

        /// <summary>这次发给了哪一家。日志与报表用，由 Send 回填。</summary>
        public VNAiProvider provider;

        /// <summary>
        /// 估算本次花费（美元）。单价按 model 查 VNAiPricing 表——
        /// 曾经这里写死 Flash Lite 的 0.30/2.50，换个模型全部数字就静默偏低。
        /// 思考 token 按输出价计费（thinking 开到 High 时它是大头）；
        /// 缓存命中的输入 token 走便宜价；高峰时段自动乘倍率。
        /// </summary>
        public double EstimatedCostUsd =>
            VNAiPricing.Cost(model, promptTokens, outputTokens, thoughtsTokens, cachedPromptTokens);
    }

    /// <summary>
    /// AI 请求的协程封装。
    ///
    /// ★ 全项目唯一碰 HTTP 的文件 ★
    ///   传输、重试、错误分类都在这里；**各家的差异只有「拼请求体」和「解响应」两件事**，
    ///   拆在 VNAiClientGemini / VNAiClientDeepSeek（纯静态、不碰网络）。
    ///   换模型 / 加供应商 / 改成走自建中转服务器，上层（VNAiConversation /
    ///   VNAiTalkModule）一行都不用动。
    ///
    /// 用协程而不是 async/await：与项目现有 IEnumerator 风格一致，
    /// 且 Runner 的 EventCo 本来就是「while(result==null) yield return null」轮询，
    /// 天然容得下一个要等 1~2 秒的网络请求，不阻塞主线程。
    ///
    /// 实测契约见两个 provider 文件的类注释（各自记着自己那家的坑）。
    /// </summary>
    public static class VNAiClient
    {
        /// <summary>兼容旧代码：默认模型 = 全局默认供应商的默认模型。</summary>
        public static string DefaultModel => VNAiProviders.GlobalDefaultModel;

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

            // 算钱按模型查单价、报表按供应商分组，所以两者都要跟着结果一路带回去
            VNAiProvider provider = req.provider;
            string model = req.ResolveModel();
            result.provider = provider;
            result.model = model;

            if (!VNAiKey.TryGet(provider, out string key, out string _))
            {
                Finish(result, VNAiFailure.NoKey, VNAiKey.MissingKeyMessage(provider), t0, onDone);
                yield break;
            }

            string url = provider == VNAiProvider.DeepSeek
                ? VNAiClientDeepSeek.Endpoint(model)
                : VNAiClientGemini.Endpoint(model);
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
                    // ← key 只出现在这一行（各家鉴权头不同）
                    if (provider == VNAiProvider.DeepSeek)
                        www.SetRequestHeader("Authorization", "Bearer " + key);
                    else
                        www.SetRequestHeader("x-goog-api-key", key);
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
                        string hint = provider == VNAiProvider.DeepSeek
                            ? "检查网络能否访问 api.deepseek.com"
                            : "检查网络代理是否能访问 Google";
                        Finish(result, VNAiFailure.Network,
                               $"网络错误：{www.error}（{hint}）", t0, onDone);
                        yield break;
                    }

                    if (www.responseCode == 401 || www.responseCode == 403)
                    {
                        Finish(result, VNAiFailure.Auth,
                               $"鉴权失败（HTTP {www.responseCode}）：key 无效、已吊销、" +
                               $"余额不足，或该 key 未开通 {VNAiProviders.DisplayName(provider)} API。" +
                               $"来源：{VNAiKey.SourceFor(provider)}", t0, onDone);
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

                    string error;
                    VNAiFailure failure = provider == VNAiProvider.DeepSeek
                        ? VNAiClientDeepSeek.Parse(payload, result, out error)
                        : VNAiClientGemini.Parse(payload, result, out error);

                    if (failure != VNAiFailure.None)
                    {
                        Finish(result, failure, error, t0, onDone);
                        yield break;
                    }

                    result.ok = true;
                    result.failure = VNAiFailure.None;
                    result.elapsedSeconds = Time.realtimeSinceStartup - t0;
                    onDone?.Invoke(result);
                    yield break;
                }
            }
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
        internal static string Brief(string s)
        {
            if (string.IsNullOrEmpty(s)) return "(空)";
            s = s.Replace('\n', ' ').Replace('\r', ' ');
            return s.Length <= 300 ? s : s.Substring(0, 300) + "…";
        }

        // ── 请求体拼装（按家分派）─────────────────────────────────

        /// <summary>
        /// 手拼 JSON 而不是 JsonUtility：responseSchema 是动态生成的嵌套结构
        /// （enum 值要按角色的表情表现算），JsonUtility 既不支持 Dictionary
        /// 也没法原样内嵌一段 JSON 文本。所有外部文本一律走 Esc() 转义。
        /// </summary>
        internal static string BuildBody(VNAiRequest req) =>
            req.provider == VNAiProvider.DeepSeek
                ? VNAiClientDeepSeek.BuildBody(req)
                : VNAiClientGemini.BuildBody(req);

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

        /// <summary>float → 不受地区影响的 JSON 数字（德语区的逗号小数点会拼出非法 JSON）</summary>
        internal static string Num(float v) =>
            v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
