using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>开始菜单 prefab 的功能槽位；布局、图片和装饰完全由 prefab 决定。</summary>
    public class VNTitleMenuSkin : VNSystemUiSkinBehaviour
    {
        [Header("根与文字")]
        public CanvasGroup canvasGroup;
        public RectTransform titleAnimationTarget;
        public TMP_Text gameTitle;
        public TMP_Text versionText;
        public TMP_Text continueTimeText;

        [Header("固定按钮")]
        public Button startButton;
        public TMP_Text startLabel;
        public Button continueButton;
        public TMP_Text continueLabel;
        public Button loadButton;
        public TMP_Text loadLabel;
        public Button galleryButton;
        public TMP_Text galleryLabel;
        public Button configButton;
        public TMP_Text configLabel;
        public Button quitButton;
        public TMP_Text quitLabel;

        [Header("退出确认")]
        public GameObject quitConfirmRoot;
        public TMP_Text quitConfirmMessage;
        public Button quitConfirmButton;
        public TMP_Text quitConfirmLabel;
        public Button quitCancelButton;
        public TMP_Text quitCancelLabel;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(canvasGroup, "CanvasGroup", errors);
            Require(gameTitle, "游戏标题", errors);
            Require(startButton, "开始按钮", errors);
            Require(startLabel, "开始文字", errors);
            Require(continueButton, "继续按钮", errors);
            Require(continueLabel, "继续文字", errors);
            Require(loadButton, "读取按钮", errors);
            Require(loadLabel, "读取文字", errors);
            Require(galleryButton, "CG 按钮", errors);
            Require(galleryLabel, "CG 文字", errors);
            Require(configButton, "设置按钮", errors);
            Require(configLabel, "设置文字", errors);
            Require(quitButton, "退出按钮", errors);
            Require(quitLabel, "退出文字", errors);
            Require(quitConfirmRoot, "退出确认根", errors);
            Require(quitConfirmMessage, "退出确认文字", errors);
            Require(quitConfirmButton, "确认按钮", errors);
            Require(quitCancelButton, "取消按钮", errors);
        }
    }
}
