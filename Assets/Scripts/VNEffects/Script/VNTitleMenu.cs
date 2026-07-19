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
    /// <summary>
    /// 开始菜单（标题画面）：同场景覆盖层，不做独立场景。
    ///
    /// 【工作方式】
    /// VNScriptRunner.Start 发现场景里有本组件且 showOnStart 时，跳过 playOnStart，
    /// 改为打开标题层：舞台照常渲染（标题背景 + Ken Burns 缓慢漂移都是真的舞台效果），
    /// 标题层只放半透明压暗 + 标题文字 + 按钮列，排序 500 —— 在游戏 UI 之上、
    /// 所有运行时面板（画廊 600 / 存读档 900 / 设置 950）之下，因此
    /// 「读取存档 / CG 鉴赏 / 系统设置」直接复用现成面板，打开后自然盖在标题上。
    ///
    /// 【收起规则】只有一条：Runner 每次真正开始播放（ResumeAt——新游戏 / 读档 /
    /// 编辑器"从选中行播放"全走它）都会调 NotifyGameplayStarted()，标题层立即收起。
    /// 所以任何入口开始的游戏都不会残留标题层。
    ///
    /// 【内容来源】标题文字 / 标题背景 id / 标题 BGM id 都在 VNGameConfig 资产的
    /// "标题画面"区（重建场景不丢）；全部留空也能工作（背景库第一张 + 默认标题）。
    /// </summary>
    public class VNTitleMenu : MonoBehaviour
    {
        [Header("启动时显示标题菜单（调试剧情时可在场景里临时关掉）")]
        public bool showOnStart = true;

        VNScriptRunner _runner;
        VNStage _stage;
        GameObject _canvasGo;
        CanvasGroup _group;
        GameObject _quitConfirm;
        RectTransform _titleAnimationTarget;
        VNStatsHud _statsHud;
        GameObject _hintText;          // 场景底部按键提示，标题期间藏起来
        bool _open;
        bool _busy;                    // 开始过渡进行中，防连点
        bool _stageApplied;            // 标题背景/BGM 只应用一次（语言切换重建不重播）

        static readonly Color ButtonColor = new Color(0.055f, 0.075f, 0.13f, 0.88f);

        public bool IsOpen => _open;

        public void Initialize(VNScriptRunner runner, VNStage stage)
        {
            _runner = runner;
            _stage = stage;
            VNLocale.LanguageChanged -= OnLanguageChanged; // 幂等订阅
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>语言切换：所有文案在 Build 里取表，销毁重建即刷新</summary>
        void OnLanguageChanged()
        {
            if (_canvasGo == null) return;
            bool wasOpen = _open;
            Destroy(_canvasGo);
            _canvasGo = null;
            _group = null;
            _quitConfirm = null;
            _titleAnimationTarget = null;
            if (wasOpen)
            {
                _open = false;
                Open();
            }
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

        /// <summary>
        /// Runner 每次开始播放都来敲这里（ResumeAt）：立即收起标题层并恢复场景 UI。
        /// 新游戏按钮自己的淡出流程先收起、后播剧本，此时这里是幂等空操作。
        /// </summary>
        public void NotifyGameplayStarted()
        {
            if (!_open) return;
            HideAndCleanup();
        }

        /// <summary>收起并销毁标题画布（循环 Tween 全靠 SetLink 随之回收），恢复场景 UI。</summary>
        void HideAndCleanup()
        {
            _open = false;
            _busy = false;
            RestoreSceneUi();
            if (_canvasGo != null)
            {
                Destroy(_canvasGo);
                _canvasGo = null;
                _group = null;
                _quitConfirm = null;
                _titleAnimationTarget = null;
            }
        }

        // ------------------------------------------------------------------
        // 舞台联动：标题背景 / BGM / 藏起游戏 UI
        // ------------------------------------------------------------------

        void ApplyTitleStage()
        {
            var cfg = VNGameConfig.Active;
            if (!_stageApplied)
            {
                _stageApplied = true;
                if (_stage != null)
                {
                    // 背景：配置指定的 id 优先，否则背景库第一张（Ken Burns 默认漂移是舞台自带的）
                    string bgId = cfg != null && !string.IsNullOrEmpty(cfg.titleBackground)
                        ? cfg.titleBackground : null;
                    if (bgId == null && _stage.backgrounds.Count > 0)
                        bgId = _stage.backgrounds[0].id;
                    if (!string.IsNullOrEmpty(bgId)) _stage.SetBackground(bgId, null);

                    if (cfg != null && !string.IsNullOrEmpty(cfg.titleBgm))
                        _stage.vnAudio?.PlayBgm(cfg.titleBgm);
                }
            }

            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(false);
            if (_statsHud == null) _statsHud = FindFirstObjectByType<VNStatsHud>();
            _statsHud?.SetHudVisible(false);
            if (_hintText == null)
            {
                var hint = GameObject.Find("HintText"); // 生成器创建的底部按键提示
                if (hint != null) { _hintText = hint; hint.SetActive(false); }
            }
        }

        void RestoreSceneUi()
        {
            if (_hintText != null)
            {
                _hintText.SetActive(true);
                _hintText = null;
            }
            if (_stage != null && _stage.dialogue != null)
                _stage.dialogue.SetInterfaceVisible(true); // 尊重 _shown：空对话框不会被强行显示
            _statsHud?.SetHudVisible(true);
        }

        // ------------------------------------------------------------------
        // 按钮行为
        // ------------------------------------------------------------------

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
            int slot = FindLatestSlot();
            if (slot < 0) return; // 按钮已置灰，正常到不了这里
            _runner.LoadFrom(slot); // 成功时 ResumeAt → NotifyGameplayStarted 收起标题
        }

        /// <summary>找保存时间最新的槽（含快速存档槽 0）；没有任何存档返回 -1。</summary>
        static int FindLatestSlot()
        {
            int latest = -1;
            DateTime latestTime = DateTime.MinValue;
            for (int slot = 0; slot <= VNSaveSystem.SlotCount; slot++)
            {
                var data = VNSaveSystem.Peek(slot);
                if (data == null) continue;
                if (!DateTime.TryParse(data.savedAt, out var time)) time = DateTime.MinValue;
                if (latest < 0 || time > latestTime)
                {
                    latest = slot;
                    latestTime = time;
                }
            }
            return latest;
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
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                _quitConfirm.SetActive(false);
        }

        // ------------------------------------------------------------------
        // UI 构建（全部运行时生成，风格与存读档/设置面板一致）
        // ------------------------------------------------------------------

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
            canvas.sortingOrder = 500; // 游戏 UI 之上、画廊 600 与存读档/设置面板之下
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.titleMenuPrefab);
            var customSkin = VNSystemUiSkinUtility.Instantiate<VNTitleMenuSkin>(
                skinPrefab, _canvasGo.transform, "VNTitleMenu");
            if (customSkin != null)
            {
                BindCustomSkin(customSkin);
                _canvasGo.SetActive(false);
                return;
            }

            var root = new GameObject("Root", typeof(RectTransform), typeof(CanvasGroup));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(_canvasGo.transform, false);
            Stretch(rootRect);
            _group = root.GetComponent<CanvasGroup>();

            // 整屏轻压暗：让舞台背景当画、文字按钮浮在上面
            var dim = CreateImage(rootRect, "Dim", new Color(0.01f, 0.015f, 0.04f, 0.38f));
            Stretch(dim.rectTransform);

            // 左侧纵向暗带：按钮列的可读性衬底（光晕贴图横向拉伸当渐变用）
            var side = CreateImage(rootRect, "SideShade", new Color(0.01f, 0.015f, 0.05f, 0.72f));
            side.sprite = VNProceduralTextures.RadialGlowSprite;
            var sideRect = side.rectTransform;
            sideRect.anchorMin = new Vector2(0f, 0f);
            sideRect.anchorMax = new Vector2(0f, 1f);
            sideRect.pivot = new Vector2(0.5f, 0.5f);
            sideRect.anchoredPosition = new Vector2(210f, 0f);
            sideRect.sizeDelta = new Vector2(1000f, 700f);

            BuildTitleText(rootRect);
            BuildSparkles(rootRect);
            BuildButtons(rootRect);

            // 右下角版本号
            var version = CreateText(rootRect, "Ver " + Application.version, 20,
                TextAlignmentOptions.BottomRight);
            version.color = new Color(0.8f, 0.85f, 0.95f, 0.55f);
            var versionRect = version.rectTransform;
            versionRect.anchorMin = versionRect.anchorMax = new Vector2(1f, 0f);
            versionRect.pivot = new Vector2(1f, 0f);
            versionRect.anchoredPosition = new Vector2(-36f, 24f);
            versionRect.sizeDelta = new Vector2(320f, 34f);

            BuildQuitConfirm(rootRect);
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
            VNSaveData latest = latestSlot >= 0 ? VNSaveSystem.Peek(latestSlot) : null;
            skin.continueLabel.text = VNLocale.T("title.continue");
            if (skin.continueTimeText != null)
                skin.continueTimeText.text = latest != null ? latest.savedAt : "";
            else if (latest != null && !string.IsNullOrEmpty(latest.savedAt))
                skin.continueLabel.text += $"  <size=60%><color=#C9D2E8AA>{latest.savedAt}</color></size>";

            BindButton(skin.startButton, skin.startLabel, VNLocale.T("title.start"), OnStartClicked, true);
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

        void BuildTitleText(RectTransform parent)
        {
            // 标题背后的暖色光晕，缓慢呼吸（与项目"发光=光晕贴图+脉动"的套路一致）
            var glow = CreateImage(parent, "TitleGlow", new Color(1f, 0.82f, 0.45f, 0.4f));
            glow.sprite = VNProceduralTextures.RadialGlowSprite;
            var glowRect = glow.rectTransform;
            glowRect.anchorMin = glowRect.anchorMax = new Vector2(0f, 1f);
            glowRect.pivot = new Vector2(0.5f, 0.5f);
            glowRect.anchoredPosition = new Vector2(560f, -230f);
            glowRect.sizeDelta = new Vector2(1150f, 480f);
            glow.transform.DOScale(1.07f, 3.2f).SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(glow.gameObject);

            var title = CreateText(parent, ResolveGameTitle(), 108, TextAlignmentOptions.Left);
            title.name = "GameTitle";
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(0.99f, 0.97f, 0.92f, 1f);
            var titleRect = title.rectTransform;
            _titleAnimationTarget = titleRect;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(118f, -150f);
            titleRect.sizeDelta = new Vector2(1400f, 150f);

            // 标题下的一条金色细线，收住排版
            var line = CreateImage(parent, "TitleLine", new Color(1f, 0.78f, 0.38f, 0.85f));
            var lineRect = line.rectTransform;
            lineRect.anchorMin = lineRect.anchorMax = new Vector2(0f, 1f);
            lineRect.pivot = new Vector2(0f, 1f);
            lineRect.anchoredPosition = new Vector2(122f, -302f);
            lineRect.sizeDelta = new Vector2(560f, 3f);
        }

        /// <summary>标题文字：VNGameConfig 按当前语言取（En/Ja 缺省回退中文），全空用默认。</summary>
        static string ResolveGameTitle()
        {
            var cfg = VNGameConfig.Active;
            if (cfg == null) return "Visual Novel";
            string text = cfg.gameTitle;
            switch (VNLocale.Language)
            {
                case VNLanguage.English:
                    if (!string.IsNullOrEmpty(cfg.gameTitleEn)) text = cfg.gameTitleEn;
                    break;
                case VNLanguage.Japanese:
                    if (!string.IsNullOrEmpty(cfg.gameTitleJa)) text = cfg.gameTitleJa;
                    break;
            }
            return string.IsNullOrEmpty(text) ? "Visual Novel" : text;
        }

        /// <summary>右半屏撒一把缓慢上飘的星光（uGUI 假粒子：Overlay 画布盖不住真粒子系统）</summary>
        void BuildSparkles(RectTransform parent)
        {
            var random = new System.Random(20260719);
            for (int i = 0; i < 14; i++)
            {
                bool star = i % 3 != 0; // 星与柔圆混着放
                var sparkle = CreateImage(parent, $"Sparkle_{i:00}",
                    new Color(1f, 0.92f, 0.68f, 0f));
                sparkle.sprite = star ? VNProceduralTextures.SparkleSprite
                                      : VNProceduralTextures.RadialGlowSprite;
                float size = star ? 14f + (float)random.NextDouble() * 22f
                                  : 40f + (float)random.NextDouble() * 60f;
                var rect = sparkle.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(size, size);
                float x = 60f + (float)random.NextDouble() * 840f;   // 主要撒在右半屏
                float y = -420f + (float)random.NextDouble() * 780f;
                rect.anchoredPosition = new Vector2(x, y);

                float rise = 60f + (float)random.NextDouble() * 90f;
                float duration = 5f + (float)random.NextDouble() * 6f;
                float delay = (float)random.NextDouble() * 4f;
                float alpha = 0.25f + (float)random.NextDouble() * 0.5f;

                rect.DOAnchorPosY(y + rise, duration).SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo).SetDelay(delay)
                    .SetUpdate(true).SetLink(sparkle.gameObject);
                sparkle.DOFade(alpha, duration * 0.5f).SetEase(Ease.InOutSine)
                    .SetLoops(-1, LoopType.Yoyo).SetDelay(delay)
                    .SetUpdate(true).SetLink(sparkle.gameObject);
            }
        }

        void BuildButtons(RectTransform parent)
        {
            int latestSlot = FindLatestSlot();
            string continueLabel = VNLocale.T("title.continue");
            if (latestSlot >= 0)
            {
                var data = VNSaveSystem.Peek(latestSlot);
                if (data != null && !string.IsNullOrEmpty(data.savedAt))
                    continueLabel += $"  <size=60%><color=#C9D2E8AA>{data.savedAt}</color></size>";
            }

            (string label, Action action, bool enabled)[] entries =
            {
                (VNLocale.T("title.start"), OnStartClicked, true),
                (continueLabel, OnContinueClicked, latestSlot >= 0),
                (VNLocale.T("title.load"), () => _runner?.RequestLoadPanel(), true),
                (VNLocale.T("title.gallery"), () => _runner?.RequestCgGallery(), true),
                (VNLocale.T("title.config"), () => _runner?.RequestConfigPanel(), true),
                (VNLocale.T("title.quit"), () => _quitConfirm.SetActive(true), true),
            };

            float y = 150f + (entries.Length - 1) * 76f; // 底部对齐往上排
            for (int i = 0; i < entries.Length; i++)
            {
                CreateMenuButton(parent, $"Btn_{i}", entries[i].label,
                    new Vector2(118f, y - i * 76f), entries[i].action, entries[i].enabled);
            }
        }

        GameObject CreateMenuButton(RectTransform parent, string name, string label,
            Vector2 bottomLeftPos, Action onClick, bool enabled)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = bottomLeftPos;
            rect.sizeDelta = new Vector2(400f, 62f);

            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = ButtonColor;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            // ColorBlock 与 Image 颜色相乘，倍率 >1 做悬停提亮（与存读档面板同套路）
            colors.highlightedColor = new Color(1.9f, 1.75f, 1.35f, 1.1f);
            colors.pressedColor = new Color(0.72f, 0.78f, 0.92f, 1f);
            colors.disabledColor = new Color(0.55f, 0.57f, 0.62f, 0.45f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.interactable = enabled;
            if (onClick != null) button.onClick.AddListener(() => onClick());

            var text = CreateText(rect, label, 27, TextAlignmentOptions.Left);
            text.fontStyle = FontStyles.Bold;
            text.color = enabled ? new Color(0.95f, 0.96f, 1f, 1f)
                                 : new Color(0.62f, 0.66f, 0.76f, 0.8f);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(26f, 0f);
            text.rectTransform.offsetMax = new Vector2(-12f, 0f);
            return go;
        }

        void BuildQuitConfirm(RectTransform parent)
        {
            _quitConfirm = new GameObject("QuitConfirm", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var blockerRect = (RectTransform)_quitConfirm.transform;
            blockerRect.SetParent(parent, false);
            Stretch(blockerRect);
            _quitConfirm.GetComponent<Image>().color = new Color(0f, 0f, 0.015f, 0.72f);

            var dialog = new GameObject("Dialog", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)dialog.transform;
            rect.SetParent(blockerRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620f, 250f);
            var image = dialog.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.035f, 0.05f, 0.09f, 0.995f);

            var message = CreateText(rect, VNLocale.T("title.quitConfirm"), 29,
                TextAlignmentOptions.Center);
            var messageRect = message.rectTransform;
            messageRect.anchorMin = new Vector2(0f, 0.42f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.offsetMin = new Vector2(30f, 0f);
            messageRect.offsetMax = new Vector2(-30f, -20f);

            CreateConfirmButton(rect, "Yes", VNLocale.T("common.confirm"),
                new Vector2(205f, -188f), OnQuitConfirmed);
            CreateConfirmButton(rect, "No", VNLocale.T("common.cancel"),
                new Vector2(415f, -188f), () => _quitConfirm.SetActive(false));
            _quitConfirm.SetActive(false);
        }

        void CreateConfirmButton(RectTransform parent, string name, string label,
            Vector2 topLeftPos, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeftPos;
            rect.sizeDelta = new Vector2(180f, 54f);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.11f, 0.14f, 0.22f, 1f);
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
            var text = CreateText(rect, label, 25, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform);
        }

        void PlayEntrance()
        {
            _group.DOKill();
            _group.alpha = 0f;
            _group.interactable = true;
            _group.blocksRaycasts = true;
            _group.DOFade(1f, 0.9f).SetEase(Ease.OutQuad)
                .SetUpdate(true).SetLink(_canvasGo);

            // 标题从上方轻轻落位
            var title = _titleAnimationTarget;
            if (title != null)
            {
                Vector2 pos = title.anchoredPosition;
                title.anchoredPosition = pos + new Vector2(0f, 36f);
                title.DOAnchorPos(pos, 1.1f).SetEase(Ease.OutCubic)
                    .SetUpdate(true).SetLink(title.gameObject);
            }
        }

        // ------------------------------------------------------------------
        // 小工具（与其他运行时面板保持同款）
        // ------------------------------------------------------------------

        Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI CreateText(Transform parent, string value, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
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

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
        }
    }
}
