using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>奖励种类。全部落在 flag 上，所以随存档走、剧本 if 直接判断。</summary>
    public enum VNQuestRewardKind
    {
        Stat,   // 养成属性（走 VNStatsHud.Apply：带钳制与 HUD 飘字演出）
        Item,   // 道具（道具_<id>）
        Flag,   // 任意 flag（剧情解锁走这条：待触发_xx / 商店解锁_xx）
        Cg,     // 解锁 CG 鉴赏条目（全局，不随存档回退）
        Quest,  // 连锁接取下一个任务（任务链用）
    }

    /// <summary>
    /// 一条奖励（惩罚同结构，数量取负）。
    /// 说明文案同时用于委托板的报酬预览与领取 Toast，所以必须能被翻译。
    /// </summary>
    [System.Serializable]
    public class VNQuestReward
    {
        public VNQuestRewardKind kind = VNQuestRewardKind.Stat;

        [Header("目标：属性 id / 道具 id / flag 名 / CG id / 任务 id")]
        public string target;

        [Header("数量（Flag 类为写入值；负数 = 惩罚）")]
        public int amount = 1;

        [Header("Flag 类：勾上 = 增减，不勾 = 直接设为该值\n" +
                "解锁类（待触发_xx）建议不勾——日常任务重复领取时才不会累加成 2、3")]
        public bool flagAdd;

        [Header("显示文案（留空 = 自动生成，如「金钱 +500」）")]
        public string note;
        public string noteEn;
        public string noteJa;

        public string LocalizedNote
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? noteEn
                    : VNLocale.Language == VNLanguage.Japanese ? noteJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return note;
            }
        }

        /// <summary>面板与 Toast 用的一行描述（自定义文案优先）</summary>
        public string Describe(VNStatsHud hud = null)
        {
            string custom = LocalizedNote;
            if (!string.IsNullOrEmpty(custom)) return custom;

            string name = target;
            switch (kind)
            {
                case VNQuestRewardKind.Stat:
                {
                    var def = hud != null ? hud.Find(target) : null;
                    if (def != null) name = def.DisplayName;
                    return $"{name} {Signed(amount)}";
                }
                case VNQuestRewardKind.Item:
                    return amount > 1 ? $"{name} ×{amount}" : name;
                case VNQuestRewardKind.Cg:
                    return VNLocale.T("quest.rewardCg", name);
                case VNQuestRewardKind.Quest:
                    return VNLocale.T("quest.rewardQuest", VNQuestEngine.TitleOf(target));
                default:
                    return flagAdd ? $"{name} {Signed(amount)}" : $"{name} = {amount}";
            }
        }

        static string Signed(int v) => v >= 0 ? "+" + v : v.ToString();

        // ------------------------------------------------------------------

        /// <summary>
        /// 发放一条奖励。silent = 调试重建/读档重算，不弹演出。
        /// 幂等由调用方（引擎的「待领 → 领取」跳变）保证，本方法自己不做去重。
        /// </summary>
        public static void Grant(VNQuestReward r, VNStatsHud hud, bool silent)
        {
            if (r == null || string.IsNullOrEmpty(r.target)) return;
            switch (r.kind)
            {
                case VNQuestRewardKind.Stat:
                    // 走 Apply 而不是直接写 flag：钳制、HUD 数字滚动、+N 上飘都在里面
                    if (hud != null) hud.Apply(r.target, Signed(r.amount), silent, 0);
                    else VNFlags.Add(r.target, r.amount);
                    break;

                case VNQuestRewardKind.Item:
                    VNFlags.Add(VNShopDef.ItemFlagName(r.target), r.amount);
                    break;

                case VNQuestRewardKind.Flag:
                    if (r.flagAdd) VNFlags.Add(r.target, r.amount);
                    else VNFlags.Set(r.target, r.amount);
                    break;

                case VNQuestRewardKind.Cg:
                    VNCgUnlocks.Unlock(r.target);
                    break;

                case VNQuestRewardKind.Quest:
                    VNQuestEngine.Accept(r.target, 1, silent);
                    break;
            }
        }

        /// <summary>发放一整组，并返回拼好的描述（Toast 用；一条都没有返回空串）</summary>
        public static string GrantAll(List<VNQuestReward> list, VNStatsHud hud, bool silent)
        {
            if (list == null || list.Count == 0) return "";
            var parts = new List<string>();
            foreach (var r in list)
            {
                if (r == null || string.IsNullOrEmpty(r.target)) continue;
                Grant(r, hud, silent);
                parts.Add(r.Describe(hud));
            }
            return string.Join("　", parts);
        }

        /// <summary>只拼描述不发放（委托板的报酬预览、面板的奖励行）</summary>
        public static string Preview(List<VNQuestReward> list, VNStatsHud hud = null)
        {
            if (list == null || list.Count == 0) return "";
            var parts = new List<string>();
            foreach (var r in list)
            {
                if (r == null || string.IsNullOrEmpty(r.target)) continue;
                parts.Add(r.Describe(hud));
            }
            return string.Join("　", parts);
        }
    }
}
