using UnityEngine;

namespace VNEffects
{
    /// <summary>飘落物的叶型。形状与运动学参数都由它决定。</summary>
    public enum VNLeafShape
    {
        Sakura,    // 樱花瓣：双瓣椭圆并集，顶端 V 形缺口
        Maple,     // 枫叶：五裂锯齿边
        Ginkgo,    // 银杏：扇形，中央裂口 + 二叉叶脉
        Broadleaf, // 阔叶：椭圆带尖，中脉 + 侧脉 + 叶柄
        Bamboo,    // 竹叶 / 柳叶：细长微弯
    }

    /// <summary>
    /// 花瓣 / 落叶的程序化图集生成器（零美术依赖）。
    ///
    /// 【图集布局】列 = 翻转帧，行 = 形态变体
    ///   ┌─ 第 0 列 ─┬─ 第 1 列 ─┬ … ┬ 第 11 列 ┐
    ///   │ 变体0 0°  │ 变体0 30° │   │ 变体0 330°│  ← 行 0
    ///   │ 变体1 0°  │ …                          │  ← 行 1
    ///   └───────────┴───────────┴───┴───────────┘
    /// 粒子系统用 Texture Sheet Animation：
    ///   animation = SingleRow + rowMode = Random  → 每颗粒子随机抽一种形态变体
    ///   frameOverTime = 随机速度的 0→N 循环曲线   → 该行内逐帧播放 = 绕纵轴翻转
    ///   startFrame    = 随机                      → 每片起始角度不同，不会集体同步
    ///
    /// 【为什么翻转这么重要】
    /// 只做平面自转（rotationOverLifetime.z）的落瓣永远是「纸片」：宽度恒定、亮度恒定。
    /// 真实花瓣绕自身长轴翻转时，宽度按 |cos θ| 呼吸、背面因背光而变暗 ——
    /// 这两条线索是眼睛判断「这东西有厚度、在三维空间里」的核心依据，
    /// 也是业余落樱和商业落樱最大的观感差距来源。
    ///
    /// 【贴图内容】RGB = 明暗系数（叶脉/折痕/根深尖浅/背面压暗），A = 形状。
    /// 色相不写进贴图，由粒子 startColor 提供 → 一张图集可用于任意颜色。
    /// </summary>
    public static class VNFoliageTextures
    {
        /// <summary>每行的翻转帧数（= 绕纵轴转一整圈）</summary>
        public const int FlipFrames = 12;
        /// <summary>形态变体数（= 图集行数）。粒子随机抽行 → 同屏花瓣不会长得一样</summary>
        public const int Variants = 4;

        const int Cell = 64;
        // 形状坐标放大 → 图形只占格子 86%，四周留透明边距。
        // 既防止平面预旋转后超框，也保证 VN/ParticleAlpha 的 _SoftBlur 采样不会串到邻帧。
        const float Inset = 1f / 0.86f;

        static readonly Texture2D[] _atlas =
            new Texture2D[System.Enum.GetValues(typeof(VNLeafShape)).Length];

        /// <summary>取某叶型的图集（懒加载 + 全局缓存，一个叶型只生成一次）</summary>
        public static Texture2D Atlas(VNLeafShape shape)
        {
            int i = (int)shape;
            if (i < 0 || i >= _atlas.Length) i = 0;
            if (_atlas[i] == null) _atlas[i] = Build((VNLeafShape)i);
            return _atlas[i];
        }

        // ------------------------------------------------------------------
        // 图集烘焙
        // ------------------------------------------------------------------

