using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>高亮洞口的形状</summary>
    public enum VNTutorialHole
    {
        /// <summary>圆角矩形（按钮、面板、记分板……绝大多数 UI）</summary>
        RoundedRect,
        /// <summary>椭圆（立绘的脸、圆形图标）</summary>
        Ellipse,
    }

    /// <summary>说明卡片停在屏幕的哪一格</summary>
    public enum VNTutorialCardSpot
    {
        /// <summary>自动：躲开洞口（洞在上半屏就把卡片放下半屏，反之亦然）</summary>
        Auto,
        Top,
        Center,
        Bottom,
        /// <summary>自定义：用步骤的 cardPos（归一化、左下原点、**卡片中心**）落位，教程编辑器画布上可直接拖卡片</summary>
        Custom,
    }

    /// <summary>教程的一步：挖哪个洞 + 说什么。</summary>
    [System.Serializable]
    public class VNTutorialStep
    {
        [Header("高亮目标：锚点 id（模块用 VNTutorialAnchors.Register 登记的名字）\n" +
                "留空 = 用下面的归一化矩形；两者都留空 = 不挖洞（整屏压暗的纯图文页）")]
        public string anchor;

        [Header("兜底矩形（屏幕归一化：x/y 左下为原点，w/h 为宽高比例）\n" +
                "只在 anchor 留空或找不到时使用；w 或 h 为 0 视为没填")]
        public Rect area = new Rect(0.35f, 0.35f, 0.3f, 0.3f);

        [Header("洞口形状与外扩边距（像素，1920×1080 基准）")]
        public VNTutorialHole shape = VNTutorialHole.RoundedRect;
        public float padding = 16f;

        [Header("圆角半径 / 边缘羽化宽度（像素）")]
        public float corner = 22f;
        public float feather = 18f;

        // ------------------------------------------------------------------
        // 文字（三语；En/Ja 留空回退中文，与 VNInterludeDef 同套语义）
        // ------------------------------------------------------------------

        [Header("标题（留空则不显示标题行）")]
        public string title;
        public string titleEn;
        public string titleJa;

        [Header("正文")]
        [TextArea(2, 6)] public string body;
        [TextArea(2, 6)] public string bodyEn;
        [TextArea(2, 6)] public string bodyJa;

        [Header("配图（可留空）与显示高度（像素）")]
        public Sprite image;
        public float imageHeight = 220f;

        [Header("卡片位置（Custom 时用 cardPos：归一化、左下原点、卡片中心；(0.5,0.5) = 屏幕正中）")]
        public VNTutorialCardSpot card = VNTutorialCardSpot.Auto;
        public Vector2 cardPos = new Vector2(0.5f, 0.5f);

        [Header("卡片尺寸覆盖（0 = 用整篇默认）：宽度 px（1920 基准）/ 整体缩放（字号·边距·配图一起变）")]
        public float cardWidth = 0f;
        public float cardScale = 0f;

        [Header("这一步出现时放的音效 id（须在 SE 库登记；留空 = 不放）")]
        public string se;

        public string ResolveTitle() => VNTutorialDef.Pick(title, titleEn, titleJa);
        public string ResolveBody() => VNTutorialDef.Pick(body, bodyEn, bodyJa);

        /// <summary>兜底矩形有没有填（宽高都 &gt; 0 才算）</summary>
        public bool HasArea => area.width > 0.0001f && area.height > 0.0001f;
    }

    /// <summary>
    /// 一篇教程的全部内容。
    ///
    /// 【它解决什么】
    /// 新玩家第一次进某个界面/小游戏时：压暗全屏 → 只把要讲的那块抠出来 →
    /// 旁边一张图文卡片 → 点一下讲下一条。讲解期间**整个玩法冻结**
    /// （靠 <see cref="VNPause"/>，不是 Time.timeScale，理由见那边的注释）。
    ///
    /// 【为什么做成资产而不是写在剧本里】
    /// 教程天然绑的是「某个模块/某块 UI」而不是「某段剧情」——羽毛球的教学
    /// 跟第几章无关。而且文字要三语（.vn.txt 只写中文）、配图是引用、
    /// 同一篇可能在多处触发。写进剧本行里三处都要改。
    ///
    /// 【怎么用】
    ///   1. 右键 Create → VN → Tutorial Def 建资产，填 id（剧本引用名，可中文）
    ///   2. 登记进 VNGameConfig 的「教程库」
    ///   3. 剧本里：tutorial 界面入门        （看过就自动跳过）
    ///      强制重看：tutorial 界面入门 force:on
    ///      模块首次自动播：在 event 行写 tutorial:羽毛球基础，
    ///      或在模块模板的 Inspector 上填 tutorialId
    ///
    /// 【不进存档】
    /// 「看过没有」是玩家的元知识，跟 CG 解锁同类：走全局 JSON
    /// （<see cref="VNTutorialSeen"/>），读旧档 / 开新周目都不该重看。
    /// 教程本身是一段一次性演出，播完什么都不留，所以也不进 VNSaveData。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Tutorial Def", fileName = "VNTutorialDef")]
    public class VNTutorialDef : ScriptableObject
    {
        [Header("剧本 / 模块引用的 id（可中文，如 羽毛球基础 / 界面入门）")]
        public string id;

        [Header("步骤（按顺序播；一步一张卡片）")]
        public List<VNTutorialStep> steps = new List<VNTutorialStep>();

        [Header("暗幕浓度（0 = 不压暗；洞外的画面按这个变暗）")]
        [Range(0f, 1f)] public float dim = 0.72f;

        [Header("洞口描边颜色（HDR，> 1 会被 Bloom 吃到发光）与宽度（像素）")]
        [ColorUsage(true, true)]
        public Color edgeColor = new Color(1.6f, 1.35f, 0.75f, 1f);
        public float edgeWidth = 3.5f;

        [Header("洞口描边呼吸（0 = 不呼吸）")]
        [Range(0f, 1f)] public float edgePulse = 0.45f;

        [Header("允许 ESC 一键跳过整篇（关掉则必须一步步看完）")]
        public bool allowSkip = true;

        [Header("看过一次就不再自动播（剧本 force:on 仍可强制重看）")]
        public bool once = true;

        [Header("卡片默认尺寸：宽度 px（0 = 播放器组件的 cardWidth / 皮肤 prefab 自带宽度）与整体缩放")]
        public float cardWidth = 0f;
        public float cardScale = 1f;

        /// <summary>这一步实际用的卡片宽度：步骤覆盖 → 整篇默认 → 0（交给播放器 / 皮肤自己定）</summary>
        public float ResolveCardWidth(VNTutorialStep step)
        {
            if (step != null && step.cardWidth > 0f) return step.cardWidth;
            return cardWidth > 0f ? cardWidth : 0f;
        }

        /// <summary>这一步实际用的整体缩放：步骤覆盖 → 整篇默认 → 1</summary>
        public float ResolveCardScale(VNTutorialStep step)
        {
            if (step != null && step.cardScale > 0f) return step.cardScale;
            return cardScale > 0f ? cardScale : 1f;
        }

        /// <summary>有效步骤数（空步骤——既没文字也没图——不算）</summary>
        public int StepCount
        {
            get
            {
                int n = 0;
                foreach (var s in steps)
                    if (s != null) n++;
                return n;
            }
        }

        internal static string Pick(string zh, string en, string ja)
        {
            switch (VNLocale.Language)
            {
                case VNLanguage.English: return string.IsNullOrEmpty(en) ? zh : en;
                case VNLanguage.Japanese: return string.IsNullOrEmpty(ja) ? zh : ja;
                default: return zh;
            }
        }
    }
}
