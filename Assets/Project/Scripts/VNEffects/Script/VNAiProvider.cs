using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// AI 供应商。加新的一家 = 这里加一项 + 写一个 VNAiClient&lt;X&gt; 的拼包/解包静态类，
    /// 传输层（VNAiClient.Send）不用动。
    /// </summary>
    public enum VNAiProvider
    {
        [InspectorName("Google Gemini")] Gemini = 0,
        [InspectorName("DeepSeek")] DeepSeek = 1,
    }

    /// <summary>
    /// 人格资产上的供应商选择。比 <see cref="VNAiProvider"/> 多一个「跟随全局」——
    /// 默认值 0 就是跟随，所以存量资产不用改一个字就自动跟着全局设置走。
    /// </summary>
    public enum VNAiProviderChoice
    {
        [InspectorName("跟随全局默认（VNGameConfig）")] Inherit = 0,
        [InspectorName("Google Gemini")] Gemini = 1,
        [InspectorName("DeepSeek")] DeepSeek = 2,
    }

    /// <summary>
    /// 供应商的差异集中登记处：默认模型、key 的环境变量与文件名、能力差异。
    ///
    /// 【为什么要有这一层】
    ///   两家的差异不只是端点和鉴权头，还包括**能力**：
    ///   Gemini 有 responseSchema（enum / minItems 是硬约束，模型物理上编不出
    ///   不存在的表情名），DeepSeek 只有 `response_format:{"type":"json_object"}`，
    ///   格式全靠提示词约束 + 我们自己钳制。差异写在这里，让上层用
    ///   `SupportsResponseSchema(...)` 判断，而不是到处 `if (provider == Gemini)`。
    ///
    /// 【全局默认】
    ///   VNGameConfig.aiProvider / aiModel 是「一处改、全部人格跟着换」的开关，
    ///   人格资产里选了具体供应商才会覆盖它。查表结果缓存，改完资产调 Invalidate()。
    /// </summary>
    public static class VNAiProviders
    {
        public const string GeminiDefaultModel = "gemini-3.5-flash-lite";
        public const string DeepSeekDefaultModel = "deepseek-v4-flash";

        static bool _resolved;
        static VNAiProvider _provider;
        static string _model;

        /// <summary>改过 VNGameConfig 的 AI 设置后调一次。</summary>
        public static void Invalidate() { _resolved = false; _model = null; }

        static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _provider = VNAiProvider.DeepSeek;   // 没有 config 时的兜底
            _model = null;

            var config = VNGameConfig.Active;
            if (config == null) return;
            _provider = config.aiProvider;
            _model = string.IsNullOrWhiteSpace(config.aiModel) ? null : config.aiModel.Trim();
        }

        /// <summary>全局默认供应商（VNGameConfig.aiProvider）。</summary>
        public static VNAiProvider GlobalDefault { get { Resolve(); return _provider; } }

        /// <summary>全局默认模型名。config 里留空 = 该供应商的默认模型。</summary>
        public static string GlobalDefaultModel
        {
            get
            {
                Resolve();
                return string.IsNullOrEmpty(_model) ? DefaultModelFor(_provider) : _model;
            }
        }

        /// <summary>人格资产的选择 → 真正用哪家。</summary>
        public static VNAiProvider Resolve(VNAiProviderChoice choice)
        {
            switch (choice)
            {
                case VNAiProviderChoice.Gemini: return VNAiProvider.Gemini;
                case VNAiProviderChoice.DeepSeek: return VNAiProvider.DeepSeek;
                default: return GlobalDefault;
            }
        }

        public static string DefaultModelFor(VNAiProvider p) =>
            p == VNAiProvider.DeepSeek ? DeepSeekDefaultModel : GeminiDefaultModel;

        public static string DisplayName(VNAiProvider p) =>
            p == VNAiProvider.DeepSeek ? "DeepSeek" : "Gemini";

        /// <summary>这家的 key 环境变量名。</summary>
        public static string EnvVarFor(VNAiProvider p) =>
            p == VNAiProvider.DeepSeek ? "DEEPSEEK_API_KEY" : "GEMINI_API_KEY";

        /// <summary>这家的 key 文件名（放项目根或项目上级目录）。</summary>
        public static string KeyFileFor(VNAiProvider p) =>
            p == VNAiProvider.DeepSeek ? "DeepSeekAiApiKey.txt" : "GeminiAiApiKey.txt";

        /// <summary>
        /// 支持 responseSchema 这种**硬**结构化输出吗？
        /// false 的家要把 schema 翻译成提示词（见 VNAiConversation.BuildJsonFormatPrompt），
        /// 并且更依赖 TryParseTurn 里的降级/补齐/钳制。
        /// </summary>
        public static bool SupportsResponseSchema(VNAiProvider p) => p == VNAiProvider.Gemini;

        /// <summary>支持按类别放宽内容安全阈值吗？DeepSeek 没有这个参数。</summary>
        public static bool SupportsSafetySettings(VNAiProvider p) => p == VNAiProvider.Gemini;

        /// <summary>
        /// 从模型名反推供应商。给日志/报表用——历史日志里只存了模型名，
        /// 事后算钱要知道是哪家。认不出来时按全局默认算。
        /// </summary>
        public static VNAiProvider FromModelName(string model) =>
            TryFromModelName(model, out VNAiProvider p) ? p : GlobalDefault;

        /// <summary>
        /// 能从模型名**确定**是哪家吗？认不出来时返回 false ——
        /// 自建中转/私有部署的模型名可能两个关键字都不含，那种情况不该当成配置错误。
        /// </summary>
        public static bool TryFromModelName(string model, out VNAiProvider provider)
        {
            provider = GlobalDefault;
            if (string.IsNullOrWhiteSpace(model)) return false;
            string m = model.ToLowerInvariant();
            if (m.Contains("deepseek")) { provider = VNAiProvider.DeepSeek; return true; }
            if (m.Contains("gemini")) { provider = VNAiProvider.Gemini; return true; }
            return false;
        }
    }
}
