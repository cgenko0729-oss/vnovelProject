using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>单个存档卡模板槽位。模板对象应在 prefab 中保持关闭。</summary>
    public class VNSaveSlotSkin : MonoBehaviour
    {
        public Button button;
        public RawImage thumbnail;
        public TMP_Text slotNumber;
        public TMP_Text savedAt;
        public TMP_Text lastLine;
        public Graphic cardGraphic;
        public Color occupiedColor = new Color(0.055f, 0.075f, 0.13f, 0.96f);
        public Color emptyColor = new Color(0.025f, 0.035f, 0.065f, 0.92f);
        public Color occupiedNumberColor = new Color(1f, 0.78f, 0.38f, 1f);
        public Color emptyNumberColor = new Color(0.55f, 0.6f, 0.72f, 1f);

        public bool IsValid => button != null && thumbnail != null && slotNumber != null &&
                               savedAt != null && lastLine != null;
    }

    /// <summary>存读档整页 prefab 槽位；存档数量和内容仍由 VNSaveLoadPanel 管理。</summary>
    public class VNSaveLoadSkin : VNSystemUiSkinBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public TMP_Text hintText;
        public Button saveTab;
        public TMP_Text saveTabLabel;
        public Button loadTab;
        public TMP_Text loadTabLabel;
        public Button closeButton;
        public RectTransform slotContainer;
        public VNSaveSlotSkin slotTemplate;

        [Header("确认弹窗")]
        public GameObject confirmRoot;
        public TMP_Text confirmMessage;
        public Button confirmYes;
        public TMP_Text confirmYesLabel;
        public Button confirmNo;
        public TMP_Text confirmNoLabel;

        [Header("页签颜色")]
        public Color selectedTabColor = new Color(1f, 0.78f, 0.38f, 1f);
        public Color normalTabColor = new Color(0.11f, 0.14f, 0.22f, 1f);

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "面板根", errors);
            Require(titleText, "标题", errors);
            Require(hintText, "提示文字", errors);
            Require(saveTab, "保存页签", errors);
            Require(loadTab, "读取页签", errors);
            Require(closeButton, "关闭按钮", errors);
            Require(slotContainer, "存档容器", errors);
            Require(slotTemplate, "存档卡模板", errors);
            if (slotTemplate != null && !slotTemplate.IsValid) errors.Add("存档卡模板完整槽位");
            Require(confirmRoot, "确认弹窗根", errors);
            Require(confirmMessage, "确认提示", errors);
            Require(confirmYes, "确认按钮", errors);
            Require(confirmNo, "取消按钮", errors);
        }
    }
}
