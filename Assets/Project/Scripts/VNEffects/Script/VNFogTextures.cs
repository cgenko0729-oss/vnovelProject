using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 擦雾玩法的程序化贴图：四种擦拭道具的光标图。
    /// 独立成类的理由同 VNPhotoTextures / VNFoliageTextures——玩法专用的形状不该
    /// 挤进通用的 VNProceduralTextures，那里放的是全项目共用的粒子与光晕。
    ///
    /// 全部懒加载并缓存；`hideFlags = DontSave` 保证域重载后自动销毁重建。
    /// 资产里给了 cursor.icon 就用玩家自己的图，这些只是「没有美术也立刻能玩」的兜底。
    ///
    /// 形状一律用 SDF（有符号距离场）画：比逐像素条件判断好写，且天然带 1px 抗锯齿。
    /// RGB 存明暗（顶点色只能整体染色，做不出「胶条左暗右亮」这种立体感），A 存形状。
    /// </summary>
    public static class VNFogTextures
    {
        static Sprite _wiper, _palm, _cloth, _finger;

        public static Sprite For(VNWiperKind kind)
        {
            switch (kind)
            {
                case VNWiperKind.Palm: return Palm;
                case VNWiperKind.Cloth: return Cloth;
                case VNWiperKind.Finger: return Finger;
                default: return Wiper;
            }
        }

        /// <summary>玻璃雨刷：橡胶条 + 金属支架</summary>
        public static Sprite Wiper
        {
            get
            {
                // 宽高比 1:2 而不是 1:3——更细长确实更像真雨刷，但当光标看不清，
                // 屏幕上只剩一根竖线。这里以「一眼认得出」优先于比例写实
                if (_wiper == null)
                    _wiper = Make("VNFog_Wiper", 132, 264, (px, py) =>
                    {
                        // 橡胶条（主体）
                        float blade = RoundBox(px, py, 0f, -44f, 34f, 84f, 12f);
                        // 金属支架
                        float arm = RoundBox(px, py, 0f, 58f, 9f, 60f, 5f);
                        // 顶部接头
                        float joint = RoundBox(px, py, 0f, 106f, 20f, 16f, 6f);

                        float d = Mathf.Min(blade, Mathf.Min(arm, joint));
                        float a = Alpha(d);
                        if (a <= 0f) return Color.clear;

                        // 明暗：左侧暗、中间偏右有一道高光（橡胶的反光）
                        float t = Mathf.InverseLerp(-34f, 34f, px);
                        float shade = Mathf.Lerp(0.42f, 0.72f, t);
                        float spec = Mathf.Exp(-Mathf.Pow((px - 9f) / 8f, 2f)) * 0.45f;

                        bool metal = blade > arm || blade > joint;
                        Color c = metal
                            ? new Color(0.72f, 0.75f, 0.80f)
                            : new Color(shade * 0.55f, shade * 0.57f, shade * 0.62f);
                        c.r += spec; c.g += spec; c.b += spec;
                        c.a = a;
                        return c;
                    });
                return _wiper;
            }
        }

        /// <summary>手掌：掌心椭圆 + 四指 + 拇指</summary>
        public static Sprite Palm
        {
            get
            {
                if (_palm == null)
                    _palm = Make("VNFog_Palm", 208, 232, (px, py) =>
                    {
                        float d = Ellipse(px, py, 0f, -34f, 70f, 66f);
                        // 四根手指
                        for (int i = 0; i < 4; i++)
                        {
                            float fx = -54f + i * 36f;
                            float top = 62f - Mathf.Abs(i - 1.4f) * 9f;   // 中指最长
                            d = Mathf.Min(d, RoundBox(px, py, fx, top * 0.5f + 10f,
                                                      15f, top * 0.5f + 26f, 14f));
                        }
                        // 拇指：斜着挂在左下
                        float rx = (px + 62f) * 0.87f + (py + 20f) * 0.5f;
                        float ry = -(px + 62f) * 0.5f + (py + 20f) * 0.87f;
                        d = Mathf.Min(d, RoundBox(rx, ry, 0f, 0f, 16f, 34f, 15f));

                        float a = Alpha(d);
                        if (a <= 0f) return Color.clear;

                        // 边缘略暗、中心略亮，才不像一块死板的色块
                        float lift = Mathf.Clamp01(-d / 26f);
                        Color c = Color.Lerp(new Color(0.86f, 0.66f, 0.58f),
                                             new Color(0.98f, 0.82f, 0.74f), lift);
                        c.a = a;
                        return c;
                    });
                return _palm;
            }
        }

        /// <summary>抹布：圆角方巾 + 斜向褶皱</summary>
        public static Sprite Cloth
        {
            get
            {
                if (_cloth == null)
                    _cloth = Make("VNFog_Cloth", 240, 200, (px, py) =>
                    {
                        // 轻微起伏的边缘：正方形的布看起来像贴纸，波动一下才像布
                        float wobble = Mathf.Sin(px * 0.06f) * 4f + Mathf.Cos(py * 0.07f) * 4f;
                        float d = RoundBox(px, py, 0f, 0f, 102f + wobble, 82f + wobble, 28f);

                        float a = Alpha(d);
                        if (a <= 0f) return Color.clear;

                        // 褶皱：两组不同频率的斜向暗纹
                        float fold = Mathf.Sin((px * 0.7f + py * 1.1f) * 0.06f) * 0.5f + 0.5f;
                        fold *= Mathf.Sin((px * 1.3f - py * 0.6f) * 0.035f) * 0.5f + 0.5f;
                        float shade = Mathf.Lerp(0.72f, 1f, fold);

                        Color c = new Color(0.93f * shade, 0.90f * shade, 0.82f * shade);
                        c.a = a;
                        return c;
                    });
                return _cloth;
            }
        }

        /// <summary>手指：一根圆角指条</summary>
        public static Sprite Finger
        {
            get
            {
                if (_finger == null)
                    _finger = Make("VNFog_Finger", 80, 216, (px, py) =>
                    {
                        float d = RoundBox(px, py, 0f, -10f, 25f, 88f, 24f);
                        float a = Alpha(d);
                        if (a <= 0f) return Color.clear;

                        float t = Mathf.InverseLerp(-25f, 25f, px);
                        float shade = Mathf.Lerp(0.78f, 1f, t);
                        // 指甲：顶端一小片更亮的圆角
                        float nail = RoundBox(px, py, 0f, 56f, 15f, 20f, 11f);
                        if (nail < 0f) shade *= 1.12f;

                        Color c = new Color(0.94f * shade, 0.77f * shade, 0.69f * shade);
                        c.a = a;
                        return c;
                    });
                return _finger;
            }
        }

        // ==================================================================
        // SDF 辅助
        // ==================================================================

        /// <summary>圆角矩形的有符号距离（&lt;0 在内部）</summary>
        static float RoundBox(float px, float py, float cx, float cy,
            float halfX, float halfY, float radius)
        {
            radius = Mathf.Min(radius, Mathf.Min(halfX, halfY));
            float dx = Mathf.Abs(px - cx) - (halfX - radius);
            float dy = Mathf.Abs(py - cy) - (halfY - radius);
            float ox = Mathf.Max(dx, 0f), oy = Mathf.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
        }

        /// <summary>椭圆的近似有符号距离（&lt;0 在内部）</summary>
        static float Ellipse(float px, float py, float cx, float cy, float rx, float ry)
        {
            float nx = (px - cx) / rx;
            float ny = (py - cy) / ry;
            float len = Mathf.Sqrt(nx * nx + ny * ny);
            return (len - 1f) * Mathf.Min(rx, ry);
        }

        /// <summary>距离场 → alpha，自带 1.5px 抗锯齿带</summary>
        static float Alpha(float d) => Mathf.Clamp01(0.75f - d / 1.5f);

        // ==================================================================

        /// <summary>colorFunc 收到的是**像素坐标**（中心为原点），形状比例才不会被贴图长宽比拉歪</summary>
        static Sprite Make(string name, int width, int height,
            System.Func<float, float, Color> colorFunc)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                float py = y + 0.5f - height * 0.5f;
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - width * 0.5f;
                    var c = colorFunc(px, py);
                    pixels[y * width + x] = new Color32(
                        (byte)(Mathf.Clamp01(c.r) * 255f),
                        (byte)(Mathf.Clamp01(c.g) * 255f),
                        (byte)(Mathf.Clamp01(c.b) * 255f),
                        (byte)(Mathf.Clamp01(c.a) * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, width, height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
