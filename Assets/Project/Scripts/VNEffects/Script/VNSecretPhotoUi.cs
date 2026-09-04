using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 偷拍模式的界面层（手机相机风）：右上角相机图标 + 取景 HUD
    /// （四角取景角标 / 三分线 / 上缘察觉条 / 左上目标与缩放 / 右上胶卷数 /
    /// 底部中央圆形快门 / 左下退出 / 右下操作提示 / 快门闪白 / 被发现红闪 / 缩略图飞入）。
    ///
    /// 【挂在主 Canvas 下而不是自建 Overlay 画布】
    /// 教程（92）与全屏转场（100）都在主 Canvas 上；Overlay 画布会永远压在它们之上，
    /// 教程的暗幕和挖洞就盖不住快门键。本层嵌套 Canvas 排序 70：
    /// 在事件层（60）之上、教程（92）之下。
    ///
    /// 【射线】除三个按钮外一律 raycastTarget=false（事件层同一条教训：吃掉射线会挡住舞台点击）。
    /// 拖动平移由模式层直接读 Mouse，不经 EventSystem；按下时若指针在本层按钮上则不算拖动。
    /// </summary>
    public class VNSecretPhotoUi : MonoBehaviour
    {
        public const int SortingOrder = 70;

        public const string AnchorIcon = "secretphoto.icon";
        public const string AnchorShutter = "secretphoto.shutter";
        public const string AnchorAlert = "secretphoto.alert";
        public const string AnchorFilm = "secretphoto.film";

        public event Action IconClicked;
        public event Action ShutterClicked;
        public event Action ExitClicked;

        RectTransform _root;
        GraphicRaycaster _raycaster;

        // 图标
        GameObject _iconRoot;
        CanvasGroup _iconGroup;
        Button _iconButton;
        TMP_Text _iconBadge;
        Image _iconOutline;
        Tween _iconPulse;

        // 取景 HUD
        GameObject _hud;
        RectTransform _alertRect;
        Image _alertFill;
        TMP_Text _alertLabel, _alertPercent;
        TMP_Text _targetText, _zoomText, _filmText, _hintText;
        Button _shutterButton, _exitButton;
        RectTransform _shutterRect;
        Image _flash, _redFlash;
        RawImage _thumbFly;
        RectTransform _captureArea;
        Tween _alertPulse;

        static readonly Color Pink = new Color(0.98f, 0.62f, 0.76f, 1f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.55f);

        public RectTransform CaptureArea => _captureArea;
        public GameObject HudRoot => _hud;
        public RectTransform ShutterRect => _shutterRect;

        // ==================================================================
        // 构建
        // ==================================================================

        public void Build(Transform canvasRoot)
        {
            if (_root != null) return;

            var go = new GameObject("VNSecretPhotoUi", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster));
            _root = (RectTransform)go.transform;
            _root.SetParent(canvasRoot, false);
            Stretch(_root);
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortingOrder;
            _raycaster = go.GetComponent<GraphicRaycaster>();

            // 抓图区域：铺满整屏、无图形。VNPhotoCapture 按它算屏幕矩形
            var cap = new GameObject("CaptureArea", typeof(RectTransform));
            _captureArea = (RectTransform)cap.transform;
            _captureArea.SetParent(_root, false);
            Stretch(_captureArea);

            BuildIcon();
            BuildHud();

            VNTutorialAnchors.Register(AnchorIcon, (RectTransform)_iconRoot.transform);
            VNTutorialAnchors.Register(AnchorShutter, _shutterRect);
            VNTutorialAnchors.Register(AnchorAlert, _alertRect);
            VNTutorialAnchors.Register(AnchorFilm, (RectTransform)_filmText.transform);

            _hud.SetActive(false);
            _iconRoot.SetActive(false);
        }

        void OnDestroy()
        {
            VNTutorialAnchors.Unregister(AnchorIcon);
            VNTutorialAnchors.Unregister(AnchorShutter);
            VNTutorialAnchors.Unregister(AnchorAlert);
            VNTutorialAnchors.Unregister(AnchorFilm);
            _iconPulse?.Kill();
            _alertPulse?.Kill();
        }

        /// <summary>
        /// 右上角相机图标：机身（圆角矩形）+ 镜头（环 + 中心点）+ 闪光灯小块 + 右下胶卷数角标。
        /// 位置避开 VNToast 的 AUTO/SKIP 角标（-36,-24）与任务角标（-36,-66）两行。
        /// </summary>
        void BuildIcon()
        {
            _iconRoot = new GameObject("CameraIcon", typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)_iconRoot.transform;
            rect.SetParent(_root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // 再往下让一段：默认系统主题把对话框快捷条摆在右上（约 y -150~-215），
            // 放 -118 会压在它最右那个「隐藏UI」按钮上
            rect.anchoredPosition = new Vector2(-40f, -240f);
            rect.sizeDelta = new Vector2(76f, 60f);
            _iconGroup = _iconRoot.GetComponent<CanvasGroup>();

            // 外描边（粉）→ 机身（深色）
            _iconOutline = MakeImage(rect, "Outline", VNProceduralTextures.RoundedRectSprite,
                new Color(Pink.r, Pink.g, Pink.b, 0.95f), Vector2.zero, new Vector2(70f, 50f), true);
            _iconOutline.type = Image.Type.Sliced;
            var body = MakeImage(rect, "Body", VNProceduralTextures.RoundedRectSprite,
                new Color(0.11f, 0.09f, 0.14f, 0.96f), Vector2.zero, new Vector2(64f, 44f), false);
            body.type = Image.Type.Sliced;
            // 闪光灯小块（左上）
            MakeImage(rect, "Flash", VNProceduralTextures.RoundedRectSprite,
                new Color(0.11f, 0.09f, 0.14f, 0.96f), new Vector2(-18f, 24f), new Vector2(18f, 10f), false)
                .type = Image.Type.Sliced;
            // 镜头：环 + 中心点
            MakeRaw(rect, "Lens", RingTexture, Color.white, Vector2.zero, new Vector2(28f, 28f));
            MakeRaw(rect, "LensDot", DiscTexture, Pink, Vector2.zero, new Vector2(12f, 12f));

            _iconButton = _iconRoot.AddComponent<Button>();
            _iconButton.targetGraphic = _iconOutline;
            var colors = _iconButton.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            _iconButton.colors = colors;
            _iconButton.onClick.AddListener(() => IconClicked?.Invoke());

            // 胶卷数角标（右下）
            _iconBadge = MakeText(rect, 18, TextAlignmentOptions.BottomRight);
            _iconBadge.fontStyle = FontStyles.Bold;
            var br = (RectTransform)_iconBadge.transform;
            br.anchorMin = br.anchorMax = new Vector2(1f, 0f);
            br.pivot = new Vector2(1f, 0f);
            br.anchoredPosition = new Vector2(-2f, -8f);
            br.sizeDelta = new Vector2(60f, 24f);
            _iconBadge.color = new Color(1f, 0.85f, 0.4f, 1f);
        }

        void BuildHud()
        {
            _hud = new GameObject("Viewfinder", typeof(RectTransform));
            var hud = (RectTransform)_hud.transform;
            hud.SetParent(_root, false);
            Stretch(hud);

            // ---- 三分线（很淡）----
            var gridColor = new Color(1f, 1f, 1f, 0.10f);
            for (int i = 1; i <= 2; i++)
            {
                MakeLine(hud, $"GridV{i}", gridColor, new Vector2(i / 3f, 0f), new Vector2(i / 3f, 1f), 2f, true);
                MakeLine(hud, $"GridH{i}", gridColor, new Vector2(0f, i / 3f), new Vector2(1f, i / 3f), 2f, false);
            }

            // ---- 四角取景角标 ----
            var bracket = new Color(1f, 1f, 1f, 0.9f);
            const float inset = 48f, len = 54f, thick = 4f;
            BuildCorner(hud, new Vector2(0f, 1f), new Vector2(inset, -inset), new Vector2(1f, -1f), len, thick, bracket);
            BuildCorner(hud, new Vector2(1f, 1f), new Vector2(-inset, -inset), new Vector2(-1f, -1f), len, thick, bracket);
            BuildCorner(hud, new Vector2(0f, 0f), new Vector2(inset, inset), new Vector2(1f, 1f), len, thick, bracket);
            BuildCorner(hud, new Vector2(1f, 0f), new Vector2(-inset, inset), new Vector2(-1f, 1f), len, thick, bracket);

            // ---- 察觉条（上缘正中）----
            var alertGo = new GameObject("AlertBar", typeof(RectTransform));
            _alertRect = (RectTransform)alertGo.transform;
            _alertRect.SetParent(hud, false);
            _alertRect.anchorMin = _alertRect.anchorMax = new Vector2(0.5f, 1f);
            _alertRect.pivot = new Vector2(0.5f, 1f);
            _alertRect.anchoredPosition = new Vector2(0f, -34f);
            _alertRect.sizeDelta = new Vector2(560f, 40f);

            var alertBg = MakeImage(_alertRect, "Bg", VNProceduralTextures.RoundedRectSprite,
                new Color(0f, 0f, 0f, 0.55f), Vector2.zero, new Vector2(560f, 40f), false);
            alertBg.type = Image.Type.Sliced;

            _alertLabel = MakeText(_alertRect, 20, TextAlignmentOptions.MidlineLeft);
            _alertLabel.text = VNLocale.T("secretphoto.alert");
            _alertLabel.color = Dim;
            var lr = (RectTransform)_alertLabel.transform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 1f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(16f, 0f);
            lr.sizeDelta = new Vector2(90f, 0f);

            _alertPercent = MakeText(_alertRect, 20, TextAlignmentOptions.MidlineRight);
            _alertPercent.color = Dim;
            var pr = (RectTransform)_alertPercent.transform;
            pr.anchorMin = new Vector2(1f, 0f); pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(1f, 0.5f);
            pr.anchoredPosition = new Vector2(-16f, 0f);
            pr.sizeDelta = new Vector2(70f, 0f);

            var track = MakeImage(_alertRect, "Track", VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.12f), Vector2.zero, Vector2.zero, false);
            track.type = Image.Type.Sliced;
            var tr = track.rectTransform;
            tr.anchorMin = new Vector2(0f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f);
            tr.pivot = new Vector2(0f, 0.5f);
            tr.offsetMin = new Vector2(100f, -7f); tr.offsetMax = new Vector2(-84f, 7f);

            _alertFill = MakeImage(tr, "Fill", VNProceduralTextures.RoundedRectSprite,
                Pink, Vector2.zero, Vector2.zero, false);
            _alertFill.type = Image.Type.Filled;
            _alertFill.fillMethod = Image.FillMethod.Horizontal;
            _alertFill.fillOrigin = 0;
            _alertFill.fillAmount = 0f;
            Stretch(_alertFill.rectTransform);

            // ---- 左上：目标 + 缩放 ----
            _targetText = MakeText(hud, 24, TextAlignmentOptions.TopLeft);
            PlaceCorner((RectTransform)_targetText.transform, new Vector2(0f, 1f), new Vector2(64f, -92f), new Vector2(520f, 34f));
            _zoomText = MakeText(hud, 26, TextAlignmentOptions.TopLeft);
            _zoomText.fontStyle = FontStyles.Bold;
            _zoomText.color = new Color(1f, 1f, 1f, 0.92f);
            PlaceCorner((RectTransform)_zoomText.transform, new Vector2(0f, 1f), new Vector2(64f, -128f), new Vector2(200f, 36f));

            // ---- 右上：胶卷 ----
            _filmText = MakeText(hud, 24, TextAlignmentOptions.TopRight);
            _filmText.color = new Color(1f, 0.85f, 0.4f, 1f);
            PlaceCorner((RectTransform)_filmText.transform, new Vector2(1f, 1f), new Vector2(-64f, -92f), new Vector2(300f, 34f));

            // ---- 底部中央：快门 ----
            var shutterGo = new GameObject("Shutter", typeof(RectTransform));
            _shutterRect = (RectTransform)shutterGo.transform;
            _shutterRect.SetParent(hud, false);
            _shutterRect.anchorMin = _shutterRect.anchorMax = new Vector2(0.5f, 0f);
            _shutterRect.pivot = new Vector2(0.5f, 0f);
            _shutterRect.anchoredPosition = new Vector2(0f, 60f);
            _shutterRect.sizeDelta = new Vector2(120f, 120f);
            // 硬边圆盘（不用 VNProceduralTextures.SoftCircle——那是发光用的软边渐变，
            // 叠在亮画面上几乎看不见）：深色底盘 → 白色圆环 → 白色圆盘 → 粉色中心点
            MakeRaw(_shutterRect, "Halo", DiscTexture, new Color(0f, 0f, 0f, 0.55f),
                Vector2.zero, new Vector2(124f, 124f));
            MakeRaw(_shutterRect, "Ring", RingTexture, new Color(1f, 1f, 1f, 0.95f),
                Vector2.zero, new Vector2(110f, 110f));
            var disc = MakeRaw(_shutterRect, "Disc", DiscTexture, Color.white,
                Vector2.zero, new Vector2(84f, 84f));
            MakeRaw(_shutterRect, "Dot", DiscTexture, Pink,
                Vector2.zero, new Vector2(26f, 26f));
            disc.raycastTarget = true;
            _shutterButton = disc.gameObject.AddComponent<Button>();
            _shutterButton.targetGraphic = disc;
            var sc = _shutterButton.colors;
            sc.highlightedColor = new Color(1f, 0.9f, 0.94f, 1f);
            sc.pressedColor = new Color(0.8f, 0.65f, 0.72f, 1f);
            _shutterButton.colors = sc;
            _shutterButton.onClick.AddListener(() => ShutterClicked?.Invoke());

            // ---- 左下：退出 ----
            _exitButton = MakeTextButton(hud, "Exit", VNLocale.T("secretphoto.exit"),
                new Vector2(0f, 0f), new Vector2(64f, 52f), new Vector2(150f, 48f));
            _exitButton.onClick.AddListener(() => ExitClicked?.Invoke());

            // ---- 右下：操作提示 ----
            _hintText = MakeText(hud, 20, TextAlignmentOptions.BottomRight);
            _hintText.text = VNLocale.T("secretphoto.hint");
            _hintText.color = Dim;
            PlaceCorner((RectTransform)_hintText.transform, new Vector2(1f, 0f), new Vector2(-64f, 52f), new Vector2(760f, 34f));

            // ---- 缩略图飞入（快门后从中央缩到右上）----
            _thumbFly = MakeRaw(hud, "ThumbFly", null, Color.white, Vector2.zero, new Vector2(480f, 270f));
            _thumbFly.gameObject.SetActive(false);

            // ---- 闪白 / 红闪（挂在 _root 上，HUD 关掉时也要能闪）----
            _flash = MakeImage(_root, "Flash", null, new Color(1f, 1f, 1f, 0f), Vector2.zero, Vector2.zero, false);
            Stretch(_flash.rectTransform);
            _flash.gameObject.SetActive(false);
            _redFlash = MakeImage(_root, "RedFlash", null, new Color(1f, 0.15f, 0.2f, 0f), Vector2.zero, Vector2.zero, false);
            Stretch(_redFlash.rectTransform);
            _redFlash.gameObject.SetActive(false);
        }

        void BuildCorner(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 dir,
            float len, float thick, Color color)
        {
            // 横臂
            var h = MakeImage(parent, "CornerH", null, color, Vector2.zero, new Vector2(len, thick), false);
            var hr = h.rectTransform;
            hr.anchorMin = hr.anchorMax = anchor;
            hr.pivot = new Vector2(dir.x > 0 ? 0f : 1f, dir.y > 0 ? 0f : 1f);
            hr.anchoredPosition = pos;
            // 竖臂
            var v = MakeImage(parent, "CornerV", null, color, Vector2.zero, new Vector2(thick, len), false);
            var vr = v.rectTransform;
            vr.anchorMin = vr.anchorMax = anchor;
            vr.pivot = new Vector2(dir.x > 0 ? 0f : 1f, dir.y > 0 ? 0f : 1f);
            vr.anchoredPosition = pos;
        }

        // ==================================================================
        // 图标
        // ==================================================================

        /// <summary>visible = 解锁且此刻能进；enabled = 有胶卷</summary>
        public void SetIcon(bool visible, bool enabled, int film)
        {
            if (_iconRoot == null) return;
            if (_iconRoot.activeSelf != visible) _iconRoot.SetActive(visible);
            if (!visible) return;
            _iconGroup.alpha = enabled ? 1f : 0.45f;
            _iconBadge.text = film.ToString();
            _iconBadge.color = enabled ? new Color(1f, 0.85f, 0.4f, 1f) : new Color(1f, 0.4f, 0.4f, 1f);
        }

        /// <summary>首次解锁时抖一下吸引注意</summary>
        public void PulseIcon()
        {
            if (_iconRoot == null || !_iconRoot.activeSelf) return;
            _iconPulse?.Kill();
            var rect = (RectTransform)_iconRoot.transform;
            rect.localScale = Vector3.one;
            _iconPulse = rect.DOPunchScale(Vector3.one * 0.25f, 0.6f, 6, 0.6f)
                .SetLink(_iconRoot).SetUpdate(true);
        }

        // ==================================================================
        // HUD
        // ==================================================================

        public void SetHudVisible(bool on)
        {
            if (_hud != null && _hud.activeSelf != on) _hud.SetActive(on);
            if (!on) { _alertPulse?.Kill(); _alertPulse = null; }
        }

        public void SetTarget(string displayName)
        {
            if (_targetText == null) return;
            bool has = !string.IsNullOrEmpty(displayName);
            _targetText.text = has ? VNLocale.T("secretphoto.target", displayName)
                                   : VNLocale.T("secretphoto.target.none");
            _targetText.color = has ? Pink : Dim;
        }

        public void SetZoom(float zoom)
        {
            if (_zoomText != null) _zoomText.text = VNLocale.T("secretphoto.zoom", zoom);
        }

        public void SetFilm(int film)
        {
            if (_filmText != null) _filmText.text = VNLocale.T("secretphoto.film", film);
        }

        /// <summary>察觉度 0~100：过半变橙、80 以上变红并脉动</summary>
        public void SetAlert(float percent)
        {
            if (_alertFill == null) return;
            float t = Mathf.Clamp01(percent / 100f);
            _alertFill.fillAmount = t;
            _alertPercent.text = $"{Mathf.RoundToInt(percent)}%";

            Color c = t < 0.5f ? Pink
                    : t < 0.8f ? new Color(1f, 0.62f, 0.25f, 1f)
                    : new Color(1f, 0.22f, 0.25f, 1f);
            _alertFill.color = c;

            bool danger = t >= 0.8f;
            if (danger && _alertPulse == null)
            {
                _alertPulse = _alertRect.DOScale(1.04f, 0.28f).SetLoops(-1, LoopType.Yoyo)
                    .SetEase(Ease.InOutSine).SetLink(_alertRect.gameObject);
            }
            else if (!danger && _alertPulse != null)
            {
                _alertPulse.Kill();
                _alertPulse = null;
                _alertRect.localScale = Vector3.one;
            }
        }

        /// <summary>快门闪白（盖住 ScreenCapture 那几十毫秒的卡顿）</summary>
        public IEnumerator FlashCo()
        {
            if (_flash == null) yield break;
            _flash.gameObject.SetActive(true);
            _flash.color = new Color(1f, 1f, 1f, 0.95f);
            var t = _flash.DOFade(0f, 0.32f).SetEase(Ease.OutQuad).SetLink(_flash.gameObject);
            yield return t.WaitForCompletion();
            _flash.gameObject.SetActive(false);
        }

        /// <summary>被发现：红闪两下</summary>
        public IEnumerator CaughtFlashCo()
        {
            if (_redFlash == null) yield break;
            _redFlash.gameObject.SetActive(true);
            for (int i = 0; i < 2; i++)
            {
                _redFlash.color = new Color(1f, 0.15f, 0.2f, 0.55f);
                var t = _redFlash.DOFade(0f, 0.22f).SetLink(_redFlash.gameObject);
                yield return t.WaitForCompletion();
            }
            _redFlash.gameObject.SetActive(false);
        }

        /// <summary>快门后的缩略图：从中央大图缩到右上角（胶卷数附近）再淡出</summary>
        public void PlayThumbnailFly(Texture2D shot)
        {
            if (_thumbFly == null || shot == null) return;
            _thumbFly.texture = shot;
            _thumbFly.gameObject.SetActive(true);
            var rect = _thumbFly.rectTransform;
            rect.DOKill();
            _thumbFly.DOKill();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(480f, 270f);
            rect.localScale = Vector3.one;
            _thumbFly.color = Color.white;

            // 目标：右上胶卷数下方一点
            var target = new Vector2(Screen.width > 0 ? 720f : 0f, 380f);
            DOTween.Sequence()
                .AppendInterval(0.18f)
                .Append(rect.DOAnchorPos(target, 0.55f).SetEase(Ease.InOutQuad))
                .Join(rect.DOScale(0.22f, 0.55f).SetEase(Ease.InQuad))
                .Append(_thumbFly.DOFade(0f, 0.25f))
                .AppendCallback(() =>
                {
                    _thumbFly.gameObject.SetActive(false);
                    _thumbFly.texture = null;
                })
                .SetLink(_thumbFly.gameObject);
        }

        /// <summary>快门按下时的按钮弹一下</summary>
        public void PunchShutter()
        {
            if (_shutterRect == null) return;
            _shutterRect.DOKill();
            _shutterRect.localScale = Vector3.one;
            _shutterRect.DOPunchScale(Vector3.one * -0.18f, 0.28f, 5, 0.5f).SetLink(_shutterRect.gameObject);
        }

        /// <summary>指针此刻是否压在本层的按钮上（拖动平移要跳过这种按下）</summary>
        static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        public bool IsPointerOverButton(Vector2 screenPos)
        {
            if (_raycaster == null || EventSystem.current == null) return false;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            _hits.Clear();
            _raycaster.Raycast(data, _hits);
            foreach (var h in _hits)
                if (h.gameObject.GetComponentInParent<Selectable>() != null) return true;
            return false;
        }

        // ==================================================================
        // 硬边圆形贴图（域重载会丢，lazy 重建；同 VNAssetTheme 的圆角贴图做法）
        // ==================================================================

        static Texture2D _disc, _ring;

        /// <summary>实心圆盘，边缘 2px 抗锯齿</summary>
        static Texture2D DiscTexture => _disc != null ? _disc : (_disc = MakeCircle(false));

        /// <summary>圆环（内径 = 外径 × 0.82）</summary>
        static Texture2D RingTexture => _ring != null ? _ring : (_ring = MakeCircle(true));

        static Texture2D MakeCircle(bool ring)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = ring ? "VNSecretPhotoRing" : "VNSecretPhotoDisc",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            float c = size * 0.5f;
            float rOut = c - 1.5f;
            float rIn = ring ? rOut * 0.82f : -1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                    float a = Mathf.Clamp01(rOut - d + 0.5f);          // 外缘 1px 渐变
                    if (ring) a *= Mathf.Clamp01(d - rIn + 0.5f);      // 内缘同理
                    byte alpha = (byte)Mathf.RoundToInt(a * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        // ==================================================================
        // 小工具
        // ==================================================================

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void PlaceCorner(RectTransform rect, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        static Image MakeImage(RectTransform parent, string name, Sprite sprite, Color color,
            Vector2 pos, Vector2 size, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        static RawImage MakeRaw(RectTransform parent, string name, Texture2D tex, Color color,
            Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var raw = go.GetComponent<RawImage>();
            raw.texture = tex;
            raw.color = color;
            raw.raycastTarget = false;
            return raw;
        }

        static void MakeLine(RectTransform parent, string name, Color color, Vector2 a, Vector2 b,
            float thick, bool vertical)
        {
            var img = MakeImage(parent, name, null, color, Vector2.zero, Vector2.zero, false);
            var r = img.rectTransform;
            r.anchorMin = a;
            r.anchorMax = b;
            r.offsetMin = vertical ? new Vector2(-thick * 0.5f, 0f) : new Vector2(0f, -thick * 0.5f);
            r.offsetMax = vertical ? new Vector2(thick * 0.5f, 0f) : new Vector2(0f, thick * 0.5f);
        }

        static TextMeshProUGUI MakeText(RectTransform parent, int size, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = align;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        static Button MakeTextButton(RectTransform parent, string name, string label,
            Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var img = MakeImage(parent, name, VNProceduralTextures.RoundedRectSprite,
                new Color(0f, 0f, 0f, 0.5f), pos, size, true);
            img.type = Image.Type.Sliced;
            var rect = img.rectTransform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var c = button.colors;
            c.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            button.colors = c;
            var text = MakeText(rect, 22, TextAlignmentOptions.Center);
            text.text = label;
            Stretch((RectTransform)text.transform);
            return button;
        }
    }
}
