using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 热浪/空气扭曲：复用 VN/ImageEffect 的波浪 UV 扭曲（调大幅度、加快频率），
    /// 配套白色蒸汽雾气粒子（Mist 预设）从画面下方升起。
    /// 夏日柏油路、温泉、篝火场景一键成套。
    /// </summary>
    public class VNHeatHaze : MonoBehaviour
    {
        [Tooltip("被热浪扭曲的图片（通常是背景；立绘可不加，避免脸部扭曲）")]
        public VNImageEffectController[] targets;

        [Header("扭曲参数")]
        [Tooltip("扭曲幅度（UV 偏移量，0.004~0.01 为宜）")]
        public float waveAmount = 0.006f;
        public float waveSpeed = 3.5f;
        public float waveFrequency = 24f;

        [Header("蒸汽雾气")]
        [Tooltip("是否同时开启升腾的雾气粒子")]
        public bool withSteam = true;
        [Tooltip("雾气颜色")]
        public Color steamColor = new Color(1f, 1f, 1f);
        [Tooltip("可选：预制的 VN/Additive 材质资产")]
        public Material additiveMaterial;

        VNAmbientParticles _steam;
        bool _active;

        public bool IsActive => _active;

        /// <summary>开/关热浪</summary>
        public void SetActive(bool on)
        {
            _active = on;

            if (targets != null)
            {
                foreach (var t in targets)
                {
                    if (t == null) continue;
                    t.SetWave(on ? waveAmount : 0f, waveSpeed, waveFrequency);
                }
            }

            if (withSteam)
            {
                if (on && _steam == null)
                {
                    _steam = VNAmbientParticles.Create(
                        VNAmbientParticles.Preset.Mist,
                        steamColor, 11, additiveMaterial, 1f, transform);
                }
                if (_steam != null) _steam.SetPlaying(on);
            }
        }

        public void Toggle() => SetActive(!_active);
    }
}
