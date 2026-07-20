using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 结果结算大弹窗（event result）皮肤槽位声明（挂 prefab 根；登记在
    /// VNSystemUiSkinSet.resultPopupPrefab）。只有 panelRoot 和 gradeText 必需，
    /// 其余全可选降级。判定冲条三槽（barRoot/barFill/percentText）齐全才播
    /// 悬念演出，否则直接揭晓大字。barFill 由脚本按 anchorMax.x 0→1 驱动
    /// （保留皮肤作者设置的纵向锚点），须做成 barRoot 内左锚定的填充条。
    /// percentText 建议放 barRoot 下，随冲条一起淡出。
    /// </summary>
    public class VNResultPopupSkin : VNSystemUiSkinBehaviour
    {
        public RectTransform panelRoot;    // 弹入缩放动画的目标
        public Image panelBackground;      // 可选：赋值则按等级套 panelTint 底色
        public TMP_Text gradeText;         // 等级大字（FAIL…/OK/GOOD!/GREAT!!）
        public TMP_Text gradeLocalText;    // 可选：本地化等级小字
        public TMP_Text titleText;         // 可选：行动名（剧本 title:）
        public TMP_Text subText;           // 可选：补充说明（剧本 sub:）
        public TMP_Text hintText;          // 可选：继续提示（呼吸闪烁）

        [Header("判定冲条（三项都齐才播悬念演出）")]
        public GameObject barRoot;
        public RectTransform barFill;
        public TMP_Text percentText;

        [Header("星光爆发原点（留空 = 等级大字位置）")]
        public RectTransform burstOrigin;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "结算面板根", errors);
            Require(gradeText, "等级大字", errors);
        }
    }
}
