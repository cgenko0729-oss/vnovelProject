using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 运行时程序化生成粒子/光晕贴图，无需任何美术资源。
    /// 所有贴图懒加载并缓存，整个游戏生命周期只生成一次。
    /// </summary>
    public static class VNProceduralTextures
    {
        static Texture2D _softCircle;
        static Texture2D _sparkle;
        static Texture2D _radialGlow;
        static Texture2D _lightBeam;
        static Texture2D _edgeGlowFrame;
        static Texture2D _petal;
        static Sprite _radialGlowSprite;

        /// <summary>柔边圆形（尘埃 / 光斑粒子用）</summary>
        public static Texture2D SoftCircle
        {
            get
            {
                if (_softCircle == null)
                    _softCircle = Generate("VN_SoftCircle", 64, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        return Mathf.Pow(Mathf.Clamp01(1f - r / 0.5f), 1.8f);
                    });
                return _softCircle;
            }
        }

        /// <summary>四芒星光（闪烁星光粒子用）</summary>
        public static Texture2D Sparkle
        {
            get
            {
                if (_sparkle == null)
                    _sparkle = Generate("VN_Sparkle", 64, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float core = Mathf.Pow(Mathf.Clamp01(1f - r / 0.35f), 3f);
                        float nx = Mathf.Abs(dx) / 0.5f;
                        float ny = Mathf.Abs(dy) / 0.5f;
                        // 横竖两道细长的星芒
                        float spikeH = Mathf.Pow(Mathf.Clamp01(1f - ny), 24f) * Mathf.Pow(Mathf.Clamp01(1f - nx), 2f);
                        float spikeV = Mathf.Pow(Mathf.Clamp01(1f - nx), 24f) * Mathf.Pow(Mathf.Clamp01(1f - ny), 2f);
                        return Mathf.Clamp01(core + (spikeH + spikeV) * 0.9f);
                    });
                return _sparkle;
            }
        }

        /// <summary>大尺寸径向光晕（图片背后的柔光光环用）</summary>
        public static Texture2D RadialGlow
        {
            get
            {
                if (_radialGlow == null)
                    _radialGlow = Generate("VN_RadialGlow", 256, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        return Mathf.Pow(Mathf.Clamp01(1f - r / 0.5f), 2.5f);
                    });
                return _radialGlow;
            }
        }

        /// <summary>
        /// 竖直光束（God Rays 用）：横向柔边、纵向从上（亮）到下（渐隐）。
        /// 使用时把 RawImage 的 pivot 设在顶部，旋转即得斜射光束。
        /// </summary>
        public static Texture2D LightBeam
        {
            get
            {
                if (_lightBeam == null)
                    _lightBeam = Generate("VN_LightBeam", 128, 512, (dx, dy) =>
                    {
                        // dy ∈ [-0.5, 0.5]，+0.5 为贴图顶部
                        float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dx) * 2f), 1.6f);
                        float t = dy + 0.5f; // 0 = 底部, 1 = 顶部
                        float along = Mathf.Pow(Mathf.Clamp01(t), 1.3f);
                        return across * along;
                    });
                return _lightBeam;
            }
        }

        /// <summary>屏幕边缘泛光框：越靠近边缘越亮，中心完全透明（情绪泛光用）</summary>
        public static Texture2D EdgeGlowFrame
        {
            get
            {
                if (_edgeGlowFrame == null)
                    _edgeGlowFrame = Generate("VN_EdgeGlowFrame", 256, 256, (dx, dy) =>
                    {
                        float x = dx + 0.5f;
                        float y = dy + 0.5f;
                        float edgeDist = Mathf.Min(Mathf.Min(x, 1f - x), Mathf.Min(y, 1f - y));
                        return Mathf.Pow(Mathf.Clamp01(1f - edgeDist / 0.28f), 2.2f);
                    });
                return _edgeGlowFrame;
            }
        }

        /// <summary>柔边椭圆花瓣（落樱/落叶粒子用）</summary>
        public static Texture2D Petal
        {
            get
            {
                if (_petal == null)
                    _petal = Generate("VN_Petal", 64, 64, (dx, dy) =>
                    {
                        float nx = dx / 0.42f;
                        float ny = (dy + 0.06f) / 0.26f; // 轻微偏心，更像花瓣
                        float r = Mathf.Sqrt(nx * nx + ny * ny);
                        return Mathf.Pow(Mathf.Clamp01(1f - r), 1.3f);
                    });
                return _petal;
            }
        }

        static Texture2D[] _speedLines;

        /// <summary>集中线贴图的变体数量（VNSpeedLines 轮换这些变体实现"闪帧"）</summary>
        public const int SpeedLineVariantCount = 3;

        /// <summary>
        /// 漫画集中线/速度线（512px）：从四周边缘向中心收拢的楔形放射线，
        /// 中心留空、各线内端参差、疏密不均，模拟手绘效果。
        /// 不同 variant 用不同随机种子 → 轮换播放即为逐帧闪化。
        /// </summary>
        public static Texture2D SpeedLines(int variant)
        {
            if (_speedLines == null) _speedLines = new Texture2D[SpeedLineVariantCount];
            int idx = Mathf.Abs(variant) % SpeedLineVariantCount;
            if (_speedLines[idx] == null)
            {
                int seed = idx * 7919 + 31;
                _speedLines[idx] = Generate($"VN_SpeedLines_{idx}", 512, (dx, dy) =>
                {
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r < 0.12f) return 0f; // 中心留白

                    const int rayCount = 110;
                    float a = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f) * rayCount;
                    int ray = Mathf.FloorToInt(a) % rayCount;
                    float frac = a - Mathf.Floor(a) - 0.5f; // 扇区内偏移 [-0.5, 0.5]

                    float h1 = Hash01(ray * 3 + seed);
                    if (h1 < 0.3f) return 0f; // 三成扇区留空 → 疏密不均更像手绘
                    float h2 = Hash01(ray * 3 + 1 + seed);
                    float h3 = Hash01(ray * 3 + 2 + seed);

                    // 楔形线条：外缘宽、向中心收成尖，各线内端半径参差
                    float inner = Mathf.Lerp(0.15f, 0.32f, h3);
                    float taper = Mathf.InverseLerp(inner, 0.72f, r);
                    if (taper <= 0f) return 0f;
                    float halfWidth = Mathf.Lerp(0.06f, 0.34f, h2) * taper;
                    float edge = 1f - Mathf.Clamp01(
                        (Mathf.Abs(frac) - halfWidth * 0.55f) /
                        Mathf.Max(halfWidth * 0.45f, 1e-4f));
                    return Mathf.Clamp01(edge);
                });
            }
            return _speedLines[idx];
        }

        /// <summary>整数散列 → [0,1] 伪随机（贴图生成期确定性抖动用）</summary>
        static float Hash01(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }

        static Texture2D _meteorStreak;

        /// <summary>
        /// 流星拖尾（256×64）：右端亮头 + 向左渐隐渐细的尾迹。
        /// 使用时把 RawImage 旋转到飞行方向（贴图 +X 即流星头朝向）。
        /// </summary>
        public static Texture2D MeteorStreak
        {
            get
            {
                if (_meteorStreak == null)
                    _meteorStreak = Generate("VN_MeteorStreak", 256, 64, (dx, dy) =>
                    {
                        // 头部亮核：右端小光点
                        float hx = (dx - 0.42f) / 0.07f;
                        float hy = dy / 0.28f;
                        float head = Mathf.Pow(
                            Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy)), 1.6f);
                        // 尾迹：向左渐隐、越远越细
                        float t = Mathf.Clamp01((dx + 0.5f) / 0.92f); // 0=尾端 1=头部
                        float halfW = Mathf.Lerp(0.04f, 0.30f, t);
                        float across = Mathf.Pow(
                            Mathf.Clamp01(1f - Mathf.Abs(dy) / halfW), 1.8f);
                        float tail = across * Mathf.Pow(t, 2.2f) * 0.85f;
                        return Mathf.Clamp01(head + tail);
                    });
                return _meteorStreak;
            }
        }

        static Texture2D _cloudPuff;

        /// <summary>
        /// 蓬松云团（256×128）：数个柔边椭圆瓣叠加、云底压平（云本体缓移用）。
        /// </summary>
        public static Texture2D CloudPuff
        {
            get
            {
                if (_cloudPuff == null)
                {
                    // 椭圆瓣：x偏移 / y偏移 / 半宽 / 半高（相对贴图归一坐标）
                    var lobes = new[]
                    {
                        new Vector4(0f, 0.02f, 0.30f, 0.16f),
                        new Vector4(-0.22f, -0.04f, 0.20f, 0.12f),
                        new Vector4(0.20f, -0.03f, 0.22f, 0.13f),
                        new Vector4(-0.08f, 0.10f, 0.18f, 0.11f),
                        new Vector4(0.10f, 0.09f, 0.16f, 0.10f),
                    };
                    _cloudPuff = Generate("VN_CloudPuff", 256, 128, (dx, dy) =>
                    {
                        float a = 0f;
                        foreach (var l in lobes)
                        {
                            float nx = (dx - l.x) / l.z;
                            float ny = (dy - l.y) / l.w;
                            float r = Mathf.Sqrt(nx * nx + ny * ny);
                            a = Mathf.Max(a, Mathf.Pow(Mathf.Clamp01(1f - r), 1.5f));
                        }
                        // 云底压平：下缘更快消隐
                        if (dy < -0.12f)
                            a *= Mathf.Clamp01(1f - (-0.12f - dy) / 0.2f);
                        return Mathf.Clamp01(a);
                    });
                }
                return _cloudPuff;
            }
        }

        static Texture2D _ring;

        /// <summary>柔边圆环（点击涟漪用）</summary>
        public static Texture2D Ring
        {
            get
            {
                if (_ring == null)
                    _ring = Generate("VN_Ring", 128, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float band = Mathf.Abs(r - 0.36f) / 0.1f;
                        return Mathf.Pow(Mathf.Clamp01(1f - band), 2f);
                    });
                return _ring;
            }
        }

        // ------------------------------------------------------------------
        // 圆角面板 / 边框（对话框用，9-slice Sprite）
        // ------------------------------------------------------------------

        static Sprite _roundedRectSprite;
        static Sprite _roundedFrameSprite;

        /// <summary>圆角矩形 SDF：d &lt; 0 在内部</summary>
        static float RoundedBoxDist(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - (halfW - radius);
            float qy = Mathf.Abs(py) - (halfH - radius);
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>实心圆角面板（64px，圆角 16px，9-slice 边距 22px）</summary>
        public static Sprite RoundedRectSprite
        {
            get
            {
                if (_roundedRectSprite == null)
                {
                    const int size = 64;
                    var tex = Generate("VN_RoundedRect", size, size, (dx, dy) =>
                    {
                        float d = RoundedBoxDist(dx * size, dy * size, size * 0.5f - 1f, size * 0.5f - 1f, 16f);
                        return Mathf.Clamp01(0.5f - d); // 1px 抗锯齿
                    });
                    _roundedRectSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                        new Vector2(0.5f, 0.5f), 100f, 0,
                        SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
                    _roundedRectSprite.name = "VN_RoundedRectSprite";
                    _roundedRectSprite.hideFlags = HideFlags.DontSave;
                }
                return _roundedRectSprite;
            }
        }

        /// <summary>圆角描边框（3px 线宽，对话框边缘流光的载体）</summary>
        public static Sprite RoundedFrameSprite
        {
            get
            {
                if (_roundedFrameSprite == null)
                {
                    const int size = 64;
                    const float thickness = 3f;
                    var tex = Generate("VN_RoundedFrame", size, size, (dx, dy) =>
                    {
                        float d = RoundedBoxDist(dx * size, dy * size, size * 0.5f - 1f, size * 0.5f - 1f, 16f);
                        float outer = Mathf.Clamp01(0.5f - d);
                        float inner = Mathf.Clamp01(0.5f - (d + thickness));
                        return outer - inner; // 只留边缘细环
                    });
                    _roundedFrameSprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                        new Vector2(0.5f, 0.5f), 100f, 0,
                        SpriteMeshType.FullRect, new Vector4(22, 22, 22, 22));
                    _roundedFrameSprite.name = "VN_RoundedFrameSprite";
                    _roundedFrameSprite.hideFlags = HideFlags.DontSave;
                }
                return _roundedFrameSprite;
            }
        }

        /// <summary>径向光晕的 Sprite 包装（供 Image 使用）</summary>
        public static Sprite RadialGlowSprite
        {
            get
            {
                if (_radialGlowSprite == null)
                {
                    var tex = RadialGlow;
                    _radialGlowSprite = Sprite.Create(
                        tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _radialGlowSprite.name = "VN_RadialGlowSprite";
                    _radialGlowSprite.hideFlags = HideFlags.DontSave;
                }
                return _radialGlowSprite;
            }
        }

        static Sprite _sparkleSprite;

        /// <summary>四芒星光的 Sprite 包装（供 Image 使用，结算弹窗星光爆发等）</summary>
        public static Sprite SparkleSprite
        {
            get
            {
                if (_sparkleSprite == null)
                {
                    var tex = Sparkle;
                    _sparkleSprite = Sprite.Create(
                        tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _sparkleSprite.name = "VN_SparkleSprite";
                    _sparkleSprite.hideFlags = HideFlags.DontSave;
                }
                return _sparkleSprite;
            }
        }

        static Texture2D _loadingRing;
        static Sprite _loadingRingSprite;

        /// <summary>
        /// loading 图标用的细环（256px，锐利边缘）。
        /// 与上面软绵绵的 <see cref="Ring"/> 分开：那个是给粒子/光晕用的柔光环，
        /// 缩到 56px 当 spinner 会糊成一团灰。这里要 1px 抗锯齿的实边。
        /// </summary>
        public static Texture2D LoadingRing
        {
            get
            {
                if (_loadingRing == null)
                {
                    const int size = 256;
                    _loadingRing = Generate("VN_LoadingRing", size, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        // 半径 0.40、半厚 0.038 的环，边缘按像素宽度做抗锯齿
                        float aa = 1.2f / size;
                        return Mathf.Clamp01((0.038f - Mathf.Abs(r - 0.40f)) / aa);
                    });
                }
                return _loadingRing;
            }
        }

        /// <summary>loading 细环的 Sprite 包装（Image 用；配 Filled + Radial360 就是转圈弧）</summary>
        public static Sprite LoadingRingSprite
        {
            get
            {
                if (_loadingRingSprite == null)
                {
                    var tex = LoadingRing;
                    _loadingRingSprite = Sprite.Create(
                        tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _loadingRingSprite.name = "VN_LoadingRingSprite";
                    _loadingRingSprite.hideFlags = HideFlags.DontSave;
                }
                return _loadingRingSprite;
            }
        }

        // ------------------------------------------------------------------
        // 漫符（汗滴 / 井字怒气 / 感叹号 …）—— VNCharacterMarks 用
        // 与上面的粒子贴图不同：漫符自带颜色与描边，所以走 RGBA 生成而不是纯 alpha。
        // ------------------------------------------------------------------

        static readonly Dictionary<VNMarkKind, Sprite> _markSprites =
            new Dictionary<VNMarkKind, Sprite>();

        /// <summary>取漫符贴图（懒加载并缓存；角色资产里配了素材图时不会走到这里）</summary>
        public static Sprite MarkSprite(VNMarkKind kind)
        {
            if (_markSprites.TryGetValue(kind, out var cached) && cached != null)
                return cached;
            var sprite = BuildMark(kind);
            _markSprites[kind] = sprite;
            return sprite;
        }

        const int MarkSize = 128;

        static Sprite BuildMark(VNMarkKind kind)
        {
            switch (kind)
            {
                case VNMarkKind.Sweat:
                    return HardMark("Sweat", new Color(0.68f, 0.87f, 1f),
                        new Color(0.10f, 0.30f, 0.55f), 0.030f, MarkSweat);
                case VNMarkKind.Anger:
                    return HardMark("Anger", new Color(0.90f, 0.16f, 0.18f),
                        new Color(0.34f, 0.02f, 0.05f), 0.028f, MarkAnger);
                case VNMarkKind.Exclaim:
                    return HardMark("Exclaim", new Color(1f, 0.86f, 0.20f),
                        new Color(0.26f, 0.13f, 0f), 0.030f, MarkExclaim);
                case VNMarkKind.Question:
                    return HardMark("Question", new Color(1f, 0.86f, 0.20f),
                        new Color(0.26f, 0.13f, 0f), 0.030f, MarkQuestion);
                case VNMarkKind.Heart:
                    return HardMark("Heart", new Color(1f, 0.36f, 0.52f),
                        new Color(0.55f, 0.05f, 0.18f), 0.028f, MarkHeart);
                case VNMarkKind.Note:
                    return HardMark("Note", new Color(0.58f, 0.89f, 1f),
                        new Color(0.08f, 0.28f, 0.50f), 0.026f, MarkNote);
                case VNMarkKind.Bulb:
                    return HardMark("Bulb", new Color(1f, 0.88f, 0.35f),
                        new Color(0.42f, 0.26f, 0.02f), 0.028f, MarkBulb);
                case VNMarkKind.Ellipsis:
                    return HardMark("Ellipsis", new Color(0.94f, 0.94f, 0.96f),
                        new Color(0.18f, 0.18f, 0.24f), 0.028f, MarkEllipsis);
                case VNMarkKind.Dizzy:
                    return HardMark("Dizzy", new Color(1f, 0.92f, 0.42f),
                        new Color(0.45f, 0.30f, 0f), 0.026f, MarkDizzy);
                case VNMarkKind.Blush:
                    return SoftMark("Blush", new Color(1f, 0.42f, 0.50f, 0.82f), MarkBlushAlpha);
                case VNMarkKind.Steam:
                    return SoftMark("Steam", new Color(0.88f, 0.90f, 0.95f, 0.72f), MarkSteamAlpha);
                default:
                    return HardMark("Dot", Color.white, Color.black, 0.02f,
                        (dx, dy) => InCircle(dx, dy, 0f, 0f, 0.3f));
            }
        }

        /// <summary>
        /// 硬边漫符：inside 布尔判定 + 4×4 超采样抗锯齿，再用形态学膨胀套一圈描边。
        /// 走膨胀而不是 SDF，是为了让"多段拼起来"的形状（? ! ♪）也能拿到连续的外轮廓。
        /// </summary>
        static Sprite HardMark(string name, Color fill, Color outline, float outlineWidth,
            System.Func<float, float, bool> inside)
        {
            const int size = MarkSize;
            const int ss = 4;
            const float inv = 1f / (ss * ss);

            var cover = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        float dy = (y + (sy + 0.5f) / ss) / size - 0.5f;
                        for (int sx = 0; sx < ss; sx++)
                        {
                            float dx = (x + (sx + 0.5f) / ss) / size - 0.5f;
                            if (inside(dx, dy)) hit++;
                        }
                    }
                    cover[y * size + x] = hit * inv;
                }

            var outer = Dilate(cover, size, Mathf.Max(1, Mathf.RoundToInt(outlineWidth * size)));

            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                float fa = cover[i] * fill.a;
                float oa = outer[i] * outline.a * (1f - fa); // 描边只出现在填充没盖住的部分
                float a = fa + oa;
                if (a <= 0.001f) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                float r = (fill.r * fa + outline.r * oa) / a;
                float g = (fill.g * fa + outline.g * oa) / a;
                float b = (fill.b * fa + outline.b * oa) / a;
                pixels[i] = new Color(r, g, b, a);
            }

            return MakeSprite($"VN_Mark_{name}", size, pixels);
        }

        /// <summary>柔边漫符（红晕 / 蒸汽）：直接给 alpha 曲线，不描边</summary>
        static Sprite SoftMark(string name, Color color,
            System.Func<float, float, float> alphaFunc)
        {
            const int size = MarkSize;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f) / size - 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float a = Mathf.Clamp01(alphaFunc(dx, dy)) * color.a;
                    pixels[y * size + x] = new Color(color.r, color.g, color.b, a);
                }
            }
            return MakeSprite($"VN_Mark_{name}", size, pixels);
        }

        /// <summary>圆形核膨胀：半径内取最大覆盖率，得到带抗锯齿的外扩轮廓</summary>
        static float[] Dilate(float[] source, int size, int radius)
        {
            // 先算出圆形核的偏移表，避免逐像素做距离判断
            var offsets = new List<Vector2Int>();
            for (int oy = -radius; oy <= radius; oy++)
                for (int ox = -radius; ox <= radius; ox++)
                    if (ox * ox + oy * oy <= radius * radius)
                        offsets.Add(new Vector2Int(ox, oy));

            var result = new float[source.Length];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float max = 0f;
                    foreach (var o in offsets)
                    {
                        int nx = x + o.x, ny = y + o.y;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                        float v = source[ny * size + nx];
                        if (v > max) max = v;
                        if (max >= 1f) break;
                    }
                    result[y * size + x] = max;
                }
            return result;
        }

        static Sprite MakeSprite(string name, int size, Color32[] pixels)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        // ---- 形状基元（dx, dy 以贴图中心为原点，范围 [-0.5, 0.5]）----

        static bool InCircle(float dx, float dy, float cx, float cy, float r)
        {
            float ax = dx - cx, ay = dy - cy;
            return ax * ax + ay * ay <= r * r;
        }

        /// <summary>胶囊（线段膨胀）；半径沿线段从 r0 渐变到 r1，用来做带粗细变化的笔画</summary>
        static bool InCapsule(float dx, float dy, float ax, float ay, float bx, float by,
            float r0, float r1)
        {
            float ex = bx - ax, ey = by - ay;
            float len2 = ex * ex + ey * ey;
            float t = len2 <= 1e-6f ? 0f
                : Mathf.Clamp01(((dx - ax) * ex + (dy - ay) * ey) / len2);
            float px = dx - (ax + ex * t), py = dy - (ay + ey * t);
            float r = Mathf.Lerp(r0, r1, t);
            return px * px + py * py <= r * r;
        }

        /// <summary>圆弧段（角度制，逆时针从 fromDeg 扫到 toDeg，允许跨 0）</summary>
        static bool InArc(float dx, float dy, float cx, float cy, float radius,
            float thickness, float fromDeg, float toDeg)
        {
            float ax = dx - cx, ay = dy - cy;
            float d = Mathf.Sqrt(ax * ax + ay * ay);
            if (Mathf.Abs(d - radius) > thickness * 0.5f) return false;
            float ang = Mathf.Atan2(ay, ax) * Mathf.Rad2Deg;
            float span = Mathf.Repeat(toDeg - fromDeg, 360f);
            return Mathf.Repeat(ang - fromDeg, 360f) <= span;
        }

        // ---- 各漫符的形状 ----

        /// <summary>汗滴：下方圆头 + 向上收成尖（接缝处斜率为 0，所以不会出现折角）</summary>
        static bool MarkSweat(float dx, float dy)
        {
            const float cy = -0.10f, r = 0.19f, tip = 0.34f;
            if (dy <= cy) return InCircle(dx, dy, 0f, cy, r);
            float t = (dy - cy) / (tip - cy);
            if (t > 1f) return false;
            return Mathf.Abs(dx) <= r * (1f - Mathf.Pow(t, 1.7f));
        }

        /// <summary>井字怒气：极坐标四瓣（尖角在上下左右，对角收窄）</summary>
        static bool MarkAnger(float dx, float dy)
        {
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            if (d > 0.42f) return false;
            float lobe = Mathf.Abs(Mathf.Cos(2f * Mathf.Atan2(dy, dx)));
            return d <= 0.42f * (0.38f + 0.62f * Mathf.Sqrt(lobe));
        }

        /// <summary>感叹号：上粗下细的竖笔 + 圆点</summary>
        static bool MarkExclaim(float dx, float dy)
            => InCapsule(dx, dy, 0f, 0.29f, 0f, -0.02f, 0.085f, 0.055f) ||
               InCircle(dx, dy, 0f, -0.22f, 0.085f);

        /// <summary>问号：上方开口向下的钩 + 连接短笔 + 圆点</summary>
        static bool MarkQuestion(float dx, float dy)
            => InArc(dx, dy, 0f, 0.15f, 0.155f, 0.10f, -70f, 180f) ||
               InCapsule(dx, dy, 0.053f, 0.005f, 0f, -0.10f, 0.05f, 0.05f) ||
               InCircle(dx, dy, 0f, -0.24f, 0.075f);

        /// <summary>爱心：经典隐函数 (x²+y²-1)³ - x²y³ ≤ 0</summary>
        static bool MarkHeart(float dx, float dy)
        {
            float x = dx * 2.6f;
            float y = dy * 2.6f + 0.08f;
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f;
        }

        /// <summary>音符 ♪：倾斜的符头 + 符干 + 符尾</summary>
        static bool MarkNote(float dx, float dy)
        {
            // 符头：绕中心旋转 20° 的椭圆
            float hx = dx + 0.13f, hy = dy + 0.21f;
            const float cos = 0.9397f, sin = 0.3420f;
            float u = hx * cos + hy * sin;
            float v = -hx * sin + hy * cos;
            if (u * u / (0.135f * 0.135f) + v * v / (0.098f * 0.098f) <= 1f) return true;

            return InCapsule(dx, dy, 0f, -0.19f, 0f, 0.30f, 0.033f, 0.033f) ||
                   InCapsule(dx, dy, 0f, 0.30f, 0.19f, 0.11f, 0.048f, 0.038f);
        }

        static readonly float[] BulbRayAngles = { 55f, 90f, 125f };

        /// <summary>灵光一闪：灯泡本体 + 灯颈 + 灯座 + 三道放射光芒</summary>
        static bool MarkBulb(float dx, float dy)
        {
            if (InCircle(dx, dy, 0f, 0.10f, 0.19f)) return true;
            if (Mathf.Abs(dx) <= 0.085f && dy >= -0.16f && dy <= -0.05f) return true;
            if (Mathf.Abs(dx) <= 0.105f && dy >= -0.27f && dy <= -0.16f) return true;

            for (int i = 0; i < BulbRayAngles.Length; i++)
            {
                float a = BulbRayAngles[i] * Mathf.Deg2Rad;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                if (InCapsule(dx, dy, cs * 0.23f, 0.10f + sn * 0.23f,
                        cs * 0.33f, 0.10f + sn * 0.33f, 0.026f, 0.026f))
                    return true;
            }
            return false;
        }

        /// <summary>省略号：横排三点（无语 / 沉默）</summary>
        static bool MarkEllipsis(float dx, float dy)
            => InCircle(dx, dy, -0.26f, 0f, 0.095f) ||
               InCircle(dx, dy, 0f, 0f, 0.095f) ||
               InCircle(dx, dy, 0.26f, 0f, 0.095f);

        /// <summary>眩晕：三颗大小不一的四芒星，中间那颗更高更大</summary>
        static bool MarkDizzy(float dx, float dy)
            => FourPointStar(dx + 0.27f, dy + 0.06f, 0.19f) ||
               FourPointStar(dx, dy - 0.12f, 0.23f) ||
               FourPointStar(dx - 0.27f, dy + 0.06f, 0.19f);

        static bool FourPointStar(float x, float y, float size)
        {
            float d = Mathf.Sqrt(x * x + y * y);
            if (d > size) return false;
            float ang = Mathf.Atan2(y, x);
            float lobe = Mathf.Max(Mathf.Abs(Mathf.Cos(ang)), Mathf.Abs(Mathf.Sin(ang)));
            return d <= size * (0.16f + 0.84f * Mathf.Pow(lobe, 6f));
        }

        /// <summary>红晕：左右两块柔边椭圆（贴脸颊用，所以默认锚点要另配）</summary>
        static float MarkBlushAlpha(float dx, float dy)
            => Mathf.Max(BlushPatch(dx + 0.26f, dy), BlushPatch(dx - 0.26f, dy));

        static float BlushPatch(float x, float y)
        {
            float nx = x / 0.21f, ny = y / 0.115f;
            return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny)), 1.5f);
        }

        /// <summary>怒气蒸汽：两道向外倾斜、越往上越淡的柔边烟柱</summary>
        static float MarkSteamAlpha(float dx, float dy)
            => Mathf.Max(
                SteamPlume(dx, dy, -0.10f, -0.28f, -0.22f, 0.24f, 0.075f),
                SteamPlume(dx, dy, 0.13f, -0.25f, 0.24f, 0.20f, 0.062f));

        static float SteamPlume(float dx, float dy, float ax, float ay,
            float bx, float by, float r)
        {
            float ex = bx - ax, ey = by - ay;
            float len2 = ex * ex + ey * ey;
            float t = len2 <= 1e-6f ? 0f
                : Mathf.Clamp01(((dx - ax) * ex + (dy - ay) * ey) / len2);
            float px = dx - (ax + ex * t), py = dy - (ay + ey * t);
            float d = Mathf.Sqrt(px * px + py * py);
            float body = Mathf.Pow(Mathf.Clamp01(1f - d / r), 0.8f);
            return body * Mathf.Clamp01(1.15f - t); // 顶端渐隐 = 蒸汽消散
        }

        // ------------------------------------------------------------------
        // 液体喷溅（VNLiquidSplash / VNWetScreen）
        // ------------------------------------------------------------------

        static Texture2D _liquidBlob;
        static Texture2D _liquidSplinter;
        static Texture2D _waterDrop;
        static Texture2D _dropSpec;
        static Texture2D _liquidStreak;

        /// <summary>
        /// 空中飞行的水珠（拉伸公告板粒子用）。
        ///
        /// 【为什么是近圆形而不是长条】
        /// StretchedBillboard 会把贴图沿速度方向再拉一次（`lengthScale`）。
        /// 贴图本身画成长条的话，两次拉伸叠起来就是一根面条——第一版又粗又长正是栽在这。
        /// 现有的雨干脆用纯圆的 SoftCircle + lengthScale 5 来拉出雨丝，这里同理：
        /// 只画一个**略带尖尾的近圆**，长度交给 lengthScale 决定。
        /// 尖尾朝 +x，拉伸后自然成为"头重尾轻"而不是对称胶囊。
        /// </summary>
        public static Texture2D LiquidBlob
        {
            get
            {
                if (_liquidBlob == null)
                    _liquidBlob = Generate("VN_LiquidBlob", 64, 64, (dx, dy) =>
                    {
                        // 只占中间一小块：形状紧凑，长度全部交给 lengthScale
                        const float halfWidth = 0.30f;
                        float t = Mathf.InverseLerp(-halfWidth, halfWidth, dx); // 0 = 头, 1 = 尾
                        if (t <= 0f || t >= 1f) return 0f;

                        // 头部近圆（半高 ≈ 半宽），尾部收到三分之一
                        float halfHeight = Mathf.Lerp(0.26f, 0.085f, Mathf.Pow(t, 1.35f));
                        float ny = Mathf.Abs(dy) / halfHeight;
                        if (ny >= 1f) return 0f;

                        float body = Mathf.Pow(1f - ny, 0.7f);
                        float capL = Mathf.Clamp01((dx + halfWidth) / 0.09f);
                        float capR = Mathf.Clamp01((halfWidth - dx) / 0.05f);
                        return body * capL * capR;
                    });
                return _liquidBlob;
            }
        }

        /// <summary>爆溅碎珠：单纯的小圆点，比 SoftCircle 边缘更实（是水不是光）</summary>
        public static Texture2D LiquidSplinter
        {
            get
            {
                if (_liquidSplinter == null)
                    _liquidSplinter = Generate("VN_LiquidSplinter", 32, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy) / 0.44f;
                        if (r >= 1f) return 0f;
                        return Mathf.Clamp01((1f - r) / 0.22f); // 实心 + 两像素柔边
                    });
                return _liquidSplinter;
            }
        }

        /// <summary>
        /// 溅在镜头上的水渍本体（C1 假折射）。RGB 存明暗、A 存形状与厚度：
        ///   · 中心压暗到 0.5      —— 透过水看到的画面本来就更暗
        ///   · 内侧一圈亮环        —— 液面隆起处的反光
        ///   · 最外圈急剧变暗到 0.16 —— 菲涅尔暗边，"这是玻璃不是贴纸"全靠它
        ///   · 中心 alpha 低、边缘 alpha 高 —— 中间透得过去，边缘厚实
        /// 不做真折射（不采样背景），因此不需要 GrabPass / 额外相机，
        /// 代价是看不到水滴里倒立的背景——远看几乎分辨不出。
        /// </summary>
        public static Texture2D WaterDrop
        {
            get
            {
                if (_waterDrop == null)
                    _waterDrop = GenerateRgba("VN_WaterDrop", 128, (dx, dy) =>
                    {
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        const float R = 0.47f;
                        float t = r / R;                      // 0 中心 → 1 边缘
                        if (t >= 1f) return new Color(0f, 0f, 0f, 0f);

                        // 明暗剖面：中心暗 → 内亮环 → 外暗边
                        float shade = 0.50f;
                        shade += 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.85f, t));
                        shade *= 1f - 0.80f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 1f, t));

                        // 厚度：中心薄（透）、边缘厚（实），最外沿再柔化两像素消锯齿
                        float a = Mathf.Lerp(0.62f, 1f, Mathf.Pow(t, 1.6f));
                        a *= Mathf.Clamp01((1f - t) / 0.04f);

                        return new Color(shade, shade, shade, a);
                    });
                return _waterDrop;
            }
        }

        static Texture2D _waterSpeck;

        /// <summary>
        /// 溅在镜头上的**小水点**（竖向细长，长轴 = +Y）。屏幕水渍的主力形状。
        ///
        /// 【为什么不用 WaterDrop】
        /// WaterDrop 那张假折射图有完整的明暗剖面（中心压暗 + 内亮环 + 菲涅尔暗边），
        /// 那是给几十像素的大水滴看的。缩到 5~8 像素时整套剖面糊成一圈灰环，
        /// 看起来就是个肥皂泡——那正是第二版屏幕水珠"假"的根源。
        /// 这么小的东西只需要两条信息：**是一条细的**，**边上比中间亮**。
        ///
        /// 形状：底端（-Y）圆钝是"头"，顶端（+Y）收尖是"尾"。
        /// 静止下滑时长轴竖直、圆头朝下；刚溅上时整体旋转到撞击方向，圆头朝外。
        /// </summary>
        public static Texture2D WaterSpeck
        {
            get
            {
                if (_waterSpeck == null)
                    _waterSpeck = GenerateRgba("VN_WaterSpeck", 32, 96, (dx, dy) =>
                    {
                        float t = dy + 0.5f;                     // 0 = 底（头）, 1 = 顶（尾）
                        float halfWidth = Mathf.Lerp(0.42f, 0.09f, Mathf.Pow(t, 1.25f));
                        float nx = Mathf.Abs(dx) / halfWidth;
                        if (nx >= 1f) return new Color(0f, 0f, 0f, 0f);

                        // 比空中水珠更实：镜头上的水是贴着玻璃的一小摊，不是半透明气泡
                        float body = Mathf.Pow(1f - nx, 0.5f);
                        float capBottom = Mathf.Clamp01((dy + 0.48f) / 0.055f);
                        float capTop = Mathf.Clamp01((0.49f - dy) / 0.035f);

                        // 只保留最基本的一条：边缘比中间亮。几像素宽的东西放不下更多信息
                        float shade = Mathf.Lerp(0.60f, 1f, Mathf.Pow(nx, 2f));
                        return new Color(shade, shade, shade, body * capBottom * capTop);
                    });
                return _waterSpeck;
            }
        }

        /// <summary>
        /// 水渍高光层（叠在 WaterDrop 上，走 VN/Additive + HDR 才吃得到 Bloom）。
        /// 一个偏左上的小亮斑 + 一道细弧——真实水滴反射的是环境里最亮的那一小块，
        /// 位置固定在同一侧才像"同一个光源"，随机化反而会散掉。
        /// </summary>
        public static Texture2D DropSpec
        {
            get
            {
                if (_dropSpec == null)
                    _dropSpec = Generate("VN_DropSpec", 64, (dx, dy) =>
                    {
                        // 主高光：左上小圆斑
                        float hx = (dx + 0.17f) / 0.115f;
                        float hy = (dy - 0.16f) / 0.085f;
                        float spot = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy)), 1.4f);

                        // 副高光：右下贴着边缘的一道细弧（液面反射的地平线）
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float ring = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(r - 0.375f) / 0.045f), 2f);
                        float ang = Mathf.Atan2(dy, dx);
                        float arc = Mathf.Clamp01(Mathf.Cos(ang + 1.15f)); // 只保留右下一段
                        ring *= Mathf.Pow(arc, 3.5f) * 0.55f;

                        return Mathf.Clamp01(spot + ring);
                    });
                return _dropSpec;
            }
        }

        /// <summary>
        /// 水渍下滑留下的水痕（竖向，pivot 放底部后向上拉伸即可）。
        /// 顶端（最早流过的地方）最淡：水痕是边流边被表面张力收干的。
        /// </summary>
        public static Texture2D LiquidStreak
        {
            get
            {
                if (_liquidStreak == null)
                    _liquidStreak = GenerateRgba("VN_LiquidStreak", 32, 256, (dx, dy) =>
                    {
                        float across = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dx) / 0.34f), 0.9f);
                        if (across <= 0f) return new Color(0f, 0f, 0f, 0f);
                        float t = dy + 0.5f;                        // 0 底（新）→ 1 顶（旧）
                        float along = Mathf.Pow(Mathf.Clamp01(1f - t), 0.85f);

                        // 和水滴同一套明暗语言：中间偏暗、两侧亮边
                        float edge = Mathf.Pow(Mathf.Clamp01(Mathf.Abs(dx) / 0.34f), 2.2f);
                        float shade = Mathf.Lerp(0.55f, 1.05f, edge);
                        return new Color(shade, shade, shade, across * along * 0.85f);
                    });
                return _liquidStreak;
            }
        }

        /// <summary>
        /// 通用生成器：alphaFunc 以中心为原点（dx, dy ∈ [-0.5, 0.5]）返回 alpha。
        /// RGB 恒为白色，颜色交给顶点色 / 材质 Tint 控制。
        /// </summary>
        static Texture2D Generate(string name, int size, System.Func<float, float, float> alphaFunc)
            => Generate(name, size, size, alphaFunc);

        static Texture2D Generate(string name, int width, int height, System.Func<float, float, float> alphaFunc)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float dy = (y + 0.5f) / height - 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float dx = (x + 0.5f) / width - 0.5f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaFunc(dx, dy)) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// 彩色生成器：colorFunc 返回带 RGB 的颜色（RGB 会与顶点色相乘）。
        /// 用于明暗信息必须写进贴图的形状——液体的菲涅尔暗边、内亮环靠顶点色做不出来，
        /// 顶点色只能整体染色，没法在一张图里既有暗边又有亮环。
        /// RGB 允许写到 1 以上的意图请改用 Additive 图层，Color32 存不下 HDR。
        /// </summary>
        static Texture2D GenerateRgba(string name, int size, System.Func<float, float, Color> colorFunc)
            => GenerateRgba(name, size, size, colorFunc);

        static Texture2D GenerateRgba(string name, int width, int height,
            System.Func<float, float, Color> colorFunc)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float dy = (y + 0.5f) / height - 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float dx = (x + 0.5f) / width - 0.5f;
                    Color c = colorFunc(dx, dy);
                    pixels[y * width + x] = new Color32(
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                        (byte)Mathf.RoundToInt(Mathf.Clamp01(c.a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
