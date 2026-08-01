using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 排程面板日程格行槽位（挂在格子模板上）：按钮/天数/行动名必需。
    /// background 可选：赋值后由 VNPlanModule 按空/已排状态切换 emptyColor/filledColor；
    /// 留空则皮肤自己的外观不被脚本改色（仅更新文字）。
    /// </summary>
    public class VNPlanSlotRowSkin : MonoBehaviour
    {
        public Button button;
        public TMP_Text dayText;        // 第 N 天
        public TMP_Text assignedText;   // 已排行动名（空格显示「休息」）
        public Image background;        // 可选：状态改色目标

        [Header("空格 / 已排 的背景色（background 赋值时生效）")]
        public Color emptyColor = new Color(1f, 1f, 1f, 0.05f);
        public Color filledColor = new Color(0.35f, 0.55f, 0.95f, 0.25f);

        public bool IsValid => button != null && dayText != null && assignedText != null;
    }
}
