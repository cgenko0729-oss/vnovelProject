using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>系统设置面板：绑定 Prefab 控件，并把音量、文字速度、语言与全屏设置保存到 PlayerPrefs。</summary>
    public class VNConfigPanel : MonoBehaviour
    {
        const string BgmKey = "VN.Config.BgmVolume";
        const string SeKey = "VN.Config.SeVolume";
        const string VoiceKey = "VN.Config.VoiceVolume";
        const string TextSpeedKey = "VN.Config.TextSpeed";
        const string FullscreenKey = "VN.Config.Fullscreen";
        const string WheelBacklogKey = "VN.Config.WheelBacklog";

        VNScriptRunner _runner;
        VNStage _stage;
        GameObject _canvasGo;
        GameObject _panel;
        TMP_Text _fullscreenLabel;
        TMP_Text _wheelBacklogLabel;
        TMP_Text _tutorialHintsLabel;
        bool _open;
        bool _settingsApplied;

        public bool IsOpen => _open;

        /// <summary>
        /// 滚轮上滑是否打开回想。默认开（Galgame 惯例），关掉后只剩 H 键。
        /// 静态是因为 VNScriptRunner 每帧要读它，而设置面板未必存在于所有场景。
        /// </summary>
        public static bool WheelOpensBacklog
        {
            get => PlayerPrefs.GetInt(WheelBacklogKey, 1) != 0;
            set => PlayerPrefs.SetInt(WheelBacklogKey, value ? 1 : 0);
        }

        public void Initialize(VNScriptRunner runner, VNStage stage)
        {
            _runner = runner;
            _stage = stage;
            ApplySavedSettings();
        }

        public void Open()
        {
            Build();
            RefreshValues();
            _panel.SetActive(true);
            _open = true;
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (_panel != null) _panel.SetActive(false);
            PlayerPrefs.Save();
            _runner?.OnConfigPanelClosed();
        }

        void ApplySavedSettings()
        {
            if (_settingsApplied || _stage == null) return;
            _settingsApplied = true;
            if (_stage.vnAudio != null)
            {
                if (PlayerPrefs.HasKey(BgmKey))
                    _stage.vnAudio.SetVolume("bgm", PlayerPrefs.GetFloat(BgmKey));
                if (PlayerPrefs.HasKey(SeKey))
                    _stage.vnAudio.SetVolume("se", PlayerPrefs.GetFloat(SeKey));
                if (PlayerPrefs.HasKey(VoiceKey))
                    _stage.vnAudio.SetVolume("voice", PlayerPrefs.GetFloat(VoiceKey));
            }
            if (_stage.dialogue != null && PlayerPrefs.HasKey(TextSpeedKey))
                _stage.dialogue.SetTextSpeed(PlayerPrefs.GetFloat(TextSpeedKey));
            if (PlayerPrefs.HasKey(FullscreenKey))
                Screen.fullScreen = PlayerPrefs.GetInt(FullscreenKey) != 0;
        }

        void Build()
        {
            if (_panel != null) return;
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = _canvasGo = new GameObject("VNConfigCanvas", typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 950;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.configPanelPrefab);
            var customSkin = VNSystemUiSkinUtility.Instantiate<VNConfigPanelSkin>(
                skinPrefab, canvasGo.transform, "VNConfigPanel");
            if (customSkin == null)
                throw new InvalidOperationException("Config panel prefab is missing or invalid.");

            BindCustomSkin(customSkin);
            _panel.SetActive(false);
        }


        void BindCustomSkin(VNConfigPanelSkin skin)
        {
            _panel = skin.panelRoot;
            _fullscreenLabel = skin.fullscreenLabel;

            skin.titleText.text = VNLocale.T("config.title");
            if (skin.hintText != null) skin.hintText.text = VNLocale.T("config.hint");
            if (skin.bgmLabel != null) skin.bgmLabel.text = VNLocale.T("config.bgm");
            if (skin.seLabel != null) skin.seLabel.text = VNLocale.T("config.se");
            if (skin.voiceLabel != null) skin.voiceLabel.text = VNLocale.T("config.voice");
            if (skin.textSpeedLabel != null) skin.textSpeedLabel.text = VNLocale.T("config.textSpeed");
            if (skin.languageLabel != null) skin.languageLabel.text = VNLocale.T("config.language");

            BindButton(skin.closeButton, Close);
            if (skin.backgroundCloseButton != null) BindButton(skin.backgroundCloseButton, Close);

            float bgm = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.bgmVolume : 0.75f;
            float se = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.seVolume : 1f;
            float voice = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.voiceVolume : 1f;
            float speed = _stage != null && _stage.dialogue != null ? _stage.dialogue.TextSpeed : 18f;

            BindSlider(skin.bgmSlider, skin.bgmValue, 0f, 1f, bgm, v => $"{v:P0}", value =>
            {
                _stage?.vnAudio?.SetVolume("bgm", value);
                PlayerPrefs.SetFloat(BgmKey, value);
            });
            BindSlider(skin.seSlider, skin.seValue, 0f, 1f, se, v => $"{v:P0}", value =>
            {
                _stage?.vnAudio?.SetVolume("se", value);
                PlayerPrefs.SetFloat(SeKey, value);
            });
            BindSlider(skin.voiceSlider, skin.voiceValue, 0f, 1f, voice, v => $"{v:P0}", value =>
            {
                _stage?.vnAudio?.SetVolume("voice", value);
                PlayerPrefs.SetFloat(VoiceKey, value);
            });
            BindSlider(skin.textSpeedSlider, skin.textSpeedValue, 8f, 60f, speed,
                v => VNLocale.T("config.textSpeedValue", v), value =>
                {
                    _stage?.dialogue?.SetTextSpeed(value);
                    PlayerPrefs.SetFloat(TextSpeedKey, value);
                });

            BindLanguageButton(skin.chineseButton, skin.chineseLabel, VNLanguage.Chinese, skin.selectedLanguageColor);
            BindLanguageButton(skin.englishButton, skin.englishLabel, VNLanguage.English, skin.selectedLanguageColor);
            BindLanguageButton(skin.japaneseButton, skin.japaneseLabel, VNLanguage.Japanese, skin.selectedLanguageColor);
            BindButton(skin.fullscreenButton, ToggleFullscreen);
            UpdateFullscreenLabel();

            // 槽位可留空：老的皮肤 prefab 没有这个按钮，缺了就只是设置面板里没这一项，
            // 开关本身照常生效（默认开），不影响面板其它部分。
            _wheelBacklogLabel = skin.wheelBacklogLabel;
            if (skin.wheelBacklogButton != null)
                BindButton(skin.wheelBacklogButton, ToggleWheelBacklog);
            UpdateWheelBacklogLabel();

            _tutorialHintsLabel = skin.tutorialHintsLabel;
            if (skin.tutorialHintsButton != null)
                BindButton(skin.tutorialHintsButton, ToggleTutorialHints);
            if (skin.tutorialResetButton != null)
                BindButton(skin.tutorialResetButton, ResetTutorials);
            if (skin.tutorialResetLabel != null)
                skin.tutorialResetLabel.text = VNLocale.T("config.tutorialReset");
            UpdateTutorialHintsLabel();
        }

        static void BindSlider(Slider slider, TMP_Text valueText, float min, float max, float value,
            Func<float, string> format, Action<float> changed)
        {
            var hitGraphic = slider.GetComponent<Graphic>();
            if (hitGraphic == null)
            {
                var hitImage = slider.gameObject.AddComponent<Image>();
                hitImage.color = Color.clear;
                hitGraphic = hitImage;
            }
            hitGraphic.raycastTarget = true;

            slider.minValue = min;
            slider.maxValue = max;
            slider.SetValueWithoutNotify(value);
            valueText.text = format(value);
            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(v =>
            {
                valueText.text = format(v);
                changed(v);
            });
        }

        void BindLanguageButton(Button button, TMP_Text label, VNLanguage language, Color activeColor)
        {
            if (label != null) label.text = VNLocale.DisplayName(language);
            BindButton(button, () => SetLanguage(language));
            if (VNLocale.Language == language && button.targetGraphic != null)
                button.targetGraphic.color = activeColor;
        }

        static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void RefreshValues()
        {
            UpdateFullscreenLabel();
            UpdateWheelBacklogLabel();
            UpdateTutorialHintsLabel();
        }

        /// <summary>关掉后自动触发的教程一律不播；剧本 force:on 点名要讲的仍然会播</summary>
        void ToggleTutorialHints()
        {
            VNTutorialSeen.Enabled = !VNTutorialSeen.Enabled;
            UpdateTutorialHintsLabel();
        }

        void UpdateTutorialHintsLabel()
        {
            if (_tutorialHintsLabel == null) return;
            _tutorialHintsLabel.text = VNLocale.T(VNTutorialSeen.Enabled
                ? "config.tutorialHintsOn"
                : "config.tutorialHintsOff");
        }

        /// <summary>清空「看过了」的全局记录：所有教程都会重新弹一次</summary>
        void ResetTutorials()
        {
            VNTutorialSeen.ResetAll();
            VNToast.Show(VNLocale.T("config.tutorialResetDone"));
        }

        void ToggleWheelBacklog()
        {
            WheelOpensBacklog = !WheelOpensBacklog;
            UpdateWheelBacklogLabel();
        }

        void UpdateWheelBacklogLabel()
        {
            if (_wheelBacklogLabel == null) return;
            _wheelBacklogLabel.text = VNLocale.T(WheelOpensBacklog
                ? "config.wheelBacklogOn"
                : "config.wheelBacklogOff");
        }

        void ToggleFullscreen()
        {
            Screen.fullScreen = !Screen.fullScreen;
            PlayerPrefs.SetInt(FullscreenKey, Screen.fullScreen ? 1 : 0);
            UpdateFullscreenLabel();
        }

        void UpdateFullscreenLabel()
        {
            if (_fullscreenLabel != null)
                _fullscreenLabel.text = Screen.fullScreen
                    ? VNLocale.T("config.displayFullscreen")
                    : VNLocale.T("config.displayWindowed");
        }

        void SetLanguage(VNLanguage lang)
        {
            if (VNLocale.Language == lang) return;
            VNLocale.Language = lang;
            RebuildPanel();
        }

        void RebuildPanel()
        {
            bool wasOpen = _open;
            if (_canvasGo != null) Destroy(_canvasGo);
            _canvasGo = null;
            _panel = null;
            _fullscreenLabel = null;
            if (wasOpen)
            {
                Build();
                RefreshValues();
                _panel.SetActive(true);
            }
        }

    }
}
