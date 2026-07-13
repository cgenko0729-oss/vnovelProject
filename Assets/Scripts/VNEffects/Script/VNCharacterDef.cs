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
