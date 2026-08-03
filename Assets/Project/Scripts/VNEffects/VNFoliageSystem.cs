using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 落樱 / 落叶系统：一套参数驱动三层景深粒子。
    ///
    /// 【比旧的 VNAmbientParticles.Petals 多了什么】
    /// 1. Alpha 混合（VN/ParticleAlpha）—— 花瓣是实体，会遮挡背景。
    ///    旧版用加法混合，粉色叠明亮背景后三通道全溢出，被 Bloom 压成白色，
    ///    所以「樱花是白的」根本不是颜色配错，是混合模式选错了。
    /// 2. 图集翻转（VNFoliageTextures）—— 每片形态不同、绕自身纵轴翻转，摆脱纸片感。
    /// 3. 每粒子独立相位的横摆 —— 旧版全体共享一个 Perlin 噪声场，
    ///    相邻花瓣会同步摆动，看起来像有人在整体拨一张网，这是最出戏的地方。
    /// 4. 全局阵风 —— 起风时整屏一起加速斜掠，风停恢复缓飘。画面「活」起来的关键。
    /// 5. 三层景深 —— 远层小/慢/淡/偏环境色，中层主体，近层大/快/虚焦。
    ///    近景大虚焦花瓣一屏只要三五片，立刻有「摄影机在场」的电影感，性价比最高。
    /// 6. 尺寸↔速度相关 —— 大的（近的）落得更快，白送的伪透视。
    /// 7. 地面堆积 —— 飘到下缘的叶片钉住再淡出，而不是凭空消失。
    ///
    /// 【无状态摆动的技巧】
    /// 横摆偏移写成「已存活时间 t 的纯函数」offset(t)，每帧只加 offset(t) - offset(t-dt)。
    /// 相位/频率/幅度全部由 particle.randomSeed 散列得到 →
    /// 不需要任何平行数组，粒子死亡重排也不会错位。
    /// </summary>
    public class VNFoliageSystem : MonoBehaviour
    {
        [Header("参数资产（留空 = 内置 Sakura 预设）")]
        public VNWeatherDef def;

        [Header("可选：预制的 VN/ParticleAlpha 材质资产；留空则运行时创建")]
        public Material sourceMaterial;

        [Header("发射区域（世界单位）。为 0 时自动匹配主相机可见范围")]
        public Vector2 area = Vector2.zero;

        [Header("剧本覆盖：<=0 表示不覆盖，用资产里的值")]
        public float densityOverride;
        public float speedOverride;
        public float sizeOverride;
        [Header("风力覆盖（用 float.NaN 表示不覆盖）")]
        public float windOverride = float.NaN;

        /// <summary>层索引：0=远 1=中 2=近</summary>
        class LayerRuntime
        {
            public ParticleSystem ps;
            public ParticleSystemRenderer renderer;
            public Material mat;
            public VNWeatherDef.LayerSettings settings;
            public ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[0];
            public float baseSpeed;   // 该层的平均下落速度（尺寸↔速度联动的基准）
        }

        readonly LayerRuntime[] _layers = new LayerRuntime[3];
        VNWeatherDef _builtin;              // 没给 def 时自己造的内置预设（负责销毁）
        Color _ambient = Color.white;       // 环境色（mood / 背景联动）
        float _gust;                        // 当前阵风速度（世界单位/秒）
        float _groundY;                     // 堆积线（画面下缘略上方）
        bool _playing = true;

        VNWeatherDef Def
        {
            get
            {
                if (def != null) { def.EnsureLayers(); return def; }
                if (_builtin == null) _builtin = VNWeatherDef.CreateBuiltin(VNLeafShape.Sakura);
                _builtin.EnsureLayers();
                return _builtin;
            }
        }

        /// <summary>当前生效的参数资产（预览窗口 / 存档用）</summary>
        public VNWeatherDef ActiveDef => Def;

        // ------------------------------------------------------------------
        // 创建
        // ------------------------------------------------------------------

        /// <summary>
        /// 运行时创建一套飘落系统。
        /// 沿用项目约定：先 SetActive(false) 挂好组件赋值，再激活，保证 Awake 看到的是最终值。
        /// </summary>
        public static VNFoliageSystem Create(VNWeatherDef def, Material sourceMaterial = null,
            Transform parent = null)
        {
            var go = new GameObject($"VN_Foliage_{(def != null ? def.id : "sakura")}");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, -1f);
            go.SetActive(false);
            var sys = go.AddComponent<VNFoliageSystem>();
            sys.def = def;
            sys.sourceMaterial = sourceMaterial;
            go.SetActive(true);
            return sys;
        }

        void Awake()
        {
            if (area == Vector2.zero) area = AutoArea();
            _groundY = -area.y * 0.5f + 0.06f;
            BuildLayers();
        }

        void OnDestroy()
        {
            foreach (var l in _layers)
                if (l != null && l.mat != null) Destroy(l.mat);
            if (_builtin != null) Destroy(_builtin);
        }

        static Vector2 AutoArea()
        {
            var cam = Camera.main;
            if (cam == null) return new Vector2(12f, 7f);
            if (cam.orthographic)
            {
                float h = cam.orthographicSize * 2f;
                return new Vector2(h * cam.aspect, h);
            }
            const float dist = 10f;
            float hh = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(hh * cam.aspect, hh);
        }

        // ------------------------------------------------------------------
        // 装配
        // ------------------------------------------------------------------

        void BuildLayers()
        {
            var d = Def;
            var settings = new[] { d.far, d.mid, d.near };
            var names = new[] { "Far", "Mid", "Near" };

            for (int i = 0; i < 3; i++)
            {
                if (_layers[i] == null)
                {
                    var go = new GameObject($"Layer{names[i]}", typeof(ParticleSystem));
                    go.transform.SetParent(transform, false);
                    _layers[i] = new LayerRuntime
                    {
                        ps = go.GetComponent<ParticleSystem>(),
                        renderer = go.GetComponent<ParticleSystemRenderer>(),
                    };
                }
                _layers[i].settings = settings[i];
                ConfigureLayer(_layers[i], d);
                _layers[i].ps.gameObject.SetActive(settings[i].enabled);
            }
        }

        /// <summary>把资产参数灌进一层粒子系统。改参数后重调即可（已存在的粒子不受影响）。</summary>
        void ConfigureLayer(LayerRuntime L, VNWeatherDef d)
        {
            var s = L.settings;
            var ps = L.ps;

            float sizeMul = s.sizeMul * (sizeOverride > 0f ? sizeOverride : 1f);
            float speedMul = s.speedMul * (speedOverride > 0f ? speedOverride : 1f);
            float rate = (densityOverride > 0f ? densityOverride : d.density) * s.rateMul;

            float spdMin = d.fallSpeed.x * speedMul, spdMax = d.fallSpeed.y * speedMul;
            L.baseSpeed = (spdMin + spdMax) * 0.5f;

            // 生命周期按「穿过整个画面还有余量」自动算 —— 避免花瓣飘到半空就淡没了
            float life = (area.y + 2.2f) / Mathf.Max(L.baseSpeed, 0.05f) * 1.15f;

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.9f, life * 1.25f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(spdMin, spdMax);
            main.startSize = new ParticleSystem.MinMaxCurve(
                d.size.x * sizeMul, d.size.y * sizeMul);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.maxParticles = Mathf.Clamp(Mathf.CeilToInt(rate * life * 1.3f) + 16, 24, 600);

            // 颜色：从渐变随机取整色 → 秋叶的红/橙/黄/褐色差；再乘环境色与层透明度
            main.startColor = new ParticleSystem.MinMaxGradient(BuildColorGradient(d, s))
            { mode = ParticleSystemGradientMode.RandomColor };

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = rate;

            // 顶端细带发射。风向左时把发射带整体右移，保证强风下画面左侧也不会空
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            float wind = CurrentWindBase(d);
            float widen = Mathf.Abs(wind) * life * 0.9f;
            shape.scale = new Vector3(area.x + 1.5f + widen, 0.4f, 0.1f);
            shape.position = new Vector3(-Mathf.Sign(wind == 0f ? -1f : wind) * widen * 0.5f,
                                         area.y * 0.5f + 0.7f, 0f);
            shape.rotation = new Vector3(90f, 0f, 0f);   // 发射方向朝下（+Z 转到 -Y）

            // 只在末段淡出：粒子诞生于屏外上方，不需要淡入；
            // 堆积逻辑靠「压缩 remainingLifetime」把粒子直接推进这段淡出。
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = TailFadeGradient();

            // 平面自转（翻转另由图集帧动画负责，两者叠加才像真的在空中打转）
            var rol = ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(d.spinSpeed.x, d.spinSpeed.y);

            // 关掉噪声：全局噪声场会让相邻粒子同步摆动，正是要消灭的「集体感」。
            // 横摆改由 LateUpdate 里每粒子独立相位的正弦提供。
            // （模块是 struct，必须先取出来再改，直接 ps.noise.enabled= 编译不过）
            var noise = ps.noise;
            noise.enabled = false;
            var vel = ps.velocityOverLifetime;
            vel.enabled = false;

            ConfigureFlip(ps, d, life);

            L.renderer.renderMode = ParticleSystemRenderMode.Billboard;
            L.renderer.sortingOrder = s.sortingOrder;
            L.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            L.renderer.receiveShadows = false;
            L.renderer.sharedMaterial = ResolveMaterial(L, d);
        }

        /// <summary>
        /// 图集帧动画 = 绕自身纵轴翻转。
        /// SingleRow + rowMode Random：每颗粒子随机抽一行（= 一种形态变体），
        /// 在该行内播放 12 帧 → 一整圈翻转。
        /// frameOverTime 用「两条斜率不同的直线 + multiplier」表达随机翻转圈数，
        /// 这样每片的翻转快慢都不一样，不会整屏同步。
        /// </summary>
        static void ConfigureFlip(ParticleSystem ps, VNWeatherDef d, float life)
        {
            float cycMin = Mathf.Max(0.05f, d.flipSpeed.x * life);
            float cycMax = Mathf.Max(cycMin + 0.05f, d.flipSpeed.y * life);

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = VNFoliageTextures.FlipFrames;
            tsa.numTilesY = VNFoliageTextures.Variants;
            tsa.animation = ParticleSystemAnimationType.SingleRow;
            tsa.rowMode = ParticleSystemAnimationRowMode.Random;
            tsa.cycleCount = 1;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(
                cycMax,
                AnimationCurve.Linear(0f, 0f, 1f, cycMin / cycMax),
                AnimationCurve.Linear(0f, 0f, 1f, 1f));
            // 每片起始角度不同 —— 少了这行，同一时刻出生的花瓣会朝向一致
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, VNFoliageTextures.FlipFrames);
        }

        Material ResolveMaterial(LayerRuntime L, VNWeatherDef d)
        {
            if (L.mat == null)
            {
                if (sourceMaterial != null && sourceMaterial.shader != null &&
                    sourceMaterial.shader.name == "VN/ParticleAlpha")
                {
                    L.mat = new Material(sourceMaterial);
                }
                else
                {
                    var shader = Shader.Find("VN/ParticleAlpha");
                    if (shader == null)
                    {
                        Debug.LogError("[VNEffects] 找不到 Shader \"VN/ParticleAlpha\"。", this);
                        return null;
                    }
                    L.mat = new Material(shader);
                }
                L.mat.hideFlags = HideFlags.DontSave;
            }

            L.mat.mainTexture = VNFoliageTextures.Atlas(d.shape);
            float b = Mathf.Max(d.hdrBoost, 0.01f);
            L.mat.SetColor("_TintColor", new Color(b, b, b, 1f));
            if (L.mat.HasProperty("_SoftBlur")) L.mat.SetFloat("_SoftBlur", L.settings.blur);
            return L.mat;
        }

        /// <summary>层颜色 = 资产渐变 × 环境色（按 aerial 向环境色靠拢）× 层透明度</summary>
        Gradient BuildColorGradient(VNWeatherDef d, VNWeatherDef.LayerSettings s)
        {
            var src = d.colors;
            var g = new Gradient();
            var keys = src != null ? src.colorKeys : null;
            if (keys == null || keys.Length == 0)
                keys = new[] { new GradientColorKey(Color.white, 0f) };

            var outKeys = new GradientColorKey[keys.Length];
            Color amb = d.tintByAmbient ? _ambient : Color.white;
            for (int i = 0; i < keys.Length; i++)
            {
                Color c = keys[i].color * amb;
                // 大气透视：远层向环境色靠拢并去饱和，像隔着一层空气
                if (s.aerial > 0f)
                {
                    float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                    Color hazed = Color.Lerp(c, new Color(lum, lum, lum) * amb, 0.75f);
                    c = Color.Lerp(c, hazed, s.aerial);
                }
                outKeys[i] = new GradientColorKey(c, keys[i].time);
            }

            g.SetKeys(outKeys, new[]
            {
                new GradientAlphaKey(s.alpha, 0f),
                new GradientAlphaKey(s.alpha, 1f)
            });
            return g;
        }

        static Gradient _tailFade;

        /// <summary>只在生命末段淡出（前段全不透明）</summary>
        static Gradient TailFadeGradient()
        {
            if (_tailFade == null)
            {
                _tailFade = new Gradient();
                _tailFade.SetKeys(
                    new[]
                    {
                        new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f)
                    },
                    new[]
                    {
                        new GradientAlphaKey(1f, 0f),
                        new GradientAlphaKey(1f, 0.82f),
                        new GradientAlphaKey(0f, 1f)
                    });
            }
            return _tailFade;
        }

        float CurrentWindBase(VNWeatherDef d)
            => float.IsNaN(windOverride) ? d.windBase : windOverride;

        // ------------------------------------------------------------------
        // 每帧：阵风 + 每粒子独立横摆 + 尺寸↔速度联动 + 地面堆积
        // ------------------------------------------------------------------

        void LateUpdate()
        {
            var d = Def;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateGust(d, dt);
            float wind = CurrentWindBase(d) + _gust;

            for (int i = 0; i < _layers.Length; i++)
            {
                var L = _layers[i];
                if (L == null || L.ps == null || !L.ps.gameObject.activeSelf) continue;
                StepLayer(L, d, wind, dt);
            }
        }

        /// <summary>
        /// 阵风：Perlin 噪声阈值化成脉冲 —— 大部分时间接近 0，偶尔冲高再回落。
        /// 直接用原始 Perlin 会变成「一直在晃」，反而不像风。
        /// </summary>
        void UpdateGust(VNWeatherDef d, float dt)
        {
            if (d.gustStrength <= 0f) { _gust = Mathf.Lerp(_gust, 0f, dt * 3f); return; }

            float n = Mathf.PerlinNoise(Time.time * Mathf.Max(d.gustFrequency, 0.001f), 0.37f);
            float pulse = Mathf.Pow(Mathf.Clamp01(n * 1.45f - 0.42f), 1.7f);
            float dir = CurrentWindBase(d) >= 0f ? 1f : -1f;
            // 平滑跟随：阵风起落要有惯性，硬切会像开关
            _gust = Mathf.Lerp(_gust, pulse * d.gustStrength * dir, dt * 2.2f);
        }

        void StepLayer(LayerRuntime L, VNWeatherDef d, float wind, float dt)
        {
            var ps = L.ps;
            int alive = ps.particleCount;
            if (alive == 0) return;
            if (L.buffer.Length < alive)
                L.buffer = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(alive)];

            int n = ps.GetParticles(L.buffer);
            float ampMin = d.swayAmplitude.x, ampMax = d.swayAmplitude.y;
            float frqMin = d.swayFrequency.x, frqMax = d.swayFrequency.y;
            float sizeMin = d.size.x * L.settings.sizeMul;
            float sizeMax = Mathf.Max(d.size.y * L.settings.sizeMul, sizeMin + 1e-4f);
            bool pile = d.groundPile;

            for (int i = 0; i < n; i++)
            {
                var p = L.buffer[i];

                // ---- 地面堆积：钉住 + 压缩剩余寿命直接进入淡出段 ----
                if (pile && p.position.y <= _groundY)
                {
                    p.position = new Vector3(p.position.x, _groundY, p.position.z);
                    p.velocity = Vector3.zero;
                    p.angularVelocity = 0f;
                    // colorOverLifetime 按 remaining/start 取值 → 压缩剩余寿命 = 立刻开始淡出
                    if (p.remainingLifetime > 1.4f) p.remainingLifetime = 1.4f;
                    L.buffer[i] = p;
                    continue;
                }

                uint seed = p.randomSeed;
                float amp = Mathf.Lerp(ampMin, ampMax, Hash01(seed, 0x9E37u));
                float frq = Mathf.Lerp(frqMin, frqMax, Hash01(seed, 0x85EBu));
                float phase = Hash01(seed, 0xC2B2u) * Mathf.PI * 2f;

                // 已存活时间。横摆写成 t 的纯函数，只取相邻两帧之差 →
                // 完全无状态，粒子数组重排也不会错位
                float t = p.startLifetime - p.remainingLifetime;
                float w = Mathf.PI * 2f * frq;
                float sway = amp * (Mathf.Sin(w * t + phase) - Mathf.Sin(w * (t - dt) + phase));

                float dx = sway + wind * dt;

                // 尺寸↔速度：大的（近的）落得更快，小的（远的）更慢 —— 伪透视
                float dy = 0f;
                if (d.sizeSpeedLink > 0f)
                {
                    float sizeNorm = Mathf.InverseLerp(sizeMin, sizeMax, p.startSize);
                    dy = -(sizeNorm - 0.5f) * d.sizeSpeedLink * L.baseSpeed * 0.6f * dt;
                }

                p.position += new Vector3(dx, dy, 0f);
                L.buffer[i] = p;
            }

            ps.SetParticles(L.buffer, n);
        }

        /// <summary>粒子随机种子 → [0,1]（同一颗粒子每帧结果稳定）</summary>
        static float Hash01(uint s, uint salt)
        {
            uint n = s * 747796405u + salt * 2891336453u;
            n = ((n >> ((int)(n >> 28) + 4)) ^ n) * 277803737u;
            n = (n >> 22) ^ n;
            return (n & 0xFFFFFFu) / 16777215f;
        }

        // ------------------------------------------------------------------
        // 外部 API
        // ------------------------------------------------------------------

        public bool IsEmitting
        {
            get
            {
                foreach (var l in _layers)
                    if (l != null && l.ps != null && l.ps.isEmitting) return true;
                return false;
            }
        }

        public void SetPlaying(bool playing)
        {
            _playing = playing;
            foreach (var l in _layers)
            {
                if (l == null || l.ps == null) continue;
                if (playing) l.ps.Play();
                else l.ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>换一套参数（换叶型 / 换资产）。已在空中的粒子自然飘完，新的按新参数。</summary>
        public void SetDef(VNWeatherDef newDef)
        {
            def = newDef;
            Rebuild();
        }

        /// <summary>环境色联动：黄昏场景里花瓣不该还是正午的粉。</summary>
        public void SetAmbient(Color ambient)
        {
            if (_ambient == ambient) return;
            _ambient = ambient;
            Rebuild();
        }

        /// <summary>
        /// 剧本临时覆盖单个参数（传 &lt;=0 / NaN = 不覆盖，恢复资产值）。
        /// wind 立即生效，其余在下一批发射的粒子上生效。
        /// </summary>
        public void ApplyOverrides(float density, float wind, float speed, float size)
        {
            densityOverride = density;
            windOverride = wind;
            speedOverride = speed;
            sizeOverride = size;
            Rebuild();
        }

        void Rebuild()
        {
            if (_layers[0] == null) return;   // Awake 还没跑
            var d = Def;
            var settings = new[] { d.far, d.mid, d.near };
            for (int i = 0; i < 3; i++)
            {
                if (_layers[i] == null) continue;
                _layers[i].settings = settings[i];
                ConfigureLayer(_layers[i], d);
                _layers[i].ps.gameObject.SetActive(settings[i].enabled);
                if (!_playing) _layers[i].ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// 一次性阵风冲击（樱吹雪 / 剧本手动刮风用）：立刻把阵风顶到峰值，之后自然回落。
        /// </summary>
        public void Gust(float strength = 2.5f)
        {
            float dir = CurrentWindBase(Def) >= 0f ? 1f : -1f;
            _gust = strength * dir;
        }

        /// <summary>爆发一批粒子（樱吹雪的瞬间涌入）</summary>
        public void Burst(int count)
        {
            // 中景与近景各分一份，远景不参与（远处的花瓣涌上来看不出效果）
            if (_layers[1]?.ps != null) _layers[1].ps.Emit(Mathf.Max(1, count));
            if (_layers[2]?.ps != null) _layers[2].ps.Emit(Mathf.Max(1, count / 12));
        }
    }
}
