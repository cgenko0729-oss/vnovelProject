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
        const float ViewW = 880f;
        const float ViewH = 660f;
        const float MachineW = 1720f;
        const float MachineH = 900f;
        const float UrgentSeconds = 3f;

        /// <summary>表情格里的取景倍率（格子只要脸，比取景框拉得近得多）</summary>
        const float FaceCellFit = 6f;

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
        bool _freeMode;

        float _timeLimit, _timeLeft;
        string _flagPrefix = "大头照";
        string _statId;
        float _statRate = 0.1f;
        string _title;

        readonly List<VNPhotoStickerItem> _playerStickers = new List<VNPhotoStickerItem>();
        readonly List<GameObject> _frameDecorations = new List<GameObject>();

        // UI
        RectTransform _machine, _viewFinder, _window, _stickerLayer, _timerFill;
        Image _frameBack, _windowImage, _windowRing, _frameFront;
        Image _meImage, _herImage;
        TextMeshProUGUI _watermark, _timerText, _hintText;
        GameObject _leftPanel, _rightPanel, _bottomBar, _confirmLayer;
        RectTransform _frameContent, _stickerContent;
        Button _shutterButton;
        readonly List<Image> _frameCells = new List<Image>();
        readonly List<Image> _meCells = new List<Image>();
        readonly List<Image> _herCells = new List<Image>();

        VNPhotoPortraitDragger _dragger;

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
            ApplyFrame(_frame);
            RefreshPortraits();
            RefreshFrameCells();
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

            // 倒数数字：压在取景框中央（拍照那一刻会被藏起来，不会入镜）
            _countdownText = VNPhotoBoothUi.CreateText("Countdown", machineRect, 220,
                VNPhotoBoothUi.CountdownPink, "");
            _countdownText.fontStyle = FontStyles.Bold;
            _countdownText.outlineWidth = 0.22f;      // 白描边，压在任何背景上都看得清
            _countdownText.outlineColor = new Color32(255, 255, 255, 235);
            VNPhotoBoothUi.Center((RectTransform)_countdownText.transform,
                new Vector2(400f, 400f), new Vector2(0f, -20f));
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
                new Vector2(-MachineW * 0.5f + 300f, MachineH * 0.5f - 62f));

            string themeLine = _freeMode
                ? VNLocale.T("photo.free")
                : (_theme.hint != null && !_theme.hint.Empty
                    ? _theme.hint.Display
                    : VNLocale.T("photo.theme", _theme.DisplayName));
            var hint = VNPhotoBoothUi.CreateText("ThemeHint", parent, 28,
                Color.white, themeLine, TextAlignmentOptions.Center);
            VNPhotoBoothUi.Center((RectTransform)hint.transform, new Vector2(700f, 50f),
                new Vector2(0f, MachineH * 0.5f - 62f));

            // 限时条（不限时就整条不建）
            if (_timeLimit <= 0f) return;

            var barBg = VNPhotoBoothUi.CreateImage("TimerBg", parent,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.18f));
            var barRect = VNPhotoBoothUi.Center((RectTransform)barBg.transform,
                new Vector2(360f, 20f), new Vector2(MachineW * 0.5f - 260f, MachineH * 0.5f - 68f));

            var fill = VNPhotoBoothUi.CreateImage("TimerFill", barRect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.AccentSoft);
            _timerFill = (RectTransform)fill.transform;
            _timerFill.anchorMin = new Vector2(0f, 0f);
            _timerFill.anchorMax = new Vector2(0f, 1f);
            _timerFill.pivot = new Vector2(0f, 0.5f);
            _timerFill.offsetMin = Vector2.zero;
            _timerFill.offsetMax = Vector2.zero;
            _timerFill.sizeDelta = new Vector2(360f, 0f);

            _timerText = VNPhotoBoothUi.CreateText("TimerText", parent, 26, Color.white,
                Mathf.CeilToInt(_timeLeft).ToString());
            VNPhotoBoothUi.Center((RectTransform)_timerText.transform, new Vector2(90f, 40f),
                new Vector2(MachineW * 0.5f - 100f, MachineH * 0.5f - 66f));
        }

        void BuildViewFinder(RectTransform parent)
        {
            _viewFinder = VNPhotoBoothUi.CreateNode("ViewFinder", parent);
            VNPhotoBoothUi.Center(_viewFinder, new Vector2(ViewW, ViewH), new Vector2(0f, -20f));

            _frameBack = VNPhotoBoothUi.CreateImage("FrameBack", _viewFinder, null, Color.white);
            VNPhotoBoothUi.Stretch((RectTransform)_frameBack.transform);

            // 人物开窗：Image + Mask，立绘作为它的子节点被裁进形状里
            _windowImage = VNPhotoBoothUi.CreateImage("Window", _viewFinder, null, Color.white);
            _window = (RectTransform)_windowImage.transform;
            var mask = _windowImage.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;   // 遮罩图本身就是开窗底色

            _meImage = VNPhotoBoothUi.CreateImage("MePortrait", _window, null, Color.white);
            _herImage = VNPhotoBoothUi.CreateImage("HerPortrait", _window, null, Color.white);

            // 人物拖动层：铺满开窗，在两个立绘之上、贴纸层之下
            //（贴纸层是 ViewFinder 的后一个子节点，射线自然优先命中贴纸）
            var dragPad = VNPhotoBoothUi.CreateImage("DragPad", _window, null,
                new Color(0f, 0f, 0f, 0f), true);
            VNPhotoBoothUi.Stretch((RectTransform)dragPad.transform);
            _dragger = dragPad.gameObject.AddComponent<VNPhotoPortraitDragger>();
            _dragger.me = (RectTransform)_meImage.transform;
            _dragger.her = (RectTransform)_herImage.transform;
            _dragger.onChanged = RefreshPortraits;

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
        }

        void BuildLeftPanel(RectTransform parent)
        {
            var panel = VNPhotoBoothUi.CreateImage("LeftPanel", parent,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.PanelBg, true);
            var rect = VNPhotoBoothUi.Center((RectTransform)panel.transform,
                new Vector2(300f, 700f), new Vector2(-MachineW * 0.5f + 190f, -20f));
            _leftPanel = panel.gameObject;

            // 顶部两个标签：边框样式 / 贴纸
            Button frameTab = VNPhotoBoothUi.CreateButton("TabFrame", rect,
                new Vector2(138f, 52f), new Vector2(-72f, 700f * 0.5f - 36f),
                VNLocale.T("photo.tab.frame"), VNPhotoBoothUi.Accent, Color.white, 26,
                out var frameTabText);
            Button stickerTab = VNPhotoBoothUi.CreateButton("TabSticker", rect,
                new Vector2(138f, 52f), new Vector2(72f, 700f * 0.5f - 36f),
                VNLocale.T("photo.tab.sticker"), VNPhotoBoothUi.CellBg,
                VNPhotoBoothUi.TextDark, 26, out var stickerTabText);

            var frameScroll = VNPhotoBoothUi.CreateScrollList("FrameList", rect,
                new Vector2(284f, 610f), new Vector2(0f, -28f), 1,
                new Vector2(258f, 150f), 12f, out _frameContent);
            var stickerScroll = VNPhotoBoothUi.CreateScrollList("StickerList", rect,
                new Vector2(284f, 610f), new Vector2(0f, -28f), 2,
                new Vector2(124f, 124f), 12f, out _stickerContent);
            stickerScroll.gameObject.SetActive(false);

            var frameTabImage = frameTab.targetGraphic as Image;
            var stickerTabImage = stickerTab.targetGraphic as Image;
            frameTab.onClick.AddListener(() =>
            {
                frameScroll.gameObject.SetActive(true);
                stickerScroll.gameObject.SetActive(false);
                if (frameTabImage != null) frameTabImage.color = VNPhotoBoothUi.Accent;
                if (stickerTabImage != null) stickerTabImage.color = VNPhotoBoothUi.CellBg;
                frameTabText.color = Color.white;
                stickerTabText.color = VNPhotoBoothUi.TextDark;
            });
            stickerTab.onClick.AddListener(() =>
            {
                frameScroll.gameObject.SetActive(false);
                stickerScroll.gameObject.SetActive(true);
                if (frameTabImage != null) frameTabImage.color = VNPhotoBoothUi.CellBg;
                if (stickerTabImage != null) stickerTabImage.color = VNPhotoBoothUi.Accent;
                frameTabText.color = VNPhotoBoothUi.TextDark;
                stickerTabText.color = Color.white;
            });

            BuildFrameCells();
            BuildStickerCells();
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
                    new Vector2(236f, 96f), new Vector2(0f, 16f));
            }

            var text = VNPhotoBoothUi.CreateText("Label", rect, 24,
                VNPhotoBoothUi.TextDark, label);
            VNPhotoBoothUi.Center((RectTransform)text.transform, new Vector2(236f, 36f),
                new Vector2(0f, def != null ? -52f : 0f));

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
                    new Vector2(80f, 80f), Vector2.zero);

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
                new Vector2(330f, 700f), new Vector2(MachineW * 0.5f - 205f, -20f));
            _rightPanel = panel.gameObject;

            var title = VNPhotoBoothUi.CreateText("RightTitle", rect, 28,
                Color.white, VNLocale.T("photo.expression"));
            var titleBg = VNPhotoBoothUi.CreateImage("RightTitleBg", rect,
                VNProceduralTextures.RoundedRectSprite, VNPhotoBoothUi.Accent);
            VNPhotoBoothUi.Center((RectTransform)titleBg.transform,
                new Vector2(300f, 52f), new Vector2(0f, 700f * 0.5f - 36f));
            ((RectTransform)title.transform).SetAsLastSibling();
            VNPhotoBoothUi.Center((RectTransform)title.transform,
                new Vector2(300f, 52f), new Vector2(0f, 700f * 0.5f - 36f));

            // 列标题：左列是我、右列是她（不然两栏头像分不清谁是谁）
            var meLabel = VNPhotoBoothUi.CreateText("ColMe", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.me"));
            VNPhotoBoothUi.Center((RectTransform)meLabel.transform, new Vector2(140f, 30f),
                new Vector2(-76f, 700f * 0.5f - 76f));
            var herLabel = VNPhotoBoothUi.CreateText("ColHer", rect, 22,
                VNPhotoBoothUi.TextDark, VNLocale.T("photo.her"));
            VNPhotoBoothUi.Center((RectTransform)herLabel.transform, new Vector2(140f, 30f),
                new Vector2(76f, 700f * 0.5f - 76f));

            var scroll = VNPhotoBoothUi.CreateScrollList("FaceList", rect,
                new Vector2(314f, 580f), new Vector2(0f, -44f), 2,
                new Vector2(140f, 140f), 10f, out var content);

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
            scroll.enabled = rows > 4;
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
            VNPhotoBoothUi.Center(clip, new Vector2(124f, 124f), Vector2.zero);
            clip.gameObject.AddComponent<RectMask2D>();

            var face = VNPhotoBoothUi.CreateImage("Face", clip, null, Color.white);
            // 格子要看清表情 → 比取景框拉得更近（脸怼满格子）
            VNPhotoBoothUi.ApplyPortrait(face, def, expression, 124f, FaceCellFit,
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
            var bar = VNPhotoBoothUi.CreateNode("BottomBar", parent);
            VNPhotoBoothUi.Center(bar, new Vector2(MachineW, 120f),
                new Vector2(0f, -MachineH * 0.5f + 70f));
            _bottomBar = bar.gameObject;

            var shutter = VNPhotoBoothUi.CreateImage("Shutter", bar,
                VNPhotoTextures.CircleSprite(), VNPhotoBoothUi.Accent, true);
            var shutterRect = VNPhotoBoothUi.Center((RectTransform)shutter.transform,
                new Vector2(104f, 104f), Vector2.zero);

            var icon = VNPhotoBoothUi.CreateText("ShutterIcon", shutterRect, 44,
                Color.white, "◉");
            VNPhotoBoothUi.Center((RectTransform)icon.transform, new Vector2(104f, 104f),
                Vector2.zero);

            _shutterButton = shutter.gameObject.AddComponent<Button>();
            _shutterButton.targetGraphic = shutter;
            _shutterButton.onClick.AddListener(Shoot);

            _hintText = VNPhotoBoothUi.CreateText("Hint", bar, 22,
                new Color(1f, 1f, 1f, 0.75f), VNLocale.T("photo.hint"));
            VNPhotoBoothUi.Center((RectTransform)_hintText.transform,
                new Vector2(1200f, 40f), new Vector2(0f, -56f));
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

            // 人物能被拖多远，跟着开窗大小走（别拖到框外去）
            if (_dragger != null) _dragger.bounds = windowSize * 0.34f;
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

            if (!fromFrame) _playerStickers.Add(item);
            return item;
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

            VNPhotoBoothUi.ApplyPortrait(_meImage, _meDef, _meExpr, slotWidth,
                FitFor(_meDef), AnchorFor(_meDef), new Vector2(-half, 0f) + meDrag, mirrorMe);
            VNPhotoBoothUi.ApplyPortrait(_herImage, _herDef, _herExpr, slotWidth,
                FitFor(_herDef), AnchorFor(_herDef),
                new Vector2(solo ? 0f : half, 0f) + herDrag, false);
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
            var hide = new List<GameObject>
            {
                _leftPanel, _rightPanel, _bottomBar, _flash.gameObject,
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
            // 快门按钮会从半透明结算层后面透出来，正好卡在两个按钮中间——藏掉
            if (_bottomBar != null) _bottomBar.SetActive(false);

            BuildResultLayer(out var paper, out var scoreText, out var barFill,
                out var hitList, out var gradeText, out var commentText);

            // ---- 相纸从取景框位置飞出来 ----
            paper.anchoredPosition = new Vector2(0f, -20f);
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
            var layer = VNPhotoBoothUi.CreateImage("ResultLayer", root, null,
                new Color(0.03f, 0.03f, 0.06f, 0.72f), true);
            VNPhotoBoothUi.Stretch((RectTransform)layer.transform);
            _resultLayer = layer.gameObject;
            var layerRect = (RectTransform)layer.transform;

            // ---- 相纸 ----
            var paperImage = VNPhotoBoothUi.CreateImage("Paper", layerRect,
                VNPhotoTextures.PaperSprite(), Color.white);
            paper = VNPhotoBoothUi.Center((RectTransform)paperImage.transform,
                new Vector2(560f, 500f), new Vector2(-430f, 20f));

            var photo = VNPhotoBoothUi.CreateImage("Photo", paper, _shotSprite, Color.white);
            photo.preserveAspect = true;
            VNPhotoBoothUi.Center((RectTransform)photo.transform,
                new Vector2(500f, 375f), new Vector2(0f, 42f));
            if (_shotSprite == null) photo.color = new Color(0.85f, 0.85f, 0.88f, 1f);

            string caption = _freeMode
                ? VNLocale.T("photo.free")
                : VNLocale.T("photo.theme", _theme.DisplayName);
            var captionText = VNPhotoBoothUi.CreateText("Caption", paper, 26,
                new Color(0.35f, 0.3f, 0.35f), caption);
            VNPhotoBoothUi.Center((RectTransform)captionText.transform,
                new Vector2(500f, 50f), new Vector2(0f, -196f));

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

            // ---- 重拍 / 完成 ----
            VNPhotoBoothUi.CreateButton("Retake", layerRect, new Vector2(240f, 72f),
                new Vector2(-160f, -400f), VNLocale.T("photo.retake"),
                new Color(0.28f, 0.3f, 0.4f, 1f), Color.white, 30, out _)
                .onClick.AddListener(Retake);

            VNPhotoBoothUi.CreateButton("Finish", layerRect, new Vector2(240f, 72f),
                new Vector2(160f, -400f), VNLocale.T("photo.finish"),
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

        void OnDestroy() => ReleaseShot();

        VNPhotoDressing BuildDressing()
        {
            var dressing = new VNPhotoDressing
            {
                meExpression = _meExpr,
                herExpression = _herExpr,
                frameId = _frame != null ? _frame.frameId : null,
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
