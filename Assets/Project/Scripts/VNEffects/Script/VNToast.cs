using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 屏幕提示：**左上角堆叠卡片队列**。
    ///   VNToast.Show("已保存")                          —— 中性卡片
    ///   VNToast.Show("智力 +3", icon, 主题色, 增减色)    —— 带图标与色条（属性变动用）
    ///   VNToast.SetMode("AUTO")                         —— 右上角常驻模式标签（null 清除）
    ///
    /// 与旧版（单条居中纯文字）的关键差别：**多条不再互相覆盖**。
    /// 新卡片始终出现在最上面那一格，已有卡片顺次下移；超过上限时最老的提前退场。
    /// 自建独立 Overlay Canvas（排序 999，渲染在一切之上），首次调用时自动创建。
    ///
    /// 计时全部 `SetUpdate(true)`：Skip 快进时 DOTween.timeScale 会被改，
    /// 提示不该跟着变速；事件模块进行中也照常显示。
    /// </summary>
    public static class VNToast
    {
        const int MaxCards = 5;          // 同屏最多几张，超出时最老的立刻开始退场
        const float CardHeight = 58f;
        const float CardGap = 8f;
        const float SlideIn = 0.26f;
        const float SlideOut = 0.28f;
        const float EnterOffsetX = -70f; // 从左侧滑入的起点偏移

        static readonly Color CardBg = new Color(0.08f, 0.09f, 0.14f, 0.93f);
        static readonly Color NeutralAccent = new Color(0.55f, 0.75f, 1f, 1f);

        static Canvas _canvas;
        static RectTransform _stack;
        static TextMeshProUGUI _mode;
        static readonly List<Card> _cards = new List<Card>();

        class Card
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Sequence life;
            public bool leaving;
        }

        // ------------------------------------------------------------------
        // 公开 API
        // ------------------------------------------------------------------

        /// <summary>中性提示卡片（存档/任务/装备等通用）</summary>
        public static void Show(string message, float holdSeconds = 1.6f) =>
            Show(message, null, NeutralAccent, NeutralAccent, holdSeconds);

        /// <summary>
        /// 带图标与色条的提示卡片。
        /// iconColor = 图标底色（属性主题色，用来"认出是哪个属性"）；
        /// accent = 左侧竖条色（增/减色，用来"一眼看出涨还是跌"）。
        /// icon 传 null 时用圆点代替。
        /// </summary>
        public static void Show(string message, Sprite icon, Color iconColor, Color accent,
            float holdSeconds = 1.6f)
        {
            EnsureCanvas();
            if (_cards.Count >= MaxCards) Dismiss(_cards[_cards.Count - 1]);

            var card = BuildCard(message, icon, iconColor, accent);
            _cards.Insert(0, card); // 新卡永远占最上面那一格
            Relayout(false);

            // 滑入 → 停留 → 滑出；滑出结束后销毁并让下面的卡片补位
            card.group.alpha = 0f;
            card.rect.anchoredPosition = new Vector2(EnterOffsetX, card.rect.anchoredPosition.y);
            card.life = DOTween.Sequence()
                .Append(card.rect.DOAnchorPosX(0f, SlideIn).SetEase(Ease.OutCubic))
                .Join(card.group.DOFade(1f, SlideIn))
                .AppendInterval(Mathf.Max(0.2f, holdSeconds))
                .AppendCallback(() => Dismiss(card))
                .SetUpdate(true)
                .SetLink(card.rect.gameObject);
        }

        /// <summary>右上角常驻模式标签（AUTO/SKIP），传 null 或空清除</summary>
        public static void SetMode(string label)
        {
            EnsureCanvas();
            _mode.text = string.IsNullOrEmpty(label) ? "" : label;
        }

        /// <summary>立刻清空所有卡片（调试重建/剧本中断时可用，不清模式标签）</summary>
        public static void ClearAll()
        {
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                _cards[i].life?.Kill();
                if (_cards[i].rect != null) Object.Destroy(_cards[i].rect.gameObject);
            }
            _cards.Clear();
        }

        // ------------------------------------------------------------------
        // 队列
        // ------------------------------------------------------------------

        static void Dismiss(Card card)
        {
            if (card == null || card.leaving) return;
            card.leaving = true;
            card.life?.Kill();
            card.life = DOTween.Sequence()
                .Append(card.rect.DOAnchorPosX(EnterOffsetX, SlideOut).SetEase(Ease.InCubic))
                .Join(card.group.DOFade(0f, SlideOut))
                .AppendCallback(() =>
                {
                    _cards.Remove(card);
                    if (card.rect != null) Object.Destroy(card.rect.gameObject);
                    Relayout(true);
                })
                .SetUpdate(true)
                .SetLink(card.rect.gameObject);
        }

        /// <summary>按当前顺序把卡片排到各自的格子上（animate=false 用于刚插入的新卡）</summary>
        static void Relayout(bool animate)
        {
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (card.rect == null) continue;
                float y = -i * (CardHeight + CardGap);
                if (Mathf.Approximately(card.rect.anchoredPosition.y, y)) continue;

                if (animate && !card.leaving)
                    card.rect.DOAnchorPosY(y, 0.22f).SetEase(Ease.OutCubic)
                            .SetUpdate(true).SetLink(card.rect.gameObject);
                else
                    card.rect.anchoredPosition =
                        new Vector2(card.rect.anchoredPosition.x, y);
            }
        }

        // ------------------------------------------------------------------
        // 程序化 UI
        // ------------------------------------------------------------------

        static void EnsureCanvas()
        {
            if (_canvas != null) return;

            var go = new GameObject("VNToastCanvas", typeof(Canvas), typeof(CanvasScaler));
            Object.DontDestroyOnLoad(go);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 999;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // 卡片栈：左上角起，向下生长
            var stackGo = new GameObject("Stack", typeof(RectTransform));
            _stack = (RectTransform)stackGo.transform;
            _stack.SetParent(go.transform, false);
            _stack.anchorMin = _stack.anchorMax = new Vector2(0f, 1f);
            _stack.pivot = new Vector2(0f, 1f);
            _stack.anchoredPosition = new Vector2(34f, -30f);
            _stack.sizeDelta = Vector2.zero;

            _mode = CreateText(go.transform, 30, TextAlignmentOptions.TopRight);
            _mode.fontStyle = FontStyles.Bold;
            var mr = (RectTransform)_mode.transform;
            mr.anchorMin = mr.anchorMax = new Vector2(1f, 1f);
            mr.pivot = new Vector2(1f, 1f);
            mr.anchoredPosition = new Vector2(-36f, -24f);
            mr.sizeDelta = new Vector2(300f, 44f);
            _mode.color = new Color(1f, 0.85f, 0.4f, 0.9f);
            _mode.text = "";
        }

        static Card BuildCard(string message, Sprite icon, Color iconColor, Color accent)
        {
            var go = new GameObject("ToastCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(CanvasGroup), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_stack, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(0f, CardHeight);

            var bg = go.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = CardBg;
            bg.raycastTarget = false;

            // 宽度跟着文字走（短提示不会拖出一条长条）
            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 22, 0, 0);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            // 左侧竖色条：增/减一眼可辨
            var bar = CreateImage("Accent", rect, null, accent);
            AddLayoutSize(bar.gameObject, 5f, 34f);

            // 图标：属性有图用图，没有用圆点染主题色
            var iconRect = CreateImage("Icon", rect,
                icon != null ? icon : VNProceduralTextures.RadialGlowSprite,
                icon != null ? Color.white : iconColor);
            iconRect.GetComponent<Image>().preserveAspect = true;
            AddLayoutSize(iconRect.gameObject, 30f, 30f);

            var text = CreateText(rect, 27, TextAlignmentOptions.MidlineLeft);
            text.text = message;
            var textElement = text.gameObject.AddComponent<LayoutElement>();
            // GetPreferredValues 当场就能算（preferredWidth 要等一次布局才有值）
            textElement.preferredWidth =
                Mathf.Clamp(text.GetPreferredValues(message).x + 4f, 60f, 520f);
            textElement.preferredHeight = CardHeight;

            var card = new Card
            {
                rect = rect,
                group = go.GetComponent<CanvasGroup>(),
            };
            card.group.blocksRaycasts = false;
            card.group.interactable = false;
            return card;
        }

        static void AddLayoutSize(GameObject go, float width, float height)
        {
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            element.flexibleWidth = 0f;
        }

        static RectTransform CreateImage(string name, RectTransform parent, Sprite sprite,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
            }
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static TextMeshProUGUI CreateText(Transform parent, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.95f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }
    }
}
