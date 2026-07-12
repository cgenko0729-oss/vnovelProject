using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 立绘情绪演出动作库：一行代码调用的常用小动作。
    ///   Surprise()  惊讶：快速上跳 + 微缩放回弹
    ///   Angry()     生气：左右快速抖动 + 红色发光脉冲
    ///   Shy()       害羞：轻微缩小下沉 + 粉色光晕
    ///   Dejected()  沮丧：下沉 + 变暗 + 降饱和（持续，直到 Recover()）
    ///   Recover()   从沮丧恢复
    ///   Nod()       点头：两次快速下沉回弹
    ///   HeadShake() 摇头：小幅左右旋转摆动
    /// 动作期间自动暂停悬浮飘动，结束后自动恢复；动作互相打断安全。
    /// </summary>
    [RequireComponent(typeof(VNImageEffectController))]
    public class VNCharacterEmotes : MonoBehaviour
    {
        VNImageEffectController _fx;
        RectTransform _rect;
        Sequence _seq;

        Vector2 _basePos;
        Vector3 _baseScale;
        bool _cached;
        bool _wasFloating;
        bool _dejected;

        public bool IsDejected => _dejected;

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
            _rect = _fx.Rect;
        }

        /// <summary>动作开始前的统一准备：暂停悬浮、打断上个动作、回到基准姿态</summary>
        void Begin()
        {
            _wasFloating = _fx.IsFloating || _wasFloating && _seq != null && _seq.IsActive();
            _fx.StopFloating(); // 会把位置重置回悬浮基准

            _seq?.Kill();
            _seq = null;

            if (!_cached)
            {
                _basePos = _rect.anchoredPosition;
                _baseScale = _rect.localScale;
                _cached = true;
            }

            _rect.localScale = _baseScale;
            _rect.localRotation = Quaternion.identity;
            if (!_dejected) _rect.anchoredPosition = _basePos;
        }

        /// <summary>动作收尾：恢复悬浮（沮丧状态除外）</summary>
        Sequence End(Sequence seq)
        {
            seq.OnComplete(() =>
            {
                if (_wasFloating && !_dejected) _fx.ResumeFloating();
            });
            seq.SetLink(gameObject);
            _seq = seq;
            return seq;
        }

        // ------------------------------------------------------------------

        /// <summary>惊讶：快速上跳 + 微放大，落回时轻微回弹</summary>
        public Sequence Surprise()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y + 34f, 0.12f).SetEase(Ease.OutQuad))
                .Join(_rect.DOScale(_baseScale * 1.05f, 0.12f).SetEase(Ease.OutQuad))
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.32f).SetEase(Ease.OutBounce))
                .Join(_rect.DOScale(_baseScale, 0.26f).SetEase(Ease.OutQuad));
            return End(seq);
        }

        /// <summary>生气：横向快速抖动 + 红色发光脉冲</summary>
        public Sequence Angry()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOShakeAnchorPos(0.55f, new Vector2(16f, 0f), 22, 90f, false, true))
                .Insert(0f, _fx.PulseEmission(new Color(1.6f, 0.25f, 0.15f), 0.55f, 0.65f));
            return End(seq);
        }

        /// <summary>害羞：轻微缩小 + 下沉一点 + 粉色光晕，然后慢慢恢复</summary>
        public Sequence Shy()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOScale(_baseScale * 0.97f, 0.28f).SetEase(Ease.OutQuad))
                .Join(_rect.DOAnchorPosY(_basePos.y - 7f, 0.28f).SetEase(Ease.OutQuad))
                .Join(_fx.PulseEmission(new Color(1.5f, 0.65f, 0.85f), 0.45f, 1.3f))
                .AppendInterval(0.35f)
                .Append(_rect.DOScale(_baseScale, 0.45f).SetEase(Ease.InOutSine))
                .Join(_rect.DOAnchorPosY(_basePos.y, 0.45f).SetEase(Ease.InOutSine));
            return End(seq);
        }

        /// <summary>沮丧：下沉 + 变暗 + 降饱和，保持该状态直到 Recover()</summary>
        public Sequence Dejected()
        {
            Begin();
            _dejected = true;
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y - 24f, 0.6f).SetEase(Ease.OutQuad))
                .Join(_fx.DOBrightness(0.72f, 0.6f))
                .Join(_fx.DOSaturation(0.68f, 0.6f));
            return End(seq);
        }

        /// <summary>从沮丧状态恢复：回到原位、亮度饱和度复原、恢复悬浮</summary>
        public Sequence Recover()
        {
            if (!_dejected) return _seq;
            Begin();
            _dejected = false;
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.5f).SetEase(Ease.OutQuad))
                .Join(_fx.DOBrightness(1f, 0.5f))
                .Join(_fx.DOSaturation(1f, 0.5f));
            return End(seq);
        }

        /// <summary>点头：两次快速下沉回弹（第二次幅度更小）</summary>
        public Sequence Nod()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y - 14f, 0.13f).SetEase(Ease.OutQuad))
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.16f).SetEase(Ease.InOutSine))
                .Append(_rect.DOAnchorPosY(_basePos.y - 9f, 0.12f).SetEase(Ease.OutQuad))
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.15f).SetEase(Ease.InOutSine));
            return End(seq);
        }

        /// <summary>摇头：小幅左右旋转摆动后归正</summary>
        public Sequence HeadShake()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOLocalRotate(new Vector3(0f, 0f, 2.6f), 0.1f).SetEase(Ease.OutQuad))
                .Append(_rect.DOLocalRotate(new Vector3(0f, 0f, -2.6f), 0.16f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(new Vector3(0f, 0f, 2f), 0.15f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(new Vector3(0f, 0f, -1.4f), 0.14f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(Vector3.zero, 0.12f).SetEase(Ease.OutQuad));
            return End(seq);
        }

        void OnDestroy()
        {
            _seq?.Kill();
        }
    }
}
