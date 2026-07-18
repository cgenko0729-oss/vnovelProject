using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 商店定义资产：一家商店的商品清单与文案。持有数量全部存 VNFlags
    /// （flag 名 = 道具_&lt;商品id&gt;），随存档走、if 分支可直接判断
    /// （如 if 道具_药水>=1 jump 有药）。金钱走养成属性（默认「金钱」）。
    /// 剧本用法：event shop id:&lt;商店id&gt;（模板 Inspector 里登记多家商店）。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Shop Definition", fileName = "NewShop")]
    public class VNShopDef : ScriptableObject
    {
        public const string ItemFlagPrefix = "道具_";

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
