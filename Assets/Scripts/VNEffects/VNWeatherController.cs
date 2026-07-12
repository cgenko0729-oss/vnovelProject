using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>天气类型</summary>
    public enum VNWeather { None, Petals, Rain, Snow, Fireflies }

    /// <summary>
    /// 天气系统总控制器：
    ///   - 按需惰性创建各天气的粒子系统（VNAmbientParticles.Create），切换时旧天气
    ///     停止发射（已有粒子自然消散），新天气开始发射 → 自然的交叉过渡
    ///   - 可选的"情绪调色联动"：切天气时对注册的图片控制器补间亮度/饱和度
    ///     （下雨自动偏冷灰、萤火虫之夜自动变暗等）
    /// </summary>
    public class VNWeatherController : MonoBehaviour
    {
        [Tooltip("可选：预制的 VN/Additive 材质资产，传给创建的粒子系统")]
        public Material additiveMaterial;

        [Tooltip("受天气调色联动影响的图片控制器（背景、立绘）")]
        public VNImageEffectController[] moodTargets;

        [Tooltip("切换天气时是否自动对 moodTargets 做亮度/饱和度联动")]
        public bool applyMoodGrading = true;

        readonly Dictionary<VNWeather, VNAmbientParticles> _systems =
            new Dictionary<VNWeather, VNAmbientParticles>();

        VNWeather _current = VNWeather.None;
        public VNWeather Current => _current;

        /// <summary>切换天气（transition 为调色联动的过渡时长）</summary>
        public void SetWeather(VNWeather weather, float transition = 1.5f)
        {
            if (weather == _current) return;

            if (_systems.TryGetValue(_current, out var old) && old != null)
                old.SetPlaying(false);

            if (weather != VNWeather.None)
            {
                var sys = GetOrCreate(weather);
                if (sys != null) sys.SetPlaying(true);
            }

            _current = weather;

            if (applyMoodGrading) ApplyMood(weather, transition);
        }

        /// <summary>循环切换到下一种天气（演示用）</summary>
        public VNWeather CycleNext(float transition = 1.5f)
        {
            var next = (VNWeather)(((int)_current + 1) % 5);
            SetWeather(next, transition);
            return next;
        }

        VNAmbientParticles GetOrCreate(VNWeather weather)
        {
            if (_systems.TryGetValue(weather, out var sys) && sys != null) return sys;

            switch (weather)
            {
                case VNWeather.Petals:
                    sys = VNAmbientParticles.Create(VNAmbientParticles.Preset.Petals,
                        new Color(1f, 0.72f, 0.82f), 11, additiveMaterial, 1f, transform);
                    break;
                case VNWeather.Rain:
                    sys = VNAmbientParticles.Create(VNAmbientParticles.Preset.Rain,
                        new Color(0.7f, 0.8f, 1f), 12, additiveMaterial, 1f, transform);
                    break;
                case VNWeather.Snow:
                    sys = VNAmbientParticles.Create(VNAmbientParticles.Preset.Snow,
                        new Color(1f, 1f, 1f), 11, additiveMaterial, 1f, transform);
                    break;
                case VNWeather.Fireflies:
                    // hdrBoost 2.4：萤火虫要更亮才能被 Bloom 泛光
                    sys = VNAmbientParticles.Create(VNAmbientParticles.Preset.Fireflies,
                        new Color(0.72f, 1f, 0.42f), 12, additiveMaterial, 1f, transform, 2.4f);
                    break;
                default:
                    return null;
            }
            _systems[weather] = sys;
            return sys;
        }

        /// <summary>天气 → 画面情绪调色（亮度 / 饱和度）</summary>
        void ApplyMood(VNWeather weather, float transition)
        {
            if (moodTargets == null) return;

            float brightness, saturation;
            switch (weather)
            {
                case VNWeather.Rain:      brightness = 0.8f;  saturation = 0.8f;  break; // 冷灰
                case VNWeather.Snow:      brightness = 1.03f; saturation = 0.86f; break; // 清冷透亮
                case VNWeather.Fireflies: brightness = 0.72f; saturation = 0.95f; break; // 夜晚
                case VNWeather.Petals:    brightness = 1.04f; saturation = 1.06f; break; // 明媚
                default:                  brightness = 1f;    saturation = 1f;    break;
            }

            foreach (var target in moodTargets)
            {
                if (target == null) continue;
                target.DOBrightness(brightness, transition);
                target.DOSaturation(saturation, transition);
            }
        }
    }
}
