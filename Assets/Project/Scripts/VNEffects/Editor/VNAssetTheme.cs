using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 素材浏览器窗口的配色主题。
    ///
    /// 【能改到什么程度】
    /// Unity 编辑器**整体**只有 Light / Dark 两套官方主题，没法自定义
    /// （2019.3+ 编辑器 UI 虽然迁到了 UI Toolkit/USS，但样式表打包在编辑器资源里不开放覆盖）。
    /// 但**自己 IMGUI 画的窗口**每一像素都归自己管 —— 这个类就是把
    /// VNAssetBrowserWindow 的所有颜色、圆角、字体样式收成一处。
    ///
    /// 【为什么必须显式设文字颜色】
    /// Unity Dark 主题下 EditorStyles 的文字是浅色的。换成粉白底之后
    /// 直接用 EditorStyles.label 会得到"白底白字" —— 字直接消失。
    /// 所以主题启用时每个 GUIStyle 都要覆盖 normal/hover/active/focused 四个状态的 textColor。
    ///
    /// 【圆角怎么来】
    /// 程序化生成一张**白色**圆角贴图，用 GUI.color 染成任意颜色后 9-slice 拉伸 ——
    /// 一张贴图搞定所有尺寸和配色，零美术依赖（与项目里 VNProceduralTextures 一个路子）。
    /// 贴图是 static 的，域重载后会丢，所以全部走 lazy 重建 + HideFlags.DontSave。
    /// </summary>
    public static class VNAssetTheme
    {
        public enum Kind
        {
            /// <summary>跟随 Unity 原生外观，不画任何自定义底</summary>
            Default = 0,
            /// <summary>樱花粉白</summary>
            Sakura = 1,
        }

        const string PrefKey = "VNEffects.AssetBrowser.Theme";

        static Kind _kind = (Kind)(-1);

        public static Kind Current
        {
            get
            {
                if ((int)_kind < 0) _kind = (Kind)EditorPrefs.GetInt(PrefKey, (int)Kind.Default);
                return _kind;
            }
            set
            {
                if (_kind == value) return;
                _kind = value;
                EditorPrefs.SetInt(PrefKey, (int)value);
                ResetStyles();
            }
        }

        /// <summary>是否启用了自定义外观（Default 时全部绘制退回 Unity 原生）</summary>
        public static bool Enabled { get { return Current != Kind.Default; } }

        public static readonly string[] Names = { "默认", "樱花" };

        // ==================================================================
        // 调色板
        // ==================================================================

        static Color C(int r, int g, int b, float a = 1f)
        {
            return new Color(r / 255f, g / 255f, b / 255f, a);
        }

        /// <summary>窗口大底</summary>
        public static Color Window { get { return C(255, 245, 248); } }
        /// <summary>左侧分类栏底</summary>
        public static Color SidePanel { get { return C(253, 235, 241); } }
        /// <summary>工具条底</summary>
        public static Color Toolbar { get { return C(252, 228, 236); } }
        /// <summary>卡片 / 详情栏底</summary>
        public static Color Card { get { return C(255, 255, 255); } }
        /// <summary>卡片描边</summary>
        public static Color CardBorder { get { return C(245, 216, 226); } }
        /// <summary>选中态填充</summary>
        public static Color SelectedFill { get { return C(255, 228, 236); } }
        /// <summary>主色（选中描边 / 按钮 / 强调）</summary>
        public static Color Accent { get { return C(255, 143, 177); } }
        /// <summary>主色按下态</summary>
        public static Color AccentDown { get { return C(244, 116, 156); } }
        /// <summary>正文字色</summary>
        public static Color Text { get { return C(90, 64, 72); } }
        /// <summary>次要字色</summary>
        public static Color TextDim { get { return C(155, 129, 137); } }
        /// <summary>分隔线</summary>
        public static Color Divider { get { return C(240, 213, 222); } }
        /// <summary>隔行底色</summary>
        public static Color RowAlt { get { return C(255, 250, 252); } }

        // ==================================================================
        // 圆角贴图（白色，用 GUI.color 染色）
        // ==================================================================

        const int TexSize = 32;
        const int Radius = 9;

        static Texture2D _fill, _outline;

        public static Texture2D RoundedFill
        {
            get
            {
                if (_fill == null) _fill = BuildRounded(false);
                return _fill;
            }
        }

        public static Texture2D RoundedOutline
        {
            get
            {
                if (_outline == null) _outline = BuildRounded(true);
                return _outline;
            }
        }

        /// <summary>
        /// 生成圆角矩形贴图。outline=true 时只留 2px 描边，中间透明。
        /// 边缘按到圆心的距离做 1px 软过渡，缩放后不会有锯齿。
        /// </summary>
        static Texture2D BuildRounded(bool outline)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,       // 不写进任何资产
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = outline ? "VNRoundedOutline" : "VNRoundedFill",
            };

            const float BorderW = 1.6f;
            var px = new Color[TexSize * TexSize];

            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    // 到"圆角矩形边界"的有符号距离：把点折到最近的圆心去量
                    float dx = Mathf.Max(Radius - x - 0.5f, (x + 0.5f) - (TexSize - Radius), 0f);
                    float dy = Mathf.Max(Radius - y - 0.5f, (y + 0.5f) - (TexSize - Radius), 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);   // 直边区 dist=0
                    float sd = dist - Radius;                      // <0 在内部

                    float a = Mathf.Clamp01(0.5f - sd);            // 1px 软边
                    if (outline)
                    {
                        // 只保留贴着边界的一圈
                        float inner = Mathf.Clamp01(0.5f - (sd + BorderW));
                        a = Mathf.Clamp01(a - inner);
                    }
                    px[y * TexSize + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // ==================================================================
        // 绘制辅助
        // ==================================================================

        /// <summary>画一个圆角实心块（主题关闭时退回直角 DrawRect）。</summary>
        public static void Box(Rect r, Color color)
        {
            if (!Enabled) { EditorGUI.DrawRect(r, color); return; }
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, RoundedFill, ScaleMode.StretchToFill, true, 0f,
                            Color.white, 0f, Radius);
            GUI.color = old;
        }

        /// <summary>画一圈圆角描边。</summary>
        public static void Outline(Rect r, Color color, float width = 1.6f)
        {
            if (!Enabled) { VNAssetUi.DrawOutline(r, color, Mathf.Max(1f, width)); return; }
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, RoundedOutline, ScaleMode.StretchToFill, true, 0f,
                            Color.white, 0f, Radius);
            GUI.color = old;
        }

        /// <summary>卡片：白底 + 淡粉描边；选中时粉底 + 粉描边。</summary>
        public static void CardBox(Rect r, bool selected)
        {
            if (!Enabled)
            {
                if (selected) EditorGUI.DrawRect(r, new Color(0.35f, 0.6f, 0.95f, 0.45f));
                return;
            }
            Box(r, selected ? SelectedFill : Card);
            Outline(r, selected ? Accent : CardBorder, selected ? 2f : 1.2f);
        }

        /// <summary>粉色圆角按钮。返回是否被点击。</summary>
        public static bool Button(Rect r, GUIContent content, bool primary = false)
        {
            if (!Enabled) return GUI.Button(r, content, EditorStyles.miniButton);

            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;
            bool hover = r.Contains(e.mousePosition);
            bool active = GUIUtility.hotControl == id;
            bool clicked = false;

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (hover && e.button == 0) { GUIUtility.hotControl = id; e.Use(); }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        if (hover) clicked = true;
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                    Color bg = primary
                        ? (active ? AccentDown : (hover ? Accent : Accent))
                        : (active ? SelectedFill : (hover ? SelectedFill : Card));
                    Box(r, bg);
                    Outline(r, primary ? AccentDown : CardBorder, 1.2f);
                    var style = primary ? ButtonLabelOn : ButtonLabel;
                    style.Draw(r, content, false, false, false, false);
                    break;
            }
            return clicked;
        }

        // ==================================================================
        // 样式（主题关闭时直接回退到 VNAssetUi / EditorStyles）
        // ==================================================================

        static GUIStyle _label, _dim, _dimWrap, _bold, _caption, _captionOn, _btn, _btnOn, _search, _side, _sideOn;

        public static void ResetStyles()
        {
            _label = _dim = _dimWrap = _bold = _caption = _captionOn = null;
            _btn = _btnOn = _search = _side = _sideOn = null;
        }

        static GUIStyle Tint(GUIStyle src, Color c)
        {
            var s = new GUIStyle(src);
            s.normal.textColor = c;
            s.hover.textColor = c;
            s.active.textColor = c;
            s.focused.textColor = c;
            s.onNormal.textColor = c;
            s.onHover.textColor = c;
            s.onActive.textColor = c;
            s.onFocused.textColor = c;
            return s;
        }

        public static GUIStyle Label
        {
            get
            {
                if (!Enabled) return EditorStyles.label;
                if (_label == null) _label = Tint(EditorStyles.label, Text);
                return _label;
            }
        }

        public static GUIStyle Bold
        {
            get
            {
                if (!Enabled) return EditorStyles.boldLabel;
                if (_bold == null) _bold = Tint(EditorStyles.boldLabel, Text);
                return _bold;
            }
        }

        /// <summary>次要信息（文件名、尺寸、时长）</summary>
        public static GUIStyle Dim
        {
            get
            {
                if (!Enabled) return VNAssetUi.RowLabel;
                if (_dim == null) _dim = Tint(EditorStyles.miniLabel, TextDim);
                return _dim;
            }
        }

        /// <summary>会换行的次要说明文字</summary>
        public static GUIStyle DimWrap
        {
            get
            {
                if (!Enabled) return EditorStyles.wordWrappedMiniLabel;
                if (_dimWrap == null) _dimWrap = Tint(EditorStyles.wordWrappedMiniLabel, TextDim);
                return _dimWrap;
            }
        }

        /// <summary>网格卡片下方的 id 标签</summary>
        public static GUIStyle Caption
        {
            get
            {
                if (_caption == null)
                {
                    _caption = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        clipping = TextClipping.Clip,
                    };
                    if (Enabled) _caption = Tint(_caption, Text);
                }
                return _caption;
            }
        }

        public static GUIStyle CaptionOn
        {
            get
            {
                if (_captionOn == null)
                {
                    _captionOn = new GUIStyle(Caption) { fontStyle = FontStyle.Bold };
                    if (Enabled) _captionOn = Tint(_captionOn, AccentDown);
                }
                return _captionOn;
            }
        }

        static GUIStyle ButtonLabel
        {
            get
            {
                if (_btn == null)
                    _btn = Tint(new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleCenter }, Text);
                return _btn;
            }
        }

        static GUIStyle ButtonLabelOn
        {
            get
            {
                if (_btnOn == null)
                    _btnOn = Tint(new GUIStyle(EditorStyles.miniLabel)
                    { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }, Color.white);
                return _btnOn;
            }
        }

        /// <summary>左栏类别条目</summary>
        public static GUIStyle Side
        {
            get
            {
                if (!Enabled) return EditorStyles.label;
                if (_side == null) _side = Tint(EditorStyles.label, Text);
                return _side;
            }
        }

        public static GUIStyle SideOn
        {
            get
            {
                if (!Enabled) return EditorStyles.boldLabel;
                if (_sideOn == null) _sideOn = Tint(EditorStyles.boldLabel, AccentDown);
                return _sideOn;
            }
        }

        /// <summary>搜索框：主题开启时用无边框输入，外框自己画圆角</summary>
        public static GUIStyle SearchField
        {
            get
            {
                if (!Enabled) return EditorStyles.toolbarSearchField;
                if (_search == null)
                {
                    _search = Tint(new GUIStyle(EditorStyles.textField)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(8, 6, 0, 0),
                    }, Text);
                    // 背景交给 Box() 画，这里全部清空免得叠出双层框
                    _search.normal.background = null;
                    _search.focused.background = null;
                    _search.hover.background = null;
                    _search.active.background = null;
                }
                return _search;
            }
        }
    }
}
