using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public class VNStatsHudEntrySkin : MonoBehaviour
    {
        public Image icon;
        public TMP_Text nameText;
        public TextMeshProUGUI valueText;
        public GameObject barRoot;
        public Image barFill;
        public bool IsValid => icon != null && nameText != null && valueText != null;
    }

    public class VNStatsHudSkin : VNSystemUiSkinBehaviour
    {
        public GameObject hudRoot;
        public RectTransform entryContainer;
        public VNStatsHudEntrySkin entryTemplate;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(hudRoot, "HUD 根", errors);
            Require(entryContainer, "HUD 属性容器", errors);
            Require(entryTemplate, "HUD 属性模板", errors);
            if (entryTemplate != null && !entryTemplate.IsValid) errors.Add("HUD 模板图标/名称/数值槽位");
        }
    }

    public class VNStatsPanelRowSkin : MonoBehaviour
    {
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text valueText;
        public GameObject barRoot;
        public Image barFill;
        public bool IsValid => nameText != null && valueText != null;
    }

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
