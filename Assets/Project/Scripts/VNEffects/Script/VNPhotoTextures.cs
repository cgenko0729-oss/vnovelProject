using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 大头贴用的程序化贴图：边框底图 / 人物开窗遮罩 / 开窗描边 / 贴纸图案 / 相纸。
    /// 参考项目那套边框全是美术图（img_photobooth_1..n），本项目的铁律是零美术依赖，
    /// 所以默认样式都在这里画出来；资产里配了 Sprite 就用资产的，两条路互不干扰。
    ///
    /// 全部懒加载 + 缓存，整个游戏生命周期每种只生成一次。
    /// 贴图一律画成白色 + alpha，颜色交给 Image.color 乘上去（同一张图能出多种配色）。
    /// </summary>
    public static class VNPhotoTextures
    {
        // 边框底图按 4:3 生成，取景框拉伸使用
        const int FrameW = 512;
        const int FrameH = 384;
        const int MaskSize = 256;
        const int StickerSize = 128;

        static readonly Dictionary<string, Sprite> _frames = new Dictionary<string, Sprite>();
        static readonly Dictionary<VNPhotoMaskShape, Sprite> _masks =
            new Dictionary<VNPhotoMaskShape, Sprite>();
        static readonly Dictionary<VNPhotoMaskShape, Sprite> _maskRings =
            new Dictionary<VNPhotoMaskShape, Sprite>();
        static readonly Dictionary<VNPhotoStickerShape, Sprite> _stickers =
            new Dictionary<VNPhotoStickerShape, Sprite>();
        static Sprite _paper;

        // ==================================================================
        // 边框底图
        // ==================================================================

        /// <summary>程序化边框底图（含主色调，不同颜色各缓存一份）</summary>
        public static Sprite FrameSprite(VNPhotoFrameStyle style, Color main)
        {
            string key = $"{style}_{ColorUtility.ToHtmlStringRGB(main)}";
            if (_frames.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = new Color32[FrameW * FrameH];
            for (int y = 0; y < FrameH; y++)
                for (int x = 0; x < FrameW; x++)
                {
                    float u = (x + 0.5f) / FrameW;          // 0~1 左→右
                    float v = (y + 0.5f) / FrameH;          // 0~1 下→上
                    pixels[y * FrameW + x] = FramePixel(style, main, u, v, x, y);
                }

            var sprite = MakeSprite($"PhotoFrame_{key}", FrameW, FrameH, pixels);
            _frames[key] = sprite;
            return sprite;
        }

        static Color32 FramePixel(VNPhotoFrameStyle style, Color main, float u, float v,
            int x, int y)
        {
            switch (style)
            {
                case VNPhotoFrameStyle.PinkCheck:
                {
                    // 粉格子：两级方格交替。对比度必须压得很低，否则一眼看去像
                    // Photoshop 的透明棋盘格而不是可爱的格纹布
                    bool cell = ((x / 26) + (y / 26)) % 2 == 0;
                    var baseColor = cell ? Lighten(main, 0.58f) : Lighten(main, 0.76f);
                    float edge = EdgeAmount(u, v, 0.055f);
                    return ToColor32(Color.Lerp(baseColor, main, edge));
                }
                case VNPhotoFrameStyle.StarrySky:
                {
                    // 星空：上深下浅的夜色渐变 + 伪随机星点
                    var night = Color.Lerp(Darken(main, 0.55f), main, v * 0.65f);
                    float star = StarNoise(x, y);
                    var c = Color.Lerp(night, Color.white, star);
                    float edge = EdgeAmount(u, v, 0.05f);
                    return ToColor32(Color.Lerp(c, Lighten(main, 0.5f), edge * 0.85f));
                }
                case VNPhotoFrameStyle.Film:
                {
                    // 胶片：深色片基 + 上下齿孔
                    var body = Darken(main, 0.72f);
                    bool holeBand = v > 0.9f || v < 0.1f;
                    if (holeBand)
                    {
                        int cx = (x % 32) - 16;
                        int cyBase = v > 0.5f ? (int)(FrameH * 0.95f) : (int)(FrameH * 0.05f);
                        int cy = y - cyBase;
                        if (cx * cx + cy * cy < 8 * 8) return ToColor32(new Color(1f, 1f, 1f, 1f));
                    }
                    return ToColor32(body);
                }
                case VNPhotoFrameStyle.SimpleWhite:
                {
                    // 简约白框：白底 + 一圈细描边
                    float edge = EdgeAmount(u, v, 0.03f);
                    float line = EdgeAmount(u, v, 0.045f) - EdgeAmount(u, v, 0.035f);
                    var c = Color.Lerp(new Color(0.98f, 0.98f, 0.98f), Color.white, edge);
                    return ToColor32(Color.Lerp(c, main, Mathf.Clamp01(line * 4f)));
                }
                default: // Sakura
                {
                    // 樱花：斜向柔和渐变 + 散落花点
                    float g = Mathf.Clamp01((u * 0.6f + v * 0.4f));
                    var c = Color.Lerp(Lighten(main, 0.55f), main, g);
                    float petal = PetalNoise(x, y);
                    c = Color.Lerp(c, Lighten(main, 0.85f), petal);
                    float edge = EdgeAmount(u, v, 0.05f);
                    return ToColor32(Color.Lerp(c, Saturate(main, 0.2f), edge * 0.7f));
                }
            }
        }

        /// <summary>离边缘多近（0 = 中心，1 = 最外圈），band 是边缘带宽度占比</summary>
        static float EdgeAmount(float u, float v, float band)
        {
            float d = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
            return 1f - Mathf.Clamp01(d / band);
        }

        static float StarNoise(int x, int y)
        {
            // 固定伪随机：同一像素永远同一结果（贴图要可重现）
            float h = Frac(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f);
            if (h < 0.9965f) return 0f;
            return Mathf.InverseLerp(0.9965f, 1f, h);
        }

        static float PetalNoise(int x, int y)
        {
            float h = Frac(Mathf.Sin(x * 3.1f + y * 7.7f) * 1234.567f);
            return h > 0.993f ? 0.8f : 0f;
        }

        // ==================================================================
        // 背景（画在人物身后，被开窗形状裁切）
        // ==================================================================

        static readonly Dictionary<string, Sprite> _backdrops = new Dictionary<string, Sprite>();

        /// <summary>程序化背景。两色各缓存一份。</summary>
        public static Sprite BackdropSprite(VNPhotoBackdropStyle style, Color main, Color second)
        {
            string key = $"{style}_{ColorUtility.ToHtmlStringRGB(main)}" +
                         $"_{ColorUtility.ToHtmlStringRGB(second)}";
            if (_backdrops.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = new Color32[FrameW * FrameH];
            for (int y = 0; y < FrameH; y++)
                for (int x = 0; x < FrameW; x++)
                {
                    float u = (x + 0.5f) / FrameW;
                    float v = (y + 0.5f) / FrameH;
                    pixels[y * FrameW + x] = BackdropPixel(style, main, second, u, v, x, y);
                }

            var sprite = MakeSprite($"PhotoBackdrop_{key}", FrameW, FrameH, pixels);
            _backdrops[key] = sprite;
            return sprite;
        }

        static Color32 BackdropPixel(VNPhotoBackdropStyle style, Color main, Color second,
            float u, float v, int x, int y)
        {
            switch (style)
            {
                case VNPhotoBackdropStyle.SolidColor:
                    return ToColor32(main);

                case VNPhotoBackdropStyle.VerticalGradient:
                    return ToColor32(Color.Lerp(second, main, v));

                case VNPhotoBackdropStyle.RadialBurst:
                {
                    // 大头贴机的经典放射线：从画面中心射出的交替色楔形
                    float dx = u - 0.5f, dy = (v - 0.5f) * 0.75f;   // 4:3 修正成圆
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    const int wedges = 24;
                    float t = Mathf.Repeat(angle / (360f / wedges), 1f);
                    var c = t < 0.5f ? main : second;
                    // 中心亮一点，像打了顶光
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    return ToColor32(Color.Lerp(Lighten(c, 0.45f), c, Mathf.Clamp01(r / 0.42f)));
                }
                case VNPhotoBackdropStyle.Stripes:
                {
                    bool on = Mathf.Repeat((x + y) / 36f, 2f) < 1f;
                    return ToColor32(on ? main : second);
                }
                case VNPhotoBackdropStyle.Dots:
                {
                    const int cell = 44;
                    int cx = x % cell - cell / 2;
                    int cy = y % cell - cell / 2;
                    bool dot = cx * cx + cy * cy < 12 * 12;
                    return ToColor32(dot ? main : second);
                }
                case VNPhotoBackdropStyle.StarryNight:
                {
                    var night = Color.Lerp(Darken(main, 0.45f), main, v * 0.7f);
                    float star = StarNoise(x, y);
                    return ToColor32(Color.Lerp(night, second, star));
                }
                case VNPhotoBackdropStyle.Rainbow:
                {
                    var c = Color.HSVToRGB(Mathf.Repeat(u, 1f), 0.42f, 1f);
                    return ToColor32(Color.Lerp(c, second, 0.25f));
                }
                default: // Bokeh
                {
                    var c = Color.Lerp(second, main, v * 0.8f);
                    // 几个固定位置的柔光斑（写死坐标，保证每次生成都一样）
                    float glow = Blob(u, v, 0.22f, 0.68f, 0.16f)
                               + Blob(u, v, 0.72f, 0.78f, 0.11f)
                               + Blob(u, v, 0.55f, 0.32f, 0.14f)
                               + Blob(u, v, 0.14f, 0.24f, 0.09f)
                               + Blob(u, v, 0.88f, 0.42f, 0.13f);
                    return ToColor32(Color.Lerp(c, Lighten(main, 0.7f), Mathf.Clamp01(glow)));
                }
            }
        }

        /// <summary>柔边圆斑的强度（用于 Bokeh）</summary>
        static float Blob(float u, float v, float cx, float cy, float radius)
        {
            float dx = u - cx, dy = (v - cy) * 0.75f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(1f - d / radius) * 0.55f;
        }

        // ==================================================================
        // 人物开窗遮罩 / 描边
        // ==================================================================

        /// <summary>开窗形状的实心白图（挂 Mask 用；None 返回纯白方块）</summary>
        public static Sprite MaskSprite(VNPhotoMaskShape shape)
        {
            if (_masks.TryGetValue(shape, out var cached) && cached != null) return cached;

            var pixels = new Color32[MaskSize * MaskSize];
            for (int y = 0; y < MaskSize; y++)
                for (int x = 0; x < MaskSize; x++)
                {
                    float dx = (x + 0.5f) / MaskSize - 0.5f;
                    float dy = (y + 0.5f) / MaskSize - 0.5f;
                    float a = ShapeAlpha(shape, dx, dy, 0.5f);
                    pixels[y * MaskSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            var sprite = MakeSprite($"PhotoMask_{shape}", MaskSize, MaskSize, pixels);
            _masks[shape] = sprite;
            return sprite;
        }

        /// <summary>开窗形状的描边环（只有边缘一圈实心）</summary>
        public static Sprite MaskRingSprite(VNPhotoMaskShape shape)
        {
            if (_maskRings.TryGetValue(shape, out var cached) && cached != null) return cached;

            const float outer = 0.5f;
            const float inner = 0.5f - 0.045f;
            var pixels = new Color32[MaskSize * MaskSize];
            for (int y = 0; y < MaskSize; y++)
                for (int x = 0; x < MaskSize; x++)
                {
                    float dx = (x + 0.5f) / MaskSize - 0.5f;
                    float dy = (y + 0.5f) / MaskSize - 0.5f;
                    float a = ShapeAlpha(shape, dx, dy, outer) *
                              (1f - ShapeAlpha(shape, dx, dy, inner));
                    pixels[y * MaskSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            var sprite = MakeSprite($"PhotoMaskRing_{shape}", MaskSize, MaskSize, pixels);
            _maskRings[shape] = sprite;
            return sprite;
        }

        /// <summary>形状覆盖率（带 1px 软边抗锯齿）。dx/dy 以中心为原点，范围 ±0.5</summary>
        static float ShapeAlpha(VNPhotoMaskShape shape, float dx, float dy, float radius)
        {
            const float soft = 1.5f / MaskSize;
            switch (shape)
            {
                case VNPhotoMaskShape.Ellipse:
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    return Mathf.Clamp01((radius - d) / soft);
                }
                case VNPhotoMaskShape.RoundedRect:
                {
                    // 圆角矩形有向距离场：d ≤ 0 在内部
                    float r = radius * 0.32f;                       // 圆角半径
                    float qx = Mathf.Abs(dx) - (radius - r);
                    float qy = Mathf.Abs(dy) - (radius - r);
                    float outside = Mathf.Sqrt(
                        Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                        Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
                    float d = outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    return Mathf.Clamp01(-d / soft);
                }
                default:
                    // 不裁切：整块方形
                    return (Mathf.Abs(dx) <= radius && Mathf.Abs(dy) <= radius) ? 1f : 0f;
            }
        }

        // ==================================================================
        // 贴纸
        // ==================================================================

        /// <summary>贴纸图案（纯白 + alpha，颜色靠 Image.color 上）</summary>
        public static Sprite StickerSprite(VNPhotoStickerShape shape)
        {
            if (_stickers.TryGetValue(shape, out var cached) && cached != null) return cached;

            var pixels = new Color32[StickerSize * StickerSize];
            for (int y = 0; y < StickerSize; y++)
                for (int x = 0; x < StickerSize; x++)
                {
                    // 2×2 超采样抗锯齿
                    float a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float px = (x + 0.25f + sx * 0.5f) / StickerSize - 0.5f;
                            float py = (y + 0.25f + sy * 0.5f) / StickerSize - 0.5f;
                            if (StickerInside(shape, px, py)) a += 0.25f;
                        }
                    pixels[y * StickerSize + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            var sprite = MakeSprite($"PhotoSticker_{shape}", StickerSize, StickerSize, pixels);
            _stickers[shape] = sprite;
            return sprite;
        }

        /// <summary>贴纸形状的隐函数判定。x/y 以中心为原点，范围 ±0.5</summary>
        static bool StickerInside(VNPhotoStickerShape shape, float x, float y)
        {
            switch (shape)
            {
                case VNPhotoStickerShape.Heart:
                {
                    // 经典心形隐函数（y 轴翻转让尖端朝下）
                    float hx = x / 0.44f, hy = -y / 0.44f;
                    float t = hx * hx + hy * hy - 1f;
                    return t * t * t - hx * hx * hy * hy * hy <= 0f;
                }
                case VNPhotoStickerShape.Star:
                    return InStar(x, y, 5, 0.47f, 0.2f, 90f);

                case VNPhotoStickerShape.Sparkle:
                {
                    // 四角闪光：内凹的星形（超椭圆的反面）
                    float ax = Mathf.Abs(x) / 0.48f, ay = Mathf.Abs(y) / 0.48f;
                    return Mathf.Sqrt(ax) + Mathf.Sqrt(ay) <= 1f;
                }
                case VNPhotoStickerShape.Ribbon:
                {
                    // 蝴蝶结：中心结 + 左右两个向外张开的三角
                    if (x * x + y * y <= 0.09f * 0.09f) return true;
                    float ax = Mathf.Abs(x);
                    if (ax < 0.07f || ax > 0.46f) return false;
                    float halfHeight = Mathf.Lerp(0.06f, 0.26f, Mathf.InverseLerp(0.07f, 0.46f, ax));
                    return Mathf.Abs(y) <= halfHeight;
                }
                case VNPhotoStickerShape.SpeechBubble:
                {
                    // 圆角矩形气泡 + 左下尾巴
                    float bx = Mathf.Abs(x) - 0.28f, by = Mathf.Abs(y - 0.07f) - 0.18f;
                    float d = Mathf.Sqrt(Mathf.Max(bx, 0f) * Mathf.Max(bx, 0f) +
                                         Mathf.Max(by, 0f) * Mathf.Max(by, 0f)) +
                              Mathf.Min(Mathf.Max(bx, by), 0f);
                    if (d <= 0.14f) return true;
                    if (y < -0.11f && y > -0.42f)
                    {
                        float w = Mathf.Lerp(0.12f, 0f, Mathf.InverseLerp(-0.11f, -0.42f, y));
                        return x > -0.22f && x < -0.22f + w;
                    }
                    return false;
                }
                case VNPhotoStickerShape.Flower:
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    float ang = Mathf.Atan2(y, x);
                    float petal = 0.18f + 0.28f * Mathf.Abs(Mathf.Cos(ang * 2.5f));
                    return r <= petal;
                }
                case VNPhotoStickerShape.Note:
                {
                    // 八分音符：符头椭圆 + 竖杆 + 顶部旗
                    float hx = (x + 0.16f) / 0.19f, hy = (y + 0.26f) / 0.14f;
                    if (hx * hx + hy * hy <= 1f) return true;
                    if (x > 0.0f && x < 0.08f && y > -0.26f && y < 0.42f) return true;
                    if (x >= 0.08f && x < 0.3f && y > 0.42f - (x - 0.08f) * 1.1f - 0.13f &&
                        y < 0.42f - (x - 0.08f) * 0.6f) return true;
                    return false;
                }
                case VNPhotoStickerShape.Crown:
                {
                    if (y < -0.3f || y > 0.34f) return false;
                    if (Mathf.Abs(x) > 0.36f) return false;
                    if (y < -0.12f) return true;                              // 底座
                    // 三个尖：把 x 折成 3 段，每段中心是峰
                    float t = (x + 0.36f) / 0.24f;
                    float saw = 1f - Mathf.Abs(Frac(t) - 0.5f) * 2f;          // 0(谷)~1(峰)
                    return y <= -0.12f + saw * 0.46f;
                }
                case VNPhotoStickerShape.CatEars:
                {
                    // 两个三角耳朵
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float ex = x - s * 0.22f;
                        if (y < -0.18f || y > 0.34f) continue;
                        float halfWidth = Mathf.Lerp(0.17f, 0f, Mathf.InverseLerp(-0.18f, 0.34f, y));
                        if (Mathf.Abs(ex) <= halfWidth) return true;
                    }
                    return false;
                }
                default: // Cloud
                {
                    return InCircle(x, y, -0.18f, -0.04f, 0.19f) ||
                           InCircle(x, y, 0.04f, 0.06f, 0.24f) ||
                           InCircle(x, y, 0.24f, -0.04f, 0.17f) ||
                           (Mathf.Abs(x) <= 0.4f && y >= -0.22f && y <= -0.02f);
                }
            }
        }

        static bool InStar(float x, float y, int points, float outer, float inner,
            float rotationDeg)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r > outer) return false;
            float ang = Mathf.Atan2(y, x) * Mathf.Rad2Deg - rotationDeg;
            float step = 360f / points;
            float local = Mathf.Abs(Mathf.Repeat(ang, step) - step * 0.5f) / (step * 0.5f);
            return r <= Mathf.Lerp(outer, inner, local);
        }

        static bool InCircle(float x, float y, float cx, float cy, float radius)
        {
            float dx = x - cx, dy = y - cy;
            return dx * dx + dy * dy <= radius * radius;
        }

        // ==================================================================
        // 相纸（结算时飞出来的那张）
        // ==================================================================

        /// <summary>白色圆角相纸（结算层用，照片贴在它上面）</summary>
        public static Sprite PaperSprite()
        {
            if (_paper != null) return _paper;

            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float a = ShapeAlpha(VNPhotoMaskShape.RoundedRect, dx, dy, 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            // 九宫格：四角保持圆角，中间任意拉伸
            _paper = MakeSprite("PhotoPaper", size, size, pixels,
                new Vector4(28, 28, 28, 28));
            return _paper;
        }

        static Sprite _circle;

        /// <summary>实心圆（快门按钮底）。VNProceduralTextures 那边只有软边光晕，做不了实心按钮</summary>
        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;

            const int size = 128;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float a = ShapeAlpha(VNPhotoMaskShape.Ellipse, dx, dy, 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }

            _circle = MakeSprite("PhotoCircle", size, size, pixels);
            return _circle;
        }

        // ==================================================================
        // 工具
        // ==================================================================

        static Sprite MakeSprite(string name, int w, int h, Color32[] pixels,
            Vector4 border = default)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, border);
            sprite.name = name + "Sprite";
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }

        static float Frac(float v) => v - Mathf.Floor(v);

        static Color32 ToColor32(Color c) => new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f),
            (byte)(Mathf.Clamp01(c.g) * 255f),
            (byte)(Mathf.Clamp01(c.b) * 255f),
            (byte)(Mathf.Clamp01(c.a) * 255f));

        static Color Lighten(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darken(Color c, float t) => Color.Lerp(c, Color.black, t);

        static Color Saturate(Color c, float t)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var result = Color.HSVToRGB(h, Mathf.Clamp01(s + t), Mathf.Clamp01(v - t * 0.15f));
            result.a = c.a;
            return result;
        }
    }
}
