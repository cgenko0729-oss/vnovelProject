using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 背包界面槽位声明（挂 prefab 根；登记在 VNSystemUiSkinSet.inventoryPrefab）。
    /// 左侧道具列表 + 右侧 7 个装备栏 + 底部介绍区；右键菜单由 VNInventory 程序化弹出。
    /// 必需槽位缺失时整体退回程序化默认 UI。
    /// </summary>
    public class VNInventorySkin : VNSystemUiSkinBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public Button closeButton;             // 可选
        public Button backgroundCloseButton;   // 可选
        public TMP_Text hintText;              // 可选：操作提示

        [Header("道具列表（左侧）")]
        public RectTransform itemContent;
        public VNInventoryRowSkin rowTemplate;
        public TMP_Text emptyText;             // 可选：没有道具时显示

        [Header("装备栏（右侧，7 个部位一格都不能少）")]
        public VNInventorySlotSkin headSlot;
        public VNInventorySlotSkin faceSlot;
        public VNInventorySlotSkin upperBodySlot;
        public VNInventorySlotSkin handsSlot;
        public VNInventorySlotSkin lowerBodySlot;
        public VNInventorySlotSkin feetSlot;
        public VNInventorySlotSkin specialSlot;

        [Header("介绍区")]
        public TMP_Text detailText;

        public VNInventorySlotSkin Slot(VNEquipSlot slot)
        {
            switch (slot)
            {
                case VNEquipSlot.Head: return headSlot;
                case VNEquipSlot.Face: return faceSlot;
                case VNEquipSlot.UpperBody: return upperBodySlot;
                case VNEquipSlot.Hands: return handsSlot;
                case VNEquipSlot.LowerBody: return lowerBodySlot;
                case VNEquipSlot.Feet: return feetSlot;
                case VNEquipSlot.Special: return specialSlot;
                default: return null;
            }
        }

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "背包面板根", errors);
            Require(titleText, "背包标题", errors);
            Require(itemContent, "道具列表容器", errors);
            Require(rowTemplate, "道具行模板", errors);
            if (rowTemplate != null && !rowTemplate.IsValid) errors.Add("道具行按钮/图标/名字槽位");
            Require(detailText, "道具介绍文本", errors);
            for (int i = 1; i <= 7; i++)
            {
                var slot = Slot((VNEquipSlot)i);
                if (slot == null) errors.Add($"装备栏第 {i} 格");
                else if (!slot.IsValid) errors.Add($"装备栏第 {i} 格的按钮/部位名/图标槽位");
            }
        }
    }
}
