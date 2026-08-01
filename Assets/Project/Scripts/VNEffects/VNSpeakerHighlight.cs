using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 说话者高亮系统：多人对话时突出正在说话的角色。
    ///   说话者：正常亮度 + 放大 3% + 移到最前 + 背后光环闪耀
    ///   旁听者：压暗到 0.6 + 降饱和 + 微缩 0.97
    /// 全部复用 VNImageEffectController 的现有 API，缩放走 DOScaleMultiplier
    /// 与呼吸动作无冲突。
    /// </summary>
    public class VNSpeakerHighlight : MonoBehaviour
    {
        [Header("参与对话的立绘控制器（场景生成器自动填，或运行时 Register()）")]
        public List<VNImageEffectController> characters = new List<VNImageEffectController>();

        [Header("说话者")]
        public float speakerScale = 1.03f;
        [Header("说话者亮度（1 = 原亮度）")]
        public float speakerBrightness = 1f;

        [Header("旁听者")]
        public float dimBrightness = 0.6f;
        [Header("旁听者饱和度保留比例")]
        public float dimSaturation = 0.85f;
        [Header("旁听者缩小倍率")]
        public float dimScale = 0.97f;

        [Header("过渡时长（秒）")]
        public float transition = 0.35f;

        VNImageEffectController _speaker;

        public VNImageEffectController Current => _speaker;

        public void Register(VNImageEffectController c)
        {
            if (c != null && !characters.Contains(c)) characters.Add(c);
        }

        /// <summary>指定说话者；其余角色自动压暗。传 null 等同 ClearSpeaker()。</summary>
        public void SetSpeaker(VNImageEffectController speaker)
        {
            if (speaker == null)
            {
                ClearSpeaker();
                return;
            }
            _speaker = speaker;

            // 说话者移到同级立绘的最前面（只在立绘之间换序，不越过 UI）
            int maxIdx = -1;
            foreach (var c in characters)
                if (c != null) maxIdx = Mathf.Max(maxIdx, c.transform.GetSiblingIndex());
            if (maxIdx >= 0) speaker.transform.SetSiblingIndex(maxIdx);

            foreach (var c in characters)
            {
                if (c == null) continue;
                bool isSpeaker = c == speaker;
                c.DOBrightness(isSpeaker ? speakerBrightness : dimBrightness, transition);
                c.DOSaturation(isSpeaker ? 1f : dimSaturation, transition);
                c.DOScaleMultiplier(isSpeaker ? speakerScale : dimScale, transition);

                var glow = c.GetComponent<VNGlowBackdrop>();
                if (glow != null)
                {
                    if (isSpeaker) glow.Flare(1.5f, 0.7f);
                    else glow.Hide();
                }
            }
        }

        /// <summary>清除高亮：所有角色恢复正常</summary>
        public void ClearSpeaker()
        {
            _speaker = null;
            foreach (var c in characters)
            {
                if (c == null) continue;
                c.DOBrightness(1f, transition);
                c.DOSaturation(1f, transition);
                c.DOScaleMultiplier(1f, transition);
                var glow = c.GetComponent<VNGlowBackdrop>();
                if (glow != null) glow.StartPulse();
            }
        }
    }
}
