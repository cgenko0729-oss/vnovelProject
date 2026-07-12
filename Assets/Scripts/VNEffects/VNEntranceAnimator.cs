using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>出场演出预设</summary>
    public enum VNEntrancePreset
    {
        /// <summary>噪声溶解显形 + 辉光边缘 + 星光爆发（最华丽，推荐立绘首次登场）</summary>
        DissolveGlow,
        /// <summary>从下方轻盈滑入 + 淡入（日常对话切换立绘）</summary>
        FadeSlideUp,
        /// <summary>弹性缩放弹出 + 微闪白（俏皮/惊喜登场）</summary>
        ScaleBounce,
        /// <summary>淡入后一道扫光掠过（优雅登场）</summary>
        ShineReveal,
        /// <summary>爆闪 + 光环闪耀中显形（高潮/重要角色登场）</summary>
        FlashBloom,
    }

    /// <summary>
    /// 图片出场/退场演出编排器。
    /// 组合 VNImageEffectController（shader 参数）、CanvasGroup（整体透明度）、
    /// RectTransform（位移缩放）、VNGlowBackdrop（背后光环）与星光爆发粒子，
    /// 编排成一次性的 DOTween Sequence。
    /// </summary>
    [RequireComponent(typeof(VNImageEffectController))]
    public class VNEntranceAnimator : MonoBehaviour
    {
        [Tooltip("出场时星光爆发的粒子颜色")]
        public Color burstColor = new Color(1f, 0.92f, 0.6f, 1f);

        [Tooltip("是否在出场时触发星光爆发粒子")]
        public bool useParticleBurst = true;

        VNImageEffectController _fx;
        CanvasGroup _group;
        VNGlowBackdrop _backdrop; // 可选
        Sequence _current;

        Vector2 _basePos;
        Vector3 _baseScale;
        bool _baseCached;

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _backdrop = GetComponent<VNGlowBackdrop>();
            CacheBase();
        }

        void CacheBase()
        {
            if (_baseCached) return;
            _basePos = _fx.Rect.anchoredPosition;
            _baseScale = _fx.Rect.localScale;
            _baseCached = true;
        }

        /// <summary>把图片重置到"完全隐藏"状态，准备出场</summary>
        public void PrepareHidden()
        {
            CacheBase();
            KillCurrent();
            _fx.StopAllLoops();
            _fx.ResetScaleMultiplier(); // 清掉说话者高亮的缩放倍率
            _group.alpha = 0f;
            _fx.SetDissolve(0f);
            _fx.SetFlash(0f);
            _fx.Rect.anchoredPosition = _basePos;
            _fx.Rect.localScale = _baseScale;
            _backdrop?.Hide();
        }

        void KillCurrent()
        {
            _current?.Kill();
            _current = null;
        }

        Vector3 BurstWorldPos() => _fx.Rect.position;

        // ------------------------------------------------------------------
        // 出场
        // ------------------------------------------------------------------

        /// <summary>播放指定预设的出场演出</summary>
        public Sequence PlayEntrance(VNEntrancePreset preset, float durationScale = 1f)
        {
            PrepareHidden();
            switch (preset)
            {
                case VNEntrancePreset.DissolveGlow: _current = BuildDissolveGlow(durationScale); break;
                case VNEntrancePreset.FadeSlideUp: _current = BuildFadeSlideUp(durationScale); break;
                case VNEntrancePreset.ScaleBounce: _current = BuildScaleBounce(durationScale); break;
                case VNEntrancePreset.ShineReveal: _current = BuildShineReveal(durationScale); break;
                case VNEntrancePreset.FlashBloom: _current = BuildFlashBloom(durationScale); break;
                default: _current = BuildFadeSlideUp(durationScale); break;
            }
            _current.SetLink(gameObject);
            return _current;
        }

        Sequence BuildDissolveGlow(float k)
        {
            _group.alpha = 1f; // 可见性完全交给溶解控制
            var seq = DOTween.Sequence();
            seq.Append(_fx.DODissolve(1f, 1.3f * k, Ease.InOutSine));
            if (_backdrop != null)
                seq.Insert(0.25f * k, _backdrop.Flare(2.2f, 1.1f * k));
            if (useParticleBurst)
                seq.InsertCallback(0.45f * k, () =>
                    VNAmbientParticles.PlaySparkleBurst(BurstWorldPos(), burstColor, 30));
            seq.Append(_fx.DOFlash(0.18f, 0.3f * k));
            return seq;
        }

        Sequence BuildFadeSlideUp(float k)
        {
            _fx.SetDissolve(1f);
            _fx.Rect.anchoredPosition = _basePos + new Vector2(0f, -45f);
            var seq = DOTween.Sequence();
            seq.Append(_group.DOFade(1f, 0.7f * k).SetEase(Ease.OutQuad));
            seq.Join(_fx.Rect.DOAnchorPos(_basePos, 0.8f * k).SetEase(Ease.OutCubic));
            if (_backdrop != null)
                seq.Insert(0.35f * k, _backdrop.Flare(1.4f, 0.8f * k));
            return seq;
        }

        Sequence BuildScaleBounce(float k)
        {
            _fx.SetDissolve(1f);
            _fx.Rect.localScale = _baseScale * 0.65f;
            var seq = DOTween.Sequence();
            seq.Append(_group.DOFade(1f, 0.3f * k).SetEase(Ease.OutQuad));
            seq.Join(_fx.Rect.DOScale(_baseScale, 0.65f * k).SetEase(Ease.OutBack, 1.4f));
            seq.Insert(0.3f * k, _fx.DOFlash(0.25f, 0.35f * k));
            if (useParticleBurst)
                seq.InsertCallback(0.35f * k, () =>
                    VNAmbientParticles.PlaySparkleBurst(BurstWorldPos(), burstColor, 20));
            if (_backdrop != null)
                seq.Insert(0.25f * k, _backdrop.Flare(1.6f, 0.8f * k));
            return seq;
        }

        Sequence BuildShineReveal(float k)
        {
            _fx.SetDissolve(1f);
            var seq = DOTween.Sequence();
            seq.Append(_group.DOFade(1f, 0.6f * k).SetEase(Ease.InOutSine));
            seq.Append(_fx.PlayShine(0.7f * k));
            if (_backdrop != null)
                seq.Insert(0.5f * k, _backdrop.Flare(1.5f, 0.9f * k));
            return seq;
        }

        Sequence BuildFlashBloom(float k)
        {
            _fx.SetDissolve(1f);
            _fx.SetFlash(1f);
            _group.alpha = 1f;
            _fx.Rect.localScale = _baseScale * 1.07f;
            var seq = DOTween.Sequence();
            seq.Append(_fx.Mat.DOFloat(0f, "_FlashAmount", 0.9f * k).SetEase(Ease.OutCubic));
            seq.Join(_fx.Rect.DOScale(_baseScale, 0.9f * k).SetEase(Ease.OutQuad));
            if (_backdrop != null)
                seq.Insert(0f, _backdrop.Flare(2.6f, 1.2f * k));
            if (useParticleBurst)
                seq.InsertCallback(0.05f * k, () =>
                    VNAmbientParticles.PlaySparkleBurst(BurstWorldPos(), Color.white, 36));
            seq.Append(_fx.PlayShine(0.7f * k));
            return seq;
        }

        // ------------------------------------------------------------------
        // 退场
        // ------------------------------------------------------------------

        /// <summary>溶解退场（化作光点消散）</summary>
        public Sequence PlayExitDissolve(float duration = 1f)
        {
            KillCurrent();
            _fx.StopAllLoops();
            _backdrop?.Hide();
            _current = DOTween.Sequence()
                .Append(_fx.DODissolve(0f, duration, Ease.InOutSine))
                .SetLink(gameObject);
            if (useParticleBurst)
                _current.InsertCallback(duration * 0.4f, () =>
                    VNAmbientParticles.PlaySparkleBurst(BurstWorldPos(), burstColor, 16));
            return _current;
        }

        /// <summary>淡出下滑退场</summary>
        public Sequence PlayExitFade(float duration = 0.6f)
        {
            KillCurrent();
            _fx.StopAllLoops();
            _backdrop?.Hide();
            _current = DOTween.Sequence()
                .Append(_group.DOFade(0f, duration).SetEase(Ease.InQuad))
                .Join(_fx.Rect.DOAnchorPos(_basePos + new Vector2(0f, -30f), duration).SetEase(Ease.InCubic))
                .OnComplete(() => _fx.Rect.anchoredPosition = _basePos)
                .SetLink(gameObject);
            return _current;
        }

        /// <summary>出场完成后开启常驻的"活图"效果（呼吸发光 + 悬浮 + 呼吸动作 + 周期扫光）</summary>
        public void StartIdleEffects(
            Color? glowColor = null, float glowAmount = 0.12f,
            float floatAmplitude = 6f, float shineInterval = 7f)
        {
            _fx.StartBreathingGlow(glowColor ?? new Color(1f, 0.9f, 0.7f, 1f), glowAmount, 3.2f);
            _fx.StartFloating(floatAmplitude, 4.5f);
            _fx.StartBreathingMotion(); // 呼吸感立绘：横向缩放呼吸 + 微倾斜
            if (shineInterval > 0f) _fx.StartShineLoop(shineInterval, 0.8f);
        }
    }
}
