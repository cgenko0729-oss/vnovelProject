using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 一个模型的每百万 token 单价。
    /// </summary>
    [Serializable]
    public class VNAiModelPrice
    {
        [Tooltip("匹配模型名的关键字（子串包含即算命中）。\n" +
                 "★ 越长越优先——「gemini-3.5-flash-lite」必须比「gemini-3.5-flash」先匹配上，\n" +
                 "  「deepseek-v4-flash」也必须比 Gemini 的「flash」先匹配上，\n" +
                 "  否则会被隔壁家的价错算。排序由代码做，这里不用管顺序")]
        public string modelKey;

        [Tooltip("输入（prompt）每百万 token 美元。**未命中缓存**的价")]
        public double inputPerMillion = 0.30;

        [Tooltip("输出每百万 token 美元。**思考 token 按这个价计费**，量还很大")]
        public double outputPerMillion = 2.50;

        [Tooltip("命中提示缓存的输入每百万 token 美元。\n" +
                 "0 = 不区分（按上面的输入价算）。DeepSeek 的缓存命中价便宜约 30 倍，\n" +
                 "而我们每轮都重发整段 system prompt + 历史，命中率很高，不填会严重高估")]
        public double cachedInputPerMillion = 0;

        [Tooltip("高峰时段的价格倍率。1 = 全天同价（Gemini）；\n" +
                 "DeepSeek 填 2 —— 它的标价是非高峰价，高峰时段翻倍")]
        public double peakMultiplier = 1;
    }

    /// <summary>UTC 高峰时段（左闭右开）。跨零点（start &gt; end）也支持。</summary>
    [Serializable]
    public class VNAiPeakWindow
    {
        [Range(0, 24)] public int startUtcHour;
        [Range(0, 24)] public int endUtcHour;
    }

    /// <summary>
    /// 模型单价表资产。Create → VN → AI Pricing，登记进 VNGameConfig.aiPricing。
    ///
    /// 【为什么要单独做成资产】
    ///   单价原本写死在 `VNAiResult.EstimatedCostUsd` 里（Flash Lite 的 0.30/2.50）。
    ///   人格资产的 `model` 字段一换成 flash 或 pro，**全部成本数字就静默偏低**，
    ///   而且看不出来。价格还会随供应商调整变动——那属于配置，不该躺在代码里。
    ///   DeepSeek 的高峰时段也一样：官方随时可能改时段，所以时段也放进资产。
    ///
    /// 【不建资产也能用】
    ///   `Builtin` 是一份内置默认表。没有资产、或资产里查不到这个模型时都用它兜底，
    ///   查不到时会在日志里标注「单价存疑」，免得你对着一个瞎猜出来的数字做决策。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/AI Pricing", fileName = "AiPricing")]
    public class VNAiPricingDef : ScriptableObject
    {
        [Header("每百万 token 的美元单价。改价直接改这里，不用动代码")]
        public List<VNAiModelPrice> prices = new List<VNAiModelPrice>(Builtin);

        [Header("高峰时段（UTC，左闭右开）。只对 peakMultiplier > 1 的模型生效\n" +
                "留空 = 用内置默认（DeepSeek 2026-08 的时段：01-04 与 06-10 UTC）")]
        public List<VNAiPeakWindow> peakWindowsUtc = new List<VNAiPeakWindow>(BuiltinPeakWindows);

        /// <summary>
        /// 内置默认表（2026-08 的公开价）。资产没配或查不到时用它。
        /// 顺序无所谓——查表时按 key 长度降序匹配。
        ///
        /// DeepSeek 标的是**非高峰**价，高峰时段翻倍（peakMultiplier = 2）。
        /// </summary>
        public static readonly VNAiModelPrice[] Builtin =
        {
            // Google Gemini（全天同价，响应里拿不到缓存命中数）
            new VNAiModelPrice { modelKey = "flash-lite", inputPerMillion = 0.30, outputPerMillion = 2.50 },
            new VNAiModelPrice { modelKey = "flash",      inputPerMillion = 0.60, outputPerMillion = 3.50 },
            new VNAiModelPrice { modelKey = "pro",        inputPerMillion = 2.50, outputPerMillion = 15.00 },

            // DeepSeek（非高峰价；key 比上面的 flash / pro 长，所以不会被抢走）
            new VNAiModelPrice { modelKey = "deepseek-v4-flash", inputPerMillion = 0.22,
                                 outputPerMillion = 0.66, cachedInputPerMillion = 0.007, peakMultiplier = 2 },
            new VNAiModelPrice { modelKey = "deepseek-v4-pro",   inputPerMillion = 0.66,
                                 outputPerMillion = 1.98, cachedInputPerMillion = 0.022, peakMultiplier = 2 },
        };

        /// <summary>DeepSeek 2026-08 的高峰时段：01:00-04:00 与 06:00-10:00 UTC。</summary>
        public static readonly VNAiPeakWindow[] BuiltinPeakWindows =
        {
            new VNAiPeakWindow { startUtcHour = 1, endUtcHour = 4 },
            new VNAiPeakWindow { startUtcHour = 6, endUtcHour = 10 },
        };
    }

    /// <summary>
    /// 按模型名算钱。全项目算成本只走这一个入口。
    ///
    /// 【查表规则】
    ///   模型名里**包含**某个 modelKey 即命中；多个命中时取 **key 最长的那个**——
    ///   「gemini-3.5-flash-lite」同时含 "flash" 和 "flash-lite"，
    ///   「deepseek-v4-flash」也含 "flash"，
    ///   不按长度优先就会被隔壁家的价错算。
    ///
    /// 【三个价，别只算一个】
    ///   输入（未命中缓存）/ 输入（命中缓存，DeepSeek 便宜 30 倍）/ 输出（思考 token 同价）。
    ///   再乘一个时段倍率——DeepSeek 高峰时段整体翻倍。
    ///
    /// 【缓存】
    ///   `VNGameConfig.Active` 每次都去查资产，而算钱在每轮请求后都要做一次。
    ///   这里缓存解析结果，`Invalidate()` 供编辑器改完资产后手动刷新。
    /// </summary>
    public static class VNAiPricing
    {
        static List<VNAiModelPrice> _table;
        static List<VNAiPeakWindow> _windows;
        static bool _resolved;

        /// <summary>改过单价资产后调一次，下次查表会重新读。</summary>
        public static void Invalidate() { _resolved = false; _table = null; _windows = null; }

        static void Resolve()
        {
            if (_resolved && _table != null) return;

            var list = new List<VNAiModelPrice>();
            var windows = new List<VNAiPeakWindow>();

            var config = VNGameConfig.Active;
            if (config != null && config.aiPricing != null)
            {
                if (config.aiPricing.prices != null)
                    foreach (var p in config.aiPricing.prices)
                        if (p != null && !string.IsNullOrWhiteSpace(p.modelKey)) list.Add(p);
                if (config.aiPricing.peakWindowsUtc != null)
                    foreach (var w in config.aiPricing.peakWindowsUtc)
                        if (w != null && w.startUtcHour != w.endUtcHour) windows.Add(w);
            }

            if (list.Count == 0) list.AddRange(VNAiPricingDef.Builtin);
            if (windows.Count == 0) windows.AddRange(VNAiPricingDef.BuiltinPeakWindows);

            // 长 key 优先，见类注释
            list.Sort((a, b) => b.modelKey.Length.CompareTo(a.modelKey.Length));

            _table = list;
            _windows = windows;
            _resolved = true;
        }

        static List<VNAiModelPrice> Table { get { Resolve(); return _table; } }

        /// <summary>查这个模型的单价。found=false 表示表里没有，返回的是兜底价。</summary>
        public static VNAiModelPrice For(string model, out bool found)
        {
            found = false;
            if (!string.IsNullOrWhiteSpace(model))
            {
                string m = model.Trim().ToLowerInvariant();
                foreach (var p in Table)
                    if (m.Contains(p.modelKey.Trim().ToLowerInvariant()))
                    {
                        found = true;
                        return p;
                    }
            }
            // 认不出来的模型：取表里**最贵**的一档，并让调用方知道这是猜的。
            // 方向是有讲究的——低估会让人以为「才这么点钱」然后放心用下去，
            // 高估最多让人多留意一眼。成本估算宁可保守。
            var worst = VNAiPricingDef.Builtin[0];
            foreach (var p in Table)
                if (p.outputPerMillion > worst.outputPerMillion) worst = p;
            return worst;
        }

        /// <summary>现在（UTC）是不是高峰时段。</summary>
        public static bool IsPeakNow() => IsPeak(DateTime.UtcNow);

        /// <summary>某个 UTC 时刻是不是高峰时段。</summary>
        public static bool IsPeak(DateTime utc)
        {
            Resolve();
            int h = utc.Hour;
            foreach (var w in _windows)
            {
                if (w.startUtcHour <= w.endUtcHour)
                {
                    if (h >= w.startUtcHour && h < w.endUtcHour) return true;
                }
                else // 跨零点，如 22 → 3
                {
                    if (h >= w.startUtcHour || h < w.endUtcHour) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 算一次请求的钱。
        /// </summary>
        /// <param name="promptTokens">输入 token 总数（**含**命中缓存的部分）</param>
        /// <param name="outputTokens">输出 token（不含思考）</param>
        /// <param name="thoughtsTokens">思考 token，按输出价计费（量大，别漏）</param>
        /// <param name="cachedPromptTokens">其中命中提示缓存的输入 token，走便宜价</param>
        /// <param name="atUtc">按哪个 UTC 时刻判高峰。null = 现在（事后重算历史日志时传当时的时间）</param>
        public static double Cost(string model, int promptTokens, int outputTokens,
                                  int thoughtsTokens = 0, int cachedPromptTokens = 0,
                                  DateTime? atUtc = null)
        {
            var p = For(model, out _);

            int cached = Mathf.Clamp(cachedPromptTokens, 0, Mathf.Max(0, promptTokens));
            int uncached = Mathf.Max(0, promptTokens - cached);
            double cachedRate = p.cachedInputPerMillion > 0 ? p.cachedInputPerMillion : p.inputPerMillion;

            double usd = uncached * p.inputPerMillion / 1e6
                       + cached * cachedRate / 1e6
                       + (outputTokens + thoughtsTokens) * p.outputPerMillion / 1e6;

            double mul = p.peakMultiplier > 0 ? p.peakMultiplier : 1;
            if (mul != 1 && IsPeak(atUtc ?? DateTime.UtcNow)) usd *= mul;
            return usd;
        }

        /// <summary>模型名认不出来（成本数字只是猜的）。日志里要标出来。</summary>
        public static bool IsUnknownModel(string model)
        {
            For(model, out bool found);
            return !found;
        }

        /// <summary>
        /// 给日志/报表显示用的一行，如
        /// 「deepseek-v4-flash $0.22/$0.66 每百万（缓存命中 $0.007；当前非高峰）」
        /// </summary>
        public static string Describe(string model)
        {
            var p = For(model, out bool found);
            string s = $"{model} ${p.inputPerMillion:0.###}/${p.outputPerMillion:0.###} 每百万";
            if (p.cachedInputPerMillion > 0)
                s += $"（缓存命中 ${p.cachedInputPerMillion:0.###}）";
            if (p.peakMultiplier > 1)
                s += IsPeakNow()
                    ? $"　⚠ 当前是高峰时段，实际 ×{p.peakMultiplier:0.##}"
                    : "　当前非高峰";
            if (!found) s += "（⚠ 单价表里没有这个模型，按最贵档估算）";
            return s;
        }
    }
}
