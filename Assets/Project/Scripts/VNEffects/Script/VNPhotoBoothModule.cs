using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：拍大头照。
    ///
    /// 参考实现（Student Age new / PhotoboothView）是「选边框 + 切表情 + 摆贴纸 → 快门 →
    /// 截图存 PNG」的装扮互动，没有输赢。本项目在它之上加了主题评分：
    /// 剧本给一个主题，主题资产列出它想要什么表情/边框/贴纸，按命中项算分分三档。
    ///
    /// 剧本用法：
    ///   event photo vs:亚里沙 me:小雪 theme:甜蜜 frame:粉格子 time:60 stat:好感度 rate:0.1
    ///   * 完美 -> 甜蜜回忆
    ///   * 普通 -> 平常一天
    ///   * 失败 -> 尴尬收场
    ///
    /// kwargs：
    ///   vs:     合影对象的角色 id（必填）
    ///   me:     主角的角色 id（默认取 VNGameConfig.photoMeCharacterId）
    ///   theme:  主题资产 id；不写 = 自由拍照（不评分，结果固定「完成」）
    ///   mode:   match（默认）/ free；free 强制不评分
    ///   frame:  初始边框 id
    ///   time:   装扮限时秒（默认主题资产的 timeLimit；0 = 不限时）
    ///   stat:   拍完自动加的属性名（可选）
    ///   rate:   分数 → 属性的换算率（默认 0.1，即 100 分加 10 点）
    ///   flag:   成绩 flag 前缀（默认「大头照」）
    ///   title:  面板标题覆盖
    ///
    /// 结果："完美" / "普通" / "失败"；自由拍照为 "完成"。
    /// 同时写 flag「&lt;前缀&gt;_分数」「&lt;前缀&gt;_档位」「&lt;前缀&gt;_次数」。
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 / 组件随时可被销毁。
    /// </summary>
    public class VNPhotoBoothModule : VNEventModule
    {
        // ==================================================================
        // 模板上登记的内容库（VNGameConfig 会覆盖）
        // ==================================================================

        [Header("边框样式库（event photo frame: 按 frameId 查找）")]
        public List<VNPhotoFrameDef> frames = new List<VNPhotoFrameDef>();

        [Header("贴纸库（右下角贴纸列表 + 主题评分表按 stickerId 引用）")]
        public List<VNPhotoStickerDef> stickers = new List<VNPhotoStickerDef>();

        [Header("背景库（画在两个人身后，被开窗裁切；event photo bg: 按 backdropId 查找）")]
        public List<VNPhotoBackdropDef> backdrops = new List<VNPhotoBackdropDef>();

        [Header("主题库（event photo theme: 按 themeId 查找）")]
        public List<VNPhotoThemeDef> themes = new List<VNPhotoThemeDef>();

        [Header("「我」默认用哪个角色的立绘（剧本 me: 可覆盖；留空则取景框左侧空着）")]
        public string defaultMeCharacterId = "";

        [Header("取景倍率：立绘宽度 = 每人分到的宽度 × 此值。调大 = 人物更近更大\n" +
                "默认按项目现有的全身站姿横图标定（拍到头+上半身）")]
        [Range(0.6f, 10f)] public float photoFit = 3.6f;

        [Header("脸在立绘里的纵向位置（0 = 图顶，1 = 图底）。用来把脸推到取景框中心，\n" +
                "素材构图不同就调它：看到的偏下就调小，偏上就调大")]
        [Range(0f, 1f)] public float faceAnchor = 0.22f;

        [Header("两人中心距 = 每人分到的宽度 × 此值。调大 = 站得更开")]
        [Range(0.2f, 1.4f)] public float pairSpread = 1.1f;

        /// <summary>某个角色的取景单独调（素材构图和别人不一样时用）</summary>
        [System.Serializable]
        public class PortraitTweak
        {
            [Header("角色 id（VNCharacterDef.id）")]
            public string characterId;
            [Header("取景倍率覆盖（0 = 用上面的全局值）")]
            [Range(0f, 10f)] public float fit;
            [Header("脸的纵向位置覆盖（负数 = 用上面的全局值）")]
            [Range(-1f, 1f)] public float faceAnchor = -1f;
        }

        [Header("按角色单独微调取景（留空表示都用全局值）\n" +
                "同一角色的不同表情图如果构图不统一，这里也只能调一个折中值——" +
                "根治办法是把该角色的立绘统一成同样的画布与站位")]
        public List<PortraitTweak> portraitTweaks = new List<PortraitTweak>();

        [Header("主角立绘左右镜像（让两人朝向彼此，参考实现也是这么做的）")]
        public bool mirrorMe = true;

        // ==================================================================
        // 常量
        // ==================================================================

        /// <summary>取景框尺寸（4:3，照片就是这块区域）</summary>
        const float ViewW = 1040f;
        const float ViewH = 780f;
        /// <summary>取景框中心的纵向位置：上方让出标题条，下方让出快门</summary>
        const float ViewY = 22f;
        const float MachineW = 1860f;
        const float MachineH = 1020f;

        // 左右侧栏统一尺寸。三块（左栏 / 取景框 / 右栏）横向排开：
        // 机身内边距 30 + 栏 340 + 间隙 40 + 取景框 1040 + 间隙 40 + 栏 340 + 30 = 1860
        const float PanelW = 340f;
        const float PanelH = 900f;
        const float PanelX = MachineW * 0.5f - 200f;
        const float PanelY = -40f;
        /// <summary>栏内滚动列表的尺寸（宽度扣掉栏的左右留白，纵向让出顶部标签行）</summary>
        const float ListW = PanelW - 16f;
        const float ListH = PanelH - 90f;
        const float ListY = -33f;

        /// <summary>标题条基线（机身顶部往下一点，说明钮与限时条也排在这条线上）</summary>
        const float TitleY = MachineH * 0.5f - 58f;
        const float UrgentSeconds = 3f;

        /// <summary>表情格里的取景倍率（格子只要脸，比取景框拉得近得多）</summary>
        const float FaceCellFit = 6f;
        /// <summary>表情格尺寸与格内裁窗（两列刚好塞进 ListW）</summary>
        const float FaceCellSize = 146f;
        const float FaceClipSize = 130f;
        /// <summary>表情列表一屏能放下的行数，超过才需要滚动</summary>
        const int FaceRowsPerPage = 5;

        public const string FlagScoreSuffix = "_分数";
        public const string FlagGradeSuffix = "_档位";
        public const string FlagCountSuffix = "_次数";

        enum Phase { Dressing, Confirm, Shooting, Result, Ending }

        // ==================================================================
        // 运行时状态
        // ==================================================================

        Phase _phase = Phase.Dressing;
        VNStage _stage;
        VNStatsHud _statsHud;
        Canvas _canvas;

        VNCharacterDef _meDef, _herDef;
        string _meExpr, _herExpr;
        VNPhotoThemeDef _theme;
        VNPhotoFrameDef _frame;
        VNPhotoBackdropDef _backdrop;
        bool _freeMode;

        float _timeLimit, _timeLeft;
        string _flagPrefix = "大头照";
        string _statId;
        float _statRate = 0.1f;
        string _title;

        readonly List<VNPhotoStickerItem> _playerStickers = new List<VNPhotoStickerItem>();
        readonly List<GameObject> _frameDecorations = new List<GameObject>();

        // UI
        RectTransform _machine, _viewFinder, _window, _stickerLayer, _backStickerLayer;
        RectTransform _timerFill;
        Image _frameBack, _windowImage, _windowRing, _frameFront, _backdropImage;
        Image _meImage, _herImage;
        TextMeshProUGUI _watermark, _timerText;
        GameObject _leftPanel, _rightPanel, _bottomBar, _confirmLayer;
        RectTransform _frameContent, _backdropContent, _stickerContent;
        Button _shutterButton;
        readonly List<Image> _frameCells = new List<Image>();
        readonly List<Image> _backdropCells = new List<Image>();
        readonly List<Image> _meCells = new List<Image>();
        readonly List<Image> _herCells = new List<Image>();

        VNPhotoPortraitDragger _dragger;
        VNPhotoDoodle _doodle;
        Button _undoButton;
        Image _penPreview;

        // 演出（P3）
        VNPhotoSfx _sfx;
        Image _flash;
        TextMeshProUGUI _countdownText;
        GameObject _resultLayer;
        Texture2D _shotTexture;              // 结算层正在展示的照片（销毁时释放）
        Sprite _shotSprite;
        VNPhotoAlbum.Entry _shotEntry;       // 已存进相册的那张（重拍要删掉它）
        VNPhotoScore.Result _lastResult;

        // ==================================================================
        // 启动
        // ==================================================================

        protected override void OnLaunch(VNEventContext ctx)
        {
            _stage = ctx?.stage;
            _statsHud = FindFirstObjectByType<VNStatsHud>();
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvas = _canvas.rootCanvas;

            _sfx = new VNPhotoSfx();
            _sfx.Build(gameObject, FindFirstObjectByType<VNAudio>());

            ApplyConfig();
            if (!ParseArgs(ctx)) return;

            BuildUi();
            ApplyFrame(_frame);          // 内部会连带铺一次背景
            RefreshPortraits();
            RefreshFrameCells();
            RefreshBackdropCells();
            RefreshExpressionCells();

            _phase = Phase.Dressing;
        }

        void ApplyConfig()
        {
            var cfg = VNGameConfig.Active;
            if (cfg == null) return;
            VNGameConfig.ApplyList(cfg.photoFrames, ref frames);
            VNGameConfig.ApplyList(cfg.photoStickers, ref stickers);
            VNGameConfig.ApplyList(cfg.photoThemes, ref themes);
            VNGameConfig.ApplyList(cfg.photoBackdrops, ref backdrops);
            if (!string.IsNullOrEmpty(cfg.photoMeCharacterId))
                defaultMeCharacterId = cfg.photoMeCharacterId;
        }

        /// <summary>解析剧本参数。返回 false = 参数不成立，模块已自行结束。</summary>
        bool ParseArgs(VNEventContext ctx)
        {
            if (ctx == null)
            {
                Done(VNPhotoScore.OutcomeFree);
                return false;
            }

            string herId = ctx.Kw("vs");
            _herDef = FindCharacter(herId);
            if (_herDef == null)
            {
                Debug.LogWarning($"[VNPhoto] 第 {ctx.line} 行：找不到角色「{herId}」" +
                                 "（event photo 需要 vs:<角色 id>）");
                Done(VNPhotoScore.OutcomeFree);
                return false;
            }

            string meId = ctx.Kw("me", defaultMeCharacterId);
            _meDef = string.IsNullOrEmpty(meId) ? null : FindCharacter(meId);

            _herExpr = DefaultExpression(_herDef);
            _meExpr = DefaultExpression(_meDef);

            string mode = ctx.Kw("mode", "match");
            _freeMode = mode == "free" || mode == "自由";

            string themeId = ctx.Kw("theme");
            if (!_freeMode && !string.IsNullOrEmpty(themeId))
            {
                _theme = FindTheme(themeId);
                if (_theme == null)
                    Debug.LogWarning($"[VNPhoto] 第 {ctx.line} 行：主题库里没有「{themeId}」" +
                                     "（按自由拍照处理）");
            }
            if (_theme == null) _freeMode = true;

            _frame = FindFrame(ctx.Kw("frame"));
            _backdrop = FindBackdrop(ctx.Kw("bg"));

            _timeLimit = ctx.KwF("time", _theme != null ? _theme.timeLimit : 0f);
            if (_freeMode) _timeLimit = ctx.KwF("time", 0f);   // 自由拍照默认不限时
            _timeLeft = _timeLimit;

            _flagPrefix = ctx.Kw("flag", "大头照");
            _statId = ctx.Kw("stat");
            _statRate = ctx.KwF("rate", 0.1f);
            _title = ctx.Kw("title", VNLocale.T("photo.title"));
            return true;
        }

        static string DefaultExpression(VNCharacterDef def) =>
            def != null && def.expressions != null && def.expressions.Count > 0
                ? def.expressions[0].name : "";

        VNCharacterDef FindCharacter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            if (_stage != null && _stage.characters != null)
                foreach (var def in _stage.characters)
                    if (def != null && def.id == id) return def;

            var cfg = VNGameConfig.Active;
            if (cfg != null && cfg.characters != null)
                foreach (var def in cfg.characters)
                    if (def != null && def.id == id) return def;

            return null;
        }

        VNPhotoThemeDef FindTheme(string id)
        {
            foreach (var t in themes)
                if (t != null && t.themeId == id) return t;
            return null;
        }

        VNPhotoFrameDef FindFrame(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var f in frames)
                if (f != null && f.frameId == id) return f;
            Debug.LogWarning($"[VNPhoto] 边框库里没有「{id}」（按无边框处理）");
            return null;
        }

        VNPhotoBackdropDef FindBackdrop(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var b in backdrops)
                if (b != null && b.backdropId == id) return b;
            Debug.LogWarning($"[VNPhoto] 背景库里没有「{id}」（按无背景处理）");
            return null;
        }

        // ==================================================================
        // 界面搭建
        // ==================================================================

        void BuildUi()
        {
            var root = (RectTransform)transform;

            // 半透明背板：同时吃掉所有穿透到舞台的点击
            var backdrop = VNPhotoBoothUi.CreateImage("Backdrop", root, null,
                VNPhotoBoothUi.Backdrop, true);
            VNPhotoBoothUi.Stretch((RectTransform)backdrop.transform);

            // 机身
            var machine = VNPhotoBoothUi.CreateImage("Machine", root,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.MachineBody, true);
            var machineRect = VNPhotoBoothUi.Center((RectTransform)machine.transform,
                new Vector2(MachineW, MachineH), Vector2.zero);

            var inner = VNPhotoBoothUi.CreateImage("Inner", machineRect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.MachineInner);
            VNPhotoBoothUi.Center((RectTransform)inner.transform,
                new Vector2(MachineW - 60f, MachineH - 60f), Vector2.zero);

            _machine = machineRect;

            BuildTitleBar(machineRect);
            BuildViewFinder(machineRect);
            BuildLeftPanel(machineRect);
            BuildRightPanel(machineRect);
            BuildBottomBar(machineRect);
            BuildHelpPanel(machineRect);   // 最后建 = 展开时压在所有面板之上

            // 倒数数字：压在取景框中央（拍照那一刻会被藏起来，不会入镜）
            _countdownText = VNPhotoBoothUi.CreateText("Countdown", machineRect, 220,
                VNPhotoBoothUi.CountdownPink, "");
            _countdownText.fontStyle = FontStyles.Bold;
            _countdownText.outlineWidth = 0.22f;      // 白描边，压在任何背景上都看得清
            _countdownText.outlineColor = new Color32(255, 255, 255, 235);
            VNPhotoBoothUi.Center((RectTransform)_countdownText.transform,
                new Vector2(400f, 400f), new Vector2(0f, ViewY));
            _countdownText.gameObject.SetActive(false);

            // 闪光层：铺满整屏，快门瞬间白一下
            _flash = VNPhotoBoothUi.CreateImage("Flash", root, null,
                new Color(1f, 1f, 1f, 0f));
            VNPhotoBoothUi.Stretch((RectTransform)_flash.transform);
            _flash.raycastTarget = false;
        }

        void BuildTitleBar(RectTransform parent)
        {
            var title = VNPhotoBoothUi.CreateText("Title", parent, 42,
                VNPhotoBoothUi.AccentSoft, _title, TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)title.transform, new Vector2(500f, 60f),
                new Vector2(-MachineW * 0.5f + 300f, TitleY));

            string themeLine = _freeMode
                ? VNLocale.T("photo.free")
                : (_theme.hint != null && !_theme.hint.Empty
                    ? _theme.hint.Display
                    : VNLocale.T("photo.theme", _theme.DisplayName));
            var hint = VNPhotoBoothUi.CreateText("ThemeHint", parent, 28,
                Color.white, themeLine, TextAlignmentOptions.Center);
            VNPhotoBoothUi.Center((RectTransform)hint.transform, new Vector2(700f, 50f),
                new Vector2(0f, TitleY));

            // 限时条（不限时就整条不建）。
            // 右上角那块归说明钮，所以限时条整体往左让出 100px。
            if (_timeLimit <= 0f) return;

            var barBg = VNPhotoBoothUi.CreateImage("TimerBg", parent,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.18f));
            var barRect = VNPhotoBoothUi.Center((RectTransform)barBg.transform,
                new Vector2(360f, 20f), new Vector2(MachineW * 0.5f - 300f, TitleY - 6f));

            var fill = VNPhotoBoothUi.CreateImage("TimerFill", barRect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.AccentSoft);
            _timerFill = (RectTransform)fill.transform;
            _timerFill.anchorMin = new Vector2(0f, 0f);
            _timerFill.anchorMax = new Vector2(0f, 1f);
            _timerFill.pivot = new Vector2(0f, 0.5f);
            _timerFill.offsetMin = Vector2.zero;
            _timerFill.offsetMax = Vector2.zero;
            _timerFill.sizeDelta = new Vector2(360f, 0f);

            // 秒数摆在条的左侧（右侧被说明钮占了），右对齐贴着条的左端
            _timerText = VNPhotoBoothUi.CreateText("TimerText", parent, 26, Color.white,
                Mathf.CeilToInt(_timeLeft).ToString(), TextAlignmentOptions.Right);
            VNPhotoBoothUi.Center((RectTransform)_timerText.transform, new Vector2(90f, 40f),
                new Vector2(MachineW * 0.5f - 537f, TitleY - 4f));
        }

        // ==================================================================
        // 操作说明（右上角「?」，点一下展开卡片）
        // ==================================================================

        /// <summary>
        /// 说明卡片整组（钮 + 卡片）。拍照时要整组藏掉——
        /// 卡片是往左下展开的，会盖住取景框右上角，不藏就会入镜。
        /// </summary>
        GameObject _helpRoot;
        CanvasGroup _helpCard;
        bool _helpOpen;

        void BuildHelpPanel(RectTransform parent)
        {
            var root = VNPhotoBoothUi.CreateNode("Help", parent);
            VNPhotoBoothUi.Stretch(root);
            _helpRoot = root.gameObject;

            // ---- 折叠钮 ----
            var button = VNPhotoBoothUi.CreateImage("HelpButton", root,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.Accent, true);
            var buttonRect = VNPhotoBoothUi.Center((RectTransform)button.transform,
                new Vector2(54f, 54f), new Vector2(MachineW * 0.5f - 64f, TitleY));
            var icon = VNPhotoBoothUi.CreateText("HelpIcon", buttonRect, 34, Color.white, "?");
            VNPhotoBoothUi.Stretch((RectTransform)icon.transform);

            var toggle = button.gameObject.AddComponent<Button>();
            toggle.targetGraphic = button;
            toggle.onClick.AddListener(ToggleHelp);

            // ---- 展开的卡片 ----
            // 底色必须**不透明**：它会盖在右栏的表情格上，留一点透明度就变成
            // 一层脏兮兮的滤镜，字反而看不清
            var card = VNPhotoBoothUi.CreateImage("HelpCard", root,
                VNProceduralTextures.RoundedRectSprite,
                new Color(0.13f, 0.13f, 0.17f, 1f), true);
            var cardRect = (RectTransform)card.transform;
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(1f, 1f);      // 右上角钉在钮的下方，向左下展开

            var titleText = VNPhotoBoothUi.CreateText("HelpTitle", cardRect, 26,
                VNPhotoBoothUi.AccentSoft, VNLocale.T("photo.help.title"),
                TextAlignmentOptions.TopLeft);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(24f, 0f);
            titleRect.offsetMax = new Vector2(-24f, -18f);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 34f);

            var bodyText = VNPhotoBoothUi.CreateText("HelpBody", cardRect, 22,
                new Color(1f, 1f, 1f, 0.85f), VNLocale.T("photo.help.body"),
                TextAlignmentOptions.TopLeft);
            bodyText.lineSpacing = 8f;
            var bodyRect = (RectTransform)bodyText.transform;
            bodyRect.anchorMin = new Vector2(0f, 1f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.pivot = new Vector2(0.5f, 1f);
            bodyRect.offsetMin = new Vector2(24f, 0f);
            bodyRect.offsetMax = new Vector2(-24f, -60f);

            // 卡片高度按正文实测（TMP + ContentSizeFitter 首帧量不准，手工量更稳）
            const float cardWidth = 440f;
            cardRect.sizeDelta = new Vector2(cardWidth, 400f);
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, 400f);
            bodyText.ForceMeshUpdate();
            float bodyHeight = bodyText.preferredHeight;
            bodyRect.sizeDelta = new Vector2(bodyRect.sizeDelta.x, bodyHeight);
            cardRect.sizeDelta = new Vector2(cardWidth, bodyHeight + 82f);
            cardRect.anchoredPosition = new Vector2(
                MachineW * 0.5f - 38f, TitleY - 40f);

            _helpCard = card.gameObject.AddComponent<CanvasGroup>();
            _helpCard.alpha = 0f;
            _helpCard.blocksRaycasts = false;
            card.gameObject.SetActive(false);
        }

        /// <summary>点「?」开合。展开时卡片自己吃掉射线，免得点到底下的取景框</summary>
        void ToggleHelp()
        {
            if (_helpCard == null) return;
            _helpOpen = !_helpOpen;

            var rect = (RectTransform)_helpCard.transform;
            _helpCard.gameObject.SetActive(true);
            _helpCard.blocksRaycasts = _helpOpen;
            _helpCard.DOKill();
            rect.DOKill();

            if (_helpOpen)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, TitleY - 26f);

            _helpCard.DOFade(_helpOpen ? 1f : 0f, 0.16f)
                .SetUpdate(true).SetLink(_helpCard.gameObject)
                .OnComplete(() =>
                {
                    if (!_helpOpen) _helpCard.gameObject.SetActive(false);
                });
            rect.DOAnchorPosY(_helpOpen ? TitleY - 40f : TitleY - 26f, 0.16f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(_helpCard.gameObject);
        }

        void BuildViewFinder(RectTransform parent)
        {
            _viewFinder = VNPhotoBoothUi.CreateNode("ViewFinder", parent);
            VNPhotoBoothUi.Center(_viewFinder, new Vector2(ViewW, ViewH), new Vector2(0f, ViewY));

            _frameBack = VNPhotoBoothUi.CreateImage("FrameBack", _viewFinder, null, Color.white);
            VNPhotoBoothUi.Stretch((RectTransform)_frameBack.transform);

            // 人物开窗：Image + Mask，立绘作为它的子节点被裁进形状里
            _windowImage = VNPhotoBoothUi.CreateImage("Window", _viewFinder, null, Color.white);
            _window = (RectTransform)_windowImage.transform;
            var mask = _windowImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;   // 遮罩图本身就是开窗底色

            // ★ 开窗内的层序（从后往前）：背景 → 拖动板 → 人后贴纸 → 我 → 她
            //   拖动板压在最底下是刻意的：它 raycastTarget=true 会吃掉射线，
            //   放上面的话「人后贴纸」就永远点不到了。人物本身不接收射线，不用担心被挡。
            _backdropImage = VNPhotoBoothUi.CreateImage("Backdrop", _window, null, Color.white);

            var dragPad = VNPhotoBoothUi.CreateImage("DragPad", _window, null,
                new Color(0f, 0f, 0f, 0f), true);
            VNPhotoBoothUi.Stretch((RectTransform)dragPad.transform);
            _dragger = dragPad.gameObject.AddComponent<VNPhotoPortraitDragger>();
            _dragger.onChanged = RefreshPortraits;
            _dragger.onDoubleClick = BringPortraitToFront;
            _dragger.onBackdropChanged = () => ApplyBackdrop(_backdrop);

            // 人后贴纸层：尺寸与取景框一致（超出开窗的部分被 Mask 裁掉，正好），
            // 这样它与人前贴纸层坐标系相同，翻层时贴纸不会跳位
            _backStickerLayer = VNPhotoBoothUi.CreateNode("BackStickerLayer", _window);
            VNPhotoBoothUi.Center(_backStickerLayer, new Vector2(ViewW, ViewH), Vector2.zero);

            _meImage = VNPhotoBoothUi.CreateImage("MePortrait", _window, null, Color.white);
            _herImage = VNPhotoBoothUi.CreateImage("HerPortrait", _window, null, Color.white);
            _dragger.me = (RectTransform)_meImage.transform;
            _dragger.her = (RectTransform)_herImage.transform;

            // 描边环必须在 Mask 外面，否则会被自己裁掉
            _windowRing = VNPhotoBoothUi.CreateImage("WindowRing", _viewFinder, null, Color.white);

            _frameFront = VNPhotoBoothUi.CreateImage("FrameFront", _viewFinder, null, Color.white);
            VNPhotoBoothUi.Stretch((RectTransform)_frameFront.transform);

            _watermark = VNPhotoBoothUi.CreateText("Watermark", _viewFinder, 34, Color.white, "");
            VNPhotoBoothUi.Center((RectTransform)_watermark.transform,
                new Vector2(ViewW - 60f, 60f), new Vector2(0f, -ViewH * 0.5f + 52f));

            // 贴纸层在最上：贴纸可以压到边框上（参考图里的爱心就在框上）
            _stickerLayer = VNPhotoBoothUi.CreateNode("StickerLayer", _viewFinder);
            VNPhotoBoothUi.Stretch(_stickerLayer);

            // 涂鸦层再压一层：笔是画在「洗好的照片」上的，盖住一切
            var doodleRoot = VNPhotoBoothUi.CreateNode("DoodleLayer", _viewFinder);
            VNPhotoBoothUi.Stretch(doodleRoot);
            _doodle = new VNPhotoDoodle();
            _doodle.Build(doodleRoot);
        }

        void BuildLeftPanel(RectTransform parent)
        {
            var panel = VNPhotoBoothUi.CreateImage("LeftPanel", parent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.PanelBg, true);
            var rect = VNPhotoBoothUi.Center((RectTransform)panel.transform,
                new Vector2(PanelW, PanelH), new Vector2(-PanelX, PanelY));
            _leftPanel = panel.gameObject;

            // 顶部四个标签：边框 / 背景 / 贴纸 / 涂鸦
            const float tabW = 76f;
            var tabs = new List<(Button button, TextMeshProUGUI text, GameObject page)>();
            float tabY = PanelH * 0.5f - 36f;
            float[] tabX = { -117f, -39f, 39f, 117f };
            string[] tabKeys =
            {
                "photo.tab.frame", "photo.tab.backdrop",
                "photo.tab.sticker", "photo.tab.doodle",
            };

            var buttons = new Button[4];
            var labels = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                buttons[i] = VNPhotoBoothUi.CreateButton($"Tab{i}", rect,
                    new Vector2(tabW, 52f), new Vector2(tabX[i], tabY),
                    VNLocale.T(tabKeys[i]),
                    i == 0 ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg,
                    i == 0 ? Color.white : VNPhotoBoothUi.TextDark, 22, out labels[i]);
            }

            var frameScroll = VNPhotoBoothUi.CreateScrollList("FrameList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 1,
                new Vector2(300f, 176f), 12f, out _frameContent);
            var backdropScroll = VNPhotoBoothUi.CreateScrollList("BackdropList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 1,
                new Vector2(300f, 176f), 12f, out _backdropContent);
            var stickerScroll = VNPhotoBoothUi.CreateScrollList("StickerList", rect,
                new Vector2(ListW, ListH), new Vector2(0f, ListY), 2,
                new Vector2(144f, 144f), 12f, out _stickerContent);
            var doodlePage = BuildDoodlePage(rect);
            backdropScroll.gameObject.SetActive(false);
            stickerScroll.gameObject.SetActive(false);
            doodlePage.SetActive(false);

            tabs.Add((buttons[0], labels[0], frameScroll.gameObject));
            tabs.Add((buttons[1], labels[1], backdropScroll.gameObject));
            tabs.Add((buttons[2], labels[2], stickerScroll.gameObject));
            tabs.Add((buttons[3], labels[3], doodlePage));

            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i;
                tabs[i].button.onClick.AddListener(() => SelectTab(tabs, index));
            }

            BuildFrameCells();
            BuildBackdropCells();
            BuildStickerCells();
        }

        void SelectTab(
            List<(Button button, TextMeshProUGUI text, GameObject page)> tabs, int active)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == active;
                tabs[i].page.SetActive(on);
                if (tabs[i].button.targetGraphic is Image image)
                    image.color = on ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
                tabs[i].text.color = on ? Color.white : VNPhotoBoothUi.TextDark;
            }

            // 只有停在「涂鸦」页时画布才吃鼠标，否则挡住人物与贴纸的操作
            _doodle?.SetInteractive(active == 3);
        }

        // ==================================================================
        // 涂鸦工具页
        // ==================================================================

        static readonly Color[] PenColors =
        {
            new Color(1f, 0.35f, 0.55f), new Color(1f, 0.55f, 0.25f),
            new Color(1f, 0.85f, 0.25f), new Color(0.55f, 0.85f, 0.35f),
            new Color(0.3f, 0.78f, 0.72f), new Color(0.35f, 0.6f, 1f),
            new Color(0.65f, 0.45f, 1f), new Color(1f, 0.45f, 0.85f),
            new Color(1f, 1f, 1f), new Color(0.25f, 0.22f, 0.28f),
            new Color(0.75f, 0.55f, 0.4f), new Color(0.6f, 0.85f, 1f),
        };

        GameObject BuildDoodlePage(RectTransform parent)
        {
            var page = VNPhotoBoothUi.CreateNode("DoodlePage", parent);
            VNPhotoBoothUi.Center(page, new Vector2(ListW, ListH), new Vector2(0f, ListY));

            float y = ListH * 0.5f - 40f;

            // ---- 颜色格 4×3 ----
            var colorCells = new List<Image>();
            for (int i = 0; i < PenColors.Length; i++)
            {
                int row = i / 4, col = i % 4;
                var cell = VNPhotoBoothUi.CreateImage($"Pen{i}", page,
                    VNProceduralTextures.RoundedRectSprite, PenColors[i], true);
                VNPhotoBoothUi.Center((RectTransform)cell.transform, new Vector2(76f, 76f),
                    new Vector2(-120f + col * 80f, y - row * 96f));

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = cell;
                var captured = PenColors[i];
                int index = i;
                button.onClick.AddListener(() =>
                {
                    _doodle.penColor = captured;
                    _doodle.eraser = false;
                    RefreshPenUi(colorCells, index);
                });
                colorCells.Add(cell);
            }
            y -= 2 * 96f + 94f;

            // ---- 笔粗滑块（自由调，不是几个档位）----
            var sizeLabel = VNPhotoBoothUi.CreateText("SizeLabel", page, 24,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.pen.size"),
                TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)sizeLabel.transform,
                new Vector2(150f, 32f), new Vector2(-68f, y));

            _penPreview = VNPhotoBoothUi.CreateImage("PenPreview", page,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.TextDark);
            VNPhotoBoothUi.Center((RectTransform)_penPreview.transform,
                new Vector2(24f, 24f), new Vector2(120f, y));

            y -= 50f;
            var slider = VNPhotoBoothUi.CreateSlider("PenSize", page,
                new Vector2(292f, 32f), new Vector2(0f, y), 2f, 40f, _doodle.penSize);
            slider.onValueChanged.AddListener(v =>
            {
                _doodle.penSize = v;
                UpdatePenPreview();
            });
            UpdatePenPreview();

            y -= 84f;

            // ---- 荧光笔 / 橡皮 ----
            var glowButton = VNPhotoBoothUi.CreateButton("GlowPen", page,
                new Vector2(144f, 60f), new Vector2(-76f, y),
                VNLocale.T("photo.pen.glow"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out var glowText);
            var eraserButton = VNPhotoBoothUi.CreateButton("Eraser", page,
                new Vector2(144f, 60f), new Vector2(76f, y),
                VNLocale.T("photo.pen.eraser"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out var eraserText);

            glowButton.onClick.AddListener(() =>
            {
                _doodle.glowPen = !_doodle.glowPen;
                _doodle.eraser = false;
                RefreshToolButtons(glowButton, glowText, eraserButton, eraserText);
            });
            eraserButton.onClick.AddListener(() =>
            {
                _doodle.eraser = !_doodle.eraser;
                RefreshToolButtons(glowButton, glowText, eraserButton, eraserText);
            });

            y -= 88f;

            // ---- 撤销 / 清空 ----
            _undoButton = VNPhotoBoothUi.CreateButton("Undo", page,
                new Vector2(144f, 60f), new Vector2(-76f, y),
                VNLocale.T("photo.pen.undo"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out _);
            _undoButton.onClick.AddListener(() => _doodle.Undo());

            VNPhotoBoothUi.CreateButton("ClearDoodle", page,
                new Vector2(144f, 60f), new Vector2(76f, y),
                VNLocale.T("photo.pen.clear"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 24, out _)
                .onClick.AddListener(() => _doodle.Clear());

            // 说明跟在工具区下面（不钉到页底，否则中间空一大块）
            y -= 100f;
            var hint = VNPhotoBoothUi.CreateText("DoodleHint", page, 21,
                new Color(0.45f, 0.4f, 0.45f), VNLocale.T("photo.pen.hint"));
            VNPhotoBoothUi.Center((RectTransform)hint.transform,
                new Vector2(300f, 70f), new Vector2(0f, y));

            RefreshPenUi(colorCells, 0);
            return page.gameObject;
        }

        void UpdatePenPreview()
        {
            if (_penPreview == null) return;
            // 画布 768 宽显示成 1040，所以预览点按同一比例放大才是「所见即所得」
            float shown = Mathf.Clamp(_doodle.penSize * 2f * (ViewW / VNPhotoDoodle.Width),
                6f, 56f);
            ((RectTransform)_penPreview.transform).sizeDelta = new Vector2(shown, shown);
            _penPreview.color = _doodle.eraser ? new Color(0.6f, 0.6f, 0.62f)
                : _doodle.penColor;
        }

        void RefreshPenUi(List<Image> cells, int active)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var rect = (RectTransform)cells[i].transform;
                rect.localScale = Vector3.one * (i == active && !_doodle.eraser ? 1.16f : 1f);
            }
            UpdatePenPreview();
        }

        void RefreshToolButtons(Button glow, TextMeshProUGUI glowText,
            Button eraser, TextMeshProUGUI eraserText)
        {
            if (glow.targetGraphic is Image glowImage)
                glowImage.color = _doodle.glowPen && !_doodle.eraser
                    ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
            glowText.color = _doodle.glowPen && !_doodle.eraser
                ? Color.white : VNPhotoBoothUi.TextDark;

            if (eraser.targetGraphic is Image eraserImage)
                eraserImage.color = _doodle.eraser
                    ? VNPhotoBoothUi.Accent : VNPhotoBoothUi.CellBg;
            eraserText.color = _doodle.eraser ? Color.white : VNPhotoBoothUi.TextDark;

            UpdatePenPreview();
        }

        /// <summary>背景列表：第一项固定是「无背景」（露出边框自己的开窗底色）</summary>
        void BuildBackdropCells()
        {
            _backdropCells.Clear();
            AddBackdropCell(null, VNLocale.T("photo.backdrop.none"));
            foreach (var def in backdrops)
                if (def != null) AddBackdropCell(def, def.DisplayName);
        }

        void AddBackdropCell(VNPhotoBackdropDef def, string label)
        {
            var cell = VNPhotoBoothUi.CreateImage($"BackdropCell_{label}", _backdropContent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg, true);
            var rect = (RectTransform)cell.transform;

            if (def != null)
            {
                var preview = VNPhotoBoothUi.CreateImage("Preview", rect,
                    def.ResolveSprite(), Color.white);
                VNPhotoBoothUi.Center((RectTransform)preview.transform,
                    new Vector2(276f, 112f), new Vector2(0f, 20f));
            }

            var text = VNPhotoBoothUi.CreateText("Label", rect, 26,
                VNPhotoBoothUi.TextDark, label);
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(276f, 40f),
                new Vector2(0f, def != null ? -60f : 0f));

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            var captured = def;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                // 换了图构图就不一样了，上一张调好的位移/缩放没有意义，重置
                if (_dragger != null)
                {
                    _dragger.backdropOffset = Vector2.zero;
                    _dragger.backdropScale = 1f;
                }
                ApplyBackdrop(captured);
                RefreshBackdropCells();
            });

            _backdropCells.Add(cell);
        }

        void RefreshBackdropCells()
        {
            for (int i = 0; i < _backdropCells.Count; i++)
            {
                if (_backdropCells[i] == null) continue;
                bool selected = i == 0 ? _backdrop == null
                    : i - 1 < backdrops.Count && backdrops[i - 1] == _backdrop;
                _backdropCells[i].color = selected
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }

        /// <summary>边框列表：第一项固定是「默认」（不加边框），对应参考实现的 0 号</summary>
        void BuildFrameCells()
        {
            _frameCells.Clear();
            AddFrameCell(null, VNLocale.T("photo.frame.none"));
            foreach (var frame in frames)
                if (frame != null) AddFrameCell(frame, frame.DisplayName);
        }

        void AddFrameCell(VNPhotoFrameDef def, string label)
        {
            var cell = VNPhotoBoothUi.CreateImage($"FrameCell_{label}", _frameContent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg, true);
            var rect = (RectTransform)cell.transform;

            if (def != null)
            {
                var preview = VNPhotoBoothUi.CreateImage("Preview", rect,
                    def.ResolveFrameSprite(), Color.white);
                VNPhotoBoothUi.Center((RectTransform)preview.transform,
                    new Vector2(276f, 112f), new Vector2(0f, 20f));
            }

            var text = VNPhotoBoothUi.CreateText("Label", rect, 26,
                VNPhotoBoothUi.TextDark, label);
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(276f, 40f),
                new Vector2(0f, def != null ? -60f : 0f));

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            var captured = def;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                ApplyFrame(captured);
                RefreshFrameCells();
            });

            _frameCells.Add(cell);
        }

        void BuildStickerCells()
        {
            foreach (var sticker in stickers)
            {
                if (sticker == null) continue;
                var cell = VNPhotoBoothUi.CreateImage($"StickerCell_{sticker.stickerId}",
                    _stickerContent, VNProceduralTextures.RoundedRectSprite,
                    VNPhotoBoothUi.CellBg, true);
                var rect = (RectTransform)cell.transform;

                var icon = VNPhotoBoothUi.CreateImage("Icon", rect,
                    sticker.ResolveSprite(), sticker.tint);
                icon.preserveAspect = true;
                VNPhotoBoothUi.Center((RectTransform)icon.transform,
                    new Vector2(96f, 96f), Vector2.zero);

                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = cell;
                var captured = sticker;
                button.onClick.AddListener(() =>
                {
                    if (_phase != Phase.Dressing) return;
                    SpawnSticker(captured, RandomSpawnPos(), 1f, 0f, false);
                });
            }

            if (stickers.Count != 0) return;
            var empty = VNPhotoBoothUi.CreateText("EmptyHint", _stickerContent, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.sticker.empty"));
            ((RectTransform)empty.transform).sizeDelta = new Vector2(260f, 80f);
        }

        void BuildRightPanel(RectTransform parent)
        {
            var panel = VNPhotoBoothUi.CreateImage("RightPanel", parent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.PanelBg, true);
            var rect = VNPhotoBoothUi.Center((RectTransform)panel.transform,
                new Vector2(PanelW, PanelH), new Vector2(PanelX, PanelY));
            _rightPanel = panel.gameObject;

            var title = VNPhotoBoothUi.CreateText("RightTitle", rect, 28,
                Color.white, VNLocale.T("photo.expression"));
            var titleBg = VNPhotoBoothUi.CreateImage("RightTitleBg", rect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.Accent);
            VNPhotoBoothUi.Center((RectTransform)titleBg.transform,
                new Vector2(310f, 52f), new Vector2(0f, PanelH * 0.5f - 36f));
            ((RectTransform)title.transform).SetAsLastSibling();
            VNPhotoBoothUi.Center((RectTransform)title.transform,
                new Vector2(310f, 52f), new Vector2(0f, PanelH * 0.5f - 36f));

            // 列标题：左列是我、右列是她（不然两栏头像分不清谁是谁）
            var meLabel = VNPhotoBoothUi.CreateText("ColMe", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.me"));
            VNPhotoBoothUi.Center((RectTransform)meLabel.transform, new Vector2(140f, 30f),
                new Vector2(-78f, PanelH * 0.5f - 78f));
            var herLabel = VNPhotoBoothUi.CreateText("ColHer", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.her"));
            VNPhotoBoothUi.Center((RectTransform)herLabel.transform, new Vector2(140f, 30f),
                new Vector2(78f, PanelH * 0.5f - 78f));

            var scroll = VNPhotoBoothUi.CreateScrollList("FaceList", rect,
                new Vector2(ListW, PanelH - 120f), new Vector2(0f, -50f), 2,
                new Vector2(FaceCellSize, FaceCellSize), 10f, out var content);

            // 两列：左列是「我」，右列是「她」。行数取两边表情数的较大值，缺的补空格子。
            int meCount = _meDef?.expressions?.Count ?? 0;
            int herCount = _herDef?.expressions?.Count ?? 0;
            int rows = Mathf.Max(meCount, herCount);

            for (int i = 0; i < rows; i++)
            {
                AddFaceCell(content, _meDef, i < meCount ? _meDef.expressions[i].name : null,
                    true, i);
                AddFaceCell(content, _herDef, i < herCount ? _herDef.expressions[i].name : null,
                    false, i);
            }
            scroll.enabled = rows > FaceRowsPerPage;
        }

        void AddFaceCell(RectTransform parent, VNCharacterDef def, string expression,
            bool isMe, int index)
        {
            var cell = VNPhotoBoothUi.CreateImage($"Face_{(isMe ? "me" : "her")}_{index}",
                parent, VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.CellBg,
                expression != null);
            var rect = (RectTransform)cell.transform;

            if (def == null || expression == null)
            {
                cell.color = new Color(1f, 1f, 1f, 0.12f);
                (isMe ? _meCells : _herCells).Add(null);
                return;
            }

            // 裁一个方窗把脸框进去（和取景框同一套 portrait 参数）
            var clip = VNPhotoBoothUi.CreateNode("Clip", rect);
            VNPhotoBoothUi.Center(clip, new Vector2(FaceClipSize, FaceClipSize), Vector2.zero);
            clip.gameObject.AddComponent<RectMask2D>();

            var face = VNPhotoBoothUi.CreateImage("Face", clip, null, Color.white);
            // 格子要看清表情 → 比取景框拉得更近（脸怼满格子）
            VNPhotoBoothUi.ApplyPortrait(face, def, expression, FaceClipSize, FaceCellFit,
                AnchorFor(def), Vector2.zero, false);

            var button = cell.gameObject.AddComponent<Button>();
            button.targetGraphic = cell;
            string capturedExpr = expression;
            button.onClick.AddListener(() =>
            {
                if (_phase != Phase.Dressing) return;
                if (isMe) _meExpr = capturedExpr; else _herExpr = capturedExpr;
                RefreshPortraits();
                RefreshExpressionCells();
            });

            (isMe ? _meCells : _herCells).Add(cell);
        }

        void BuildBottomBar(RectTransform parent)
        {
            // 操作说明搬去了右上角的「?」，这条只剩快门
            var bar = VNPhotoBoothUi.CreateNode("BottomBar", parent);
            VNPhotoBoothUi.Center(bar, new Vector2(MachineW, 120f),
                new Vector2(0f, -MachineH * 0.5f + 74f));
            _bottomBar = bar.gameObject;

            var shutter = VNPhotoBoothUi.CreateImage("Shutter", bar,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.Accent, true);
            var shutterRect = VNPhotoBoothUi.Center((RectTransform)shutter.transform,
                new Vector2(112f, 112f), Vector2.zero);

            var icon = VNPhotoBoothUi.CreateText("ShutterIcon", shutterRect, 48,
                Color.white, "◉");
            VNPhotoBoothUi.Center((RectTransform)icon.transform, new Vector2(112f, 112f),
                Vector2.zero);

            _shutterButton = shutter.gameObject.AddComponent<Button>();
            _shutterButton.targetGraphic = shutter;
            _shutterButton.onClick.AddListener(Shoot);
        }

        // ==================================================================
        // 装扮
        // ==================================================================

        void ApplyFrame(VNPhotoFrameDef def)
        {
            _frame = def;

            // 底图
            if (def != null)
            {
                _frameBack.sprite = def.ResolveFrameSprite();
                _frameBack.color = Color.white;
            }
            else
            {
                _frameBack.sprite = null;
                _frameBack.color = new Color(0.97f, 0.97f, 0.98f, 1f);
            }

            // 开窗
            var maskShape = def != null ? def.maskShape : VNPhotoMaskShape.None;
            Vector2 maskSize = def != null ? def.maskSize : Vector2.one;
            Vector2 maskOffset = def != null ? def.maskOffset : Vector2.zero;
            var windowSize = new Vector2(ViewW * Mathf.Clamp01(maskSize.x),
                                         ViewH * Mathf.Clamp01(maskSize.y));
            var windowPos = new Vector2(ViewW * maskOffset.x, ViewH * maskOffset.y);

            _windowImage.sprite = VNPhotoTextures.MaskSprite(maskShape);
            _windowImage.color = def != null ? def.windowColor : new Color(0.87f, 0.94f, 1f, 1f);
            VNPhotoBoothUi.Center(_window, windowSize, windowPos);

            float edge = def != null ? def.maskEdgeWidth : 0f;
            var ringColor = def != null ? def.maskEdgeColor : new Color(0f, 0f, 0f, 0f);
            _windowRing.sprite = VNPhotoTextures.MaskRingSprite(maskShape);
            _windowRing.color = ringColor;
            _windowRing.enabled = ringColor.a > 0.01f && edge > 0f;

            ApplyBackdrop(_backdrop);   // 开窗尺寸变了，背景要重新按 cover 铺一次

            // 人后贴纸层要抵消开窗偏移，才能与人前贴纸层共用同一套坐标
            //（否则边框换成偏心开窗时，翻层的贴纸会整体跳位）
            if (_backStickerLayer != null)
                _backStickerLayer.anchoredPosition = -windowPos;

            // 人物能被拖多远，跟着开窗大小走。
            // ★ x 必须给到 0.75W：基准站位在 ±0.275W，要让左边那个能越过中线走到
            //   右边去（换位），单侧行程至少得有 0.55W，再留点余量才够用。
            if (_dragger != null)
                _dragger.bounds = new Vector2(windowSize.x * 0.75f, windowSize.y * 0.55f);
            VNPhotoBoothUi.Center((RectTransform)_windowRing.transform,
                windowSize + Vector2.one * edge, windowPos);

            // 前景与水印
            var front = def != null ? def.frontSprite : null;
            _frameFront.sprite = front;
            _frameFront.enabled = front != null;

            string mark = def != null ? def.DisplayWatermark : null;
            _watermark.text = mark ?? "";
            _watermark.color = def != null ? def.watermarkColor : Color.white;
            _watermark.gameObject.SetActive(!string.IsNullOrEmpty(mark));

            RebuildFrameDecorations();
            RefreshPortraits();
        }

        /// <summary>
        /// 换背景。图按 **cover** 铺满开窗（宁可裁掉两侧也不留白边、不拉变形）——
        /// 背景是「照相馆的背景布」，露白比裁切难看得多。
        /// </summary>
        void ApplyBackdrop(VNPhotoBackdropDef def)
        {
            _backdrop = def;
            if (_backdropImage == null) return;

            var sprite = def != null ? def.ResolveSprite() : null;
            if (sprite == null)
            {
                _backdropImage.enabled = false;   // 没选背景 = 露出边框的开窗底色
                return;
            }

            _backdropImage.enabled = true;
            _backdropImage.sprite = sprite;
            _backdropImage.preserveAspect = false;

            Vector2 window = _window != null ? _window.sizeDelta : new Vector2(ViewW, ViewH);
            float winAspect = window.y > 0f ? window.x / window.y : 1f;
            float imgAspect = sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height : winAspect;

            // cover 基准：刚好铺满开窗的尺寸（玩家的缩放倍率从这里往上乘）
            Vector2 cover = imgAspect > winAspect
                ? new Vector2(window.y * imgAspect, window.y)   // 图更宽 → 以高为准，裁两侧
                : new Vector2(window.x, window.x / imgAspect);  // 图更高 → 以宽为准，裁上下

            float scale = _dragger != null ? Mathf.Max(1f, _dragger.backdropScale) : 1f;
            Vector2 size = cover * scale;

            // 可移动范围 = 溢出开窗的那一半。scale=1 时某个方向恰好是 0，
            // 于是「不允许露边」这条规则天然由这个钳制保证，不需要额外判断。
            var slack = new Vector2(
                Mathf.Max(0f, (size.x - window.x) * 0.5f),
                Mathf.Max(0f, (size.y - window.y) * 0.5f));

            Vector2 offset = _dragger != null ? _dragger.backdropOffset : Vector2.zero;
            offset = new Vector2(
                Mathf.Clamp(offset.x, -slack.x, slack.x),
                Mathf.Clamp(offset.y, -slack.y, slack.y));
            if (_dragger != null) _dragger.backdropOffset = offset;   // 钳制后写回

            VNPhotoBoothUi.Center((RectTransform)_backdropImage.transform, size, offset);
        }

        /// <summary>边框自带装饰件：换边框时整批重建（它们属于边框，不计分）</summary>
        void RebuildFrameDecorations()
        {
            foreach (var go in _frameDecorations) if (go != null) Destroy(go);
            _frameDecorations.Clear();

            if (_frame == null || _frame.decorations == null) return;
            foreach (var deco in _frame.decorations)
            {
                if (deco == null || deco.sticker == null) continue;
                var item = SpawnSticker(deco.sticker, deco.position, deco.scale,
                    deco.rotation, true);
                if (item == null) continue;
                item.locked = deco.locked;
                _frameDecorations.Add(item.gameObject);
            }
        }

        VNPhotoStickerItem SpawnSticker(VNPhotoStickerDef def, Vector2 position, float scale,
            float rotation, bool fromFrame)
        {
            if (def == null) return null;

            var image = VNPhotoBoothUi.CreateImage($"Sticker_{def.stickerId}", _stickerLayer,
                def.ResolveSprite(), def.tint, true);
            image.preserveAspect = true;
            var rect = (RectTransform)image.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * def.defaultSize;

            var item = image.gameObject.AddComponent<VNPhotoStickerItem>();
            item.stickerId = def.stickerId;
            item.bounds = new Vector2(ViewW * 0.5f, ViewH * 0.5f);
            item.Place(position, scale, rotation);
            item.onDelete = RemoveSticker;
            item.onToggleLayer = ToggleStickerLayer;

            if (!fromFrame) _playerStickers.Add(item);
            return item;
        }

        /// <summary>
        /// 双击贴纸：在「人前」与「人后」之间翻转。
        /// 两个贴纸层尺寸都等于取景框，坐标系一致，所以换父节点后位置原样不动
        /// （人后那层在 Mask 里，超出开窗的部分会被裁掉——它已经在照片"里面"了）。
        /// </summary>
        void ToggleStickerLayer(VNPhotoStickerItem item)
        {
            if (_phase != Phase.Dressing || item == null || item.locked) return;
            if (_stickerLayer == null || _backStickerLayer == null) return;

            bool inFront = item.transform.parent == _stickerLayer;
            item.transform.SetParent(inFront ? _backStickerLayer : _stickerLayer, false);
            item.transform.SetAsLastSibling();
            _sfx?.Play(VNPhotoSfx.Kind.Place, inFront ? 0.8f : 1.15f);
        }

        /// <summary>双击某个人：把他提到另一个人前面</summary>
        void BringPortraitToFront(bool isMe)
        {
            if (_phase != Phase.Dressing) return;
            var target = isMe ? _meImage : _herImage;
            if (target == null || !target.enabled) return;
            target.transform.SetAsLastSibling();
            _sfx?.Play(VNPhotoSfx.Kind.Place, isMe ? 1.1f : 0.9f);
        }

        void RemoveSticker(VNPhotoStickerItem item)
        {
            if (item == null || _phase != Phase.Dressing) return;
            _playerStickers.Remove(item);
            _frameDecorations.Remove(item.gameObject);
            Destroy(item.gameObject);
        }

        Vector2 RandomSpawnPos()
        {
            // 落在取景框内偏外圈的位置，别一上来就糊住脸
            float x = Random.Range(0.24f, 0.42f) * ViewW * (Random.value < 0.5f ? -1f : 1f);
            float y = Random.Range(-0.34f, 0.34f) * ViewH;
            return new Vector2(x, y);
        }

        void RefreshPortraits()
        {
            // 两人各分半个开窗的宽度；单人时对方居中占满
            float windowWidth = _window != null ? _window.sizeDelta.x : ViewW;
            bool solo = _meDef == null;
            float slotWidth = solo ? windowWidth : windowWidth * 0.5f;
            float half = slotWidth * pairSpread * 0.5f;   // 肩膀轻微交叠是合影该有的样子

            // 玩家拖出来的偏移叠在基准站位上（切表情重摆时不会被冲掉）
            Vector2 meDrag = _dragger != null ? _dragger.meOffset : Vector2.zero;
            Vector2 herDrag = _dragger != null ? _dragger.herOffset : Vector2.zero;

            // 玩家滚轮缩出来的倍率叠在取景倍率上——素材尺寸不统一时靠它救场
            float meScale = _dragger != null ? _dragger.meScale : 1f;
            float herScale = _dragger != null ? _dragger.herScale : 1f;

            float meRot = _dragger != null ? _dragger.meRotation : 0f;
            float herRot = _dragger != null ? _dragger.herRotation : 0f;

            VNPhotoBoothUi.ApplyPortrait(_meImage, _meDef, _meExpr, slotWidth,
                FitFor(_meDef) * meScale, AnchorFor(_meDef),
                new Vector2(-half, 0f) + meDrag, mirrorMe, meRot);
            VNPhotoBoothUi.ApplyPortrait(_herImage, _herDef, _herExpr, slotWidth,
                FitFor(_herDef) * herScale, AnchorFor(_herDef),
                new Vector2(solo ? 0f : half, 0f) + herDrag, false, herRot);
        }

        PortraitTweak TweakFor(VNCharacterDef def)
        {
            if (def == null || portraitTweaks == null) return null;
            foreach (var t in portraitTweaks)
                if (t != null && t.characterId == def.id) return t;
            return null;
        }

        float FitFor(VNCharacterDef def)
        {
            var tweak = TweakFor(def);
            return tweak != null && tweak.fit > 0f ? tweak.fit : photoFit;
        }

        float AnchorFor(VNCharacterDef def)
        {
            var tweak = TweakFor(def);
            return tweak != null && tweak.faceAnchor >= 0f ? tweak.faceAnchor : faceAnchor;
        }

        void RefreshFrameCells()
        {
            for (int i = 0; i < _frameCells.Count; i++)
            {
                if (_frameCells[i] == null) continue;
                bool selected = i == 0 ? _frame == null
                    : i - 1 < frames.Count && frames[i - 1] == _frame;
                _frameCells[i].color = selected
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }

        void RefreshExpressionCells()
        {
            HighlightCells(_meCells, _meDef, _meExpr);
            HighlightCells(_herCells, _herDef, _herExpr);
        }

        void HighlightCells(List<Image> cells, VNCharacterDef def, string current)
        {
            if (def?.expressions == null) return;
            for (int i = 0; i < cells.Count && i < def.expressions.Count; i++)
            {
                if (cells[i] == null) continue;
                cells[i].color = def.expressions[i].name == current
                    ? VNPhotoBoothUi.CellSelected : VNPhotoBoothUi.CellBg;
            }
        }

        // ==================================================================
        // 限时 / 键盘
        // ==================================================================

        void Update()
        {
            if (_phase == Phase.Dressing) TickTimer();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (_phase == Phase.Dressing) ShowConfirm(true);
                else if (_phase == Phase.Confirm) ShowConfirm(false);
                else if (_phase == Phase.Result) Finish();   // 照片已经拍好了，ESC = 收下
            }

            // Ctrl+Z 撤销涂鸦（装扮阶段才有意义）
            if (_phase == Phase.Dressing && _doodle != null &&
                keyboard.zKey.wasPressedThisFrame &&
                (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
                _doodle.Undo();

            if (_undoButton != null && _doodle != null)
                _undoButton.interactable = _doodle.CanUndo;
        }

        void TickTimer()
        {
            if (_timeLimit <= 0f) return;

            _timeLeft -= Time.unscaledDeltaTime;   // 三铁律：不受快进 timeScale 影响
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                UpdateTimerUi();
                Shoot();                            // 超时自动快门
                return;
            }
            UpdateTimerUi();
        }

        void UpdateTimerUi()
        {
            if (_timerFill != null)
                _timerFill.sizeDelta = new Vector2(
                    360f * Mathf.Clamp01(_timeLeft / Mathf.Max(0.01f, _timeLimit)), 0f);

            if (_timerText == null) return;
            _timerText.text = Mathf.CeilToInt(_timeLeft).ToString();

            bool urgent = _timeLeft <= UrgentSeconds;
            _timerText.color = urgent ? VNPhotoBoothUi.Urgent : Color.white;
            if (_timerFill != null)
            {
                var fillImage = _timerFill.GetComponent<Image>();
                if (fillImage != null)
                    fillImage.color = urgent ? VNPhotoBoothUi.Urgent : VNPhotoBoothUi.AccentSoft;
            }
        }

        // ==================================================================
        // 放弃确认
        // ==================================================================

        void ShowConfirm(bool show)
        {
            if (show && _confirmLayer == null) BuildConfirmLayer();
            if (_confirmLayer == null) return;

            _confirmLayer.SetActive(show);
            _phase = show ? Phase.Confirm : Phase.Dressing;
        }

        void BuildConfirmLayer()
        {
            var root = (RectTransform)transform;
            var layer = VNPhotoBoothUi.CreateImage("ConfirmLayer", root, null,
                new Color(0f, 0f, 0f, 0.6f), true);
            VNPhotoBoothUi.Stretch((RectTransform)layer.transform);
            _confirmLayer = layer.gameObject;

            var box = VNPhotoBoothUi.CreateImage("Box", (RectTransform)layer.transform,
                VNProceduralTextures.RoundedRectSprite, new Color(0.16f, 0.15f, 0.19f, 0.98f), true);
            var boxRect = VNPhotoBoothUi.Center((RectTransform)box.transform,
                new Vector2(660f, 260f), Vector2.zero);

            var text = VNPhotoBoothUi.CreateText("Text", boxRect, 30, Color.white,
                VNLocale.T("photo.quit.confirm"));
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(600f, 100f),
                new Vector2(0f, 40f));

            VNPhotoBoothUi.CreateButton("Cancel", boxRect, new Vector2(220f, 66f),
                new Vector2(-130f, -62f), VNLocale.T("photo.quit.cancel"),
                VNPhotoBoothUi.Accent, Color.white, 28, out _)
                .onClick.AddListener(() => ShowConfirm(false));

            VNPhotoBoothUi.CreateButton("Quit", boxRect, new Vector2(220f, 66f),
                new Vector2(130f, -62f), VNLocale.T("photo.quit.ok"),
                new Color(0.75f, 0.28f, 0.32f, 1f), Color.white, 28, out _)
                .onClick.AddListener(QuitWithoutPhoto);
        }

        void QuitWithoutPhoto()
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            // 放弃 = 没有照片：自由拍照按「完成」收场，评分模式按 0 分失败
            if (_freeMode)
            {
                Done(VNPhotoScore.OutcomeFree);
                return;
            }
            WriteFlags(0, 0);
            Done(VNPhotoScore.OutcomeFail);
        }

        // ==================================================================
        // 快门
        // ==================================================================

        void Shoot()
        {
            if (_phase != Phase.Dressing) return;
            _phase = Phase.Shooting;
            if (_shutterButton != null) _shutterButton.interactable = false;
            StartCoroutine(ShootRoutine());
        }

        IEnumerator ShootRoutine()
        {
            // ---- 3・2・1 倒数 ----
            _countdownText.gameObject.SetActive(true);
            for (int n = 3; n >= 1; n--)
            {
                _countdownText.text = n.ToString();
                _sfx.Play(VNPhotoSfx.Kind.Tick, 1f + (3 - n) * 0.12f);   // 越数越急

                var rect = (RectTransform)_countdownText.transform;
                rect.localScale = Vector3.one * 1.6f;
                _countdownText.alpha = 1f;
                rect.DOScale(1f, 0.32f).SetEase(Ease.OutBack)
                    .SetUpdate(true).SetLink(gameObject);
                _countdownText.DOFade(0.25f, 0.85f)
                    .SetUpdate(true).SetLink(gameObject);

                yield return WaitUnscaled(1f);
            }
            _countdownText.gameObject.SetActive(false);

            // ---- 快门：闪白 + 咔嚓 + 机身一颤 ----
            _sfx.Play(VNPhotoSfx.Kind.Shutter);
            _flash.color = new Color(1f, 1f, 1f, 0.92f);
            _flash.DOFade(0f, 0.45f).SetEase(Ease.OutQuad)
                .SetUpdate(true).SetLink(gameObject);
            if (_machine != null)
                _machine.DOShakeAnchorPos(0.3f, 14f, 18, 90f, false, true)
                    .SetUpdate(true).SetLink(gameObject);

            // ---- 抓图（协程内部会把这些藏一帧，所以取景框上不会有杂物）----
            // ★ _flash 必须进这个列表：闪白要 0.45 秒才淡完，而抓图只等一帧，
            //   不藏它拍下来的就是一张白纱。闪光是"拍照瞬间"的表现，不该进照片。
            //   _helpRoot 同理：说明卡片是往左下展开的，会压住取景框右上角。
            var hide = new List<GameObject>
            {
                _leftPanel, _rightPanel, _bottomBar, _flash.gameObject, _helpRoot,
            };
            Texture2D shot = null;
            yield return VNPhotoCapture.Capture(_viewFinder, _canvas, hide, tex => shot = tex);

            _lastResult = VNPhotoScore.Evaluate(BuildDressing(), _theme);

            // 先存进相册；玩家点「重拍」再把这张删掉（只有留下的才算数）
            if (shot != null)
            {
                _shotEntry = VNPhotoAlbum.Add(shot, _herDef != null ? _herDef.id : "",
                    _meDef != null ? _meDef.id : "",
                    _theme != null ? _theme.themeId : "",
                    _lastResult.total, _freeMode ? -1 : _lastResult.grade);
                _shotTexture = shot;
                _shotSprite = Sprite.Create(shot, new Rect(0, 0, shot.width, shot.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _shotSprite.hideFlags = HideFlags.DontSave;
            }

            _phase = Phase.Result;
            yield return StartCoroutine(ShowResult());
        }

        static IEnumerator WaitUnscaled(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        // ==================================================================
        // 结算：相纸飞入 → 冲分 → 档位大字
        // ==================================================================

        IEnumerator ShowResult()
        {
            // 快门按钮会从半透明结算层后面透出来，正好卡在两个按钮中间——藏掉。
            // 左右两栏同理：分数栏与右栏的表情格横向重叠，加分项飘在头像上根本读不清。
            // 机身与取景框留着，相纸才有"从这儿飞出来"的连续感。
            if (_bottomBar != null) _bottomBar.SetActive(false);
            if (_leftPanel != null) _leftPanel.SetActive(false);
            if (_rightPanel != null) _rightPanel.SetActive(false);

            BuildResultLayer(out var paper, out var scoreText, out var barFill,
                out var hitList, out var gradeText, out var commentText);

            // ---- 相纸从取景框位置飞出来 ----
            paper.anchoredPosition = new Vector2(0f, ViewY);
            paper.localScale = Vector3.one * 0.42f;
            paper.localRotation = Quaternion.Euler(0f, 0f, -14f);
            paper.DOAnchorPos(new Vector2(_freeMode ? 0f : -430f, 20f), 0.55f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            paper.DOScale(1f, 0.55f).SetEase(Ease.OutBack)
                .SetUpdate(true).SetLink(gameObject);
            paper.DOLocalRotate(new Vector3(0f, 0f, -2.5f), 0.55f)
                .SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
            _sfx.Play(VNPhotoSfx.Kind.Place);

            yield return WaitUnscaled(0.65f);

            // 相纸落定后取景框就没用了：留着的话它那张亮底会从背板后面顶出来，
            // 跟分数、评语叠在一起。飞入过程要留着，所以只能等动画完再藏
            if (_viewFinder != null) _viewFinder.gameObject.SetActive(false);

            // 自由拍照：不评分，看一眼就完事
            if (_freeMode) yield break;

            // ---- 逐条弹出命中项，分数一路冲上去 ----
            int shown = 0;
            int max = Mathf.Max(_theme.perfectLine, _lastResult.total);
            int listed = 0;

            foreach (var hit in _lastResult.hits)
            {
                if (listed >= 6) break;      // 再多就刷屏了，剩下的合进总分
                listed++;
                shown += hit.score;

                SpawnHitRow(hitList, hit, listed);
                _sfx.Play(VNPhotoSfx.Kind.Count, 1f + listed * 0.06f);

                int from = shown - hit.score;
                int to = shown;
                DOTween.To(() => from, v => { from = v; scoreText.text = v.ToString(); },
                        to, 0.22f)
                    .SetUpdate(true).SetLink(gameObject);
                barFill.DOSizeDelta(
                        new Vector2(BarWidth * Mathf.Clamp01((float)to / max), 0f), 0.22f)
                    .SetEase(Ease.OutQuad).SetUpdate(true).SetLink(gameObject);

                yield return WaitUnscaled(0.26f);
            }

            scoreText.text = _lastResult.total.ToString();
            barFill.sizeDelta = new Vector2(
                BarWidth * Mathf.Clamp01((float)_lastResult.total / max), 0f);

            yield return WaitUnscaled(0.25f);

            // ---- 档位大字 ----
            bool good = _lastResult.grade >= 1;
            gradeText.text = GradeLabel(_lastResult.grade);
            gradeText.color = _lastResult.grade >= 2 ? new Color(1f, 0.85f, 0.35f)
                : _lastResult.grade == 1 ? new Color(0.62f, 0.88f, 1f)
                : new Color(0.85f, 0.6f, 0.65f);
            gradeText.gameObject.SetActive(true);

            var gradeRect = (RectTransform)gradeText.transform;
            gradeRect.localScale = Vector3.one * 2.2f;
            gradeRect.DOScale(1f, 0.4f).SetEase(Ease.OutBack)
                .SetUpdate(true).SetLink(gameObject);
            _sfx.Play(VNPhotoSfx.Kind.Fanfare, good ? 1f : 0.72f);

            if (_lastResult.grade >= 2) SparkleBurst(gradeRect);

            // ---- 评语：优先说命中项的细评，没有就用分档总评 ----
            string comment = !string.IsNullOrEmpty(_lastResult.bestComment)
                ? _lastResult.bestComment : _lastResult.gradeComment;
            if (!string.IsNullOrEmpty(comment))
            {
                yield return WaitUnscaled(0.3f);
                commentText.text = comment;
                commentText.alpha = 0f;
                commentText.DOFade(1f, 0.4f).SetUpdate(true).SetLink(gameObject);
            }
        }

        const float BarWidth = 520f;
        /// <summary>结算相纸（照片 780×585 + 上下白边 + 主题标题）</summary>
        const float PaperW = 860f;
        const float PaperH = 770f;

        string GradeLabel(int grade) => VNLocale.T(
            grade >= 2 ? "photo.grade.perfect"
            : grade == 1 ? "photo.grade.normal" : "photo.grade.fail");

        void SpawnHitRow(RectTransform list, VNPhotoScore.Hit hit, int index)
        {
            var row = VNPhotoBoothUi.CreateNode($"Hit{index}", list);
            VNPhotoBoothUi.Center(row, new Vector2(BarWidth, 38f),
                new Vector2(0f, -index * 42f + 90f));

            var label = VNPhotoBoothUi.CreateText("Label", row, 24,
                new Color(1f, 1f, 1f, 0.9f), hit.label, TextAlignmentOptions.Left);
            VNPhotoBoothUi.Center((RectTransform)label.transform,
                new Vector2(BarWidth - 110f, 38f), new Vector2(-55f, 0f));

            var value = VNPhotoBoothUi.CreateText("Value", row, 26,
                hit.score >= 0 ? new Color(1f, 0.82f, 0.4f) : new Color(1f, 0.5f, 0.5f),
                (hit.score >= 0 ? "+" : "") + hit.score, TextAlignmentOptions.Right);
            VNPhotoBoothUi.Center((RectTransform)value.transform,
                new Vector2(100f, 38f), new Vector2(BarWidth * 0.5f - 50f, 0f));

            row.anchoredPosition += new Vector2(40f, 0f);
            row.DOAnchorPosX(row.anchoredPosition.x - 40f, 0.25f)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>完美时的星光爆发。UI 层自绘，不去碰舞台粒子（事件模块三铁律）。</summary>
        void SparkleBurst(RectTransform center)
        {
            for (int i = 0; i < 8; i++)
            {
                var star = VNPhotoBoothUi.CreateImage($"Sparkle{i}", center,
                    VNProceduralTextures.SparkleSprite, new Color(1f, 0.92f, 0.6f, 1f));
                var rect = VNPhotoBoothUi.Center((RectTransform)star.transform,
                    Vector2.one * 48f, Vector2.zero);

                float angle = i * 45f + Random.Range(-12f, 12f);
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad),
                                      Mathf.Sin(angle * Mathf.Deg2Rad));
                rect.DOAnchorPos(dir * Random.Range(130f, 200f), 0.6f)
                    .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
                rect.DOScale(0.2f, 0.6f).SetUpdate(true).SetLink(gameObject);
                star.DOFade(0f, 0.6f).SetUpdate(true).SetLink(gameObject)
                    .OnComplete(() => { if (star != null) Destroy(star.gameObject); });
            }
        }

        void BuildResultLayer(out RectTransform paper, out TextMeshProUGUI scoreText,
            out RectTransform barFill, out RectTransform hitList,
            out TextMeshProUGUI gradeText, out TextMeshProUGUI commentText)
        {
            var root = (RectTransform)transform;
            // 背板要压得够黑：相纸放大后与分数栏几乎铺满整屏，底下的机身、
            // 右栏表情格再透出来就会跟分数、评语搅在一起看不清
            var layer = VNPhotoBoothUi.CreateImage("ResultLayer", root, null,
                new Color(0.03f, 0.03f, 0.06f, 0.9f), true);
            VNPhotoBoothUi.Stretch((RectTransform)layer.transform);
            _resultLayer = layer.gameObject;
            var layerRect = (RectTransform)layer.transform;

            // ---- 相纸 ----
            var paperImage = VNPhotoBoothUi.CreateImage("Paper", layerRect,
                VNPhotoTextures.PaperSprite(), Color.white);
            // 相纸左移到与分数栏对半分屏；自由拍照没有分数栏，居中放
            paper = VNPhotoBoothUi.Center((RectTransform)paperImage.transform,
                new Vector2(PaperW, PaperH), new Vector2(-430f, 20f));

            var photo = VNPhotoBoothUi.CreateImage("Photo", paper, _shotSprite, Color.white);
            photo.preserveAspect = true;
            VNPhotoBoothUi.Center((RectTransform)photo.transform,
                new Vector2(780f, 585f), new Vector2(0f, 65f));
            if (_shotSprite == null) photo.color = new Color(0.85f, 0.85f, 0.88f, 1f);

            string caption = _freeMode
                ? VNLocale.T("photo.free")
                : VNLocale.T("photo.theme", _theme.DisplayName);
            var captionText = VNPhotoBoothUi.CreateText("Caption", paper, 30,
                new Color(0.35f, 0.3f, 0.35f), caption);
            VNPhotoBoothUi.Center((RectTransform)captionText.transform,
                new Vector2(780f, 56f), new Vector2(0f, -302f));

            // ---- 分数区（自由拍照没有）----
            scoreText = null; barFill = null; hitList = null;
            gradeText = null; commentText = null;

            if (!_freeMode)
            {
                var panel = VNPhotoBoothUi.CreateNode("ScorePanel", layerRect);
                VNPhotoBoothUi.Center(panel, new Vector2(640f, 620f), new Vector2(380f, 20f));

                scoreText = VNPhotoBoothUi.CreateText("Score", panel, 92,
                    Color.white, "0");
                VNPhotoBoothUi.Center((RectTransform)scoreText.transform,
                    new Vector2(BarWidth, 110f), new Vector2(0f, 250f));

                var barBg = VNPhotoBoothUi.CreateImage("BarBg", panel,
                    VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.16f));
                var barBgRect = VNPhotoBoothUi.Center((RectTransform)barBg.transform,
                    new Vector2(BarWidth, 20f), new Vector2(0f, 186f));

                var fill = VNPhotoBoothUi.CreateImage("BarFill", barBgRect,
                    VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.AccentSoft);
                barFill = (RectTransform)fill.transform;
                barFill.anchorMin = new Vector2(0f, 0f);
                barFill.anchorMax = new Vector2(0f, 1f);
                barFill.pivot = new Vector2(0f, 0.5f);
                barFill.offsetMin = Vector2.zero;
                barFill.offsetMax = Vector2.zero;
                barFill.sizeDelta = new Vector2(0f, 0f);

                hitList = VNPhotoBoothUi.CreateNode("HitList", panel);
                VNPhotoBoothUi.Center(hitList, new Vector2(BarWidth, 300f),
                    new Vector2(0f, 20f));

                gradeText = VNPhotoBoothUi.CreateText("Grade", panel, 80, Color.white, "");
                VNPhotoBoothUi.Center((RectTransform)gradeText.transform,
                    new Vector2(BarWidth, 110f), new Vector2(0f, -190f));
                gradeText.gameObject.SetActive(false);

                commentText = VNPhotoBoothUi.CreateText("Comment", panel, 28,
                    new Color(1f, 1f, 1f, 0.9f), "");
                VNPhotoBoothUi.Center((RectTransform)commentText.transform,
                    new Vector2(620f, 90f), new Vector2(0f, -272f));
            }

            // ---- 重拍 / 完成（相纸变高了，按钮跟着往下让）----
            VNPhotoBoothUi.CreateButton("Retake", layerRect, new Vector2(240f, 72f),
                new Vector2(-160f, -450f), VNLocale.T("photo.retake"),
                new Color(0.28f, 0.3f, 0.4f, 1f), Color.white, 30, out _)
                .onClick.AddListener(Retake);

            VNPhotoBoothUi.CreateButton("Finish", layerRect, new Vector2(240f, 72f),
                new Vector2(160f, -450f), VNLocale.T("photo.finish"),
                VNPhotoBoothUi.Accent, Color.white, 30, out _)
                .onClick.AddListener(Finish);
        }

        /// <summary>重拍：把刚存的那张从相册删掉，回到装扮阶段（装扮内容全保留）</summary>
        void Retake()
        {
            if (_phase != Phase.Result) return;

            if (_shotEntry != null) VNPhotoAlbum.Delete(_shotEntry.file);
            _shotEntry = null;
            ReleaseShot();

            if (_resultLayer != null) { Destroy(_resultLayer); _resultLayer = null; }
            if (_bottomBar != null) _bottomBar.SetActive(true);
            if (_leftPanel != null) _leftPanel.SetActive(true);
            if (_rightPanel != null) _rightPanel.SetActive(true);
            if (_viewFinder != null) _viewFinder.gameObject.SetActive(true);

            _timeLeft = _timeLimit;
            UpdateTimerUi();
            if (_shutterButton != null) _shutterButton.interactable = true;
            _phase = Phase.Dressing;
        }

        /// <summary>完成：留下照片，写成绩，按结果分支离开</summary>
        void Finish()
        {
            if (_phase != Phase.Result) return;
            _phase = Phase.Ending;

            if (_freeMode)
            {
                VNFlags.Add(_flagPrefix + FlagCountSuffix, 1);
                Done(VNPhotoScore.OutcomeFree);
                return;
            }

            WriteFlags(_lastResult.total, _lastResult.grade);
            ApplyStatReward(_lastResult.total);
            Done(_lastResult.Outcome);
        }

        void ReleaseShot()
        {
            if (_shotSprite != null) { Destroy(_shotSprite); _shotSprite = null; }
            if (_shotTexture != null) { Destroy(_shotTexture); _shotTexture = null; }
        }

        void OnDestroy()
        {
            ReleaseShot();
            _doodle?.Destroy();   // 两张画布纹理 + 加法材质
        }

        VNPhotoDressing BuildDressing()
        {
            var dressing = new VNPhotoDressing
            {
                meExpression = _meExpr,
                herExpression = _herExpr,
                frameId = _frame != null ? _frame.frameId : null,
                backdropId = _backdrop != null ? _backdrop.backdropId : null,
            };
            foreach (var item in _playerStickers)
                if (item != null) dressing.stickerIds.Add(item.stickerId);
            return dressing;
        }

        void WriteFlags(int score, int grade)
        {
            VNFlags.Set(_flagPrefix + FlagScoreSuffix, score);
            VNFlags.Set(_flagPrefix + FlagGradeSuffix, grade);
            VNFlags.Add(_flagPrefix + FlagCountSuffix, 1);
        }

        void ApplyStatReward(int score)
        {
            if (string.IsNullOrEmpty(_statId) || score <= 0) return;

            int amount = Mathf.RoundToInt(score * _statRate);
            if (amount == 0) return;

            if (_statsHud != null)
                _statsHud.Apply(_statId, (amount >= 0 ? "+" : "") + amount, false, 0);
            else
                VNFlags.Add(_statId, amount);
        }
    }
}
