using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 背景 Ken Burns 漂移：静止背景以 60~90 秒周期极缓慢地缩放 1.0→1.06 +
    /// 平移几十像素，画面永不静止——商业 VN 标配的"活着的背景"。
    /// 实现：无限链式随机航点（缩放/平移/时长皆随机），InOutSine 缓动让每段
    /// 首尾速度归零、段间无停顿也无折角；关闭时缓慢归位而非急停。
    /// 挂在背景 Image 上（生成器自动挂；旧场景由 VNStage/VNEffectsDemo 自愈补挂）。
    /// 剧本 fx kenburns on|off，默认开启。
    /// 与 VNCamera（缩放 ZoomRoot）、VNParallax（移动层容器）作用于不同节点，可叠加。
    /// </summary>
    public class VNKenBurns : MonoBehaviour
    {
        [Header("被漂移的背景（留空 = 本物体的 RectTransform）")]
        public RectTransform target;

        [Header("缩放下限（1 = 原始大小）")]
        public float minScale = 1.0f;

        [Header("缩放上限（背景四边有 60px 溢出余量，1.06 内安全）")]
        public float maxScale = 1.06f;

        [Header("平移幅度（像素，单方向最大偏移）")]
        public float panRange = 40f;

        [Header("单段航点时长下限（秒）；一去一回 ≈ 完整周期 60~90 秒")]
        public float legDurationMin = 30f;

        [Header("单段航点时长上限（秒）")]
        public float legDurationMax = 45f;

        [Header("启动即开始漂移（永不静止）")]
        public bool playOnAwake = true;

        RectTransform Rect => target != null ? target : (RectTransform)transform;

        Vector2 _basePos;
        Vector3 _baseScale;
        bool _captured;
        bool _playing;
        Sequence _seq;

        public bool IsPlaying => _playing;

        void Awake()
        {
            CaptureBase();
            if (playOnAwake) SetPlaying(true);
        }

        /// <summary>记录归位基准（首次调用时的位置/缩放）</summary>
        void CaptureBase()
        {
            if (_captured) return;
            _captured = true;
            _basePos = Rect.anchoredPosition;
            _baseScale = Rect.localScale;
        }

        /// <summary>开/关漂移。关闭时用 2.5 秒缓慢归位，不急停。</summary>
        public void SetPlaying(bool on)
        {
            CaptureBase();
            if (on == _playing) return;
            _playing = on;

            _seq?.Kill();
            _seq = null;

            if (on)
            {
                NextLeg();
            }
            else
            {
                var rect = Rect;
                _seq = DOTween.Sequence()
                    .Join(rect.DOAnchorPos(_basePos, 2.5f).SetEase(Ease.InOutSine))
                    .Join(rect.DOScale(_baseScale, 2.5f).SetEase(Ease.InOutSine))
                    .SetLink(gameObject);
            }
        }

        public void Toggle() => SetPlaying(!_playing);

        /// <summary>漂到下一个随机航点，到达后自动接续（无限链）</summary>
        void NextLeg()
        {
            var rect = Rect;
            float scale = Random.Range(minScale, maxScale);
            // 平移偏移限制在椭圆内，避免斜角方向偏出溢出余量
            Vector2 pan = _basePos + Random.insideUnitCircle * panRange;
            float duration = Random.Range(legDurationMin, legDurationMax);

            _seq = DOTween.Sequence()
                .Join(rect.DOAnchorPos(pan, duration).SetEase(Ease.InOutSine))
                .Join(rect.DOScale(_baseScale * scale, duration).SetEase(Ease.InOutSine))
                .OnComplete(NextLeg)
                .SetLink(gameObject);
        }
    }
}
