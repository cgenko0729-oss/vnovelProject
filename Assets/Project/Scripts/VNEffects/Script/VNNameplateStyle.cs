using TMPro;
using UnityEngine;

namespace VNEffects
{
    /// <summary>内置名牌样式预设的 id（剧本/Inspector 里按这个选）</summary>
    public enum VNNameplateStyleId
    {
        /// <summary>不装饰：老版本外观（正文字体 + 伪粗 + 纯色底板）</summary>
        Plain = 0,
        /// <summary>粗黑渐变 + 白粗描边 + 底部投影 + 下划线，无底板（气势最强）</summary>
        Bold = 1,
        /// <summary>粗黑纯色 + 白内描边 + 深外描边，保留圆角底板（最稳，易读性最好）</summary>
        Plate = 2,
        /// <summary>白字 + 深色粗描边，无底板（任何背景都压得住）</summary>
        Outline = 3,

        // ---- 以下为「三层字」系列：面 + 角色色描边 + 深色最外层 ----
        // 最外层是深色是这一系列的立身之本：Bold/Outline 的最外层是白色，
        // 遇到白背景或亮立绘就整个消失（实测浅背景下名字糊得看不见）。

        /// <summary>白面 + 角色色粗描边 + 深色第二层外描边，无底板（深浅底通用，日常首选）</summary>
        Duo = 4,
        /// <summary>金色渐变 + 浮雕高光 + 深棕描边（最贵气；金色固定，不跟角色色走）</summary>
        Gold = 5,
        /// <summary>冷银渐变 + 浮雕高光 + 蓝灰描边（Gold 的冷色版）</summary>
        Silver = 6,
        /// <summary>角色色 HDR 面 + 白细描边 + 同色大扩散辉光（走 Bloom，暗场/科幻）</summary>
        Neon = 7,
        /// <summary>角色色深面 + 白细描边 + 大扩散黑投影（柔和文艺，压得住花背景）</summary>
        Ink = 8,
        /// <summary>角色色亮面 + 白粗描边 + 同色深第二层（可爱系，糖果轮廓）</summary>
        Candy = 9,
    }

    /// <summary>
    /// 名牌装饰样式 —— 把「粗黑体 + 描边 + 渐变 + 投影」这套观感拆成可调参数。
    ///
    /// 【为什么单独一层而不是直接在 VNDialogueBox 里写死】
    /// 同一套参数要能被多个预设复用、被角色配色注入、被语言切换后重新应用，
    /// 所以做成「纯数据 + 静态 Apply」：不持有场景引用，可单独测试。
    ///
    /// 【三个必须知道的硬约束】
    /// 1. **材质必须走实例**。TMP 组件默认用 fontSharedMaterial，也就是字体资产自带的
    ///    那一份材质——直接改它会把正文、按钮、Backlog 里所有用同一字体的文字一起改掉。
    ///    这里一律通过 text.fontMaterial 取（TMP 在首次访问时自动 new 一份实例）。
    /// 2. **描边厚度受字体图集 padding 限制**。装饰字体走 VNFont.DisplayAsset
    ///    （padding 14），普通正文字体 padding 只有 6，粗描边会被切出方块状缺角。
    /// 3. **underlay 通道只有一条**，所以「第二层外描边」和「投影」二选一
    ///    （见 UnderlayUse）。要两者兼得只能叠第二个 TMP 组件，这里不值得。
    /// </summary>
    [System.Serializable]
    public class VNNameplateStyle
    {
        /// <summary>underlay 通道的用途（TMP 只有一条 underlay，二选一）</summary>
        public enum UnderlayUse
        {
            None = 0,
            /// <summary>当第二层外描边用：offset 归零 + 大 dilate，环绕出一圈深色轮廓</summary>
            SecondOutline = 1,
            /// <summary>当投影用：offset 往右下偏</summary>
            Shadow = 2,
        }

        // ---- 字体与排版 ----
        [Header("用 VNFont 的装饰字体（Heavy 字重 + 大 padding）")]
        public bool useDisplayFont = true;
        [Header("字号")]
        public float fontSize = 34f;
        [Header("字距（参考图那种拉开的间隔感）")]
        public float characterSpacing = 16f;

        // ---- 面色 ----
        [Header("面色用上下渐变（关 = 纯色）")]
        public bool useGradient = true;
        [Header("面色取角色配色（关 = 用下面的固定面色）")]
        public bool faceUsesCharacterColor = true;
        [Header("固定面色（faceUsesCharacterColor 关时生效）")]
        public Color fixedFaceColor = Color.white;

