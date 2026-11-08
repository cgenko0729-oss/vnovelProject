using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public static partial class VNScenarioLinter
    {
        // ==============================================================
        // 任务系统
        // ==============================================================

        /// <summary>
        /// 任务检查。最要紧的一条是 quest-unknown-identifier：
        /// 条件表达式里的 flag 名拼错会**静默求值为 0**——任务永远不完成，
        /// 且运行时没有任何报错。这是整套系统最容易踩、最难查的 bug。
        ///
        /// 已知 flag 的来源刻意放宽（宁可漏报也不要喊狼来了）：剧本 flag/stat 写过的、
        /// 属性资产 id、道具_*、任务_*、统计派生名、事件模块 flag: 前缀派生的一切，
        /// 以及奖励自己会写出来的 flag。
        /// </summary>
        static void CheckQuests(List<ScriptFile> files, Registry reg, List<VNLintIssue> issues)
        {
            if (reg.questDefs.Count == 0) return;

            var defsById = new Dictionary<string, VNQuestDef>();
            foreach (var q in reg.questDefs) defsById[q.id] = q;

            // ---- 已知 flag 名与前缀 ----
            var knownFlags = new HashSet<string>(reg.statIds);
            var knownPrefixes = new HashSet<string>();
            foreach (var f in files)
                foreach (string n in f.writtenFlags) knownFlags.Add(n);
            foreach (string item in reg.shopItemIds)
                knownFlags.Add(VNShopDef.ItemFlagName(item));
            foreach (var q in reg.questDefs)
            {
                knownFlags.Add(VNQuestEngine.FlagName(q.id));
                knownPrefixes.Add(VNQuestEngine.FlagName(q.id));   // 任务_x@待领 等旁路 flag
                foreach (var r in AllRewards(q))
                    if (r.kind == VNQuestRewardKind.Flag && !string.IsNullOrEmpty(r.target))
                        knownFlags.Add(r.target);
            }
            foreach (var t in reg.trackers)
            {
                knownFlags.Add(t.sourceFlag);
                knownPrefixes.Add(t.sourceFlag);
            }
            knownFlags.Add(VNQuestEngine.MonthSerialFlag);
            knownFlags.Add(VNCalendarHud.MonthFlag);
            knownFlags.Add(VNCalendarHud.RemainFlag);
            knownFlags.Add("事件结果");
            knownPrefixes.Add("道具");
            knownPrefixes.Add("装备");
            knownPrefixes.Add("装备效果");
            knownPrefixes.Add("装备实增");
            knownPrefixes.Add("日程");

            // 事件模块的 flag: 参数派生出一整族成绩 flag（羽球_我方得分…），
            // 后缀由模块决定、静态看不到，所以按前缀整族放行
            bool anyTimeCommand = false;
            var scriptedStarts = new HashSet<string>();
            foreach (var f in files)
            {
                foreach (var c in f.cmds)
                {
                    if (c.keyword == "time") anyTimeCommand = true;
                    if (c.keyword == "event")
                    {
                        string prefix = c.Kw("flag");
                        if (!string.IsNullOrEmpty(prefix)) knownPrefixes.Add(prefix);
                    }
                    if (c.keyword != "quest") continue;

                    string op = c.Arg(0, "start");
                    string id = c.Arg(1);
                    if (string.IsNullOrEmpty(id)) continue;

                    if (op == "start" || op == "offer") scriptedStarts.Add(id);

                    if (!reg.questIds.Contains(id))
                    {
                        Add(issues, VNLintSeverity.Warning, "quest-unknown-id", f, c.line,
                            $"quest 引用了未登记的任务「{id}」",
                            "在 VNGameConfig 的任务库登记 VNQuestDef 资产。" +
                            "未登记也能跑（id 当标题），但没有阶段文案、条件与奖励。");
                        continue;
                    }

                    // 阶段号范围：100 与 -1 是完成/失败的保留值，可领取走旁路 flag
                    if (op == "stage")
                    {
                        int stage = (int)c.ArgF(2, 0f);
                        var def = defsById[id];
                        if (stage < 1 || stage > VNQuestEngine.MaxStage)
                        {
                            Add(issues, VNLintSeverity.Error, "quest-stage-range", f, c.line,
                                $"quest stage 的阶段号 {stage} 超出合法范围 " +
                                $"1..{VNQuestEngine.MaxStage}",
                                "100 是「已完成」、-1 是「已失败」的保留值。");
                        }
                        else if (def.StageCount > 0 && stage > def.StageCount)
                        {
                            Add(issues, VNLintSeverity.Warning, "quest-stage-range", f, c.line,
                                $"任务「{id}」只有 {def.StageCount} 个阶段，却要跳到阶段 {stage}",
                                "面板会显示不出阶段文案。给资产补阶段，或改阶段号。");
                        }
                    }
                }
            }

            // ---- 逐个任务资产查 ----
            var firstFile = files[0];
            foreach (var q in reg.questDefs)
            {
                string where = $"任务资产「{q.id}」";

                if (q.id.IndexOf(VNTracker.ReservedChar) >= 0)
                {
                    Add(issues, VNLintSeverity.Error, "quest-reserved-char", firstFile, 0,
                        $"{where} 的 id 含保留字符 '{VNTracker.ReservedChar}'",
                        "'@' 被统计层与任务旁路 flag 占用（任务_x@待领 / 羽球_得分@最高），" +
                        "任务 / 属性 / 道具 id 都不能含它。");
                }

                // 条件表达式：语法 + 标识符
                foreach (var pair in AllConditions(q))
                {
                    string expr = pair.Value;
                    if (string.IsNullOrEmpty(expr)) continue;

                    if (!VNExpression.TryValidate(expr, out string error))
                    {
                        Add(issues, VNLintSeverity.Error, "quest-condition-syntax", firstFile, 0,
                            $"{where} 的{pair.Key}条件「{expr}」语法错误：{error}",
                            "条件是 VNFlags 表达式，支持 ! 算术 比较 && || 与括号，中间不能有空格。");
                        continue;
                    }

                    var ids = new List<string>();
                    VNExpression.TryCollectIdentifiers(expr, ids, out _);
                    foreach (string name in ids)
                    {
                        if (IsKnownFlag(name, knownFlags, knownPrefixes)) continue;

                        // 用了 @后缀 但没声明统计源 —— 单独报，因为修法完全不同
                        int at = name.LastIndexOf(VNTracker.ReservedChar);
                        if (at > 0)
                        {
                            string src = name.Substring(0, at);
                            Add(issues, VNLintSeverity.Warning, "quest-tracker-missing",
                                firstFile, 0,
                                $"{where} 的条件用了统计值「{name}」，但没有声明统计源「{src}」",
                                "在 VNGameConfig 的「统计声明」里加一行：源 flag 填 " +
                                $"{src}，勾上对应的 最高/最低/累计/次数。");
                            continue;
                        }

                        Add(issues, VNLintSeverity.Warning, "quest-unknown-identifier",
                            firstFile, 0,
                            $"{where} 的条件引用了没人写过的 flag「{name}」",
                            "拼错的 flag 会静默求值为 0，任务永远不会完成且没有任何报错。" +
                            "确认名字，或先在剧本里 flag / stat 写过它。");
                    }
                }

                // 奖励目标
                foreach (var r in AllRewards(q))
                {
                    if (string.IsNullOrEmpty(r.target))
                    {
                        Add(issues, VNLintSeverity.Warning, "quest-reward-unknown-target",
                            firstFile, 0, $"{where} 有一条奖励没有填目标", "空目标的奖励会被静默跳过。");
                        continue;
                    }
                    switch (r.kind)
                    {
                        case VNQuestRewardKind.Stat when reg.statIds.Count > 0 &&
                                                         !reg.statIds.Contains(r.target):
                            Add(issues, VNLintSeverity.Warning, "quest-reward-unknown-target",
                                firstFile, 0,
                                $"{where} 的奖励发放属性「{r.target}」，但没有这个属性定义",
                                "没有 VNStatDef 的属性照样能加减，但不会钳制、也不上 HUD。");
                            break;
                        case VNQuestRewardKind.Item when reg.shopItemIds.Count > 0 &&
                                                         !reg.shopItemIds.Contains(r.target):
                            Add(issues, VNLintSeverity.Warning, "quest-reward-unknown-target",
                                firstFile, 0,
                                $"{where} 的奖励发放道具「{r.target}」，但没有商店登记过这个 id",
                                "非卖品也建议在某个 VNShopDef 里登记（可设为不可购买），" +
                                "否则背包拿不到它的名字、图标与说明。");
                            break;
                        case VNQuestRewardKind.Cg when reg.cgs.Count > 0 &&
                                                       !reg.cgs.Contains(r.target):
                            Add(issues, VNLintSeverity.Warning, "quest-reward-unknown-target",
                                firstFile, 0,
                                $"{where} 的奖励解锁 CG「{r.target}」，但 CG 库里没有这个 id", "");
                            break;
                        case VNQuestRewardKind.Quest when !reg.questIds.Contains(r.target):
                            Add(issues, VNLintSeverity.Warning, "quest-reward-unknown-target",
                                firstFile, 0,
                                $"{where} 的奖励要接取任务「{r.target}」，但没有这个任务定义", "");
                            break;
                    }
                }

                // 前置任务
                if (q.requires != null)
                {
                    foreach (string req in q.requires)
                    {
                        if (string.IsNullOrEmpty(req)) continue;
                        if (!reg.questIds.Contains(req))
                            Add(issues, VNLintSeverity.Warning, "quest-unknown-id", firstFile, 0,
                                $"{where} 的前置任务「{req}」不存在",
                                "前置任务永远不会完成 = 本任务永远接不了。");
                    }
                    if (HasRequireCycle(q, defsById))
                        Add(issues, VNLintSeverity.Error, "quest-requires-cycle", firstFile, 0,
                            $"{where} 的前置任务链成环",
                            "环上的任务互相等对方完成，全都永远接不了。");
                }

                // 接取出口：有条件却没人能接到它
                bool hasEntry = q.acceptAuto || q.acceptFromBoard || scriptedStarts.Contains(q.id);
                if (!hasEntry)
                {
                    foreach (var other in reg.questDefs)
                        foreach (var r in AllRewards(other))
                            if (r.kind == VNQuestRewardKind.Quest && r.target == q.id)
                                hasEntry = true;
                }
                if (!hasEntry && q.HasAnyCondition)
                {
                    Add(issues, VNLintSeverity.Warning, "quest-no-entry", firstFile, 0,
                        $"{where} 配了完成条件，却没有任何接取出口",
                        "没有剧本 quest start / 不上委托板 / 不自动接取 / 也没有别的任务把它当奖励，" +
                        "这个任务永远不会出现。");
                }

                // 限时但整个项目没有 time 命令 → 月序永远是 0，永不过期
                if (q.deadlineMonths > 0 && !anyTimeCommand)
                {
                    Add(issues, VNLintSeverity.Warning, "quest-deadline-no-time", firstFile, 0,
                        $"{where} 设了 {q.deadlineMonths} 个月期限，但剧本里没有任何 time 命令",
                        "期限基准是 time pass 维护的「月序」，没人推进它就永远不会过期。");
                }
            }
        }

        /// <summary>任务里所有条件表达式（附上「哪来的」用于报错定位）</summary>
        static List<KeyValuePair<string, string>> AllConditions(VNQuestDef q)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (!string.IsNullOrEmpty(q.unlockCondition))
                list.Add(new KeyValuePair<string, string>("出现", q.unlockCondition));
            if (q.stageDefs != null)
            {
                for (int i = 0; i < q.stageDefs.Count; i++)
                {
                    var s = q.stageDefs[i];
                    if (s == null || s.objectives == null) continue;
                    foreach (var o in s.objectives)
                    {
                        if (o == null || string.IsNullOrEmpty(o.condition)) continue;
                        list.Add(new KeyValuePair<string, string>($"阶段 {i + 1} 的", o.condition));
                    }
                }
            }
            return list;
        }

        static IEnumerable<VNQuestReward> AllRewards(VNQuestDef q)
        {
            if (q.stageDefs != null)
                foreach (var s in q.stageDefs)
                {
                    if (s == null || s.rewards == null) continue;
                    foreach (var r in s.rewards)
                        if (r != null) yield return r;
                }
            if (q.penalties != null)
                foreach (var r in q.penalties)
                    if (r != null) yield return r;
        }

        /// <summary>已知 flag：精确命中，或以某个已知前缀打头（前缀族整族放行）</summary>
        static bool IsKnownFlag(string name, HashSet<string> known, HashSet<string> prefixes)
        {
            if (known.Contains(name)) return true;

            // 剥掉统计后缀再试一次（羽球_我方得分@最高 → 羽球_我方得分）
            int at = name.LastIndexOf(VNTracker.ReservedChar);
            string bare = at > 0 ? name.Substring(0, at) : name;
            if (at > 0 && known.Contains(bare)) return true;

            foreach (string p in prefixes)
                if (bare.Length > p.Length && bare.StartsWith(p) &&
                    (bare[p.Length] == '_' || bare[p.Length] == VNTracker.ReservedChar))
                    return true;
            return false;
        }

        static bool HasRequireCycle(VNQuestDef start, Dictionary<string, VNQuestDef> byId)
        {
            var visiting = new HashSet<string>();
            return Walk(start);

            bool Walk(VNQuestDef q)
            {
                if (q == null || string.IsNullOrEmpty(q.id)) return false;
                if (!visiting.Add(q.id)) return true;
                if (q.requires != null)
                {
                    foreach (string req in q.requires)
                    {
                        if (string.IsNullOrEmpty(req)) continue;
                        if (byId.TryGetValue(req, out var next) && Walk(next)) return true;
                    }
                }
                visiting.Remove(q.id);
                return false;
            }
        }

        static void Add(List<VNLintIssue> issues, VNLintSeverity sev, string code,
            ScriptFile f, int line, string message, string hint)
        {
            issues.Add(new VNLintIssue
            {
                severity = sev,
                code = code,
                file = f.name,
                assetPath = f.assetPath,
                line = line,
                message = message,
                hint = hint,
            });
        }
    }
}
