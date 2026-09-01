using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：羽毛球对战小游戏。
    ///
    /// 玩法逻辑 1:1 复刻参考实现（Student Age 的 BadmintonMiniGameView），但：
    ///   · 弹道数学抽到 VNBadmintonBallistics（纯静态、可单测）
    ///   · 球场与 HUD 抽到 VNBadmintonCourt，角色表现抽到 VNBadmintonActor
    ///   · **不使用 Physics2D**——参考实现靠 Rigidbody2D/Collider2D 做击球判定，
    ///     我们改成纯数学距离判定，代价是必须自己做子步进（见 StepBall 注释）
    ///
    /// 剧本用法（P1 已支持的部分）：
    ///   event badminton target:5 first:me
    ///   * 胜利 -> 赢了
    ///   * 失败 -> 输了
    ///
    /// 操作：A/D（或 ←/→）移动，J / 鼠标左键 发球兼击球，K / 鼠标右键 起跳扣杀，ESC 认输。
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) / 全部 Tween SetLink。
    /// </summary>
    public class VNBadmintonModule : VNEventModule
    {
        [Header("对手 / 难度库（剧本 id: 引用；VNGameConfig 登记的会覆盖这里）")]
        public List<VNBadmintonDef> defs = new List<VNBadmintonDef>();

        [Header("兜底手感参数（没匹配到 Def 时用；换算表见《羽毛球小游戏实施计划.md》第四节）")]
        public VNBadmintonTuning tuning = new VNBadmintonTuning();

        [Header("默认目标分数（Def / 剧本 target: 逐级覆盖；净胜 2 分制）")]
        public int targetScore = 5;

        [Header("兜底角色底图（留空 = 剪影占位；规格见计划书第八节）")]
        public Sprite playerBody;
        public Sprite opponentBody;
        public Sprite racketSprite;
        public Sprite armSprite;

        [Header("兜底羽毛球本体图（留空 = 程序化光球+羽裙；有图时按图片比例显示，图片约定羽裙朝上/球头朝下）")]
        public Sprite ballSprite;

        [Header("兜底远景底图（留空 = 程序化渐变天空）")]
        public Sprite backdrop;

        [Header("调试：玩家也交给 AI（自动对拉，用来验证回合逻辑，正式使用请关掉）")]
        public bool debugAutoPlayer;

        // ── 判定常量 ──
        /// <summary>拍面命中半径</summary>
        const float HitRadius = 105f;
        /// <summary>子步进的最大单步位移（px）——低帧率下防止球穿过拍面</summary>
        const float MaxSubStep = 12f;
        const int MaxSubSteps = 16;
        /// <summary>AI 起拍提前量 = 拍面有效窗起点 + 这个余量（确保接触时稳稳落在窗内）</summary>
        const float AiSwingMargin = 0.03f;

        enum Phase { Intro, Serve, Rally, Point, Over }

        Phase _phase = Phase.Intro;
        float _phaseTimer;

        readonly System.Random _rng = new System.Random();
        VNBadmintonCourt _court;
        VNBadmintonActor _me, _op;
        VNBadmintonDef _def;
        VNBadmintonSfx _sfx;
        Sprite _opponentBody;   // 三级回退的结果，OnLaunch 里定下来

        /// <summary>自由练习模式：无胜负、无上限、随时可退（结果名「结束」）</summary>
        bool _freeMode;
        /// <summary>养成属性换算出的能力加成，每次重刷 tuning 后要重新叠上去</summary>
        float _bonusPower, _bonusSpeed, _bonusJump;
        bool _confirmOpen;
        RectTransform _confirmPanel;

        // 比分与赛制
        int _scoreMe, _scoreOp, _target;
        /// <summary>发球方：-1 = 玩家（左）/ +1 = 对手（右）</summary>
        int _serverSide = -1;
        bool _playerWon;

        // 球
        Vector2 _ball;
        VNBadmintonArc _arc;
        /// <summary>球当前飞行方向：+1 向右（飞向对手）/ -1 向左（飞向玩家）</summary>
        int _ballDir;
        RectTransform _ballRect, _ballShadow, _hitMarker;

        // 角色运动状态
        float _meX, _meY, _opX, _opY;
        bool _meAir, _opAir;
        float _meJumpT, _opJumpT, _meJumpV0, _opJumpV0;

        // AI 本回合的计划
        Vector2 _receivePoint;
        bool _opWillReturn, _opSwung, _opJumped, _opWantsSmash;
        bool _meSwung;   // 仅 debugAutoPlayer 用

        // 轨迹虚点
        readonly List<RectTransform> _trackPool = new List<RectTransform>();
        readonly Queue<KeyValuePair<float, RectTransform>> _tracks =
            new Queue<KeyValuePair<float, RectTransform>>();
        RectTransform _trackRoot;

        // 战绩（回写 flag 用）
        int _perfectCount, _rallyCount, _bestRally;
        string _flagPrefix = "羽球";

        // 输入
        float _moveInput;
        bool _pressHit, _pressJump, _pressQuit;

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        protected override void OnLaunch(VNEventContext ctx)
        {
            ResolveDef(ctx);
            ComputeStatBonus(ctx);
            ApplyTuning();

            _freeMode = ctx.Kw("mode", "match") == "free";
            _target = Mathf.Max(1, ctx.KwI("target",
                _def != null ? _def.targetScore : targetScore));
            _flagPrefix = ctx.Kw("flag", _flagPrefix);

            string first = ctx.Kw("first", "random");
            _serverSide = first == "me" ? -1
                        : first == "opponent" ? 1
                        : (_rng.NextDouble() < 0.5 ? -1 : 1);

            _court = new VNBadmintonCourt();
            _court.Build((RectTransform)transform, tuning,
                _def != null && _def.backdrop != null ? _def.backdrop : backdrop, gameObject);
            _court.SetNames(ctx.Kw("pname", VNLocale.T("badminton.player")), OpponentName(ctx));

            _opponentBody = ResolveOpponentBody(ctx);

            // 顺序有依赖：ResetPositions 会碰 _hitMarker，所以必须等 BuildBall 之后才调
            BuildActors();
            BuildBall();
            ResetPositions();

            _sfx = new VNBadmintonSfx();
            _sfx.Build(gameObject, _def, ctx.stage != null ? ctx.stage.vnAudio : null);

            _court.SetHint(VNLocale.T(_freeMode ? "badminton.hintFree" : "badminton.hint"));
            _court.SetGoal(_freeMode, _target);

            _scoreMe = _scoreOp = 0;
            _court.SetScore(0, 0, false);
            _phase = Phase.Intro;
            _phaseTimer = 1.2f;
            _court.ShowTips(VNLocale.T(_freeMode ? "badminton.startFree"
                                                 : "badminton.start", _target));

            // 先同步一次，否则第一帧渲染时球还在原点（低帧率下会看到它在左下角闪一下）
            SyncVisuals();

            RegisterTutorialAnchors();
        }

        /// <summary>
        /// 养成联动：能力 = Def 基础值 + 属性点数 × 每点增量（照 VNBattleModule 的 patkstat 范式）。
        /// 属性名由剧本给，模块不认识任何具体属性——桥全在 flags 上。
        /// </summary>
        void ComputeStatBonus(VNEventContext ctx)
        {
            int cap = _def != null ? _def.statCap : 20;
            float perPower = _def != null ? _def.powerPerStat : 0.04f;
            float perSpeed = _def != null ? _def.speedPerStat : 0.12f;
            float perJump = _def != null ? _def.jumpPerStat : 0.05f;

            _bonusPower = StatOf(ctx, "powerstat", cap) * perPower;
            _bonusSpeed = StatOf(ctx, "speedstat", cap) * perSpeed;
            _bonusJump = StatOf(ctx, "jumpstat", cap) * perJump;
        }

        static int StatOf(VNEventContext ctx, string key, int cap)
        {
            string statName = ctx.Kw(key);
            if (string.IsNullOrEmpty(statName)) return 0;
            return Mathf.Clamp(VNFlags.Get(statName), 0, Mathf.Max(0, cap));
        }

        /// <summary>Def 参数 → tuning，再叠上养成加成。Editor 实时调参每帧都会重走一遍。</summary>
        void ApplyTuning()
        {
            if (_def != null) tuning.CopyFrom(_def.tuning);
            tuning.playerPower += _bonusPower;
            tuning.playerMoveSpeed += _bonusSpeed;
            tuning.playerJumpHeight += _bonusJump;
        }

        /// <summary>
        /// 解析对手 / 难度资产：剧本 id: > 剧本 vs:（角色 id 同名资产）> 库里只有一条时直接用。
        /// 匹配到就把它的手感参数整块吃进来（后续 Editor 下每帧重读同一份）。
        /// </summary>
        void ResolveDef(VNEventContext ctx)
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.badmintons, ref defs);

            string id = ctx.Kw("id");
            string vs = ctx.Kw("vs");
            _def = FindDef(id) ?? FindDef(vs);
            if (_def == null && defs.Count == 1 && string.IsNullOrEmpty(id)) _def = defs[0];

            if (_def == null && !string.IsNullOrEmpty(id))
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：没有 id 为「{id}」的 " +
                                 "VNBadmintonDef，本局用模板上的兜底参数");
        }

        VNBadmintonDef FindDef(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in defs)
                if (d != null && d.badmintonId == id) return d;
            return null;
        }

        string OpponentName(VNEventContext ctx)
        {
            if (_def != null && !string.IsNullOrEmpty(_def.DisplayOpponentName))
                return _def.DisplayOpponentName;

            // 退回 vs: 指定角色的显示名
            string vs = ctx.Kw("vs");
            var character = FindCharacter(vs);
            if (character != null) return character.LocalizedDisplayName;
            return string.IsNullOrEmpty(vs) ? VNLocale.T("badminton.opponent") : vs;
        }

        static VNCharacterDef FindCharacter(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var cfg = VNGameConfig.Active;
            if (cfg == null || cfg.characters == null) return null;
            foreach (var c in cfg.characters)
                if (c != null && c.id == id) return c;
            return null;
        }

        /// <summary>立绘三级回退：Def 的羽球专用图 → Def 指定角色的默认立绘 → 模板兜底图</summary>
        Sprite ResolveOpponentBody(VNEventContext ctx)
        {
            if (_def != null && _def.opponentBody != null) return _def.opponentBody;

            string charId = _def != null && !string.IsNullOrEmpty(_def.opponentCharacterId)
                ? _def.opponentCharacterId : ctx?.Kw("vs");
            var character = FindCharacter(charId);
            if (character != null && character.DefaultSprite != null) return character.DefaultSprite;

            return opponentBody;
        }

        void BuildActors()
        {
            Sprite racket = _def != null && _def.racket != null ? _def.racket : racketSprite;
            Sprite arm = _def != null && _def.arm != null ? _def.arm : armSprite;
            Sprite mine = _def != null && _def.playerBody != null ? _def.playerBody : playerBody;

            _me = new VNBadmintonActor();
            _me.Build(_court.ActorLayer, mine, racket, arm,
                new Color(0.35f, 0.52f, 0.85f, 1f), true, gameObject);

            _op = new VNBadmintonActor();
            _op.Build(_court.ActorLayer, _opponentBody, racket, arm,
                new Color(0.85f, 0.40f, 0.46f, 1f), false, gameObject);
        }

        void BuildBall()
        {
            _trackRoot = VNBadmintonUi.CreateNode("Track", _court.ActorLayer);
            VNBadmintonUi.AnchorBottomCenter(_trackRoot);
            _trackRoot.anchoredPosition = Vector2.zero;
            _trackRoot.sizeDelta = Vector2.zero;

            // 扣杀机会指示圈（虚线圈用发光贴图近似）
            _hitMarker = VNBadmintonUi.CreateImage("HitMarker", _court.ActorLayer,
                VNProceduralTextures.RadialGlowSprite, new Color(1f, 1f, 1f, 0.35f));
            VNBadmintonUi.AnchorBottomCenter(_hitMarker);
            _hitMarker.sizeDelta = new Vector2(150f, 150f);
            _hitMarker.gameObject.SetActive(false);

            _ballShadow = VNBadmintonUi.CreateImage("BallShadow", _court.ActorLayer,
                VNProceduralTextures.RadialGlowSprite, new Color(0f, 0f, 0f, 0.28f));
            VNBadmintonUi.AnchorBottomCenter(_ballShadow);
            _ballShadow.sizeDelta = new Vector2(56f, 20f);

            // 球体：有图用图（羽裙朝上/球头朝下的约定见 ballSprite 注释），
            // 没图时软光晕打底 + 实心球头兜底——纯 Sparkle 贴图在亮底上几乎看不见，
            // 而「看得见球」是这个玩法的生命线。
            Sprite ball = _def != null && _def.ballSprite != null ? _def.ballSprite : ballSprite;
            _ballRect = VNBadmintonUi.CreateImage("Ball", _court.ActorLayer,
                ball != null ? ball : VNProceduralTextures.RadialGlowSprite,
                ball != null ? Color.white : new Color(1f, 1f, 1f, 0.55f));
            VNBadmintonUi.AnchorBottomCenter(_ballRect);

            if (ball != null)
            {
                _ballRect.GetComponent<Image>().preserveAspect = true;
                const float h = 56f;
                float aspect = ball.rect.width / Mathf.Max(1f, ball.rect.height);
                _ballRect.sizeDelta = new Vector2(h * aspect, h);
                return;
            }

            _ballRect.sizeDelta = new Vector2(52f, 52f);

            // 羽毛裙（拉长的半透明尾）
            var skirt = VNBadmintonUi.CreateImage("Skirt", _ballRect,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.8f));
            skirt.GetComponent<Image>().type = Image.Type.Sliced;
            skirt.anchorMin = skirt.anchorMax = new Vector2(0.5f, 0.5f);
            skirt.anchoredPosition = new Vector2(0f, 11f);
            skirt.sizeDelta = new Vector2(17f, 26f);

            // 球头（实心，最显眼的那一点）
            var head = VNBadmintonUi.CreateImage("Head", _ballRect,
                VNProceduralTextures.RoundedRectSprite, Color.white);
            head.GetComponent<Image>().type = Image.Type.Sliced;
            head.anchorMin = head.anchorMax = new Vector2(0.5f, 0.5f);
            head.anchoredPosition = new Vector2(0f, -6f);
            head.sizeDelta = new Vector2(19f, 19f);
        }

        void ResetPositions()
        {
            _meX = -tuning.startStandX;
            _opX = tuning.startStandX;
            _meY = _opY = tuning.groundY;
            _meAir = _opAir = false;
            _me.SetPosition(_meX, _meY);
            _op.SetPosition(_opX, _opY);
            ParkBallForServe();
        }

        void ParkBallForServe()
        {
            _ball = new Vector2(tuning.serveTargetX * _serverSide, tuning.ballStartY);
            _ballDir = 0;
            ClearTrack();
            _hitMarker.gameObject.SetActive(false);
        }

        public override void CancelForDebug()
        {
            _court?.Dispose();
            _me?.Dispose();
            _op?.Dispose();
        }

        void OnDestroy()
        {
            UnregisterTutorialAnchors();
            _court?.Dispose();
        }

        // ------------------------------------------------------------------
        // 教程锚点
        // ------------------------------------------------------------------

        /// <summary>
        /// 把几块 HUD 登记成教程可高亮的目标。**不能靠物体名查找**——
        /// 球场 UI 全是程序化生成的，改一次布局教程就静默挖到空气上（见
        /// VNTutorialAnchors 的注释）。登记是一行的事，改布局也不影响。
        /// </summary>
        void RegisterTutorialAnchors()
        {
            if (_court != null)
            {
                VNTutorialAnchors.Register(AnchorScore, _court.ScoreBoard);
                VNTutorialAnchors.Register(AnchorHint, _court.HintBox);
                VNTutorialAnchors.Register(AnchorNet, _court.NetRoot);
            }
            if (_me != null) VNTutorialAnchors.Register(AnchorMe, _me.Root);
            if (_op != null) VNTutorialAnchors.Register(AnchorOpponent, _op.Root);
            VNTutorialAnchors.Register(AnchorBall, _ballRect);
        }

        void UnregisterTutorialAnchors()
        {
            VNTutorialAnchors.Unregister(AnchorScore);
            VNTutorialAnchors.Unregister(AnchorHint);
            VNTutorialAnchors.Unregister(AnchorNet);
            VNTutorialAnchors.Unregister(AnchorMe);
            VNTutorialAnchors.Unregister(AnchorOpponent);
            VNTutorialAnchors.Unregister(AnchorBall);
        }

        public const string AnchorScore = "badminton.scoreboard";
        public const string AnchorHint = "badminton.hint";
        public const string AnchorNet = "badminton.net";
        public const string AnchorMe = "badminton.me";
        public const string AnchorOpponent = "badminton.opponent";
        public const string AnchorBall = "badminton.ball";

        // ------------------------------------------------------------------
        // 主循环
        // ------------------------------------------------------------------

        void Update()
        {
            // 教程讲解中：整局冻结。**必须在 ReadInput 之前拦**——
            // 同下面确认框那条的教训，拦晚了照样能挥拍，「冻结」名不副实。
            if (VNPause.IsPaused) return;

            // 事件模块三铁律②：unscaled 计时，不受 Skip 快进影响。
            // 上限 0.05s（切窗口回来的巨大 dt 会让球瞬移过整个球场）已收进 VNTime.Delta。
            float dt = VNTime.Delta;

#if UNITY_EDITOR
            // 决策 10 没做 Editor 调参窗口的补偿：Play 着直接拖 Def 资产的 Inspector
            // 就能实时看到手感变化，不用反复进出 Play Mode。运行时构建整段编译掉。
            if (_def != null) ApplyTuning();
#endif

            // 确认框开着时冻结整局：**必须在 ReadInput 之前拦**，
            // 否则 ReadInput 尾部还会照常触发挥拍/起跳，「冻结」名不副实
            if (_confirmOpen) { TickConfirm(); return; }

            ReadInput();

            if (_pressQuit && _phase != Phase.Over)
            {
                // 自由练习没有胜负，直接收工；正式赛要弹确认——退出即判负
                if (_freeMode) FinishFree();
                else OpenConfirm();
                return;
            }

            switch (_phase)
            {
                case Phase.Intro:
                case Phase.Point:
                    _phaseTimer -= dt;
                    if (_phaseTimer <= 0f) EnterServe();
                    break;
                case Phase.Serve:
                    TickServe(dt);
                    break;
                case Phase.Rally:
                    TickAi();
                    StepBall(dt);
                    break;
            }

            TickActors(dt);
            SyncVisuals();
        }

        void ReadInput()
        {
            _moveInput = 0f;
            _pressHit = _pressJump = _pressQuit = false;

            var k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) _moveInput -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) _moveInput += 1f;
                if (k.jKey.wasPressedThisFrame) _pressHit = true;
                if (k.kKey.wasPressedThisFrame) _pressJump = true;
                if (k.escapeKey.wasPressedThisFrame) _pressQuit = true;
            }

            // 决策 8：鼠标只负责击球，移动只能键盘
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) _pressHit = true;
                if (m.rightButton.wasPressedThisFrame) _pressJump = true;
            }

            if (_phase != Phase.Rally && _phase != Phase.Serve) return;

            if (_pressJump && !_meAir && _me.CanSwing) StartJump(true);
            if (_pressHit && _phase == Phase.Rally && !_meAir)
                _me.PlaySwing(_ball.y < tuning.lowSwingY
                    ? VNBadmintonSwing.Low : VNBadmintonSwing.High);
        }

        // ------------------------------------------------------------------
        // 认输确认框（决策 9：退出即判负，结算只有一条路径）
        // ------------------------------------------------------------------

        void OpenConfirm()
        {
            if (_confirmOpen) return;
            _confirmOpen = true;

            var root = (RectTransform)transform;
            _confirmPanel = VNBadmintonUi.CreateImage("QuitConfirm", root, null,
                new Color(0f, 0f, 0f, 0.55f));
            VNBadmintonUi.Stretch(_confirmPanel);
            _confirmPanel.GetComponent<Image>().raycastTarget = true;   // 挡住底下的点击

            var box = VNBadmintonUi.CreateImage("Box", _confirmPanel,
                VNProceduralTextures.RoundedRectSprite,
                new Color(0.08f, 0.09f, 0.14f, 0.96f));
            box.GetComponent<Image>().type = Image.Type.Sliced;
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(620f, 240f);

            var msg = VNBadmintonUi.CreateText("Msg", box, 36, Color.white,
                VNLocale.T("badminton.confirmQuit"));
            var mr2 = VNBadmintonUi.Rect(msg);
            mr2.anchorMin = mr2.anchorMax = new Vector2(0.5f, 0.68f);
            mr2.sizeDelta = new Vector2(580f, 100f);
            msg.textWrappingMode = TMPro.TextWrappingModes.Normal;

            MakeConfirmButton(box, -140f, VNLocale.T("badminton.quitYes"),
                new Color(0.85f, 0.35f, 0.38f, 1f), () => CloseConfirm(true));
            MakeConfirmButton(box, 140f, VNLocale.T("badminton.quitNo"),
                new Color(0.30f, 0.45f, 0.70f, 1f), () => CloseConfirm(false));

            box.localScale = Vector3.one * 0.8f;
            box.DOScale(1f, 0.2f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
        }

        void MakeConfirmButton(RectTransform parent, float x, string label,
            Color color, UnityEngine.Events.UnityAction onClick)
        {
            var rect = VNBadmintonUi.CreateImage("Btn", parent,
                VNProceduralTextures.RoundedRectSprite, color);
            var img = rect.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.26f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(220f, 72f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);

            var text = VNBadmintonUi.CreateText("Label", rect, 32, Color.white, label);
            var tr = VNBadmintonUi.Rect(text);
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
        }

        /// <summary>确认框开着时只读键盘，其余一律冻结</summary>
        void TickConfirm()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (k.yKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame) CloseConfirm(true);
            else if (k.nKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame) CloseConfirm(false);
        }

        void CloseConfirm(bool quit)
        {
            _confirmOpen = false;
            if (_confirmPanel != null)
            {
                Destroy(_confirmPanel.gameObject);
                _confirmPanel = null;
            }
            if (quit) Finish(false);   // 认输 = 失败，与打输走同一条结算路径
        }

        void EnterServe()
        {
            ResetPositions();
            _rallyCount = 0;
            _phase = Phase.Serve;
            _phaseTimer = 0.9f;   // 对手发球的准备时间
            _court.ShowScoreBoard(true);
        }

        void TickServe(float dt)
        {
            if (_serverSide < 0 && !debugAutoPlayer)
            {
                if (_pressHit) DoServe();
                return;
            }
            _phaseTimer -= dt;
            if (_phaseTimer <= 0f) DoServe();
        }

        void DoServe()
        {
            bool byPlayer = _serverSide < 0;
            var server = byPlayer ? _me : _op;
            int outDir = -_serverSide;   // 球飞向对面

            var end = new Vector2(tuning.serveTargetX * outDir, tuning.groundY);
            float power = byPlayer ? tuning.playerPower : tuning.opponentPower;
            if (!VNBadmintonBallistics.BuildArc(_ball, end, tuning, power, false, _rng, out _arc))
                return;

            _ballDir = outDir;
            server.PlaySwing(VNBadmintonSwing.High);
            _sfx.Play(VNBadmintonSfx.Kind.Serve);
            Talk(byPlayer, TalkKind.Serve);
            BuildTrack();
            PlanReceive();

            _phase = Phase.Rally;
            _court.ShowScoreBoard(false);
        }

        // ------------------------------------------------------------------
        // 球的推进
        // ------------------------------------------------------------------

        /// <summary>
        /// 球沿抛物线推进。**必须子步进**：扣杀球速度可达 1350+ px/s，
        /// 30fps 下单帧位移 45px，而拍面命中半径只有 105px——不切小步的话
        /// 低帧率时球会直接穿过球拍（参考实现靠 Physics2D 的连续碰撞规避了这点，
        /// 我们改纯数学判定后必须自己补）。
        /// </summary>
        void StepBall(float dt)
        {
            float total = _arc.speed * dt;
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(total) / MaxSubStep),
                                    1, MaxSubSteps);
            float sub = total / steps;

            for (int i = 0; i < steps; i++)
            {
                float prevX = _ball.x;
                float nx = prevX + sub;
                float ny = _arc.Y(nx);
                _ball = new Vector2(nx, ny);

                // 触网：跨越 x=0 且高度不足
                if (prevX * nx <= 0f && ny < tuning.netTopY) { NetFault(); return; }
                // 击球
                if (TryReceive()) return;
                // 落地
                if (ny <= tuning.groundY + 8f) { LandPoint(); return; }
            }
        }

        /// <summary>接球方的拍面此刻是否碰到球；碰到就当场解算回球</summary>
        bool TryReceive()
        {
            bool playerReceives = _ballDir < 0;
            var actor = playerReceives ? _me : _op;
            bool airborne = playerReceives ? _meAir : _opAir;

            // AI 本回合注定漏球：让它照样挥空，但不结算
            if (!playerReceives && !_opWillReturn) return false;

            var kind = airborne ? VNBadmintonSwing.Smash : actor.CurrentSwing;
            if (!airborne && !actor.RacketActive) return false;

            if (Vector2.Distance(_ball, actor.RacketPointFor(kind)) > HitRadius) return false;

            ResolveHit(playerReceives, airborne);
            return true;
        }

        void ResolveHit(bool byPlayer, bool airborne)
        {
            var actor = byPlayer ? _me : _op;
            float actorX = byPlayer ? _meX : _opX;
            int outDir = byPlayer ? 1 : -1;

            // 「身前为正」的水平距离——精准判定的唯一输入
            float distance = (_ball.x - actorX) * outDir;
            bool perfect = VNBadmintonBallistics.IsPerfect(distance, tuning);
            bool heavy = airborne || perfect;
            float accuracy = VNBadmintonBallistics.Accuracy(distance, tuning, heavy);

            var end = VNBadmintonBallistics.SampleEndPos(outDir, accuracy, tuning, _rng);
            float power = byPlayer ? tuning.playerPower : tuning.opponentPower;

            if (!VNBadmintonBallistics.BuildArc(_ball, end, tuning, power, heavy, _rng,
                    out var next))
                return;   // 极端角度解不出球路：当作没碰到，球继续飞

            _arc = next;
            _ballDir = outDir;
            _rallyCount++;

            actor.PlaySwing(airborne ? VNBadmintonSwing.Smash
                : _ball.y < tuning.lowSwingY ? VNBadmintonSwing.Low : VNBadmintonSwing.High);

            _sfx.Play(airborne ? VNBadmintonSfx.Kind.Smash
                    : perfect ? VNBadmintonSfx.Kind.Perfect
                              : VNBadmintonSfx.Kind.Hit);

            if (perfect)
            {
                if (byPlayer) _perfectCount++;
                ShowFloat(VNLocale.T("badminton.perfect"), _ball,
                    new Color(1f, 0.85f, 0.35f, 1f));
            }

            // 台词：扣杀 > 精准（打得好，对面隔一拍夸一句）> 普通击球
            if (airborne) Talk(byPlayer, TalkKind.Smash);
            else if (perfect)
            {
                Talk(byPlayer, TalkKind.Hit);
                DOVirtual.DelayedCall(0.9f, () => Talk(!byPlayer, TalkKind.Praise), true)
                         .SetLink(gameObject);
            }
            else Talk(byPlayer, TalkKind.Hit, 0.25f);   // 平球少说话，免得一直在刷气泡

            BuildTrack();
            PlanReceive();
        }

        // ------------------------------------------------------------------
        // 台词气泡
        // ------------------------------------------------------------------

        enum TalkKind { Serve, Hit, Smash, Praise, Score, LoseScore }

        /// <summary>说一句。没配 Def / 没配这一类台词 / 没抽中概率 / 同侧气泡还在，都静默跳过。</summary>
        void Talk(bool player, TalkKind kind, float rate = -1f)
        {
            if (_def == null || !_def.TalkEnabled) return;
            var actor = player ? _me : _op;
            if (actor == null) return;

            var set = player ? _def.playerTalk : _def.opponentTalk;
            var lines = kind switch
            {
                TalkKind.Serve => set.serve,
                TalkKind.Hit => set.hit,
                TalkKind.Smash => set.smash,
                TalkKind.Praise => set.praise,
                TalkKind.Score => set.score,
                _ => set.loseScore,
            };

            string line = set.Pick(lines, _rng);
            if (string.IsNullOrEmpty(line)) return;
            actor.ShowTalk(line, rate < 0f ? _def.talkRate : rate, _rng);
        }

        /// <summary>算出接球方该在哪接、要不要扣杀、这一球会不会被接到</summary>
        void PlanReceive()
        {
            int recvDir = _ballDir > 0 ? 1 : -1;
            _opWantsSmash = _rng.NextDouble() < tuning.opponentHeavyRate;

            if (!VNBadmintonBallistics.SolveReceivePoint(_arc, recvDir,
                    recvDir > 0 && _opWantsSmash, tuning, _rng, out _receivePoint))
                _receivePoint = new Vector2(tuning.startStandX * recvDir,
                                            tuning.groundY + 200f);

            if (recvDir > 0)
            {
                // 对手侧：先掷一次「接不接得到」，注定漏球也照样跑位挥空
                _opWillReturn = _rng.NextDouble() <= tuning.opponentHitRate;
                _opSwung = _opJumped = false;
                _opWantsSmash = _opWantsSmash && _receivePoint.y > tuning.smashNeedY;
            }

            if (recvDir < 0) _meSwung = false;

            // 玩家侧：来球够高时亮出扣杀机会指示圈
            bool marker = recvDir < 0 && _receivePoint.y > tuning.smashNeedY;
            _hitMarker.gameObject.SetActive(marker);
            if (marker) _hitMarker.anchoredPosition = _receivePoint;
        }

        // ------------------------------------------------------------------
        // AI
        // ------------------------------------------------------------------

        /// <summary>
        /// 对手不是实时追球——球路一解出来就知道该站哪、什么时候起拍。
        /// 起拍/起跳的时机按「还有多久到位」算，比参考实现的距离阈值稳（帧率无关）。
        /// </summary>
        void TickAi()
        {
            if (_ballDir == 0 || Mathf.Approximately(_arc.speed, 0f)) return;

            float timeToArrive = (_receivePoint.x - _ball.x) / _arc.speed;
            if (timeToArrive < 0f) return;

            var kind = _receivePoint.y < tuning.lowSwingY
                ? VNBadmintonSwing.Low : VNBadmintonSwing.High;
            // 提前量直接问表现层要「拍面几时开始有效」，动画时长改了这里自动跟上
            float lead = VNBadmintonActor.ActiveWindowStart(kind) + AiSwingMargin;

            if (_ballDir > 0)
            {
                if (!_opJumped && _opWantsSmash && !_opAir &&
                    timeToArrive <= JumpRiseTime(tuning.opponentJumpHeight))
                { StartJump(false); _opJumped = true; Talk(false, TalkKind.Smash); }

                if (!_opSwung && !_opAir && timeToArrive <= lead)
                {
                    _op.PlaySwing(kind);
                    _opSwung = true;
                }
            }
            else if (debugAutoPlayer && !_meSwung && !_meAir && timeToArrive <= lead)
            {
                _me.PlaySwing(kind);
                _meSwung = true;
            }
        }

        /// <summary>起跳到最高点要多久（AI 按这个提前量起跳，与帧率无关）</summary>
        float JumpRiseTime(float heightUnits) =>
            tuning.JumpSpeed(heightUnits) / Mathf.Max(0.01f, Mathf.Abs(tuning.JumpGravity));

        // ------------------------------------------------------------------
        // 角色运动
        // ------------------------------------------------------------------

        void StartJump(bool player)
        {
            if (player)
            {
                _meAir = true; _meJumpT = 0f;
                _meJumpV0 = tuning.JumpSpeed(tuning.playerJumpHeight);
            }
            else
            {
                _opAir = true; _opJumpT = 0f;
                _opJumpV0 = tuning.JumpSpeed(tuning.opponentJumpHeight);
            }
        }

        void TickActors(float dt)
        {
            // 玩家
            if (_meAir)
            {
                _meJumpT += dt;
                float h = JumpHeightAt(_meJumpV0, _meJumpT);
                if (h <= 0f) { h = 0f; _meAir = false; }
                _meY = tuning.groundY + h;
            }
            else
            {
                _meY = tuning.groundY;
                // 挥拍中减速移动（参考实现是完全锁死；0.42s 全锁在本作手感偏僵，折中 35%）
                float speed = tuning.playerMoveSpeed * tuning.pixelsPerUnit
                              * (_me.Swinging ? 0.35f : 1f);

                if (debugAutoPlayer)
                {
                    float want = _ballDir < 0 ? _receivePoint.x : -tuning.startStandX;
                    want = Mathf.Clamp(want, -tuning.moveMaxX, -tuning.moveMinX);
                    float d = want - _meX;
                    _meX += Mathf.Clamp(d, -speed * dt, speed * dt);
                    _moveInput = Mathf.Abs(d) < 2f ? 0f : Mathf.Sign(d);
                }
                else
                {
                    _meX = Mathf.Clamp(_meX + _moveInput * dt * speed,
                                       -tuning.moveMaxX, -tuning.moveMinX);
                }
            }
            _me.SetMoveInput(_meAir ? 0f : _moveInput);
            _me.SetPosition(_meX, _meY);
            _me.Tick(dt, _meY - tuning.groundY);

            // 对手
            if (_opAir)
            {
                _opJumpT += dt;
                float h = JumpHeightAt(_opJumpV0, _opJumpT);
                if (h <= 0f) { h = 0f; _opAir = false; }
                _opY = tuning.groundY + h;
            }
            else
            {
                _opY = tuning.groundY;
                float targetX = _ballDir > 0 ? _receivePoint.x : tuning.startStandX;
                targetX = Mathf.Clamp(targetX, tuning.moveMinX, tuning.moveMaxX);
                float step = tuning.opponentMoveSpeed * tuning.pixelsPerUnit * dt;
                float delta = targetX - _opX;
                float move = Mathf.Clamp(delta, -step, step);
                _opX += move;
                _op.SetMoveInput(Mathf.Abs(delta) < 2f ? 0f : Mathf.Sign(delta));
            }
            _op.SetPosition(_opX, _opY);
            _op.Tick(dt, _opY - tuning.groundY);
        }

        /// <summary>抛体：h = (v0·t + ½·g·t²) × pixelsPerUnit</summary>
        float JumpHeightAt(float v0, float t) =>
            (v0 * t + 0.5f * tuning.JumpGravity * t * t) * tuning.pixelsPerUnit;

        // ------------------------------------------------------------------
        // 得分
        // ------------------------------------------------------------------

        void LandPoint()
        {
            bool outOfBounds = !VNBadmintonBallistics.InBounds(_ball.x, tuning);
            bool playerReceives = _ballDir < 0;
            // 出界 = 击球方失分；界内 = 接球方没接住失分
            AwardPoint(playerReceives == outOfBounds,
                outOfBounds ? "badminton.out" : null);
        }

        void NetFault()
        {
            bool hitterIsPlayer = _ballDir > 0;
            AwardPoint(!hitterIsPlayer, "badminton.net");
        }

        void AwardPoint(bool toPlayer, string reasonKey)
        {
            _sfx.Play(VNBadmintonSfx.Kind.Land);
            if (toPlayer) _scoreMe++; else _scoreOp++;
            _serverSide = toPlayer ? -1 : 1;   // 拿分方发球
            _bestRally = Mathf.Max(_bestRally, _rallyCount);

            ClearTrack();
            _hitMarker.gameObject.SetActive(false);
            _ballDir = 0;

            _court.SetScore(_scoreMe, _scoreOp, true);
            _court.ShowScoreBoard(true);

            // 得分/失分的台词是必说的（rate 1），跟参考实现一致
            Talk(toPlayer, TalkKind.Score, 1f);
            DOVirtual.DelayedCall(0.5f, () => Talk(!toPlayer, TalkKind.LoseScore, 1f), true)
                     .SetLink(gameObject);

            string msg = VNLocale.T(toPlayer ? "badminton.pointMe" : "badminton.pointOp");
            if (!string.IsNullOrEmpty(reasonKey)) msg = VNLocale.T(reasonKey) + msg;

            // 自由练习没有终局，只记分不判胜负
            bool meWin = !_freeMode && _scoreMe >= _target && _scoreMe - _scoreOp >= 2;
            bool opWin = !_freeMode && _scoreOp >= _target && _scoreOp - _scoreMe >= 2;

            if (meWin || opWin)
            {
                _phase = Phase.Over;
                _court.ShowTips(msg, () => Finish(meWin));
                return;
            }

            // 赛点提醒并进同一条横幅——ShowTips 是单条通道，连喊两次会把前一条掐掉
            if (!_freeMode && Mathf.Max(_scoreMe, _scoreOp) >= _target - 1 &&
                Mathf.Abs(_scoreMe - _scoreOp) >= 1)
                msg += "  " + VNLocale.T("badminton.matchPoint");

            _phase = Phase.Point;
            _phaseTimer = 1.7f;
            _court.ShowTips(msg);
        }

        void Finish(bool win)
        {
            _phase = Phase.Over;
            _playerWon = win;
            WriteFlags();
            _court.ShowTips(VNLocale.T(win ? "badminton.win" : "badminton.lose"),
                () => Done(win ? "胜利" : "失败"));
        }

        /// <summary>自由练习收工：无胜负，结果名固定「结束」</summary>
        void FinishFree()
        {
            _phase = Phase.Over;
            WriteFlags();
            Done("结束");
        }

        /// <summary>与剧情通信只走 flags（事件模块状态不进存档）</summary>
        void WriteFlags()
        {
            VNFlags.Set($"{_flagPrefix}_我方得分", _scoreMe);
            VNFlags.Set($"{_flagPrefix}_对方得分", _scoreOp);
            VNFlags.Set($"{_flagPrefix}_精准数", _perfectCount);
            VNFlags.Set($"{_flagPrefix}_最长回合", Mathf.Max(_bestRally, _rallyCount));
        }

        // ------------------------------------------------------------------
        // 轨迹虚点与视觉同步
        // ------------------------------------------------------------------

        /// <summary>沿抛物线铺一串虚点；trackDisplayRate 控制预告多长（难度旋钮）</summary>
        void BuildTrack()
        {
            ClearTrack();
            if (!_arc.Valid || tuning.trackDisplayRate <= 0f) return;

            int dir = _ballDir;
            float span = Mathf.Abs(_arc.endX - _arc.startX) * tuning.trackDisplayRate;
            float limit = _arc.startX + span * dir;

            for (float x = _arc.startX + tuning.trackSpacing * dir;
                 (x - limit) * dir < 0f;
                 x += tuning.trackSpacing * dir)
            {
                var dot = RentTrackDot();
                dot.anchoredPosition = new Vector2(x, _arc.Y(x));
                _tracks.Enqueue(new KeyValuePair<float, RectTransform>(x, dot));
                if (_tracks.Count > 200) break;   // 保险丝
            }
        }

        RectTransform RentTrackDot()
        {
            foreach (var d in _trackPool)
                if (!d.gameObject.activeSelf) { d.gameObject.SetActive(true); return d; }

            var dot = VNBadmintonUi.CreateImage("Dot", _trackRoot,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.85f));
            dot.GetComponent<Image>().type = Image.Type.Sliced;
            VNBadmintonUi.AnchorBottomCenter(dot);
            dot.sizeDelta = new Vector2(11f, 11f);
            _trackPool.Add(dot);
            return dot;
        }

        void ClearTrack()
        {
            while (_tracks.Count > 0) _tracks.Dequeue().Value.gameObject.SetActive(false);
        }

        void SyncVisuals()
        {
            _ballRect.anchoredPosition = _ball;
            _ballShadow.anchoredPosition = new Vector2(_ball.x, tuning.groundY - 2f);

            // 球头朝向飞行方向
            if (_phase == Phase.Rally && _arc.Valid)
            {
                float slope = 2f * _arc.a * _ball.x + _arc.b;
                float angle = Mathf.Atan2(slope * Mathf.Sign(_arc.speed),
                                          Mathf.Sign(_arc.speed)) * Mathf.Rad2Deg;
                _ballRect.localRotation = Quaternion.Euler(0f, 0f, angle - 90f);
            }

            // 球飞过的虚点回收
            while (_tracks.Count > 0)
            {
                var head = _tracks.Peek();
                if ((_ball.x - head.Key) * _ballDir < 0f) break;
                head.Value.gameObject.SetActive(false);
                _tracks.Dequeue();
            }
        }

        void ShowFloat(string message, Vector2 at, Color color)
        {
            var text = VNBadmintonUi.CreateText("Float", _court.ActorLayer, 46, color, message);
            var rect = VNBadmintonUi.Rect(text);
            VNBadmintonUi.AnchorBottomCenter(rect);
            rect.anchoredPosition = new Vector2(at.x, Mathf.Max(at.y + 60f, 620f));
            rect.sizeDelta = new Vector2(300f, 60f);

            var seq = DOTween.Sequence();
            seq.Append(rect.DOAnchorPosY(rect.anchoredPosition.y + 70f, 0.8f));
            seq.Join(text.DOFade(0f, 0.8f).SetEase(Ease.InQuad));
            seq.AppendCallback(() => { if (rect != null) Destroy(rect.gameObject); });
            seq.SetUpdate(true).SetLink(gameObject);
        }
    }
}