        // ---- 描边 ----
        [Header("描边宽度 0~1（受字体图集 padding 限制，装饰字体可到 ~0.3）")]
        public float outlineWidth = 0.18f;
        [Header("描边柔和度 0~1（0 = 硬边）")]
        public float outlineSoftness = 0f;
        [Header("描边取角色配色（关 = 用下面的固定描边色）")]
        public bool outlineUsesCharacterColor;
        [Header("固定描边色（outlineUsesCharacterColor 关时生效）")]
        public Color fixedOutlineColor = Color.white;

        // ---- underlay（第二层描边 或 投影，二选一）----
        [Header("underlay 通道用途")]
        public UnderlayUse underlayUse = UnderlayUse.Shadow;
        [Header("underlay 颜色")]
        public Color underlayColor = new Color(0.08f, 0.05f, 0.09f, 0.85f);
        [Header("underlay 偏移（Shadow 用；SecondOutline 时强制归零）")]
        public Vector2 underlayOffset = new Vector2(0.6f, -0.6f);
        [Header("underlay 膨胀量（SecondOutline 靠它撑出轮廓厚度）")]
        public float underlayDilate = 0.1f;
        [Header("underlay 柔和度")]
        public float underlaySoftness = 0.05f;

        // ---- 固定渐变面色的下端色（faceUsesCharacterColor 关 + useGradient 开时生效）----
        [Header("固定面色的下端色（金/银这种不跟角色色走的样式用）")]
        public Color fixedFaceColorBottom = Color.white;

        // ---- 面色 HDR 增益 ----
        [Header("面色亮度倍率：>1 会超出 Bloom 阈值(1.0) 变成发光字")]
        public float faceHdrBoost = 1f;

        // ---- 浮雕 + 光照（金属质感的来源）----
        // TMP 的 SDF shader 支持把字面当立体表面打光，这就是「镶金边」的实现方式：
        // 金色渐变只是颜色，真正让它看起来像金属的是 Bevel 的高光与暗面。
        // 注意 Mobile 版 TMP shader 没有这组属性，应用前会检查 HasProperty，缺了就跳过。
        [Header("开启浮雕+光照（金属/立体感；Mobile 版 TMP shader 不支持会自动跳过）")]
        public bool useBevel;
        [Header("浮雕强度")]
        public float bevelAmount = 0.6f;
        [Header("浮雕宽度")]
        public float bevelWidth = 0.4f;
        [Header("浮雕圆润度")]
        public float bevelRoundness = 0.4f;
        [Header("浮雕削顶（防止高光过曝）")]
        public float bevelClamp = 0.2f;
        [Header("打光角度（弧度；默认左上打光）")]
        public float lightAngle = 2.356f;
        [Header("高光颜色")]
        public Color specularColor = Color.white;
        [Header("高光锐度")]
        public float specularPower = 2.5f;
        [Header("反射强度")]
        public float reflectivity = 10f;
        [Header("漫反射")]
        public float diffuse = 0.6f;
        [Header("环境光")]
        public float ambient = 0.5f;

        // ---- 名牌容器装饰（由 VNDialogueBox 落实）----
        [Header("显示圆角底板")]
        public bool showPlate = true;
        [Header("底板不透明度倍率")]
        public float plateAlpha = 1f;
        [Header("底部横线装饰")]
        public bool showUnderline;
        [Header("横线高度（像素）")]
        public float underlineHeight = 4f;
        [Header("横线左右内缩（像素）")]
        public float underlineInset = 6f;
        [Header("横线取角色配色（关 = 用描边色）")]
        public bool underlineUsesCharacterColor = true;

        // ------------------------------------------------------------------
        // 材质属性 id（自己缓存，不依赖 TMP 版本间可能变动的 ShaderUtilities 常量）
        // ------------------------------------------------------------------

        static readonly int IdFaceColor = Shader.PropertyToID("_FaceColor");
        static readonly int IdOutlineColor = Shader.PropertyToID("_OutlineColor");
        static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");
        static readonly int IdOutlineSoftness = Shader.PropertyToID("_OutlineSoftness");
        static readonly int IdUnderlayColor = Shader.PropertyToID("_UnderlayColor");
        static readonly int IdUnderlayOffsetX = Shader.PropertyToID("_UnderlayOffsetX");
        static readonly int IdUnderlayOffsetY = Shader.PropertyToID("_UnderlayOffsetY");
        static readonly int IdUnderlayDilate = Shader.PropertyToID("_UnderlayDilate");
        static readonly int IdUnderlaySoftness = Shader.PropertyToID("_UnderlaySoftness");

