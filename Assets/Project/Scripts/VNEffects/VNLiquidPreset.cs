using UnityEngine;

namespace VNEffects
{
    /// <summary>内置液体种类（剧本 type: 参数取这几个名字，大小写不敏感）</summary>
    public enum VNLiquidType
    {
        Water, // 清水：透亮、落得快、干得快
        Blood, // 血：浓稠、下坠慢、几乎不干
        Ink,   // 墨：最不透明、无高光
        Slime  // 黏液：拉丝、下滑极慢、自发光
    }

    /// <summary>
    /// liquid 命令的一组参数。剧本层（VNScriptRunner）、舞台层（VNStage）、
    /// 存档重建三方共用同一个结构，避免各自维护一份参数列表而慢慢走样。
    /// </summary>
    public struct VNLiquidArgs
    {
        public string type;   // 液体类型（water/blood/ink/slime 或中文别名）
        public bool on;       // spray/click/wet/cover 的开关位
        public float x, y;    // 喷射点，屏幕比例 0~1
        public float power;   // 力度倍率
        public float dir;     // 喷射方向角（0 = 右，90 = 上）
        public float spread;  // 扇形张角半角
        public float rate;    // 间歇喷射的频率倍率
        public float screen;  // 命中镜头的概率倍率（0 = 绝不溅到屏幕上）
        public float amount;  // 常驻湿镜头的浓度倍率

        /// <summary>全部参数的默认值——剧本里没写的项一律取这里，三方保持一致</summary>
        public static VNLiquidArgs Default => new VNLiquidArgs
        {
            type = null,
            on = true,
            x = 0.5f,
            y = 0.35f,
            power = 1f,
            dir = 90f,
            spread = 40f,
            rate = 1f,
            screen = 1f,
            amount = 1f,
        };
    }

    /// <summary>
    /// 一种液体的全部手感参数。
    ///
    /// 【为什么不做成 ScriptableObject】
    /// 天气那套（VNWeatherDef）资产化是因为要在 Preview 窗口反复微调形态；
    /// 液体的参数量少一个数量级，且剧本命令的 power/screen 已经覆盖了日常调整需求。
    /// 先内置，等真的需要逐项精调再抽资产——那时把本类字段原样搬进 SO 即可，
    /// 调用方全部走 <see cref="Get"/>，不会有第二处需要改。
    ///
    /// 【调参的直觉】
    /// 黏度不是一个参数，是四个参数的合谋：gravityScale（下坠快慢）、
    /// stretch（空中被拉多长）、dripSpeed（在屏幕上往下流多快）、drySeconds（多久干）。
    /// 想让某种液体"更黏"，四个一起往下调，只调其中一个会得到"轻飘飘的血"这种怪东西。
    /// </summary>
    public class VNLiquidPreset
    {
        public VNLiquidType type;

        // ---- 空中飞行的水珠 ----
        [Tooltip("主体颜色（顶点色，会被钳到 1，发光靠 glowTint）")]
        public Color tint;
        [Tooltip("主体不透明度：清水要很低才透，墨/血要高")]
        public float bodyAlpha;
        [Tooltip("高光色（HDR，>1 才会被 Bloom 拾取）")]
        public Color glowTint;
        [Tooltip("高光 HDR 增益")]
        public float glowBoost;
        [Tooltip("高光粒子数量相对主体的比例（0 = 完全不发光，墨用）")]
        public float glowRatio;

        [Tooltip("重力倍率：越小越黏，越大越像水")]
        public float gravityScale;
        [Tooltip("空气阻尼：黏液拖得住速度")]
        public float drag;
        public float lifeMin, lifeMax;
        public float sizeMin, sizeMax;
        [Tooltip("拉伸公告板的速度系数——水感的一大半来自这里，球形水珠永远像泡泡")]
        public float stretch;
        [Tooltip("初速倍率")]
        public float speedScale;

        [Tooltip("一次爆溅的主体粒子数基准")]
        public int burstCount;
        [Tooltip("跟着爆溅一起撒的低速碎珠数基准")]
        public int splinterCount;

        // ---- 溅到镜头上的水渍 ----
        [Tooltip("单颗水珠命中镜头的基础概率（剧本 screen: 会整体缩放它）")]
        public float screenChance;
        [Tooltip("屏幕水渍下滑速度（像素/秒，1080p 基准）")]
        public float dripSpeed;
        [Tooltip("水渍从出现到完全干涸的秒数")]
        public float drySeconds;
        [Tooltip("水渍尺寸倍率")]
        public float dropScale;
        [Tooltip("下滑水痕的浓度（0 = 不留痕）")]
        public float trailAlpha;
        [Tooltip("水渍挂住不动的时间范围——表面张力撑不住之前的那一下停顿")]
        public float clingMin, clingMax;

        static VNLiquidPreset[] _all;

        /// <summary>按类型取预设（全局共享只读实例，不要在外部改字段）</summary>
        public static VNLiquidPreset Get(VNLiquidType type)
        {
            if (_all == null) Build();
            return _all[(int)type];
        }

