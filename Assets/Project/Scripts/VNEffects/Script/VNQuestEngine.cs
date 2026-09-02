using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 任务引擎：条件求值、状态推进、领取发奖、超期判定、日常冷却重置。
    /// 纯逻辑（不建 UI、不碰舞台），面板与剧本命令都调它。
    ///
    /// 状态全部落在 flag 上，所以存档、if 分支、调试重建零改动复用，
    /// VNSaveData 一个字段都不用加：
    ///   任务_&lt;id&gt;          阶段号 0 未接取 / 1..98 进行中 / 100 完成 / -1 失败
    ///   任务_&lt;id&gt;@待领     待领取的阶段号（0 = 无）。面板金色高亮看的就是它
    ///   任务_&lt;id&gt;@接取月   接取那一刻的「月序」，算超期用
    ///   任务_&lt;id&gt;@完成数   历史完成次数（maxTimes 上限看它）
    ///   任务_&lt;id&gt;@重置月   下次可再接的「月序」，日常冷却用
    ///
    /// 「可领取」刻意不写进主 flag（比如塞个 99）：主 flag 的语义是阶段号，
    /// 塞了会让剧本里的 if 任务_xx==2 在可领取期间失效，多阶段还会丢失
    /// 「是哪个阶段可领」的信息，而且 quest start &lt;id&gt; N 的阶段号本来就没上限。
    /// </summary>
    public static class VNQuestEngine
    {
        public const int StageDone = 100;
        public const int StageFailed = -1;
        public const int MaxStage = 98;      // 阶段号合法上限（Lint 卡这个）

        public const string FlagPrefix = "任务_";
        public const string SuffixPending = "@待领";
        public const string SuffixAcceptedMonth = "@接取月";
        public const string SuffixTimes = "@完成数";
        public const string SuffixResetMonth = "@重置月";

        /// <summary>
        /// 单调递增的绝对月计数，限时与冷却的唯一时间基准。
        /// 不能用日历「月份」——time pass 让它在 1~12 里循环，
        /// 11 月接的 3 个月期限任务，到期该是次年 2 月，而「月份>=14」永远不成立。
        /// 也不用「剩余月数」：那个只有养成模式才会被 time set … remain: 设上。
        /// </summary>
        public const string MonthSerialFlag = "月序";

        static readonly List<VNQuestDef> _defs = new List<VNQuestDef>();
        static VNStatsHud _hud;

        /// <summary>状态有变（面板刷新用）</summary>
        public static event System.Action Changed;

        /// <summary>读档 / 调试重建期间挂起：不自动接取、不判完成、不发奖、不弹 Toast</summary>
        public static bool Suspended { get; set; }

        // ------------------------------------------------------------------
        // 配置与查询
        // ------------------------------------------------------------------

        public static void Configure(List<VNQuestDef> defs, VNStatsHud hud)
        {
            _defs.Clear();
            if (defs != null)
                foreach (var d in defs)
                    if (d != null && !string.IsNullOrEmpty(d.id)) _defs.Add(d);
            _hud = hud;
        }

        public static IReadOnlyList<VNQuestDef> Defs => _defs;

        public static VNQuestDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in _defs)
                if (d.id == id) return d;
            return null;
        }

        public static string TitleOf(string id)
        {
            var def = Find(id);
            return def != null ? def.Title : id;
        }

        public static string FlagName(string id) => FlagPrefix + id;
        public static string PendingFlag(string id) => FlagPrefix + id + SuffixPending;
        public static string AcceptedMonthFlag(string id) => FlagPrefix + id + SuffixAcceptedMonth;
        public static string TimesFlag(string id) => FlagPrefix + id + SuffixTimes;
        public static string ResetMonthFlag(string id) => FlagPrefix + id + SuffixResetMonth;

        /// <summary>任务当前阶段（0 = 未接取）</summary>
        public static int StageOf(string id) => VNFlags.Get(FlagName(id));

        /// <summary>待领取的阶段号（0 = 没有可领的）</summary>
        public static int PendingOf(string id) => VNFlags.Get(PendingFlag(id));

        public static bool IsClaimable(string id) => PendingOf(id) > 0;
        public static bool IsDone(string id) => StageOf(id) == StageDone;
        public static bool IsFailed(string id) => StageOf(id) == StageFailed;

        /// <summary>进行中（含可领取）</summary>
        public static bool IsActive(string id)
        {
            int s = StageOf(id);
            return s > 0 && s <= MaxStage;
        }

        public static int Month => VNFlags.Get(MonthSerialFlag);

        /// <summary>当前有几个任务可以领（快捷条角标用）</summary>
        public static int ClaimableCount
        {
            get
            {
                int n = 0;
                foreach (var d in _defs)
                    if (PendingOf(d.id) > 0) n++;
                return n;
            }
        }

        // ------------------------------------------------------------------
        // 接取条件
        // ------------------------------------------------------------------

        /// <summary>前置任务是否全部完成</summary>
        public static bool RequirementsMet(VNQuestDef def)
        {
            if (def == null || def.requires == null) return true;
            foreach (var r in def.requires)
            {
                if (string.IsNullOrEmpty(r)) continue;
                if (!IsDone(r)) return false;
            }
            return true;
        }

        /// <summary>条件表达式是否满足（留空 = 满足）</summary>
        public static bool ConditionMet(string expr) =>
            string.IsNullOrEmpty(expr) || VNFlags.Evaluate(expr);

        /// <summary>
        /// 现在能不能接这个任务：未接取 + 前置完成 + 出现条件满足 + 不在冷却 + 未达次数上限。
        /// 委托板列表与自动接取共用这一条判定。
        /// </summary>
        public static bool CanAccept(VNQuestDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return false;
            if (StageOf(def.id) != 0) return false;
            if (def.maxTimes > 0 && VNFlags.Get(TimesFlag(def.id)) >= def.maxTimes) return false;

            int reset = VNFlags.Get(ResetMonthFlag(def.id));
            if (reset > 0 && Month < reset) return false;

            return RequirementsMet(def) && ConditionMet(def.unlockCondition);
        }

        /// <summary>委托板可接列表（tag 留空 = 不过滤）</summary>
        public static List<VNQuestDef> BoardList(string tag = null)
        {
            var list = new List<VNQuestDef>();
            foreach (var d in _defs)
            {
                if (!d.acceptFromBoard) continue;
                if (!string.IsNullOrEmpty(tag) && d.boardTag != tag) continue;
                if (!CanAccept(d)) continue;
                list.Add(d);
            }
            list.Sort((a, b) => b.priority.CompareTo(a.priority));
            return list;
        }

        // ------------------------------------------------------------------
        // 状态操作
        // ------------------------------------------------------------------

        /// <summary>接取。startStage 可 >1（quest start id 2 直接从阶段 2 开始）</summary>
        public static bool Accept(string id, int startStage = 1, bool silent = false)
        {
            if (string.IsNullOrEmpty(id)) return false;
            int stage = Mathf.Clamp(Mathf.Max(1, startStage), 1, MaxStage);

            VNFlags.Set(FlagName(id), stage);
            VNFlags.Set(PendingFlag(id), 0);
            VNFlags.Set(AcceptedMonthFlag(id), Month);
            VNFlags.Set(ResetMonthFlag(id), 0);

            if (!silent) VNToast.Show(VNLocale.T("quest.toastNew", TitleOf(id)), 2.2f);
            Changed?.Invoke();
            return true;
        }

        /// <summary>直接设阶段（剧本 quest stage）。清掉待领标记，由引擎重新判定</summary>
        public static void SetStage(string id, int stage, bool silent = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            stage = Mathf.Clamp(stage, 1, MaxStage);
            VNFlags.Set(FlagName(id), stage);
            VNFlags.Set(PendingFlag(id), 0);

            if (!silent)
            {
                var def = Find(id);
                string text = def != null ? def.StageText(stage) : "";
                VNToast.Show(string.IsNullOrEmpty(text)
                    ? VNLocale.T("quest.toastUpdate", TitleOf(id))
                    : VNLocale.T("quest.toastUpdateWith", TitleOf(id), text), 2.2f);
            }
            Changed?.Invoke();
        }

        /// <summary>标记完成（剧本 quest done：不发奖，奖励只走领取路径）</summary>
        public static void Complete(string id, bool silent = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            CompleteInternal(id, silent, "");
        }

        static void CompleteInternal(string id, bool silent, string rewardText)
        {
            var def = Find(id);
            VNFlags.Set(FlagName(id), StageDone);
            VNFlags.Set(PendingFlag(id), 0);
            VNFlags.Add(TimesFlag(id), 1);

            // 可重复日常：记下下次可接的月序
            if (def != null && def.repeatable)
                VNFlags.Set(ResetMonthFlag(id), Month + Mathf.Max(1, def.cooldownMonths));

            if (!silent)
            {
                VNToast.Show(string.IsNullOrEmpty(rewardText)
                    ? VNLocale.T("quest.toastDone", TitleOf(id))
                    : VNLocale.T("quest.toastDoneWith", TitleOf(id), rewardText), 2.6f);
            }
            Changed?.Invoke();
        }

        /// <summary>标记失败。发惩罚（silent 时只写状态）</summary>
        public static void Fail(string id, bool silent = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            var def = Find(id);
            VNFlags.Set(FlagName(id), StageFailed);
            VNFlags.Set(PendingFlag(id), 0);

            string penaltyText = def != null
                ? VNQuestReward.GrantAll(def.penalties, _hud, silent) : "";

            if (!silent)
            {
                VNToast.Show(string.IsNullOrEmpty(penaltyText)
                    ? VNLocale.T("quest.toastFail", TitleOf(id))
                    : VNLocale.T("quest.toastFailWith", TitleOf(id), penaltyText), 2.6f);
            }
            Changed?.Invoke();
        }

        /// <summary>放弃：回未接取，不发惩罚</summary>
        public static void Abandon(string id, bool silent = false)
        {
            if (string.IsNullOrEmpty(id)) return;
            VNFlags.Set(FlagName(id), 0);
            VNFlags.Set(PendingFlag(id), 0);

            // 自动接取型任务放弃后要压一个月冷却，否则下一帧就被引擎推回来了
            var def = Find(id);
            if (def != null && def.acceptAuto)
                VNFlags.Set(ResetMonthFlag(id), Month + 1);

            if (!silent) VNToast.Show(VNLocale.T("quest.toastAbandon", TitleOf(id)), 2f);
            Changed?.Invoke();
        }

        /// <summary>彻底清空（含完成次数与冷却）：剧情重置 / 调试用</summary>
        public static void Reset(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            VNFlags.Set(FlagName(id), 0);
            VNFlags.Set(PendingFlag(id), 0);
            VNFlags.Set(AcceptedMonthFlag(id), 0);
            VNFlags.Set(TimesFlag(id), 0);
            VNFlags.Set(ResetMonthFlag(id), 0);
            Changed?.Invoke();
        }

        /// <summary>把任务推上委托板（清掉冷却，让它立刻可接），但不接取</summary>
        public static void Offer(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (StageOf(id) != 0) return;
            VNFlags.Set(ResetMonthFlag(id), 0);
            Changed?.Invoke();
        }

        /// <summary>
        /// 领取：发放该阶段奖励，然后推进到下一阶段或完成。
        /// 幂等靠状态机——奖励只在「@待领 &gt; 0 → 领取」这一次跳变里发，
        /// 清 @待领 与写下一阶段在同一步完成，重复调用直接被 pending<=0 挡回。
        /// </summary>
        public static bool Claim(string id, bool silent = false)
        {
            int pending = PendingOf(id);
            if (pending <= 0) return false;

            var def = Find(id);
            string rewardText = def != null
                ? VNQuestReward.GrantAll(def.RewardsAt(pending), _hud, silent) : "";

            VNFlags.Set(PendingFlag(id), 0);

            int total = def != null ? def.StageCount : 0;
            if (def != null && pending < total)
            {
                // 还有下一阶段：回到「进行中」继续跑下一段条件
                VNFlags.Set(FlagName(id), Mathf.Min(pending + 1, MaxStage));
                if (!silent)
                {
                    string next = def.StageText(pending + 1);
                    string head = string.IsNullOrEmpty(rewardText)
                        ? VNLocale.T("quest.toastUpdate", TitleOf(id))
                        : VNLocale.T("quest.toastClaimed", TitleOf(id), rewardText);
                    VNToast.Show(string.IsNullOrEmpty(next) ? head : head + "\n▶ " + next, 2.6f);
                }
                Changed?.Invoke();
            }
            else
            {
                CompleteInternal(id, silent, rewardText);
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 引擎主循环
        // ------------------------------------------------------------------

        /// <summary>
        /// 全量求值：自动接取 / 条件达成转可领取 / 超期 / 日常冷却重置。
        /// 由 VNQuestLog 以「标脏 + 下一帧一次」的节奏驱动（同属性 HUD 的做法），
        /// 只算进行中的任务，几十个任务的量级毫无压力。
        /// silent = 读档后的静默重算：只更新可领取标记，不弹 Toast、不发奖。
        /// </summary>
        public static void Evaluate(bool silent = false)
        {
            if (Suspended) return;

            int month = Month;
            bool changed = false;

            foreach (var def in _defs)
            {
                string id = def.id;
                int stage = VNFlags.Get(FlagName(id));

                // ── 未接取：自动接取判定 ──
                if (stage == 0)
                {
                    if (def.acceptAuto && CanAccept(def))
                    {
                        Accept(id, 1, silent);
                        changed = true;
                    }
                    continue;
                }

                // ── 已完成：可重复日常的冷却重置 ──
                if (stage == StageDone)
                {
                    if (!def.repeatable) continue;
                    if (def.maxTimes > 0 && VNFlags.Get(TimesFlag(id)) >= def.maxTimes) continue;
                    int reset = VNFlags.Get(ResetMonthFlag(id));
                    if (reset > 0 && month >= reset)
                    {
                        VNFlags.Set(FlagName(id), 0);   // 回到未接取，等再次接取（或自动接）
                        changed = true;
                    }
                    continue;
                }

                if (stage == StageFailed) continue;

                // ── 进行中 ──
                if (PendingOf(id) > 0) continue;   // 已经在等玩家领了

                // 超期
                if (def.deadlineMonths > 0)
                {
                    int accepted = VNFlags.Get(AcceptedMonthFlag(id));
                    if (month - accepted > def.deadlineMonths)
                    {
                        if (def.expireToFail) Fail(id, silent);
                        else Abandon(id, silent);
                        changed = true;
                        continue;
                    }
                }

                // 阶段条件全部达成 → 转「可领取」
                var stageDef = def.StageAt(stage);
                if (stageDef != null && stageDef.AllMet)
                {
                    VNFlags.Set(PendingFlag(id), stage);
                    if (!silent)
                        VNToast.Show(VNLocale.T("quest.toastClaimable", def.Title), 2.4f);
                    changed = true;
                }
            }

            if (changed) Changed?.Invoke();
        }

        /// <summary>读档后调用：重算可领取标记，不发奖不弹提示</summary>
        public static void RecalculateSilently()
        {
            bool wasSuspended = Suspended;
            Suspended = false;
            Evaluate(true);
            Suspended = wasSuspended;
        }
    }
}
