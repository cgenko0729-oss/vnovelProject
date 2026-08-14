using System.Collections.Generic;
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
        Dream,   // 梦境：偏亮低对比柔紫粉 + 轻暗角（配 CRT 柔和滤镜）
    }

    /// <summary>
    /// 场景色调预设系统（电影级情绪调色）—— 分层调色版。
    ///
    /// 为什么不用全屏后处理做色彩：
    /// 场景是「单相机 + 单个 Screen Space - Camera 的 Canvas」，背景、立绘、
    /// 对话框、HUD 对 URP 来说是同一张画面，Volume 的调色物理上无法只染一部分，
    /// 于是黄昏一开对话框和属性栏也一起变橙。
    ///
    /// 现在拆成两半：
    ///   · 色彩（滤镜/色相/饱和/亮度/对比度）→ 逐层写进各自的
    ///     VNImageEffectController 材质实例，按 <see cref="backgroundStrength"/> /
    ///     <see cref="midStrength"/> / <see cref="characterStrength"/> 分配强度，
    ///     UI 不在目标列表里所以完全不受影响。
    ///   · 暗角与胶片颗粒 → 仍留在全屏 Volume（它们不改变色相，压住四角反而
    ///     有电影感，恐怖/回忆的氛围主要靠这两个）。
    ///
    /// Volume 仍是 A/B 双缓冲交叉过渡，保证任意两种情绪之间都能平滑直切。
    /// </summary>
    public class VNMoodGrading : MonoBehaviour
    {
        [Header("默认过渡时长（秒）")]
        public float defaultTransition = 2f;

        [Header("分层调色目标：背景（含 CG）")]
        public List<VNImageEffectController> backgroundTargets = new List<VNImageEffectController>();

        [Header("分层调色目标：中景（光束等）")]
        public List<VNImageEffectController> midTargets = new List<VNImageEffectController>();

        [Header("分层调色目标：立绘（由 VNStage 在角色进出场时自动维护）")]
        public List<VNImageEffectController> characterTargets = new List<VNImageEffectController>();

        [Range(0f, 1f)]
        [Header("背景染色强度")]
        public float backgroundStrength = 1f;

        [Range(0f, 1f)]
        [Header("中景染色强度")]
        public float midStrength = 0.8f;

        [Range(0f, 1f)]
        [Header("立绘染色强度（0 = 完全不染会明显像贴纸，0.25~0.4 为宜）")]
        public float characterStrength = 0.3f;

        class Layer
        {
            public Volume vol;
            public FilmGrain grain;
            public Vignette vig;
        }

        /// <summary>全屏部分（只有暗角与颗粒，不含任何色彩）</summary>
        struct MoodScreen
        {
            public float grainIntensity;    // 0 = 不启用
            public float vignetteIntensity; // 0 = 不启用
        }

        Layer _a, _b;
        bool _nextIsA = true;
        float _priority = 10f;
        VNMood _current = VNMood.Neutral;
        float _lastDuration = 0f;

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

            // Add<T>(true) = 所有参数 overrideState 全开。
            // 只加暗角与颗粒——ColorAdjustments / WhiteBalance / LiftGammaGain
            // 已经移交给分层调色，留在这里会重新把 UI 一起染色。
            layer.grain = profile.Add<FilmGrain>(true);
            layer.vig = profile.Add<Vignette>(true);
            layer.grain.type.value = FilmGrainLookup.Medium1;
            return layer;
        }

        // ------------------------------------------------------------------
        // 切换情绪
        // ------------------------------------------------------------------

        /// <summary>切换情绪色调（duration &lt; 0 时用 defaultTransition）</summary>
        public void SetMood(VNMood mood, float duration = -1f)
        {
            if (mood == _current || _a == null) return;
            if (duration < 0f) duration = defaultTransition;
            _current = mood;
            _lastDuration = duration;

            DOTween.Kill(this);

            ApplyGradeToAll(duration);

            var screen = GetScreen(mood);
            if (mood == VNMood.Neutral)
            {
                FadeWeight(_a.vol, 0f, duration);
                FadeWeight(_b.vol, 0f, duration);
                return;
            }

            var target = _nextIsA ? _a : _b;
            var other = _nextIsA ? _b : _a;
            _nextIsA = !_nextIsA;

            ApplyScreen(screen, target);
            target.vol.priority = ++_priority; // 新层永远叠在旧层之上

            FadeWeight(target.vol, 1f, duration);
            FadeWeight(other.vol, 0f, duration);
        }

        /// <summary>循环切换到下一种情绪（演示用）</summary>
        public VNMood CycleNext(float duration = -1f)
        {
            int count = System.Enum.GetValues(typeof(VNMood)).Length;
            var next = (VNMood)(((int)_current + 1) % count);
            SetMood(next, duration);
            return next;
        }

        void FadeWeight(Volume vol, float to, float duration)
        {
            DOTween.To(() => vol.weight, w => vol.weight = w, to, duration)
                   .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 分层调色目标的登记（立绘由 VNStage 在角色进出场时维护）
        // ------------------------------------------------------------------

        /// <summary>登记一张立绘并立刻套上当前情绪（中途出场的角色才不会漏色）</summary>
        public void RegisterCharacter(VNImageEffectController fx)
        {
            if (fx == null || characterTargets.Contains(fx)) return;
            characterTargets.Add(fx);
            fx.SetGrade(VNGradeLayer.Mood,
                GetGrade(_current).Scaled(characterStrength), 0f);
        }

        /// <summary>注销立绘（退场时调用；顺手清掉它的 mood 通道）</summary>
        public void UnregisterCharacter(VNImageEffectController fx)
        {
            if (fx == null) return;
            characterTargets.Remove(fx);
            fx.ClearGrade(VNGradeLayer.Mood);
        }

        /// <summary>整批替换立绘目标（VNStage.RefreshRegistries 用）</summary>
        public void SetCharacterTargets(IEnumerable<VNImageEffectController> targets)
        {
            foreach (var old in characterTargets)
                if (old != null) old.ClearGrade(VNGradeLayer.Mood);
            characterTargets.Clear();
            if (targets == null) return;
            foreach (var fx in targets) RegisterCharacter(fx);
        }

        /// <summary>登记背景（换背景图不换控制器时不必重复调用）</summary>
        public void RegisterBackground(VNImageEffectController fx)
        {
            if (fx == null || backgroundTargets.Contains(fx)) return;
            backgroundTargets.Add(fx);
            fx.SetGrade(VNGradeLayer.Mood,
                GetGrade(_current).Scaled(backgroundStrength), 0f);
        }

        /// <summary>重新把当前情绪套到所有已登记的层（改强度参数后调用）</summary>
        public void ApplyGradeToAll(float duration = -1f)
        {
            if (duration < 0f) duration = _lastDuration;
            var grade = GetGrade(_current);
            ApplyToList(backgroundTargets, grade.Scaled(backgroundStrength), duration);
            ApplyToList(midTargets, grade.Scaled(midStrength), duration);
            ApplyToList(characterTargets, grade.Scaled(characterStrength), duration);
        }

        static void ApplyToList(List<VNImageEffectController> list, VNGrade grade, float duration)
        {
            if (list == null) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i] == null) { list.RemoveAt(i); continue; }
                list[i].SetGrade(VNGradeLayer.Mood, grade, duration);
            }
        }

        void OnValidate()
        {
            if (Application.isPlaying && _a != null) ApplyGradeToAll(0.2f);
        }

        // ------------------------------------------------------------------
        // 预设表
        // ------------------------------------------------------------------

        void ApplyScreen(MoodScreen s, Layer l)
        {
            // 颗粒/暗角只在需要的情绪里启用，避免 0 值覆盖基础 Volume 的暗角
            l.grain.active = s.grainIntensity > 0f;
            l.grain.intensity.value = s.grainIntensity;

            l.vig.active = s.vignetteIntensity > 0f;
            l.vig.intensity.value = s.vignetteIntensity;
            l.vig.smoothness.value = 0.5f;
            l.vig.center.value = new Vector2(0.5f, 0.5f);
        }

        static MoodScreen GetScreen(VNMood mood)
        {
            switch (mood)
            {
                case VNMood.Memory:  return new MoodScreen { grainIntensity = 0.28f, vignetteIntensity = 0.34f };
                case VNMood.Tension: return new MoodScreen { grainIntensity = 0.12f, vignetteIntensity = 0.28f };
                case VNMood.Horror:  return new MoodScreen { grainIntensity = 0.4f,  vignetteIntensity = 0.48f };
                case VNMood.Dream:   return new MoodScreen { vignetteIntensity = 0.22f };
                default:             return new MoodScreen();
            }
        }

        /// <summary>
        /// 每种情绪的色彩（100% 强度下的值，各层再按系数缩放）。
        ///
        /// 这套数值是从原来的 Volume 参数换算并**整体收敛**过的：老版
        /// Sunset 把 colorFilter(1.18,0.94,0.72)、temperature +30、
        /// gain(1.08,1,0.9) 三条暖化通道叠乘，红通道乘到 1.27 直接冲破
        /// Bloom 阈值 1.0 泛白、蓝通道压到 0.65，配上本来就偏橙的黄昏背景
        /// 就糊成一片橙。现在合并成单条滤镜并留足 Bloom 余量。
        /// </summary>
        public static VNGrade GetGrade(VNMood mood)
        {
            switch (mood)
            {
                case VNMood.Morning: // 清晨：冷青偏亮，空气感
                    return new VNGrade(new Color(0.95f, 1.00f, 1.07f), 0f, 1.05f, 1.08f, 1.02f);

                case VNMood.Sunset:  // 黄昏：橙金（收敛版，蓝通道不再压死）
                    return new VNGrade(new Color(1.09f, 1.00f, 0.88f), 0f, 1.06f, 1.00f, 1.05f);

                case VNMood.Night:   // 夜晚：深蓝低饱和，压暗
                    return new VNGrade(new Color(0.80f, 0.87f, 1.12f), 0f, 0.72f, 0.62f, 1.06f);

                case VNMood.Memory:  // 回忆：褪色暖黄（颗粒与暗角在全屏层）
                    return new VNGrade(new Color(1.11f, 1.02f, 0.87f), 0f, 0.62f, 1.06f, 0.85f);

                case VNMood.Tension: // 紧张：高对比偏绿
                    return new VNGrade(new Color(0.92f, 1.03f, 0.93f), 0f, 0.88f, 0.92f, 1.28f);

                case VNMood.Horror:  // 恐怖：重度去饱和压暗
                    return new VNGrade(new Color(0.88f, 0.91f, 0.97f), 0f, 0.40f, 0.70f, 1.20f);

                case VNMood.Dream:   // 梦境：偏亮低对比柔紫粉
                    return new VNGrade(new Color(1.05f, 0.95f, 1.11f), 0f, 0.86f, 1.16f, 0.82f);

                default:
                    return VNGrade.Identity;
            }
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
