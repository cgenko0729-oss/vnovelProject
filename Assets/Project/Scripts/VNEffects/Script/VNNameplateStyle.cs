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

        /// <summary>TMP 的 underlay 开关关键字（不开这个，改 underlay 参数完全没反应）</summary>
        const string UnderlayKeyword = "UNDERLAY_ON";

        // ------------------------------------------------------------------
        // 内置预设
        // ------------------------------------------------------------------

        /// <summary>取内置预设（每次返回新实例，调用方可以随手改而不污染下一次）</summary>
        public static VNNameplateStyle Preset(VNNameplateStyleId id)
        {
            switch (id)
            {
                case VNNameplateStyleId.Bold: return BoldPreset();
                case VNNameplateStyleId.Plate: return PlatePreset();
                case VNNameplateStyleId.Outline: return OutlinePreset();
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
            Color bottom = faceUsesCharacterColor ? faceBottom : fixedFaceColor;
            if (useGradient)
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

            mat.SetColor(IdFaceColor, Color.white); // 面色统一交给顶点色/text.color
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

            text.SetMaterialDirty();
        }
    }
}
