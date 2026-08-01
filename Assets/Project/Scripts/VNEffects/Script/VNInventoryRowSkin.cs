using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>背包道具行槽位（挂在行模板上）：icon/名字必需，数量与「装备中」标记可选。</summary>
    public class VNInventoryRowSkin : MonoBehaviour
    {
        public Button button;
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text countText;        // 可选：留空则数量拼进 nameText
        public GameObject equippedMark;   // 可选：装备中标记（默认模板为金色 E）
        public bool IsValid => button != null && icon != null && nameText != null;
    }
}
