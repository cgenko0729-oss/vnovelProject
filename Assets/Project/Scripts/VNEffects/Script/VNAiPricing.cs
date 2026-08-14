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
                 "  否则 lite 会被便宜价错算成贵价。排序由代码做，这里不用管顺序")]
        public string modelKey;

        [Tooltip("输入（prompt）每百万 token 美元")]
        public double inputPerMillion = 0.30;

        [Tooltip("输出每百万 token 美元。**思考 token 按这个价计费**，量还很大")]
        public double outputPerMillion = 2.50;
    }

    /// <summary>
    /// 模型单价表资产。Create → VN → AI Pricing，登记进 VNGameConfig.aiPricing。
    ///
    /// 【为什么要单独做成资产】
    ///   单价原本写死在 `VNAiResult.EstimatedCostUsd` 里（Flash Lite 的 0.30/2.50）。
    ///   人格资产的 `model` 字段一换成 flash 或 pro，**全部成本数字就静默偏低**，
    ///   而且看不出来。价格还会随供应商调整变动——那属于配置，不该躺在代码里。
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

        /// <summary>
        /// 内置默认表（2026-08 的公开价）。资产没配或查不到时用它。
        /// 顺序无所谓——查表时按 key 长度降序匹配。
        /// </summary>
        public static readonly VNAiModelPrice[] Builtin =
        {
            new VNAiModelPrice { modelKey = "flash-lite", inputPerMillion = 0.30, outputPerMillion = 2.50 },
            new VNAiModelPrice { modelKey = "flash",      inputPerMillion = 0.60, outputPerMillion = 3.50 },
            new VNAiModelPrice { modelKey = "pro",        inputPerMillion = 2.50, outputPerMillion = 15.00 },
        };
    }

    /// <summary>
    /// 按模型名算钱。全项目算成本只走这一个入口。
    ///
    /// 【查表规则】
    ///   模型名里**包含**某个 modelKey 即命中；多个命中时取 **key 最长的那个**——
    ///   「gemini-3.5-flash-lite」同时含 "flash" 和 "flash-lite"，
    ///   不按长度优先就会被便宜的 lite 错算成贵价 flash（反过来更糟）。
    ///
    /// 【缓存】
    ///   `VNGameConfig.Active` 每次都去查资产，而算钱在每轮请求后都要做一次。
    ///   这里缓存解析结果，`Invalidate()` 供编辑器改完资产后手动刷新。
    /// </summary>
    public static class VNAiPricing
    {
        static List<VNAiModelPrice> _table;
        static bool _resolved;

        /// <summary>改过单价资产后调一次，下次查表会重新读。</summary>
        public static void Invalidate() { _resolved = false; _table = null; }

        static List<VNAiModelPrice> Table
        {
            get
            {
                if (_resolved && _table != null) return _table;

                var list = new List<VNAiModelPrice>();
                var config = VNGameConfig.Active;
                if (config != null && config.aiPricing != null && config.aiPricing.prices != null)
                    foreach (var p in config.aiPricing.prices)
                        if (p != null && !string.IsNullOrWhiteSpace(p.modelKey)) list.Add(p);

                if (list.Count == 0) list.AddRange(VNAiPricingDef.Builtin);

                // 长 key 优先，见类注释
                list.Sort((a, b) => b.modelKey.Length.CompareTo(a.modelKey.Length));

                _table = list;
                _resolved = true;
                return _table;
            }
        }

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

        /// <summary>算一次请求的钱。思考 token 按输出价计费（量大，别漏）。</summary>
        public static double Cost(string model, int promptTokens, int outputTokens,
                                  int thoughtsTokens = 0)
        {
            var p = For(model, out _);
            return promptTokens * p.inputPerMillion / 1e6
                 + (outputTokens + thoughtsTokens) * p.outputPerMillion / 1e6;
        }

        /// <summary>模型名认不出来（成本数字只是猜的）。日志里要标出来。</summary>
        public static bool IsUnknownModel(string model)
        {
            For(model, out bool found);
            return !found;
        }

        /// <summary>给日志/报表显示用的一行，如「gemini-3.5-flash-lite $0.30/$2.50 每百万」</summary>
        public static string Describe(string model)
        {
            var p = For(model, out bool found);
            return $"{model} ${p.inputPerMillion:0.##}/${p.outputPerMillion:0.##} 每百万" +
                   (found ? "" : "（⚠ 单价表里没有这个模型，按最低档估算）");
        }
    }
}
