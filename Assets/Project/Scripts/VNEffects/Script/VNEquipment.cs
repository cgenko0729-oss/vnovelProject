using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 装备系统核心（纯静态）：状态全部存 VNFlags，因此存档/读档/if 分支/调试重建零改动。
    ///   装备_&lt;道具id&gt;          = 部位编号（1~7 见 VNEquipSlot；0/不存在 = 未装备）
    ///   装备实增_&lt;部位&gt;_&lt;属性&gt; = 穿上时实际生效的属性增量；卸下按此扣回，
    ///                            避免钳制造成的穿脱不对称（98 穿 +5 钳到 100，卸下只扣 2）
    ///   装备效果_&lt;效果id&gt;       = 已装备道具该特殊效果的合计，穿脱后整体重算；
    ///                            生效逻辑由剧本判断（如 if 装备效果_金钱加倍>=1 jump 双倍工资）
    /// 道具数据 = VNShopDef.Item（VNGameConfig.shops 登记的商店兼任道具目录），
    /// 查表入口由 VNInventory 在 Awake 时注入 ItemResolver。
    /// </summary>
    public static class VNEquipment
    {
        public const string EquipFlagPrefix = "装备_";
        public const string AppliedFlagPrefix = "装备实增_";
        public const string EffectFlagPrefix = "装备效果_";

        /// <summary>按 id 查道具条目（VNInventory 注入；未注入时兜底扫 VNGameConfig.shops）</summary>
        public static System.Func<string, VNShopDef.Item> ItemResolver;

        public static string EquipFlagName(string itemId) => EquipFlagPrefix + itemId;
        public static string EffectFlagName(string effectId) => EffectFlagPrefix + effectId;
        static string AppliedFlagName(int slot, string statId) =>
            $"{AppliedFlagPrefix}{slot}_{statId}";

        /// <summary>部位显示名（本地化，equip.slot.1~7）</summary>
        public static string SlotName(VNEquipSlot slot) => VNLocale.T("equip.slot." + (int)slot);

        public static bool IsEquipped(string itemId) =>
            VNFlags.Get(EquipFlagName(itemId)) > 0;

        /// <summary>某部位当前装备的道具 id（空槽返回 null）</summary>
        public static string ItemInSlot(VNEquipSlot slot)
        {
            int value = (int)slot;
            if (value <= 0) return null;
            foreach (var kv in VNFlags.All)
                if (kv.Value == value && kv.Key.StartsWith(EquipFlagPrefix))
                    return kv.Key.Substring(EquipFlagPrefix.Length);
            return null;
        }

        /// <summary>装备道具：同部位已有装备先自动卸下（静默），加成按钳制后实际值记账。</summary>
        public static bool Equip(VNShopDef.Item item)
        {
            if (item == null || !item.IsEquippable) return false;
            if (VNFlags.Get(VNShopDef.ItemFlagName(item.id)) <= 0) return false;
            if (IsEquipped(item.id)) return false;

            string occupant = ItemInSlot(item.equipSlot);
            if (occupant != null) Unequip(occupant, true);

            var hud = Object.FindFirstObjectByType<VNStatsHud>();
            foreach (var bonus in item.statBonuses)
            {
                if (bonus == null || string.IsNullOrEmpty(bonus.statId) || bonus.amount == 0)
                    continue;
                var def = hud != null ? hud.Find(bonus.statId) : null;
                int old = VNFlags.Get(bonus.statId);
                int target = old + bonus.amount;
                if (def != null) target = def.Clamp(target);
                if (target != old) VNFlags.Set(bonus.statId, target);
                VNFlags.Set(AppliedFlagName((int)item.equipSlot, bonus.statId), target - old);
            }

            VNFlags.Set(EquipFlagName(item.id), (int)item.equipSlot);
            RecomputeEffects();
            VNToast.Show(VNLocale.T("equip.toastEquip", item.DisplayName), 1.4f);
            return true;
        }

        /// <summary>卸下道具：属性按「装备实增_」记录扣回（扣回同样过钳制不跌破下限）。</summary>
        public static bool Unequip(string itemId, bool silent = false)
        {
            int slot = VNFlags.Get(EquipFlagName(itemId));
            if (slot <= 0) return false;

            string prefix = $"{AppliedFlagPrefix}{slot}_";
            var applied = new List<KeyValuePair<string, int>>();
            foreach (var kv in VNFlags.All)
                if (kv.Key.StartsWith(prefix)) applied.Add(kv);

            var hud = Object.FindFirstObjectByType<VNStatsHud>();
            foreach (var kv in applied)
            {
                if (kv.Value != 0)
                {
                    string statId = kv.Key.Substring(prefix.Length);
                    var def = hud != null ? hud.Find(statId) : null;
                    int target = VNFlags.Get(statId) - kv.Value;
                    if (def != null) target = def.Clamp(target);
                    VNFlags.Set(statId, target);
                }
                VNFlags.Set(kv.Key, 0);
            }

            VNFlags.Set(EquipFlagName(itemId), 0);
            RecomputeEffects();
            if (!silent)
            {
                var item = Resolve(itemId);
                VNToast.Show(VNLocale.T("equip.toastUnequip",
                    item != null ? item.DisplayName : itemId), 1.4f);
            }
            return true;
        }

        /// <summary>使用道具：useOps 走 VNStatDef 钳制+飘字；消耗后持有归零且在装备中则强制卸下。</summary>
        public static bool Use(VNShopDef.Item item)
        {
            if (item == null || !item.IsUsable) return false;
            string countFlag = VNShopDef.ItemFlagName(item.id);
            if (VNFlags.Get(countFlag) <= 0) return false;

            var hud = Object.FindFirstObjectByType<VNStatsHud>();
            foreach (var op in item.useOps)
            {
                if (op == null || string.IsNullOrEmpty(op.statId) || op.amount == 0) continue;
                if (hud != null)
                    hud.Apply(op.statId, (op.amount >= 0 ? "+" : "") + op.amount, false, 0);
                else
                    VNFlags.Add(op.statId, op.amount);
            }

            if (item.consumeOnUse)
            {
                VNFlags.Add(countFlag, -1);
                if (VNFlags.Get(countFlag) <= 0 && IsEquipped(item.id))
                    Unequip(item.id, true);
            }
            VNToast.Show(VNLocale.T("equip.toastUse", item.DisplayName), 1.4f);
            return true;
        }

        /// <summary>失去道具后调用（商店卖出等）：持有数不足且仍在装备中 → 强制卸下。</summary>
        public static void HandleItemLost(string itemId)
        {
            if (VNFlags.Get(VNShopDef.ItemFlagName(itemId)) <= 0 && IsEquipped(itemId))
                Unequip(itemId);
        }

        /// <summary>按当前全部已装备道具重算「装备效果_」合计（含清掉已失效的旧条目）。</summary>
        public static void RecomputeEffects()
        {
            var stale = new List<string>();
            var equipped = new List<string>();
            foreach (var kv in VNFlags.All)
            {
                if (kv.Key.StartsWith(EffectFlagPrefix) && kv.Value != 0)
                    stale.Add(kv.Key);
                else if (kv.Key.StartsWith(EquipFlagPrefix) && kv.Value > 0)
                    equipped.Add(kv.Key.Substring(EquipFlagPrefix.Length));
            }

            var totals = new Dictionary<string, int>();
            foreach (var itemId in equipped)
            {
                var item = Resolve(itemId);
                if (item == null) continue;
                foreach (var effect in item.passiveEffects)
                {
                    if (effect == null || string.IsNullOrEmpty(effect.effectId)) continue;
                    string flag = EffectFlagName(effect.effectId);
                    totals.TryGetValue(flag, out int sum);
                    totals[flag] = sum + effect.amount;
                }
            }

            foreach (var key in stale)
                if (!totals.ContainsKey(key)) VNFlags.Set(key, 0);
            foreach (var kv in totals)
                if (VNFlags.Get(kv.Key) != kv.Value) VNFlags.Set(kv.Key, kv.Value);
        }

        static VNShopDef.Item Resolve(string itemId)
        {
            if (ItemResolver != null) return ItemResolver(itemId);
            var cfg = VNGameConfig.Active;
            if (cfg != null)
            {
                foreach (var shop in cfg.shops)
                {
                    var item = shop != null ? shop.FindItem(itemId) : null;
                    if (item != null) return item;
                }
            }
            return null;
        }
    }
}
