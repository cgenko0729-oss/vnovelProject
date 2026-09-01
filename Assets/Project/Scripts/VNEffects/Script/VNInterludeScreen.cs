using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 过场层（章节标题卡）：转场图铺满 + 标题居中 + loading 图标在右下角转固定时长。
    /// 剧本命令 interlude 驱动，内容与节奏全部来自 <see cref="VNInterludeDef"/>。
    ///
    /// 【enter = Transition 为什么不调 VNScreenTransition.Play()】
    /// 那个 API 是遮罩式的：拿一块纯色（默认黑）按图案盖满画面 → 换内容 → 图案散开，
    /// 所以中间必然闪一片黑。过场层要的是「新图直接从瓦片缝里长出来」，
    /// 于是改成自己持有一份 VN/ScreenTransition 的**贴图模式**材质
    /// （`_TexMode = 1`：图案里填的是图而不是纯色），progress 0→1 就是长出来、
    /// 1→0 就是消失。图案参数仍走 VNScreenTransition.ConfigurePattern 那张唯一的表。
    /// 贴图模式下 `_Color` 退化成染色系数，暗幕就折进它里面，不用再叠一层。
    ///
    /// 【排序为什么是 90】
    /// 在对话框(40) / 事件层(60) 之上、全屏转场(100) 之下。压在转场之下是为了
    /// 别的地方调 Play() 时（比如过场结束后紧接一个 bg 转场）仍能盖住过场层。
    ///
    /// 【为什么挂在主 Canvas 下而不是自建 Overlay Canvas】
    /// 同上：Screen Space - Overlay 的画布永远压在 Screen Space - Camera 的主画布之上，
    /// 转场层就再也盖不住它了。挂在主 Canvas 下还顺带吃到 URP 后处理（标题发光靠 Bloom）。
    /// 找不到主 Canvas 时（裸场景）才退回自建 Overlay 画布。
    ///
    /// 【不进存档】
    /// 过场是一段一次性演出，播完什么都不留，所以既不进 VNSaveData，
    /// 也不需要在 RebuildStateBefore 里静默重放。
    /// </summary>
    public class VNInterludeScreen : MonoBehaviour
    {
        static readonly int IdProgress = Shader.PropertyToID("_Progress");
        static readonly int IdColor = Shader.PropertyToID("_Color");

        [Header("渲染排序（务必保持 < 全屏转场的 100）")]
        public int sortingOrder = 90;

        [Header("标题 / loading 相对图案的淡入延迟与时长（秒）")]
        public float hudDelay = 0.18f;
        public float hudFade = 0.35f;

        RectTransform _root;
        CanvasGroup _group;
        RectTransform _imageRect;
        Image _image;
        Image _dim;
        CanvasGroup _hudGroup;
        Image _spinnerTrack;
        TextMeshProUGUI _title;
        TextMeshProUGUI _subtitle;
        RectTransform _spinner;
        Image _spinnerArc;
        Sequence _spinTween;
        Material _patternMat;

        public bool IsPlaying { get; private set; }

        // ------------------------------------------------------------------
        // 播放
        // ------------------------------------------------------------------

        /// <summary>
        /// 播一次过场。协程结束时过场层已经完全收起。
        /// </summary>
        /// <param name="def">内容与参数</param>
        /// <param name="durationOverride">剧本 time: 覆盖 loading 时长（负数 = 用资产的）</param>
        /// <param name="audio">放语音用；null = 不放</param>
        public IEnumerator PlayCo(VNInterludeDef def, float durationOverride, VNAudio audio)
        {
            if (def == null) yield break;

            Build();
            IsPlaying = true;
            _root.gameObject.SetActive(true);
            _group.alpha = 0f;
            _group.blocksRaycasts = true; // 过场期间吃掉点击（时长固定，玩家点不动）
            _hudGroup.alpha = 0f;

            // 图案模式要有图才成立：没有图可长，就只能整层淡入
            Sprite sprite = ApplyContent(def);
            bool usePattern = def.enter == VNInterludeEnter.Transition && sprite != null &&
                              _patternMat != null;
            float appear = def.fadeIn, disappear = def.fadeOut;
            if (usePattern)
            {
                Rect r = _imageRect.rect;
                VNScreenTransition.ConfigurePattern(_patternMat, def.transition,
                    r.width / Mathf.Max(1f, r.height), null, sprite,
                    out appear, out disappear);
                _patternMat.SetFloat(IdProgress, 0f);
                // 贴图模式下 _Color 是染色系数：暗幕折进来，省掉单独一层
                float k = 1f - Mathf.Clamp01(def.dimStrength);
                _patternMat.SetColor(IdColor, new Color(k, k, k, 1f));
                _image.material = _patternMat;
                _dim.enabled = false;
            }
            else
            {
                _image.material = null;
            }

            StartSpinner();

            float hold = durationOverride >= 0f ? durationOverride : Mathf.Max(0f, def.loadingDuration);
            string voice = def.PickVoice();
            PlayVoice(audio, voice);

            // ---- 进：图案长出过场图（或整层淡入），标题与 loading 稍后跟上 ----
            _group.alpha = usePattern ? 1f : 0f;
            Tween enterTween = usePattern
                ? _patternMat.DOFloat(1f, IdProgress, appear).SetEase(Ease.InOutSine)
                : _group.DOFade(1f, appear).SetEase(Ease.OutQuad);
            enterTween.SetLink(gameObject);
            _hudGroup.DOFade(1f, hudFade).SetDelay(hudDelay).SetLink(gameObject);
            yield return enterTween.WaitForCompletion();

            // ---- 停留（固定时长，不接受输入打断）----
            if (hold > 0f) yield return new WaitForSeconds(hold);

            // ---- 出：先收标题，再让图案散掉，露出下面的游戏画面 ----
            _hudGroup.DOFade(0f, Mathf.Min(hudFade, disappear)).SetLink(gameObject);
            Tween exitTween = usePattern
                ? _patternMat.DOFloat(0f, IdProgress, disappear).SetEase(Ease.InOutSine)
                : _group.DOFade(0f, disappear).SetEase(Ease.InQuad);
            exitTween.SetLink(gameObject);
            yield return exitTween.WaitForCompletion();

            HideImmediate();
        }

        /// <summary>立刻收起。读档 / 停止剧本 / 调试取消都要走它，否则过场层会留在屏幕上。</summary>
        public void HideImmediate()
        {
            IsPlaying = false;
            StopSpinner();
            if (_group != null)
            {
                _group.DOKill();
                _group.alpha = 0f;
                _group.blocksRaycasts = false;
            }
            if (_hudGroup != null)
            {
                _hudGroup.DOKill();
                _hudGroup.alpha = 0f;
            }
            if (_patternMat != null)
            {
                _patternMat.DOKill();
                _patternMat.SetFloat(IdProgress, 0f);
            }
            if (_image != null) _image.material = null;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void OnDisable() => HideImmediate();

        void OnDestroy()
        {
            if (_patternMat != null) Destroy(_patternMat);
        }

        static void PlayVoice(VNAudio audio, string id)
        {
            if (audio == null || string.IsNullOrEmpty(id)) return;
            audio.PlayVoice(id);
        }

        // ------------------------------------------------------------------
        // 内容
        // ------------------------------------------------------------------

        /// <summary>装载这次过场的图与文字，返回抽中的转场图（null = 没有图可用）</summary>
        Sprite ApplyContent(VNInterludeDef def)
        {
            Sprite sprite = def.PickImage();
            _image.sprite = sprite;
            _image.enabled = sprite != null;
            if (sprite != null)
            {
                // EnvelopeParent = cover：宁可裁掉边缘也铺满，绝不留黑边
                var fitter = _imageRect.GetComponent<AspectRatioFitter>();
                fitter.aspectRatio = sprite.rect.height > 0f
                    ? sprite.rect.width / sprite.rect.height
                    : 16f / 9f;
            }

            // 没有图时暗幕就是底色本身，不透明，否则会透出后面的立绘
            _dim.enabled = true;
            _dim.color = sprite != null
                ? new Color(0f, 0f, 0f, def.dimStrength)
                : def.fallbackColor;

            _title.text = def.ResolveTitle() ?? string.Empty;
            _title.font = VNFont.DisplayAsset;
            _title.fontSize = def.titleFontSize;
            _title.color = def.titleColor;

            string subtitle = def.ResolveSubtitle();
            _subtitle.text = subtitle ?? string.Empty;
            _subtitle.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            _subtitle.font = VNFont.Asset;
            _subtitle.fontSize = def.subtitleFontSize;
            _subtitle.color = def.subtitleColor;

            _spinner.sizeDelta = new Vector2(def.loadingSize, def.loadingSize);
            _spinnerArc.color = def.loadingColor;
            _spinnerTrack.color = new Color(
                def.loadingColor.r, def.loadingColor.g, def.loadingColor.b, 0.18f);
            return sprite;
        }

        // ------------------------------------------------------------------
        // loading 图标：底环 + 一段固定长度的弧匀速转（经典 spinner，零美术依赖）
        // ------------------------------------------------------------------

        void StartSpinner()
        {
            StopSpinner();
            _spinner.localEulerAngles = Vector3.zero;
            _spinnerArc.fillAmount = 0.22f;
            _spinTween = DOTween.Sequence()
                .Join(_spinner.DOLocalRotate(new Vector3(0f, 0f, -360f), 0.9f,
                        RotateMode.FastBeyond360).SetEase(Ease.Linear).SetLoops(-1))
                // 弧长轻微呼吸：纯匀速转看久了像卡住，长度一变就「在动」
                .Join(_spinnerArc.DOFillAmount(0.42f, 0.55f)
                        .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo))
                .SetLink(gameObject);
        }

        void StopSpinner()
        {
            if (_spinTween != null && _spinTween.IsActive()) _spinTween.Kill();
            _spinTween = null;
            if (_spinner != null) _spinner.DOKill();
            if (_spinnerArc != null) _spinnerArc.DOKill();
        }

        // ------------------------------------------------------------------
        // 程序化 UI 骨架
        // ------------------------------------------------------------------

        void Build()
        {
            if (_root != null) return;

            Transform parent = transform;
            if (GetComponentInParent<Canvas>() != null)
            {
                // 主 Canvas 下：嵌套画布 + overrideSorting，才能夹在事件层与全屏转场之间
                var canvas = gameObject.GetComponent<Canvas>();
                if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
                if (gameObject.GetComponent<GraphicRaycaster>() == null)
                    gameObject.AddComponent<GraphicRaycaster>();
                Stretch(EnsureRect(gameObject));
            }
            else
            {
                // 裸场景自愈：自建 Overlay 画布
                var canvasGo = new GameObject("VNInterludeCanvas",
                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasGo.transform.SetParent(transform, false);
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = sortingOrder;
                var scaler = canvasGo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                parent = canvasGo.transform;
            }

            _patternMat = VNScreenTransition.CreatePatternMaterial();

            _root = CreateRect("InterludeRoot", parent);
            Stretch(_root);
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            // 底板拦截点击（过场时长固定，玩家怎么点都不能提前跳）
            var blocker = _root.gameObject.AddComponent<Image>();
            blocker.color = Color.clear;
            blocker.raycastTarget = true;
            // 图按 cover 铺，多出来的部分要裁掉，否则会溢出到画布外
            _root.gameObject.AddComponent<RectMask2D>();

            _imageRect = CreateRect("Image", _root);
            _imageRect.anchorMin = _imageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _imageRect.pivot = new Vector2(0.5f, 0.5f);
            _imageRect.sizeDelta = Vector2.zero;
            _image = _imageRect.gameObject.AddComponent<Image>();
            _image.raycastTarget = false;
            _image.preserveAspect = false;
            var fitter = _imageRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            var dimRect = CreateRect("Dim", _root);
            Stretch(dimRect);
            _dim = dimRect.gameObject.AddComponent<Image>();
            _dim.raycastTarget = false;
            _dim.color = new Color(0f, 0f, 0f, 0.45f);

            // 标题 / 副标题 / loading 单独一层：图案长出图的同时它们淡入，
            // 混在一起走图案的话文字会被瓦片切碎，很难看
            var hud = CreateRect("Hud", _root);
            Stretch(hud);
            _hudGroup = hud.gameObject.AddComponent<CanvasGroup>();
            _hudGroup.alpha = 0f;
            _hudGroup.blocksRaycasts = false;

            _title = CreateText("Title", hud, 96, Color.white);
            _title.font = VNFont.DisplayAsset;
            _title.alignment = TextAlignmentOptions.Center;
            var titleRect = (RectTransform)_title.transform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.offsetMin = new Vector2(120f, 0f);
            titleRect.offsetMax = new Vector2(-120f, 0f);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 180f);
            titleRect.anchoredPosition = new Vector2(0f, 20f);

            _subtitle = CreateText("Subtitle", hud, 34, new Color(0.85f, 0.86f, 0.92f, 1f));
            _subtitle.alignment = TextAlignmentOptions.Center;
            var subRect = (RectTransform)_subtitle.transform;
            subRect.anchorMin = new Vector2(0f, 0.5f);
            subRect.anchorMax = new Vector2(1f, 0.5f);
            subRect.pivot = new Vector2(0.5f, 1f);
            subRect.offsetMin = new Vector2(120f, 0f);
            subRect.offsetMax = new Vector2(-120f, 0f);
            subRect.sizeDelta = new Vector2(subRect.sizeDelta.x, 60f);
            subRect.anchoredPosition = new Vector2(0f, -78f);

            _spinner = CreateRect("Loading", hud);
            _spinner.anchorMin = _spinner.anchorMax = new Vector2(1f, 0f);
            _spinner.pivot = new Vector2(0.5f, 0.5f);
            _spinner.sizeDelta = new Vector2(56f, 56f);
            _spinner.anchoredPosition = new Vector2(-118f, 104f);

            var trackRect = CreateRect("Track", _spinner);
            Stretch(trackRect);
            _spinnerTrack = trackRect.gameObject.AddComponent<Image>();
            _spinnerTrack.sprite = VNProceduralTextures.LoadingRingSprite;
            _spinnerTrack.raycastTarget = false;
            _spinnerTrack.color = new Color(1f, 1f, 1f, 0.18f);

            var arcRect = CreateRect("Arc", _spinner);
            Stretch(arcRect);
            _spinnerArc = arcRect.gameObject.AddComponent<Image>();
            _spinnerArc.sprite = VNProceduralTextures.LoadingRingSprite;
            _spinnerArc.raycastTarget = false;
            _spinnerArc.type = Image.Type.Filled;
            _spinnerArc.fillMethod = Image.FillMethod.Radial360;
            _spinnerArc.fillOrigin = (int)Image.Origin360.Top;
            _spinnerArc.fillClockwise = true;
            _spinnerArc.fillAmount = 0.22f;

            _root.gameObject.SetActive(false);
        }

        static RectTransform EnsureRect(GameObject go)
        {
            var rect = go.GetComponent<RectTransform>();
            if (rect == null) rect = go.AddComponent<RectTransform>();
            return rect;
        }

        static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static TextMeshProUGUI CreateText(string name, Transform parent, int size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
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
