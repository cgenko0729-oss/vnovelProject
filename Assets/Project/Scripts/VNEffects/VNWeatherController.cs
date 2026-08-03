using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>天气类型（雨/雪/萤火虫仍走 VNAmbientParticles；花瓣/落叶已改走 VNFoliageSystem）</summary>
    public enum VNWeather { None, Petals, Rain, Snow, Fireflies }

    /// <summary>
    /// 天气系统总控制器。
    ///
    /// 【两套后端】
    ///   飘落类（樱花 / 枫叶 / 银杏 / 阔叶 / 竹叶）→ VNFoliageSystem
    ///       参数来自 VNWeatherDef 资产，三层景深 + 翻转 + 独立摆动 + 阵风。
    ///   其余（雨 / 雪 / 萤火虫）→ VNAmbientParticles 的老预设。
    ///
    /// 【统一入口】SetWeatherId(id)
    ///   id 认三种写法，按序尝试：
    ///     1. VNGameConfig 的飘落天气库里登记的自定义资产 id（可中文）
    ///     2. 内置叶型别名（petals/sakura/落樱 · maple/枫叶 · ginkgo/银杏 · leaves/落叶 · bamboo/竹叶）
    ///     3. VNWeather 枚举名（Rain / Snow / Fireflies / None）
    ///   旧存档里存的 "Petals"/"Rain" 全部照常认得，不需要迁移。
    /// </summary>
    public class VNWeatherController : MonoBehaviour
    {
        [Header("可选：预制的 VN/Additive 材质资产（雨雪萤火虫用）")]
        public Material additiveMaterial;

        [Header("可选：预制的 VN/ParticleAlpha 材质资产（花瓣/落叶用）")]
        public Material particleAlphaMaterial;

        [Header("受天气调色联动影响的图片控制器（背景、立绘）")]
        public VNImageEffectController[] moodTargets;

        [Header("切换天气时是否自动对 moodTargets 做亮度/饱和度联动")]
        public bool applyMoodGrading = true;

        readonly Dictionary<VNWeather, VNAmbientParticles> _systems =
            new Dictionary<VNWeather, VNAmbientParticles>();
        readonly Dictionary<string, VNFoliageSystem> _foliage =
            new Dictionary<string, VNFoliageSystem>();
        readonly List<VNWeatherDef> _builtinDefs = new List<VNWeatherDef>();

        VNWeather _current = VNWeather.None;
        string _currentId = "";                 // 生效中的天气 id（飘落类为 def id，其余为枚举名）
        Color _ambient = Color.white;

        /// <summary>旧 API：只反映雨/雪/萤火虫 + Petals，新叶型统一算 Petals</summary>
        public VNWeather Current => _current;

        /// <summary>存档 / 调试重建用的天气 id（空 = 无天气）</summary>
        public string CurrentId => _currentId;

        /// <summary>当前生效的飘落系统（没有则为 null）</summary>
        public VNFoliageSystem CurrentFoliage =>
            !string.IsNullOrEmpty(_currentId) && _foliage.TryGetValue(_currentId, out var f) ? f : null;

        void OnDestroy()
        {
            foreach (var d in _builtinDefs)
                if (d != null) Destroy(d);
            _builtinDefs.Clear();
        }

        // ------------------------------------------------------------------
        // 统一入口
        // ------------------------------------------------------------------

        /// <summary>
        /// 按 id 切天气。overrides 传 &lt;=0（wind 传 NaN）表示不覆盖，用资产里的值。
        /// </summary>
        public void SetWeatherId(string id, float transition = 1.5f,
            float density = 0f, float wind = float.NaN, float speed = 0f, float size = 0f)
        {
            id = (id ?? "").Trim();
            bool none = id.Length == 0 ||
                        id.Equals("none", System.StringComparison.OrdinalIgnoreCase) ||
                        id.Equals("off", System.StringComparison.OrdinalIgnoreCase);

            // 同一天气重复调用：只更新覆盖参数，不重启粒子（避免剧本里连写两句就断流）
            if (!none && string.Equals(id, _currentId, System.StringComparison.OrdinalIgnoreCase))
            {
                CurrentFoliage?.ApplyOverrides(density, wind, speed, size);
                return;
            }

            StopAll();

            if (none)
            {
                _current = VNWeather.None;
                _currentId = "";
                if (applyMoodGrading) ApplyMood(VNWeather.None, null, transition);
                return;
            }

            var def = ResolveFoliageDef(id);
            if (def != null)
            {
                var sys = GetOrCreateFoliage(def);
                if (sys != null)
                {
                    sys.ApplyOverrides(density, wind, speed, size);
                    sys.SetAmbient(_ambient);
                    sys.SetPlaying(true);
                }
                _current = VNWeather.Petals;
                _currentId = def.id;
                if (applyMoodGrading) ApplyMood(VNWeather.Petals, def, transition);
                return;
            }

            // 退回旧的枚举天气（雨 / 雪 / 萤火虫）
            var w = VNScriptParser.ParseEnum(id, VNWeather.None, 0);
            if (w != VNWeather.None)
            {
                var sys = GetOrCreate(w);
                if (sys != null) sys.SetPlaying(true);
            }
            _current = w;
            _currentId = w == VNWeather.None ? "" : w.ToString();
            if (applyMoodGrading) ApplyMood(w, null, transition);
        }

        /// <summary>旧 API：按枚举切天气（Petals 转发到内置 sakura 预设）</summary>
        public void SetWeather(VNWeather weather, float transition = 1.5f)
            => SetWeatherId(weather == VNWeather.None ? "" : weather.ToString(), transition);

        /// <summary>循环切换（演示用）：樱花 → 枫叶 → 银杏 → 雨 → 雪 → 萤火虫 → 无</summary>
        static readonly string[] CycleOrder =
            { "petals", "maple", "ginkgo", "leaves", "bamboo", "Rain", "Snow", "Fireflies", "" };

        public string CycleNext(float transition = 1.5f)
        {
            int idx = System.Array.FindIndex(CycleOrder,
                s => string.Equals(s, _currentId, System.StringComparison.OrdinalIgnoreCase));
            string next = CycleOrder[(idx + 1) % CycleOrder.Length];
            SetWeatherId(next, transition);
            return next;
        }

        /// <summary>一次性阵风冲击（剧本 / 樱吹雪用）</summary>
        public void Gust(float strength = 2.5f) => CurrentFoliage?.Gust(strength);

        /// <summary>
        /// 编辑器预览专用：直接套用一份 def 并立刻播放（Weather Preview 窗口调）。
        /// 与 SetWeatherId 的区别是即使 id 没变也会重灌参数 —— 调滑杆要能立刻看到。
        /// </summary>
        public void PreviewDef(VNWeatherDef def)
        {
            if (def == null) return;
            StopAll();
            var sys = GetOrCreateFoliage(def);
            if (sys == null) return;
            sys.SetDef(def);
            sys.SetAmbient(_ambient);
            sys.SetPlaying(true);
            _current = VNWeather.Petals;
            _currentId = def.id;
        }

        /// <summary>环境色联动：黄昏 / 夜晚场景下花瓣跟着变色</summary>
        public void SetAmbient(Color ambient)
        {
            _ambient = ambient;
            foreach (var f in _foliage.Values)
                if (f != null) f.SetAmbient(ambient);
        }

        void StopAll()
        {
            if (_systems.TryGetValue(_current, out var old) && old != null)
                old.SetPlaying(false);
            foreach (var f in _foliage.Values)
                if (f != null) f.SetPlaying(false);
        }

        // ------------------------------------------------------------------
        // 资产解析与惰性创建
        // ------------------------------------------------------------------

        /// <summary>id → 飘落天气资产。先查 VNGameConfig 登记的，再退回内置叶型；都不是则 null。</summary>
        public VNWeatherDef ResolveFoliageDef(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            var cfg = VNGameConfig.Active;
            if (cfg != null && cfg.weatherDefs != null)
            {
                foreach (var d in cfg.weatherDefs)
                    if (d != null && string.Equals(d.id, id, System.StringComparison.OrdinalIgnoreCase))
                        return d;
            }

            var shape = VNWeatherDef.ParseBuiltinId(id);
            if (shape == null) return null;

            string key = VNWeatherDef.DefaultId(shape.Value);
            foreach (var d in _builtinDefs)
                if (d != null && d.id == key) return d;

            var builtin = VNWeatherDef.CreateBuiltin(shape.Value);
            _builtinDefs.Add(builtin);
            return builtin;
        }

        VNFoliageSystem GetOrCreateFoliage(VNWeatherDef def)
        {
            if (def == null) return null;
            string key = def.id;
            if (_foliage.TryGetValue(key, out var sys) && sys != null) return sys;

            sys = VNFoliageSystem.Create(def, particleAlphaMaterial, transform);
            _foliage[key] = sys;
            return sys;
        }

        VNAmbientParticles GetOrCreate(VNWeather weather)
        {
            if (_systems.TryGetValue(weather, out var sys) && sys != null) return sys;

            switch (weather)
            {
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

        // ------------------------------------------------------------------
        // 天气 → 画面情绪调色
        // ------------------------------------------------------------------

        void ApplyMood(VNWeather weather, VNWeatherDef def, float transition)
        {
            if (moodTargets == null) return;

            float brightness, saturation;
            switch (weather)
            {
                case VNWeather.Rain: brightness = 0.8f; saturation = 0.8f; break;   // 冷灰
                case VNWeather.Snow: brightness = 1.03f; saturation = 0.86f; break; // 清冷透亮
                case VNWeather.Fireflies: brightness = 0.72f; saturation = 0.95f; break; // 夜晚
                case VNWeather.Petals:
                    // 秋叶偏暖偏浓，落樱明媚 —— 按叶型分档
                    if (def != null && def.shape != VNLeafShape.Sakura)
                    { brightness = 0.98f; saturation = 1.12f; }
                    else
                    { brightness = 1.04f; saturation = 1.06f; }
                    break;
                default: brightness = 1f; saturation = 1f; break;
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
