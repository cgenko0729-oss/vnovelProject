using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>全屏转场类型</summary>
    public enum VNTransition
    {
        NoiseDissolve, // 噪声溶解（带辉光边缘）
        Blinds,        // 百叶窗
        Tiles,         // 瓦片翻转
        CircleWipe,    // 圆形扩散（可从说话角色位置扩散）
        InkSpread,     // 水墨晕染
        WhiteFlash,    // 爆闪（HDR 白 + Bloom 超亮一瞬间）
        BokehOrbs,     // 光斑虚化（大光斑涌满屏幕，进入回忆）
        Eyelid,        // POV 眨眼（上下眼睑合拢再睁开：醒来/昏迷/回忆）
        PageCurl,      // 卷页（弯曲页缘 + 背光 + 阴影）
        Shatter,       // 碎裂（放射碎片 + 裂缝高光）
        Ripple,        // 水波扩散（多重波纹环）
        InkBleed,      // 墨水晕染（多墨滴融合 + 飞墨颗粒）
    }

    /// <summary>
    /// 花式全屏转场库。
    /// 用法：transition.Play(VNTransition.NoiseDissolve, () => { 在全屏被盖住时切换场景内容 });
    /// 流程：图案覆盖率 0→1（转出）→ 执行 onCovered 回调 → 1→0（转入揭示新画面）。
    /// 覆盖层用嵌套 Canvas 排序 100，盖住一切（含粒子）；转场期间拦截点击。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNScreenTransition : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdEdgeColor = Shader.PropertyToID("_EdgeColor");
        static readonly int IdProgress = Shader.PropertyToID("_Progress");
        static readonly int IdMode = Shader.PropertyToID("_Mode");
        static readonly int IdNoiseScale = Shader.PropertyToID("_NoiseScale");
        static readonly int IdCount = Shader.PropertyToID("_Count");
        static readonly int IdCenter = Shader.PropertyToID("_Center");
        static readonly int IdAspect = Shader.PropertyToID("_Aspect");
        static readonly int IdUvRect = Shader.PropertyToID("_UVRect");
        static readonly int IdRectMinMax = Shader.PropertyToID("_RectMinMax");
        static readonly int IdScatter = Shader.PropertyToID("_Scatter");

        [Tooltip("渲染排序（要盖住一切，包括粒子和边缘泛光）")]
        public int sortingOrder = 100;

        [Tooltip("可选：预制的 VN/ScreenTransition 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        Image _inputBlocker;
        Material _mat;
        Material _directMat;
        GameObject _directOverlay;
        Sequence _seq;
        VNAmbientParticles _bokehOrbs;

        public bool IsPlaying => _seq != null && _seq.IsActive() && _seq.IsPlaying();

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_img != null) return;

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            // 拦截转场期间的点击需要 Raycaster
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var go = new GameObject("TransitionOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var overlayRect = (RectTransform)go.transform;
            overlayRect.SetParent(transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            _img = go.GetComponent<RawImage>();
            _img.texture = Texture2D.whiteTexture;
            _img.raycastTarget = true; // 转场时挡住输入

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/ScreenTransition")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/ScreenTransition");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/ScreenTransition\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _img.material = _mat;
            _img.enabled = false;

            var blockerGo = new GameObject("DirectTransitionInputBlocker",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var blockerRect = (RectTransform)blockerGo.transform;
            blockerRect.SetParent(transform, false);
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            _inputBlocker = blockerGo.GetComponent<Image>();
            _inputBlocker.color = Color.clear;
            _inputBlocker.raycastTarget = true;
            _inputBlocker.enabled = false;
        }

        /// <summary>这些效果在 bg 命令中直接让旧背景离场并露出新背景，不经过黑色遮罩。</summary>
        public static bool SupportsDirectBackground(VNTransition type) =>
            type == VNTransition.PageCurl || type == VNTransition.Shatter ||
            type == VNTransition.Ripple || type == VNTransition.InkBleed;

        /// <summary>
        /// 背景 A→B 直接转场。先复制当前背景 A，再立即把真实 Image 换成 B，
        /// 最后只动画上层副本；页后、碎片间、波纹内和墨迹内会直接露出 B。
        /// </summary>
        public Sequence PlayBackground(VNTransition type, Image backgroundImage,
            Sprite nextSprite, Action onNextVisible = null, Vector2? viewportCenter = null)
        {
            if (!SupportsDirectBackground(type) || backgroundImage == null ||
                backgroundImage.sprite == null || nextSprite == null)
                return null;

            Build();
            StopCurrentTransition();

            Sprite oldSprite = backgroundImage.sprite;
            _directMat = CreateDirectMaterial();
            if (_directMat == null) return null;

            float duration = DirectDuration(type);
            int mode = DirectMode(type);
            Vector2 center = viewportCenter ?? new Vector2(0.5f, 0.5f);

            if (type == VNTransition.Shatter)
                CreateShatterOverlay(backgroundImage, oldSprite);
            else
                CreateImageOverlay(backgroundImage, oldSprite);

            ConfigureDirectMaterial(oldSprite, backgroundImage.rectTransform.rect,
                mode, center);

            // 新背景先放在临时旧背景下面，动画一开始就能从空隙中看到。
            backgroundImage.sprite = nextSprite;
            onNextVisible?.Invoke();
            if (_inputBlocker != null) _inputBlocker.enabled = true;

            _seq = DOTween.Sequence()
                .Append(_directMat.DOFloat(1f, IdProgress, duration).SetEase(DirectEase(type)))
                .OnComplete(CleanupDirectOverlay)
                .SetLink(gameObject);
            return _seq;
        }

        /// <summary>
        /// 播放转场。onCovered 在画面完全被盖住的瞬间调用（此时切换背景/场景内容）。
        /// outDuration / inDuration 传负值时使用该转场类型的推荐时长。
        /// viewportCenter：CircleWipe / InkSpread / Shatter / Ripple / InkBleed 的中心（视口坐标 0~1）。
        /// </summary>
        public Sequence Play(VNTransition type, Action onCovered = null,
            float outDuration = -1f, float inDuration = -1f,
            Color? color = null, Vector2? viewportCenter = null)
        {
            Build();
            StopCurrentTransition();

            GetDefaults(type, out float defOut, out float defIn, out Color defColor,
                out int mode, out float noiseScale, out float count);
            if (outDuration < 0f) outDuration = defOut;
            if (inDuration < 0f) inDuration = defIn;

            _mat.SetFloat(IdMode, mode);
            _mat.SetColor(IdColor, color ?? defColor);
            _mat.SetColor(IdEdgeColor, GetEdgeColor(type));
            _mat.SetFloat(IdNoiseScale, noiseScale);
            _mat.SetFloat(IdCount, count);
            _mat.SetVector(IdCenter, viewportCenter ?? new Vector2(0.5f, 0.5f));
            _mat.SetFloat(IdAspect, (float)Screen.width / Screen.height);
            _mat.SetFloat(IdProgress, 0f);
            _img.enabled = true;

            if (type == VNTransition.BokehOrbs) StartBokehOrbs();

            var outEase = type == VNTransition.WhiteFlash ? Ease.OutQuad
                        : type == VNTransition.Eyelid ? Ease.InQuad // 眼睑合拢加速
                        : Ease.InOutSine;

            _seq = DOTween.Sequence()
                .Append(_mat.DOFloat(1f, IdProgress, outDuration).SetEase(outEase))
                .AppendCallback(() => onCovered?.Invoke())
                .AppendInterval(0.08f)
                .Append(_mat.DOFloat(0f, IdProgress, inDuration).SetEase(Ease.InOutSine))
                .OnComplete(() =>
                {
                    _img.enabled = false;
                    if (type == VNTransition.BokehOrbs) StopBokehOrbs();
                })
                .SetLink(gameObject);
            return _seq;
        }

        Material CreateDirectMaterial()
        {
            var shader = Shader.Find("VN/DirectBackgroundTransition");
            if (shader == null)
            {
                Debug.LogError("[VNEffects] 找不到 Shader \"VN/DirectBackgroundTransition\"。", this);
                return null;
            }
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetFloat(IdProgress, 0f);
            return material;
        }

        void CreateImageOverlay(Image source, Sprite oldSprite)
        {
            var go = new GameObject($"DirectTransition_{oldSprite.name}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            _directOverlay = go;
            var rect = (RectTransform)go.transform;
            CopyRectTransform(source.rectTransform, rect);
            var image = go.GetComponent<Image>();
            image.sprite = oldSprite;
            image.type = source.type;
            image.useSpriteMesh = source.useSpriteMesh;
            image.preserveAspect = source.preserveAspect;
            image.fillCenter = source.fillCenter;
            image.fillMethod = source.fillMethod;
            image.fillAmount = source.fillAmount;
            image.fillClockwise = source.fillClockwise;
            image.fillOrigin = source.fillOrigin;
            image.color = source.color;
            image.raycastTarget = false;
            image.material = _directMat;
        }

        void CreateShatterOverlay(Image source, Sprite oldSprite)
        {
            var go = new GameObject($"DirectShatter_{oldSprite.name}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(VNShatterGraphic));
            _directOverlay = go;
            var rect = (RectTransform)go.transform;
            CopyRectTransform(source.rectTransform, rect);
            var graphic = go.GetComponent<VNShatterGraphic>();
            graphic.Sprite = oldSprite;
            graphic.color = source.color;
            graphic.raycastTarget = false;
            graphic.material = _directMat;

            var canvas = source.GetComponentInParent<Canvas>();
            if (canvas != null)
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                                   AdditionalCanvasShaderChannels.TexCoord2;
        }

        static void CopyRectTransform(RectTransform source, RectTransform target)
        {
            target.SetParent(source.parent, false);
            target.SetSiblingIndex(source.GetSiblingIndex() + 1);
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition3D = source.anchoredPosition3D;
            target.sizeDelta = source.sizeDelta;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        void ConfigureDirectMaterial(Sprite sprite, Rect localRect, int mode, Vector2 center)
        {
            // DataUtility 同时支持普通 Sprite 与图集 Sprite，避免 tight packing 下 textureRect 抛异常。
            Vector4 outerUv = DataUtility.GetOuterUV(sprite);
            _directMat.SetVector(IdUvRect, outerUv);
            _directMat.SetVector(IdRectMinMax, new Vector4(
                localRect.xMin, localRect.yMin, localRect.xMax, localRect.yMax));
            _directMat.SetVector(IdCenter, center);
            _directMat.SetFloat(IdAspect, Mathf.Max(0.01f,
                localRect.width / Mathf.Max(1f, localRect.height)));
            _directMat.SetFloat(IdMode, mode);
            _directMat.SetFloat(IdScatter, 0.42f);
            _directMat.SetFloat(IdProgress, 0f);
        }

        static int DirectMode(VNTransition type)
        {
            switch (type)
            {
                case VNTransition.Ripple: return 1;
                case VNTransition.InkBleed: return 2;
                case VNTransition.Shatter: return 3;
                default: return 0;
            }
        }

        static float DirectDuration(VNTransition type)
        {
            switch (type)
            {
                case VNTransition.PageCurl: return 1.2f;
                case VNTransition.Shatter: return 1.05f;
                case VNTransition.Ripple: return 1.0f;
                case VNTransition.InkBleed: return 1.25f;
                default: return 1f;
            }
        }

        static Ease DirectEase(VNTransition type) => type == VNTransition.Shatter
            ? Ease.InQuad : Ease.InOutSine;

        void StopCurrentTransition()
        {
            if (_seq != null)
            {
                var sequence = _seq;
                _seq = null;
                sequence.Kill();
            }
            if (_img != null) _img.enabled = false;
            StopBokehOrbs();
            CleanupDirectOverlay();
        }

        void CleanupDirectOverlay()
        {
            _seq = null;
            if (_inputBlocker != null) _inputBlocker.enabled = false;
            if (_directOverlay != null)
            {
                _directOverlay.SetActive(false);
                Destroy(_directOverlay);
                _directOverlay = null;
            }
            if (_directMat != null)
            {
                Destroy(_directMat);
                _directMat = null;
            }
        }

        /// <summary>从某个世界坐标目标（如说话角色）扩散的圆形转场</summary>
        public Sequence PlayFrom(VNTransition type, Transform worldTarget,
            Action onCovered = null, float outDuration = -1f, float inDuration = -1f)
        {
            Vector2 vp = new Vector2(0.5f, 0.5f);
            var cam = Camera.main;
            if (cam != null && worldTarget != null)
            {
                Vector3 p = cam.WorldToViewportPoint(worldTarget.position);
                vp = new Vector2(p.x, p.y);
            }
            return Play(type, onCovered, outDuration, inDuration, null, vp);
        }

        static void GetDefaults(VNTransition type, out float outDur, out float inDur,
            out Color color, out int mode, out float noiseScale, out float count)
        {
            outDur = 0.8f; inDur = 0.8f;
            color = Color.black;
            mode = 0; noiseScale = 7f; count = 9f;
            switch (type)
            {
                case VNTransition.NoiseDissolve:
                    mode = 0; outDur = 0.9f; inDur = 0.9f;
                    break;
                case VNTransition.Blinds:
                    mode = 1; outDur = 0.7f; inDur = 0.7f; count = 9f;
                    break;
                case VNTransition.Tiles:
                    mode = 2; outDur = 0.85f; inDur = 0.85f; count = 10f;
                    break;
                case VNTransition.CircleWipe:
                    mode = 3; outDur = 0.7f; inDur = 0.7f;
                    break;
                case VNTransition.InkSpread:
                    mode = 4; outDur = 1.1f; inDur = 1.0f; noiseScale = 5f;
                    break;
                case VNTransition.WhiteFlash:
                    mode = 5; outDur = 0.22f; inDur = 0.75f;
                    color = new Color(2.2f, 2.15f, 2.0f, 1f); // HDR 白，Bloom 爆亮
                    break;
                case VNTransition.BokehOrbs:
                    mode = 5; outDur = 1.3f; inDur = 1.1f;
                    color = new Color(1.25f, 1.15f, 0.95f, 0.8f); // 柔暖光罩
                    break;
                case VNTransition.Eyelid:
                    mode = 6; outDur = 0.4f; inDur = 0.65f;
                    break;
                case VNTransition.PageCurl:
                    mode = 7; outDur = 1.15f; inDur = 1.0f;
                    color = new Color(0.08f, 0.065f, 0.055f, 1f);
                    break;
                case VNTransition.Shatter:
                    mode = 8; outDur = 0.8f; inDur = 0.95f; count = 11f;
                    break;
                case VNTransition.Ripple:
                    mode = 9; outDur = 1.0f; inDur = 0.9f; count = 6f;
                    color = new Color(0.015f, 0.045f, 0.075f, 1f);
                    break;
                case VNTransition.InkBleed:
                    mode = 10; outDur = 1.25f; inDur = 1.1f; noiseScale = 6f;
                    color = new Color(0.012f, 0.009f, 0.016f, 1f);
                    break;
            }
        }

        static Color GetEdgeColor(VNTransition type)
        {
            switch (type)
            {
                case VNTransition.PageCurl: return new Color(1.8f, 1.35f, 0.85f, 1f);
                case VNTransition.Shatter: return new Color(0.75f, 1.45f, 2.5f, 1f);
                case VNTransition.Ripple: return new Color(0.35f, 1.15f, 2.2f, 1f);
                case VNTransition.InkBleed: return new Color(0.38f, 0.22f, 0.5f, 1f);
                default: return new Color(2.5f, 1.6f, 0.6f, 1f);
            }
        }

        // ------------------------------------------------------------------
        // 光斑虚化转场的粒子部分
        // ------------------------------------------------------------------

        void StartBokehOrbs()
        {
            if (_bokehOrbs == null)
            {
                _bokehOrbs = VNAmbientParticles.Create(
                    VNAmbientParticles.Preset.Orbs,
                    new Color(1f, 0.9f, 0.72f), sortingOrder + 1,
                    null, 14f, transform, 1.5f);
            }
            _bokehOrbs.SetPlaying(true);
        }

        void StopBokehOrbs()
        {
            if (_bokehOrbs != null) _bokehOrbs.SetPlaying(false);
        }

        void OnDestroy()
        {
            _seq?.Kill();
            CleanupDirectOverlay();
            if (_mat != null) Destroy(_mat);
        }
    }
}
