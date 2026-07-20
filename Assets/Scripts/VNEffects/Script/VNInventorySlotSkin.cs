using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>背包装备栏单格槽位：部位名/图标/按钮必需，装备名与空格提示可选。</summary>
    public class VNInventorySlotSkin : MonoBehaviour
    {
        public Button button;
        public TMP_Text slotLabel;        // 部位名（头部/脸部/…）
        public Image icon;                // 装备图标（空槽时隐藏或淡显）
        public TMP_Text itemNameText;     // 可选：装备名
        public GameObject emptyMark;      // 可选：空槽提示（有装备时隐藏）
        public bool IsValid => button != null && slotLabel != null && icon != null;
    }
}