        static readonly int IdBevel = Shader.PropertyToID("_Bevel");
        static readonly int IdBevelOffset = Shader.PropertyToID("_BevelOffset");
        static readonly int IdBevelWidth = Shader.PropertyToID("_BevelWidth");
        static readonly int IdBevelRoundness = Shader.PropertyToID("_BevelRoundness");
        static readonly int IdBevelClamp = Shader.PropertyToID("_BevelClamp");
        static readonly int IdLightAngle = Shader.PropertyToID("_LightAngle");
        static readonly int IdSpecularColor = Shader.PropertyToID("_SpecularColor");
        static readonly int IdSpecularPower = Shader.PropertyToID("_SpecularPower");
        static readonly int IdReflectivity = Shader.PropertyToID("_Reflectivity");
        static readonly int IdDiffuse = Shader.PropertyToID("_Diffuse");
        static readonly int IdAmbient = Shader.PropertyToID("_Ambient");

        /// <summary>TMP 的浮雕开关关键字</summary>
        const string BevelKeyword = "BEVEL_ON";

        /// <summary>TMP 的 underlay 开关关键字（不开这个，改 underlay 参数完全没反应）</summary>
        const string UnderlayKeyword = "UNDERLAY_ON";

        // ------------------------------------------------------------------
        // 内置预设
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // 预设名解析（剧本 `ui name <样式>` 与编辑器下拉共用同一张表）
        // ------------------------------------------------------------------

        /// <summary>
        /// 剧本里能写的样式名 → 枚举。中英双写，剧本作者写中文更省事。
        /// 顺序即编辑器下拉的顺序，也是 Lint 报错时列出的候选。
        /// </summary>
        public static readonly (string name, VNNameplateStyleId id)[] Aliases =
        {
            ("双描边", VNNameplateStyleId.Duo),
            ("金边", VNNameplateStyleId.Gold),
            ("银边", VNNameplateStyleId.Silver),
            ("霓虹", VNNameplateStyleId.Neon),
            ("墨影", VNNameplateStyleId.Ink),
            ("糖果", VNNameplateStyleId.Candy),
            ("粗体", VNNameplateStyleId.Bold),
            ("描边", VNNameplateStyleId.Outline),
            ("底板", VNNameplateStyleId.Plate),
            ("朴素", VNNameplateStyleId.Plain),
        };

        /// <summary>剧本里写的名字（中文别名或英文枚举名，大小写不敏感）→ 枚举</summary>
        public static bool TryParseId(string text, out VNNameplateStyleId id)
        {
            id = VNNameplateStyleId.Bold;
            if (string.IsNullOrEmpty(text)) return false;
            string key = text.Trim();
            foreach (var a in Aliases)
                if (a.name == key) { id = a.id; return true; }
            foreach (VNNameplateStyleId v in System.Enum.GetValues(typeof(VNNameplateStyleId)))
                if (string.Equals(v.ToString(), key, System.StringComparison.OrdinalIgnoreCase))
                { id = v; return true; }
            return false;
        }

        /// <summary>枚举 → 剧本里推荐写的那个名字（存档与编辑器显示用）</summary>
        public static string NameOf(VNNameplateStyleId id)
        {
            foreach (var a in Aliases)
                if (a.id == id) return a.name;
            return id.ToString();
        }

        /// <summary>取内置预设（每次返回新实例，调用方可以随手改而不污染下一次）</summary>
        public static VNNameplateStyle Preset(VNNameplateStyleId id)
        {
            switch (id)
            {
                case VNNameplateStyleId.Bold: return BoldPreset();
                case VNNameplateStyleId.Plate: return PlatePreset();
                case VNNameplateStyleId.Outline: return OutlinePreset();
                case VNNameplateStyleId.Duo: return DuoPreset();
                case VNNameplateStyleId.Gold: return GoldPreset();
                case VNNameplateStyleId.Silver: return SilverPreset();
                case VNNameplateStyleId.Neon: return NeonPreset();
                case VNNameplateStyleId.Ink: return InkPreset();
                case VNNameplateStyleId.Candy: return CandyPreset();
                default: return PlainPreset();
            }
        }

