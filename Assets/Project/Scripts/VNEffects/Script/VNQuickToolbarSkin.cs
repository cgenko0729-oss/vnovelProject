using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    public enum VNToolbarAction
    {
        Save, Load, QuickSave, QuickLoad, Auto, Skip, Backlog,
        Quest, Stats, Inventory, Gallery, Config, HideUi
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
