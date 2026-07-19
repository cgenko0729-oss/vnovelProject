using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>CG 鉴赏页面与全屏浏览器的 prefab 槽位。</summary>
    public class VNCgGallerySkin : VNSystemUiSkinBehaviour
    {
        public GameObject panelRoot;
        public TMP_Text titleText;
        public TMP_Text progressText;
        public TMP_Text hintText;
        public Button closeButton;
        public Button backgroundCloseButton;
        public ScrollRect scroll;
        public RectTransform grid;
        public VNCgCellSkin cellTemplate;

        [Header("全屏浏览")]
        public GameObject viewerRoot;
        public Image viewerImage;
        public TMP_Text viewerCaption;
        public Button viewerCloseButton;
        public Button viewerPreviousButton;
        public Button viewerNextButton;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "面板根", errors);
            Require(titleText, "标题", errors);
            Require(progressText, "解锁进度", errors);
            Require(scroll, "ScrollRect", errors);
            Require(grid, "网格容器", errors);
            Require(cellTemplate, "CG 卡片模板", errors);
            if (cellTemplate != null && !cellTemplate.IsValid) errors.Add("CG 卡片模板完整槽位");
            Require(viewerRoot, "全屏查看器", errors);
            Require(viewerImage, "全屏图片", errors);
            Require(viewerCaption, "全屏图片标题", errors);
            Require(viewerCloseButton, "全屏关闭按钮", errors);
        }
    }
}
