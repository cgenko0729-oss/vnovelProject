using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>震动等级</summary>
    public enum VNShakeLevel
    {
        Light,  // 轻震：心跳、紧张（6px / 0.25s）
        Medium, // 中震：惊吓、撞击（16px / 0.4s）
        Heavy,  // 强震：爆炸、冲击（34px / 0.6s + 旋转抖动）
    }

    /// <summary>
    /// 分级屏幕震动系统。
    /// 因为 Canvas 是 Screen Space - Camera（震相机 UI 不会动），
    /// 所以震的是"SceneRoot"容器（背景+光束+立绘都在里面），
    /// 提示文字/对话框等 UI 保持稳定——这正是想要的效果。
    /// 每次震动前重置回基准位，连续触发不会漂移。
    /// </summary>
    public class VNScreenShake : MonoBehaviour
    {
        [Header("被震动的容器（场景生成器自动指向 SceneRoot）")]
        public RectTransform target;

        Vector2 _basePos;
        bool _cached;

        /// <summary>按等级震动</summary>
        public Tween Shake(VNShakeLevel level)
        {
            switch (level)
            {
                case VNShakeLevel.Light: return Shake(6f, 0.25f, 18, 0f);
                case VNShakeLevel.Medium: return Shake(16f, 0.4f, 22, 0f);
                default: return Shake(34f, 0.6f, 26, 1.4f);
            }
        }

        /// <summary>自定义震动（strength：像素；rotationDeg &gt; 0 时叠加旋转抖动）</summary>
        public Tween Shake(float strength, float duration, int vibrato = 20, float rotationDeg = 0f)
        {
            if (target == null) return null;

            if (!_cached)
            {
                _basePos = target.anchoredPosition;
                _cached = true;
            }

            // 打断上一次震动并复位，防止基准漂移
            DOTween.Kill(this);
            target.anchoredPosition = _basePos;
            target.localRotation = Quaternion.identity;

            var t = target.DOShakeAnchorPos(duration, new Vector2(strength, strength * 0.7f),
                                            vibrato, 90f, false, true)
                          .SetTarget(this).SetLink(gameObject);
            if (rotationDeg > 0f)
            {
                target.DOShakeRotation(duration, new Vector3(0f, 0f, rotationDeg), vibrato, 90f, true)
                      .SetTarget(this).SetLink(gameObject);
            }
            return t;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
