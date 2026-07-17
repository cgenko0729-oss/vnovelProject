using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 荷兰角（Dutch Angle）：把画面容器缓慢倾斜 2~4°，
    /// 心理不安/紧张/异常场景的经典电影手法。
    /// 倾斜的同时按角度自动放大画面，保证旋转后四角不露底。
    /// 作用于 TiltRoot 容器（与屏幕震动的 SceneRoot 分离，互不干扰）。
    /// </summary>
    public class VNDutchAngle : MonoBehaviour
    {
        [Header("被倾斜的容器（场景生成器自动指向 TiltRoot）")]
        public RectTransform target;

        [Header("默认倾斜角度（度）")]
        public float angle = 3f;

        [Header("倾斜过渡时长（秒）")]
        public float duration = 1.4f;

        [Header("画面宽高比（用于计算防露角的放大量）")]
        public float aspect = 16f / 9f;

        bool _tilted;

        public bool IsTilted => _tilted;

        /// <summary>倾斜到指定角度（0 = 回正）</summary>
        public void SetTilt(float degrees, float transitionDuration = -1f)
        {
            if (target == null) return;
            if (transitionDuration < 0f) transitionDuration = duration;
            _tilted = Mathf.Abs(degrees) > 0.01f;

            // 旋转 θ 后要覆盖整个矩形屏幕所需的放大：cosθ + aspect·sin|θ|
            float rad = Mathf.Abs(degrees) * Mathf.Deg2Rad;
            float scale = Mathf.Cos(rad) + aspect * Mathf.Sin(rad);

            DOTween.Kill(this);
            target.DOLocalRotate(new Vector3(0f, 0f, degrees), transitionDuration)
                  .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
            target.DOScale(scale, transitionDuration)
                  .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        /// <summary>回正</summary>
        public void Clear(float transitionDuration = -1f) => SetTilt(0f, transitionDuration);

        public void Toggle()
        {
            if (_tilted) Clear();
            else SetTilt(angle);
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
