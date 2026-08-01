using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 排程面板（event plan）皮肤槽位声明（挂 prefab 根；登记在
    /// VNSystemUiSkinSet.planPrefab）。左列候选行动、右列日程格均为
    /// 模板克隆制：模板行保持在容器内、导出时 SetActive(false)，
    /// 运行时按行动数/格数克隆。必需槽位缺失时整体退回程序化默认 UI。
    /// </summary>
    public class VNPlanSkin : VNSystemUiSkinBehaviour
    {
        public RectTransform panelRoot;   // 弹入缩放动画的目标
        public TMP_Text titleText;

        [Header("候选行动列（左）")]
        public RectTransform actionContent;
        public VNPlanActionRowSkin actionTemplate;

        [Header("日程格列（右）")]
        public RectTransform slotContent;
        public VNPlanSlotRowSkin slotTemplate;

        [Header("底部按钮（label 留空则不改按钮文字）")]
        public Button resetButton;
        public Button confirmButton;
        public TMP_Text resetLabel;     // 可选
        public TMP_Text confirmLabel;   // 可选

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "排程面板根", errors);
            Require(titleText, "标题", errors);
            Require(actionContent, "行动列容器", errors);
            Require(actionTemplate, "行动行模板", errors);
            if (actionTemplate != null && !actionTemplate.IsValid)
                errors.Add("行动行的按钮/名字槽位");
            Require(slotContent, "日程格容器", errors);
            Require(slotTemplate, "日程格模板", errors);
            if (slotTemplate != null && !slotTemplate.IsValid)
                errors.Add("日程格的按钮/天数/行动名槽位");
            Require(resetButton, "重置按钮", errors);
            Require(confirmButton, "确定按钮", errors);
        }
    }
}
