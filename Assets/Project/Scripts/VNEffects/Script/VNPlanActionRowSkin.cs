using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 排程面板候选行动行槽位（挂在行模板上）：按钮/名字必需；
    /// 图标可选（行动没配图标时保留模板占位图），收益文案槽可选
    /// （留空则收益拼进 nameText 的富文本小字，与程序化默认一致）。
    /// </summary>
    public class VNPlanActionRowSkin : MonoBehaviour
    {
        public Button button;
        public TMP_Text nameText;
        public TMP_Text gainText;   // 可选：预期收益/消耗文案独立槽
        public Image icon;          // 可选

        public bool IsValid => button != null && nameText != null;
    }
}
