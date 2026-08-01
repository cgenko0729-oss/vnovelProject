using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
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
}
