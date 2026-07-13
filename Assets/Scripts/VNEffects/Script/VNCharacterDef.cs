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

        [Header("尺寸标定（解决素材构图不统一）")]
        [Tooltip("尺寸倍率：这个角色的显示高度 = 舞台统一高度 × 此值。\n" +
                 "素材四周留白多/角色显小 → 调大（如 1.15）；近景图显大 → 调小（如 0.85）")]
        [Range(0.3f, 2.5f)]
        public float sizeScale = 1f;

        [Tooltip("站位偏移（像素）：在 at:left/center/right 的标准位置上附加的偏移。\n" +
                 "素材脚底留白多导致角色偏高 → y 给负值往下压；构图偏左/右 → 用 x 修正")]
        public Vector2 positionOffset = Vector2.zero;

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
    }
}
