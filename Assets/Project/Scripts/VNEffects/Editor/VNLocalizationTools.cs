using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 剧本翻译表抽取/校验工具（本地化 P2）。
    ///
    /// Extract：扫描 Assets/Scenarios/*.vn.txt，把台词（say）与选项（choice）按
    /// 出现顺序抽取成翻译表 Assets/Resources/VNLocale/Scenarios/&lt;剧本名&gt;.&lt;语言&gt;.txt。
    /// 增量合并：已填的译文按 key 保留；中文原文改动过的条目 key 变化，旧译文
    /// 挪到文件末尾的"孤儿条目"注释区供人工挪用。key 生成规则与运行时
    /// VNScriptLocale 共用同一实现，保证两边永远一致。
    ///
    /// Validate：统计每个剧本×语言的缺译数量，输出到 Console。
    ///
    /// 注意：event 结果行是逻辑标识符（结果匹配 / 去过_xx flag），不进翻译表。
    /// </summary>
    public static class VNLocalizationTools
    {
        const string ScenarioFolder = "Assets/Scenarios";
        const string OutputFolder = "Assets/Resources/VNLocale/Scenarios";
        static readonly string[] TargetLanguages = { "en", "ja" };

        struct Entry
        {
            public string key;
            public string context; // 说话者 / [选项]
            public string source;  // 中文原文
        }

        [MenuItem("Tools/VN Effects/Localization/Extract Script Translations")]
        public static void ExtractAll()
        {
            var files = FindScenarioFiles();
            if (files.Count == 0)
            {
                Debug.LogWarning($"[VNLocalization] {ScenarioFolder} 下没有 .vn.txt 剧本");
                return;
            }
            Directory.CreateDirectory(OutputFolder);

            var report = new StringBuilder("[VNLocalization] 抽取完成：\n");
            foreach (var file in files)
            {
                var entries = Collect(File.ReadAllText(file));
                string baseName = Path.GetFileNameWithoutExtension(file); // "Chapter1.vn"
                foreach (var lang in TargetLanguages)
                {
                    string outPath = Path.Combine(OutputFolder, $"{baseName}.{lang}.txt");
                    int untranslated, orphans;
                    MergeWrite(outPath, baseName, lang, entries, out untranslated, out orphans);
                    report.Append($"  {baseName}.{lang}：共 {entries.Count} 条，")
                          .Append($"待翻译 {untranslated} 条")
                          .Append(orphans > 0 ? $"，孤儿 {orphans} 条（原文已改动，见文件末尾）\n" : "\n");
                }
            }
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        [MenuItem("Tools/VN Effects/Localization/Validate Script Translations")]
        public static void ValidateAll()
        {
            var files = FindScenarioFiles();
            var report = new StringBuilder("[VNLocalization] 翻译覆盖检查：\n");
            bool anyMissing = false;
            foreach (var file in files)
            {
                var entries = Collect(File.ReadAllText(file));
                string baseName = Path.GetFileNameWithoutExtension(file);
                foreach (var lang in TargetLanguages)
                {
                    string tablePath = Path.Combine(OutputFolder, $"{baseName}.{lang}.txt");
                    if (!File.Exists(tablePath))
                    {
                        report.Append($"  ✗ {baseName}.{lang}：翻译表不存在（先 Extract）\n");
                        anyMissing = true;
                        continue;
                    }
                    var table = VNLocale.ParseTable(File.ReadAllText(tablePath));
                    int missing = 0;
                    foreach (var e in entries)
                        if (!table.TryGetValue(e.key, out var v) || string.IsNullOrEmpty(v))
                            missing++;
                    if (missing > 0) anyMissing = true;
                    report.Append(missing == 0
                        ? $"  ✓ {baseName}.{lang}：{entries.Count} 条全部翻译\n"
                        : $"  ✗ {baseName}.{lang}：{missing}/{entries.Count} 条缺译\n");
                }
            }
            if (anyMissing) Debug.LogWarning(report.ToString());
            else Debug.Log(report.ToString());
        }

        // ------------------------------------------------------------------

        static List<string> FindScenarioFiles()
        {
            var list = new List<string>();
            if (!Directory.Exists(ScenarioFolder)) return list;
            foreach (var f in Directory.GetFiles(ScenarioFolder, "*.vn.txt"))
                list.Add(f.Replace('\\', '/'));
            list.Sort();
            return list;
        }

        /// <summary>按与 VNScriptLocale.Apply 完全相同的遍历顺序收集可翻译条目</summary>
        static List<Entry> Collect(string source)
        {
            var entries = new List<Entry>();
            var occurrences = new Dictionary<string, int>();
            foreach (var cmd in VNScriptParser.Parse(source))
            {
                if (cmd.keyword == "say")
                {
                    entries.Add(new Entry
                    {
                        key = VNScriptLocale.NextKey(cmd.text, occurrences),
                        context = string.IsNullOrEmpty(cmd.speaker) ? "旁白" : cmd.speaker,
                        source = cmd.text,
                    });
                }
                else if (cmd.keyword == "choice" && cmd.options != null)
                {
                    foreach (var opt in cmd.options)
                        entries.Add(new Entry
                        {
                            key = VNScriptLocale.NextKey(opt.text, occurrences),
                            context = "选项",
                            source = opt.text,
                        });
                }
            }
            return entries;
        }

        static void MergeWrite(string outPath, string baseName, string lang,
            List<Entry> entries, out int untranslated, out int orphans)
        {
            var existing = File.Exists(outPath)
                ? VNLocale.ParseTable(File.ReadAllText(outPath))
                : new Dictionary<string, string>();

            var liveKeys = new HashSet<string>();
            untranslated = 0;

            var sb = new StringBuilder();
            sb.Append("# ==========================================================\n");
            sb.Append($"# {baseName} · {lang} 剧本翻译表\n");
            sb.Append("# 由 Tools → VN Effects → Localization → Extract 生成/增量合并\n");
            sb.Append("# 格式：key = 译文；译文留空 = 未翻译（运行时回退中文显示）\n");
            sb.Append("# 台词里的 {shake} {w:0.5} 等演出标记请在译文中原样保留\n");
            sb.Append("# 上方注释行是中文原文（每次抽取自动重写，改了也没用）\n");
            sb.Append("# ==========================================================\n");

            foreach (var e in entries)
            {
                liveKeys.Add(e.key);
                existing.TryGetValue(e.key, out var translated);
                if (string.IsNullOrEmpty(translated)) untranslated++;
                sb.Append('\n');
                sb.Append($"# [{e.context}] {e.source}\n");
                sb.Append($"{e.key} = {translated ?? ""}\n");
            }

            // 孤儿条目：原文改动/删除后失效的旧译文，保留为注释供人工挪用
            orphans = 0;
            var orphanSb = new StringBuilder();
            foreach (var kv in existing)
            {
                if (liveKeys.Contains(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                orphans++;
                orphanSb.Append($"# {kv.Key} = {kv.Value}\n");
            }
            if (orphans > 0)
            {
                sb.Append("\n# —— 孤儿条目（中文原文已改动或删除；确认后手动挪用或删除本区）——\n");
                sb.Append(orphanSb);
            }

            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
