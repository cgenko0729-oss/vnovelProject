using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>设置菜单 prefab 槽位。Slider 的背景、填充和手柄样式全部在 prefab 中编辑。</summary>
    public class VNConfigPanelSkin : VNSystemUiSkinBehaviour
    {
        [Header("根")]
        public GameObject panelRoot;
        public Button backgroundCloseButton;
        public Button closeButton;
        public TMP_Text titleText;
        public TMP_Text hintText;

        [Header("音量与文字速度")]
        public TMP_Text bgmLabel;
        public Slider bgmSlider;
        public TMP_Text bgmValue;
        public TMP_Text seLabel;
        public Slider seSlider;
        public TMP_Text seValue;
        public TMP_Text voiceLabel;
        public Slider voiceSlider;
        public TMP_Text voiceValue;
        public TMP_Text textSpeedLabel;
        public Slider textSpeedSlider;
        public TMP_Text textSpeedValue;

        [Header("语言")]
        public TMP_Text languageLabel;
        public Button chineseButton;
        public TMP_Text chineseLabel;
        public Button englishButton;
        public TMP_Text englishLabel;
        public Button japaneseButton;
        public TMP_Text japaneseLabel;
        public Color selectedLanguageColor = new Color(0.92f, 0.61f, 0.18f, 0.98f);

        [Header("显示模式")]
        public Button fullscreenButton;
        public TMP_Text fullscreenLabel;

        public override void CollectValidationErrors(List<string> errors)
        {
            Require(panelRoot, "面板根", errors);
            Require(closeButton, "关闭按钮", errors);
            Require(titleText, "标题", errors);
            Require(bgmSlider, "BGM Slider", errors);
            Require(bgmValue, "BGM 数值", errors);
            Require(seSlider, "SE Slider", errors);
            Require(seValue, "SE 数值", errors);
            Require(voiceSlider, "Voice Slider", errors);
            Require(voiceValue, "Voice 数值", errors);
            Require(textSpeedSlider, "文字速度 Slider", errors);
            Require(textSpeedValue, "文字速度数值", errors);
            Require(chineseButton, "中文按钮", errors);
            Require(englishButton, "英文按钮", errors);
            Require(japaneseButton, "日文按钮", errors);
            Require(fullscreenButton, "显示模式按钮", errors);
            Require(fullscreenLabel, "显示模式文字", errors);
        }
    }
}
