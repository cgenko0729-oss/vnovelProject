using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// VNScriptLocale —— 剧本台词/选项的翻译查表（本地化 P2）。
    ///
    /// 架构：.vn.txt 剧本永远只写中文（唯一真相），翻译放在旁路表
    ///   Assets/Resources/VNLocale/Scenarios/&lt;剧本名&gt;.&lt;语言&gt;.txt（key = 译文）。
    /// 命令流对所有语言完全一致 → 存档（命令索引）、分支、调试重建全部跨语言通用。
    ///
    /// key = FNV-1a(原文) 8 位十六进制 + "-" + 同文出现序号（同一句中文出现多次时区分）。
    /// 中文原文没改动时，增删其他行不影响 key，已有译文自动保留。
    /// 表由编辑器工具生成/增量合并：Tools → VN Effects → Localization。
    ///
    /// 翻译范围：say 台词 + choice 选项（按索引匹配，翻译只影响显示）。
    /// event 结果行不翻译 —— 它们是逻辑标识符（结果名匹配、去过_&lt;地点&gt; flag）。
    /// 缺译回退中文并集中告警，不阻塞游戏。
    /// </summary>
    public static class VNScriptLocale
    {
        /// <summary>剧本翻译表在 Resources 下的目录</summary>
        public const string TableFolder = "VNLocale/Scenarios";

        /// <summary>
        /// 为整个命令列表标注当前语言的译文（Runner 在 LoadCommands 与语言切换时调用）。
        /// 遍历顺序 = 抽取工具的遍历顺序（命令顺序：say 取台词，choice 逐个选项），
        /// 两边必须保持一致，否则出现序号会错位。
        /// </summary>
        public static void Apply(List<VNScriptCommand> commands, string scriptName)
        {
            if (commands == null) return;

            // 先清空旧标注（语言切换回中文时即全部回原文）
            foreach (var cmd in commands)
            {
                cmd.localizedText = null;
                if (cmd.keyword == "choice" && cmd.options != null)
                    foreach (var opt in cmd.options) opt.localizedText = null;
            }
            if (VNLocale.Language == VNLanguage.Chinese) return;

            if (string.IsNullOrEmpty(scriptName))
            {
                Debug.LogWarning("[VNScriptLocale] 未知剧本名（直接用文本播放？），无法查翻译表，台词回退中文");
                return;
            }

            var table = LoadTable(scriptName, VNLocale.Code);
            if (table == null)
            {
                Debug.LogWarning($"[VNScriptLocale] 缺少翻译表 Resources/{TableFolder}/" +
                                 $"{scriptName}.{VNLocale.Code}.txt，台词回退中文" +
                                 "（用 Tools → VN Effects → Localization 生成）");
                return;
            }

            var occurrences = new Dictionary<string, int>();
            int missing = 0, total = 0;
            var prewarm = new StringBuilder();

            foreach (var cmd in commands)
            {
                if (cmd.keyword == "say")
                {
                    total++;
                    string t = Lookup(table, cmd.text, occurrences);
                    if (t != null) { cmd.localizedText = t; prewarm.Append(t); }
                    else missing++;
                }
                else if (cmd.keyword == "choice" && cmd.options != null)
                {
                    foreach (var opt in cmd.options)
                    {
                        total++;
                        string t = Lookup(table, opt.text, occurrences);
                        if (t != null) { opt.localizedText = t; prewarm.Append(t); }
                        else missing++;
                    }
                }
            }

            if (missing > 0)
                Debug.LogWarning($"[VNScriptLocale] {scriptName}.{VNLocale.Code}：" +
                                 $"{missing}/{total} 条缺译，显示时回退中文");

            // 译文全文预热进动态字体图集（同 LoadCommands 对中文全文的预热）
            VNFont.Prewarm(prewarm.ToString());
        }

        /// <summary>台词的显示文本（有译文用译文，否则原文）</summary>
        public static string TextOf(VNScriptCommand cmd) =>
            string.IsNullOrEmpty(cmd.localizedText) ? cmd.text : cmd.localizedText;

        /// <summary>选项的显示文本（有译文用译文，否则原文）</summary>
        public static string TextOf(VNChoiceOption opt) =>
            string.IsNullOrEmpty(opt.localizedText) ? opt.text : opt.localizedText;

        // ------------------------------------------------------------------
        // key 生成（抽取工具与运行时共用，保证两边一致）
        // ------------------------------------------------------------------

        /// <summary>取该原文的下一个 key 并推进出现序号（遍历时按序调用）</summary>
        public static string NextKey(string sourceText, Dictionary<string, int> occurrences)
        {
            string hash = Hash(sourceText);
            occurrences.TryGetValue(hash, out int n);
            occurrences[hash] = n + 1;
            return hash + "-" + n;
        }

        /// <summary>FNV-1a 32 位哈希（8 位十六进制）。取原文本身，不含说话者/表情。</summary>
        public static string Hash(string text)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in text)
                {
                    h ^= c;
                    h *= 16777619;
                }
                return h.ToString("x8");
            }
        }

        // ------------------------------------------------------------------

        static string Lookup(Dictionary<string, string> table,
            string sourceText, Dictionary<string, int> occurrences)
        {
            string key = NextKey(sourceText, occurrences);
            return table.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
                ? value : null;
        }

        static Dictionary<string, string> LoadTable(string scriptName, string code)
        {
            var asset = Resources.Load<TextAsset>($"{TableFolder}/{scriptName}.{code}");
            return asset != null ? VNLocale.ParseTable(asset.text) : null;
        }
    }
}
