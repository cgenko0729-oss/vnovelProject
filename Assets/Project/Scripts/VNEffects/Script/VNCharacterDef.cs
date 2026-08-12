using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 眨眼的实现方式，二选一（两条运行时代码路径互不干扰）。
    /// </summary>
    public enum VNBlinkMode
    {
        [InspectorName("整张替换（换成完整闭眼立绘）")]
        FullSprite = 0,
        [InspectorName("分层叠加（只在眼部叠一张透明图）")]
        Overlay = 1,
    }

    /// <summary>
    /// 角色定义资产：剧本里用 id 引用角色，立绘表情在这里集中管理。
    /// 每个角色一个 .asset（Create → VN → Character Definition）。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Character Definition", fileName = "NewCharacter")]
    public class VNCharacterDef : ScriptableObject
    {
        [Header("剧本中使用的角色 id（可以是中文，如 亚里沙）")]
        public string id;

        [Header("对话框名牌上显示的名字")]
        public string displayName;

        [Header("名牌译名（本地化）：英文；留空 = 回退上面的 displayName")]
        public string displayNameEn;

        [Header("名牌译名（本地化）：日文；留空 = 回退 displayName")]
        public string displayNameJa;

        [Header("名牌底色")]
        public Color nameColor = new Color(0.45f, 0.3f, 0.75f, 0.9f);

        [Header("名牌字色自定义（关 = 由上面的 nameColor 自动推算渐变）")]
        public bool overrideNameplateColors;
        [Header("名牌字面 · 渐变上色")]
        public Color nameplateTopColor = new Color(1f, 0.45f, 0.42f, 1f);
        [Header("名牌字面 · 渐变下色")]
        public Color nameplateBottomColor = new Color(0.78f, 0.1f, 0.16f, 1f);
        [Header("名牌描边色")]
        public Color nameplateOutlineColor = Color.white;

        /// <summary>
        /// 取本角色的名牌配色。没勾 overrideNameplateColors 时由 nameColor 自动推算
        /// （提亮当上色、压暗当下色、描边给白）——这样存量角色资产一个字都不用改，
        /// 就能各自拿到一套和自己底色同源的名牌配色。
        /// </summary>
        public void GetNameplateColors(out Color top, out Color bottom, out Color outline)
        {
            if (overrideNameplateColors)
            {
                top = nameplateTopColor;
                bottom = nameplateBottomColor;
                outline = nameplateOutlineColor;
                return;
            }

            Color.RGBToHSV(nameColor, out float h, out float s, out float v);
            // 字面要压得住背景，所以整体往亮里推；上下拉开明度差做出渐变
            top = Color.HSVToRGB(h, Mathf.Clamp01(s * 0.82f), Mathf.Clamp01(v * 1.25f + 0.32f));
            bottom = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f + 0.05f), Mathf.Clamp01(v * 0.9f + 0.05f));
            top.a = 1f;
            bottom.a = 1f;
            outline = Color.white;
        }

        /// <summary>当前语言的名牌显示名（译名留空回退中文 displayName）。
        /// 注意剧本引用的角色 id 永远不翻译。</summary>
        public string LocalizedDisplayName
        {
            get
            {
                switch (VNLocale.Language)
                {
                    case VNLanguage.English:
                        return string.IsNullOrEmpty(displayNameEn) ? displayName : displayNameEn;
                    case VNLanguage.Japanese:
                        return string.IsNullOrEmpty(displayNameJa) ? displayName : displayNameJa;
                    default:
                        return displayName;
                }
            }
        }

        [System.Serializable]
        public class Expression
        {
            [Header("表情名（剧本里 expr:xxx 或台词行 '角色 表情: ...' 引用）")]
            public string name;
            [Header("该表情的立绘图")]
            public Sprite sprite;
        }

        [Header("表情立绘列表（第一个为默认表情）")]
        public List<Expression> expressions = new List<Expression>();

        [Header("眨眼")]
        [Header("是否允许这个角色在默认表情时自动眨眼。关闭后即使配置了闭眼图也不会眨眼")]
        public bool enableBlink;

        [Header("眨眼方式：整张替换用下面的完整闭眼立绘；分层叠加只在眼部叠一张透明图（同口型的做法）")]
        public VNBlinkMode blinkMode = VNBlinkMode.FullSprite;

        [Header("【整张替换用】闭眼状态的完整全身立绘。应与默认表情使用相同画布尺寸、人物位置和 Pivot")]
        public Sprite blinkSprite;

        [Header("【分层叠加用】透明背景的闭眼局部图。保留与默认立绘完全相同的整张透明画布，" +
                "只在眼部留下像素；该块像素要能盖住原立绘睁开的眼睛")]
        public Sprite blinkOverlaySprite;

        [Header("两次眨眼之间的最短随机间隔（秒）")]
        [Min(0.1f)]
        public float blinkIntervalMin = 2.5f;

        [Header("两次眨眼之间的最长随机间隔（秒）")]
        [Min(0.1f)]
        public float blinkIntervalMax = 5f;

        [Header("每次闭眼保持时间（秒）")]
        [Range(0.03f, 0.5f)]
        public float blinkDuration = 0.1f;

        [Header("说话口型")]
        [Header("是否允许这个角色在对白期间自动切换张嘴图。关闭后始终使用原立绘的闭嘴状态")]
        public bool enableMouthFlap;

        [Header("透明背景的张嘴局部图。建议保留与默认立绘完全相同的整张透明画布，只在嘴部位置留下像素")]
        public Sprite openMouthSprite;

        [Header("开启后仅默认表情使用口型，避免其他构图不同的表情出现嘴巴错位")]
        public bool mouthDefaultExpressionOnly = true;

        [Header("闭嘴/张嘴切换的最短随机间隔（秒）")]
        [Min(0.03f)]
        public float mouthIntervalMin = 0.08f;

        [Header("闭嘴/张嘴切换的最长随机间隔（秒）")]
        [Min(0.03f)]
        public float mouthIntervalMax = 0.16f;

        [System.Serializable]
        public class MarkOverride
        {
            [Header("漫符名（英文正名 sweat/anger/... 或中文别名 汗/怒/...）")]
            public string name;
            [Header("替代程序化生成的自定义符号图（留空 = 用程序化生成的）")]
            public Sprite sprite;
        }

        [Header("漫符（mark 命令：汗滴 / 井字怒气 / 感叹号 …）")]
        [Header("默认锚点：相对立绘尺寸的归一化偏移，(0,0) = 立绘中心，(0.5,0.5) = 右上角。" +
                "默认值大致是全身立绘的头部右上；剧本可用 pos:x,y 临时覆盖")]
        public Vector2 markAnchor = new Vector2(0.2f, 0.36f);

        [Header("漫符尺寸倍率：基准边长 = 立绘显示高度 × 0.15 × 此值（再乘剧本的 size:）")]
        [Range(0.2f, 3f)]
        public float markScale = 1f;

        [Header("自定义符号图（按需覆盖，不填的种类继续用程序化生成的图）")]
        public List<MarkOverride> markSprites = new List<MarkOverride>();

        [Header("尺寸标定（解决素材构图不统一）")]
        [Header("尺寸倍率：显示高度 = 舞台统一高度 × 此值（留白多显小→调大，近景显大→调小）")]
        [Range(0.3f, 2.5f)]
        public float sizeScale = 1f;

        [Header("站位偏移（像素）：脚底留白偏高→y 负值下压；构图偏左右→x 修正")]
        public Vector2 positionOffset = Vector2.zero;

        [Header("立绘 Z 轴旋转偏移（度）：用于修正素材本身的倾斜；正值逆时针旋转")]
        public float rotationZOffset = 0f;

        [Header("对话框头像")]
        [Header("这个角色说话时是否在对话框显示头像（另有剧本全局开关 portrait on/off）")]
        public bool showPortrait = true;

        [Header("头像图列表（name 对应表情名；留空 = 用表情立绘配合缩放/偏移框出半身）")]
        public List<Expression> portraits = new List<Expression>();

        [Header("头像缩放：1 = 图片宽度正好填满头像窗口；调大后配合偏移可框出脸部特写")]
        [Range(0.2f, 6f)]
        public float portraitScale = 1f;

        [Header("头像位置偏移（像素）：把脸挪进窗口；y 正值往上推、负值往下拉")]
        public Vector2 portraitOffset = Vector2.zero;

        /// <summary>按表情名取立绘；空/找不到时回退到第一个并告警</summary>
        public Sprite GetSprite(string expressionName)
        {
            if (expressions.Count == 0)
            {
                Debug.LogError($"[VNScript] 角色 {id} 没有配置任何表情立绘", this);
                return null;
            }
            if (string.IsNullOrEmpty(expressionName))
                return expressions[0].sprite;

            foreach (var e in expressions)
                if (e.name == expressionName)
                    return e.sprite;

            Debug.LogWarning($"[VNScript] 角色 {id} 没有表情「{expressionName}」，使用默认表情", this);
            return expressions[0].sprite;
        }

        /// <summary>当前眨眼方式实际会用到的那张图（没开眨眼或没配图 = null）。</summary>
        public Sprite ActiveBlinkSprite
        {
            get
            {
                if (!enableBlink) return null;
                return blinkMode == VNBlinkMode.Overlay ? blinkOverlaySprite : blinkSprite;
            }
        }

        /// <summary>第一张立绘是项目约定的默认表情。</summary>
        public Sprite DefaultSprite => expressions != null && expressions.Count > 0
            ? expressions[0].sprite : null;

        /// <summary>
        /// 判断请求最终是否显示默认表情。空表情与找不到后回退的表情都视为默认表情。
        /// </summary>
        public bool IsDefaultExpression(string expressionName)
        {
            if (expressions == null || expressions.Count == 0) return false;
            if (string.IsNullOrEmpty(expressionName)) return true;

            for (int i = 0; i < expressions.Count; i++)
                if (expressions[i].name == expressionName)
                    return i == 0;

            return true;
        }

        /// <summary>
        /// 按表情名取对话框头像；未配置头像列表时回退用表情立绘
        /// （头像窗口有裁切，配合 portraitScale/portraitOffset 可框出半身）。
        /// 返回 null = 该角色不显示头像。
        /// </summary>
        public Sprite GetPortrait(string expressionName)
        {
            if (!showPortrait) return null;
            if (portraits.Count > 0)
            {
                if (!string.IsNullOrEmpty(expressionName))
                    foreach (var p in portraits)
                        if (p.name == expressionName && p.sprite != null)
                            return p.sprite;
                return portraits[0].sprite;
            }
            return expressions.Count > 0 ? GetSprite(expressionName) : null;
        }
    }
}