        static Texture2D Build(VNLeafShape shape)
        {
            int w = Cell * FlipFrames, h = Cell * Variants;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = $"VN_Foliage_{shape}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var px = new Color32[w * h];
            for (int row = 0; row < Variants; row++)
            {
                var vr = VariantOf((int)shape * 977 + row * 131 + 17);
                for (int col = 0; col < FlipFrames; col++)
                {
                    float angle = col * Mathf.PI * 2f / FlipFrames;
                    for (int y = 0; y < Cell; y++)
                    {
                        float v = ((y + 0.5f) / Cell - 0.5f) * Inset;
                        // Unity 的 Texture Sheet Animation 行 0 在贴图顶部，
                        // 而 Texture2D 的 y=0 在底部 → 这里把行序倒过来写。
                        int ty = (Variants - 1 - row) * Cell + y;
                        int rowBase = ty * w + col * Cell;
                        for (int x = 0; x < Cell; x++)
                        {
                            float u = ((x + 0.5f) / Cell - 0.5f) * Inset;
                            px[rowBase + x] = Sample(shape, vr, angle, u, v);
                        }
                    }
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>形态变体：整体缩放 / 平面预旋转 / 宽度 / 明暗的随机微差</summary>
        struct VariantParams
        {
            public float scale, cos, sin, widthMul, shadeMul;
        }

        static VariantParams VariantOf(int seed)
        {
            float rot = Mathf.Lerp(-14f, 14f, Hash01(seed * 3 + 1)) * Mathf.Deg2Rad;
            return new VariantParams
            {
                scale = Mathf.Lerp(0.88f, 1.08f, Hash01(seed)),
                cos = Mathf.Cos(rot),
                sin = Mathf.Sin(rot),
                widthMul = Mathf.Lerp(0.90f, 1.10f, Hash01(seed * 3 + 2)),
                shadeMul = Mathf.Lerp(0.90f, 1.06f, Hash01(seed * 3 + 3)),
            };
        }

        /// <summary>
        /// 采样一个像素：先逆平面旋转到叶片本地系，再做绕纵轴翻转的逆变换，最后查形状函数。
        /// 顺序不能反 —— 翻转必须绕叶片自身的纵轴，而不是绕屏幕竖直轴。
        /// </summary>
        static Color32 Sample(VNLeafShape shape, VariantParams vr, float angle, float u, float v)
        {
            // 1. 逆平面旋转（变体的固定朝向差异）
            float u1 = u * vr.cos + v * vr.sin;
            float v1 = -u * vr.sin + v * vr.cos;

            // 2. 逆翻转：正交投影下宽度按 |cos θ| 收缩。
            //    下限 0.055 保证转到 90° 时不是彻底消失，而是残留一条「叶片侧边」的细线。
            float c = Mathf.Cos(angle);
            float ac = Mathf.Abs(c);
            float squeeze = Mathf.Max(ac, 0.055f);
            float su = u1 / squeeze;
            if (su < -0.5f || su > 0.5f) return default;
            if (c < 0f) su = -su;   // 背面：左右镜像

            // 3. 变体缩放
            su /= vr.scale;
            v1 /= vr.scale;
            if (su < -0.5f || su > 0.5f || v1 < -0.5f || v1 > 0.5f) return default;

            float shade;
            float a = Shape(shape, su, v1, vr.widthMul, out shade);
            if (a <= 0.002f) return default;

            shade *= vr.shadeMul;
            if (c < 0f) shade *= 0.62f;                       // 背面背光，明显更暗
            shade *= 1f + (1f - ac) * 0.16f;                  // 侧面转到边缘时的反光
            shade *= 1f + Mathf.Sin(angle) * su * 0.28f;      // 斜置时一侧受光一侧背光

            byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
            return new Color32(g, g, g, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
        }

        static float Shape(VNLeafShape shape, float u, float v, float widthMul, out float shade)
        {
            switch (shape)
            {
                case VNLeafShape.Maple: return Maple(u, v, widthMul, out shade);
                case VNLeafShape.Ginkgo: return Ginkgo(u, v, widthMul, out shade);
                case VNLeafShape.Broadleaf: return Broadleaf(u, v, widthMul, out shade);
                case VNLeafShape.Bamboo: return Bamboo(u, v, widthMul, out shade);
                default: return Sakura(u, v, widthMul, out shade);
            }
        }

        // ------------------------------------------------------------------
        // 形状函数：u/v ∈ [-0.5, 0.5]，v 向上为正（叶尖朝上、叶柄朝下）
        // 返回 alpha；out shade = 明暗系数（写进 RGB，粒子 startColor 再乘色相）
        // ------------------------------------------------------------------

        /// <summary>樱花瓣：两枚竖椭圆并集 → 顶端自然形成 V 形缺口；下接收窄的花瓣柄</summary>
        static float Sakura(float u, float v, float widthMul, out float shade)
        {
            const float soft = 0.085f;   // 以「归一化椭圆半径」为单位的软边
            float rx = 0.146f * widthMul, ry = 0.285f;
            const float cx = 0.095f, cy = 0.085f;

            float best = 0f;
            for (int s = -1; s <= 1; s += 2)
            {
                float nx = (u - s * cx) / rx;
                float ny = (v - cy) / ry;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                float a = Mathf.Clamp01((1f - r) / soft);
                if (a > best) best = a;
            }

            // 花瓣柄：从底部收窄，向上接进瓣身
            float t = Mathf.InverseLerp(-0.42f, 0.10f, v);
            if (t > 0f && t < 1f)
            {
                float hw = Mathf.Lerp(0.013f, 0.132f * widthMul, Mathf.Pow(t, 0.75f));
                float a = Mathf.Clamp01((hw - Mathf.Abs(u)) / 0.022f)
                        * Mathf.Clamp01((v + 0.44f) / 0.03f);
                if (a > best) best = a;
            }

            if (best <= 0f) { shade = 0f; return 0f; }

            // 根部深、瓣尖浅；中缝一道折痕暗线
            float lift = Mathf.InverseLerp(-0.40f, 0.36f, v);
            shade = Mathf.Lerp(0.62f, 1f, lift);
            shade *= 1f - Mathf.Clamp01(1f - Mathf.Abs(u) / 0.038f) * 0.16f;
            return best;
        }

        static readonly float[] MapleAngles = { 90f, 38f, 142f, -6f, 186f };
        static readonly float[] MapleLens = { 0.46f, 0.42f, 0.42f, 0.34f, 0.34f };
        const float MapleHalf = 36f;   // 每个裂片的半张角（度）

        /// <summary>枫叶：五个裂片取极坐标最大值 → 天然的深凹分裂；边缘叠正弦锯齿</summary>
        static float Maple(float u, float v, float widthMul, out float shade)
        {
            const float cy = -0.08f;   // 叶心（叶柄接入点）略低于中心
            float dx = u, dy = v - cy;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float thDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            float best = 0f, align = 0f;
            for (int i = 0; i < MapleAngles.Length; i++)
            {
                float k = Mathf.Abs(Mathf.DeltaAngle(MapleAngles[i], thDeg)) / MapleHalf;
                if (k >= 1f) continue;
                float rr = MapleLens[i] * widthMul *
                           Mathf.Pow(Mathf.Cos(k * Mathf.PI * 0.5f), 0.5f);
                if (rr > best) { best = rr; align = 1f - k; }
            }
            // 锯齿边缘
            if (best > 0f) best *= 1f + 0.05f * Mathf.Sin(thDeg * Mathf.Deg2Rad * 9f + 1.3f);

            float a = best > 0f ? Mathf.Clamp01((best - r) / 0.035f) : 0f;

            // 叶柄：叶心向下的细条
            if (v < cy)
            {
                float stem = Mathf.Clamp01((0.019f - Mathf.Abs(u)) / 0.012f)
                           * Mathf.Clamp01((v + 0.46f) / 0.04f);
                if (stem > a) a = stem;
            }
            if (a <= 0f) { shade = 0f; return 0f; }

            // 叶心深、外缘浅；沿裂片中轴有一道更亮的主脉
            shade = Mathf.Lerp(0.70f, 0.98f, Mathf.Clamp01(r / 0.42f)) * (1f + align * 0.10f);
            return a;
        }

        /// <summary>银杏：以叶柄为原点的扇形，小半径处收窄成柄，顶缘波浪 + 中央裂口</summary>
        static float Ginkgo(float u, float v, float widthMul, out float shade)
        {
            const float oy = -0.40f;
            float dy = v - oy;
            float a = 0f;
            float r = 0f, phi = 0f;

            if (dy > 0f)
            {
                r = Mathf.Sqrt(u * u + dy * dy);
                phi = Mathf.Atan2(u, dy);   // 相对竖直方向的张角

                // 张角随半径张开 → 小半径处自动收窄成叶柄
                float phiMax = 0.92f * widthMul *
                               Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / 0.26f));
                float R = 0.78f * (1f + 0.035f * Mathf.Sin(phi * 9f));   // 波浪外缘
                a = Mathf.Clamp01((phiMax - Mathf.Abs(phi)) / 0.10f)
                  * Mathf.Clamp01((R - r) / 0.035f);

                // 中央裂口（银杏的招牌特征）
                a *= 1f - Mathf.Clamp01((0.055f - Mathf.Abs(phi)) / 0.03f)
                        * Mathf.Clamp01((r - 0.42f) / 0.08f);
            }

            // 叶柄
            if (v < oy + 0.05f)
            {
                float stem = Mathf.Clamp01((0.017f - Mathf.Abs(u)) / 0.011f)
                           * Mathf.Clamp01((v + 0.47f) / 0.04f);
                if (stem > a) a = stem;
            }
            if (a <= 0f) { shade = 0f; return 0f; }

            // 二叉叶脉：从叶柄辐射的细密条纹
            shade = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(r / 0.7f))
                  * (0.94f + 0.09f * Mathf.Abs(Mathf.Sin(phi * 26f)));
            return a;
        }

        /// <summary>阔叶：两端收尖的纺锤形 + 中脉 + 斜向侧脉 + 叶柄</summary>
        static float Broadleaf(float u, float v, float widthMul, out float shade)
        {
            float t = Mathf.Clamp01(v + 0.5f);
            float w = 0.235f * widthMul *
                      Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.88f)), 0.70f);
            float a = Mathf.Clamp01((w - Mathf.Abs(u)) / 0.022f);

