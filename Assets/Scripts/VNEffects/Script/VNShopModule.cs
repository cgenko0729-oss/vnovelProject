using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：商店（买入/卖出）。金钱走养成属性（VNStatsHud 钳制+飘字），
    /// 持有数走 flag「道具_&lt;id&gt;」——存档/if 分支零改动复用。
    ///
    /// 剧本用法（商店在模板 Inspector 的 shops 列表里登记）：
    ///   event shop id:服装店
    ///   * 离开 -> 商店结束        ← 可选：接住"离开"结果分支；不写则顺序继续
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) /
    /// 全部 Tween SetLink。
    /// </summary>
    public class VNShopModule : VNEventModule
    {
        [Header("本模板登记的商店定义资产（event shop id:xx 按 shopId 查找）")]
        public List<VNShopDef> shops = new List<VNShopDef>();

        VNShopDef _shop;
        VNStatsHud _statsHud;
        bool _selling;   // false = 购买页，true = 卖出页
        bool _leaving;

        RectTransform _panel;
        TextMeshProUGUI _moneyText;
        RectTransform _listContent;
        Image _buyTabImage;
        Image _sellTabImage;

        static readonly Color TabActive = new Color(0.92f, 0.61f, 0.18f, 0.98f);
        static readonly Color TabNormal = new Color(0.1f, 0.13f, 0.22f, 0.95f);

        protected override void OnLaunch(VNEventContext ctx)
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.shops, ref shops);

            string id = ctx.Kw("id");
            _shop = null;
            foreach (var s in shops)
                if (s != null && s.shopId == id) { _shop = s; break; }
            if (_shop == null && shops.Count == 1 && string.IsNullOrEmpty(id))
                _shop = shops[0]; // 只登记了一家时 id 可省略

            if (_shop == null)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：商店模板没有登记 id「{id}」" +
                                 "的 VNShopDef，直接返回");
                Done("");
                return;
            }

            _statsHud = FindFirstObjectByType<VNStatsHud>();
            BuildUi();
            RefreshMoney();
            RebuildList();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Leave();
        }

        // ------------------------------------------------------------------
        // 交易
        // ------------------------------------------------------------------

        int Money => VNFlags.Get(_shop.currencyStat);

        /// <summary>金额显示：有属性定义用它的格式（500G），否则裸数字</summary>
        string FormatMoney(int value)
        {
            var def = _statsHud != null ? _statsHud.Find(_shop.currencyStat) : null;
            return def != null ? def.Format(value) : value.ToString();
        }

        void ApplyMoney(int delta)
        {
            if (_statsHud != null)
                _statsHud.Apply(_shop.currencyStat, (delta >= 0 ? "+" : "") + delta, true, 0);
            else
                VNFlags.Add(_shop.currencyStat, delta);
        }

        void Buy(VNShopDef.Item item)
        {
            if (_leaving) return;
            if (Money < item.price) return;
            if (item.maxOwned > 0 && item.Owned >= item.maxOwned) return;
            ApplyMoney(-item.price);
            VNFlags.Add(VNShopDef.ItemFlagName(item.id), 1);
            VNToast.Show(VNLocale.T("shop.toastBuy", item.DisplayName,
                FormatMoney(item.price)), 1.6f);
            RefreshMoney();
            RebuildList();
        }

        void Sell(VNShopDef.Item item)
        {
            if (_leaving) return;
            if (item.sellPrice <= 0 || item.Owned <= 0) return;
            ApplyMoney(item.sellPrice);
            VNFlags.Add(VNShopDef.ItemFlagName(item.id), -1);
            VNToast.Show(VNLocale.T("shop.toastSell", item.DisplayName,
                FormatMoney(item.sellPrice)), 1.6f);
            RefreshMoney();
            RebuildList();
        }

        void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            if (_panel != null)
            {
                _panel.DOScale(0.92f, 0.18f).SetEase(Ease.InQuad)
                      .SetUpdate(true).SetLink(gameObject);
                DOVirtual.DelayedCall(0.18f, () => Done("离开"), true).SetLink(gameObject);
            }
            else Done("离开");
        }

        // ------------------------------------------------------------------
        // UI 构建
        // ------------------------------------------------------------------

        void BuildUi()
        {
            var root = (RectTransform)transform;

            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim);

            _panel = CreateImage("Panel", root, VNProceduralTextures.RoundedRectSprite,
                new Color(0.05f, 0.07f, 0.13f, 0.97f));
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.anchorMin = new Vector2(0.24f, 0.1f);
            _panel.anchorMax = new Vector2(0.76f, 0.9f);
            _panel.offsetMin = Vector2.zero;
            _panel.offsetMax = Vector2.zero;
            _panel.localScale = Vector3.one * 0.92f;
            _panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);

            // 标题（商店名）
            var title = CreateText("Title", _panel, 40, new Color(1f, 0.92f, 0.7f, 1f),
                _shop.ShopName);
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -28f);
            titleRect.sizeDelta = new Vector2(0f, 52f);

            // 所持金（右上）
            _moneyText = CreateText("Money", _panel, 30, new Color(1f, 0.84f, 0.42f, 1f), "");
            _moneyText.alignment = TextAlignmentOptions.MidlineRight;
            _moneyText.fontStyle = FontStyles.Bold;
            var moneyRect = (RectTransform)_moneyText.transform;
            moneyRect.anchorMin = new Vector2(1f, 1f);
            moneyRect.anchorMax = new Vector2(1f, 1f);
            moneyRect.pivot = new Vector2(1f, 1f);
            moneyRect.anchoredPosition = new Vector2(-36f, -34f);
            moneyRect.sizeDelta = new Vector2(320f, 42f);

            // 页签：购买 / 卖出
            _buyTabImage = CreateTab(VNLocale.T("shop.tabBuy"), 0, () => SetTab(false));
            _sellTabImage = CreateTab(VNLocale.T("shop.tabSell"), 1, () => SetTab(true));

            // 商品滚动列表
            var scrollGo = new GameObject("Scroll", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(_panel, false);
            scrollRect.anchorMin = new Vector2(0.05f, 0.16f);
            scrollRect.anchorMax = new Vector2(0.95f, 0.76f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            var contentGo = new GameObject("Content", typeof(RectTransform),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _listContent = (RectTransform)contentGo.transform;
            _listContent.SetParent(scrollRect, false);
            _listContent.anchorMin = new Vector2(0f, 1f);
            _listContent.anchorMax = new Vector2(1f, 1f);
            _listContent.pivot = new Vector2(0.5f, 1f);
            // sizeDelta 默认 (100,100)，横向拉伸下 = 比视口宽 100px → 左右各溢出 50px
            // 被 RectMask2D 裁掉（图标/价格贴边时会缺一块），必须显式清零
            _listContent.sizeDelta = Vector2.zero;
            _listContent.anchoredPosition = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _listContent;
            scroll.viewport = scrollRect;

            // 离开按钮（底部居中）
            var leave = CreateButton(VNLocale.T("shop.leave"), _panel, Leave);
            var leaveRect = (RectTransform)leave.transform;
            leaveRect.anchorMin = new Vector2(0.5f, 0f);
            leaveRect.anchorMax = new Vector2(0.5f, 0f);
            leaveRect.pivot = new Vector2(0.5f, 0f);
            leaveRect.anchoredPosition = new Vector2(0f, 24f);
            leaveRect.sizeDelta = new Vector2(220f, 54f);

            SetTab(false);
        }

        Image CreateTab(string label, int index, UnityEngine.Events.UnityAction onClick)
        {
            var tab = CreateButton(label, _panel, onClick);
            var rect = (RectTransform)tab.transform;
            rect.anchorMin = new Vector2(0.05f, 1f);
            rect.anchorMax = new Vector2(0.05f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(index * 170f, -84f);
            rect.sizeDelta = new Vector2(160f, 48f);
            return tab.GetComponent<Image>();
        }

        void SetTab(bool selling)
        {
            _selling = selling;
            if (_buyTabImage != null) _buyTabImage.color = selling ? TabNormal : TabActive;
            if (_sellTabImage != null) _sellTabImage.color = selling ? TabActive : TabNormal;
            RebuildList();
        }

        void RefreshMoney()
        {
            if (_moneyText != null) _moneyText.text = FormatMoney(Money);
        }

        void RebuildList()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            int shown = 0;
            foreach (var item in _shop.items)
            {
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                if (!string.IsNullOrEmpty(item.condition) &&
                    !VNFlags.Evaluate(item.condition)) continue;
                if (_selling && (item.sellPrice <= 0 || item.Owned <= 0)) continue;
                CreateItemRow(item);
                shown++;
            }

            if (shown == 0)
            {
                var empty = CreateText("Empty", _listContent, 26,
                    new Color(1f, 1f, 1f, 0.5f), VNLocale.T("shop.empty"));
                var rect = (RectTransform)empty.transform;
                var element = empty.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 60f;
            }
        }

        void CreateItemRow(VNShopDef.Item item)
        {
            var row = CreateImage("Item_" + item.id, _listContent,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.05f));
            row.GetComponent<Image>().type = Image.Type.Sliced;
            var element = row.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 76f;

            // 图标（缺省色块）
            var icon = CreateImage("Icon", row,
                item.icon != null ? item.icon : VNProceduralTextures.RoundedRectSprite,
                item.icon != null ? Color.white : new Color(0.5f, 0.65f, 1f, 0.5f));
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.anchoredPosition = new Vector2(14f, 0f);
            icon.sizeDelta = new Vector2(52f, 52f);
            icon.GetComponent<Image>().preserveAspect = true;

            // 名称 + 描述 + 持有数
            var name = CreateText("Name", row, 27, new Color(0.95f, 0.96f, 1f, 1f), "");
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.richText = true;
            var nameRect = (RectTransform)name.transform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(0.62f, 1f);
            nameRect.offsetMin = new Vector2(80f, 0f);
            nameRect.offsetMax = Vector2.zero;
            string owned = item.Owned > 0
                ? $"  <size=22><color=#8ef5a2>{VNLocale.T("shop.owned", item.Owned)}</color></size>"
                : "";
            string desc = string.IsNullOrEmpty(item.LocalizedDescription)
                ? ""
                : $"\n<size=20><color=#a8aab8>{item.LocalizedDescription}</color></size>";
            name.text = $"{item.DisplayName}{owned}{desc}";

            // 价格
            bool isSell = _selling;
            int price = isSell ? item.sellPrice : item.price;
            bool canDo = isSell
                ? item.Owned > 0
                : Money >= price && (item.maxOwned <= 0 || item.Owned < item.maxOwned);
            var priceText = CreateText("Price", row, 26,
                canDo ? new Color(1f, 0.84f, 0.42f, 1f) : new Color(1f, 0.42f, 0.42f, 0.9f),
                (isSell ? "+" : "-") + FormatMoney(price));
            priceText.alignment = TextAlignmentOptions.MidlineRight;
            priceText.fontStyle = FontStyles.Bold;
            var priceRect = (RectTransform)priceText.transform;
            priceRect.anchorMin = new Vector2(0.62f, 0f);
            priceRect.anchorMax = new Vector2(0.8f, 1f);
            priceRect.offsetMin = Vector2.zero;
            priceRect.offsetMax = Vector2.zero;

            // 买/卖按钮
            var action = CreateButton(
                VNLocale.T(isSell ? "shop.sell" : "shop.buy"), row,
                () => { if (isSell) Sell(item); else Buy(item); });
            var actionRect = (RectTransform)action.transform;
            actionRect.anchorMin = new Vector2(1f, 0.5f);
            actionRect.anchorMax = new Vector2(1f, 0.5f);
            actionRect.pivot = new Vector2(1f, 0.5f);
            actionRect.anchoredPosition = new Vector2(-14f, 0f);
            actionRect.sizeDelta = new Vector2(110f, 48f);
            var button = action.GetComponent<Button>();
            button.interactable = canDo;
            if (!canDo)
                action.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.2f, 0.7f);
        }

        // ------------------------------------------------------------------
        // 共用小件
        // ------------------------------------------------------------------

        static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return rect;
        }

        TextMeshProUGUI CreateText(string name, RectTransform parent, int size,
            Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        GameObject CreateButton(string label, RectTransform parent,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = TabNormal;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", rect, 24, new Color(0.95f, 0.96f, 1f, 1f), label);
            var textRect = (RectTransform)text.transform;
            Stretch(textRect);
            return go;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
