using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    public class VNStatsHudSkin : VNSystemUiSkinBehaviour
    {
        public GameObject hudRoot;
        public RectTransform entryContainer;
        public VNStatsHudEntrySkin entryTemplate;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(hudRoot, "HUD 根", errors);
            Require(entryContainer, "HUD 属性容器", errors);
            Require(entryTemplate, "HUD 属性模板", errors);
            if (entryTemplate != null && !entryTemplate.IsValid) errors.Add("HUD 模板图标/名称/数值槽位");
        }
    }
}
