using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 镜头运动语言库：把电影运镜做成一行调用。
    ///   PushIn()    缓推 —— 重要台词时画面缓慢放大，压迫感（可指定焦点）
    ///   SnapZoom()  急推 —— 惊愕瞬间快速 zoom in（可联动轻震）
    ///   Pan()       平移 —— 视线引导到画面另一侧
    ///   DollyZoom() 眩晕镜头 —— 背景放大同时立绘反向补偿，名场面专用
    ///   ResetCamera() 复位
    /// 作用于 ZoomRoot 容器（震动在其父级 SceneRoot、荷兰角在其子级 TiltRoot，
    /// 三者互不干扰，可任意叠加）。
    /// </summary>
    public class VNCamera : MonoBehaviour
    {
        [Tooltip("镜头容器（场景生成器自动指向 ZoomRoot）")]
        public RectTransform target;

        [Tooltip("Dolly Zoom 时做反向缩放补偿的立绘")]
        public List<VNImageEffectController> dollyCharacters = new List<VNImageEffectController>();

        Vector2 _basePos;
        bool _cached;

        void Cache()
        {
            if (_cached || target == null) return;
            _basePos = target.anchoredPosition;
            _cached = true;
        }

        void KillTweens() => DOTween.Kill(this);

        /// <summary>
        /// 焦点补偿：绕中心放大后，把画面平移使 focus 点保持在原屏幕位置附近，
        /// 视觉上就是"镜头推向那个点"。focus 为画布中心坐标（立绘的 anchoredPosition 即可）。
        /// </summary>
        static Vector2 FocusOffset(Vector2? focusCanvasPos, float zoom) =>
            focusCanvasPos.HasValue ? -focusCanvasPos.Value * (zoom - 1f) : Vector2.zero;

        /// <summary>缓推：画面缓慢放大（默认 6%/5 秒），重要台词的压迫感</summary>
        public Sequence PushIn(float zoom = 1.06f, float duration = 5f, Vector2? focusCanvasPos = null)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            return DOTween.Sequence()
                .Append(target.DOScale(zoom, duration).SetEase(Ease.InOutSine))
                .Join(target.DOAnchorPos(_basePos + FocusOffset(focusCanvasPos, zoom), duration)
                            .SetEase(Ease.InOutSine))
                .SetTarget(this).SetLink(gameObject);
        }

        /// <summary>急推：惊愕瞬间快速 zoom in，可传入震动器在到位瞬间轻震</summary>
        public Sequence SnapZoom(float zoom = 1.12f, float duration = 0.16f,
            Vector2? focusCanvasPos = null, VNScreenShake shake = null)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            var seq = DOTween.Sequence()
                .Append(target.DOScale(zoom, duration).SetEase(Ease.OutQuad))
                .Join(target.DOAnchorPos(_basePos + FocusOffset(focusCanvasPos, zoom), duration)
                            .SetEase(Ease.OutQuad))
                .SetTarget(this).SetLink(gameObject);
            if (shake != null)
                seq.AppendCallback(() => shake.Shake(VNShakeLevel.Light));
            return seq;
        }

        /// <summary>
        /// 平移：把视线引向画布上某个点（如另一位角色的 anchoredPosition）。
        /// centering = 1 完全居中该点，0.5~0.7 只是偏过去（更自然）。
        /// </summary>
        public Tween Pan(Vector2 canvasPos, float centering = 0.6f, float duration = 1.2f)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            return target.DOAnchorPos(_basePos - canvasPos * centering, duration)
                         .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        /// <summary>
        /// Dolly Zoom 眩晕镜头：背景放大、立绘用缩放倍率反向补偿保持大小不变，
        /// 空间被"拉扯"的名场面效果。结束后记得 ResetCamera()。
        /// </summary>
        public Sequence DollyZoom(float zoom = 1.3f, float duration = 3f)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            var seq = DOTween.Sequence()
                .Append(target.DOScale(zoom, duration).SetEase(Ease.InOutQuad))
                .SetTarget(this).SetLink(gameObject);
            foreach (var c in dollyCharacters)
                if (c != null) seq.Join(c.DOScaleMultiplier(1f / zoom, duration));
            return seq;
        }

        /// <summary>镜头复位（缩放/平移/立绘补偿全部还原）</summary>
        public Sequence ResetCamera(float duration = 1f)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            var seq = DOTween.Sequence()
                .Append(target.DOScale(1f, duration).SetEase(Ease.InOutSine))
                .Join(target.DOAnchorPos(_basePos, duration).SetEase(Ease.InOutSine))
                .SetTarget(this).SetLink(gameObject);
            foreach (var c in dollyCharacters)
                if (c != null) seq.Join(c.DOScaleMultiplier(1f, duration));
            return seq;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
