using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 角色定义资产：剧本里用 id 引用角色，立绘表情在这里集中管理。
    /// 每个角色一个 .asset（Create → VN → Character Definition）。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Character Definition", fileName = "NewCharacter")]
    public class VNCharacterDef : ScriptableObject
    {
        [Tooltip("剧本中使用的角色 id（可以是中文，如 亚里沙）")]
        public string id;

        [Tooltip("对话框名牌上显示的名字")]
        public string displayName;

        [Tooltip("名牌底色")]
        public Color nameColor = new Color(0.45f, 0.3f, 0.75f, 0.9f);

        [System.Serializable]
        public class Expression
        {
            [Tooltip("表情名（剧本里 expr:xxx 或台词行 '角色 表情: ...' 引用）")]
            public string name;
            public Sprite sprite;
        }

        [Tooltip("表情立绘列表（第一个为默认表情）")]
        public List<Expression> expressions = new List<Expression>();

        [Header("眨眼")]
        [Tooltip("是否允许这个角色在默认表情时自动眨眼。关闭后即使配置了闭眼图也不会眨眼")]
        public bool enableBlink;

        [Tooltip("闭眼状态的完整全身立绘。应与默认表情使用相同画布尺寸、人物位置和 Pivot")]
        public Sprite blinkSprite;

        [Tooltip("两次眨眼之间的最短随机间隔（秒）")]
        [Min(0.1f)]
        public float blinkIntervalMin = 2.5f;

        [Tooltip("两次眨眼之间的最长随机间隔（秒）")]
        [Min(0.1f)]
        public float blinkIntervalMax = 5f;

        [Tooltip("每次闭眼保持时间（秒）")]
        [Range(0.03f, 0.5f)]
        public float blinkDuration = 0.1f;

        [Header("尺寸标定（解决素材构图不统一）")]
        [Tooltip("尺寸倍率：这个角色的显示高度 = 舞台统一高度 × 此值。\n" +
                 "素材四周留白多/角色显小 → 调大（如 1.15）；近景图显大 → 调小（如 0.85）")]
        [Range(0.3f, 2.5f)]
        public float sizeScale = 1f;

        [Tooltip("站位偏移（像素）：在 at:left/center/right 的标准位置上附加的偏移。\n" +
                 "素材脚底留白多导致角色偏高 → y 给负值往下压；构图偏左/右 → 用 x 修正")]
        public Vector2 positionOffset = Vector2.zero;

        [Header("对话框头像")]
        [Tooltip("这个角色说话时是否在对话框显示头像（另有剧本全局开关 portrait on/off）")]
        public bool showPortrait = true;

        [Tooltip("头像图列表（name 对应表情名；台词行带表情时优先匹配同名头像，否则用第一个）。\n" +
                 "留空 = 直接用表情立绘当头像，配合下方缩放/偏移在窗口里框出半身")]
        public List<Expression> portraits = new List<Expression>();

        [Tooltip("头像缩放：1 = 图片宽度正好填满头像窗口；调大后配合偏移可框出脸部特写")]
        [Range(0.2f, 6f)]
        public float portraitScale = 1f;

        [Tooltip("头像位置偏移（像素）：在窗口内平移图片，把想要的部位（脸）挪进窗口。\n" +
                 "图片默认顶边贴窗口顶边，y 给正值往上推、负值往下拉")]
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
