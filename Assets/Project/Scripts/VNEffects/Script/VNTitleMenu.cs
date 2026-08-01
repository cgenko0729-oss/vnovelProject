using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>使用预制体显示标题菜单，并处理继续游戏、画廊与设置入口。</summary>
    public class VNTitleMenu : MonoBehaviour
    {
        [Header("启动设置")]
        public bool showOnStart = true;

        VNScriptRunner _runner;
        VNStage _stage;
        GameObject _canvasGo;
        CanvasGroup _group;
        GameObject _quitConfirm;
        RectTransform _titleAnimationTarget;
        VNStatsHud _statsHud;
        GameObject _hintText;
        bool _open;
        bool _busy;
        bool _stageApplied;

        public bool IsOpen => _open;

        public void Initialize(VNScriptRunner runner, VNStage stage)
        {
            _runner = runner;
            _stage = stage;
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            if (_canvasGo == null) return;
            bool wasOpen = _open;
            Destroy(_canvasGo);
            _canvasGo = null;
            _group = null;
            _quitConfirm = null;
            _titleAnimationTarget = null;
            if (!wasOpen) return;
            _open = false;
            Open();
        }

        public void Open()
        {
            if (_open) return;
            ApplyTitleStage();
            Build();
            _canvasGo.SetActive(true);
            _open = true;
            PlayEntrance();
        }

        public void NotifyGameplayStarted()
        {
            if (_open) HideAndCleanup();
        }

        void HideAndCleanup()
        {
            _open = false;
            _busy = false;
            RestoreSceneUi();
            if (_canvasGo == null) return;
            Destroy(_canvasGo);
            _canvasGo = null;
            _group = null;
            _quitConfirm = null;
            _titleAnimationTarget = null;
        }

        void ApplyTitleStage()
        {
            var config = VNGameConfig.Active;
            if (!_stageApplied)
            {
                _stageApplied = true;
                if (_stage != null)
                {
                    string backgroundId = config != null && !string.IsNullOrEmpty(config.titleBackground)
                        ? config.titleBackground
                        : null;
                    if (backgroundId == null && _stage.backgrounds.Count > 0)
                        backgroundId = _stage.backgrounds[0].id;
                    if (!string.IsNullOrEmpty(backgroundId))
                        _stage.SetBackground(backgroundId, null);
                    if (config != null && !string.IsNullOrEmpty(config.titleBgm))
                        _stage.vnAudio?.PlayBgm(config.titleBgm);
                }
            }

            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(false);
            if (_statsHud == null) _statsHud = FindFirstObjectByType<VNStatsHud>();
            _statsHud?.SetHudVisible(false);
            if (_hintText != null) return;
            _hintText = GameObject.Find("HintText");
            if (_hintText != null) _hintText.SetActive(false);
        }

        void RestoreSceneUi()
        {
            if (_hintText != null)
            {
                _hintText.SetActive(true);
                _hintText = null;
            }
            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(true);
            _statsHud?.SetHudVisible(true);
        }

        void OnStartClicked()
        {
            if (_busy || _runner == null) return;
            _busy = true;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            _group.DOKill();
            _group.DOFade(0f, 0.55f).SetUpdate(true).SetLink(_canvasGo).OnComplete(() =>
            {
                HideAndCleanup();
                _runner.StartNewGame();
            });
        }

        void OnContinueClicked()
        {
            if (_busy || _runner == null) return;
            int latestSlot = FindLatestSlot();
            if (latestSlot >= 0) _runner.LoadFrom(latestSlot);
        }

        static int FindLatestSlot()
        {
            int latestSlot = -1;
            DateTime latestTime = DateTime.MinValue;
            for (int slot = 0; slot <= 20; slot++)
            {
                var data = VNSaveSystem.Peek(slot);
                if (data == null) continue;
                if (!DateTime.TryParse(data.savedAt, out var savedAt))
                    savedAt = DateTime.MinValue;
                if (latestSlot >= 0 && savedAt <= latestTime) continue;
                latestSlot = slot;
                latestTime = savedAt;
            }
            return latestSlot;
        }

        void OnQuitConfirmed()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void Update()
        {
            if (!_open || _quitConfirm == null || !_quitConfirm.activeSelf) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                _quitConfirm.SetActive(false);
        }

        void Build()
        {
            if (_canvasGo != null) return;
            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            _canvasGo = new GameObject("VNTitleCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var prefab = VNSystemUiSkinUtility.Prefab(s => s.titleMenuPrefab);
            var skin = VNSystemUiSkinUtility.Instantiate<VNTitleMenuSkin>(
                prefab, _canvasGo.transform, "VNTitleMenu");
            if (skin == null)
                throw new InvalidOperationException("Title menu prefab is missing or invalid.");

            BindCustomSkin(skin);
            _canvasGo.SetActive(false);
        }

        void BindCustomSkin(VNTitleMenuSkin skin)
        {
            _group = skin.canvasGroup;
            _titleAnimationTarget = skin.titleAnimationTarget != null
                ? skin.titleAnimationTarget
                : skin.gameTitle.rectTransform;
            _quitConfirm = skin.quitConfirmRoot;

            skin.gameTitle.text = ResolveGameTitle();
            if (skin.versionText != null) skin.versionText.text = "Ver " + Application.version;

            int latestSlot = FindLatestSlot();
            var latest = latestSlot >= 0 ? VNSaveSystem.Peek(latestSlot) : null;
            skin.continueLabel.text = VNLocale.T("title.continue");
            if (skin.continueTimeText != null)
                skin.continueTimeText.text = latest != null ? latest.savedAt : "";
            else if (latest != null && !string.IsNullOrEmpty(latest.savedAt))
                skin.continueLabel.text +=
                    $"  <size=60%><color=#C9D2E8AA>{latest.savedAt}</color></size>";

            BindButton(skin.startButton, skin.startLabel, VNLocale.T("title.start"),
                OnStartClicked, true);
            BindButton(skin.continueButton, null, null, OnContinueClicked, latestSlot >= 0);
            BindButton(skin.loadButton, skin.loadLabel, VNLocale.T("title.load"),
                () => _runner?.RequestLoadPanel(), true);
            BindButton(skin.galleryButton, skin.galleryLabel, VNLocale.T("title.gallery"),
                () => _runner?.RequestCgGallery(), true);
            BindButton(skin.configButton, skin.configLabel, VNLocale.T("title.config"),
                () => _runner?.RequestConfigPanel(), true);
            BindButton(skin.quitButton, skin.quitLabel, VNLocale.T("title.quit"),
                () => _quitConfirm.SetActive(true), true);

            skin.quitConfirmMessage.text = VNLocale.T("title.quitConfirm");
            BindButton(skin.quitConfirmButton, skin.quitConfirmLabel, VNLocale.T("common.confirm"),
                OnQuitConfirmed, true);
            BindButton(skin.quitCancelButton, skin.quitCancelLabel, VNLocale.T("common.cancel"),
                () => _quitConfirm.SetActive(false), true);
            _quitConfirm.SetActive(false);
        }

        static void BindButton(Button button, TMP_Text label, string text,
            UnityEngine.Events.UnityAction action, bool interactable)
        {
            if (label != null && text != null) label.text = text;
            button.onClick.RemoveAllListeners();
            if (action != null) button.onClick.AddListener(action);
            button.interactable = interactable;
        }

        static string ResolveGameTitle()
        {
            var config = VNGameConfig.Active;
            if (config == null) return "Visual Novel";
            string text = config.gameTitle;
            switch (VNLocale.Language)
            {
                case VNLanguage.English:
                    if (!string.IsNullOrEmpty(config.gameTitleEn)) text = config.gameTitleEn;
                    break;
                case VNLanguage.Japanese:
                    if (!string.IsNullOrEmpty(config.gameTitleJa)) text = config.gameTitleJa;
                    break;
            }
            return string.IsNullOrEmpty(text) ? "Visual Novel" : text;
        }

        void PlayEntrance()
        {
            _group.DOKill();
            _group.alpha = 0f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
            _group.DOFade(1f, 0.9f).SetEase(Ease.OutQuad)
                .SetUpdate(true).SetLink(_canvasGo);

            var title = _titleAnimationTarget;
            if (title == null) return;
            Vector2 position = title.anchoredPosition;
            title.anchoredPosition = position + new Vector2(0f, 36f);
            title.DOAnchorPos(position, 1.1f).SetEase(Ease.OutCubic)
                .SetUpdate(true).SetLink(title.gameObject);
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
        }
    }
}
