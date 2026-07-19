using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>CG 网格中的动态卡片模板。</summary>
    public class VNCgCellSkin : MonoBehaviour
    {
        public Button button;
        public Image thumbnail;
        public GameObject lockedRoot;
        public TMP_Text lockedLabel;
        public TMP_Text countBadge;
        public Graphic frameGraphic;
        public Color unlockedFrameColor = new Color(1f, 1f, 1f, 0.12f);
        public Color lockedFrameColor = new Color(1f, 1f, 1f, 0.05f);

        public bool IsValid => button != null && thumbnail != null && lockedRoot != null;
    }
}