            // 叶柄：底部一小段细条
            float stem = Mathf.Clamp01((0.015f - Mathf.Abs(u)) / 0.010f)
                       * Mathf.Clamp01((0.13f - t) / 0.03f)
                       * Mathf.Clamp01(t / 0.02f);
            if (stem > a) a = stem;
            if (a <= 0f) { shade = 0f; return 0f; }

            float mid = Mathf.Clamp01(1f - Mathf.Abs(u) / 0.016f);                 // 中脉
            float lat = Mathf.Abs(Mathf.Sin((v - Mathf.Abs(u) * 1.5f) * 26f));     // 侧脉
            shade = Mathf.Lerp(0.70f, 0.96f, t) * (0.95f + 0.08f * lat) + mid * 0.10f;
            return a;
        }

        /// <summary>竹叶 / 柳叶：细长微弯，两端尖</summary>
        static float Bamboo(float u, float v, float widthMul, out float shade)
        {
            float t = Mathf.Clamp01(v + 0.5f);
            float bend = 0.055f * Mathf.Sin(Mathf.PI * t);   // 叶片自然弯曲
            float uu = u - bend;
            float w = 0.078f * widthMul * Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.45f);

            float a = Mathf.Clamp01((w - Mathf.Abs(uu)) / 0.016f)
                    * Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((1f - t) / 0.02f);
            if (a <= 0f) { shade = 0f; return 0f; }

            shade = Mathf.Lerp(0.72f, 0.98f, t)
                  + Mathf.Clamp01(1f - Mathf.Abs(uu) / 0.012f) * 0.08f;
            return a;
        }

        /// <summary>整数散列 → [0,1]，图集烘焙期的确定性抖动</summary>
        static float Hash01(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }
    }
}
