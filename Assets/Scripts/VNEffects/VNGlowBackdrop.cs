using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 在图片背后生成一个柔光光环（径向渐变 + 加法混合 + HDR 颜色）。
    /// 光环会缓慢脉动，配合 Bloom 让立绘像被一圈柔光包裹。
    /// 挂到带 Image/RawImage 的物体上即可，光环物体自动创建并插入到本物体之前渲染。
    /// </summary>
    public class VNGlowBackdrop : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        [Header("光环颜色（配合 Bloom 时可给出 >1 的 HDR 强度）")]
        public Color glowColor = new Color(1.0f, 0.85f, 0.55f, 0.35f);

        [Header("光环相对本图片矩形的尺寸倍率")]
        public float sizeScale = 1.45f;

        [Header("HDR 强度倍率，>1 时更容易触发 Bloom 泛光")]
        public float hdrIntensity = 1.6f;

        [Header("呼吸脉动周期（秒），0 = 不脉动")]
        public float pulsePeriod = 3.5f;

        [Range(0f, 1f)]
        [Header("脉动幅度（相对基础亮度的比例）")]
        public float pulseStrength = 0.45f;

        [Header("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _halo;
        Material _mat;
        Tween _pulse;
        float _baseAlpha;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_halo != null) return;

            var self = (RectTransform)transform;

            var go = new GameObject($"{name}_GlowHalo",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(self.parent, false);
            // 插到本物体之前 → 渲染在图片背后
            rect.SetSiblingIndex(self.GetSiblingIndex());
            rect.anchorMin = self.anchorMin;
            rect.anchorMax = self.anchorMax;
            rect.pivot = self.pivot;
            rect.anchoredPosition = self.anchoredPosition;
            rect.sizeDelta = self.rect.size * sizeScale;
            rect.localScale = Vector3.one;

            _halo = go.GetComponent<RawImage>();
            _halo.texture = VNProceduralTextures.RadialGlow;
            _halo.raycastTarget = false;

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Additive")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Additive");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Additive\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _halo.material = _mat;

            _baseAlpha = glowColor.a;
            ApplyColor(0f); // 初始隐藏，由动画或 StartPulse 点亮

            if (pulsePeriod > 0.01f)
                StartPulse();
        }

        void ApplyColor(float alpha)
        {
            if (_mat == null) return;
            var c = glowColor * hdrIntensity;
            c.a = alpha;
            _mat.SetColor(IdTintColor, c);
        }

        /// <summary>开始呼吸脉动</summary>
        public void StartPulse()
        {
            StopPulse();
            float lo = _baseAlpha * (1f - pulseStrength);
            float hi = _baseAlpha;
            ApplyColor(lo);
            _pulse = DOTween.To(() => lo, ApplyColor, hi, pulsePeriod * 0.5f)
                            .SetEase(Ease.InOutSine)
                            .SetLoops(-1, LoopType.Yoyo)
                            .SetLink(gameObject);
        }

        public void StopPulse()
        {
            _pulse?.Kill();
            _pulse = null;
        }

        /// <summary>隐藏光环（出场动画开始前调用）</summary>
        public void Hide()
        {
            StopPulse();
            ApplyColor(0f);
            if (_halo != null) _halo.transform.localScale = Vector3.one;
        }

        /// <summary>
        /// 光环闪耀一次：快速放大变亮再回落，然后恢复呼吸脉动。
        /// 常用于图片出场瞬间。
        /// </summary>
        public Sequence Flare(float peakAlphaMul = 2.2f, float duration = 0.9f)
        {
            if (_halo == null) Build();
            StopPulse();
            float peak = Mathf.Clamp01(_baseAlpha * peakAlphaMul);
            var t = _halo.transform;
            t.localScale = Vector3.one * 0.6f;
            ApplyColor(0f);

            return DOTween.Sequence()
                .Append(DOTween.To(() => 0f, ApplyColor, peak, duration * 0.35f).SetEase(Ease.OutQuad))
                .Join(t.DOScale(1.15f, duration * 0.5f).SetEase(Ease.OutBack))
                .Append(DOTween.To(() => peak, ApplyColor, _baseAlpha, duration * 0.65f).SetEase(Ease.InOutSine))
                .Join(t.DOScale(1f, duration * 0.5f).SetEase(Ease.InOutSine))
                .OnComplete(StartPulse)
                .SetTarget(this).SetLink(gameObject);
        }

        void OnDestroy()
        {
            _pulse?.Kill();
            DOTween.Kill(this);
            if (_mat != null) Destroy(_mat);
            if (_halo != null) Destroy(_halo.gameObject);
        }
    }
}
