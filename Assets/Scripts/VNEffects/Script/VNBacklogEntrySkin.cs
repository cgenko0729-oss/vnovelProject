using TMPro;
using UnityEngine;

namespace VNEffects
{
    /// <summary>纯文字 Backlog 条目模板。</summary>
    public class VNBacklogEntrySkin : MonoBehaviour
    {
        public TMP_Text speakerText;
        public TMP_Text bodyText;

        public bool IsValid => speakerText != null && bodyText != null;
    }
}
