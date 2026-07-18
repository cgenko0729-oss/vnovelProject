using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 回想（Backlog）：记录已读台词，H 键 / 滚轮上滑打开全屏回想面板，
    /// 滚轮浏览，H/Esc/点击背景关闭。打开期间剧情推进被阻止。
    /// UI 全程序化构建在独立 Overlay Canvas 上（首次打开时创建）。
    /// </summary>
    public class VNBacklog : MonoBehaviour
    {
        [Header("最多保留的台词条数")]
        public int maxEntries = 200;

        struct Entry
        {
            public string name;
            public string text;
        }

        readonly List<Entry> _entries = new List<Entry>();

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        bool _open;

        public bool IsOpen => _open;

        /// <summary>记录一条台词（VNScriptRunner 在每句 say 时调用）</summary>
        public void Record(string displayName, string text)
        {
            _entries.Add(new Entry { name = displayName, text = text });
            if (_entries.Count > maxEntries)
                _entries.RemoveAt(0);
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
            // 滚动到最新一条
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        public void Close()
        {
            if (!_open) return;
            _panel.SetActive(false);
            _open = false;
        }

        // ------------------------------------------------------------------

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNBacklogCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);

            // 半透明暗底（点击关闭）
            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(panelRect, false);
            Stretch(dimRect);
            var dim = dimGo.GetComponent<Image>();
            dim.color = new Color(0f, 0f, 0.02f, 0.86f);
            dimGo.GetComponent<Button>().onClick.AddListener(Close);

            // 标题
            var title = CreateText(panelRect, 34, TextAlignmentOptions.Center);
            title.text = "—— 回想 ——";
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            // 滚动区域
            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(panelRect, false);
            scrollRect.anchorMin = new Vector2(0.12f, 0.08f);
            scrollRect.anchorMax = new Vector2(0.88f, 0.9f);
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
            layout.spacing = 18f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRect;
            _scroll.content = _content;

            _panel.SetActive(false);
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            foreach (var e in _entries)
            {
                var t = CreateText(_content, 28, TextAlignmentOptions.TopLeft);
                t.richText = true;
                string name = string.IsNullOrEmpty(e.name)
                    ? "" : $"<color=#ffd27f><b>{e.name}</b></color>　";
                t.text = name + e.text;
                t.textWrappingMode = TextWrappingModes.Normal;
                t.overflowMode = TextOverflowModes.Overflow;
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
            t.lineSpacing = 15f; // TMP 行距为字号百分比，15 ≈ legacy 1.15 倍
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
