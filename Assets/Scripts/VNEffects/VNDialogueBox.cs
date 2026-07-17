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
        [Header("渲染排序（高于氛围粒子，低于全屏转场）")]
        public int sortingOrder = 40;

        [Header("对话框面板底色")]
        public Color panelColor = new Color(0.05f, 0.07f, 0.13f, 0.78f);
        [Header("流光边框颜色")]
        public Color frameColor = new Color(1f, 0.85f, 0.5f, 0.9f);
        [Header("名牌默认底色（角色定义里的 nameColor 优先）")]
        public Color nameTagColor = new Color(0.45f, 0.3f, 0.75f, 0.9f);

        [Header("头像")]
        [Header("头像窗口尺寸（像素）：窗口外的部分被裁掉，窗口可高出面板顶边（半身像效果）")]
        public Vector2 portraitWindowSize = new Vector2(230f, 300f);

        RectTransform _root;
        CanvasGroup _group;
        Text _nameText;
        GameObject _nameTag;
        RectTransform _tagRect;
        RectTransform _bodyRect;
        VNTypewriterText _typer;
        Text _arrowText;
        RectTransform _arrowRect;
        VNImageEffectController _frameFx;

        RectTransform _portraitWindow;
        Image _portraitImage;
        bool _portraitEnabled = true;
        Sprite _portraitSprite;
        float _portraitScale = 1f;
        Vector2 _portraitOffset;

        Vector2 _basePos;
        float _arrowBaseY;
        bool _shown;
        bool _interfaceVisible = true;
        Tween _arrowBob;
        bool _built;

        public bool IsShown => _shown;
        public bool IsTyping => _typer != null && _typer.IsTyping;
        public float TextSpeed => _typer != null ? _typer.charsPerSecond : 18f;

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
            _tagRect = tagRect;

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
            _bodyRect = bodyRect;

            // ---- 说话者头像（左侧裁切窗口，可高出面板顶边）----
            var winGo = new GameObject("PortraitWindow", typeof(RectTransform), typeof(RectMask2D));
            _portraitWindow = (RectTransform)winGo.transform;
            _portraitWindow.SetParent(transform, false);
            _portraitWindow.anchorMin = _portraitWindow.anchorMax = Vector2.zero;
            _portraitWindow.pivot = Vector2.zero;
            _portraitWindow.anchoredPosition = new Vector2(14f, 12f);
            _portraitWindow.sizeDelta = portraitWindowSize;

            var portraitGo = new GameObject("Portrait",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.SetParent(_portraitWindow, false);
            portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 1f);
            portraitRect.pivot = new Vector2(0.5f, 1f);
            _portraitImage = portraitGo.GetComponent<Image>();
            _portraitImage.raycastTarget = false;
            winGo.SetActive(false);

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
            _group.blocksRaycasts = _interfaceVisible;
            if (!_interfaceVisible)
            {
                _group.alpha = 0f;
                _root.anchoredPosition = _basePos;
                return;
            }
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
            _group.blocksRaycasts = false;
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

        /// <summary>Config 面板设置打字速度，立即作用于当前和后续台词。</summary>
        public void SetTextSpeed(float charsPerSecond)
        {
            Build();
            _typer.charsPerSecond = Mathf.Clamp(charsPerSecond, 8f, 60f);
        }

        /// <summary>
        /// 隐藏/恢复整个对话 UI（包含快捷功能条），不改变当前台词和打字进度。
        /// </summary>
        public void SetInterfaceVisible(bool visible)
        {
            Build();
            if (_interfaceVisible == visible) return;
            _interfaceVisible = visible;
            DOTween.Kill(this);
            _group.alpha = visible && _shown ? 1f : 0f;
            _group.blocksRaycasts = visible && _shown;
            if (_shown) _root.anchoredPosition = _basePos;
        }

        // ------------------------------------------------------------------
        // 说话者头像
        // ------------------------------------------------------------------

        public bool PortraitEnabled => _portraitEnabled;

        /// <summary>头像全局开关（剧本 portrait on/off），立即生效</summary>
        public void SetPortraitEnabled(bool on)
        {
            Build();
            _portraitEnabled = on;
            ApplyPortrait();
        }

        /// <summary>
        /// 设置本句台词的头像（null = 无头像/旁白）。
        /// scale：1 = 图片宽度填满窗口；offset：在窗口内平移图片（框出脸部）。
        /// </summary>
        public void SetPortrait(Sprite sprite, float scale = 1f, Vector2 offset = default)
        {
            Build();
            _portraitSprite = sprite;
            _portraitScale = scale;
            _portraitOffset = offset;
            ApplyPortrait();
        }

        void ApplyPortrait()
        {
            bool show = _portraitEnabled && _portraitSprite != null;
            _portraitWindow.gameObject.SetActive(show);

            // 正文与名牌避让头像窗口
            float inset = show ? portraitWindowSize.x + 14f : 0f;
            _bodyRect.offsetMin = new Vector2(40f + inset, 26f);
            _tagRect.anchoredPosition = new Vector2(44f + inset, 4f);

            if (!show) return;
            _portraitImage.sprite = _portraitSprite;
            float w = portraitWindowSize.x * Mathf.Max(0.05f, _portraitScale);
            float h = _portraitSprite.rect.height / Mathf.Max(1f, _portraitSprite.rect.width) * w;
            _portraitImage.rectTransform.sizeDelta = new Vector2(w, h);
            _portraitImage.rectTransform.anchoredPosition = _portraitOffset;
        }

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
