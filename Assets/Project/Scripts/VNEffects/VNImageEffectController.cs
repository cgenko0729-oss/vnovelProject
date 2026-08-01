using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 单张图片（uGUI Image / RawImage）的特效控制器。
    /// 负责：
    ///   - 创建并持有 VN/ImageEffect 材质实例（每张图独立，互不干扰）
    ///   - 暴露所有 shader 参数的即时设置与 DOTween 补间 API
    ///   - 常驻循环效果：呼吸发光、周期扫光、上下悬浮飘动、微波浪
    /// </summary>
    [RequireComponent(typeof(MaskableGraphic))]
    public class VNImageEffectController : MonoBehaviour
    {
        static readonly int IdDissolveAmount = Shader.PropertyToID("_DissolveAmount");
        static readonly int IdDissolveScale = Shader.PropertyToID("_DissolveScale");
        static readonly int IdDissolveEdgeColor = Shader.PropertyToID("_DissolveEdgeColor");
        static readonly int IdShineProgress = Shader.PropertyToID("_ShineProgress");
        static readonly int IdShineWidth = Shader.PropertyToID("_ShineWidth");
        static readonly int IdShineAngle = Shader.PropertyToID("_ShineAngle");
        static readonly int IdShineColor = Shader.PropertyToID("_ShineColor");
        static readonly int IdFlashAmount = Shader.PropertyToID("_FlashAmount");
        static readonly int IdFlashColor = Shader.PropertyToID("_FlashColor");
        static readonly int IdEmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int IdEmissionAmount = Shader.PropertyToID("_EmissionAmount");
        static readonly int IdHueShift = Shader.PropertyToID("_HueShift");
        static readonly int IdSaturation = Shader.PropertyToID("_Saturation");
        static readonly int IdBrightness = Shader.PropertyToID("_Brightness");
        static readonly int IdWaveAmount = Shader.PropertyToID("_WaveAmount");
        static readonly int IdRimColor = Shader.PropertyToID("_RimColor");
        static readonly int IdRimAmount = Shader.PropertyToID("_RimAmount");
        static readonly int IdRimWidth = Shader.PropertyToID("_RimWidth");
        static readonly int IdRimAngle = Shader.PropertyToID("_RimAngle");

        const string ShaderName = "VN/ImageEffect";

        [Header("可选：预先做好的 VN/ImageEffect 材质资产；留空则运行时自动创建")]
        [SerializeField] Material sourceMaterial;

        MaskableGraphic _graphic;
        Material _mat;
        RectTransform _rect;

        Tween _breathTween;
        Sequence _shineLoop;
        Tween _floatTween;
        float _floatBaseY;
        bool _hasFloatBase;
        float _lastFloatAmplitude = 8f;
        float _lastFloatPeriod = 4f;

        /// <summary>当前是否在悬浮飘动（情绪动作库用来暂停/恢复）</summary>
        public bool IsFloating => _floatTween != null;

        /// <summary>材质实例（懒初始化，可在 Awake 前被外部访问）</summary>
        public Material Mat
        {
            get
            {
                EnsureMaterial();
                return _mat;
            }
        }

        public RectTransform Rect
        {
            get
            {
                if (_rect == null) _rect = (RectTransform)transform;
                return _rect;
            }
        }

        void Awake()
        {
            EnsureMaterial();
        }

        void EnsureMaterial()
        {
            if (_mat != null) return;
            _graphic = GetComponent<MaskableGraphic>();

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == ShaderName)
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[VNEffects] 找不到 Shader \"{ShaderName}\"，请确认 VNImageEffect.shader 已导入。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.name = $"{ShaderName} (Instance of {name})";
            _mat.hideFlags = HideFlags.DontSave;
            _graphic.material = _mat;
        }

        void OnDestroy()
        {
            _breathTween?.Kill();
            _shineLoop?.Kill();
            _floatTween?.Kill();
            DOTween.Kill(this);
            if (_mat != null) Destroy(_mat);
        }

        // ------------------------------------------------------------------
        // 溶解
        // ------------------------------------------------------------------

        /// <summary>立即设置溶解度：0 = 完全隐藏，1 = 完全显示</summary>
        public void SetDissolve(float amount) => Mat.SetFloat(IdDissolveAmount, Mathf.Clamp01(amount));

        public float GetDissolve() => Mat.GetFloat(IdDissolveAmount);

        /// <summary>补间溶解度（出场：0→1；退场：1→0）</summary>
        public Tween DODissolve(float to, float duration, Ease ease = Ease.InOutSine)
        {
            return Mat.DOFloat(Mathf.Clamp01(to), IdDissolveAmount, duration)
                      .SetEase(ease).SetTarget(this).SetLink(gameObject);
        }

        public void SetDissolveStyle(float noiseScale, Color hdrEdgeColor)
        {
            Mat.SetFloat(IdDissolveScale, noiseScale);
            Mat.SetColor(IdDissolveEdgeColor, hdrEdgeColor);
        }

        // ------------------------------------------------------------------
        // 扫光
        // ------------------------------------------------------------------

        public void SetShineStyle(float width, float angleDeg, Color hdrColor)
        {
            Mat.SetFloat(IdShineWidth, width);
            Mat.SetFloat(IdShineAngle, angleDeg);
            Mat.SetColor(IdShineColor, hdrColor);
        }

        /// <summary>播放一次扫光（高光带从一侧扫到另一侧）</summary>
        public Tween PlayShine(float duration = 0.75f)
        {
            Mat.SetFloat(IdShineProgress, -0.3f);
            return DOTween.To(() => -0.3f, v => Mat.SetFloat(IdShineProgress, v), 1.3f, duration)
                          .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        /// <summary>开启周期扫光循环（每 interval 秒扫一次）</summary>
        public void StartShineLoop(float interval = 5f, float sweepDuration = 0.8f)
        {
            StopShineLoop();
            _shineLoop = DOTween.Sequence()
                .AppendInterval(interval)
                .Append(DOTween.To(() => -0.3f, v => Mat.SetFloat(IdShineProgress, v), 1.3f, sweepDuration)
                               .SetEase(Ease.InOutSine))
                .SetLoops(-1)
                .SetLink(gameObject);
        }

        public void StopShineLoop()
        {
            _shineLoop?.Kill();
            _shineLoop = null;
            if (_mat != null) _mat.SetFloat(IdShineProgress, -0.3f);
        }

        // ------------------------------------------------------------------
        // 呼吸发光（HDR 自发光脉动，配合 Bloom 会产生柔和辉光）
        // ------------------------------------------------------------------

        public void StartBreathingGlow(Color hdrColor, float maxAmount = 0.25f, float period = 3f)
        {
            StopBreathingGlow();
            Mat.SetColor(IdEmissionColor, hdrColor);
            Mat.SetFloat(IdEmissionAmount, 0f);
            _breathTween = Mat.DOFloat(maxAmount, IdEmissionAmount, period * 0.5f)
                              .SetEase(Ease.InOutSine)
                              .SetLoops(-1, LoopType.Yoyo)
                              .SetLink(gameObject);
        }

        public void StopBreathingGlow()
        {
            _breathTween?.Kill();
            _breathTween = null;
            if (_mat != null) _mat.SetFloat(IdEmissionAmount, 0f);
        }

        /// <summary>瞬时发光脉冲（例如被点名/高亮说话者时）</summary>
        public Sequence PulseEmission(Color hdrColor, float peak = 0.8f, float duration = 0.6f)
        {
            Mat.SetColor(IdEmissionColor, hdrColor);
            return DOTween.Sequence()
                .Append(Mat.DOFloat(peak, IdEmissionAmount, duration * 0.3f).SetEase(Ease.OutQuad))
                .Append(Mat.DOFloat(0f, IdEmissionAmount, duration * 0.7f).SetEase(Ease.InOutSine))
                .SetTarget(this).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 闪白
        // ------------------------------------------------------------------

        public void SetFlash(float amount) => Mat.SetFloat(IdFlashAmount, Mathf.Clamp01(amount));

        /// <summary>闪白一次：瞬间到 peak 再淡出</summary>
        public Sequence DOFlash(float peak = 1f, float duration = 0.35f, Color? color = null)
        {
            Mat.SetColor(IdFlashColor, color ?? Color.white);
            return DOTween.Sequence()
                .Append(Mat.DOFloat(peak, IdFlashAmount, duration * 0.15f).SetEase(Ease.OutQuad))
                .Append(Mat.DOFloat(0f, IdFlashAmount, duration * 0.85f).SetEase(Ease.OutCubic))
                .SetTarget(this).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // HSV 调色 / 波浪
        // ------------------------------------------------------------------

        public void SetHSV(float hueShift = 0f, float saturation = 1f, float brightness = 1f)
        {
            Mat.SetFloat(IdHueShift, hueShift);
            Mat.SetFloat(IdSaturation, saturation);
            Mat.SetFloat(IdBrightness, brightness);
        }

        /// <summary>补间亮度（如夜晚变暗、回忆场景降饱和等）</summary>
        public Tween DOBrightness(float to, float duration) =>
            Mat.DOFloat(to, IdBrightness, duration).SetTarget(this).SetLink(gameObject);

        public Tween DOSaturation(float to, float duration) =>
            Mat.DOFloat(to, IdSaturation, duration).SetTarget(this).SetLink(gameObject);

        public void SetWave(float amount, float speed = 2f, float freq = 8f)
        {
            Mat.SetFloat(IdWaveAmount, amount);
            Mat.SetFloat(Shader.PropertyToID("_WaveSpeed"), speed);
            Mat.SetFloat(Shader.PropertyToID("_WaveFreq"), freq);
        }

        // ------------------------------------------------------------------
        // 轮廓光（Rim Light）：让立绘外缘染上环境光色，与背景光照统一
        // ------------------------------------------------------------------

        /// <summary>设置轮廓光（angleDeg：光源方向，0=右 90=上 180=左）</summary>
        public void SetRimLight(Color hdrColor, float amount, float width = 0.02f, float angleDeg = 45f)
        {
            Mat.SetColor(IdRimColor, hdrColor);
            Mat.SetFloat(IdRimWidth, width);
            Mat.SetFloat(IdRimAngle, angleDeg);
            Mat.SetFloat(IdRimAmount, amount);
        }

        /// <summary>补间轮廓光强度（渐亮/渐灭）</summary>
        public Tween DORimAmount(float to, float duration) =>
            Mat.DOFloat(to, IdRimAmount, duration).SetTarget(this).SetLink(gameObject);

        public void ClearRimLight() => Mat.SetFloat(IdRimAmount, 0f);

        // ------------------------------------------------------------------
        // 水面波光：海边/湖边场景，画面下半部叠加缓慢滚动的高光波纹
        // ------------------------------------------------------------------

        static readonly int IdShimmerAmount = Shader.PropertyToID("_ShimmerAmount");
        static readonly int IdShimmerColor = Shader.PropertyToID("_ShimmerColor");
        static readonly int IdShimmerHeight = Shader.PropertyToID("_ShimmerHeight");
        static readonly int IdShimmerScale = Shader.PropertyToID("_ShimmerScale");
        static readonly int IdShimmerSpeed = Shader.PropertyToID("_ShimmerSpeed");

        /// <summary>设置水面波光（height：波光区占画面下方的高度比例）</summary>
        public void SetWaterShimmer(float amount, Color? hdrColor = null,
            float height = 0.45f, float scale = 60f, float speed = 1f)
        {
            Mat.SetColor(IdShimmerColor, hdrColor ?? new Color(1.3f, 1.4f, 1.5f, 1f));
            Mat.SetFloat(IdShimmerHeight, height);
            Mat.SetFloat(IdShimmerScale, scale);
            Mat.SetFloat(IdShimmerSpeed, speed);
            Mat.SetFloat(IdShimmerAmount, amount);
        }

        /// <summary>补间波光强度（渐现/渐隐）</summary>
        public Tween DOShimmerAmount(float to, float duration) =>
            Mat.DOFloat(to, IdShimmerAmount, duration).SetTarget(this).SetLink(gameObject);

        public void ClearWaterShimmer() => Mat.SetFloat(IdShimmerAmount, 0f);

        // ------------------------------------------------------------------
        // 微模糊（伪景深：背景虚化让立绘"浮"出来）
        // ------------------------------------------------------------------

        static readonly int IdBlurAmount = Shader.PropertyToID("_BlurAmount");

        /// <summary>立即设置模糊半径（uv 单位，0.004~0.008 为宜）</summary>
        public void SetBlur(float uvRadius) => Mat.SetFloat(IdBlurAmount, uvRadius);

        /// <summary>补间模糊半径（虚化渐入渐出）</summary>
        public Tween DOBlur(float to, float duration) =>
            Mat.DOFloat(to, IdBlurAmount, duration).SetTarget(this).SetLink(gameObject);

        // ------------------------------------------------------------------
        // 悬浮飘动（RectTransform 上下缓慢浮动，让立绘"活"起来）
        // ------------------------------------------------------------------

        /// <summary>用上次的参数恢复悬浮（情绪动作结束后调用）</summary>
        public void ResumeFloating() => StartFloating(_lastFloatAmplitude, _lastFloatPeriod);

        /// <summary>重设悬浮基准 Y（剧本移动/换位角色后调用，否则悬浮会拽回旧位置）</summary>
        public void SetFloatBaseY(float y)
        {
            _floatBaseY = y;
            _hasFloatBase = true;
        }

        public void StartFloating(float amplitude = 8f, float period = 4f)
        {
            _lastFloatAmplitude = amplitude;
            _lastFloatPeriod = period;
            StopFloating();
            if (!_hasFloatBase)
            {
                _floatBaseY = Rect.anchoredPosition.y;
                _hasFloatBase = true;
            }
            _floatTween = Rect.DOAnchorPosY(_floatBaseY + amplitude, period * 0.5f)
                              .SetEase(Ease.InOutSine)
                              .SetLoops(-1, LoopType.Yoyo)
                              .SetLink(gameObject);
        }

        public void StopFloating()
        {
            _floatTween?.Kill();
            _floatTween = null;
            if (_hasFloatBase)
                Rect.anchoredPosition = new Vector2(Rect.anchoredPosition.x, _floatBaseY);
        }

        // ------------------------------------------------------------------
        // 呼吸感立绘（Pseudo-Live2D）：横向缩放呼吸 + 轻微倾斜摆动
        // 与悬浮飘动三个正弦叠加，立绘"活着"的感觉翻倍
        // ------------------------------------------------------------------

        Tween _breathScaleX;
        Tween _breathScaleY;
        Tween _tiltTween;
        Vector3 _origScale;
        bool _hasOrigScale;
        float _scaleMultiplier = 1f;
        float _lastBreathAmp = 0.013f, _lastBreathPeriod = 3.6f;
        float _lastTiltDeg = 0.7f, _lastTiltPeriod = 7f;

        void EnsureOrigScale()
        {
            if (_hasOrigScale) return;
            _origScale = Rect.localScale;
            _hasOrigScale = true;
        }

        /// <summary>当前基准缩放 = 初始缩放 × 缩放倍率（说话者高亮等设置）</summary>
        public Vector3 CurrentBaseScale
        {
            get { EnsureOrigScale(); return _origScale * _scaleMultiplier; }
        }

        /// <summary>静默重置缩放倍率（出场动画重播前调用）</summary>
        public void ResetScaleMultiplier() => _scaleMultiplier = 1f;

        /// <summary>
        /// 补间缩放倍率（说话者高亮用：说话者 1.03、旁听者 0.97）。
        /// 与呼吸动作兼容：呼吸的缩放分量先暂停，倍率过渡完成后围绕新基准继续呼吸。
        /// </summary>
        public Tween DOScaleMultiplier(float mult, float duration)
        {
            EnsureOrigScale();
            _scaleMultiplier = mult;
            bool wasBreathing = _breathScaleX != null;
            _breathScaleX?.Kill();
            _breathScaleY?.Kill();
            _breathScaleX = _breathScaleY = null;
            var tween = Rect.DOScale(CurrentBaseScale, duration)
                .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
            if (wasBreathing) tween.OnComplete(RestartBreathScale);
            return tween;
        }

        void RestartBreathScale()
        {
            var bs = CurrentBaseScale;
            _breathScaleX = Rect.DOScaleX(bs.x * (1f + _lastBreathAmp), _lastBreathPeriod * 0.5f)
                                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                                .SetLink(gameObject);
            _breathScaleY = Rect.DOScaleY(bs.y * (1f + _lastBreathAmp * 0.4f), _lastBreathPeriod * 0.5f)
                                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                                .SetLink(gameObject);
        }

        /// <summary>当前是否在呼吸动作中</summary>
        public bool IsBreathingMotion => _breathScaleX != null;

        /// <summary>用上次的参数恢复呼吸（情绪动作结束后调用）</summary>
        public void ResumeBreathingMotion() =>
            StartBreathingMotion(_lastBreathAmp, _lastBreathPeriod, _lastTiltDeg, _lastTiltPeriod);

        /// <summary>
        /// 开启呼吸动作：横向缩放 scaleAmplitude（约 1~2%）模拟胸腔起伏，
        /// 纵向带 40% 的同步微伸展；叠加 ±tiltDegrees 的极缓倾斜摆动。
        /// </summary>
        public void StartBreathingMotion(float scaleAmplitude = 0.013f, float period = 3.6f,
            float tiltDegrees = 0.7f, float tiltPeriod = 7f)
        {
            StopBreathingMotion();
            _lastBreathAmp = scaleAmplitude;
            _lastBreathPeriod = period;
            _lastTiltDeg = tiltDegrees;
            _lastTiltPeriod = tiltPeriod;

            var t = Rect;
            t.localScale = CurrentBaseScale;
            RestartBreathScale();

            if (tiltDegrees > 0.01f)
            {
                // 先缓慢摆到一侧，再在两侧之间往复 —— 起步不跳变
                _tiltTween = t.DOLocalRotate(new Vector3(0f, 0f, tiltDegrees), tiltPeriod * 0.5f)
                              .SetEase(Ease.InOutSine)
                              .SetLink(gameObject)
                              .OnComplete(() =>
                              {
                                  _tiltTween = t.DOLocalRotate(new Vector3(0f, 0f, -tiltDegrees), tiltPeriod)
                                                .SetEase(Ease.InOutSine)
                                                .SetLoops(-1, LoopType.Yoyo)
                                                .SetLink(gameObject);
                              });
            }
        }

        public void StopBreathingMotion()
        {
            _breathScaleX?.Kill();
            _breathScaleY?.Kill();
            _tiltTween?.Kill();
            _breathScaleX = _breathScaleY = _tiltTween = null;
            if (_hasOrigScale)
            {
                Rect.localScale = CurrentBaseScale;
                Rect.localRotation = Quaternion.identity;
            }
        }

        /// <summary>停止所有常驻循环效果</summary>
        public void StopAllLoops()
        {
            StopShineLoop();
            StopBreathingGlow();
            StopFloating();
            StopBreathingMotion();
        }
    }
}