        /// <summary>按剧本里的 type: 字符串取预设，无法识别时回退清水</summary>
        public static VNLiquidPreset Get(string id, int line = 0)
        {
            if (string.IsNullOrEmpty(id)) return Get(VNLiquidType.Water);
            if (System.Enum.TryParse(id, true, out VNLiquidType t)) return Get(t);
            // 中文别名：剧本里写中文更顺手
            switch (id)
            {
                case "水": case "清水": return Get(VNLiquidType.Water);
                case "血": case "鲜血": return Get(VNLiquidType.Blood);
                case "墨": case "墨水": return Get(VNLiquidType.Ink);
                case "黏液": case "粘液": return Get(VNLiquidType.Slime);
            }
            Debug.LogWarning($"[VNScript] 第 {line} 行：未知液体类型「{id}」，按清水处理");
            return Get(VNLiquidType.Water);
        }

        static void Build()
        {
            _all = new VNLiquidPreset[4];

            // 清水：主体几乎透明（bodyAlpha 0.26）——水看起来像水靠的是边缘环和高光，
            // 不是靠"一坨蓝色"。把 tint 调成饱和蓝会立刻变成果冻。
            _all[(int)VNLiquidType.Water] = new VNLiquidPreset
            {
                type = VNLiquidType.Water,
                tint = new Color(0.78f, 0.90f, 0.97f),
                bodyAlpha = 0.26f,
                glowTint = new Color(0.85f, 0.95f, 1f),
                glowBoost = 2.2f,
                glowRatio = 0.5f,
                gravityScale = 1f,
                drag = 0.02f,
                lifeMin = 0.65f, lifeMax = 1.15f,
                sizeMin = 0.10f, sizeMax = 0.26f,
                stretch = 0.36f,
                speedScale = 1f,
                burstCount = 34,
                splinterCount = 18,
                screenChance = 0.32f,
                dripSpeed = 185f,
                drySeconds = 7f,
                dropScale = 1f,
                trailAlpha = 0.5f,
                clingMin = 0.25f, clingMax = 1.1f,
            };

            // 血：黏、重、落得慢、拖尾短、几乎不干。dripSpeed 只有清水的三分之一，
            // 这条比颜色更能让人认出"这是血不是红水"。
            _all[(int)VNLiquidType.Blood] = new VNLiquidPreset
            {
                type = VNLiquidType.Blood,
                tint = new Color(0.52f, 0.055f, 0.075f),
                bodyAlpha = 0.92f,
                glowTint = new Color(0.75f, 0.16f, 0.14f),
                glowBoost = 1.05f,
                glowRatio = 0.12f,
                gravityScale = 0.78f,
                drag = 0.16f,
                lifeMin = 0.95f, lifeMax = 1.6f,
                sizeMin = 0.13f, sizeMax = 0.34f,
                stretch = 0.19f,
                speedScale = 0.88f,
                burstCount = 30,
                splinterCount = 22,
                screenChance = 0.42f,
                dripSpeed = 58f,
                drySeconds = 26f,
                dropScale = 1.25f,
                trailAlpha = 0.78f,
                clingMin = 0.8f, clingMax = 2.6f,
            };

            // 墨：最不透明、几乎无高光（glowRatio 0.05）。黑色液体上的高光如果做足，
            // 会变成"发光的黑水"，反而假。
            _all[(int)VNLiquidType.Ink] = new VNLiquidPreset
            {
                type = VNLiquidType.Ink,
                tint = new Color(0.075f, 0.085f, 0.13f),
                bodyAlpha = 0.94f,
                glowTint = new Color(0.35f, 0.40f, 0.55f),
                glowBoost = 0.85f,
                glowRatio = 0.05f,
                gravityScale = 0.92f,
                drag = 0.10f,
                lifeMin = 0.85f, lifeMax = 1.45f,
                sizeMin = 0.11f, sizeMax = 0.30f,
                stretch = 0.24f,
                speedScale = 0.95f,
                burstCount = 32,
                splinterCount = 20,
                screenChance = 0.38f,
                dripSpeed = 82f,
                drySeconds = 18f,
                dropScale = 1.15f,
                trailAlpha = 0.85f,
                clingMin = 0.6f, clingMax = 2f,
            };

            // 黏液：最慢、最拉丝、自发光。gravityScale 0.55 + drag 0.3 让它"挂"在空中，
            // dripSpeed 36 让它在镜头上几乎是爬的。
            _all[(int)VNLiquidType.Slime] = new VNLiquidPreset
            {
                type = VNLiquidType.Slime,
                tint = new Color(0.55f, 0.86f, 0.26f),
                bodyAlpha = 0.72f,
                glowTint = new Color(0.60f, 1f, 0.35f),
                glowBoost = 1.9f,
                glowRatio = 0.45f,
                gravityScale = 0.55f,
                drag = 0.30f,
                lifeMin = 1.25f, lifeMax = 2.1f,
                sizeMin = 0.14f, sizeMax = 0.36f,
                stretch = 0.15f,
                speedScale = 0.8f,
                burstCount = 26,
                splinterCount = 16,
                screenChance = 0.45f,
                dripSpeed = 36f,
                drySeconds = 30f,
                dropScale = 1.4f,
                trailAlpha = 0.7f,
                clingMin = 1f, clingMax = 3.2f,
            };
        }
    }
}
