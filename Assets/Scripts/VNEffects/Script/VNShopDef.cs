using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>装备部位（VNShopDef.Item.equipSlot；编号即 flag「装备_&lt;道具id&gt;」的值）</summary>
    public enum VNEquipSlot
    {
        None = 0,      // 不可装备
        Head = 1,      // 头部
        Face = 2,      // 脸部
        UpperBody = 3, // 上半身
        Hands = 4,     // 手部
        LowerBody = 5, // 下半身
        Feet = 6,      // 脚部
        Special = 7,   // 特殊
    }

    /// <summary>
    /// 商店定义资产：一家商店的商品清单与文案。持有数量全部存 VNFlags
    /// （flag 名 = 道具_&lt;商品id&gt;），随存档走、if 分支可直接判断
    /// （如 if 道具_药水>=1 jump 有药）。金钱走养成属性（默认「金钱」）。
    /// 剧本用法：event shop id:&lt;商店id&gt;（模板 Inspector 里登记多家商店）。
    /// 不上架贩卖、只当「道具目录」用的商店也可以登记进 VNGameConfig.shops，
    /// 物品栏/装备系统按 id 跨全部商店取文案与装备数据。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Shop Definition", fileName = "NewShop")]
    public class VNShopDef : ScriptableObject
    {
        public const string ItemFlagPrefix = "道具_";

        /// <summary>属性操作条目（装备加成 / 使用效果共用）</summary>
        [System.Serializable]
        public class StatOp
        {
            [Header("属性 id（= flag 名，如 魅力/行动力）")]
            public string statId;

            [Header("增量（可为负）")]
            public int amount = 1;
        }

        /// <summary>装备特殊效果条目：合计写 flag「装备效果_&lt;id&gt;」，由剧本 if 判断生效</summary>
        [System.Serializable]
        public class PassiveEffect
        {
            [Header("效果 id（= flag「装备效果_<id>」，可中文，永远不翻译）")]
            public string effectId;

            [Header("数值（多件装备同效果时累加）")]
            public int amount = 1;

            [Header("效果说明（介绍区展示；留空 = 直接用 id）")]
            public string label;
            [Header("英文/日文效果说明（留空回退中文）")]
            public string labelEn;
            public string labelJa;

            public string DisplayLabel
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? labelEn
                        : VNLocale.Language == VNLanguage.Japanese ? labelJa : null;
                    if (!string.IsNullOrEmpty(localized)) return localized;
                    return string.IsNullOrEmpty(label) ? effectId : label;
                }
            }
        }

        /// <summary>商品持有数的 flag 名</summary>
        public static string ItemFlagName(string itemId) => ItemFlagPrefix + itemId;

        [System.Serializable]
        public class Item
        {
            [Header("商品 id（= flag「道具_<id>」，可中文，永远不翻译）")]
            public string id;

            [Header("显示名；留空 = 直接用 id")]
            public string displayName;
            [Header("英文/日文显示名（留空回退中文）")]
            public string displayNameEn;
            public string displayNameJa;

            [Header("一句话描述（可留空）")]
            public string description;
            public string descriptionEn;
            public string descriptionJa;

            [Header("图标（可留空，用色块代替）")]
            public Sprite icon;

            [Header("买入价格（扣减金钱属性）")]
            public int price = 100;

            [Header("卖出价格（0 = 本店不收购该商品）")]
            public int sellPrice = 0;

            [Header("最多持有数（0 = 不限）")]
            public int maxOwned = 0;

            [Header("上架条件（VNFlags 表达式，如 好感度>=2；留空 = 总是上架）")]
            public string condition;

            [Header("—— 装备（部位 = None 则不可装备）——")]
            public VNEquipSlot equipSlot = VNEquipSlot.None;

            [Header("装备属性加成（穿上直接写属性；卸下按实际生效量扣回）")]
            public List<StatOp> statBonuses = new List<StatOp>();

            [Header("装备特殊效果（合计写 flag「装备效果_<id>」，生效逻辑由剧本 if 判断）")]
            public List<PassiveEffect> passiveEffects = new List<PassiveEffect>();

            [Header("—— 使用（列表为空则不可使用）——")]
            [Header("使用时执行的属性操作（走 VNStatDef 钳制 + 飘字）")]
            public List<StatOp> useOps = new List<StatOp>();

            [Header("使用后消耗 1 个")]
            public bool consumeOnUse = true;

            /// <summary>可装备（右键菜单显示「装备」）</summary>
            public bool IsEquippable => equipSlot != VNEquipSlot.None;

            /// <summary>可使用（右键菜单显示「使用」）</summary>
            public bool IsUsable => useOps != null && useOps.Count > 0;

            public string DisplayName
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                        : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                    if (!string.IsNullOrEmpty(localized)) return localized;
                    return string.IsNullOrEmpty(displayName) ? id : displayName;
                }
            }

            public string LocalizedDescription
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? descriptionEn
                        : VNLocale.Language == VNLanguage.Japanese ? descriptionJa : null;
                    return string.IsNullOrEmpty(localized) ? description : localized;
                }
            }

            /// <summary>当前持有数（flag 道具_&lt;id&gt;）</summary>
            public int Owned => VNFlags.Get(ItemFlagName(id));
        }

        [Header("剧本 event shop id:<商店id> 引用的 id（可中文，如 服装店）")]
        public string shopId;

        [Header("商店显示名；留空 = 直接用 shopId")]
        public string shopName;
        [Header("英文/日文商店名（留空回退中文）")]
        public string shopNameEn;
        public string shopNameJa;

        [Header("结算用的金钱属性 id（VNStatDef / flag 名）")]
        public string currencyStat = "金钱";

        [Header("商品清单")]
        public List<Item> items = new List<Item>();

        public string ShopName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? shopNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? shopNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(shopName) ? shopId : shopName;
            }
        }

        public Item FindItem(string id)
        {
            foreach (var item in items)
                if (item != null && item.id == id) return item;
            return null;
        }
    }
}
