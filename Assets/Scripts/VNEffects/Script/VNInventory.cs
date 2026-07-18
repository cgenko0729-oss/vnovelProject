using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 物品栏：持有数全部存 VNFlags（flag 名 = 道具_&lt;id&gt;，商店买入自动 +1，
    /// 剧本也可直接 flag 道具_钥匙 +1 发道具）。本组件只负责 I 键物品栏面板：
    /// 从 flags 反查持有的道具，文案/图标从登记的 VNShopDef 商品清单里找
    /// （未登记的道具用 id 当名字照常显示）。
    /// UI 全程序化构建在独立 Overlay Canvas 上，参照 VNQuestLog。
    /// </summary>
    public class VNInventory : MonoBehaviour
    {
        [Header("道具文案来源（商店定义资产；同 id 取第一个命中的商品条目）")]
        public List<VNShopDef> shops = new List<VNShopDef>();

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        bool _open;

        public bool IsOpen => _open;

        void Awake() => VNLocale.LanguageChanged += OnLanguageChanged;

        void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _content = null;
            _scroll = null;
        }

        /// <summary>按道具 id 找商品条目（跨全部登记商店，找不到返回 null）</summary>
        public VNShopDef.Item FindItem(string id)
        {
            foreach (var shop in shops)
            {
                if (shop == null) continue;
                var item = shop.FindItem(id);
                if (item != null) return item;
            }
            return null;
        }

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open) return;
            Build();
            RebuildList();
            _panel.SetActive(true);
            _open = true;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!_open) return;
            _panel.SetActive(false);
            _open = false;
        }

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNInventoryCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600; // 与任务日志/回想同层：同一时刻只会开一个
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);

            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(panelRect, false);
            Stretch(dimRect);
            dimGo.GetComponent<Image>().color = new Color(0f, 0.01f, 0.02f, 0.86f);
            dimGo.GetComponent<Button>().onClick.AddListener(Close);

            var title = CreateText(panelRect, 34, TextAlignmentOptions.Center);
            title.text = VNLocale.T("inventory.title");
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(panelRect, false);
            scrollRect.anchorMin = new Vector2(0.25f, 0.08f);
            scrollRect.anchorMax = new Vector2(0.75f, 0.9f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 40f;

            var viewportGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(scrollRect, false);
            Stretch(viewportRect);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _content = (RectTransform)contentGo.transform;
            _content.SetParent(viewportRect, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRect;
            _scroll.content = _content;

            _panel.SetActive(false);
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            // 从 flags 反查持有道具（道具_ 前缀且数量 > 0）
            var owned = new List<KeyValuePair<string, int>>();
            foreach (var kv in VNFlags.All)
            {
                if (!kv.Key.StartsWith(VNShopDef.ItemFlagPrefix) || kv.Value <= 0) continue;
                owned.Add(new KeyValuePair<string, int>(
                    kv.Key.Substring(VNShopDef.ItemFlagPrefix.Length), kv.Value));
            }

            if (owned.Count == 0)
            {
                var empty = CreateText(_content, 28, TextAlignmentOptions.Center);
                empty.text = VNLocale.T("inventory.empty");
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                return;
            }

            foreach (var pair in owned)
            {
                var item = FindItem(pair.Key);

                var row = new GameObject("Item_" + pair.Key,
                    typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(_content, false);
                row.GetComponent<LayoutElement>().preferredHeight = 64f;
                var rowRect = (RectTransform)row.transform;

                // 图标（缺省色块）
                var iconGo = new GameObject("Icon", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.SetParent(rowRect, false);
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(4f, 0f);
                iconRect.sizeDelta = new Vector2(48f, 48f);
                var icon = iconGo.GetComponent<Image>();
                icon.sprite = item != null && item.icon != null
                    ? item.icon : VNProceduralTextures.RoundedRectSprite;
                icon.color = item != null && item.icon != null
                    ? Color.white : new Color(0.5f, 0.65f, 1f, 0.5f);
                icon.preserveAspect = true;
                icon.raycastTarget = false;

                var text = CreateText(rowRect, 27, TextAlignmentOptions.MidlineLeft);
                text.richText = true;
                var textRect = (RectTransform)text.transform;
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.offsetMin = new Vector2(68f, 0f);
                textRect.offsetMax = Vector2.zero;
                string display = item != null ? item.DisplayName : pair.Key;
                string desc = item != null && !string.IsNullOrEmpty(item.LocalizedDescription)
                    ? $"\n<size=20><color=#a8aab8>{item.LocalizedDescription}</color></size>"
                    : "";
                text.text = $"{display}  <color=#ffd27f>×{pair.Value}</color>{desc}";
            }
        }

        TextMeshProUGUI CreateText(Transform parent, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.lineSpacing = 15f;
            t.raycastTarget = false;
            return t;
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
