using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 日记本（D 键）—— 翻看主角写下的、和她相处过的每一天。
    ///
    /// 数据来自 <see cref="VNAiDiary"/>（全局存储，读旧档也不丢）。
    /// 面板全程序化生成，无预制体依赖；与任务日志同层（sortingOrder 600），
    /// 同一时刻只会开一个。
    ///
    /// 【和回想（Backlog）的区别】
    /// 回想是「这一场对话的原文」，日记是「这段关系的记录」——
    /// 后者跨存档累积、有主角的主观视角，是给玩家看的收藏品而不是查询工具。
    /// </summary>
    public class VNAiDiaryPanel : MonoBehaviour
    {
        static readonly Color PaperColor = new Color(0.13f, 0.11f, 0.10f, 0.97f);
        static readonly Color AccentColor = new Color(1f, 0.78f, 0.45f, 1f);
        static readonly Color MetaColor = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color BodyColor = new Color(1f, 0.97f, 0.92f, 0.93f);
        static readonly Color TagColor = new Color(0.55f, 0.8f, 1f, 0.85f);

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        TextMeshProUGUI _title;
        ScrollRect _scroll;
        bool _open;

        /// <summary>当前筛选的角色 id（空 = 全部）</summary>
        string _filter;

        public bool IsOpen => _open;

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            Build();
            Rebuild();
            _panel.SetActive(true);
            _open = true;

            // 轻微淡入 + 上浮，别让它硬邦邦地弹出来
            var rect = (RectTransform)_panel.transform;
            rect.DOKill();
            var group = _panel.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.DOFade(1f, 0.2f).SetUpdate(true).SetLink(gameObject);
        }

        public void Close()
        {
            if (_panel != null) _panel.SetActive(false);
            _open = false;
        }

        // ──────────────── 内容 ────────────────

        void Rebuild()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            var all = VNAiDiary.For(_filter);
            _title.text = all.Count > 0
                ? string.Format(VNLocale.T("diary.title"), all.Count)
                : VNLocale.T("diary.titleEmpty");

            // 角色筛选标签（只有多于一个角色时才显示）
            var characters = VNAiDiary.Characters();
            if (characters.Count > 1) BuildFilterRow(characters);

            if (all.Count == 0)
            {
                var empty = CreateText(_content, 26, MetaColor, VNLocale.T("diary.empty"));
                empty.alignment = TextAlignmentOptions.Center;
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 160f;
                return;
            }

            foreach (var e in all) BuildEntry(e);
        }

        void BuildFilterRow(List<string> characters)
        {
            var row = new GameObject("Filters",
                typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)row.transform;
            rect.SetParent(_content, false);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            row.AddComponent<LayoutElement>().minHeight = 44f;

            MakeTab(rect, VNLocale.T("diary.all"), string.IsNullOrEmpty(_filter), null);
            foreach (string id in characters)
                MakeTab(rect, id, _filter == id, id);
        }

        void MakeTab(RectTransform parent, string label, bool active, string target)
        {
            var go = new GameObject("Tab_" + label,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = VNProceduralTextures.RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = active ? new Color(1f, 0.78f, 0.45f, 0.25f) : new Color(1f, 1f, 1f, 0.07f);
            go.AddComponent<LayoutElement>().minWidth = 130f;

            var t = CreateText(rect, 22, active ? AccentColor : MetaColor, label);
            Stretch((RectTransform)t.transform);
            t.alignment = TextAlignmentOptions.Center;

            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                _filter = target;
                Rebuild();
            });
        }

        void BuildEntry(VNAiDiaryEntry e)
        {
            // 一条 = 一张「纸」
            var card = new GameObject("Entry",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)card.transform;
            rect.SetParent(_content, false);

            var img = card.GetComponent<Image>();
            img.sprite = VNProceduralTextures.RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = PaperColor;

            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(26, 26, 20, 20);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            card.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // 抬头：日期 · 地点 · 对象
            var head = new List<string> { e.savedAt };
            if (!string.IsNullOrWhiteSpace(e.place)) head.Add(e.place);
            if (!string.IsNullOrWhiteSpace(e.displayName)) head.Add(e.displayName);
            var meta = CreateText(rect, 21, MetaColor, string.Join("　·　", head));
            meta.alignment = TextAlignmentOptions.Left;

            // 正文（主角口吻）——这一条才是主角
            var body = CreateText(rect, 27, BodyColor, e.body);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 12f;

            // 页脚：话题标签 + 好感变化
            var foot = new List<string>();
            if (e.topics != null && e.topics.Count > 0)
                foot.Add("# " + string.Join("　# ", e.topics));
            if (e.affectionDelta != 0)
                foot.Add(string.Format(VNLocale.T("diary.affection"),
                    e.affectionDelta.ToString("+#;-#;0")));
            if (foot.Count > 0)
            {
                var tags = CreateText(rect, 20, TagColor, string.Join("　　", foot));
                tags.alignment = TextAlignmentOptions.Left;
            }
        }

        // ──────────────── 面板骨架 ────────────────

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNAiDiaryCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600;   // 与任务日志/回想同层，同时只开一个
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);

            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            Stretch((RectTransform)dimGo.transform);
            dimGo.transform.SetParent(panelRect, false);
            Stretch((RectTransform)dimGo.transform);
            dimGo.GetComponent<Image>().color = new Color(0.02f, 0.015f, 0.01f, 0.9f);
            dimGo.GetComponent<Button>().onClick.AddListener(Close);

            _title = CreateText(panelRect, 34, AccentColor, "");
            _title.fontStyle = FontStyles.Bold;
            _title.alignment = TextAlignmentOptions.Center;
            var titleRect = (RectTransform)_title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            var hint = CreateText(panelRect, 20, MetaColor, VNLocale.T("diary.hint"));
            hint.alignment = TextAlignmentOptions.Center;
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 18f);
            hintRect.sizeDelta = new Vector2(0f, 30f);

            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(panelRect, false);
            scrollRect.anchorMin = new Vector2(0.22f, 0.09f);
            scrollRect.anchorMax = new Vector2(0.78f, 0.9f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            _scroll = scrollGo.GetComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.scrollSensitivity = 45f;

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
            // ★ 默认 sizeDelta 是 (100,100)：横向拉伸下会比视口宽 100px，
            //   左右各溢出 50px 被 RectMask2D 裁掉（正文左边缺字）。必须清零。
            //   —— 同 VNQuestLog 里记过的那个坑
            _content.sizeDelta = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _scroll.viewport = viewportRect;
            _scroll.content = _content;
            _panel.SetActive(false);
        }

        static TextMeshProUGUI CreateText(RectTransform parent, int size, Color color, string content)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.color = color;
            t.text = content;
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
