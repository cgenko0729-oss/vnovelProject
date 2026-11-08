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
    public partial class VNPhotoBoothModule : VNEventModule
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

    }
}
