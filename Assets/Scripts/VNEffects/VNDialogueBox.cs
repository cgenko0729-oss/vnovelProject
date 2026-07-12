using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 高级对话框：
    ///   - 半透明磨砂质感圆角面板（程序化 9-slice 贴图）
    ///   - 出现时从底部轻弹上来（OutBack）+ 淡入
    ///   - 边缘流光：圆角描边框挂 VN/ImageEffect，扫光循环只点亮边框像素
    ///   - 打字机文字，逐字上浮淡入（VNTypewriterText）
    ///   - 待机时右下角"▼"继续箭头呼吸浮动
    /// 全部运行时程序化构建，挂到 Canvas 下一个空 RectTransform 即可。
    /// 用法：dialogue.Say("少女", "今天的晚霞真漂亮啊……");
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNDialogueBox : MonoBehaviour
    {
        [Tooltip("渲染排序（高于氛围粒子，低于全屏转场）")]
        public int sortingOrder = 40;

        public Color panelColor = new Color(0.05f, 0.07f, 0.13f, 0.78f);
        public Color frameColor = new Color(1f, 0.85f, 0.5f, 0.9f);
        public Color nameTagColor = new Color(0.45f, 0.3f, 0.75f, 0.9f);

        RectTransform _root;
        CanvasGroup _group;
        Text _nameText;
        GameObject _nameTag;
        VNTypewriterText _typer;
        Text _arrowText;
        RectTransform _arrowRect;
        VNImageEffectController _frameFx;

        Vector2 _basePos;
        float _arrowBaseY;
        bool _shown;
        Tween _arrowBob;
        bool _built;

        public bool IsShown => _shown;
        public bool IsTyping => _typer != null && _typer.IsTyping;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            _root = (RectTransform)transform;
            _basePos = _root.anchoredPosition;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ---- 半透明磨砂面板 ----
            var panel = CreateChildImage("Panel", VNProceduralTextures.RoundedRectSprite, panelColor);
            panel.type = Image.Type.Sliced;

            // ---- 边缘流光框：描边贴图 + 扫光循环 ----
            var frame = CreateChildImage("Frame", VNProceduralTextures.RoundedFrameSprite, frameColor);
            frame.type = Image.Type.Sliced;
            _frameFx = frame.gameObject.AddComponent<VNImageEffectController>();
            _frameFx.SetShineStyle(0.22f, 20f, new Color(2.2f, 1.9f, 1.1f, 0.95f));
            _frameFx.StartShineLoop(2.2f, 1.3f);

            // ---- 名牌（骑在面板顶边上）----
            _nameTag = new GameObject("NameTag", typeof(RectTransform));
            var tagRect = (RectTransform)_nameTag.transform;
            tagRect.SetParent(transform, false);
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 0.5f);
            tagRect.anchoredPosition = new Vector2(44f, 4f);
            tagRect.sizeDelta = new Vector2(210f, 50f);

            var tagBgGo = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var tagBgRect = (RectTransform)tagBgGo.transform;
            tagBgRect.SetParent(tagRect, false);
            tagBgRect.anchorMin = Vector2.zero;
            tagBgRect.anchorMax = Vector2.one;
            tagBgRect.offsetMin = Vector2.zero;
            tagBgRect.offsetMax = Vector2.zero;
            var tagBg = tagBgGo.GetComponent<Image>();
            tagBg.sprite = VNProceduralTextures.RoundedRectSprite;
            tagBg.type = Image.Type.Sliced;
            tagBg.color = nameTagColor;
            tagBg.raycastTarget = false;

            _nameText = CreateChildText(tagRect, "Name", font, 26, TextAnchor.MiddleCenter);
            _nameText.fontStyle = FontStyle.Bold;

            // ---- 正文（打字机）----
            var body = CreateChildText((RectTransform)transform, "Body", font, 28, TextAnchor.UpperLeft);
            var bodyRect = (RectTransform)body.transform;
            bodyRect.anchorMin = Vector2.zero;
            bodyRect.anchorMax = Vector2.one;
            bodyRect.offsetMin = new Vector2(40f, 26f);
            bodyRect.offsetMax = new Vector2(-40f, -40f);
            body.lineSpacing = 1.25f;
            _typer = body.gameObject.AddComponent<VNTypewriterText>();

            // ---- 继续箭头 ----
            var arrow = CreateChildText((RectTransform)transform, "Arrow", font, 26, TextAnchor.MiddleCenter);
            _arrowText = arrow;
            _arrowText.text = "▼";
            _arrowRect = (RectTransform)arrow.transform;
            _arrowRect.anchorMin = _arrowRect.anchorMax = new Vector2(1f, 0f);
            _arrowRect.pivot = new Vector2(0.5f, 0.5f);
            _arrowRect.anchoredPosition = new Vector2(-38f, 26f);
            _arrowRect.sizeDelta = new Vector2(40f, 34f);
            _arrowBaseY = 26f;
            SetArrowAlpha(0f);

            gameObject.SetActive(true);
        }

        Image CreateChildImage(string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        Text CreateChildText(RectTransform parent, string name, Font font, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(1f, 1f, 1f, 0.96f);
            text.raycastTarget = false;
            return text;
        }

        void SetArrowAlpha(float a)
        {
            var c = _arrowText.color;
            c.a = a;
            _arrowText.color = c;
        }

        // ------------------------------------------------------------------

        /// <summary>显示对话框（从底部轻弹上来 + 淡入）</summary>
        public void Show(float duration = 0.45f)
        {
            Build();
            if (_shown) return;
            _shown = true;
            DOTween.Kill(this);
            _root.anchoredPosition = _basePos + new Vector2(0f, -70f);
            _group.DOFade(1f, duration * 0.7f).SetTarget(this).SetLink(gameObject);
            _root.DOAnchorPos(_basePos, duration).SetEase(Ease.OutBack, 1.2f)
                 .SetTarget(this).SetLink(gameObject);
        }

        /// <summary>隐藏对话框（下滑淡出）</summary>
        public void HideBox(float duration = 0.3f)
        {
            if (!_shown) return;
            _shown = false;
            HideArrow();
            DOTween.Kill(this);
            _group.DOFade(0f, duration).SetTarget(this).SetLink(gameObject);
            _root.DOAnchorPos(_basePos + new Vector2(0f, -50f), duration).SetEase(Ease.InQuad)
                 .SetTarget(this).SetLink(gameObject);
        }

        /// <summary>说一句话：设置名牌 + 打字机播放正文，播完自动亮出继续箭头</summary>
        public void Say(string speakerName, string content)
        {
            Build();
            if (!_shown) Show();

            bool hasName = !string.IsNullOrEmpty(speakerName);
            _nameTag.SetActive(hasName);
            if (hasName) _nameText.text = speakerName;

            HideArrow();
            _typer.onComplete = ShowArrow;
            _typer.Play(content);
        }

        /// <summary>玩家催促：立即显示全部文字</summary>
        public void CompleteTyping() => _typer.Complete();

        void ShowArrow()
        {
            _arrowBob?.Kill();
            _arrowText.DOFade(0.9f, 0.25f).SetLink(gameObject);
            _arrowRect.anchoredPosition = new Vector2(_arrowRect.anchoredPosition.x, _arrowBaseY);
            _arrowBob = _arrowRect.DOAnchorPosY(_arrowBaseY - 7f, 0.55f)
                                  .SetEase(Ease.InOutSine)
                                  .SetLoops(-1, LoopType.Yoyo)
                                  .SetLink(gameObject);
        }

        void HideArrow()
        {
            _arrowBob?.Kill();
            _arrowBob = null;
            SetArrowAlpha(0f);
        }

        void OnDestroy()
        {
            _arrowBob?.Kill();
            DOTween.Kill(this);
        }
    }
}
