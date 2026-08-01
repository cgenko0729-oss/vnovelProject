using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public class VNStatsPanelSkin : VNSystemUiSkinBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public Button closeButton;
        public Button backgroundCloseButton;
        public RectTransform content;
        public VNStatsPanelRowSkin rowTemplate;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "属性面板根", errors);
            Require(titleText, "属性面板标题", errors);
            Require(content, "属性列表容器", errors);
            Require(rowTemplate, "属性行模板", errors);
            if (rowTemplate != null && !rowTemplate.IsValid) errors.Add("属性行名称/数值槽位");
        }
    }
}
