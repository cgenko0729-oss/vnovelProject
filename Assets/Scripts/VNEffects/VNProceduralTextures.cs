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
    }
}
