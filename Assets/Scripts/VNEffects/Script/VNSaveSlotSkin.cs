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
}
