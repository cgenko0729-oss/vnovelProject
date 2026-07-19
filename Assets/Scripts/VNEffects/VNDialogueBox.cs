using DG.Tweening;
using TMPro;
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
    ///
    /// 【皮肤系统】外观有两条路：
    ///   1. 程序化默认（零素材兜底）：Build() 运行时拼 UI，行为与老版本完全一致；
    ///   2. 皮肤 prefab：ApplySkin(VNDialogueSkin) 实例化美术 prefab 并按槽位绑定。
    /// 两条路殊途同归——程序化构建的结果也装进一个 VNDialogueSkin（DefaultSkin 子物体），
    /// 之后所有行为（打字机/名牌/头像/箭头/出入场）只认 Bind() 到的槽位引用，
    /// 不关心它们来自代码还是美术资产。剧本 `ui dialogue &lt;id&gt;` 经 VNStage 调到这里。
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

        // ---- 当前绑定的皮肤槽位（默认 = DefaultSkin 子物体上的程序化构建结果）----
        VNDialogueSkin _skin;          // 当前生效的槽位声明
        GameObject _defaultRoot;       // 程序化默认皮肤的容器（切自定义皮肤时隐藏不销毁）
        GameObject _customRoot;        // 自定义皮肤实例（切回默认时销毁）
        TMP_Text _nameText;
        GameObject _nameTag;
        VNTypewriterText _typer;
        Graphic _arrow;
        RectTransform _arrowRect;
        RectTransform _portraitWindow;
        Image _portraitImage;
        RectTransform _bodyRect;       // 正文矩形（头像避让用）
        RectTransform _tagRect;        // 名牌矩形（头像避让用）
        Vector2 _bodyBaseOffsetMin;    // 无头像时的正文左下偏移（绑定时采样）
        Vector2 _tagBasePos;           // 无头像时的名牌位置（绑定时采样）
        float _portraitBodyInset;      // 皮肤声明：头像显示时正文额外左缩进
        float _portraitTagShift;       // 皮肤声明：头像显示时名牌额外右移
        RectTransform _animRect;       // 出入场动画作用对象（skin.panel 或根）
        Vector2 _animBasePos;

        // ---- 根矩形原始值（自定义皮肤要铺满画布，切回默认时还原）----
        Vector2 _origAnchorMin, _origAnchorMax, _origPivot, _origAnchoredPos, _origSizeDelta;
        bool _origSaved;

        bool _portraitEnabled = true;
        Sprite _portraitSprite;
        float _portraitScale = 1f;
        Vector2 _portraitOffset;

        float _arrowBaseY;
        bool _shown;
        bool _interfaceVisible = true;
        Tween _arrowBob;
        bool _built;
        string _lastSpeaker;
        string _lastContent;
        float _textSpeed = 18f;

        public bool IsShown => _shown;
        public bool IsTyping => _typer != null && _typer.IsTyping;
        public float TextSpeed => _typer != null ? _typer.charsPerSecond : _textSpeed;
        /// <summary>当前是否在用自定义皮肤 prefab（false = 程序化默认样式）</summary>
        public bool HasCustomSkin => _customRoot != null;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            _root = (RectTransform)transform;
            SaveRootRect();

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            BuildDefaultSkin();
            Bind(_defaultRoot.GetComponent<VNDialogueSkin>());
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 程序化默认皮肤：把老版本的运行时构建装进 DefaultSkin 容器 +
        /// VNDialogueSkin 槽位声明，与美术 prefab 走完全相同的绑定路径。
        /// </summary>
        void BuildDefaultSkin()
        {
            _defaultRoot = new GameObject("DefaultSkin", typeof(RectTransform));
            var defaultRect = (RectTransform)_defaultRoot.transform;
            defaultRect.SetParent(transform, false);
            Stretch(defaultRect);
            var skin = _defaultRoot.AddComponent<VNDialogueSkin>();

            // ---- 半透明磨砂面板 ----
            var panel = CreateChildImage(defaultRect, "Panel",
                VNProceduralTextures.RoundedRectSprite, panelColor);
            panel.type = Image.Type.Sliced;

            // ---- 边缘流光框 ----
            var frame = CreateChildImage(defaultRect, "Frame",
                VNProceduralTextures.RoundedFrameSprite, frameColor);
            frame.type = Image.Type.Sliced;
            skin.shineFrame = frame;

            // ---- 名牌（骑在面板顶边上）----
            var nameTagGo = new GameObject("NameTag", typeof(RectTransform));
            var tagRect = (RectTransform)nameTagGo.transform;
            tagRect.SetParent(defaultRect, false);
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0f, 1f);
            tagRect.pivot = new Vector2(0f, 0.5f);
            tagRect.anchoredPosition = new Vector2(44f, 4f);
            tagRect.sizeDelta = new Vector2(210f, 50f);

            var tagBg = CreateChildImage(tagRect, "Bg",
                VNProceduralTextures.RoundedRectSprite, nameTagColor);
            tagBg.type = Image.Type.Sliced;

            var nameText = CreateChildText(tagRect, "Name", 26, TextAlignmentOptions.Center);
            nameText.fontStyle = FontStyles.Bold;
            skin.nameTag = nameTagGo;
            skin.nameText = nameText;

            // ---- 正文（打字机）----
            var body = CreateChildText(defaultRect, "Body", 28, TextAlignmentOptions.TopLeft);
            var bodyRect = (RectTransform)body.transform;
            bodyRect.offsetMin = new Vector2(40f, 26f);
            bodyRect.offsetMax = new Vector2(-40f, -40f);
            body.lineSpacing = 25f; // TMP 行距单位为字号百分比，25 ≈ legacy 的 1.25 倍行距
            skin.bodyText = body;

            // ---- 说话者头像（左侧裁切窗口，可高出面板顶边）----
            var winGo = new GameObject("PortraitWindow", typeof(RectTransform), typeof(RectMask2D));
            var winRect = (RectTransform)winGo.transform;
            winRect.SetParent(defaultRect, false);
            winRect.anchorMin = winRect.anchorMax = Vector2.zero;
            winRect.pivot = Vector2.zero;
            winRect.anchoredPosition = new Vector2(14f, 12f);
            winRect.sizeDelta = portraitWindowSize;

            var portraitGo = new GameObject("Portrait",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var portraitRect = (RectTransform)portraitGo.transform;
            portraitRect.SetParent(winRect, false);
            portraitRect.anchorMin = portraitRect.anchorMax = new Vector2(0.5f, 1f);
            portraitRect.pivot = new Vector2(0.5f, 1f);
            var portraitImage = portraitGo.GetComponent<Image>();
            portraitImage.raycastTarget = false;
            winGo.SetActive(false);
            skin.portraitWindow = winRect;
            skin.portraitImage = portraitImage;

            // ---- 继续箭头 ----
            var arrow = CreateChildText(defaultRect, "Arrow", 26, TextAlignmentOptions.Center);
            arrow.text = "▼";
            var arrowRect = (RectTransform)arrow.transform;
            arrowRect.anchorMin = arrowRect.anchorMax = new Vector2(1f, 0f);
            arrowRect.pivot = new Vector2(0.5f, 0.5f);
            arrowRect.anchoredPosition = new Vector2(-38f, 26f);
            arrowRect.sizeDelta = new Vector2(40f, 34f);
            skin.arrow = arrow;
            // 头像避让量与老版本一致（窗口宽 + 14px 间距）
            skin.portraitBodyInset = portraitWindowSize.x + 14f;
            skin.portraitTagShift = portraitWindowSize.x + 14f;
            // panel 留空：默认皮肤沿用老行为，出入场动画作用于整个对话框根
        }

        // ------------------------------------------------------------------
        // 皮肤绑定与切换
        // ------------------------------------------------------------------

        /// <summary>
        /// 切换皮肤：skinPrefab = null 回程序化默认样式。
        /// 台词中途切换安全——当前句会在新皮肤上以满字重现（不重打字）。
        /// </summary>
        public void ApplySkin(VNDialogueSkin skinPrefab)
        {
            Build();
            _textSpeed = TextSpeed; // 记住当前文字速度，带到新皮肤的打字机上
            HideArrow();
            DOTween.Kill(this);

            if (_customRoot != null)
            {
                Destroy(_customRoot);
                _customRoot = null;
            }

            if (skinPrefab == null)
            {
                _defaultRoot.SetActive(true);
                RestoreRootRect();
                Bind(_defaultRoot.GetComponent<VNDialogueSkin>());
            }
            else
            {
                _defaultRoot.SetActive(false);
                StretchRootToCanvas(); // 皮肤按全画布坐标制作：panel 想放哪就锚哪
                _customRoot = Instantiate(skinPrefab.gameObject, transform);
                _customRoot.name = "Skin_" + skinPrefab.gameObject.name;
                var instRect = (RectTransform)_customRoot.transform;
                Stretch(instRect);
                _customRoot.SetActive(true);
                Bind(_customRoot.GetComponent<VNDialogueSkin>());
            }

            DockToolbar();
            RestoreVisualState();
        }

        /// <summary>把行为逻辑接到皮肤槽位上（默认与自定义皮肤共用此路径）</summary>
        void Bind(VNDialogueSkin skin)
        {
            _skin = skin;
            _nameTag = skin.nameTag;
            _nameText = skin.nameText;
            _arrow = skin.arrow;
            _arrowRect = _arrow != null ? _arrow.rectTransform : null;
            _arrowBaseY = _arrowRect != null ? _arrowRect.anchoredPosition.y : 0f;
            _portraitWindow = skin.portraitWindow;
            _portraitImage = skin.portraitImage;

            // 头像避让：皮肤声明缩进量（0 = 不避让），基准位置在绑定时采样
            _portraitBodyInset = skin.portraitBodyInset;
            _portraitTagShift = skin.portraitTagShift;
            _bodyRect = skin.bodyText != null ? (RectTransform)skin.bodyText.transform : null;
            _tagRect = _nameTag != null ? (RectTransform)_nameTag.transform : null;
            _bodyBaseOffsetMin = _bodyRect != null ? _bodyRect.offsetMin : Vector2.zero;
            _tagBasePos = _tagRect != null ? _tagRect.anchoredPosition : Vector2.zero;

            // 打字机：挂到皮肤的正文上，速度沿用之前的设置
            _typer = null;
            if (skin.bodyText != null)
            {
                _typer = skin.bodyText.GetComponent<VNTypewriterText>();
                if (_typer == null)
                    _typer = skin.bodyText.gameObject.AddComponent<VNTypewriterText>();
                _typer.charsPerSecond = _textSpeed;
            }

            // 流光边框：皮肤给了槽位才有（材质由 VNImageEffectController 自动实例化）
            if (skin.shineFrame != null)
            {
                var fx = skin.shineFrame.GetComponent<VNImageEffectController>();
                if (fx == null)
                {
                    fx = skin.shineFrame.gameObject.AddComponent<VNImageEffectController>();
                    fx.SetShineStyle(0.22f, 20f, new Color(2.2f, 1.9f, 1.1f, 0.95f));
                    fx.StartShineLoop(2.2f, 1.3f);
                }
            }

            // 出入场动画对象：skin.panel 优先，否则整个对话框根
            _animRect = skin.panel != null ? skin.panel : _root;
            _animBasePos = _animRect.anchoredPosition;

            if (_arrow != null) SetArrowAlpha(0f);
            ApplyPortrait();
        }

        /// <summary>皮肤切换后恢复可视状态：正在显示的台词满字重现</summary>
        void RestoreVisualState()
        {
            _group.alpha = _shown && _interfaceVisible ? 1f : 0f;
            _group.blocksRaycasts = _shown && _interfaceVisible;
            _animRect.anchoredPosition = _animBasePos;
            if (_shown && _lastContent != null && _typer != null)
            {
                bool hasName = !string.IsNullOrEmpty(_lastSpeaker);
                if (_nameTag != null) _nameTag.SetActive(hasName);
                if (hasName && _nameText != null) _nameText.text = _lastSpeaker;
                _typer.onComplete = ShowArrow;
                _typer.Play(_lastContent);
                _typer.Complete(); // 满字直出，不重播打字
            }
        }

        /// <summary>快捷功能条停靠：皮肤 toolbarAnchor > 皮肤 panel > 对话框根（老位置）</summary>
        void DockToolbar()
        {
            var toolbar = GetComponent<VNQuickToolbar>();
            if (toolbar == null) return;
            RectTransform dock = null;
            if (_skin != null)
                dock = _skin.toolbarAnchor != null ? _skin.toolbarAnchor : _skin.panel;
            toolbar.SetDock(dock); // null = 挂回对话框根
        }

        void SaveRootRect()
        {
            if (_origSaved) return;
            _origSaved = true;
            _origAnchorMin = _root.anchorMin;
            _origAnchorMax = _root.anchorMax;
            _origPivot = _root.pivot;
            _origAnchoredPos = _root.anchoredPosition;
            _origSizeDelta = _root.sizeDelta;
        }

        void StretchRootToCanvas()
        {
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = Vector2.zero;
        }

        void RestoreRootRect()
        {
            _root.anchorMin = _origAnchorMin;
            _root.anchorMax = _origAnchorMax;
            _root.pivot = _origPivot;
            _root.anchoredPosition = _origAnchoredPos;
            _root.sizeDelta = _origSizeDelta;
        }

        // ------------------------------------------------------------------
        // 构建小工具
        // ------------------------------------------------------------------

        Image CreateChildImage(RectTransform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        TextMeshProUGUI CreateChildText(RectTransform parent, string name, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(1f, 1f, 1f, 0.96f);
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        void SetArrowAlpha(float a)
        {
            if (_arrow == null) return;
            var c = _arrow.color;
            c.a = a;
            _arrow.color = c;
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
                _animRect.anchoredPosition = _animBasePos;
                return;
            }
            _animRect.anchoredPosition = _animBasePos + new Vector2(0f, -70f);
            _group.DOFade(1f, duration * 0.7f).SetTarget(this).SetLink(gameObject);
            _animRect.DOAnchorPos(_animBasePos, duration).SetEase(Ease.OutBack, 1.2f)
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
            _animRect.DOAnchorPos(_animBasePos + new Vector2(0f, -50f), duration).SetEase(Ease.InQuad)
                     .SetTarget(this).SetLink(gameObject);
        }

        /// <summary>说一句话：设置名牌 + 打字机播放正文，播完自动亮出继续箭头</summary>
        public void Say(string speakerName, string content)
        {
            Build();
            if (!_shown) Show();

            _lastSpeaker = speakerName;
            _lastContent = content;

            bool hasName = !string.IsNullOrEmpty(speakerName);
            if (_nameTag != null) _nameTag.SetActive(hasName);
            if (hasName && _nameText != null) _nameText.text = speakerName;

            HideArrow();
            if (_typer == null) return; // 皮肤没给正文槽：静默容错（Lint/日志层面提示）
            _typer.onComplete = ShowArrow;
            _typer.Play(content);
        }

        /// <summary>玩家催促：立即显示全部文字</summary>
        public void CompleteTyping() => _typer?.Complete();

        /// <summary>Config 面板设置打字速度，立即作用于当前和后续台词。</summary>
        public void SetTextSpeed(float charsPerSecond)
        {
            Build();
            _textSpeed = Mathf.Clamp(charsPerSecond, 8f, 60f);
            if (_typer != null) _typer.charsPerSecond = _textSpeed;
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
            if (_shown) _animRect.anchoredPosition = _animBasePos;
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
            // 皮肤没给头像窗 = 该皮肤不显示头像（槽位可选的降级约定）
            if (_portraitWindow == null || _portraitImage == null) return;

            bool show = _portraitEnabled && _portraitSprite != null;
            _portraitWindow.gameObject.SetActive(show);

            // 正文与名牌避让头像窗口：缩进量由皮肤声明（0 = 该皮肤排版固定，不避让）
            if (_bodyRect != null && _portraitBodyInset > 0f)
                _bodyRect.offsetMin = _bodyBaseOffsetMin +
                                      new Vector2(show ? _portraitBodyInset : 0f, 0f);
            if (_tagRect != null && _portraitTagShift > 0f)
                _tagRect.anchoredPosition = _tagBasePos +
                                            new Vector2(show ? _portraitTagShift : 0f, 0f);

            if (!show) return;
            Vector2 windowSize = _portraitWindow.sizeDelta;
            if (windowSize.x <= 0f) windowSize = portraitWindowSize;
            _portraitImage.sprite = _portraitSprite;
            float w = windowSize.x * Mathf.Max(0.05f, _portraitScale);
            float h = _portraitSprite.rect.height / Mathf.Max(1f, _portraitSprite.rect.width) * w;
            _portraitImage.rectTransform.sizeDelta = new Vector2(w, h);
            _portraitImage.rectTransform.anchoredPosition = _portraitOffset;
        }

        void ShowArrow()
        {
            if (_arrow == null || _arrowRect == null) return;
            _arrowBob?.Kill();
            _arrow.DOFade(0.9f, 0.25f).SetLink(gameObject);
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
