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
    ///   Tremble()   害怕：高频颤抖 + 缩小 + 冷色压暗
    /// 动作期间自动暂停悬浮飘动，结束后自动恢复；动作互相打断安全。
    ///
    /// ★ 加新动作只需两步：写一个 public Sequence Xxx() 方法（照 Begin()/End() 骨架）
    ///   + 打上 [VNEmote("中文名", "别名"...)]。剧本 switch / 编辑器下拉与中文名 / Lint 白名单 /
    ///   互动模块别名全部由 VNEmoteCatalog 反射自动跟上，**不用再去任何地方登记**。
    ///   方法名就是剧本里写的英文正名；别名给互动资产等处写中文用（正名本身大小写不敏感地永远可用）。
    /// </summary>
    [RequireComponent(typeof(VNImageEffectController))]
    public class VNCharacterEmotes : MonoBehaviour
    {
        VNImageEffectController _fx;
        RectTransform _rect;
        Sequence _seq;

        Vector2 _basePos;
        bool _cached;
        bool _wasFloating;
        bool _wasBreathing;
        bool _dejected;

        public bool IsDejected => _dejected;

        /// <summary>重设基准位置（剧本系统改变角色站位后调用）</summary>
        public void SetBasePosition(Vector2 pos)
        {
            _basePos = pos;
            _cached = true;
        }

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
            _rect = _fx.Rect;
        }

        /// <summary>动作开始前的统一准备：暂停悬浮/呼吸、打断上个动作、回到基准姿态</summary>
        void Begin()
        {
            bool interrupted = _wasFloating && _seq != null && _seq.IsActive();
            _wasFloating = _fx.IsFloating || interrupted;
            _wasBreathing = _fx.IsBreathingMotion ||
                            (_wasBreathing && _seq != null && _seq.IsActive());
            _fx.StopFloating();        // 会把位置重置回悬浮基准
            _fx.StopBreathingMotion(); // 会把缩放/旋转重置回基准

            _seq?.Kill();
            _seq = null;

            if (!_cached)
            {
                _basePos = _rect.anchoredPosition;
                _cached = true;
            }

            // 基准缩放从控制器取（包含说话者高亮的倍率），不自己缓存
            _rect.localScale = _fx.CurrentBaseScale;
            _rect.localRotation = Quaternion.Euler(_fx.RotationEuler());
            if (!_dejected) _rect.anchoredPosition = _basePos;
        }

        /// <summary>动作收尾：恢复悬浮与呼吸（沮丧状态除外）</summary>
        Sequence End(Sequence seq)
        {
            seq.OnComplete(() =>
            {
                if (_wasFloating && !_dejected) _fx.ResumeFloating();
                if (_wasBreathing && !_dejected) _fx.ResumeBreathingMotion();
            });
            seq.SetLink(gameObject);
            _seq = seq;
            return seq;
        }

        // ------------------------------------------------------------------

        /// <summary>惊讶：快速上跳 + 微放大，落回时轻微回弹</summary>
        [VNEmote("惊讶")]
        public Sequence Surprise()
        {
            Begin();
            var bs = _fx.CurrentBaseScale;
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y + 34f, 0.12f).SetEase(Ease.OutQuad))
                .Join(_rect.DOScale(bs * 1.05f, 0.12f).SetEase(Ease.OutQuad))
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.32f).SetEase(Ease.OutBounce))
                .Join(_rect.DOScale(bs, 0.26f).SetEase(Ease.OutQuad));
            return End(seq);
        }

        /// <summary>生气：横向快速抖动 + 红色发光脉冲</summary>
        [VNEmote("生气")]
        public Sequence Angry()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOShakeAnchorPos(0.55f, new Vector2(16f, 0f), 22, 90f, false, true))
                .Insert(0f, _fx.PulseEmission(new Color(1.6f, 0.25f, 0.15f), 0.55f, 0.65f));
            return End(seq);
        }

        /// <summary>害羞：轻微缩小 + 下沉一点 + 粉色光晕，然后慢慢恢复</summary>
        [VNEmote("害羞")]
        public Sequence Shy()
        {
            Begin();
            var bs = _fx.CurrentBaseScale;
            var seq = DOTween.Sequence()
                .Append(_rect.DOScale(bs * 0.97f, 0.28f).SetEase(Ease.OutQuad))
                .Join(_rect.DOAnchorPosY(_basePos.y - 7f, 0.28f).SetEase(Ease.OutQuad))
                .Join(_fx.PulseEmission(new Color(1.5f, 0.65f, 0.85f), 0.45f, 1.3f))
                .AppendInterval(0.35f)
                .Append(_rect.DOScale(bs, 0.45f).SetEase(Ease.InOutSine))
                .Join(_rect.DOAnchorPosY(_basePos.y, 0.45f).SetEase(Ease.InOutSine));
            return End(seq);
        }

        /// <summary>沮丧：下沉 + 变暗 + 降饱和，保持该状态直到 Recover()</summary>
        [VNEmote("沮丧")]
        public Sequence Dejected()
        {
            Begin();
            _dejected = true;
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y - 24f, 0.6f).SetEase(Ease.OutQuad))
                .Join(_fx.SetGrade(VNGradeLayer.Emote, VNGrade.Dim(0.72f, 0.68f), 0.6f));
            return End(seq);
        }

        /// <summary>从沮丧状态恢复：回到原位、亮度饱和度复原、恢复悬浮</summary>
        [VNEmote("恢复")]
        public Sequence Recover()
        {
            if (!_dejected) return _seq;
            Begin();
            _dejected = false;
            var seq = DOTween.Sequence()
                .Append(_rect.DOAnchorPosY(_basePos.y, 0.5f).SetEase(Ease.OutQuad))
                .Join(_fx.ClearGrade(VNGradeLayer.Emote, 0.5f));
            return End(seq);
        }

        /// <summary>点头：两次快速下沉回弹（第二次幅度更小）</summary>
        [VNEmote("点头")]
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
        [VNEmote("摇头", "shake")]   // shake：互动资产里的老别名，保持兼容
        public Sequence HeadShake()
        {
            Begin();
            var seq = DOTween.Sequence()
                .Append(_rect.DOLocalRotate(_fx.RotationEuler(2.6f), 0.1f).SetEase(Ease.OutQuad))
                .Append(_rect.DOLocalRotate(_fx.RotationEuler(-2.6f), 0.16f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(_fx.RotationEuler(2f), 0.15f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(_fx.RotationEuler(-1.4f), 0.14f).SetEase(Ease.InOutSine))
                .Append(_rect.DOLocalRotate(_fx.RotationEuler(), 0.12f).SetEase(Ease.OutQuad));
            return End(seq);
        }

        /// <summary>害怕：高频小幅颤抖 + 微微缩小 + 冷色压暗，抖完慢慢回来</summary>
        [VNEmote("颤抖", "害怕")]
        public Sequence Tremble()
        {
            Begin();
            var bs = _fx.CurrentBaseScale;
            var seq = DOTween.Sequence()
                // 抖：幅度小、频率高（vibrato 大）才像发抖而不像生气
                .Append(_rect.DOShakeAnchorPos(0.9f, new Vector2(5f, 3f), 34, 90f, false, true))
                // 同时缩起来一点（人害怕会缩）
                .Join(_rect.DOScale(bs * 0.96f, 0.25f).SetEase(Ease.OutQuad))
                // 冷色 + 压暗：淡蓝滤镜、亮度 0.85、饱和 0.8
                .Join(_fx.SetGrade(VNGradeLayer.Emote,
                    new VNGrade(new Color(0.86f, 0.92f, 1f), 0f, 0.8f, 0.85f, 1f), 0.3f))
                .AppendInterval(0.2f)
                .Append(_rect.DOScale(bs, 0.4f).SetEase(Ease.InOutSine))
                .Join(_fx.ClearGrade(VNGradeLayer.Emote, 0.4f));
            return End(seq);
        }

        void OnDestroy()
        {
            _seq?.Kill();
        }
    }
}
