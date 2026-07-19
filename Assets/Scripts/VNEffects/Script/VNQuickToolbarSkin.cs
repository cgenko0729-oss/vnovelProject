using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public enum VNToolbarAction
    {
        Save, Load, QuickSave, QuickLoad, Auto, Skip, Backlog,
        Quest, Stats, Inventory, Gallery, Config, HideUi
    }

    /// <summary>挂在自定义工具条按钮上，声明按钮动作与可选文字/激活图形。</summary>
    public class VNToolbarActionSlot : MonoBehaviour
    {
        public VNToolbarAction action;
        public Button button;
        public TMP_Text label;
        public Graphic activeGraphic;
        public Color normalColor = new Color(0.045f, 0.06f, 0.105f, 0.94f);
        public Color activeColor = new Color(0.92f, 0.61f, 0.18f, 0.98f);

        public void SetActiveState(bool active)
        {
            if (activeGraphic != null) activeGraphic.color = active ? activeColor : normalColor;
        }
    }

    /// <summary>快捷功能条 prefab 根。按钮顺序和数量完全由子级 ActionSlot 决定。</summary>
    public class VNQuickToolbarSkin : VNSystemUiSkinBehaviour
    {
        public RectTransform root;

        public VNToolbarActionSlot[] Slots =>
            GetComponentsInChildren<VNToolbarActionSlot>(true);

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(root, "工具条根", errors);
            var slots = Slots;
            if (slots == null || slots.Length == 0) errors.Add("至少一个动作按钮");
            else
                foreach (var slot in slots)
                    if (slot == null || slot.button == null)
                    {
                        errors.Add("动作按钮的 Button");
                        break;
                    }
        }
    }
}
