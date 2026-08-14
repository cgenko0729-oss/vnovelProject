using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>出场演出预设（前四个是日常向：不带粒子/光环，适合频繁的对话切换）</summary>
    public enum VNEntrancePreset
    {
        /// <summary>原地淡入，无位移无粒子（最朴素，默认值）</summary>
        Crossfade,
        /// <summary>从画面外滑入 + 淡入（方向默认按站位推断）</summary>
        SlideIn,
        /// <summary>滑入 + 落地小弹跳 + 脚下影同步扩散（有重量的登场）</summary>
        StepIn,
        /// <summary>走入：匀速位移 + 左右轻摆 + 步伐起伏（人真的走进来）</summary>
        WalkIn,
        /// <summary>噪声溶解显形 + 辉光边缘 + 星光爆发（最华丽，推荐重要角色首次登场）</summary>
        DissolveGlow,
        /// <summary>从下方轻盈滑入 + 淡入</summary>
        FadeSlideUp,
        /// <summary>弹性缩放弹出 + 微闪白（俏皮/惊喜登场）</summary>
        ScaleBounce,
        /// <summary>淡入后一道扫光掠过（优雅登场）</summary>
        ShineReveal,
        /// <summary>爆闪 + 光环闪耀中显形（高潮/重要角色登场）</summary>
        FlashBloom,
        /// <summary>高速冲入 + 身后拖出递减残影（惊喜/战斗系登场）</summary>
        AfterimageDash,
    }

    /// <summary>退场演出预设</summary>
    public enum VNExitPreset
    {
        /// <summary>淡出并轻微下滑（日常，默认值）</summary>
        Fade,
        /// <summary>溶解成光点消散</summary>
        Dissolve,
        /// <summary>快速跑出画面 + 前倾（吵架/逃跑；方向默认按站位推断）</summary>
        RunOut,
        /// <summary>下沉 + 模糊 + 变暗（消失/昏迷/失去意识）</summary>
        Sink,
    }

    /// <summary>出入场的方向（Auto = 由站位推断）</summary>
    public enum VNSide
    {
        Auto,
        Left,
        Right,
        Top,
        Bottom,
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
        [Header("出场时星光爆发的粒子颜色")]
        public Color burstColor = new Color(1f, 0.92f, 0.6f, 1f);

        [Header("是否在出场时触发星光爆发粒子")]
        public bool useParticleBurst = true;

        VNImageEffectController _fx;
        CanvasGroup _group;
        VNGlowBackdrop _backdrop;   // 可选
        VNFootShadow _footShadow;   // 可选（StepIn 落地时联动）
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
            _footShadow = GetComponent<VNFootShadow>();
            CacheBase();
        }

        void CacheBase()
        {
            if (_baseCached) return;
            _basePos = _fx.Rect.anchoredPosition;
            _baseScale = _fx.Rect.localScale;
            _baseCached = true;
        }

        /// <summary>当前基准位置（脚下阴影等组件动态读取）</summary>
        public Vector2 BasePosition
        {
            get { CacheBase(); return _basePos; }
        }

        /// <summary>
        /// 重设基准位置。剧本系统改变角色站位后必须调用，
        /// 否则 PlayEntrance 的 PrepareHidden 会把角色重置回旧基准位。
        /// </summary>
        public void SetBasePosition(Vector2 pos)
        {
            CacheBase(); // 先保证缩放基准已缓存
            _basePos = pos;
        }

        /// <summary>把图片重置到"完全隐藏"状态，准备出场</summary>
        public void PrepareHidden()
        {
            CacheBase();
            KillCurrent();
            _fx.StopAllLoops();
            _fx.ResetScaleMultiplier(); // 清掉说话者高亮的缩放倍率
            // 清掉退场压暗与说话者高亮的压暗（Mood 通道不动：情绪色调是全局的，
            // 角色一出场就该带着当前情绪的颜色）
            _fx.ClearGrade(VNGradeLayer.Emote);
            _fx.ClearGrade(VNGradeLayer.Focus);
            _group.alpha = 0f;
            _fx.SetDissolve(0f);
            _fx.SetFlash(0f);
            _fx.Rect.anchoredPosition = _basePos;
            _fx.Rect.localScale = _baseScale;
            _fx.Rect.localRotation = Quaternion.Euler(_fx.RotationEuler()); // 清掉动作倾斜，保留素材校准角
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

        /// <summary>
        /// 各预设的基准时长（秒）。剧本写 dur:1.2 时按 dur / 基准 换算成倍率，
        /// 这样"想要多久"是直接写秒数，而不用去猜每个预设本来多长。
        /// </summary>
        public static float BaseDuration(VNEntrancePreset preset)
        {
            switch (preset)
            {
                case VNEntrancePreset.Crossfade: return 0.55f;
                case VNEntrancePreset.SlideIn: return 0.8f;
                case VNEntrancePreset.StepIn: return 0.9f;
                case VNEntrancePreset.WalkIn: return 1.1f;
                case VNEntrancePreset.DissolveGlow: return 1.6f;
                case VNEntrancePreset.FadeSlideUp: return 0.8f;
                case VNEntrancePreset.ScaleBounce: return 0.65f;
                case VNEntrancePreset.ShineReveal: return 1.3f;
                case VNEntrancePreset.FlashBloom: return 1.6f;
                case VNEntrancePreset.AfterimageDash: return 0.4f;
                default: return 0.8f;
            }
        }

        public static float BaseDuration(VNExitPreset preset)
        {
            switch (preset)
            {
                case VNExitPreset.Dissolve: return 1f;
                case VNExitPreset.RunOut: return 0.5f;
                case VNExitPreset.Sink: return 0.9f;
                default: return 0.6f;
            }
        }

        /// <summary>
        /// 日常向预设：登场后不开周期扫光（每隔几秒闪一下对日常对话太吵），
        /// 只保留呼吸发光与悬浮。VNStage 据此决定 StartIdleEffects 的参数。
        /// </summary>
        public static bool IsCasual(VNEntrancePreset preset) =>
            preset == VNEntrancePreset.Crossfade || preset == VNEntrancePreset.SlideIn ||
            preset == VNEntrancePreset.StepIn || preset == VNEntrancePreset.WalkIn;

        /// <summary>播放指定预设的出场演出（旧签名：倍率版，无方向）</summary>
        public Sequence PlayEntrance(VNEntrancePreset preset, float durationScale = 1f) =>
            PlayEntrance(preset, VNSide.Auto, durationScale);

        /// <summary>
        /// 播放出场演出。side = 从哪个方向进来（Auto 由调用方按站位换算好，
        /// 这里再兜一层）；durationScale 是相对预设基准时长的倍率。
        /// </summary>
        public Sequence PlayEntrance(VNEntrancePreset preset, VNSide side, float durationScale)
        {
            PrepareHidden();
            float k = durationScale > 0.01f ? durationScale : 1f;
            switch (preset)
            {
                case VNEntrancePreset.Crossfade: _current = BuildCrossfade(k); break;
                case VNEntrancePreset.SlideIn: _current = BuildSlideIn(k, side); break;
                case VNEntrancePreset.StepIn: _current = BuildStepIn(k, side); break;
                case VNEntrancePreset.WalkIn: _current = BuildWalkIn(k, side); break;
                case VNEntrancePreset.DissolveGlow: _current = BuildDissolveGlow(k); break;
                case VNEntrancePreset.FadeSlideUp: _current = BuildFadeSlideUp(k); break;
                case VNEntrancePreset.ScaleBounce: _current = BuildScaleBounce(k); break;
                case VNEntrancePreset.ShineReveal: _current = BuildShineReveal(k); break;
                case VNEntrancePreset.FlashBloom: _current = BuildFlashBloom(k); break;
                case VNEntrancePreset.AfterimageDash: _current = BuildAfterimageDash(k); break;
                default: _current = BuildCrossfade(k); break;
            }
            _current.SetLink(gameObject);
            return _current;
        }

        /// <summary>方向兜底：没给方向时默认从下方；只走水平的预设把上下折成左</summary>
        static VNSide Resolve(VNSide side, bool horizontalOnly)
        {
            if (side == VNSide.Auto) side = horizontalOnly ? VNSide.Left : VNSide.Bottom;
            if (horizontalOnly && (side == VNSide.Top || side == VNSide.Bottom))
                side = VNSide.Left;
            return side;
        }

        /// <summary>该方向的进场偏移向量（角色从 base + 此偏移 滑到 base）</summary>
        static Vector2 EnterOffset(VNSide side, float horizontal, float vertical)
        {
            switch (side)
            {
                case VNSide.Left: return new Vector2(-horizontal, 0f);
                case VNSide.Right: return new Vector2(horizontal, 0f);
                case VNSide.Top: return new Vector2(0f, vertical);
                default: return new Vector2(0f, -vertical);
            }
        }

        // ---- 日常向四件套（不带粒子/光环，适合频繁的对话切换）----

        /// <summary>原地淡入：没有位移、没有粒子、没有闪光</summary>
        Sequence BuildCrossfade(float k)
        {
            _fx.SetDissolve(1f);
            return DOTween.Sequence()
                .Append(_group.DOFade(1f, 0.55f * k).SetEase(Ease.InOutSine));
        }

        /// <summary>四方向滑入 + 淡入</summary>
        Sequence BuildSlideIn(float k, VNSide side)
        {
            _fx.SetDissolve(1f);
            _fx.Rect.anchoredPosition = _basePos + EnterOffset(Resolve(side, false), 190f, 110f);
            return DOTween.Sequence()
                .Append(_group.DOFade(1f, 0.5f * k).SetEase(Ease.OutQuad))
                .Join(_fx.Rect.DOAnchorPos(_basePos, 0.8f * k).SetEase(Ease.OutCubic));
        }

        /// <summary>
        /// 滑入 + 落地：先滑到略高处，落下时脚下影同步压扁扩散，再弹一下收住。
        /// 不震屏——那是给冲击型登场（slam）留的。
        /// </summary>
        Sequence BuildStepIn(float k, VNSide side)
        {
            _fx.SetDissolve(1f);
            var from = _basePos + EnterOffset(Resolve(side, false), 170f, 90f);
            _fx.Rect.anchoredPosition = from + new Vector2(0f, 26f);

            var seq = DOTween.Sequence();
            seq.Append(_fx.Rect.DOAnchorPos(_basePos + new Vector2(0f, 26f), 0.46f * k)
                              .SetEase(Ease.OutQuad));
            seq.Join(_group.DOFade(1f, 0.4f * k).SetEase(Ease.OutQuad));
            // 落地
            seq.Append(_fx.Rect.DOAnchorPosY(_basePos.y, 0.14f * k).SetEase(Ease.InQuad));
            seq.AppendCallback(() => _footShadow?.Impact(1.4f, 0.3f));
            // 落地反弹（小）
            seq.Append(_fx.Rect.DOAnchorPosY(_basePos.y + 11f, 0.13f * k).SetEase(Ease.OutQuad));
            seq.Append(_fx.Rect.DOAnchorPosY(_basePos.y, 0.17f * k).SetEase(Ease.InQuad));
            return seq;
        }

        /// <summary>
        /// 走入：横向匀速位移，同时纵向做步伐起伏、左右轻摆、纵向微压缩，
        /// 三条节奏用同一个步长对齐。到位后强制把旋转/缩放归位，不留残迹。
        /// </summary>
        Sequence BuildWalkIn(float k, VNSide side)
        {
            _fx.SetDissolve(1f);
            var resolved = Resolve(side, true);
            float dir = resolved == VNSide.Right ? 1f : -1f;
            _fx.Rect.anchoredPosition = _basePos + new Vector2(dir * 300f, 0f);
            _fx.Rect.localRotation = Quaternion.Euler(_fx.RotationEuler(-1.3f * dir));

            float total = 1.1f * k;
            const int steps = 4;                 // 4 步走到位
            float half = total / (steps * 2f);   // 半步 = 一次起伏

            var seq = DOTween.Sequence();
            // 横向匀速（走路不该有加减速），淡入只占前段
            seq.Append(_fx.Rect.DOAnchorPosX(_basePos.x, total).SetEase(Ease.Linear));
            seq.Join(_group.DOFade(1f, total * 0.45f).SetEase(Ease.OutQuad));
            // 步伐起伏 / 左右轻摆 / 踩地压缩：同一步长，Yoyo 回到原值
            seq.Join(_fx.Rect.DOAnchorPosY(_basePos.y + 8f, half)
                             .SetEase(Ease.InOutSine).SetLoops(steps * 2, LoopType.Yoyo));
            seq.Join(_fx.Rect.DOLocalRotate(_fx.RotationEuler(1.3f * dir), half * 2f)
                             .SetEase(Ease.InOutSine).SetLoops(steps, LoopType.Yoyo));
            seq.Join(_fx.Rect.DOScaleY(_baseScale.y * 0.988f, half)
                             .SetEase(Ease.InOutSine).SetLoops(steps * 2, LoopType.Yoyo));
            seq.OnComplete(() =>
            {
                // Yoyo 的循环次数是偶数，理论上自然回到原值；这里兜底防抖动残留
                _fx.Rect.localRotation = Quaternion.Euler(_fx.RotationEuler());
                _fx.Rect.localScale = _baseScale;
                _fx.Rect.anchoredPosition = _basePos;
            });
            return seq;
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

        Sequence BuildAfterimageDash(float k)
        {
            _fx.SetDissolve(1f);
            _group.alpha = 1f;
            _fx.Rect.anchoredPosition = _basePos + new Vector2(-560f, 0f);

            var seq = DOTween.Sequence();
            seq.Append(_fx.Rect.DOAnchorPos(_basePos, 0.38f * k).SetEase(Ease.OutCubic));
            // 冲入途中在当前位置留下三道递减残影
            seq.InsertCallback(0.04f * k, SpawnGhost);
            seq.InsertCallback(0.11f * k, SpawnGhost);
            seq.InsertCallback(0.19f * k, SpawnGhost);
            seq.Insert(0.3f * k, _fx.DOFlash(0.15f, 0.25f * k));
            if (_backdrop != null)
                seq.Insert(0.25f * k, _backdrop.Flare(1.6f, 0.7f * k));
            if (useParticleBurst)
                seq.InsertCallback(0.38f * k, () =>
                    VNAmbientParticles.PlaySparkleBurst(BurstWorldPos(), burstColor, 18));
            return seq;
        }

        /// <summary>在角色当前位置生成一个冷色残影副本，快速淡出后销毁</summary>
        void SpawnGhost()
        {
            var img = GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.sprite == null) return;

            var go = new GameObject("Ghost",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_fx.Rect.parent, false);
            rect.SetSiblingIndex(_fx.Rect.GetSiblingIndex()); // 在本体背后
            rect.anchorMin = _fx.Rect.anchorMin;
            rect.anchorMax = _fx.Rect.anchorMax;
            rect.pivot = _fx.Rect.pivot;
            rect.sizeDelta = _fx.Rect.sizeDelta;
            rect.anchoredPosition = _fx.Rect.anchoredPosition;
            rect.localScale = _fx.Rect.localScale;

            var ghost = go.GetComponent<UnityEngine.UI.Image>();
            ghost.sprite = img.sprite;
            ghost.preserveAspect = true;
            ghost.raycastTarget = false;
            ghost.color = new Color(0.75f, 0.85f, 1f, 0.42f); // 冷色调残影

            ghost.DOFade(0f, 0.3f).SetEase(Ease.OutQuad).SetLink(go)
                 .OnComplete(() => Destroy(go));
        }

        // ------------------------------------------------------------------
        // 退场
        // ------------------------------------------------------------------

        /// <summary>
        /// 播放退场演出。side = 往哪边走（只有 RunOut 用），
        /// durationScale 是相对预设基准时长的倍率。
        /// </summary>
        public Sequence PlayExit(VNExitPreset preset, VNSide side, float durationScale)
        {
            float k = durationScale > 0.01f ? durationScale : 1f;
            switch (preset)
            {
                case VNExitPreset.Dissolve: return PlayExitDissolve(BaseDuration(preset) * k);
                case VNExitPreset.RunOut: return BuildRunOut(k, side);
                case VNExitPreset.Sink: return BuildSink(k);
                default: return PlayExitFade(BaseDuration(preset) * k);
            }
        }

        /// <summary>快速跑出画面：横向冲出 + 前倾 + 淡出</summary>
        Sequence BuildRunOut(float k, VNSide side)
        {
            KillCurrent();
            _fx.StopAllLoops();
            _backdrop?.Hide();

            float dir = Resolve(side, true) == VNSide.Right ? 1f : -1f;
            float total = 0.5f * k;
            _current = DOTween.Sequence()
                .Append(_fx.Rect.DOAnchorPos(_basePos + new Vector2(dir * 1250f, 0f), total)
                                .SetEase(Ease.InQuad))
                .Join(_fx.Rect.DOLocalRotate(_fx.RotationEuler(-6f * dir), total * 0.35f)
                                .SetEase(Ease.OutQuad))
                .Join(_group.DOFade(0f, total).SetEase(Ease.InQuad))
                .SetLink(gameObject);
            return _current;
        }

        /// <summary>下沉消失：往下沉 + 失焦模糊 + 变暗 + 淡出（昏迷/意识远去）</summary>
        Sequence BuildSink(float k)
        {
            KillCurrent();
            _fx.StopAllLoops();
            _backdrop?.Hide();

            float total = 0.9f * k;
            _current = DOTween.Sequence()
                .Append(_fx.Rect.DOAnchorPosY(_basePos.y - 95f, total).SetEase(Ease.InQuad))
                .Join(_fx.DOBlur(0.006f, total).SetEase(Ease.InQuad))
                .Join(_fx.SetGrade(VNGradeLayer.Emote, VNGrade.Dim(0.3f, 1f), total))
                .Join(_group.DOFade(0f, total).SetEase(Ease.InCubic))
                .SetLink(gameObject);
            return _current;
        }

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
