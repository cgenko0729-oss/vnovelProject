using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 任务定义资产：文案 + 完成条件 + 奖励。运行状态全部存 VNFlags
    /// （主 flag 名 = 任务_&lt;id&gt;），随存档免费保存，剧本可直接 if 任务_xx>=2 判断。
    ///
    /// 阶段号约定：0=未接取，1..98=进行中（stageDefs[阶段-1] 为当前目标），
    /// 100=完成，-1=失败（见 VNQuestEngine 常量）。
    /// 「可领取」不占主 flag，走旁路 任务_&lt;id&gt;@待领 —— 否则剧本里的
    /// if 任务_xx==2 会在可领取期间失效，多阶段也丢失「是哪个阶段可领」的信息。
    ///
    /// 向后兼容：老资产只填了 stages（一串文案、无条件）照常工作，
    /// 只是不会自动判定完成，仍靠剧本 quest stage/done 推进。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Quest Definition", fileName = "NewQuest")]
    public class VNQuestDef : ScriptableObject
    {
        // ==============================================================
        // 子目标 / 阶段
        // ==============================================================

        /// <summary>一条子目标：一个条件表达式 + 一行文案（+ 可选进度条）</summary>
        [System.Serializable]
        public class Objective
        {
            [Header("目标文案（面板里逐条 ☑/☐ 显示）")]
            public string text;
            public string textEn;
            public string textJa;

            [Header("完成条件（VNFlags 表达式，如 羽球_我方得分@最高>=5000）\n" +
                    "留空 = 这条永不自动达成（整个阶段就只能靠剧本 quest stage 推进）")]
            public string condition;

            [Header("进度条：看哪个 flag（留空 = 不画进度条）")]
            public string progressFlag;

            [Header("进度条目标值（progressFlag 达到它算满）")]
            public int progressTarget;

            public string LocalizedText
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? textEn
                        : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                    return string.IsNullOrEmpty(localized) ? text : localized;
                }
            }

            public bool IsMet => !string.IsNullOrEmpty(condition) &&
                                 VNFlags.Evaluate(condition);

            /// <summary>0~1 进度；没声明进度 flag 时按达成与否返回 0/1</summary>
            public float Progress01
            {
                get
                {
                    if (string.IsNullOrEmpty(progressFlag) || progressTarget == 0)
                        return IsMet ? 1f : 0f;
                    return Mathf.Clamp01(VNFlags.Get(progressFlag) / (float)progressTarget);
                }
            }

            public bool HasProgressBar =>
                !string.IsNullOrEmpty(progressFlag) && progressTarget != 0;
        }

        /// <summary>一个阶段：目标文案 + 子目标列表 + 领取时发的奖励</summary>
        [System.Serializable]
        public class Stage
        {
            [Header("阶段目标文案（面板里显示在任务标题下）")]
            public string text;
            public string textEn;
            public string textJa;

            [Header("子目标：全部达成才转「可领取」\n" +
                    "一条带条件的都没有 = 本阶段不自动判定，靠剧本 quest stage/done 推进")]
            public List<Objective> objectives = new List<Objective>();

            [Header("本阶段领取时发放的奖励")]
            public List<VNQuestReward> rewards = new List<VNQuestReward>();

            public string LocalizedText
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? textEn
                        : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                    return string.IsNullOrEmpty(localized) ? text : localized;
                }
            }

            /// <summary>本阶段有没有可自动判定的条件</summary>
            public bool HasCondition
            {
                get
                {
                    if (objectives == null) return false;
                    foreach (var o in objectives)
                        if (o != null && !string.IsNullOrEmpty(o.condition)) return true;
                    return false;
                }
            }

            /// <summary>全部子目标达成（没有子目标视为未达成，避免空阶段瞬间完成）</summary>
            public bool AllMet
            {
                get
                {
                    if (!HasCondition) return false;
                    foreach (var o in objectives)
                    {
                        if (o == null) continue;
                        if (!string.IsNullOrEmpty(o.condition) && !o.IsMet) return false;
                    }
                    return true;
                }
            }
        }

        // ==============================================================
        // 基本
        // ==============================================================

        [Header("剧本 quest 命令引用的 id（可中文，如 告白大作战；不得含 '@'）")]
        public string id;

        [Header("任务日志显示的标题；留空 = 直接用 id")]
        public string title;

        [TextArea]
        [Header("任务总描述（日志里显示在标题下，可留空）")]
        public string description;

        [Header("面板图标（可留空）")]
        public Sprite icon;

        [Header("面板排序权重：越大越靠前（主线置顶）")]
        public int priority;

        [Header("隐藏任务：未接取时不出现在日志里")]
        public bool hidden;

        // ==============================================================
        // 接取
        // ==============================================================

        [Header("──────── 接取 ────────")]
        [Header("出现在委托板上（event questboard 可接）")]
        public bool acceptFromBoard;

        [Header("满足下方条件时自动接取（无需 UI，引擎推送 + Toast）")]
        public bool acceptAuto;

        [Header("出现条件（VNFlags 表达式；留空 = 无条件）\n" +
                "自动接取模式下达成即接；委托板模式下决定是否上板")]
        public string unlockCondition;

        [Header("前置任务 id：全部完成后本任务才可接\n" +
                "等价于往 unlockCondition 写 任务_A==100，但独立字段才能在面板显示" +
                "「需先完成 X」并被 Lint 查环")]
        public List<string> requires = new List<string>();

        [Header("委托板分类标签（event questboard tag: 按它过滤）")]
        public string boardTag;

        [Header("委托人角色 id（委托板上显示名字，可留空）")]
        public string clientCharacterId;

        // ==============================================================
        // 阶段与奖励
        // ==============================================================

        [Header("──────── 阶段 ────────")]
        [Header("阶段列表：第 1 项对应阶段 1（quest start 后的初始阶段）")]
        public List<Stage> stageDefs = new List<Stage>();

        [Header("【旧字段·兼容保留】只有文案的阶段。stageDefs 为空时用它，\n" +
                "此时任务不会自动判定，仍靠剧本 quest stage/done 推进")]
        public List<string> stages = new List<string>();

        // ==============================================================
        // 限时
        // ==============================================================

        [Header("──────── 限时 ────────")]
        [Header("接取后 N 个月内要完成；0 = 无限期\n" +
                "基准是引擎维护的单调递增 flag「月序」，不是会 1~12 循环的「月份」")]
        public int deadlineMonths;

        [Header("超期后：勾上 = 转为失败（发下方惩罚），不勾 = 自动放弃回未接取")]
        public bool expireToFail = true;

        [Header("失败惩罚（与奖励同结构，数量取负）")]
        public List<VNQuestReward> penalties = new List<VNQuestReward>();

        // ==============================================================
        // 重复
        // ==============================================================

        [Header("──────── 可重复日常 ────────")]
        [Header("完成后可再次接取")]
        public bool repeatable;

        [Header("完成后隔几个月可再接（1 = 每月一次）")]
        public int cooldownMonths = 1;

        [Header("总共可完成几次；0 = 无限")]
        public int maxTimes;

        // ==============================================================
        // 本地化文案
        // ==============================================================

        [Header("—— English 文案（本地化；留空的项回退中文）——")]
        public string titleEn;
        [TextArea]
        public string descriptionEn;
        [Header("英文阶段文案：与旧 stages 一一对应（stageDefs 有自己的译文字段）")]
        public List<string> stagesEn = new List<string>();

        [Header("—— 日本語 文案（本地化；留空的项回退中文）——")]
        public string titleJa;
        [TextArea]
        public string descriptionJa;
        [Header("日文阶段文案：与旧 stages 一一对应")]
        public List<string> stagesJa = new List<string>();

        // ==============================================================
        // 查询
        // ==============================================================

        /// <summary>当前语言的任务标题（译名留空回退中文 title，再回退 id）</summary>
        public string Title
        {
            get
            {
                string localized = Pick(titleEn, titleJa);
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(title) ? id : title;
            }
        }

        /// <summary>当前语言的任务总描述（留空回退中文）</summary>
        public string LocalizedDescription
        {
            get
            {
                string localized = Pick(descriptionEn, descriptionJa);
                return string.IsNullOrEmpty(localized) ? description : localized;
            }
        }

        /// <summary>阶段总数（新结构优先，回退旧 stages）</summary>
        public int StageCount =>
            stageDefs != null && stageDefs.Count > 0 ? stageDefs.Count
            : stages != null ? stages.Count : 0;

        /// <summary>取阶段结构（阶段号从 1 起）；旧资产或越界返回 null</summary>
        public Stage StageAt(int stage)
        {
            if (stageDefs == null || stage < 1 || stage > stageDefs.Count) return null;
            return stageDefs[stage - 1];
        }

        /// <summary>当前语言的阶段目标文案（阶段号从 1 起；缺译/越界回退中文，再回退空串）</summary>
        public string StageText(int stage)
        {
            if (stage < 1) return "";

            var def = StageAt(stage);
            if (def != null) return def.LocalizedText;

            var localizedList = VNLocale.Language == VNLanguage.English ? stagesEn
                : VNLocale.Language == VNLanguage.Japanese ? stagesJa : null;
            if (localizedList != null && stage <= localizedList.Count &&
                !string.IsNullOrEmpty(localizedList[stage - 1]))
                return localizedList[stage - 1];
            return stages != null && stage <= stages.Count ? stages[stage - 1] : "";
        }

        /// <summary>该阶段领取时发的奖励（旧资产没有奖励，返回 null）</summary>
        public List<VNQuestReward> RewardsAt(int stage) => StageAt(stage)?.rewards;

        /// <summary>整份任务有没有配过任何自动判定条件（Lint 的 no-entry 检查用）</summary>
        public bool HasAnyCondition
        {
            get
            {
                if (stageDefs == null) return false;
                foreach (var s in stageDefs)
                    if (s != null && s.HasCondition) return true;
                return false;
            }
        }

        static string Pick(string en, string ja)
        {
            switch (VNLocale.Language)
            {
                case VNLanguage.English: return en;
                case VNLanguage.Japanese: return ja;
                default: return null;
            }
        }
    }
}
