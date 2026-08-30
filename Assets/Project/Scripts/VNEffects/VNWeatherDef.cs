using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 飘落天气（落樱 / 落叶）的全部可调参数。
    ///
    /// 【为什么做成资产】
    /// 原先参数硬编码在 VNAmbientParticles.Configure() 的一个大 switch 里，
    /// 剧本层只能写一句 `weather petals`，连密度和风力都改不了。
    /// 搬进 ScriptableObject 后：策划可以在 Inspector 里调、可以在
    /// Tools → VN Effects → 预览 Preview → 天气预览 Weather Preview 里实时预览、剧本还能临时覆盖单个参数。
    ///
    /// 【怎么用】
    ///   1. 右键 Create → VN → Weather Def 建资产，填 id（剧本引用名，可中文）
    ///   2. 登记进 VNGameConfig 的「飘落天气库」（留空则自动扫 Assets/VNEffects/Weather）
    ///   3. 剧本：weather 落樱  /  weather 落樱 density:14 wind:-1.2
    /// 不建任何资产也能用：内置五套预设（sakura/maple/ginkgo/leaves/bamboo）走
    /// CreateBuiltin()，与旧的 `weather petals` 完全兼容。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Weather Def", fileName = "VNWeatherDef")]
    public class VNWeatherDef : ScriptableObject
    {
        /// <summary>单层景深的参数（远 / 中 / 近三层各一份）</summary>
        [System.Serializable]
        public class LayerSettings
        {
            [Header("这一层是否启用")]
            public bool enabled = true;
            [Header("发射速率倍率（相对 density）")]
            public float rateMul = 1f;
            [Header("尺寸 / 下落速度倍率")]
            public float sizeMul = 1f;
            public float speedMul = 1f;
            [Header("整层透明度")]
            [Range(0f, 1f)] public float alpha = 0.75f;
            [Header("向环境色靠拢的程度（大气透视：远层越大越「灰」）")]
            [Range(0f, 1f)] public float aerial;
            [Header("虚焦模糊半径（texel，近层用；0 = 清晰）")]
            [Range(0f, 4f)] public float blur;
            [Header("渲染排序（Canvas 之上要 > Canvas 的 sortingOrder）")]
            public int sortingOrder = 12;

            public LayerSettings Clone() => (LayerSettings)MemberwiseClone();
        }

        [Header("剧本 weather 命令引用的 id（可中文，如 落樱 / 枫叶）")]
        public string id;

        [Header("叶型（决定形状图集与默认运动学）")]
        public VNLeafShape shape = VNLeafShape.Sakura;

        [Header("颜色：每片从渐变上随机取一整色\n" +
                "秋叶的红/橙/黄/褐色差全靠它 —— 单色永远比有微差的一群假")]
        public Gradient colors = new Gradient();

        [Header("HDR 增益（>1 会被 Bloom 泛光。花瓣是实体，建议 1；只有近景高光片值得给一点）")]
        public float hdrBoost = 1f;

        [Header("密度：中景层每秒发射片数")]
        public float density = 7f;

        [Header("下落速度区间（世界单位/秒）")]
        public Vector2 fallSpeed = new Vector2(0.50f, 0.90f);

        [Header("尺寸区间（世界单位）")]
        public Vector2 size = new Vector2(0.10f, 0.18f);

        [Header("横向摆动幅度（世界单位）—— 每片相位/频率独立，绝不集体同步")]
        public Vector2 swayAmplitude = new Vector2(0.10f, 0.26f);
        [Header("横向摆动频率（Hz）。花瓣幅大频低（飘），落叶幅小频高（抖）")]
        public Vector2 swayFrequency = new Vector2(0.25f, 0.55f);

        [Header("平面自转速度（弧度/秒）")]
        public Vector2 spinSpeed = new Vector2(-0.6f, 0.6f);

        [Header("翻转速度（圈/秒，绕自身纵轴 —— 图集帧动画的播放速度）")]
        public Vector2 flipSpeed = new Vector2(0.15f, 0.45f);

        [Header("基础风力（世界单位/秒，负 = 向左吹）")]
        public float windBase = -0.25f;

        [Header("阵风强度（0 = 只留恒定风）。起风 → 整屏一起加速斜掠 → 风停，是画面「活」起来的关键")]
        public float gustStrength = 1.10f;
        [Header("阵风频率（Hz，越小阵风间隔越长）")]
        public float gustFrequency = 0.12f;

        [Header("尺寸↔速度相关性：1 = 大的（近的）落得明显更快（伪透视）")]
        [Range(0f, 1f)] public float sizeSpeedLink = 0.7f;

        [Header("景深三层")]
        public LayerSettings far = new LayerSettings
        { rateMul = 1.6f, sizeMul = 0.42f, speedMul = 0.55f, alpha = 0.32f, aerial = 0.65f, sortingOrder = 10 };
        public LayerSettings mid = new LayerSettings
        { rateMul = 1f, sizeMul = 1f, speedMul = 1f, alpha = 0.78f, aerial = 0.12f, sortingOrder = 12 };
        public LayerSettings near = new LayerSettings
        { rateMul = 0.10f, sizeMul = 2.6f, speedMul = 1.9f, alpha = 0.45f, blur = 2.4f, sortingOrder = 31 };

        [Header("地面堆积：飘到画面下缘的少量叶片静止贴住再缓慢淡出（秋叶尤其需要）")]
        public bool groundPile;

        [Header("是否随场景 mood / 背景环境色调整颜色（黄昏场景里花瓣不该还是正午的粉）")]
        public bool tintByAmbient = true;

        /// <summary>
        /// 三层设置的空值兜底：老资产可能缺字段，或者被人在 Inspector 里删空。
        /// 缺哪层就补一份默认，避免运行时 NRE。
        /// </summary>
        public void EnsureLayers()
        {
            if (far == null) far = new LayerSettings
            { rateMul = 1.6f, sizeMul = 0.42f, speedMul = 0.55f, alpha = 0.32f, aerial = 0.65f, sortingOrder = 10 };
            if (mid == null) mid = new LayerSettings
            { rateMul = 1f, sizeMul = 1f, speedMul = 1f, alpha = 0.78f, aerial = 0.12f, sortingOrder = 12 };
            if (near == null) near = new LayerSettings
            { rateMul = 0.10f, sizeMul = 2.6f, speedMul = 1.9f, alpha = 0.45f, blur = 2.4f, sortingOrder = 31 };
            if (colors == null) colors = new Gradient();
        }

        // ------------------------------------------------------------------
        // 内置预设：不建任何资产也能用
        // ------------------------------------------------------------------

        void Reset()
        {
            var b = CreateBuiltin(VNLeafShape.Sakura);
            CopyFrom(b);
            DestroyImmediate(b);
        }

        /// <summary>把另一份 def 的全部参数拷进自己（预览窗口写回、内置预设初始化用）</summary>
        public void CopyFrom(VNWeatherDef src)
        {
            if (src == null) return;
            shape = src.shape;
            colors = src.colors;
            hdrBoost = src.hdrBoost;
            density = src.density;
            fallSpeed = src.fallSpeed;
            size = src.size;
            swayAmplitude = src.swayAmplitude;
            swayFrequency = src.swayFrequency;
            spinSpeed = src.spinSpeed;
            flipSpeed = src.flipSpeed;
            windBase = src.windBase;
            gustStrength = src.gustStrength;
            gustFrequency = src.gustFrequency;
            sizeSpeedLink = src.sizeSpeedLink;
            if (src.far != null) far = src.far.Clone();
            if (src.mid != null) mid = src.mid.Clone();
            if (src.near != null) near = src.near.Clone();
            groundPile = src.groundPile;
            tintByAmbient = src.tintByAmbient;
        }

        /// <summary>
        /// 五套内置预设。形状差异只是外观，**运动学差异才是关键**：
        /// 花瓣轻（慢、幅大频低地飘），落叶重（快、幅小频高地抖 + 剧烈翻转 + 色差大）。
        /// </summary>
        public static VNWeatherDef CreateBuiltin(VNLeafShape shape)
        {
            var d = CreateInstance<VNWeatherDef>();
            d.hideFlags = HideFlags.DontSave;
            d.shape = shape;
            d.id = DefaultId(shape);
            d.name = $"VNWeatherDef_{shape}";

            switch (shape)
            {
                case VNLeafShape.Maple:
                    // 秋枫：重、快、翻转剧烈、色差极大（红↔橙↔黄↔褐）
                    d.colors = Grad(
                        new Color(0.88f, 0.22f, 0.14f), new Color(0.93f, 0.45f, 0.13f),
                        new Color(0.95f, 0.70f, 0.22f), new Color(0.62f, 0.32f, 0.14f));
                    d.density = 5.5f;
                    d.fallSpeed = new Vector2(0.95f, 1.60f);
                    d.size = new Vector2(0.14f, 0.26f);
                    d.swayAmplitude = new Vector2(0.06f, 0.16f);
                    d.swayFrequency = new Vector2(0.55f, 1.05f);
                    d.spinSpeed = new Vector2(-1.6f, 1.6f);
                    d.flipSpeed = new Vector2(0.45f, 1.10f);
                    d.windBase = -0.45f;
                    d.gustStrength = 1.8f;
                    d.gustFrequency = 0.16f;
                    d.groundPile = true;
                    break;

                case VNLeafShape.Ginkgo:
                    // 银杏：金黄单色系，比枫叶轻，打旋更明显
                    d.colors = Grad(
                        new Color(0.98f, 0.82f, 0.20f), new Color(0.95f, 0.72f, 0.16f),
                        new Color(0.88f, 0.62f, 0.18f), new Color(0.99f, 0.90f, 0.42f));
                    d.density = 6f;
                    d.fallSpeed = new Vector2(0.80f, 1.35f);
                    d.size = new Vector2(0.13f, 0.23f);
                    d.swayAmplitude = new Vector2(0.09f, 0.22f);
                    d.swayFrequency = new Vector2(0.45f, 0.90f);
                    d.spinSpeed = new Vector2(-1.9f, 1.9f);
                    d.flipSpeed = new Vector2(0.40f, 0.95f);
                    d.windBase = -0.38f;
                    d.gustStrength = 1.6f;
                    d.gustFrequency = 0.15f;
                    d.groundPile = true;
                    break;

                case VNLeafShape.Broadleaf:
                    // 阔叶：初秋的绿黄混杂到深秋褐色，最「普通」的落叶
                    d.colors = Grad(
                        new Color(0.55f, 0.62f, 0.24f), new Color(0.78f, 0.68f, 0.22f),
                        new Color(0.72f, 0.45f, 0.18f), new Color(0.48f, 0.34f, 0.18f));
                    d.density = 5f;
                    d.fallSpeed = new Vector2(0.90f, 1.45f);
                    d.size = new Vector2(0.13f, 0.24f);
                    d.swayAmplitude = new Vector2(0.07f, 0.18f);
                    d.swayFrequency = new Vector2(0.50f, 0.95f);
                    d.spinSpeed = new Vector2(-1.4f, 1.4f);
                    d.flipSpeed = new Vector2(0.35f, 0.90f);
                    d.windBase = -0.40f;
                    d.gustStrength = 1.7f;
                    d.gustFrequency = 0.14f;
                    d.groundPile = true;
                    break;

                case VNLeafShape.Bamboo:
                    // 竹叶 / 柳叶：细长轻盈，飘忽，几乎不堆积
                    d.colors = Grad(
                        new Color(0.62f, 0.78f, 0.42f), new Color(0.78f, 0.84f, 0.46f),
                        new Color(0.52f, 0.68f, 0.36f), new Color(0.86f, 0.86f, 0.55f));
                    d.density = 6.5f;
                    d.fallSpeed = new Vector2(0.60f, 1.05f);
                    d.size = new Vector2(0.12f, 0.22f);
                    d.swayAmplitude = new Vector2(0.14f, 0.32f);
                    d.swayFrequency = new Vector2(0.40f, 0.85f);
                    d.spinSpeed = new Vector2(-1.1f, 1.1f);
                    d.flipSpeed = new Vector2(0.30f, 0.75f);
                    d.windBase = -0.42f;
                    d.gustStrength = 1.5f;
                    d.gustFrequency = 0.13f;
                    break;

                default: // Sakura
                    // 落樱：轻、慢、幅大频低地飘；色差小但不能是单色
                    d.colors = Grad(
                        new Color(1.00f, 0.78f, 0.86f), new Color(0.99f, 0.86f, 0.90f),
                        new Color(0.96f, 0.70f, 0.80f), new Color(1.00f, 0.93f, 0.95f));
                    d.density = 7f;
                    d.fallSpeed = new Vector2(0.50f, 0.90f);
                    d.size = new Vector2(0.10f, 0.18f);
                    d.swayAmplitude = new Vector2(0.10f, 0.26f);
                    d.swayFrequency = new Vector2(0.25f, 0.55f);
                    d.spinSpeed = new Vector2(-0.6f, 0.6f);
                    d.flipSpeed = new Vector2(0.15f, 0.45f);
                    d.windBase = -0.25f;
                    d.gustStrength = 1.1f;
                    d.gustFrequency = 0.12f;
                    break;
            }
            return d;
        }

        /// <summary>叶型 → 剧本默认引用名（剧本可以直接写英文 id，不必建资产）</summary>
        public static string DefaultId(VNLeafShape shape)
        {
            switch (shape)
            {
                case VNLeafShape.Maple: return "maple";
                case VNLeafShape.Ginkgo: return "ginkgo";
                case VNLeafShape.Broadleaf: return "leaves";
                case VNLeafShape.Bamboo: return "bamboo";
                default: return "petals";
            }
        }

        /// <summary>剧本写的 id → 内置叶型（认英文正名与常见中文别名）。不认识返回 null。</summary>
        public static VNLeafShape? ParseBuiltinId(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            switch (id.Trim().ToLower())
            {
                case "petals": case "sakura": case "petal":
                case "落樱": case "樱花": case "花瓣":
                    return VNLeafShape.Sakura;
                case "maple": case "枫叶": case "红叶":
                    return VNLeafShape.Maple;
                case "ginkgo": case "银杏":
                    return VNLeafShape.Ginkgo;
                case "leaves": case "leaf": case "broadleaf":
                case "落叶": case "树叶": case "秋叶":
                    return VNLeafShape.Broadleaf;
                case "bamboo": case "willow": case "竹叶": case "柳叶":
                    return VNLeafShape.Bamboo;
                default:
                    return null;
            }
        }

        static Gradient Grad(params Color[] cs)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[cs.Length];
            for (int i = 0; i < cs.Length; i++)
                ck[i] = new GradientColorKey(cs[i], cs.Length == 1 ? 0f : i / (float)(cs.Length - 1));
            g.SetKeys(ck, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
            return g;
        }
    }
}
