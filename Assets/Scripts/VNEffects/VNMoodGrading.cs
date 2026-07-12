using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VNEffects
{
    /// <summary>场景情绪色调预设</summary>
    public enum VNMood
    {
        Neutral, // 无（回到原始画面）
        Morning, // 清晨：冷青偏亮
        Sunset,  // 黄昏：橙金
        Night,   // 夜晚：深蓝低饱和
        Memory,  // 回忆：褪色暖黄 + 胶片颗粒 + 暗角
        Tension, // 紧张：高对比偏绿
        Horror,  // 恐怖：去饱和 + 颗粒 + 暗角加深
    }

    /// <summary>
    /// 场景色调预设系统（电影级情绪调色）。
    /// 原理：运行时创建两个全局 Volume（A/B 双缓冲），每个挂
    /// ColorAdjustments + WhiteBalance + LiftGammaGain + FilmGrain + Vignette。
    /// 切换情绪时把目标预设写入闲置的那个 Volume，然后交叉补间两个 Volume 的
    /// weight —— 画面像电影调色一样平滑过渡，且任意两种情绪之间都能直接切换。
    /// 新 Volume 的 priority 递增，保证永远叠在旧的之上，交叉期间不打架。
    /// 让同一张背景图演出完全不同的情绪。
    /// </summary>
    public class VNMoodGrading : MonoBehaviour
    {
        [Tooltip("默认过渡时长（秒）")]
        public float defaultTransition = 2f;

        class Layer
        {
            public Volume vol;
            public ColorAdjustments ca;
            public WhiteBalance wb;
            public LiftGammaGain lgg;
            public FilmGrain grain;
            public Vignette vig;
        }

        struct MoodSettings
        {
            public float exposure, contrast, saturation;
            public Color filter;
            public float temperature, tint;
            public Vector4 lift, gamma, gain;
            public float grainIntensity;   // 0 = 不启用
            public float vignetteIntensity; // 0 = 不启用
        }

        Layer _a, _b;
        bool _nextIsA = true;
        float _priority = 10f;
        VNMood _current = VNMood.Neutral;

        public VNMood Current => _current;

        void Awake()
        {
            _a = CreateLayer("MoodVolume_A");
            _b = CreateLayer("MoodVolume_B");
        }

        Layer CreateLayer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var layer = new Layer();
            layer.vol = go.AddComponent<Volume>();
            layer.vol.isGlobal = true;
            layer.vol.weight = 0f;
            layer.vol.priority = _priority;

            // 运行时创建 profile 实例（不落盘，不弄脏资产）
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            layer.vol.profile = profile;

            // Add<T>(true) = 所有参数 overrideState 全开
            layer.ca = profile.Add<ColorAdjustments>(true);
            layer.wb = profile.Add<WhiteBalance>(true);
            layer.lgg = profile.Add<LiftGammaGain>(true);
            layer.grain = profile.Add<FilmGrain>(true);
            layer.vig = profile.Add<Vignette>(true);
            layer.grain.type.value = FilmGrainLookup.Medium1;
            return layer;
        }

        /// <summary>切换情绪色调（duration &lt; 0 时用 defaultTransition）</summary>
        public void SetMood(VNMood mood, float duration = -1f)
        {
            if (mood == _current || _a == null) return;
            if (duration < 0f) duration = defaultTransition;
            _current = mood;

            DOTween.Kill(this);

            if (mood == VNMood.Neutral)
            {
                FadeWeight(_a.vol, 0f, duration);
                FadeWeight(_b.vol, 0f, duration);
                return;
            }

            var target = _nextIsA ? _a : _b;
            var other = _nextIsA ? _b : _a;
            _nextIsA = !_nextIsA;

            Apply(GetSettings(mood), target);
            target.vol.priority = ++_priority; // 新层永远叠在旧层之上

            FadeWeight(target.vol, 1f, duration);
            FadeWeight(other.vol, 0f, duration);
        }

        /// <summary>循环切换到下一种情绪（演示用）</summary>
        public VNMood CycleNext(float duration = -1f)
        {
            var next = (VNMood)(((int)_current + 1) % 7);
            SetMood(next, duration);
            return next;
        }

        void FadeWeight(Volume vol, float to, float duration)
        {
            DOTween.To(() => vol.weight, w => vol.weight = w, to, duration)
                   .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        void Apply(MoodSettings s, Layer l)
        {
            l.ca.postExposure.value = s.exposure;
            l.ca.contrast.value = s.contrast;
            l.ca.saturation.value = s.saturation;
            l.ca.colorFilter.value = s.filter;
            l.ca.hueShift.value = 0f;

            l.wb.temperature.value = s.temperature;
            l.wb.tint.value = s.tint;

            l.lgg.lift.value = s.lift;
            l.lgg.gamma.value = s.gamma;
            l.lgg.gain.value = s.gain;

            // 颗粒/暗角只在需要的情绪里启用，避免 0 值覆盖基础 Volume 的暗角
            l.grain.active = s.grainIntensity > 0f;
            l.grain.intensity.value = s.grainIntensity;

            l.vig.active = s.vignetteIntensity > 0f;
            l.vig.intensity.value = s.vignetteIntensity;
            l.vig.smoothness.value = 0.5f;
            l.vig.center.value = new Vector2(0.5f, 0.5f);
        }

        static readonly Vector4 One = new Vector4(1f, 1f, 1f, 0f);

        static MoodSettings GetSettings(VNMood mood)
        {
            var s = new MoodSettings
            {
                filter = Color.white,
                lift = One, gamma = One, gain = One,
            };
            switch (mood)
            {
                case VNMood.Morning: // 清晨：冷青偏亮，空气感
                    s.exposure = 0.25f; s.contrast = 5f; s.saturation = 6f;
                    s.filter = new Color(0.93f, 1.0f, 1.06f);
                    s.temperature = -18f;
                    s.gamma = new Vector4(1f, 1.01f, 1.04f, 0f);
                    break;
                case VNMood.Sunset: // 黄昏：橙金，高光偏暖
                    s.exposure = 0.05f; s.contrast = 12f; s.saturation = 8f;
                    s.filter = new Color(1.18f, 0.94f, 0.72f);
                    s.temperature = 30f; s.tint = 8f;
                    s.lift = new Vector4(1f, 0.98f, 0.94f, 0f);
                    s.gain = new Vector4(1.08f, 1.0f, 0.9f, 0f);
                    break;
                case VNMood.Night: // 夜晚：深蓝低饱和，压暗
                    s.exposure = -0.75f; s.contrast = 8f; s.saturation = -28f;
                    s.filter = new Color(0.72f, 0.82f, 1.18f);
                    s.temperature = -12f;
                    s.lift = new Vector4(0.95f, 0.97f, 1.06f, 0f);
                    break;
                case VNMood.Memory: // 回忆：褪色暖黄 + 颗粒 + 暗角
                    s.exposure = 0.1f; s.contrast = -18f; s.saturation = -38f;
                    s.filter = new Color(1.1f, 1.02f, 0.85f);
                    s.temperature = 18f;
                    s.gamma = new Vector4(1.04f, 1.02f, 0.98f, 0f);
                    s.grainIntensity = 0.28f;
                    s.vignetteIntensity = 0.34f;
                    break;
                case VNMood.Tension: // 紧张：高对比偏绿
                    s.exposure = -0.15f; s.contrast = 32f; s.saturation = -12f;
                    s.filter = new Color(0.9f, 1.04f, 0.92f);
                    s.temperature = -5f;
                    s.grainIntensity = 0.12f;
                    s.vignetteIntensity = 0.28f;
                    break;
                case VNMood.Horror: // 恐怖：重度去饱和 + 颗粒 + 深暗角
                    s.exposure = -0.55f; s.contrast = 22f; s.saturation = -60f;
                    s.filter = new Color(0.86f, 0.9f, 0.97f);
                    s.temperature = -10f;
                    s.grainIntensity = 0.4f;
                    s.vignetteIntensity = 0.48f;
                    break;
            }
            return s;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
