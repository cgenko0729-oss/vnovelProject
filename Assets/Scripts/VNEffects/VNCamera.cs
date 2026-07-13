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

        // ------------------------------------------------------------------
        // 路径镜头（camseq / camto / camcut）
        // ------------------------------------------------------------------

        [Header("路径镜头")]
        [Tooltip("自动钳制偏移量，防止高倍缩放对准边角时露出画布边缘")]
        public bool clampToCanvas = true;

        [Tooltip("画布半尺寸（1920×1080 的一半）")]
        public Vector2 canvasHalf = new Vector2(960f, 540f);

        [Tooltip("背景图的四边溢出量（生成器默认 60px）")]
        public Vector2 overscan = new Vector2(60f, 60f);

        /// <summary>一个镜头路径点</summary>
        public struct Waypoint
        {
            public Vector2 point;   // 看向的画布坐标（中心为原点）
            public float zoom;      // 缩放倍率
            public float duration;  // 到达本点的时长（≤0 = 瞬切）
            public Ease ease;       // 本段缓动（easeSet 为 true 时生效）
            public bool easeSet;
        }

        /// <summary>
        /// "看向点 p"（居中语义）所需的容器偏移，含防露边钳制。
        /// 静态版供编辑器预览共用同一份公式。
        /// </summary>
        public static Vector2 ComputeOffset(Vector2 point, float zoom,
            Vector2 canvasHalf, Vector2 overscan, bool clamp)
        {
            var o = -point * zoom;
            if (clamp)
            {
                var max = (canvasHalf + overscan) * zoom - canvasHalf;
                max = Vector2.Max(max, Vector2.zero);
                o.x = Mathf.Clamp(o.x, -max.x, max.x);
                o.y = Mathf.Clamp(o.y, -max.y, max.y);
            }
            return o;
        }

        Vector2 OffsetFor(Vector2 point, float zoom) =>
            ComputeOffset(point, zoom, canvasHalf, overscan, clampToCanvas);

        /// <summary>瞬切到镜头状态（"一开始就已经 zoom 在那里"的起手）</summary>
        public void Cut(Vector2 point, float zoom)
        {
            Cache();
            if (target == null) return;
            KillTweens();
            target.localScale = Vector3.one * zoom;
            target.anchoredPosition = _basePos + OffsetFor(point, zoom);
        }

        /// <summary>单段直达：补间到指定镜头状态</summary>
        public Sequence GoTo(Vector2 point, float zoom, float duration, Ease ease = Ease.InOutSine)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            return DOTween.Sequence()
                .Append(target.DOScale(zoom, duration).SetEase(ease))
                .Join(target.DOAnchorPos(_basePos + OffsetFor(point, zoom), duration).SetEase(ease))
                .SetTarget(this).SetLink(gameObject);
        }

        /// <summary>
        /// 多段镜头路径：整条路径编成一条 Sequence（可等待/可异步）。
        /// 默认缓动让整条路径像一次连续运镜：
        /// 首个移动段 InSine（从静止缓起）、中间段 Linear（匀速）、末段 OutSine（缓停）；
        /// 单段路径用 InOutSine；每个路径点可用 ease 覆盖。
        /// </summary>
        public Sequence PlayPath(System.Collections.Generic.List<Waypoint> points)
        {
            Cache();
            if (target == null || points == null || points.Count == 0) return null;
            KillTweens();

            // 找出第一个/最后一个"移动段"（时长>0），用于默认缓动分配
            int firstMove = -1, lastMove = -1, moveCount = 0;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].duration > 0.001f)
                {
                    if (firstMove < 0) firstMove = i;
                    lastMove = i;
                    moveCount++;
                }
            }

            var seq = DOTween.Sequence().SetTarget(this).SetLink(gameObject);
            for (int i = 0; i < points.Count; i++)
            {
                var wp = points[i];
                float zoom = Mathf.Max(0.1f, wp.zoom);
                var pos = _basePos + OffsetFor(wp.point, zoom);

                if (wp.duration <= 0.001f)
                {
                    // 瞬切段
                    seq.AppendCallback(() =>
                    {
                        target.localScale = Vector3.one * zoom;
                        target.anchoredPosition = pos;
                    });
                    continue;
                }

                Ease ease = wp.easeSet ? wp.ease
                    : moveCount <= 1 ? Ease.InOutSine
                    : i == firstMove ? Ease.InSine
                    : i == lastMove ? Ease.OutSine
                    : Ease.Linear;

                seq.Append(target.DOScale(zoom, wp.duration).SetEase(ease));
                seq.Join(target.DOAnchorPos(pos, wp.duration).SetEase(ease));
            }
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
