using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 屏幕悬浮氛围粒子。挂到空物体上，运行时全程序化配置 ParticleSystem，
    /// 无需任何预制体或美术资源。三种预设：
    ///   Dust     —— 细小尘埃缓慢漂浮（自然氛围）
    ///   Sparkles —— 四芒星光一闪一闪（梦幻/浪漫）
    ///   Orbs     —— 大颗柔光光斑缓慢浮动（散景 Bokeh 感）
    /// 另提供静态方法 PlaySparkleBurst() 在任意位置爆发一簇星光（供出场演出调用）。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class VNAmbientParticles : MonoBehaviour
    {
        public enum Preset
        {
            Dust,      // 尘埃缓慢漂浮
            Sparkles,  // 四芒星一闪一闪
            Orbs,      // 大颗柔光光斑（散景）
            Petals,    // 落樱/花瓣：旋转、摇摆下落
            Rain,      // 雨：细长拉伸快速下落 + 底部溅落
            Snow,      // 雪：慢速飘落 + 噪声横移
            Fireflies, // 萤火虫：夜里缓慢游走、忽明忽暗
            Mist,      // 雾气/蒸汽：大团柔雾从下方缓缓升起（温泉/热浪配套）
        }

        [Tooltip("粒子风格预设")]
        public Preset preset = Preset.Sparkles;

        [Tooltip("粒子颜色（HDR 强度由 hdrBoost 提供）")]
        public Color tint = new Color(1f, 0.95f, 0.75f, 1f);

        [Tooltip("颜色 HDR 增益，>1 时粒子会被 Bloom 泛光")]
        public float hdrBoost = 1.8f;

        [Tooltip("发射区域（世界单位）。为 0 时自动匹配主相机可见范围")]
        public Vector2 area = Vector2.zero;

        [Tooltip("发射速率倍率")]
        public float rateMultiplier = 1f;

        [Tooltip("渲染排序（需要高于 Canvas 的 sortingOrder 才会显示在 UI 之上）")]
        public int sortingOrder = 10;

        [Tooltip("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        ParticleSystem _ps;
        Material _runtimeMat;
        ParticleSystem _splashPs; // 仅 Rain 预设使用

        /// <summary>
        /// 运行时创建一个氛围/天气粒子系统。
        /// 利用"先禁用再挂组件"保证 Awake 在字段赋值之后才执行。
        /// </summary>
        public static VNAmbientParticles Create(Preset preset, Color tint, int sortingOrder,
            Material sourceMaterial = null, float rateMultiplier = 1f, Transform parent = null,
            float hdrBoost = -1f)
        {
            var go = new GameObject($"VN_Particles_{preset}", typeof(ParticleSystem));
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, -1f);
            go.SetActive(false);
            var amb = go.AddComponent<VNAmbientParticles>();
            amb.preset = preset;
            amb.tint = tint;
            amb.sortingOrder = sortingOrder;
            amb.sourceMaterial = sourceMaterial;
            amb.rateMultiplier = rateMultiplier;
            if (hdrBoost > 0f) amb.hdrBoost = hdrBoost;
            go.SetActive(true); // 此刻才触发 Awake → Configure
            return amb;
        }

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            if (area == Vector2.zero) area = AutoArea();
            Configure();
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
            // 透视相机：取距相机 10 单位处的可见范围
            float dist = 10f;
            float hh = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return new Vector2(hh * cam.aspect, hh);
        }

        Material ResolveMaterial(Texture2D tex)
        {
            Material mat;
            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Additive")
            {
                mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Additive");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Additive\"。", this);
                    return null;
                }
                mat = new Material(shader);
            }
            mat.hideFlags = HideFlags.DontSave;
            mat.mainTexture = tex;
            _runtimeMat = mat;
            return mat;
        }

        void OnDestroy()
        {
            if (_runtimeMat != null) Destroy(_runtimeMat);
        }

        void Configure()
        {
            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startSpeed = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 300;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(area.x, area.y, 0.1f);

            var em = _ps.emission;
            em.enabled = true;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOutGradient();

            var noise = _ps.noise;
            var vel = _ps.velocityOverLifetime;
            var sol = _ps.sizeOverLifetime;
            var rol = _ps.rotationOverLifetime;

            Color hdrTint = new Color(tint.r * hdrBoost, tint.g * hdrBoost, tint.b * hdrBoost, tint.a);
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            switch (preset)
            {
                case Preset.Dust:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 12f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.35f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.65f));
                    em.rateOverTime = 6f * rateMultiplier;

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); // 模式须与 x/y 一致

                    noise.enabled = true;
                    noise.strength = 0.12f;
                    noise.frequency = 0.25f;
                    noise.scrollSpeed = 0.1f;

                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);
                    break;
                }
                case Preset.Sparkles:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.7f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 1f));
                    em.rateOverTime = 10f * rateMultiplier;

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); // 模式须与 x/y 一致

                    noise.enabled = true;
                    noise.strength = 0.08f;
                    noise.frequency = 0.4f;

                    // 一闪一闪：尺寸随生命周期多次起伏
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, TwinkleCurve());

                    rol.enabled = true;
                    rol.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

                    renderer.material = ResolveMaterial(VNProceduralTextures.Sparkle);
                    break;
                }
                case Preset.Orbs:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.08f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.2f));
                    em.rateOverTime = 2.5f * rateMultiplier;

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f); // 模式须与 x/y 一致

                    noise.enabled = true;
                    noise.strength = 0.15f;
                    noise.frequency = 0.15f;

                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);
                    break;
                }
                case Preset.Petals:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.55f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.85f));
                    em.rateOverTime = 7f * rateMultiplier;

                    // 从画面顶端上方的一条细带生成，飘落穿过整个画面
                    shape.scale = new Vector3(area.x + 2f, 0.5f, 0.1f);
                    shape.position = new Vector3(0f, area.y * 0.5f + 0.8f, 0f);

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.5f, -0.1f); // 微风向左
                    vel.y = new ParticleSystem.MinMaxCurve(-0.85f, -0.5f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                    // 横向大摆动、纵向几乎不受噪声影响 → 左右摇曳飘落
                    noise.enabled = true;
                    noise.separateAxes = true;
                    noise.strengthX = 0.55f;
                    noise.strengthY = 0.08f;
                    noise.strengthZ = 0f;
                    noise.frequency = 0.3f;

                    rol.enabled = true;
                    rol.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f); // 翻转旋转

                    renderer.material = ResolveMaterial(VNProceduralTextures.Petal);
                    break;
                }
                case Preset.Rain:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.5f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.25f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.45f));
                    main.maxParticles = 600;
                    em.rateOverTime = 90f * rateMultiplier;

                    // 顶端细带 + 形状旋转 90° 使发射方向朝下，
                    // 用 startSpeed 提供真实粒子速度 → 拉伸渲染方向才正确
                    shape.scale = new Vector3(area.x + 3f, 0.5f, 0.1f);
                    shape.position = new Vector3(0f, area.y * 0.5f + 0.8f, 0f);
                    shape.rotation = new Vector3(90f, 0f, 0f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 13f);

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-1.2f, -0.7f); // 斜风
                    vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 5f;
                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);

                    BuildRainSplashes(hdrTint, renderer);
                    break;
                }
                case Preset.Snow:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 24f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.055f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.5f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.9f));
                    main.maxParticles = 600;
                    em.rateOverTime = 24f * rateMultiplier;

                    shape.scale = new Vector3(area.x + 2f, 0.5f, 0.1f);
                    shape.position = new Vector3(0f, area.y * 0.5f + 0.8f, 0f);

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                    vel.y = new ParticleSystem.MinMaxCurve(-0.7f, -0.45f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                    noise.enabled = true;
                    noise.strength = 0.3f;
                    noise.frequency = 0.12f;
                    noise.scrollSpeed = 0.03f;

                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);
                    break;
                }
                case Preset.Fireflies:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 10f);
                    main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.7f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 1f));
                    em.rateOverTime = 5f * rateMultiplier;

                    // 只在画面中下部游走（贴近地面/草丛的感觉）
                    shape.scale = new Vector3(area.x, area.y * 0.65f, 0.1f);
                    shape.position = new Vector3(0f, -area.y * 0.17f, 0f);

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                    vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.06f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                    noise.enabled = true;
                    noise.strength = 0.35f;
                    noise.frequency = 0.5f;

                    // 忽明忽暗：复用星光的闪烁曲线
                    sol.enabled = true;
                    sol.size = new ParticleSystem.MinMaxCurve(1f, TwinkleCurve());

                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);
                    break;
                }
                case Preset.Mist:
                {
                    main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 15f);
                    main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.045f),
                        new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.1f));
                    em.rateOverTime = 3.5f * rateMultiplier;

                    // 从画面下方一条带生成，缓缓升起
                    shape.scale = new Vector3(area.x + 2f, 1f, 0.1f);
                    shape.position = new Vector3(0f, -area.y * 0.5f - 0.5f, 0f);

                    vel.enabled = true;
                    vel.space = ParticleSystemSimulationSpace.Local;
                    vel.x = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                    noise.enabled = true;
                    noise.strength = 0.2f;
                    noise.frequency = 0.1f;
                    noise.scrollSpeed = 0.05f;

                    rol.enabled = true;
                    rol.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f); // 缓慢翻滚

                    renderer.material = ResolveMaterial(VNProceduralTextures.SoftCircle);
                    break;
                }
            }
        }

        /// <summary>雨的底部溅落子系统（挂为子物体，与雨同开同关）</summary>
        void BuildRainSplashes(Color hdrTint, ParticleSystemRenderer rainRenderer)
        {
            var go = new GameObject("RainSplashes", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, -area.y * 0.5f + 0.25f, 0f);
            _splashPs = go.GetComponent<ParticleSystem>();

            var main = _splashPs.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.3f),
                new Color(hdrTint.r, hdrTint.g, hdrTint.b, 0.5f));
            main.gravityModifier = 2f;
            main.maxParticles = 200;

            var em = _splashPs.emission;
            em.enabled = true;
            em.rateOverTime = 60f * rateMultiplier;

            var shape = _splashPs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(area.x, 0.1f, 0.1f);
            shape.rotation = new Vector3(-90f, 0f, 0f); // 朝上溅起

            var col = _splashPs.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOutGradient();

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;
            renderer.sharedMaterial = rainRenderer.sharedMaterial; // 共用雨滴材质
        }

        static ParticleSystem.MinMaxGradient FadeInOutGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(g);
        }

        /// <summary>闪烁曲线：生命周期内亮度（尺寸）多次起伏，营造"一闪一闪"</summary>
        static AnimationCurve TwinkleCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.3f, 0.35f),
                new Keyframe(0.5f, 0.95f),
                new Keyframe(0.68f, 0.3f),
                new Keyframe(0.85f, 0.8f),
                new Keyframe(1f, 0f));
        }

        public void SetPlaying(bool playing)
        {
            if (_ps == null) return;
            if (playing)
            {
                _ps.Play();
                if (_splashPs != null) _splashPs.Play();
            }
            else
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_splashPs != null) _splashPs.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public bool IsEmitting => _ps != null && _ps.isEmitting;

        // ------------------------------------------------------------------
        // 星光爆发（一次性，供出场演出调用）
        // ------------------------------------------------------------------

        static Material _burstMat;

        /// <summary>
        /// 在世界坐标 pos 处爆发一簇向四周飞散、逐渐消隐的星光。
        /// 自动创建、自动销毁，调用即忘。
        /// </summary>
        public static void PlaySparkleBurst(Vector3 pos, Color color, int count = 24,
            float speed = 1.6f, int sortingOrder = 12)
        {
            var go = new GameObject("VN_SparkleBurst");
            go.transform.position = pos + new Vector3(0f, 0f, -0.5f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            float boost = 2.2f;
            main.startColor = new Color(color.r * boost, color.g * boost, color.b * boost, 1f);
            main.gravityModifier = -0.02f; // 轻微上飘
            main.maxParticles = count;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.25f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeInOutGradient();

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;

            if (_burstMat == null)
            {
                var shader = Shader.Find("VN/Additive");
                if (shader != null)
                {
                    _burstMat = new Material(shader) { hideFlags = HideFlags.DontSave };
                    _burstMat.mainTexture = VNProceduralTextures.Sparkle;
                }
            }
            if (_burstMat != null) renderer.material = _burstMat;

            ps.Play();
            Object.Destroy(go, 2f);
        }
    }
}
