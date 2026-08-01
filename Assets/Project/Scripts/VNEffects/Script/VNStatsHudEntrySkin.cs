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
}
