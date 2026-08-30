using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// camseq 的缩放模式——「谁跟着 zoom 一起缩放」。
    /// 对应演出手册第 1 章里被拆成三条的缩放手法（VN 的 2D 舞台上它们是不同的东西）：
    ///   Both  トラックアップ/バック（TU/TB）推拉镜：背景+立绘同步等比放大。最常用，默认。
    ///   Depth 带纵深的推拉镜：立绘比背景多缩放一点。真镜头推近时近处的人放大得比远景快，
    ///         等比缩放其实是「数码变焦」；这一档才是手册说的「速度差是伪 3D 的全部秘密」。
    ///   Bg    ドリーズーム/めまいショット 眩晕变焦：只有背景放大、立绘尺寸不变。
    ///         杀伤力极强，全篇用 1~2 次。
    ///   Char  ズームイン/アウト 变焦推拉：只放大立绘，**背景纹丝不动**（连平移都不做）。
    ///         用于强调某人的反应；背景分辨率不够时也靠它做特写而不糊背景。
    /// </summary>
    public enum VNCamZoomMode { Both, Depth, Bg, Char }

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
        [Header("镜头容器（场景生成器自动指向 ZoomRoot）")]
        public RectTransform target;

        [Header("参与运镜缩放的立绘（VNStage 在角色进出场时自动维护）")]
        public List<VNImageEffectController> dollyCharacters = new List<VNImageEffectController>();

        [Header("mode:depth 的视差系数——立绘额外倍率 = 1+(zoom-1)×k")]
        [Range(0f, 1.5f)] public float depthRatio = DefaultDepthRatio;

        /// <summary>
        /// mode:depth 的默认视差系数。手册 13 章：「同一部作品内部保持一致比数值本身正确更重要」，
        /// 所以这是一个全局值、不做逐点覆盖。编辑器预览读的也是这一个常量。
        /// </summary>
        public const float DefaultDepthRatio = 0.5f;

        /// <summary>某个缩放模式下，立绘相对背景的额外倍率（编辑器预览共用同一份公式）</summary>
        public static float CharacterScaleFor(VNCamZoomMode mode, float zoom,
            float depthRatio = DefaultDepthRatio)
        {
            zoom = Mathf.Max(0.1f, zoom);
            switch (mode)
            {
                case VNCamZoomMode.Depth: return 1f + (zoom - 1f) * depthRatio;
                case VNCamZoomMode.Bg: return 1f / zoom;   // 立绘尺寸不变 = 抵消掉背景的放大
                case VNCamZoomMode.Char: return zoom;      // 背景不动，放大量全落在立绘上
                default: return 1f;
            }
        }

        /// <summary>某个缩放模式下 ZoomRoot 实际用的倍率（Char 模式下背景完全不动）</summary>
        public static float ContainerZoomFor(VNCamZoomMode mode, float zoom) =>
            mode == VNCamZoomMode.Char ? 1f : Mathf.Max(0.1f, zoom);

        /// <summary>本次路径的缩放模式（PlayPathCo / PlayPath 开始时设置，供各段共用）</summary>
        VNCamZoomMode _mode = VNCamZoomMode.Both;

        /// <summary>把某一段的立绘额外倍率派发给全部在场立绘（时长/缓动与镜头本段一致）</summary>
        void ApplyCharacterZoom(float zoom, float duration, Ease ease)
        {
            if (_mode == VNCamZoomMode.Both) return;   // 默认模式不碰立绘倍率，等于零开销
            float mult = CharacterScaleFor(_mode, zoom, depthRatio);
            foreach (var c in dollyCharacters)
                if (c != null) c.DOCamScaleMultiplier(mult, duration, ease);
        }

        /// <summary>
        /// 切换本次运镜的缩放模式。**从非 both 切回 both 时必须在这里显式还原立绘倍率**——
        /// ApplyCharacterZoom 在 both 下是空转（否则每个路径点都要给每个立绘起一条补间，
        /// 顺带把说话者高亮的补间打断），所以还原没有别的地方可做。
        /// camcut / camto / 复位走的都是这个入口，camseq 跑完不复位也不会污染下一条命令。
        /// </summary>
        void SetMode(VNCamZoomMode mode, float resetDuration = 0f)
        {
            if (mode == VNCamZoomMode.Both && _mode != VNCamZoomMode.Both)
                ResetCharacterZoom(resetDuration);
            _mode = mode;
        }

        /// <summary>还原全部立绘的运镜通道倍率（不碰说话者高亮那一路）</summary>
        void ResetCharacterZoom(float duration)
        {
            foreach (var c in dollyCharacters)
                if (c != null) c.DOCamScaleMultiplier(1f, duration);
        }

        /// <summary>VNStage 在角色进出场后调用，与 VNMoodGrading.SetCharacterTargets 同一时机</summary>
        public void SetCharacterTargets(IEnumerable<VNImageEffectController> characters)
        {
            dollyCharacters.Clear();
            if (characters == null) return;
            foreach (var c in characters)
                if (c != null) dollyCharacters.Add(c);
        }

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
            // 走运镜通道：直接写手动通道的话，玩家点下一句台词说话者高亮一改倍率，
            // 立绘尺寸就会当场跳回去（与 camseq mode:bg 同一份公式）
            _mode = VNCamZoomMode.Bg;   // 登记模式，下一条 both 运镜才知道要把立绘还原回去
            foreach (var c in dollyCharacters)
                if (c != null) c.DOCamScaleMultiplier(1f / zoom, duration, Ease.InOutQuad);
            return seq;
        }

        // ------------------------------------------------------------------
        // 路径镜头（camseq / camto / camcut）
        // ------------------------------------------------------------------

        [Header("路径镜头")]
        [Header("自动钳制偏移量，防止高倍缩放对准边角时露出画布边缘")]
        public bool clampToCanvas = true;

        [Header("画布半尺寸（1920×1080 的一半）")]
        public Vector2 canvasHalf = new Vector2(960f, 540f);

        [Header("背景图的四边溢出量（生成器默认 60px）")]
        public Vector2 overscan = new Vector2(60f, 60f);

        [Header("镜头交叉淡化组件（留空则首次使用时自动创建在 Canvas 下）")]
        public VNCameraFade cameraFade;

        /// <summary>一个镜头路径点</summary>
        public struct Waypoint
        {
            public Vector2 point;   // 看向的画布坐标（中心为原点）
            public float zoom;      // 缩放倍率
            public float duration;  // 到达本点的时长（≤0 = 瞬切）
            public Ease ease;       // 本段缓动（easeSet 为 true 时生效）
            public bool easeSet;
            public float fade;      // >0 = 交叉淡化到本点（秒），代替平移/瞬切
            public float hold;      // >0 = 到达本点后停留的秒数（停在原地再走下一段）
            public VNShakeSpec shake; // Valid = 到达本点的瞬间震一下（震完才走下一段）
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

        /// <summary>
        /// 瞬切到镜头状态（camcut）。**公开入口一律按 both 处理并还原立绘倍率**——
        /// camcut 语法里没有缩放模式，继承上一段 camseq 的模式只会莫名其妙。
        /// </summary>
        public void Cut(Vector2 point, float zoom)
        {
            SetMode(VNCamZoomMode.Both);
            CutWithMode(point, zoom);
        }

        /// <summary>带当前模式的瞬切（camseq 的叠化段内部用）</summary>
        void CutWithMode(Vector2 point, float zoom)
        {
            Cache();
            if (target == null) return;
            KillTweens();
            float cz = ContainerZoomFor(_mode, zoom);
            target.localScale = Vector3.one * cz;
            // Char 模式下背景连平移都不做：zoom=1 时 ComputeOffset 仍会给出一个 overscan
            // 级别的偏移，那点位移会让「背景纹丝不动」的语义打折
            target.anchoredPosition = _mode == VNCamZoomMode.Char
                ? _basePos : _basePos + OffsetFor(point, cz);
            ApplyCharacterZoom(zoom, 0f, Ease.Linear);
        }

        /// <summary>单段直达：补间到指定镜头状态</summary>
        public Sequence GoTo(Vector2 point, float zoom, float duration, Ease ease = Ease.InOutSine)
        {
            Cache();
            if (target == null) return null;
            KillTweens();
            SetMode(VNCamZoomMode.Both, duration);   // camto 没有模式概念，顺带还原上一段留下的立绘倍率
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
        public Sequence PlayPath(System.Collections.Generic.List<Waypoint> points,
            VNCamZoomMode mode = VNCamZoomMode.Both)
        {
            Cache();
            if (target == null || points == null || points.Count == 0) return null;
            KillTweens();
            SetMode(mode);
            return BuildSegment(points, 0, points.Count);
        }

        /// <summary>把 [from, to) 区间的普通点编成一条 Sequence（缓动分配同 PlayPath）</summary>
        Sequence BuildSegment(System.Collections.Generic.List<Waypoint> points, int from, int to,
            VNScreenShake shaker = null)
        {
            // 找出第一个/最后一个"移动段"（时长>0），用于默认缓动分配
            int firstMove = -1, lastMove = -1, moveCount = 0;
            for (int i = from; i < to; i++)
            {
                if (points[i].duration > 0.001f)
                {
                    if (firstMove < 0) firstMove = i;
                    lastMove = i;
                    moveCount++;
                }
            }

            var seq = DOTween.Sequence().SetTarget(this).SetLink(gameObject);
            for (int i = from; i < to; i++)
            {
                var wp = points[i];
                float zoom = Mathf.Max(0.1f, wp.zoom);
                float cz = ContainerZoomFor(_mode, zoom);
                var pos = _mode == VNCamZoomMode.Char
                    ? _basePos : _basePos + OffsetFor(wp.point, cz);

                if (wp.duration <= 0.001f)
                {
                    // 瞬切段
                    seq.AppendCallback(() =>
                    {
                        target.localScale = Vector3.one * cz;
                        target.anchoredPosition = pos;
                        ApplyCharacterZoom(zoom, 0f, Ease.Linear);
                    });
                }
                else
                {
                    Ease ease = wp.easeSet ? wp.ease
                        : moveCount <= 1 ? Ease.InOutSine
                        : i == firstMove ? Ease.InSine
                        : i == lastMove ? Ease.OutSine
                        : Ease.Linear;

                    // 立绘倍率起独立补间（同 shake 的套路），不 Join 进本 Sequence：
                    // 它的 target 是立绘的 VNImageEffectController，被那边 Kill 掉时
                    // 不该连累整条镜头序列。时长与缓动照抄本段，观感上就是同步的
                    float wpZoom = zoom, wpDur = wp.duration;
                    Ease wpEase = ease;
                    seq.AppendCallback(() => ApplyCharacterZoom(wpZoom, wpDur, wpEase));
                    seq.Append(target.DOScale(cz, wp.duration).SetEase(ease));
                    seq.Join(target.DOAnchorPos(pos, wp.duration).SetEase(ease));
                }

                // 到点即震。触发也编进 Sequence（而不是协程里 yield），
                // 这样它和 hold、和后续段共用同一条时间轴，Skip 快进时不会错位
                float stall = wp.hold;
                if (shaker != null && wp.shake.Valid)
                {
                    var spec = wp.shake;
                    seq.AppendCallback(() => shaker.Shake(spec));
                    // 「震完再走下一段」：把该点的停顿撑到至少一次震动那么长。
                    // 与 hold 取较大值而不是相加——hold:1 就该老老实实停 1 秒
                    stall = Mathf.Max(stall, spec.duration);
                }

                // hold：到点后停在原地。编进同一条 Sequence，Skip 的 DOTween.timeScale
                // 加速对它同样生效（用 WaitForSeconds 就会在快进时变成唯一的卡点）
                if (stall > 0.001f) seq.AppendInterval(stall);
            }
            return seq;
        }

        /// <summary>
        /// 带交叉淡化的镜头路径（协程版，供 VNScriptRunner 的 camseq 使用）：
        ///   startFade > 0 —— 先截屏当前画面，瞬切到首点后淡出（"全图叠化进首镜头"）
        ///   wp.fade > 0   —— 该点用"截屏→瞬切→淡出"代替平移/瞬切
        ///   endFade > 0   —— 路径走完后截屏、瞬间复位、淡出（"末镜头叠化回全图"）
        /// 连续的普通点仍合成一条 Sequence，保持与 PlayPath 相同的连贯缓动手感。
        /// </summary>
        public IEnumerator PlayPathCo(System.Collections.Generic.List<Waypoint> points,
            float startFade = 0f, float endFade = 0f, VNScreenShake shaker = null,
            VNCamZoomMode mode = VNCamZoomMode.Both)
        {
            Cache();
            if (target == null || points == null) yield break;
            KillTweens();
            SetMode(mode);

            int i = 0;
            if (startFade > 0.001f && points.Count > 0)
            {
                yield return CrossfadeTo(points[0].point, points[0].zoom, startFade);
                yield return ShakeHoldCo(points[0], shaker);
                i = 1;
            }

            while (i < points.Count)
            {
                if (points[i].fade > 0.001f)
                {
                    yield return CrossfadeTo(points[i].point, points[i].zoom, points[i].fade);
                    yield return ShakeHoldCo(points[i], shaker);
                    i++;
                    continue;
                }
                int j = i + 1;
                while (j < points.Count && points[j].fade <= 0.001f) j++;
                var seq = BuildSegment(points, i, j, shaker);
                if (seq != null) yield return seq.WaitForCompletion();
                i = j;
            }

            if (endFade > 0.001f)
            {
                var fade = EnsureFade();
                if (fade != null)
                {
                    yield return fade.CaptureCo();
                    SnapReset();
                    var t = fade.FadeOut(endFade);
                    if (t != null) yield return t.WaitForCompletion();
                }
                else
                {
                    var t = ResetCamera(endFade); // 没有淡化组件就退化为普通补间复位
                    if (t != null) yield return t.WaitForCompletion();
                }
            }
        }

        /// <summary>
        /// 叠化段到点后的「震一下 + 停留」。补间段走 Sequence 里的 AppendCallback，
        /// 叠化段不在 Sequence 里所以在这儿触发，两条路径的时长规则保持一致：
        /// 停顿 = max(hold, 震动时长)。
        /// </summary>
        IEnumerator ShakeHoldCo(Waypoint wp, VNScreenShake shaker)
        {
            float stall = wp.hold;
            if (shaker != null && wp.shake.Valid)
            {
                shaker.Shake(wp.shake);
                stall = Mathf.Max(stall, wp.shake.duration);
            }
            yield return HoldCo(stall);
        }

        /// <summary>
        /// 叠化段之后的 hold（普通补间段的 hold 直接编进 Sequence，见 BuildSegment）。
        /// 同样走 DOTween 而不是 WaitForSeconds，Skip 加速才对所有段一致生效。
        /// </summary>
        IEnumerator HoldCo(float seconds)
        {
            if (seconds <= 0.001f) yield break;
            var t = DOTween.Sequence().AppendInterval(seconds)
                .SetTarget(this).SetLink(gameObject);
            yield return t.WaitForCompletion();
        }

        /// <summary>截屏当前画面 → 瞬切到目标镜头 → 截图淡出</summary>
        IEnumerator CrossfadeTo(Vector2 point, float zoom, float duration)
        {
            var fade = EnsureFade();
            if (fade == null)
            {
                CutWithMode(point, Mathf.Max(0.1f, zoom)); // 退化为瞬切
                yield break;
            }
            yield return fade.CaptureCo();
            CutWithMode(point, Mathf.Max(0.1f, zoom));
            var t = fade.FadeOut(duration);
            if (t != null) yield return t.WaitForCompletion();
        }

        VNCameraFade EnsureFade()
        {
            if (cameraFade != null) return cameraFade;
            cameraFade = FindFirstObjectByType<VNCameraFade>();
            if (cameraFade == null && target != null)
            {
                var canvas = target.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    var go = new GameObject("CameraFade", typeof(RectTransform));
                    go.transform.SetParent(canvas.rootCanvas.transform, false);
                    cameraFade = go.AddComponent<VNCameraFade>();
                }
            }
            return cameraFade;
        }

        /// <summary>瞬间复位（end:fade 截屏后调用：睁眼即是复位视角）</summary>
        public void SnapReset()
        {
            Cache();
            if (target == null) return;
            KillTweens();
            target.localScale = Vector3.one;
            target.anchoredPosition = _basePos;
            SetMode(VNCamZoomMode.Both);
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
            // 复位只还原运镜通道；写 DOScaleMultiplier 会顺手抹掉说话者高亮的倍率
            SetMode(VNCamZoomMode.Both, duration);
            return seq;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
