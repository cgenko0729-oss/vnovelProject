using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>快捷功能条使用的轻量设置面板；选项写入 PlayerPrefs。</summary>
    public class VNConfigPanel : MonoBehaviour
    {
        const string BgmKey = "VN.Config.BgmVolume";
        const string SeKey = "VN.Config.SeVolume";
        const string VoiceKey = "VN.Config.VoiceVolume";
        const string TextSpeedKey = "VN.Config.TextSpeed";
        const string FullscreenKey = "VN.Config.Fullscreen";

        VNScriptRunner _runner;
        VNStage _stage;
        GameObject _canvasGo;
        GameObject _panel;
        TMP_Text _fullscreenLabel;
        bool _open;
        bool _settingsApplied;

        public bool IsOpen => _open;

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
            if (customSkin != null)
            {
                BindCustomSkin(customSkin);
                _panel.SetActive(false);
                return;
            }

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);

            var dim = CreateButton(panelRect, "Dim", "", Vector2.zero, Vector2.zero, Close);
            var dimRect = (RectTransform)dim.transform;
            Stretch(dimRect);
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0.018f, 0.82f);

            var window = new GameObject("Window", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var windowRect = (RectTransform)window.transform;
            windowRect.SetParent(panelRect, false);
            windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.pivot = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(780f, 740f);
            var windowImage = window.GetComponent<Image>();
            windowImage.sprite = VNProceduralTextures.RoundedRectSprite;
            windowImage.type = Image.Type.Sliced;
            windowImage.color = new Color(0.025f, 0.038f, 0.075f, 0.995f);

            var title = CreateText(windowRect, VNLocale.T("config.title"), 38, TextAlignmentOptions.Left);
            SetRect(title.rectTransform, new Vector2(58f, -34f), new Vector2(560f, 56f));
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.78f, 0.38f, 1f);

            CreateButton(windowRect, "Close", "×", new Vector2(700f, -34f),
                new Vector2(48f, 48f), Close, 32);

            float bgm = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.bgmVolume : 0.75f;
            float se = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.seVolume : 1f;
            float voice = _stage != null && _stage.vnAudio != null ? _stage.vnAudio.voiceVolume : 1f;
            float speed = _stage != null && _stage.dialogue != null ? _stage.dialogue.TextSpeed : 18f;

            CreateSettingSlider(windowRect, VNLocale.T("config.bgm"), 126f, 0f, 1f, bgm,
                v => $"{v:P0}",
                value =>
                {
                    _stage?.vnAudio?.SetVolume("bgm", value);
                    PlayerPrefs.SetFloat(BgmKey, value);
                });
            CreateSettingSlider(windowRect, VNLocale.T("config.se"), 212f, 0f, 1f, se,
                v => $"{v:P0}",
                value =>
                {
                    _stage?.vnAudio?.SetVolume("se", value);
                    PlayerPrefs.SetFloat(SeKey, value);
                });
            CreateSettingSlider(windowRect, VNLocale.T("config.voice"), 298f, 0f, 1f, voice,
                v => $"{v:P0}",
                value =>
                {
                    _stage?.vnAudio?.SetVolume("voice", value);
                    PlayerPrefs.SetFloat(VoiceKey, value);
                });
            CreateSettingSlider(windowRect, VNLocale.T("config.textSpeed"), 384f, 8f, 60f, speed,
                v => VNLocale.T("config.textSpeedValue", v),
                value =>
                {
                    _stage?.dialogue?.SetTextSpeed(value);
                    PlayerPrefs.SetFloat(TextSpeedKey, value);
                });

            var langLabel = CreateText(windowRect, VNLocale.T("config.language"), 24,
                TextAlignmentOptions.Left);
            SetRect(langLabel.rectTransform, new Vector2(82f, -470f), new Vector2(220f, 42f));
            CreateLanguageButton(windowRect, VNLanguage.Chinese, 260f);
            CreateLanguageButton(windowRect, VNLanguage.English, 408f);
            CreateLanguageButton(windowRect, VNLanguage.Japanese, 556f);

            var fullscreen = CreateButton(windowRect, "Fullscreen", "", new Vector2(82f, -584f),
                new Vector2(616f, 58f), ToggleFullscreen, 24);
            _fullscreenLabel = fullscreen.GetComponentInChildren<TextMeshProUGUI>();
            UpdateFullscreenLabel();

            var hint = CreateText(windowRect,
                VNLocale.T("config.hint"), 19, TextAlignmentOptions.Center);
            SetRect(hint.rectTransform, new Vector2(80f, -668f), new Vector2(620f, 34f));
            hint.color = new Color(0.68f, 0.74f, 0.86f, 0.9f);
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
        }

        static void BindSlider(Slider slider, TMP_Text valueText, float min, float max, float value,
            Func<float, string> format, Action<float> changed)
        {
            // Slider 本身必须位于 GraphicRaycaster 的命中面上。美术 prefab 可能只保留
            // 不接收射线的装饰图片，因此在实例上补一层透明命中图，不改变可见外观。
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

        void RefreshValues() => UpdateFullscreenLabel();

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

        void CreateLanguageButton(RectTransform parent, VNLanguage lang, float x)
        {
            bool current = VNLocale.Language == lang;
            var go = CreateButton(parent, "Lang_" + lang, VNLocale.DisplayName(lang),
                new Vector2(x, -474f), new Vector2(140f, 50f), () => SetLanguage(lang), 22);
            if (current)
                go.GetComponent<Image>().color = new Color(0.92f, 0.61f, 0.18f, 0.98f);
        }

        void SetLanguage(VNLanguage lang)
        {
            if (VNLocale.Language == lang) return;
            VNLocale.Language = lang; // VNFont 全场景换字体 + 各 UI 组件经 LanguageChanged 重建
            RebuildPanel();
        }

        /// <summary>语言切换后销毁重建整个面板（所有文案在 Build 里取表，重建即刷新）</summary>
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

        void CreateSettingSlider(RectTransform parent, string label, float top,
            float min, float max, float value, Func<float, string> format, Action<float> changed)
        {
            var labelText = CreateText(parent, label, 24, TextAlignmentOptions.Left);
            SetRect(labelText.rectTransform, new Vector2(82f, -top), new Vector2(170f, 42f));

            var sliderGo = new GameObject(label + "Slider", typeof(RectTransform), typeof(Slider));
            var sliderRect = (RectTransform)sliderGo.transform;
            sliderRect.SetParent(parent, false);
            SetRect(sliderRect, new Vector2(260f, -top - 4f), new Vector2(340f, 44f));

            var background = CreateImage(sliderRect, "Background", new Color(0.1f, 0.13f, 0.21f, 1f));
            var backgroundRect = background.rectTransform;
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 10f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.SetParent(sliderRect, false);
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.offsetMin = new Vector2(7f, -5f);
            fillAreaRect.offsetMax = new Vector2(-7f, 5f);
            var fill = CreateImage(fillAreaRect, "Fill", new Color(1f, 0.62f, 0.2f, 1f));
            fill.type = Image.Type.Sliced;
            Stretch(fill.rectTransform);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.SetParent(sliderRect, false);
            Stretch(handleAreaRect);
            handleAreaRect.offsetMin = new Vector2(10f, 0f);
            handleAreaRect.offsetMax = new Vector2(-10f, 0f);
            var handle = CreateImage(handleAreaRect, "Handle", new Color(1f, 0.88f, 0.62f, 1f));
            handle.sprite = VNProceduralTextures.RoundedRectSprite;
            handle.type = Image.Type.Sliced;
            var handleRect = handle.rectTransform;
            handleRect.sizeDelta = new Vector2(24f, 32f);

            var valueText = CreateText(parent, "", 21, TextAlignmentOptions.Right);
            SetRect(valueText.rectTransform, new Vector2(614f, -top), new Vector2(84f, 42f));

            var slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.SetValueWithoutNotify(value);
            Action<float> update = v =>
            {
                valueText.text = format(v);
                changed?.Invoke(v);
            };
            update(value);
            slider.onValueChanged.AddListener(v => update(v));
        }

        GameObject CreateButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action, int fontSize = 24)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetRect(rect, position, size);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.09f, 0.12f, 0.2f, 0.98f);
            go.GetComponent<Button>().onClick.AddListener(action);
            if (!string.IsNullOrEmpty(label))
            {
                var text = CreateText(rect, label, fontSize, TextAlignmentOptions.Center);
                Stretch(text.rectTransform);
                text.fontStyle = FontStyles.Bold;
            }
            return go;
        }

        TextMeshProUGUI CreateText(Transform parent, string value, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.alignment = anchor;
            text.text = value;
            text.color = new Color(0.95f, 0.96f, 1f, 1f);
            text.raycastTarget = false;
            return text;
        }

        Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        static void SetRect(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
