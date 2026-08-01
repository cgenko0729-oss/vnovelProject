using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 心跳演出：画面随心跳节奏微缩放（咚-咚——停）+ 粉红边缘泛光同步脉动。
    /// 缩放作用于 SceneRoot（震动只动位置、镜头缩放在子级 ZoomRoot，互不干扰）。
    /// 告白、紧张、暧昧场景一行开启：heartbeat.StartBeat()。
    /// </summary>
    public class VNHeartbeat : MonoBehaviour
    {
        [Header("被脉动的容器（场景生成器自动指向 SceneRoot）")]
        public RectTransform target;

        [Header("联动的边缘泛光（自动切到 HeartBeat 粉色预设）")]
        public VNEdgeGlow edgeGlow;

        [Range(0.002f, 0.05f)]
        [Header("缩放脉动幅度")]
        public float strength = 0.014f;

        Sequence _seq;
        bool _beating;

        public bool IsBeating => _beating;

        /// <summary>开始心跳（节奏与 VNEdgeGlow.HeartBeat 泛光图案一致）</summary>
        public void StartBeat()
        {
            if (_beating || target == null) return;
            _beating = true;

            if (edgeGlow != null) edgeGlow.Show(VNEmotionGlow.HeartBeat);

            float s1 = 1f + strength;          // 第一声"咚"
            float s2 = 1f + strength * 0.35f;  // 回落
            float s3 = 1f + strength * 0.8f;   // 第二声"咚"
            target.localScale = Vector3.one;
            _seq = DOTween.Sequence()
                .Append(target.DOScale(s1, 0.1f).SetEase(Ease.OutQuad))
                .Append(target.DOScale(s2, 0.16f).SetEase(Ease.InOutSine))
                .Append(target.DOScale(s3, 0.1f).SetEase(Ease.OutQuad))
                .Append(target.DOScale(1f, 0.42f).SetEase(Ease.OutQuad))
                .AppendInterval(0.38f)
                .SetLoops(-1)
                .SetLink(gameObject);
        }

        /// <summary>停止心跳（画面回正、泛光淡出）</summary>
        public void StopBeat()
        {
            if (!_beating) return;
            _beating = false;
            _seq?.Kill();
            _seq = null;
            if (target != null)
                target.DOScale(1f, 0.3f).SetEase(Ease.OutQuad).SetLink(gameObject);
            if (edgeGlow != null) edgeGlow.Hide();
        }

        public void Toggle()
        {
            if (_beating) StopBeat();
            else StartBeat();
        }

        void OnDestroy()
        {
            _seq?.Kill();
        }
    }
}
