using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 亲密互动事件模块：光标变成道具图标，点/摸角色的部位，
    /// 摸到一定程度换表情、说台词、放语音，最后按阶段判结果。
    ///
    /// 剧本用法：
    ///   event interact vs:星野结衣 id:初次抚摸 items:手,羽毛 time:120 flag:抚摸1
    ///   * 满足
    ///   * 普通
    ///   * 拒绝      ← 必须接住，否则玩家惹毛角色会静默跳过
    ///
    /// **本模块刻意破一次「模块不碰舞台」的铁律**（先例：VNAiTalkModule）——
    /// 玩法本身就是对着舞台上的立绘操作，自绘一套立绘等于要把眨眼/口型/
    /// 色调匹配/出场动画全部重接一遍。边界收紧为：只碰**表情与叠加层**，
    /// 且正常结束 / ESC / CancelForDebug 三条路径都还原原表情。
    ///
    /// 射线：EventLayer 排序 60 在对话框 40 之上，所以
    ///   1) 不铺全屏暗幕 —— 会盖住对话框，台词就看不见了；
    ///   2) 自绘的一切 raycastTarget = false，只有道具按钮和结束钮例外。
    /// </summary>
    public class VNInteractionModule : VNEventModule
    {
        [Header("资产库（装机器负责登记；剧本 id: 在这里查）")]
        public List<VNInteractionDef> interactions = new List<VNInteractionDef>();

        [Header("部位区域库（按角色 id 匹配）")]
        public List<VNTouchZoneDef> zoneDefs = new List<VNTouchZoneDef>();

        [Header("默认开启部位框可视化（开发调试用；剧本 zones:on|off 覆盖）")]
        public bool showZones;

        static readonly Color PanelColor = new Color(0.08f, 0.06f, 0.10f, 0.82f);
        static readonly Color AccentColor = new Color(1f, 0.45f, 0.62f, 1f);
        static readonly Color ZoneDebugColor = new Color(0.4f, 0.9f, 1f, 0.25f);
        static readonly Color ZoneLockedColor = new Color(1f, 0.35f, 0.35f, 0.25f);

        enum Phase { Idle, Playing, Ending }
        Phase _phase = Phase.Idle;

        VNStage _stage;
        VNAudio _audio;
        VNStatsHud _statsHud;
        VNScriptRunner _runner;
        /// <summary>正在播阻塞型反馈（等玩家推进），期间不吃抚摸输入</summary>
        bool _blocked;
        VNInteractionDef _def;
        VNTouchZoneDef _zoneDef;
        VNStage.ActiveCharacter _char;
        string _charId;
        string _originalExpression;

        readonly VNTouchScore _score = new VNTouchScore();
        readonly List<VNInteractionItem> _usableItems = new List<VNInteractionItem>();
        VNInteractionItem _item;
        string _flagPrefix;
        float _timeLeft;
        bool _timed;
        bool _endExpressionKept;
        bool _cleanupDone;

        Vector2 _lastMouse;
        bool _hasLastMouse;
        VNTouchZone _hoverZone;
        string _lastVoiceId;
        Camera _uiCamera;
        /// <summary>本次判定是点击触发还是拖动触发（反馈的 trigger 条件用）</summary>
        bool _lastTouchWasClick;
        readonly List<int> _candidateIdx = new List<int>();

        // UI
        VNTouchCursor _cursor;
        readonly List<Image> _itemButtons = new List<Image>();
        RectTransform _hud;
        Image _barFill;
        TextMeshProUGUI _stageText, _timerText, _hintText;
        RectTransform _zoneOverlay;
        readonly List<RectTransform> _zoneMarkers = new List<RectTransform>();

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------

        protected override void OnLaunch(VNEventContext ctx)
        {
            _stage = ctx.stage;
            _audio = _stage != null ? _stage.vnAudio : null;
            _statsHud = FindAnyObjectByType<VNStatsHud>();
            _runner = FindAnyObjectByType<VNScriptRunner>();

            _charId = ctx.Kw("vs");
            _def = FindDef(ctx.Kw("id"));
            if (_def == null)
            {
                Debug.LogWarning($"[VNInteract] 第 {ctx.line} 行：互动定义库里没有" +
                                 $"「{ctx.Kw("id")}」（在 VNInteractionModule.interactions 登记）");
                Done(""); return;
            }

            _char = _stage != null ? _stage.Get(_charId) : null;
            if (_char == null)
            {
                Debug.LogWarning($"[VNInteract] 第 {ctx.line} 行：角色「{_charId}」不在场，" +
                                 "event 之前要先 show 出来");
                Done(""); return;
            }
            _originalExpression = _char.expression;
            _zoneDef = FindZoneDef(_charId);
            if (_zoneDef == null)
                Debug.LogWarning($"[VNInteract] 角色「{_charId}」没有部位区域定义" +
                                 "（VNTouchZoneDef），本场摸哪儿都不会有反应");

            _flagPrefix = ctx.Kw("flag", _def.flagPrefix);
            showZones = ParseOnOff(ctx.Kw("zones"), showZones);

            float limit = ctx.KwF("time", _def.timeLimit);
            _timed = limit > 0.01f;
            _timeLeft = limit;

            BuildItemList(ctx.Kw("items"));
            _score.Init(_def.Thresholds());
            ApplyStageIdleExpression(0);

            BuildUi();
            _phase = Phase.Playing;
        }

        void Update()
        {
            if (VNPause.IsPaused) return;   // 教程讲解中：限时、冷却与抚摸输入一起冻住
            if (_phase != Phase.Playing) return;

            float now = VNTime.Time;
            float dt = VNTime.Delta;

            if (_timed)
            {
                _timeLeft -= dt;
                if (_timeLeft <= 0f) { Finish(ResolveOutcome(false)); return; }
            }

            var mouse = Mouse.current;
            if (mouse == null) return;

            // 阻塞型反馈（角色正在说需要玩家推进的台词）：不吃抚摸输入，
            // 否则玩家点一下既推进了对话又顺手摸了一把
            if (_blocked)
            {
                _hoverZone = null;
                _cursor?.SetState(false, false);
                _hasLastMouse = false;

                // **推进输入必须由这里转发**：Runner.Update 第一行就是
                // `if (_eventActive) return;`，事件期间它完全不收输入，
                // 所以模块内播的阻塞台词会死等 _advance —— 玩家点破屏幕也过不去。
                var kb = Keyboard.current;
                bool advance =
                    (kb != null && (kb.enterKey.wasPressedThisFrame ||
                                    kb.spaceKey.wasPressedThisFrame)) ||
                    (mouse.leftButton.wasPressedThisFrame &&
                     !IsPointerOverModuleUi(mouse.position.ReadValue()));
                if (advance) _runner?.RequestAdvance();

                RefreshHud();
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            bool held = mouse.leftButton.isPressed;
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            bool overUi = IsPointerOverModuleUi(screen);

            _hoverZone = overUi ? null : ZoneAt(screen);

            if (!held && _def.exciteDecayPerSecond > 0f)
                _score.Decay(_def.exciteDecayPerSecond, dt);

            if (!overUi && _hoverZone != null)
            {
                float units = 0f;

                // 单击：固定量
                if (clicked) units += _def.clickUnits;

                // 拖动：把屏幕位移换算成「立绘像素」，再除以 dragPixelsPerUnit
                if (held && _hasLastMouse)
                {
                    float moved = ScreenToStagePixels(screen - _lastMouse);
                    if (moved > 0.01f) units += moved / Mathf.Max(1f, _def.dragPixelsPerUnit);
                }

                if (units > 0f) ApplyTouch(_hoverZone, units, now, clicked);
            }

            _lastMouse = screen;
            _hasLastMouse = true;

            _cursor?.SetState(_hoverZone != null, held);

            RefreshHud();
            RefreshZoneMarkers();
        }

        // ------------------------------------------------------------------
        // 判定
        // ------------------------------------------------------------------

        void ApplyTouch(VNTouchZone zone, float units, float now, bool fromClick = false)
        {
            _lastTouchWasClick = fromClick;
            // 禁忌部位：阶段不够 → 拒绝
            if (_score.Stage < zone.unlockStage)
            {
                var rej = _score.Reject(now, _def.rejectCooldown, _def.rejectLimit);
                if (rej.rejected)
                {
                    PlayFeedback(PickFeedback(_def.rejectFeedbacks, "reject", now), now);
                    if (rej.rejectOverflow) Finish(_def.outcomeRejected);
                }
                return;
            }

            var rule = _def.FindRule(zone.id, _item != null ? _item.id : null);
            float gain = zone.gainScale * (_item != null ? _item.gainScale : 1f) *
                         (rule != null ? rule.gainPerUnit : 1f);
            float every = rule != null ? rule.feedbackEvery : 8f;

            var tick = _score.AddUnits(zone.id, units, gain, every);

            if (tick.feedback && rule != null)
                PlayFeedback(PickFeedback(rule.feedbacks, zone.id, now), now);

            if (tick.stageUp)
            {
                ApplyStageIdleExpression(tick.newStage);
                var st = StageAt(tick.newStage);
                if (st != null) PlayFeedback(st.enterFeedback, now, true);

                if (_def.autoEndOnTarget && tick.newStage >= _def.ResolvedTargetStage)
                    Finish(_def.outcomeSatisfied);
            }
        }

        /// <summary>按权重从池里抽一条（过滤阶段条件与冷却）</summary>
        VNInteractionFeedback PickFeedback(List<VNInteractionFeedback> pool, string key, float now)
        {
            if (pool == null || pool.Count == 0) return null;

            float total = 0f;
            _candidateIdx.Clear();
            for (int i = 0; i < pool.Count; i++)
            {
                var fb = pool[i];
                if (fb == null || fb.IsEmpty || !fb.StageOk(_score.Stage)) continue;
                if (!fb.TriggerOk(_lastTouchWasClick)) continue;
                if (!_score.CoolDownReady(key + "#" + i, now)) continue;
                _candidateIdx.Add(i);
                total += Mathf.Max(0.01f, fb.weight);
            }
            if (_candidateIdx.Count == 0) return null;

            // 按索引抽而不是按引用抽：同一条反馈被复制进池两次时，IndexOf 会把
            // 冷却记到第一条头上，第二条就永远冷却不上
            int chosen = _candidateIdx[_candidateIdx.Count - 1];
            float r = Random.value * total;
            foreach (int i in _candidateIdx)
            {
                r -= Mathf.Max(0.01f, pool[i].weight);
                if (r <= 0f) { chosen = i; break; }
            }
            _score.MarkCoolDown(key + "#" + chosen, now, pool[chosen].cooldown);
            return pool[chosen];
        }

        void PlayFeedback(VNInteractionFeedback fb, float now, bool isStageEnter = false,
            bool allowBlocking = true)
        {
            if (fb == null || fb.IsEmpty) return;

            if (!string.IsNullOrEmpty(fb.expression))
                _stage.SetExpression(_charId, fb.expression);

            if (!string.IsNullOrEmpty(fb.mark) && _char.marks != null &&
                VNCharacterMarks.TryParse(fb.mark, out var kind))
                _char.marks.Show(kind, false, null, 1f, 1.1f);

            if (!string.IsNullOrEmpty(fb.emote)) PlayEmote(fb.emote);

            if (!string.IsNullOrEmpty(fb.overlay) && _char.overlay != null &&
                !_char.overlay.SetStrength(fb.overlay, fb.overlayStrength))
                Debug.LogWarning($"[VNInteract] 角色「{_charId}」没有叠加层「{fb.overlay}」" +
                                 "（在 VNCharacterDef.overlays 登记）");

            if (!string.IsNullOrEmpty(fb.se)) _audio?.PlaySe(fb.se);

            string voice = PickVoice(fb.voicePool);
            if (!string.IsNullOrEmpty(voice)) _audio?.PlayVoice(voice);

            if (!Mathf.Approximately(fb.excite, 0f)) _score.AddExcite(fb.excite);
            if (!string.IsNullOrEmpty(fb.statOp)) ApplyStatOp(fb.statOp);
            if (isStageEnter && _hintText != null) FlashHint();

            string line = fb.LocalizedLine;
            bool hasScript = !string.IsNullOrEmpty(fb.scriptLines);
            // 已经在播阻塞反馈时，新来的一条降级为非阻塞：排队会让台词堆成一串，
            // 玩家得连点好几下才能继续摸
            bool blocking = fb.blocking && !_blocked && allowBlocking;

            if (blocking || hasScript)
            {
                StartCoroutine(FeedbackCo(line, fb.scriptLines, blocking));
                return;
            }

            if (!string.IsNullOrEmpty(line))
                _stage.Say(_charId, _char.expression, line);
        }

        /// <summary>
        /// 阻塞台词 / 内嵌剧本行都走 Runner 的 <see cref="VNScriptRunner.RunInlineCo"/>：
        /// 台词拼成一行丢回去，等打字完与等玩家推进（含 Auto / Skip）全部复用
        /// Runner 现成的 SayCo，不用在模块里重写一套推进逻辑。
        /// </summary>
        System.Collections.IEnumerator FeedbackCo(string line, string scriptLines, bool blocking)
        {
            if (_runner == null)
            {
                // 没有 Runner（比如单独测试模块）时退化为不等待
                if (!string.IsNullOrEmpty(line)) _stage.Say(_charId, _char.expression, line);
                yield break;
            }

            if (blocking) _blocked = true;

            // **演出先于台词**：台词若是阻塞型会一直等玩家点击，把喷水/震屏推迟到
            // 点击之后就完全脱节了 —— 演出是「刚发生的事」，台词是随后的反应。
            // 占位符也在这一刻展开，鼠标还停在玩家刚摸的位置上。
            if (!string.IsNullOrEmpty(scriptLines))
                yield return _runner.RunInlineCo(ExpandPlaceholders(scriptLines));

            if (!string.IsNullOrEmpty(line))
            {
                if (blocking)
                {
                    // 注意：角色 id 里有空格会拼错行；项目约定 id 不含空格
                    string expr = string.IsNullOrEmpty(_char.expression)
                        ? "" : " " + _char.expression;
                    yield return _runner.RunInlineCo($"{_charId}{expr}: {line}");
                }
                else _stage.Say(_charId, _char.expression, line);
            }

            _blocked = false;
        }

        // ------------------------------------------------------------------
        // 内嵌剧本行的占位符
        // ------------------------------------------------------------------

        /// <summary>
        /// 把内嵌剧本行里的占位符换成实时数值。存在的理由：坐标写死之后，
        /// 角色一移位、镜头一推拉、换张构图不同的立绘，特效就喷偏了；
        /// 而这个玩法真正想要的是「摸哪儿喷哪儿」。
        ///
        /// 坐标一律是 **viewport 比例 0~1，左下角为原点** —— 与 liquid 命令的
        /// x:/y: 同一套（它内部走 Camera.ViewportToWorldPoint），所以可以直接写
        /// <c>liquid splash x:{cx} y:{cy}</c>。fx / 粒子之类将来要坐标时也能复用。
        /// </summary>
        string ExpandPlaceholders(string lines)
        {
            if (string.IsNullOrEmpty(lines) || lines.IndexOf('{') < 0) return lines;

            // 直接读实时鼠标位置，**不用 _lastMouse** —— 那个只在 Update 里写，
            // 而阻塞台词期间 Update 提前 return，取到的会是过期值甚至初始的 (0,0)，
            // 水就喷到屏幕左下角去了
            Vector2 cursorScreen = Mouse.current != null
                ? Mouse.current.position.ReadValue() : _lastMouse;
            Vector2 cursor = ViewportOfScreen(cursorScreen);
            Vector2 zone = _hoverZone != null
                ? ViewportOfSpriteNorm(_hoverZone.center) : cursor;
            Vector2 person = ViewportOfSpriteNorm(Vector2.zero);

            var sb = new System.Text.StringBuilder(lines);
            sb.Replace("{cx}", F(cursor.x)).Replace("{cy}", F(cursor.y));
            sb.Replace("{zx}", F(zone.x)).Replace("{zy}", F(zone.y));
            sb.Replace("{px}", F(person.x)).Replace("{py}", F(person.y));
            // imprint 要的是立绘归一化坐标，和 liquid 的 viewport 比例不是一回事
            Vector2 normCursor = SpriteNormOfScreen(cursorScreen);
            Vector2 normZone = _hoverZone != null ? _hoverZone.center : normCursor;
            sb.Replace("{nx}", F(normCursor.x)).Replace("{ny}", F(normCursor.y));
            sb.Replace("{znx}", F(normZone.x)).Replace("{zny}", F(normZone.y));
            sb.Replace("{prog}", F(_score.ProgressTo(_def.ResolvedTargetStage)));
            sb.Replace("{stage}", _score.Stage.ToString());
            sb.Replace("{zone}", _hoverZone != null ? _hoverZone.id : "");
            // {char} = 本场互动的角色 id，这样示例/模板资产不用写死角色名
            sb.Replace("{char}", _charId ?? "");
            return sb.ToString();
        }

        /// <summary>数值一律用不变文化格式化 —— 逗号小数点的地区会把 x:0,5 拆成两个参数</summary>
        static string F(float v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>屏幕点 → viewport 比例</summary>
        static Vector2 ViewportOfScreen(Vector2 screen) =>
            new Vector2(Mathf.Clamp01(screen.x / Mathf.Max(1f, Screen.width)),
                        Mathf.Clamp01(screen.y / Mathf.Max(1f, Screen.height)));

        /// <summary>屏幕点 → 立绘归一化坐标（-0.5~0.5）；换算失败回 (0,0)</summary>
        Vector2 SpriteNormOfScreen(Vector2 screen)
        {
            if (_char == null || _char.rect == null) return Vector2.zero;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _char.rect, screen, UiCamera, out Vector2 local)) return Vector2.zero;
            Vector2 size = _char.rect.rect.size;
            if (size.x <= 0.01f || size.y <= 0.01f) return Vector2.zero;
            return new Vector2(local.x / size.x, local.y / size.y);
        }

        /// <summary>立绘归一化坐标（-0.5~0.5）→ viewport 比例</summary>
        Vector2 ViewportOfSpriteNorm(Vector2 norm)
        {
            if (_char == null || _char.rect == null) return new Vector2(0.5f, 0.5f);
            Vector2 size = _char.rect.rect.size;
            Vector3 world = _char.rect.TransformPoint(
                new Vector3(norm.x * size.x, norm.y * size.y, 0f));
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(UiCamera, world);
            return ViewportOfScreen(screen);
        }

        /// <summary>
        /// 结束收尾。**四条退出路径都要走到**，且协程挂在 Runner 上而不是模块上 ——
        /// 模块马上就要被销毁，挂自己身上的协程会被拦腰打断，
        /// 于是 liquid spray 就一直喷下去了。
        /// </summary>
        void RunCleanup()
        {
            if (_cleanupDone) return;
            _cleanupDone = true;
            if (_def == null || string.IsNullOrEmpty(_def.cleanupLines) || _runner == null)
                return;
            _runner.StartCoroutine(_runner.RunInlineCo(ExpandPlaceholders(_def.cleanupLines)));
        }

        /// <summary>随机抽一条语音，尽量不重复上一条</summary>
        string PickVoice(List<string> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            if (pool.Count == 1) return pool[0];

            for (int attempt = 0; attempt < 4; attempt++)
            {
                string pick = pool[Random.Range(0, pool.Count)];
                if (pick != _lastVoiceId) { _lastVoiceId = pick; return pick; }
            }
            return pool[Random.Range(0, pool.Count)];
        }

        void PlayEmote(string emote)
        {
            var e = _char.emotes;
            if (e == null) return;
            switch (emote)
            {
                case "惊讶": case "surprise": e.Surprise(); break;
                case "生气": case "angry": e.Angry(); break;
                case "害羞": case "shy": e.Shy(); break;
                case "沮丧": case "dejected": e.Dejected(); break;
                case "恢复": case "recover": e.Recover(); break;
                case "点头": case "nod": e.Nod(); break;
                case "摇头": case "shake": e.HeadShake(); break;
                default:
                    Debug.LogWarning($"[VNInteract] 未知情绪动作「{emote}」");
                    break;
            }
        }

        /// <summary>"好感 +2" → VNStatsHud.Apply("好感", "+2")</summary>
        void ApplyStatOp(string op)
        {
            var parts = op.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || _statsHud == null) return;
            _statsHud.Apply(parts[0], parts[1], false, 0);
        }

        void ApplyStageIdleExpression(int stage)
        {
            var st = StageAt(stage);
            if (st != null && !string.IsNullOrEmpty(st.idleExpression))
                _stage.SetExpression(_charId, st.idleExpression);
        }

        VNInteractionStage StageAt(int i) =>
            _def.stages != null && i >= 0 && i < _def.stages.Count ? _def.stages[i] : null;

        // ------------------------------------------------------------------
        // 命中：屏幕点 → 立绘归一化坐标 → 部位
        // ------------------------------------------------------------------

        VNTouchZone ZoneAt(Vector2 screen)
        {
            if (_zoneDef == null || _char == null || _char.rect == null) return null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _char.rect, screen, UiCamera, out Vector2 local)) return null;

            Vector2 size = _char.rect.rect.size;
            if (size.x <= 0.01f || size.y <= 0.01f) return null;

            // 局部坐标 → 归一化 [-0.5, 0.5]（与 markAnchor 同语义）
            var norm = new Vector2(local.x / size.x, local.y / size.y);
            if (Mathf.Abs(norm.x) > 0.5f || Mathf.Abs(norm.y) > 0.5f) return null;

            return _zoneDef.Pick(norm, _char.image != null ? _char.image.sprite : null,
                                 _char.expression);
        }

        /// <summary>屏幕位移 → 立绘所在画布空间的像素长度（与分辨率无关）</summary>
        float ScreenToStagePixels(Vector2 screenDelta)
        {
            float scale = Canvas != null ? Canvas.scaleFactor : 1f;
            return screenDelta.magnitude / Mathf.Max(0.0001f, scale);
        }

        Canvas _canvas;
        Canvas Canvas
        {
            get
            {
                if (_canvas == null && _char != null && _char.rect != null)
                    _canvas = _char.rect.GetComponentInParent<Canvas>();
                if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
                return _canvas;
            }
        }

        /// <summary>
        /// 屏幕点换算用的相机。本项目 Canvas 是 Screen Space - **Camera**，
        /// 传 null 会让所有 RectTransformUtility 判定整体错位。
        /// </summary>
        Camera UiCamera
        {
            get
            {
                if (_uiCamera != null) return _uiCamera;
                var c = Canvas;
                if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
                    _uiCamera = c.worldCamera;
                return _uiCamera;
            }
        }

        // ------------------------------------------------------------------
        // 结束
        // ------------------------------------------------------------------

        string ResolveOutcome(bool rejected)
        {
            if (rejected) return _def.outcomeRejected;
            return _score.Stage >= _def.ResolvedTargetStage
                ? _def.outcomeSatisfied : _def.outcomeNormal;
        }

        void Finish(string outcome)
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            WriteFlags();
            DestroyZoneOverlay();     // 结算台词已经在说了，调试框不该还挂在脸上
            ClearImprints();
            _cursor?.Dispose();       // 系统光标立刻还回去，别等模块销毁
            RunCleanup();             // 关掉被反馈开起来的持续状态（spray / wet …）

            VNInteractionFeedback endFb =
                outcome == _def.outcomeSatisfied ? _def.endSatisfied :
                outcome == _def.outcomeRejected ? _def.endRejected : _def.endNormal;

            if (endFb != null && !string.IsNullOrEmpty(endFb.expression))
                _endExpressionKept = true;      // 结局指定了表情 → 保留它，不还原

            // 结束反馈强制非阻塞：模块 0.7 秒后就 Done 并被销毁，等玩家推进的台词
            // 会被拦腰打断。结算台词该写在剧本的「* 结果行」下面，这里只放表情/漫符/短演出
            PlayFeedback(endFb, VNTime.Time, false, false);

            RestoreExpression();

            if (_hud != null)
                _hud.DOScale(0.85f, 0.25f).SetUpdate(true).SetLink(gameObject);

            DOVirtual.DelayedCall(0.7f, () => Done(outcome), true).SetLink(gameObject);
        }

        void WriteFlags()
        {
            if (string.IsNullOrEmpty(_flagPrefix)) return;
            VNFlags.Set(_flagPrefix + "_兴奋度", Mathf.RoundToInt(_score.Excite));
            VNFlags.Set(_flagPrefix + "_阶段", _score.Stage);
            VNFlags.Set(_flagPrefix + "_拒绝数", _score.RejectCount);
            foreach (var kv in _score.AllZoneTouches)
                VNFlags.Set(_flagPrefix + "_" + kv.Key + "次数", kv.Value);
        }

        /// <summary>还原原表情。三条退出路径都要走（正常结束 / ESC / 调试中断）</summary>
        void RestoreExpression()
        {
            if (_endExpressionKept) return;
            if (_stage != null && !string.IsNullOrEmpty(_charId) &&
                !string.IsNullOrEmpty(_originalExpression))
                _stage.SetExpression(_charId, _originalExpression);
        }

        public override void CancelForDebug()
        {
            _phase = Phase.Ending;
            _endExpressionKept = false;
            RestoreExpression();
            DestroyZoneOverlay();
            ClearImprints();
            _cursor?.Dispose();
            RunCleanup();
        }

        void OnDestroy()
        {
            // 保底：任何路径销毁都不留下改过的表情，也绝不留下消失的鼠标指针
            if (_phase != Phase.Ending) RestoreExpression();
            DestroyZoneOverlay();
            ClearImprints();
            _cursor?.Dispose();
            RunCleanup();
        }

        /// <summary>
        /// 部位框可视化是**挂在立绘底下**的（要跟着立绘一起缩放位移），
        /// 不在模块自己的子树里 —— 所以模块被销毁时它不会自动回收，必须显式删。
        /// 破「模块不碰舞台」的铁律要自己付的账：凡是挂到舞台上的东西，
        /// 三条退出路径都得亲手清干净。
        /// </summary>
        /// <summary>
        /// 清掉本场留下的立绘痕迹。痕迹是**互动内的临时演出**（不进存档），
        /// 所以四条退出路径都要清 —— 否则互动结束了掌印还留在脸上，
        /// 而它又不在存档里，读档后会莫名其妙地消失。
        /// </summary>
        void ClearImprints()
        {
            if (_char != null && _char.imprints != null) _char.imprints.ClearAll(0.35f);
        }

        void DestroyZoneOverlay()
        {
            if (_zoneOverlay == null) return;
            Destroy(_zoneOverlay.gameObject);
            _zoneOverlay = null;
            _zoneMarkers.Clear();
        }

        // ------------------------------------------------------------------
        // 参数解析
        // ------------------------------------------------------------------

        VNInteractionDef FindDef(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in interactions)
                if (d != null && d.id == id) return d;
            return null;
        }

        VNTouchZoneDef FindZoneDef(string characterId)
        {
            foreach (var z in zoneDefs)
                if (z != null && z.characterId == characterId) return z;
            return null;
        }

        void BuildItemList(string csv)
        {
            _usableItems.Clear();
            var wanted = new List<string>();
            if (!string.IsNullOrEmpty(csv))
                foreach (var s in csv.Split(','))
                    if (!string.IsNullOrEmpty(s.Trim())) wanted.Add(s.Trim());

            foreach (var it in _def.items)
            {
                if (it == null || string.IsNullOrEmpty(it.id)) continue;
                if (wanted.Count > 0 && !wanted.Contains(it.id)) continue;
                if (!string.IsNullOrEmpty(it.unlockCondition) &&
                    !VNFlags.Evaluate(it.unlockCondition)) continue;
                _usableItems.Add(it);
            }

            if (_usableItems.Count == 0 && _def.items.Count > 0)
            {
                Debug.LogWarning("[VNInteract] items: 筛完一个道具都不剩，回退为全部可用");
                foreach (var it in _def.items)
                    if (it != null && !string.IsNullOrEmpty(it.id)) _usableItems.Add(it);
            }
            _item = _usableItems.Count > 0 ? _usableItems[0] : null;
        }

        static bool ParseOnOff(string s, bool def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            return s == "on" || s == "1" || s == "true" || s == "开";
        }

        // ------------------------------------------------------------------
        // UI（批次 2 会把道具栏和自绘光标接上来）
        // ------------------------------------------------------------------

        void BuildUi()
        {
            var root = (RectTransform)transform;

            // 部位框可视化层：挂在立绘下面，跟着立绘一起动
            if (showZones) BuildZoneOverlay();

            // 左下角 HUD 面板（**不铺全屏暗幕**，否则会盖住对话框）
            _hud = CreateImage("Hud", root, VNProceduralTextures.RoundedRectSprite, PanelColor);
            _hud.GetComponent<Image>().type = Image.Type.Sliced;
            _hud.anchorMin = _hud.anchorMax = new Vector2(0f, 0f);
            _hud.pivot = new Vector2(0f, 0f);
            _hud.anchoredPosition = new Vector2(40f, 40f);
            _hud.sizeDelta = new Vector2(420f, 108f);

            _stageText = CreateText("Stage", _hud, 26, AccentColor, "");
            var sr = (RectTransform)_stageText.transform;
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.74f);
            sr.sizeDelta = new Vector2(380f, 36f);

            var barBg = CreateImage("BarBg", _hud, VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.12f));
            barBg.GetComponent<Image>().type = Image.Type.Sliced;
            barBg.anchorMin = barBg.anchorMax = new Vector2(0.5f, 0.34f);
            barBg.sizeDelta = new Vector2(360f, 22f);

            var fill = CreateImage("BarFill", barBg, VNProceduralTextures.RoundedRectSprite,
                AccentColor);
            _barFill = fill.GetComponent<Image>();
            _barFill.type = Image.Type.Sliced;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            _timerText = CreateText("Timer", _hud, 22, new Color(1f, 1f, 1f, 0.75f), "");
            var tr = (RectTransform)_timerText.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.1f);
            tr.sizeDelta = new Vector2(380f, 30f);

            _hintText = CreateText("Hint", root, 30, new Color(1f, 1f, 1f, 0f), "");
            var hr = (RectTransform)_hintText.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0.5f, 0.86f);
            hr.sizeDelta = new Vector2(900f, 48f);

            if (_usableItems.Count > 1) BuildItemBar(root);
            if (_def.allowManualEnd) BuildEndButton(root);

            // 光标最后建 → 层级最上面，压在道具栏和结束钮之上
            var cursorGo = new GameObject("TouchCursor", typeof(RectTransform));
            cursorGo.transform.SetParent(root, false);
            _cursor = cursorGo.AddComponent<VNTouchCursor>();
            _cursor.Initialize(root, UiCamera);
            _cursor.SetItem(_item);

            RefreshHud();
            _hud.localScale = Vector3.one * 0.85f;
            _hud.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>
        /// 右侧竖排道具栏。**放右边不放底部**：底部是对话框的地盘，
        /// 而互动过程中角色随时会说话。
        /// </summary>
        void BuildItemBar(RectTransform root)
        {
            const float Cell = 104f, Gap = 12f;
            float height = _usableItems.Count * Cell + (_usableItems.Count - 1) * Gap + 24f;

            // 底板 raycastTarget=true：整条栏都不该触发抚摸，
            // 也让 IsPointerOverModuleUi 只查 root 直接子级就够
            var bar = CreateImage("ItemBar", root, VNProceduralTextures.RoundedRectSprite,
                PanelColor);
            var barImg = bar.GetComponent<Image>();
            barImg.type = Image.Type.Sliced;
            barImg.raycastTarget = true;
            bar.anchorMin = bar.anchorMax = new Vector2(1f, 0.5f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.anchoredPosition = new Vector2(-40f, 60f);
            bar.sizeDelta = new Vector2(Cell + 24f, height);

            _itemButtons.Clear();
            for (int i = 0; i < _usableItems.Count; i++)
            {
                var item = _usableItems[i];
                var cell = CreateImage("Item_" + item.id, bar,
                    VNProceduralTextures.RoundedRectSprite, Color.clear);
                var cellImg = cell.GetComponent<Image>();
                cellImg.type = Image.Type.Sliced;
                cellImg.raycastTarget = true;
                cell.anchorMin = cell.anchorMax = new Vector2(0.5f, 1f);
                cell.pivot = new Vector2(0.5f, 1f);
                cell.anchoredPosition = new Vector2(0f, -12f - i * (Cell + Gap));
                cell.sizeDelta = new Vector2(Cell, Cell);
                _itemButtons.Add(cellImg);

                if (item.icon != null)
                {
                    var icon = CreateImage("Icon", cell, item.icon, Color.white);
                    icon.GetComponent<Image>().preserveAspect = true;
                    icon.anchorMin = new Vector2(0.1f, 0.22f);
                    icon.anchorMax = new Vector2(0.9f, 0.95f);
                    icon.offsetMin = icon.offsetMax = Vector2.zero;
                }

                var label = CreateText("Label", cell, 18, new Color(1f, 1f, 1f, 0.85f),
                    item.Label);
                var lr = (RectTransform)label.transform;
                lr.anchorMin = new Vector2(0f, 0f);
                lr.anchorMax = new Vector2(1f, 0.2f);
                lr.offsetMin = lr.offsetMax = Vector2.zero;

                var btn = cell.gameObject.AddComponent<Button>();
                btn.targetGraphic = cellImg;
                var captured = item;
                btn.onClick.AddListener(() => SelectItem(captured));
            }
            RefreshItemBar();
        }

        void SelectItem(VNInteractionItem item)
        {
            if (_phase != Phase.Playing || item == null) return;
            _item = item;
            _cursor?.SetItem(item);
            _audio?.PlaySe("se1");
            RefreshItemBar();
        }

        void RefreshItemBar()
        {
            for (int i = 0; i < _itemButtons.Count && i < _usableItems.Count; i++)
                _itemButtons[i].color = _usableItems[i] == _item
                    ? new Color(1f, 0.45f, 0.62f, 0.45f)      // 选中
                    : new Color(1f, 1f, 1f, 0.06f);
        }

        void BuildEndButton(RectTransform root)
        {
            var btn = CreateImage("EndButton", root, VNProceduralTextures.RoundedRectSprite,
                new Color(0.16f, 0.13f, 0.2f, 0.9f));
            var img = btn.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;                 // 唯二能吃射线的东西之一
            btn.anchorMin = btn.anchorMax = new Vector2(1f, 0f);
            btn.pivot = new Vector2(1f, 0f);
            btn.anchoredPosition = new Vector2(-40f, 40f);
            btn.sizeDelta = new Vector2(170f, 64f);

            var label = CreateText("Label", btn, 26, Color.white, VNLocale.T("interact.end"));
            Stretch((RectTransform)label.transform);

            var button = btn.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() =>
            {
                if (_phase == Phase.Playing) Finish(ResolveOutcome(false));
            });
        }

        /// <summary>鼠标是否压在模块自己的可点 UI 上（道具栏 / 结束钮）</summary>
        bool IsPointerOverModuleUi(Vector2 screen)
        {
            var root = (RectTransform)transform;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i) as RectTransform;
                if (child == null || !child.gameObject.activeSelf) continue;
                var img = child.GetComponent<Image>();
                if (img == null || !img.raycastTarget) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(child, screen, UiCamera))
                    return true;
            }
            return false;
        }

        void RefreshHud()
        {
            if (_stageText == null) return;

            var st = StageAt(_score.Stage);
            string stageName = st != null && !string.IsNullOrEmpty(st.name)
                ? st.name : VNLocale.T("interact.stage", _score.Stage);
            string zoneName = _hoverZone != null ? "　·　" + _hoverZone.Label : "";
            _stageText.text = stageName + zoneName;

            float p = _score.ProgressTo(_def.ResolvedTargetStage);
            _barFill.rectTransform.anchorMax = new Vector2(p, 1f);

            if (_timed) _timerText.text = $"{Mathf.Max(0f, _timeLeft):0} s";
            else if (_def.rejectLimit > 0 && _score.RejectCount > 0)
                _timerText.text = VNLocale.T("interact.rejected",
                    _score.RejectCount, _def.rejectLimit);
            else _timerText.text = "";
        }

        void FlashHint()
        {
            var st = StageAt(_score.Stage);
            if (st == null || string.IsNullOrEmpty(st.name)) return;
            _hintText.text = st.name;
            _hintText.DOKill();
            _hintText.color = new Color(1f, 1f, 1f, 0f);
            _hintText.DOFade(1f, 0.25f).SetUpdate(true).SetLink(gameObject)
                     .OnComplete(() => _hintText.DOFade(0f, 0.6f).SetDelay(0.8f)
                                                .SetUpdate(true).SetLink(gameObject));
        }

        // ---- 部位框可视化（开发调试） ----

        void BuildZoneOverlay()
        {
            if (_char == null || _char.rect == null || _zoneDef == null) return;

            var go = new GameObject("ZoneOverlay", typeof(RectTransform));
            _zoneOverlay = (RectTransform)go.transform;
            _zoneOverlay.SetParent(_char.rect, false);   // 跟着立绘走（含缩放/位移）
            Stretch(_zoneOverlay);

            foreach (var z in _zoneDef.ZonesFor(
                         _char.image != null ? _char.image.sprite : null, _char.expression))
            {
                var marker = CreateImage("Zone_" + z.id, _zoneOverlay,
                    VNProceduralTextures.RoundedFrameSprite, ZoneDebugColor);
                marker.GetComponent<Image>().type = Image.Type.Sliced;
                marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
                marker.localRotation = Quaternion.Euler(0f, 0f, z.rotation);
                _zoneMarkers.Add(marker);

                var label = CreateText("L", marker, 20, new Color(1f, 1f, 1f, 0.8f), z.Label);
                Stretch((RectTransform)label.transform);
            }
        }

        void RefreshZoneMarkers()
        {
            if (_zoneOverlay == null || _char == null) return;

            var zones = _zoneDef.ZonesFor(
                _char.image != null ? _char.image.sprite : null, _char.expression);
            Vector2 size = _char.rect.rect.size;

            for (int i = 0; i < _zoneMarkers.Count && i < zones.Count; i++)
            {
                var z = zones[i];
                var m = _zoneMarkers[i];
                m.anchoredPosition = new Vector2(z.center.x * size.x, z.center.y * size.y);
                m.sizeDelta = new Vector2(z.size.x * size.x, z.size.y * size.y);
                var img = m.GetComponent<Image>();
                img.color = _score.Stage < z.unlockStage ? ZoneLockedColor
                          : z == _hoverZone ? new Color(1f, 0.85f, 0.4f, 0.45f)
                          : ZoneDebugColor;
            }
        }

        // ---- 程序化 UI 辅助 ----

        static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;    // 铁律：默认不吃射线，否则会挡住选项/对话推进
            return rect;
        }

        static TextMeshProUGUI CreateText(string name, RectTransform parent,
            int size, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
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
    }
}
