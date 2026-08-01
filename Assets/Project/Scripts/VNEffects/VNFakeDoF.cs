using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 伪景深（Fake DoF）：对话特写时让背景"退后"，立绘浮出来。
    /// 组合四件事：背景 9-tap 微模糊 + 压暗 0.86 + 微降饱和 + 背景层微放大。
    /// 不用 URP 真 DoF —— Canvas UI 不写深度缓冲，真 DoF 会把立绘一起糊掉。
    /// 用法：fakeDoF.SetFocus(true) 进入特写；false 恢复。
    /// </summary>
    public class VNFakeDoF : MonoBehaviour
    {
        [Header("背景的特效控制器")]
        public VNImageEffectController backgroundFx;

        [Header("背景所在的视差层（缩放它而不是背景图，避开 Ken Burns 的缩放动画）")]
        public RectTransform backLayer;

        [Header("特写参数")]
        public float blurRadius = 0.006f;
        [Header("背景压暗亮度（1 = 不变暗）")]
        public float dimBrightness = 0.86f;
        [Header("背景饱和度保留比例")]
        public float saturation = 0.9f;
        [Header("背景层微放大倍率（拉开纵深）")]
        public float layerZoom = 1.035f;
        [Header("开/关过渡时长（秒）")]
        public float transition = 0.7f;

        bool _focused;

        public bool IsFocused => _focused;

        /// <summary>进入/退出特写景深</summary>
        public void SetFocus(bool on, float duration = -1f)
        {
            if (on == _focused) return;
            _focused = on;
            if (duration < 0f) duration = transition;

            if (backgroundFx != null)
            {
                backgroundFx.DOBlur(on ? blurRadius : 0f, duration);
                backgroundFx.DOBrightness(on ? dimBrightness : 1f, duration);
                backgroundFx.DOSaturation(on ? saturation : 1f, duration);
            }
            if (backLayer != null)
            {
                DOTween.Kill(this);
                backLayer.DOScale(on ? layerZoom : 1f, duration)
                         .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
            }
        }

        public void Toggle() => SetFocus(!_focused);

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
