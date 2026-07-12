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
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f) / size - 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaFunc(dx, dy)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