        /// <summary>老外观：正文字体 + 纯色底板，不做任何装饰（回退用）</summary>
        static VNNameplateStyle PlainPreset() => new VNNameplateStyle
        {
            useDisplayFont = false,
            fontSize = 26f,
            characterSpacing = 0f,
            useGradient = false,
            faceUsesCharacterColor = false,
            fixedFaceColor = new Color(1f, 1f, 1f, 0.96f),
            outlineWidth = 0f,
            underlayUse = UnderlayUse.None,
            showPlate = true,
            showUnderline = false,
        };

        /// <summary>粗黑渐变 + 白粗描边 + 投影 + 下划线，无底板</summary>
        static VNNameplateStyle BoldPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 18f,
            useGradient = true,
            faceUsesCharacterColor = true,
            outlineWidth = 0.3f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = Color.white,
            underlayUse = UnderlayUse.Shadow,
            underlayColor = new Color(0.06f, 0.03f, 0.07f, 0.8f),
            underlayOffset = new Vector2(0.6f, -0.7f),
            underlayDilate = 0.08f,
            underlaySoftness = 0.06f,
            showPlate = false,
            showUnderline = true,
            underlineHeight = 4f,
            underlineInset = 4f,
            underlineUsesCharacterColor = true,
        };

        /// <summary>粗黑纯色 + 白内描边 + 深外描边，保留底板</summary>
        static VNNameplateStyle PlatePreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 32f,
            characterSpacing = 12f,
            useGradient = true,
            faceUsesCharacterColor = true,
            outlineWidth = 0.24f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = Color.white,
            underlayUse = UnderlayUse.SecondOutline,
            underlayColor = new Color(0.1f, 0.07f, 0.12f, 0.95f),
            underlayDilate = 0.35f,
            underlaySoftness = 0f,
            showPlate = true,
            plateAlpha = 1f,
            showUnderline = false,
        };

        /// <summary>白字 + 深色粗描边，无底板（百搭）</summary>
        static VNNameplateStyle OutlinePreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 34f,
            characterSpacing = 14f,
            useGradient = false,
            faceUsesCharacterColor = false,
            fixedFaceColor = Color.white,
            outlineWidth = 0.32f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = true,
            underlayUse = UnderlayUse.Shadow,
            underlayColor = new Color(0.05f, 0.04f, 0.06f, 0.7f),
            underlayOffset = new Vector2(0.5f, -0.5f),
            underlayDilate = 0.05f,
            underlaySoftness = 0.08f,
            showPlate = false,
            showUnderline = false,
        };

        /// <summary>白面 + 角色色粗描边 + 深色第二层外描边（深浅底通用）</summary>
        static VNNameplateStyle DuoPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 12f,
            useGradient = false,
            faceUsesCharacterColor = false,
            fixedFaceColor = Color.white,
            outlineWidth = 0.28f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = true, // 中间那圈 = 角色色，一眼认人
            underlayUse = UnderlayUse.SecondOutline,
            underlayColor = new Color(0.06f, 0.02f, 0.07f, 1f),
            underlayDilate = 0.58f, // 白面在浅背景上没对比，全靠这圈深外描边把字框出来
            underlaySoftness = 0f,
            showPlate = false,
            showUnderline = false,
        };

        /// <summary>金色渐变 + 浮雕高光 + 深棕描边</summary>
        static VNNameplateStyle GoldPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 14f,
            useGradient = true,
            faceUsesCharacterColor = false, // 金色是固定的：贵气来自金属感，不是角色色
            fixedFaceColor = new Color(1f, 0.94f, 0.72f),
            fixedFaceColorBottom = new Color(0.78f, 0.52f, 0.13f),
            outlineWidth = 0.22f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = new Color(0.24f, 0.12f, 0.02f),
            underlayUse = UnderlayUse.Shadow,
            underlayColor = new Color(0f, 0f, 0f, 0.75f),
            underlayOffset = new Vector2(0.7f, -0.8f),
            underlayDilate = 0.1f,
            underlaySoftness = 0.1f,
            useBevel = true,
            bevelAmount = 0.6f,
            bevelWidth = 0.4f,
            bevelRoundness = 0.4f,
            bevelClamp = 0.2f,
            lightAngle = 2.356f,
            specularColor = new Color(1f, 0.96f, 0.85f),
            specularPower = 2.5f,
            reflectivity = 12f,
            diffuse = 0.6f,
            ambient = 0.55f,
            showPlate = false,
            showUnderline = false,
        };

        /// <summary>冷银渐变 + 浮雕高光（Gold 的冷色版）</summary>
        static VNNameplateStyle SilverPreset()
        {
            var s = GoldPreset();
            s.fixedFaceColor = new Color(0.97f, 0.98f, 1f);
            s.fixedFaceColorBottom = new Color(0.55f, 0.62f, 0.72f);
            s.fixedOutlineColor = new Color(0.09f, 0.12f, 0.18f);
            s.outlineWidth = 0.26f; // 银面接近浅背景，描边要比金色那套更厚
            s.specularColor = new Color(0.9f, 0.95f, 1f);
            return s;
        }

        /// <summary>角色色 HDR 面 + 白细描边 + 同色辉光（面色超过 Bloom 阈值 1.0 才会发光）</summary>
        static VNNameplateStyle NeonPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 16f,
            useGradient = false,
            faceUsesCharacterColor = true,
            faceHdrBoost = 2.2f, // 关键：>1 才会被 Bloom 抓成发光
            outlineWidth = 0.12f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = new Color(1f, 1f, 1f, 0.9f),
            underlayUse = UnderlayUse.SecondOutline,
            underlayColor = new Color(0.4f, 0f, 0.3f, 0.7f),
            underlayDilate = 0.4f,
            underlaySoftness = 0.6f, // 柔到发散 = 辉光
            showPlate = false,
            showUnderline = false,
        };

        /// <summary>角色色深面 + 白细描边 + 大扩散黑投影</summary>
        static VNNameplateStyle InkPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 14f,
            useGradient = true,
            faceUsesCharacterColor = true,
            outlineWidth = 0.09f,
            outlineSoftness = 0.02f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = new Color(1f, 1f, 1f, 0.95f),
            underlayUse = UnderlayUse.Shadow,
            underlayColor = new Color(0f, 0f, 0f, 0.7f),
            underlayOffset = new Vector2(0.3f, -0.4f),
            underlayDilate = 0.3f,
            underlaySoftness = 0.55f, // 扩散开的软影，把杂背景压下去
            showPlate = false,
            showUnderline = false,
        };

        /// <summary>角色色亮面 + 白粗描边 + 同色深第二层（可爱系）</summary>
        static VNNameplateStyle CandyPreset() => new VNNameplateStyle
        {
            useDisplayFont = true,
            fontSize = 36f,
            characterSpacing = 14f,
            useGradient = true,
            faceUsesCharacterColor = true,
            outlineWidth = 0.24f,
            outlineSoftness = 0f,
            outlineUsesCharacterColor = false,
            fixedOutlineColor = Color.white,
            underlayUse = UnderlayUse.SecondOutline,
            underlayColor = new Color(0.35f, 0.06f, 0.26f, 0.95f),
            underlayDilate = 0.5f,
            underlaySoftness = 0f,
            showPlate = false,
            showUnderline = false,
        };

        // ------------------------------------------------------------------
        // 应用
        // ------------------------------------------------------------------

        /// <summary>
        /// 把样式应用到名牌文本上。
        /// <paramref name="faceTop"/> / <paramref name="faceBottom"/> / <paramref name="outline"/>
        /// 是当前说话者的配色（见 VNCharacterDef.GetNameplateColors）。
        /// </summary>
        public void ApplyTo(TMP_Text text, Color faceTop, Color faceBottom, Color outline)
        {
            if (text == null) return;

            // ---- 字体：装饰字体解析失败时静默留用当前字体，不让名牌变空白 ----
            if (useDisplayFont)
            {
                var display = VNFont.DisplayAsset;
                if (display != null && text.font != display) text.font = display;
            }
            else
            {
                var body = VNFont.Asset;
                if (body != null && text.font != body) text.font = body;
            }

            text.fontSize = fontSize;
            text.characterSpacing = characterSpacing;
            // Heavy 字重不能再叠伪粗：TMP 的 Bold 是 SDF 膨胀，会把描边一起顶糊
            text.fontStyle = useDisplayFont ? FontStyles.Normal : FontStyles.Bold;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            // ---- 面色 ----
            Color top = faceUsesCharacterColor ? faceTop : fixedFaceColor;
            Color bottom = faceUsesCharacterColor
                ? faceBottom
                : (useGradient ? fixedFaceColorBottom : fixedFaceColor);

            // HDR 发光走材质，不走顶点色：uGUI 的顶点色被钳到 1，写多少都不会超过 1，
            // 而 Bloom 的阈值就是 1.0（项目硬约定：发光 = HDR 颜色 + Bloom）。
            // 代价是发光与上下渐变二选一——渐变只能由顶点色表达。
            bool hdr = faceHdrBoost > 1.0001f;
            if (hdr)
            {
                text.enableVertexGradient = false;
                text.color = Color.white;
            }
            else if (useGradient)
            {
                // 顶点色与材质 _FaceColor 相乘，所以本体色留白、颜色全交给渐变
                text.color = Color.white;
                text.enableVertexGradient = true;
                text.colorGradient = new VertexGradient(top, top, bottom, bottom);
            }
            else
            {
                text.enableVertexGradient = false;
                text.color = top;
            }

            // ---- 材质实例：走 fontMaterial 而非 fontSharedMaterial ----
            var mat = text.fontMaterial;
            if (mat == null) return;

            // 面色统一交给顶点色/text.color；只有 HDR 发光那条路要把带增益的颜色写进材质
            mat.SetColor(IdFaceColor, hdr
                ? new Color(top.r * faceHdrBoost, top.g * faceHdrBoost, top.b * faceHdrBoost, 1f)
                : Color.white);
            mat.SetColor(IdOutlineColor, outlineUsesCharacterColor ? outline : fixedOutlineColor);
            mat.SetFloat(IdOutlineWidth, outlineWidth);
            mat.SetFloat(IdOutlineSoftness, outlineSoftness);

            if (underlayUse == UnderlayUse.None)
            {
                mat.DisableKeyword(UnderlayKeyword);
                mat.SetFloat(IdUnderlayDilate, 0f);
                mat.SetColor(IdUnderlayColor, new Color(0f, 0f, 0f, 0f));
            }
            else
            {
                mat.EnableKeyword(UnderlayKeyword);
                mat.SetColor(IdUnderlayColor, underlayColor);
                // 当第二层外描边用时偏移必须归零，否则轮廓会偏到一边
                Vector2 offset = underlayUse == UnderlayUse.SecondOutline
                    ? Vector2.zero : underlayOffset;
                mat.SetFloat(IdUnderlayOffsetX, offset.x);
                mat.SetFloat(IdUnderlayOffsetY, offset.y);
                mat.SetFloat(IdUnderlayDilate, underlayDilate);
                mat.SetFloat(IdUnderlaySoftness, underlaySoftness);
            }

            ApplyBevel(mat);
            text.SetMaterialDirty();
        }

        /// <summary>
        /// 浮雕 + 光照：把字面当立体表面打光，金/银那种金属质感全靠这一层。
        /// Mobile 版 TMP shader 没有这组属性，`HasProperty` 挡一下——
        /// 直接 SetFloat 不会报错但也不会有效果，静默失效比报错更难查。
        /// </summary>
        void ApplyBevel(Material mat)
        {
            if (!mat.HasProperty(IdBevel))
            {
                if (useBevel && !_bevelWarned)
                {
                    _bevelWarned = true;
                    Debug.LogWarning("[VNNameplate] 当前字体材质的 shader 不支持浮雕（多半是 " +
                                     "TMP Mobile 版 shader），金/银样式会退化成普通渐变描边。" +
                                     "把字体资产的 shader 换成 TextMeshPro/Distance Field 即可。");
                }
                return;
            }

            if (!useBevel)
            {
                mat.DisableKeyword(BevelKeyword);
                mat.SetFloat(IdBevel, 0f);
                return;
            }

            mat.EnableKeyword(BevelKeyword);
            mat.SetFloat(IdBevel, bevelAmount);
            mat.SetFloat(IdBevelOffset, 0f);
            mat.SetFloat(IdBevelWidth, bevelWidth);
            mat.SetFloat(IdBevelRoundness, bevelRoundness);
            mat.SetFloat(IdBevelClamp, bevelClamp);
            mat.SetFloat(IdLightAngle, lightAngle);
            mat.SetColor(IdSpecularColor, specularColor);
            mat.SetFloat(IdSpecularPower, specularPower);
            mat.SetFloat(IdReflectivity, reflectivity);
            mat.SetFloat(IdDiffuse, diffuse);
            mat.SetFloat(IdAmbient, ambient);
        }

        /// <summary>浮雕不支持的警告只发一次（每句台词都会重新上妆，不然刷屏）</summary>
        static bool _bevelWarned;
    }
}
