using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public class VNStatsPanelRowSkin : MonoBehaviour
    {
        public Image icon;
        public TMP_Text nameText;
        public TMP_Text valueText;
        public GameObject barRoot;
        public Image barFill;
        public bool IsValid => nameText != null && valueText != null;
    }
}
