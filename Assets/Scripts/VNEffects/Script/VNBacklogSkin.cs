using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>Backlog 页面 prefab 槽位。</summary>
    public class VNBacklogSkin : VNSystemUiSkinBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public Button closeButton;
        public Button backgroundCloseButton;
        public ScrollRect scroll;
        public RectTransform content;
        public VNBacklogEntrySkin entryTemplate;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "面板根", errors);
            Require(titleText, "标题", errors);
            Require(scroll, "ScrollRect", errors);
            Require(content, "内容容器", errors);
            Require(entryTemplate, "台词条目模板", errors);
            if (entryTemplate != null && !entryTemplate.IsValid) errors.Add("条目角色名和正文槽位");
        }
    }
}
