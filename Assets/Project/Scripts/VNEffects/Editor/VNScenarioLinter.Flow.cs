using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public static partial class VNScenarioLinter
    {
        // ==============================================================
        // 规则：跳转目标（跨文件）
        // ==============================================================

        /// <summary>
        /// 文件名 → 剧本。子目录里可能有同名剧本（如 Hina/第1章），运行时 FindChapter 是
        /// 先到先得，这里保持同样语义，不能用 ToDictionary（遇到重名会让整个 Lint 崩溃）。
        /// </summary>
        static Dictionary<string, ScriptFile> IndexByName(List<ScriptFile> files)
        {
            var byName = new Dictionary<string, ScriptFile>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
                if (!byName.ContainsKey(f.name)) byName.Add(f.name, f);
            return byName;
        }

        static void CheckDuplicateNames(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var seen = IndexByName(files);
            foreach (var f in files)
            {
                if (seen[f.name] == f) continue;
                issues.Add(new VNLintIssue
                {
                    severity = VNLintSeverity.Warning,
                    code = "duplicate-script-name",
                    file = f.name,
                    assetPath = f.assetPath,
                    line = 0,
                    message = $"剧本文件名与 {seen[f.name].assetPath} 重复，`{f.name}::标签` 只会解析到其中一个",
                    hint = "章节按文件名查找、不看目录，请给其中一个改名。",
                });
            }
        }

        static void CheckJumpTargets(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = IndexByName(files);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    switch (c.keyword)
                    {
                        case "jump":
                            Resolve(issues, byName, f, c.line, c.Arg(0), "jump");
                            break;
                        case "call":
                            Resolve(issues, byName, f, c.line, c.Arg(0), "call");
                            break;
                        case "if":
                            int at = LastJumpIndex(c);
                            if (at >= 0 && at + 1 < c.args.Count)
                                Resolve(issues, byName, f, c.line, c.args[at + 1], "if…jump");
                            break;
                        case "sns":
                            // sns reply 的 late:<标签> 是超时（已读不回）的去向
                            if (c.Arg(0) == "reply")
                                Resolve(issues, byName, f, c.line, c.Kw("late"), "sns reply late:");
                            break;
                        case "chapter":
                            string target = (c.Arg(0) ?? "").Replace(".vn.txt", "");
                            if (!string.IsNullOrEmpty(target) && !Dynamic(target) &&
                                !byName.ContainsKey(target))
                                Add(issues, VNLintSeverity.Error, "bad-chapter", f, c.line,
                                    $"chapter 目标文件「{c.Arg(0)}」不存在",
                                    $"剧本必须放在 {ScenariosDir} 下才会被登记。检查文件名拼写。");
                            break;
                    }

                    if (c.options != null)
                        foreach (var o in c.options)
                            if (!string.IsNullOrEmpty(o.jumpLabel))
                                Resolve(issues, byName, f, o.line, o.jumpLabel, "选项 ->");
                }
        }

        static void Resolve(List<VNLintIssue> issues, Dictionary<string, ScriptFile> byName,
            ScriptFile from, int line, string address, string what)
        {
            if (string.IsNullOrEmpty(address) || Dynamic(address)) return;

            if (!VNStoryAddress.TryParse(address, out string file, out string label, out string err))
            {
                Add(issues, VNLintSeverity.Error, "bad-address", from, line,
                    $"{what} 地址「{address}」无效：{err}", "格式是 `标签` 或 `文件::标签`。");
                return;
            }

            ScriptFile target = from;
            if (!string.IsNullOrEmpty(file))
            {
                string key = file.Replace(".vn.txt", "");
                if (!byName.TryGetValue(key, out target))
                {
                    Add(issues, VNLintSeverity.Error, "dangling-jump", from, line,
                        $"{what} 的目标文件「{file}」不存在",
                        $"剧本必须放在 {ScenariosDir} 下。检查文件名拼写。");
                    return;
                }
            }

            if (!target.labels.ContainsKey(label))
                Add(issues, VNLintSeverity.Error, "dangling-jump", from, line,
                    $"{what} 的目标 label「{label}」在 {target.name} 里不存在",
                    Suggest(label, target.labels.Keys));
        }

        // ==============================================================
        // 规则：call 契约（必填参数）
        // ==============================================================

        static void CheckCallContracts(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = IndexByName(files);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "call") continue;
                    string address = c.Arg(0);
                    if (string.IsNullOrEmpty(address) || Dynamic(address)) continue;
                    if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                        continue;

                    ScriptFile target = f;
                    if (!string.IsNullOrEmpty(file) &&
                        !byName.TryGetValue(file.Replace(".vn.txt", ""), out target)) continue;
                    if (!target.labels.TryGetValue(label, out int at)) continue;

                    // params 是目标 label 的下一条命令
                    int p = at + 1;
                    if (p >= target.cmds.Count || target.cmds[p].keyword != "params") continue;

                    var required = new List<string>();
                    foreach (string decl in target.cmds[p].args)
                        if (!decl.Contains("=")) required.Add(decl);

                    foreach (string need in required)
                        if (!c.kwargs.ContainsKey(need))
                            Add(issues, VNLintSeverity.Error, "call-missing-arg", f, c.line,
                                $"call {address} 缺少必填参数「{need}」",
                                $"目标声明的参数：{string.Join(" ", target.cmds[p].args)}。" +
                                "少传必填参数时本次 call 会报错并跳过子程序。");

                    foreach (var kv in c.kwargs)
                        if (!target.cmds[p].args.Any(a => a == kv.Key || a.StartsWith(kv.Key + "=")))
                            Add(issues, VNLintSeverity.Warning, "call-unknown-arg", f, c.line,
                                $"call {address} 传了未声明的参数「{kv.Key}」",
                                $"目标声明的参数：{string.Join(" ", target.cmds[p].args)}。" +
                                "多传的参数会被忽略（通常是拼写错误）。");
                }
        }

        // ==============================================================
        // 规则：子程序必须能 return
        // ==============================================================

        static void CheckSubroutineReturns(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = IndexByName(files);

            // 收集所有 call 目标（去重），只对它们做可达性分析
            var targets = new HashSet<(string file, string label)>();
            var origin = new Dictionary<(string, string), (ScriptFile f, int line)>();

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "call") continue;
                    string address = c.Arg(0);
                    if (string.IsNullOrEmpty(address) || Dynamic(address)) continue;
                    if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                        continue;
                    string fileName = string.IsNullOrEmpty(file) ? f.name : file.Replace(".vn.txt", "");
                    if (!byName.ContainsKey(fileName)) continue;
                    if (!byName[fileName].labels.ContainsKey(label)) continue;
                    var key = (fileName, label);
                    if (targets.Add(key)) origin[key] = (f, c.line);
                }

            foreach (var (fileName, label) in targets)
            {
                var target = byName[fileName];
                var visited = new HashSet<(string, int)>();
                string bad = null;

                if (ReachesEnd(byName, target, target.labels[label] + 1, visited, ref bad))
                {
                    var src = origin[(fileName, label)];
                    Add(issues, VNLintSeverity.Error, "missing-return", target,
                        target.cmds[target.labels[label]].line,
                        $"子程序「{label}」存在走不到 return 的路径（{bad}）",
                        $"它被 {src.f.name}:{src.line} 用 call 调用。" +
                        "带着调用栈跑到文件末尾会报错停机——每条分支末尾都要有 return。");
                }
            }
        }

        /// <summary>
        /// 从 index 开始沿控制流走，判断是否存在"走到文件末尾/chapter 却没遇到 return"的路径。
        /// return 是好的终点；chapter 会清空调用栈（子程序里用 = 回不去了）；EOF 直接报错。
        /// </summary>
        static bool ReachesEnd(Dictionary<string, ScriptFile> byName, ScriptFile f, int index,
            HashSet<(string, int)> visited, ref string reason)
        {
            while (true)
            {
                if (index >= f.cmds.Count)
                {
                    reason = $"{f.name} 文件末尾";
                    return true;
                }
                if (!visited.Add((f.name, index))) return false; // 走过了，此路无新结论

                var c = f.cmds[index];
                switch (c.keyword)
                {
                    case "return":
                        return false; // 正常终止

                    case "chapter":
                        reason = $"{f.name}:{c.line} 的 chapter（会清空调用栈）";
                        return true;

                    case "jump":
                    {
                        if (!Follow(byName, f, c.Arg(0), out var nf, out int ni)) return false;
                        f = nf; index = ni;
                        continue;
                    }

                    case "if":
                    {
                        int at = LastJumpIndex(c);
                        if (at >= 0 && at + 1 < c.args.Count &&
                            Follow(byName, f, c.args[at + 1], out var nf, out int ni) &&
                            ReachesEnd(byName, nf, ni, visited, ref reason)) return true;
                        index++;
                        continue;
                    }

                    case "choice":
                    case "event":
                    {
                        bool fallthrough = false;
                        if (c.options != null)
                            foreach (var o in c.options)
                            {
                                if (string.IsNullOrEmpty(o.jumpLabel)) { fallthrough = true; continue; }
                                if (Follow(byName, f, o.jumpLabel, out var nf, out int ni) &&
                                    ReachesEnd(byName, nf, ni, visited, ref reason)) return true;
                            }
                        if (c.options == null || c.options.Count == 0) fallthrough = true;
                        if (!fallthrough) return false;
                        index++;
                        continue;
                    }

                    default:
                        index++; // call 会回到下一行，其余命令顺序执行
                        continue;
                }
            }
        }

        static bool Follow(Dictionary<string, ScriptFile> byName, ScriptFile from, string address,
            out ScriptFile file, out int index)
        {
            file = null; index = 0;
            if (string.IsNullOrEmpty(address) || Dynamic(address)) return false;
            if (!VNStoryAddress.TryParse(address, out string fileName, out string label, out _))
                return false;
            var target = from;
            if (!string.IsNullOrEmpty(fileName) &&
                !byName.TryGetValue(fileName.Replace(".vn.txt", ""), out target)) return false;
            if (!target.labels.TryGetValue(label, out int at)) return false;
            file = target; index = at + 1;
            return true;
        }

        // ==============================================================
        // 规则：跨文件回跳的死循环守卫
        // ==============================================================

        /// <summary>
        /// 典型事故：主循环里 `if 月份==6 jump 第2章::序`，第2章演完 `jump 主循环::月初`，
        /// 于是条件再次成立 → 无限循环。
        /// 判据：条件里引用的 flag，**没有任何一个**在目标文件里被写过 → 大概率缺守卫。
        /// </summary>
        static void CheckLoopGuards(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = IndexByName(files);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "if") continue;
                    int at = LastJumpIndex(c);
                    if (at < 0 || at + 1 >= c.args.Count) continue;

                    string address = c.args[at + 1];
                    if (Dynamic(address) || !address.Contains("::")) continue; // 只查跨文件
                    if (!VNStoryAddress.TryParse(address, out string fileName, out string label, out _))
                        continue;
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (!byName.TryGetValue(fileName.Replace(".vn.txt", ""), out var target)) continue;

                    // 目标文件是否会跳回本文件？不跳回就没有循环风险
                    bool jumpsBack = target.cmds.Any(tc =>
                        (tc.keyword == "jump" || tc.keyword == "chapter") &&
                        (tc.Arg(0) ?? "").Replace(".vn.txt", "")
                            .StartsWith(f.name, System.StringComparison.OrdinalIgnoreCase));
                    if (!jumpsBack) continue;

                    string cond = string.Join(" ", c.args.Take(at));
                    var names = FlagNames(cond);
                    if (names.Count == 0) continue;
                    if (names.Any(n => target.writtenFlags.Contains(n))) continue; // 有守卫

                    Add(issues, VNLintSeverity.Warning, "loop-risk", f, c.line,
                        $"跳到「{target.name}」后可能死循环（条件里没有该章节会设置的守卫 flag）",
                        $"{target.name} 会跳回 {f.name}，而条件「{cond}」引用的 flag " +
                        $"（{string.Join("、", names)}）在 {target.name} 里从未被写入，" +
                        "所以回来后条件仍然成立。" +
                        $"惯例：在 {target.name} 的开头写 `flag {target.name}已看 1`，" +
                        $"并在条件里加 `&& !{target.name}已看`。");
                }
        }

        static List<string> FlagNames(string condition)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(condition)) return result;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         condition, @"[^\s!()&|<>=+\-*/%]+"))
            {
                string t = m.Value.Trim();
                if (t.Length == 0 || int.TryParse(t, out _)) continue;
                if (!result.Contains(t)) result.Add(t);
            }
            return result;
        }

        // ==============================================================
        // 规则：从未被引用的 label
        // ==============================================================

        static void CheckUnreferencedLabels(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var referenced = new HashSet<string>(); // "文件::label"（小写）

            void Mark(ScriptFile from, string address)
            {
                if (string.IsNullOrEmpty(address) || Dynamic(address)) return;
                if (!VNStoryAddress.TryParse(address, out string file, out string label, out _)) return;
                string f = string.IsNullOrEmpty(file) ? from.name : file.Replace(".vn.txt", "");
                referenced.Add((f + "::" + label).ToLowerInvariant());
            }

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword == "jump" || c.keyword == "call") Mark(f, c.Arg(0));
                    else if (c.keyword == "if")
                    {
                        int at = LastJumpIndex(c);
                        if (at >= 0 && at + 1 < c.args.Count) Mark(f, c.args[at + 1]);
                    }
                    if (c.options != null)
                        foreach (var o in c.options) Mark(f, o.jumpLabel);
                }

            foreach (var f in files)
                foreach (var kv in f.labels)
                {
                    if (referenced.Contains((f.name + "::" + kv.Key).ToLowerInvariant())) continue;
                    // 文件第一条命令之前的 label = 章节入口，靠 chapter 从头进入，不算孤立
                    if (kv.Value <= 1) continue;
                    Add(issues, VNLintSeverity.Info, "unreferenced-label", f,
                        f.cmds[kv.Value].line,
                        $"label「{kv.Key}」从未被跳转引用",
                        "可能是拼写不一致，或是写完忘了接线；确认无用可以删掉。");
                }
        }
    }
}
