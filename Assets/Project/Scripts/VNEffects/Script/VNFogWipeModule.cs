using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：擦雾。整屏起雾盖住 CG，玩家按住左键把雾擦开，
    /// 在时限内把清晰度推过阈值。雾会重新凝结（边缘往中间吞 + 中心随机冒雾团）。
    ///
    /// 剧本用法（★ 用 cg: 指定要擦的图，**不要**在 event 之前先写 cg）：
    ///   event wipefog id:浴室镜面 cg:浴室镜面 time:60 target:70 stat:好感 rate:0.08 flag:擦雾1
    ///   * 完美 -&gt; 看清了
    ///   * 普通 -&gt; 看清了
    ///   * 失败
    ///
    ///   label 看清了
    ///   cg 浴室镜面        ← 事件结束后再摆上舞台，画面才留得住继续演
    ///
    /// 【为什么不能先 cg】
    /// 雾要到 OnLaunch 才铺得出来。剧本先 cg 的话，从 cg 的转场开始到事件真正启动为止，
    /// 清晰的画面一直摆在玩家眼前——谜底在开始擦之前就已经揭晓，整个玩法的前提就没了
    /// （中间再夹一句台词，还得等玩家点一下，暴露得更久）。交给模块自己铺底图，
    /// 进事件的第一帧就是盖满雾的状态。Lint 的 fogwipe-cg-before-event 会盯着这件事。
    /// 事件结束与下一条 cg 在同一帧交接（Runner 的 Destroy 是帧末延迟），不会闪。
    ///
    /// kwargs：
    ///   id:       VNFogWipeDef 资产 id（只登记一套时可省略）
    ///   cg:       要擦的 CG id（留空 = 舞台当前显示的 CG，再留空 = 当前背景）
    ///   time:     时限秒数
    ///   target:   「普通」档门槛（%）
    ///   perfect:  「完美」档门槛（%），达到即提前结束
    ///   vs:       角色 id（只用来取台词条上的显示名，**不碰立绘**）
    ///   stat:/rate: 清晰度换算成属性加成
    ///   flag:     成绩 flag 前缀
    ///
    /// 结果："完美" / "普通" / "失败"（资产可改名）。
    /// 同时写 flag「&lt;前缀&gt;_清晰度」「&lt;前缀&gt;_用时」「&lt;前缀&gt;_档位」。
    ///
    /// 【本模块不破模块三铁律】
    /// 角色在这个玩法里是**被动 CG**（不换表情、不碰立绘），所以不像 VNInteractionModule /
    /// VNAiTalkModule 那样需要破例去驱动舞台。全部绘制都在自己的 UI 子树内。
    ///
    /// 【为什么自己铺一份清晰 CG】
    /// 事件层排序 60 在对话框 40 之上，雾必须铺满整屏，于是必然盖住对话框。
    /// 与其让底下露出半截对话框，不如模块自己铺一份清晰 CG 打底，画面完全自洽；
    /// 过程中的台词也因此改用自绘的台词条（贴着画面下缘，像镜面上的旁白）。
    ///
    /// 【射线】
    /// 擦拭输入走 Update 轮询 Mouse.current（同 VNInteractionModule），不依赖 EventSystem，
    /// 所以自绘的一切 raycastTarget = false —— 只有 ESC 确认框的两个按钮例外。
    /// </summary>
    public class VNFogWipeModule : VNEventModule
    {
        [Header("本模板登记的擦雾定义（event wipefog id:xx 按 fogWipeId 查找）")]
        public List<VNFogWipeDef> fogWipes = new List<VNFogWipeDef>();

        /// <summary>笔刷直径等屏幕像素参数的参考画布宽度</summary>
        const float ReferenceWidth = 1920f;

        /// <summary>剩余时间进入这个秒数后开始「最后冲刺」演出</summary>
        const float UrgentSeconds = 5f;

        public const string ClarityFlagSuffix = "_清晰度";
        public const string TimeFlagSuffix = "_用时";
        public const string GradeFlagSuffix = "_档位";

        static readonly Color HudColor = new Color(0.06f, 0.08f, 0.13f, 0.72f);
        static readonly Color AccentColor = new Color(0.55f, 0.85f, 1f, 1f);
        static readonly Color UrgentColor = new Color(1f, 0.45f, 0.4f, 1f);
        static readonly Color PeakColor = new Color(1f, 0.92f, 0.5f, 0.9f);

        static readonly int IdMaskTex = Shader.PropertyToID("_MaskTex");
        static readonly int IdUVRect = Shader.PropertyToID("_UVRect");
        static readonly int IdFogColor = Shader.PropertyToID("_FogColor");
        static readonly int IdFogMix = Shader.PropertyToID("_FogMix");
        static readonly int IdFogDensity = Shader.PropertyToID("_FogDensity");
        static readonly int IdBlurAmount = Shader.PropertyToID("_BlurAmount");
        static readonly int IdBrightness = Shader.PropertyToID("_Brightness");
        static readonly int IdEdgeNoise = Shader.PropertyToID("_EdgeNoise");
        static readonly int IdNoiseScale = Shader.PropertyToID("_NoiseScale");
        static readonly int IdGrain = Shader.PropertyToID("_Grain");
        static readonly int IdFalloff = Shader.PropertyToID("_Falloff");

        enum Phase { Idle, Playing, Confirming, Ending }

        Phase _phase = Phase.Idle;
        Phase _phaseBeforeConfirm = Phase.Playing;

        VNFogWipeDef _def;
        readonly VNFogMask _mask = new VNFogMask();
        readonly VNFogScore _score = new VNFogScore();
        VNFogSfx _sfx;

        VNStage _stage;
        VNAudio _audio;
        VNStatsHud _statsHud;

        List<VNFogWipeDef.Stage> _stages = new List<VNFogWipeDef.Stage>();
        /// <summary>底图真的取自 CG 库时的 id（结束时用来解锁画廊）</summary>
        string _resolvedCgId;
        string _speaker;
        string _flagPrefix;
        string _statId;
        float _statRate;
        float _timeLimit, _timeLeft;
        float _perfectAt, _normalAt;      // 0~1
        float _brushRadiusMask, _brushFeather, _wipeStrength;
        float _nextBlobAt;
        float _lastTickSecond = -1f;

        Vector2 _lastMouse;
        bool _hasLastMouse;
        bool _wasHeld;

        // UI
        RectTransform _fogRect;
        RawImage _fogImage;
        Material _fogMat;
        RectTransform _hud;
        RectTransform _barFill, _peakMark;
        Image _barFillImage;
        TextMeshProUGUI _clarityText, _timerText, _hintText;
        RectTransform _lineBar;
        CanvasGroup _lineGroup;
        TextMeshProUGUI _lineSpeaker, _lineText;
        RectTransform _confirm;
        RectTransform _blobHint;
        Image _blobHintImage;
        VNTouchCursor _cursor;
        bool _cleanupDone;

        // ------------------------------------------------------------------
        // 启动
        // ------------------------------------------------------------------

        protected override void OnLaunch(VNEventContext ctx)
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.fogWipes, ref fogWipes);

            string id = ctx.Kw("id");
            foreach (var d in fogWipes)
                if (d != null && d.fogWipeId == id) { _def = d; break; }
            if (_def == null && fogWipes.Count == 1 && string.IsNullOrEmpty(id))
                _def = fogWipes[0];

            if (_def == null)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：擦雾模板没有登记 id「{id}」" +
                                 "的 VNFogWipeDef，直接返回");
                Done("");
                return;
            }

            _stage = ctx.stage;
            _audio = _stage != null ? _stage.vnAudio : null;
            _statsHud = FindFirstObjectByType<VNStatsHud>();

            if (!ResolveBaseSprite(ctx, out Sprite baseSprite))
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：擦雾找不到可擦的画面" +
                                 "（既没有显示中的 CG，也没有背景），直接返回");
                Done("");
                return;
            }

            _timeLimit = Mathf.Max(5f, ctx.KwF("time", _def.timeLimit));
            _timeLeft = _timeLimit;
            _perfectAt = Mathf.Clamp01(ctx.KwF("perfect", _def.targetPerfect) * 0.01f);
            _normalAt = Mathf.Clamp01(ctx.KwF("target", _def.targetNormal) * 0.01f);
            if (_normalAt > _perfectAt) _normalAt = _perfectAt;

            _flagPrefix = ctx.Kw("flag", _def.flagPrefix);
            if (string.IsNullOrEmpty(_flagPrefix)) _flagPrefix = "擦雾";
            _statId = ctx.Kw("stat");
            _statRate = ctx.KwF("rate", 0.1f);

            _speaker = _def.speakerName;
            if (string.IsNullOrEmpty(_speaker)) _speaker = ResolveCharacterName(ctx.Kw("vs"));

            _brushFeather = _def.brushFeather;
            _wipeStrength = _def.wipeStrength;

            _stages = _def.SortedStages();
            var thresholds = new List<float>();
            foreach (var s in _stages) thresholds.Add(s.threshold * 0.01f);
            _score.Init(thresholds);

            _mask.Build();

            BuildUi(baseSprite);
            ApplyFogParams();

            _sfx = new VNFogSfx();
            _sfx.Build(gameObject, _def, _audio);

            ScheduleNextBlob();
            _phase = Phase.Playing;
        }

        /// <summary>
        /// 要擦的那张图：剧本 cg: → 舞台当前 CG → 当前背景。
        ///
        /// ★ 推荐写法是 <c>event wipefog cg:xxx</c> 而**不要**在 event 之前先写 <c>cg xxx</c>：
        /// 模块要到 OnLaunch 才铺得出雾，剧本先 cg 的话，从 cg 的转场开始到 event 真正启动
        /// 为止，清晰的 CG 就一直摆在玩家眼前——谜底在开始擦之前就已经揭晓了
        /// （中间要是再夹一句台词，还得等玩家点一下，暴露得更久）。
        /// 交给模块自己铺，进事件的第一帧就是盖满雾的状态。
        /// Lint 的 fogwipe-cg-before-event 会盯着这件事。
        /// </summary>
        bool ResolveBaseSprite(VNEventContext ctx, out Sprite sprite)
        {
            sprite = null;
            if (_stage == null) return false;

            string cgId = ctx.Kw("cg", _stage.CurrentCgId);
            if (!string.IsNullOrEmpty(cgId))
            {
                var entry = _stage.cgLibrary.Find(c => c != null && c.id == cgId);
                if (entry != null && entry.sprite != null)
                {
                    sprite = entry.sprite;
                    _resolvedCgId = cgId;
                }
                else if (!string.IsNullOrEmpty(ctx.Kw("cg")))
                    Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：擦雾的 cg「{cgId}」" +
                                     "没在 CG 库里登记，退回当前背景");
            }

            if (sprite == null && _stage.backgroundImage != null)
                sprite = _stage.backgroundImage.sprite;

            return sprite != null;
        }

        string ResolveCharacterName(string charId)
        {
            if (string.IsNullOrEmpty(charId)) return "";
            var cfg = VNGameConfig.Active;
            if (cfg != null && cfg.characters != null)
                foreach (var c in cfg.characters)
                    if (c != null && c.id == charId)
                        return string.IsNullOrEmpty(c.displayName) ? c.id : c.displayName;
            return charId;
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        void Update()
        {
            if (VNPause.IsPaused) return;

            var kb = Keyboard.current;
            if (_phase == Phase.Confirming)
            {
                if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseConfirm();
                return;
            }
            if (_phase != Phase.Playing) return;

            float dt = VNTime.Delta;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { OpenConfirm(); return; }

            HandleWipe(dt);
            HandleRefog(dt);

            _mask.Flush();
            var tick = _score.Update(_mask.Clarity);
            if (tick.stageUp) PlayStage(tick.newStage);

            _timeLeft -= dt;
            RefreshHud();

            if (_score.Reached(_perfectAt)) { Finish(true); return; }
            if (_timeLeft <= 0f) Finish(false);
        }

        void HandleWipe(float dt)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 screen = mouse.position.ReadValue();
            bool held = mouse.leftButton.isPressed;

            float speed = 0f;
            if (_hasLastMouse && dt > 0f)
                speed = (screen - _lastMouse).magnitude / dt;

            bool inside = RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _fogRect, screen, UiCamera, out Vector2 local);

            if (held && inside)
            {
                var size = _fogRect.rect.size;
                if (size.x > 0.01f && size.y > 0.01f)
                {
                    var uv = new Vector2(local.x / size.x + 0.5f, local.y / size.y + 0.5f);
                    if (!_wasHeld) _mask.BeginStroke();
                    _mask.StrokeTo(uv, _brushRadiusMask, _brushFeather, _wipeStrength);
                }
            }
            else if (_wasHeld) _mask.EndStroke();

            _cursor?.SetState(inside, held);
            _sfx?.SetWipeSpeed(held && inside ? speed : 0f, dt);

            _wasHeld = held && inside;
            _lastMouse = screen;
            _hasLastMouse = true;
        }

        void HandleRefog(float dt)
        {
            _mask.ErodeFromEdges(_def.edgeRate, dt);

            if (_def.blobRate <= 0f || VNTime.Time < _nextBlobAt) return;

            // 雾团只在中心区冒（最外圈 15% 留给边缘侵蚀，两种回雾各管一片，
            // 叠在一起的话边缘会掉得比设定值快一倍）
            var uv = new Vector2(Random.Range(0.15f, 0.85f), Random.Range(0.15f, 0.85f));
            float radiusScreen = Random.Range(_def.blobRadiusMin, _def.blobRadiusMax);
            float radiusMask = ScreenToMask(radiusScreen);
            float interval = 0.5f * (_def.blobIntervalMin + _def.blobIntervalMax);
            float strength = VNFogMask.BlobStrengthFor(_def.blobRate, interval, radiusMask);

            _mask.FogBlob(uv, radiusMask, strength);
            _sfx?.Play(VNFogSfx.Kind.Blob);
            ShowBlobHint(uv, radiusScreen);
            ScheduleNextBlob();
        }

        void ScheduleNextBlob() =>
            _nextBlobAt = VNTime.Time + Random.Range(_def.blobIntervalMin, _def.blobIntervalMax);

        /// <summary>雾团出现的位置闪一下——不提示的话玩家根本不知道哪里又糊了</summary>
        void ShowBlobHint(Vector2 uv, float radiusScreen)
        {
            if (_blobHint == null) return;
            var size = _fogRect.rect.size;
            _blobHint.anchoredPosition = new Vector2((uv.x - 0.5f) * size.x, (uv.y - 0.5f) * size.y);
            _blobHint.sizeDelta = Vector2.one * radiusScreen * 3f;

            _blobHintImage.DOKill();
            _blobHintImage.color = new Color(1f, 1f, 1f, 0.28f);
            _blobHintImage.DOFade(0f, 0.55f).SetUpdate(true).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 阶段台词
        // ------------------------------------------------------------------

        void PlayStage(int index)
        {
            if (index < 0 || index >= _stages.Count) return;
            var stage = _stages[index];

            ApplyStatOps(stage.reward);

            var line = stage.PickLine();
            if (line == null) return;

            if (_audio != null && !string.IsNullOrEmpty(line.voice))
                _audio.PlayVoice(line.voice);

            _lineSpeaker.text = _speaker ?? "";
            _lineSpeaker.gameObject.SetActive(!string.IsNullOrEmpty(_speaker));
            _lineText.text = line.Display;

            _lineGroup.DOKill();
            _lineGroup.alpha = 0f;
            _lineBar.gameObject.SetActive(true);
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
            seq.Append(_lineGroup.DOFade(1f, 0.25f));
            seq.AppendInterval(3.2f);
            seq.Append(_lineGroup.DOFade(0f, 0.5f));
        }

        /// <summary>属性奖励：有 HUD 就走它（钳制 + 飘字），否则退回裸 flag 加减</summary>
        void ApplyStatOps(List<VNShopDef.StatOp> ops)
        {
            if (ops == null) return;
            foreach (var op in ops)
            {
                if (op == null || string.IsNullOrEmpty(op.statId) || op.amount == 0) continue;
                if (_statsHud != null)
                    _statsHud.Apply(op.statId, (op.amount >= 0 ? "+" : "") + op.amount, false, 0);
                else
                    VNFlags.Add(op.statId, op.amount);
            }
        }

        // ------------------------------------------------------------------
        // HUD
        // ------------------------------------------------------------------

        void RefreshHud()
        {
            float clarity = _score.Clarity;
            float peak = _score.Peak;

            _clarityText.text = VNLocale.T("fogwipe.clarity", Mathf.RoundToInt(clarity * 100f));
            _barFill.anchorMax = new Vector2(Mathf.Clamp01(clarity), 1f);

            // 峰值刻度：结算按峰值算，就必须让玩家看得见峰值在哪，
            // 否则「我明明只有 60% 怎么算普通」会变成一个说不清的疑问
            _peakMark.anchorMin = new Vector2(Mathf.Clamp01(peak), 0f);
            _peakMark.anchorMax = new Vector2(Mathf.Clamp01(peak), 1f);

            float left = Mathf.Max(0f, _timeLeft);
            _timerText.text = VNLocale.T("fogwipe.timer", left.ToString("0.0"));

            bool urgent = left <= UrgentSeconds;
            if (urgent)
            {
                float pulse = 1f + 0.1f * Mathf.Abs(Mathf.Sin(VNTime.Time * 8f));
                _timerText.transform.localScale = Vector3.one * pulse;
                _timerText.color = UrgentColor;

                // 每整秒响一下（用取整比较，不用累加器：暂停/切窗口后不会漂）
                float second = Mathf.Ceil(left);
                if (!Mathf.Approximately(second, _lastTickSecond))
                {
                    _lastTickSecond = second;
                    _sfx?.Play(VNFogSfx.Kind.Tick);
                }
            }
            else
            {
                _timerText.transform.localScale = Vector3.one;
                _timerText.color = new Color(1f, 1f, 1f, 0.85f);
            }

            _barFillImage.color = clarity >= _normalAt ? new Color(0.45f, 0.9f, 0.6f, 1f)
                                                       : AccentColor;
        }

        // ------------------------------------------------------------------
        // 结束
        // ------------------------------------------------------------------

        /// <param name="reachedTarget">true = 擦到完美档提前结束，false = 时限到</param>
        void Finish(bool reachedTarget)
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            _sfx?.Stop();
            _cursor?.Dispose();

            float peak = _score.Peak;
            string outcome = VNFogScore.Grade(peak, _perfectAt, _normalAt,
                _def.outcomePerfect, _def.outcomeNormal, _def.outcomeFail);

            WriteFlags(peak, outcome);
            ApplyClarityStat(peak);

            // 解锁画廊。推荐写法下剧本不再提前 cg，而解锁本来是 VNStage.ShowCg 顺手做的，
            // 不在这里补一次就会漏——玩家明明看过这张 CG，画廊里却是灰的。
            // 只在真的擦出点东西时解锁（一进来就 ESC 放弃不该算看过）。
            if (!string.IsNullOrEmpty(_resolvedCgId) && peak >= 0.2f)
                VNCgUnlocks.Unlock(_resolvedCgId);

            _hintText.gameObject.SetActive(false);
            if (_confirm != null) _confirm.gameObject.SetActive(false);

            // 达标时剩余的雾「哗」地一次性散开（不是慢慢消失）——给足高潮；
            // 时限到的话只散一半，让玩家看得出自己没擦干净
            float target = reachedTarget ? 0f : _def.fogDensity * (1f - peak) * 0.6f;
            if (_fogMat != null)
                _fogMat.DOFloat(target, IdFogDensity, 0.55f)
                       .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);

            if (reachedTarget) _sfx?.Play(VNFogSfx.Kind.Clear);

            ShowResult(peak, outcome, reachedTarget);

            DOVirtual.DelayedCall(2.2f, () => Done(outcome), true).SetLink(gameObject);
        }

        /// <summary>
        /// 档位写**整数** 2/1/0 而不是结果名字符串——VNFlags 是整型字典
        /// （同 VNPhotoBoothModule 的 _档位），剧本可以直接 if 擦雾1_档位&gt;=2。
        /// 结果名本身走 event 的「* 结果行」分支，不需要再进 flag。
        /// </summary>
        void WriteFlags(float peak, string outcome)
        {
            VNFlags.Set(_flagPrefix + ClarityFlagSuffix, Mathf.RoundToInt(peak * 100f));
            VNFlags.Set(_flagPrefix + TimeFlagSuffix,
                Mathf.RoundToInt(_timeLimit - Mathf.Max(0f, _timeLeft)));

            int grade = outcome == _def.outcomePerfect ? 2
                : outcome == _def.outcomeNormal ? 1 : 0;
            VNFlags.Set(_flagPrefix + GradeFlagSuffix, grade);
        }

        /// <summary>清晰度换算成属性加成（stat: / rate:）</summary>
        void ApplyClarityStat(float peak)
        {
            if (string.IsNullOrEmpty(_statId)) return;
            int amount = Mathf.RoundToInt(peak * 100f * _statRate);
            if (amount == 0) return;

            if (_statsHud != null) _statsHud.Apply(_statId, "+" + amount, false, 0);
            else VNFlags.Add(_statId, amount);
        }

        void ShowResult(float peak, string outcome, bool reachedTarget)
        {
            var text = CreateText("Result", (RectTransform)transform, 96, Color.white, outcome);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(1200f, 160f);
            rect.anchoredPosition = new Vector2(0f, 40f);
            text.font = VNFont.DisplayAsset != null ? VNFont.DisplayAsset : VNFont.Asset;
            text.color = reachedTarget ? new Color(1f, 0.95f, 0.7f) : Color.white;

            var sub = CreateText("ResultSub", (RectTransform)transform, 40,
                new Color(1f, 1f, 1f, 0.85f),
                VNLocale.T("fogwipe.result", Mathf.RoundToInt(peak * 100f)));
            var subRect = (RectTransform)sub.transform;
            subRect.anchorMin = subRect.anchorMax = new Vector2(0.5f, 0.5f);
            subRect.pivot = new Vector2(0.5f, 0.5f);
            subRect.sizeDelta = new Vector2(900f, 70f);
            subRect.anchoredPosition = new Vector2(0f, -50f);

            rect.localScale = Vector3.one * 0.7f;
            rect.DOScale(1f, 0.45f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // ESC 确认
        // ------------------------------------------------------------------

        void OpenConfirm()
        {
            if (_confirm == null) BuildConfirm();
            _phaseBeforeConfirm = _phase;
            _phase = Phase.Confirming;
            _confirm.gameObject.SetActive(true);
            _cursor?.SetState(false, false);
            _sfx?.SetWipeSpeed(0f, 1f);
            // 确认框弹出时松开笔，否则回来会从旧位置拉一条长线
            _mask.EndStroke();
            _wasHeld = false;
        }

        void CloseConfirm()
        {
            if (_confirm != null) _confirm.gameObject.SetActive(false);
            _phase = _phaseBeforeConfirm;
            _hasLastMouse = false;
        }

        /// <summary>放弃：按当前峰值判档（不强行判失败——擦到 90% 才放弃的人不该被抹掉成绩）</summary>
        void QuitNow()
        {
            if (_confirm != null) _confirm.gameObject.SetActive(false);
            _phase = Phase.Playing;
            _timeLeft = 0f;
            Finish(false);
        }

        // ------------------------------------------------------------------
        // 清理（★ 光标必须在四条路径上都还回去）
        // ------------------------------------------------------------------

        public override void CancelForDebug() => RunCleanup();

        void OnDestroy() => RunCleanup();

        void OnDisable() => RunCleanup();

        void RunCleanup()
        {
            if (_cleanupDone) return;
            _cleanupDone = true;

            // 漏掉任何一条路径，玩家的系统鼠标指针就永久消失了
            // （VNInteractionModule 踩过，是所有 bug 里最难受的一种）
            _cursor?.Dispose();
            _sfx?.Stop();
            _mask.Destroy();
            if (_fogMat != null) { Destroy(_fogMat); _fogMat = null; }
        }

        // ------------------------------------------------------------------
        // UI 搭建
        // ------------------------------------------------------------------

        void BuildUi(Sprite baseSprite)
        {
            var root = (RectTransform)transform;
            Stretch(root);

            var texture = baseSprite.texture;
            var rect = baseSprite.textureRect;
            var uvRect = new Rect(rect.x / texture.width, rect.y / texture.height,
                                  rect.width / texture.width, rect.height / texture.height);

            // [0] 清晰底图
            var baseGo = NewChild("BaseImage", root);
            Stretch(baseGo);
            var baseImage = baseGo.gameObject.AddComponent<RawImage>();
            baseImage.texture = texture;
            baseImage.uvRect = uvRect;
            baseImage.raycastTarget = false;

            // [1] 雾层
            _fogRect = NewChild("FogLayer", root);
            Stretch(_fogRect);
            _fogImage = _fogRect.gameObject.AddComponent<RawImage>();
            _fogImage.texture = texture;
            _fogImage.uvRect = uvRect;
            _fogImage.raycastTarget = false;

            var shader = Shader.Find("VN/FogWipe");
            if (shader != null)
            {
                _fogMat = new Material(shader) { hideFlags = HideFlags.DontSave };
                _fogMat.SetTexture(IdMaskTex, _mask.Texture);
                _fogMat.SetVector(IdUVRect,
                    new Vector4(uvRect.x, uvRect.y, uvRect.width, uvRect.height));
                _fogImage.material = _fogMat;
            }
            else
            {
                Debug.LogError("[VNFogWipe] 找不到 shader「VN/FogWipe」，" +
                               "雾层会退化成一张不透明的底图（检查 Assets/Art/Shaders/VNFogWipe.shader）");
            }

            // 笔刷半径换算：屏幕像素 → 掩码像素。
            // 384/1920 == 216/1080，两个方向系数相同，所以屏幕上的正圆在掩码里还是正圆
            _brushRadiusMask = ScreenToMask(_def.brushDiameter * 0.5f);

            // [2] 雾团提示
            _blobHint = NewChild("BlobHint", root);
            _blobHint.anchorMin = _blobHint.anchorMax = new Vector2(0.5f, 0.5f);
            _blobHintImage = _blobHint.gameObject.AddComponent<Image>();
            _blobHintImage.sprite = VNProceduralTextures.RadialGlowSprite;
            _blobHintImage.color = Color.clear;
            _blobHintImage.raycastTarget = false;

            BuildHud(root);
            BuildLineBar(root);

            // [5] 光标（最上层，绝不吃射线）
            var cursorGo = NewChild("Cursor", root);
            _cursor = cursorGo.gameObject.AddComponent<VNTouchCursor>();
            _cursor.Initialize(root, UiCamera);
            if (_def.cursor != null && _def.cursor.icon == null)
                _def.cursor.icon = VNFogTextures.For(_def.cursorKind);
            _cursor.SetItem(_def.cursor);
        }

        void ApplyFogParams()
        {
            if (_fogMat == null) return;
            _fogMat.SetColor(IdFogColor, _def.fogColor);
            _fogMat.SetFloat(IdFogMix, _def.fogMix);
            _fogMat.SetFloat(IdFogDensity, _def.fogDensity);
            _fogMat.SetFloat(IdBlurAmount, _def.blurAmount);
            _fogMat.SetFloat(IdBrightness, _def.brightness);
            _fogMat.SetFloat(IdEdgeNoise, _def.edgeNoise);
            _fogMat.SetFloat(IdNoiseScale, _def.noiseScale);
            _fogMat.SetFloat(IdGrain, _def.grain);
            _fogMat.SetFloat(IdFalloff, _def.falloff);
        }

        void BuildHud(RectTransform root)
        {
            _hud = NewChild("Hud", root);
            _hud.anchorMin = new Vector2(0.5f, 1f);
            _hud.anchorMax = new Vector2(0.5f, 1f);
            _hud.pivot = new Vector2(0.5f, 1f);
            _hud.sizeDelta = new Vector2(720f, 96f);
            _hud.anchoredPosition = new Vector2(0f, -28f);

            var bg = _hud.gameObject.AddComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = HudColor;
            bg.raycastTarget = false;

            _clarityText = CreateText("Clarity", _hud, 34, Color.white, "");
            var clarityRect = (RectTransform)_clarityText.transform;
            clarityRect.anchorMin = new Vector2(0f, 1f);
            clarityRect.anchorMax = new Vector2(0.6f, 1f);
            clarityRect.pivot = new Vector2(0f, 1f);
            clarityRect.anchoredPosition = new Vector2(28f, -14f);
            clarityRect.sizeDelta = new Vector2(0f, 44f);
            _clarityText.alignment = TextAlignmentOptions.Left;

            _timerText = CreateText("Timer", _hud, 34, new Color(1f, 1f, 1f, 0.85f), "");
            var timerRect = (RectTransform)_timerText.transform;
            timerRect.anchorMin = new Vector2(1f, 1f);
            timerRect.anchorMax = new Vector2(1f, 1f);
            timerRect.pivot = new Vector2(1f, 1f);
            timerRect.anchoredPosition = new Vector2(-28f, -14f);
            timerRect.sizeDelta = new Vector2(200f, 44f);
            _timerText.alignment = TextAlignmentOptions.Right;

            var barBg = NewChild("BarBg", _hud);
            barBg.anchorMin = new Vector2(0f, 0f);
            barBg.anchorMax = new Vector2(1f, 0f);
            barBg.pivot = new Vector2(0.5f, 0f);
            barBg.offsetMin = new Vector2(28f, 20f);
            barBg.offsetMax = new Vector2(-28f, 20f);
            barBg.sizeDelta = new Vector2(barBg.sizeDelta.x, 16f);
            var barBgImage = barBg.gameObject.AddComponent<Image>();
            barBgImage.sprite = VNProceduralTextures.RoundedRectSprite;
            barBgImage.type = Image.Type.Sliced;
            barBgImage.color = new Color(1f, 1f, 1f, 0.12f);
            barBgImage.raycastTarget = false;

            _barFill = NewChild("BarFill", barBg);
            _barFill.anchorMin = Vector2.zero;
            _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.offsetMin = Vector2.zero;
            _barFill.offsetMax = Vector2.zero;
            _barFillImage = _barFill.gameObject.AddComponent<Image>();
            _barFillImage.sprite = VNProceduralTextures.RoundedRectSprite;
            _barFillImage.type = Image.Type.Sliced;
            _barFillImage.color = AccentColor;
            _barFillImage.raycastTarget = false;

            // 目标线：普通档门槛画一道白线，玩家才知道「擦到哪算够」
            var goalMark = NewChild("GoalMark", barBg);
            goalMark.anchorMin = new Vector2(_normalAt, 0f);
            goalMark.anchorMax = new Vector2(_normalAt, 1f);
            goalMark.pivot = new Vector2(0.5f, 0.5f);
            goalMark.sizeDelta = new Vector2(3f, 10f);
            goalMark.anchoredPosition = Vector2.zero;
            var goalImage = goalMark.gameObject.AddComponent<Image>();
            goalImage.color = new Color(1f, 1f, 1f, 0.7f);
            goalImage.raycastTarget = false;

            _peakMark = NewChild("PeakMark", barBg);
            _peakMark.anchorMin = _peakMark.anchorMax = new Vector2(0f, 0f);
            _peakMark.pivot = new Vector2(0.5f, 0.5f);
            _peakMark.sizeDelta = new Vector2(3f, 22f);
            _peakMark.anchoredPosition = Vector2.zero;
            var peakImage = _peakMark.gameObject.AddComponent<Image>();
            peakImage.color = PeakColor;
            peakImage.raycastTarget = false;

            _hintText = CreateText("Hint", root, 30, new Color(1f, 1f, 1f, 0.7f),
                VNLocale.T("fogwipe.hint"));
            var hintRect = (RectTransform)_hintText.transform;
            hintRect.anchorMin = new Vector2(0.5f, 1f);
            hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.sizeDelta = new Vector2(900f, 44f);
            hintRect.anchoredPosition = new Vector2(0f, -136f);
        }

        void BuildLineBar(RectTransform root)
        {
            _lineBar = NewChild("LineBar", root);
            _lineBar.anchorMin = new Vector2(0.5f, 0f);
            _lineBar.anchorMax = new Vector2(0.5f, 0f);
            _lineBar.pivot = new Vector2(0.5f, 0f);
            _lineBar.sizeDelta = new Vector2(1240f, 132f);
            _lineBar.anchoredPosition = new Vector2(0f, 56f);

            var bg = _lineBar.gameObject.AddComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.05f, 0.07f, 0.12f, 0.78f);
            bg.raycastTarget = false;

            _lineGroup = _lineBar.gameObject.AddComponent<CanvasGroup>();
            _lineGroup.alpha = 0f;
            _lineGroup.blocksRaycasts = false;
            _lineGroup.interactable = false;

            _lineSpeaker = CreateText("Speaker", _lineBar, 30, AccentColor, "");
            var speakerRect = (RectTransform)_lineSpeaker.transform;
            speakerRect.anchorMin = new Vector2(0f, 1f);
            speakerRect.anchorMax = new Vector2(0.5f, 1f);
            speakerRect.pivot = new Vector2(0f, 1f);
            speakerRect.anchoredPosition = new Vector2(34f, -12f);
            speakerRect.sizeDelta = new Vector2(0f, 38f);
            _lineSpeaker.alignment = TextAlignmentOptions.Left;

            _lineText = CreateText("Line", _lineBar, 34, Color.white, "");
            _lineText.textWrappingMode = TextWrappingModes.Normal;
            _lineText.alignment = TextAlignmentOptions.TopLeft;
            var lineRect = (RectTransform)_lineText.transform;
            lineRect.anchorMin = new Vector2(0f, 0f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.offsetMin = new Vector2(34f, 16f);
            lineRect.offsetMax = new Vector2(-34f, -52f);

            _lineBar.gameObject.SetActive(false);
        }

        void BuildConfirm()
        {
            var root = (RectTransform)transform;
            _confirm = NewChild("Confirm", root);
            _confirm.anchorMin = _confirm.anchorMax = new Vector2(0.5f, 0.5f);
            _confirm.pivot = new Vector2(0.5f, 0.5f);
            _confirm.sizeDelta = new Vector2(700f, 260f);
            _confirm.anchoredPosition = Vector2.zero;

            var bg = _confirm.gameObject.AddComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.07f, 0.08f, 0.13f, 0.97f);
            bg.raycastTarget = true;   // 确认框是唯一吃射线的东西

            var title = CreateText("Title", _confirm, 40, Color.white,
                VNLocale.T("fogwipe.quit"));
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.offsetMin = new Vector2(30f, 0f);
            titleRect.offsetMax = new Vector2(-30f, 0f);
            titleRect.anchoredPosition = new Vector2(0f, -50f);
            titleRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, 60f);

            MakeConfirmButton(VNLocale.T("fogwipe.quit.yes"), -160f,
                new Color(0.55f, 0.2f, 0.24f, 1f), QuitNow);
            MakeConfirmButton(VNLocale.T("fogwipe.quit.no"), 160f,
                new Color(0.2f, 0.32f, 0.5f, 1f), CloseConfirm);
        }

        void MakeConfirmButton(string label, float x, Color color,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Button", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_confirm, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(260f, 72f);
            rect.anchoredPosition = new Vector2(x, 40f);

            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", rect, 32, Color.white, label);
            Stretch((RectTransform)text.transform);
        }

        // ------------------------------------------------------------------
        // 辅助
        // ------------------------------------------------------------------

        /// <summary>屏幕像素长度 → 掩码像素长度</summary>
        float ScreenToMask(float screenPixels)
        {
            float width = _fogRect != null && _fogRect.rect.width > 1f
                ? _fogRect.rect.width : ReferenceWidth;
            return screenPixels * VNFogMask.Width / width;
        }

        Canvas _canvas;
        Canvas Canvas
        {
            get
            {
                if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
                return _canvas;
            }
        }

        Camera _uiCamera;
        /// <summary>
        /// 本项目 Canvas 是 Screen Space - **Camera**，
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

        static RectTransform NewChild(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
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
