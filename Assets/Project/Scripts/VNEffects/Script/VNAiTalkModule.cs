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
    /// AI 自由聊天事件模块：接 Gemini 实时生成女主角台词，玩家从 AI 同时生成的
    /// 三个候选回复里选一个，循环推进。
    ///
    /// 剧本用法：
    ///   show 星野结衣 center            ← 先把人摆上台，模块不负责出场
    ///   event aitalk vs:星野结衣 persona:星野结衣_日常 turns:12 topic:社团招新 \
    ///                place:放学后的教室 me:我 stat:好感 rate:2 flag:AI聊天_
    ///   * 好感提升 -> 好结局
    ///   * 普通
    ///   * 冷场 -> 尴尬收场
    ///   * 失败                          ← 断网/无key/被拦，必须接住否则玩家卡死
    ///
    /// 【与模块三铁律的关系】
    ///   铁律说「不直接改舞台演出」，但 AI 控制表情恰恰就是改舞台。这里**刻意破例**，
    ///   因为自绘立绘要把眨眼/口型/色调匹配/出场动画全部重接一遍，代价远大于收益。
    ///   破例的边界收得很紧：
    ///     - 只碰「表情」和「对话框内容」两样，绝不碰位置/缩放/背景/特效
    ///     - 进入时记下原表情，正常结束、ESC 退出、调试中断（CancelForDebug）
    ///       三条路径都还原，保证退出后舞台与剧本记录一致
    ///   其余两条铁律严格遵守：计时全用 unscaledTime、Tween 全部
    ///   SetUpdate(true) + SetLink(gameObject)。
    ///
    /// 【射线的坑】
    ///   EventLayer 排序 60，选项面板在 45——**在本模块下方**。所以本模块自绘的
    ///   任何东西都必须 raycastTarget=false，否则会把选项的点击全吃掉。
    ///   唯一的例外是 ESC 确认框，它就是要独占输入。
    /// </summary>
    public class VNAiTalkModule : VNEventModule
    {
        // 结果名（剧本「* 结果行」精确匹配这四个字符串）
        public const string OutcomeUp = "好感提升";
        public const string OutcomeNormal = "普通";
        public const string OutcomeCold = "冷场";
        public const string OutcomeFailed = "失败";

        [Header("人格库（留空 = 运行时从 VNGameConfig.aiPersonas 取）")]
        public List<VNAiPersonaDef> personas = new List<VNAiPersonaDef>();

        [Header("选项飞入前的停顿（秒）：给眼睛一个缓冲，别紧贴着最后一个字弹出来")]
        [Range(0f, 1.5f)] public float optionDelay = 0.4f;

        [Header("对话日志：聊完把全程写成 .md + .json（调人格 / 查成本用）\n" +
                "编辑器写到 <项目根>/AiTalkLogs/，Build 写到 persistentDataPath")]
        public VNAiLogMode logMode = VNAiLogMode.EditorOnly;

        static readonly Color HintColor = new Color(1f, 1f, 1f, 0.5f);
        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.14f, 0.96f);
        static readonly Color AccentColor = new Color(0.45f, 0.8f, 1f, 1f);

        enum Phase { Booting, Thinking, Speaking, Pausing, Choosing, Confirming, Ending }

        Phase _phase = Phase.Booting;
        VNEventContext _ctx;
        VNStage _stage;
        VNAiPersonaDef _persona;
        VNAiConversation _convo;

        string _charId;
        string _originalExpression;      // 进入前的表情，退出时还原
        bool _expressionTouched;

        int _maxTurns;
        int _affectionTotal;
        string _statName, _flagPrefix;
        int _statRate = 1;

        // 全程记录，FinishWith 时落盘。就地 new 而不是在 OnLaunch 里建——
        // OnLaunch 有几条提前 return（找不到人格等），那些路径也会走到 FinishWith
        readonly VNAiTalkLog _log = new VNAiTalkLog();

        // 自绘 UI（全部 raycastTarget=false）
        RectTransform _hintRoot;
        TextMeshProUGUI _turnLabel, _thinkingLabel;
        RectTransform _confirmRoot;
        Tween _thinkingTween;

        bool _escConfirmed;              // ESC 确认框选了「结束对话」

        // ──────────────────────────────────────────────────────────
        // 生命周期
        // ──────────────────────────────────────────────────────────

        protected override void OnLaunch(VNEventContext ctx)
        {
            _ctx = ctx;
            _stage = ctx.stage;

            _charId = ctx.Kw("vs");
            _persona = ResolvePersona(ctx.Kw("persona"), _charId);
            if (_persona == null)
            {
                Debug.LogError($"[VNAiTalk] 第 {ctx.line} 行：找不到人格" +
                               $"（persona:{ctx.Kw("persona")} vs:{_charId}）。" +
                               "先 Create → VN → AI Persona 建一套，并跑 " +
                               "Tools → VN Effects → Install AI Talk Module To Scene。");
                Done(OutcomeFailed);
                return;
            }

            var errors = _persona.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError($"[VNAiTalk] 人格「{_persona.id}」配置有问题：\n  - " +
                               string.Join("\n  - ", errors));
                Done(OutcomeFailed);
                return;
            }

            if (string.IsNullOrEmpty(_charId) && _persona.character != null)
                _charId = _persona.character.id;

            // 记下原表情：还原的依据。角色不在台上时为 null，还原步骤自然跳过。
            var active = _stage != null ? _stage.Get(_charId) : null;
            if (active == null)
                Debug.LogWarning($"[VNAiTalk] 第 {ctx.line} 行：角色「{_charId}」不在台上，" +
                                 "立绘和表情不会有变化。建议在 event 前先写一行 " +
                                 $"show {_charId} center");
            else
                _originalExpression = active.expression;

            _maxTurns = Mathf.Max(1, ctx.KwI("turns", _persona.defaultTurns));
            _statName = ctx.Kw("stat");
            _statRate = Mathf.Max(1, ctx.KwI("rate", 1));
            _flagPrefix = ctx.Kw("flag");

            // options:N 覆盖人格资产的扩展开关；0 = 用资产的设定
            int optionOverride = ctx.KwI("options", 0);
            _convo = new VNAiConversation(_persona, optionOverride);
            if (optionOverride > 0 && _convo.OptionCount != optionOverride)
                Debug.LogWarning($"[VNAiTalk] 第 {ctx.line} 行：options:{optionOverride} 超出人格" +
                                 $"「{_persona.id}」能提供的范围，实际用 {_convo.OptionCount} 条" +
                                 $"（optionTones 现有 {_persona.optionTones?.Count ?? 0} 条，" +
                                 $"下限 {VNAiPersonaDef.MinOptions}）");

            BuildUi();
            _stage?.dialogue?.Show();   // EventCo 进来前 HideBox 过，这里请回来
            StartCoroutine(TalkLoop());
        }

        /// <summary>剧本被停止 / 调试中断：必须走和正常结束一样的还原路径。</summary>
        public override void CancelForDebug()
        {
            RestoreStage();
            _thinkingTween?.Kill();
        }

        void OnDestroy()
        {
            _thinkingTween?.Kill();
        }

        void Update()
        {
            // 打字过程中点一下 = 立刻显示完整台词（和正常台词行的手感一致）
            if (_phase == Phase.Speaking && AnyAdvancePressed())
                _stage?.dialogue?.CompleteTyping();

            // ESC：弹确认框。Choosing / Speaking 阶段都允许，Thinking 时不打断请求
            if (_phase != Phase.Confirming && _phase != Phase.Ending &&
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ShowConfirm();
        }

        static bool AnyAdvancePressed() =>
            (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
            (Keyboard.current != null &&
             (Keyboard.current.spaceKey.wasPressedThisFrame ||
              Keyboard.current.enterKey.wasPressedThisFrame));

        // ──────────────────────────────────────────────────────────
        // 主循环
        // ──────────────────────────────────────────────────────────

        IEnumerator TalkLoop()
        {
            string playerSaid = null;

            for (int turn = 0; turn < _maxTurns; turn++)
            {
                RefreshTurnLabel(turn);

                // ① 请求
                _phase = Phase.Thinking;
                SetThinking(true);

                var req = _convo.BuildRequest(playerSaid, BuildContext(turn));
                // system prompt 只在首轮存进日志：后续轮次只有「剩余轮数」在变
                if (turn == 0)
                    _log.Begin(_persona, _ctx, req.systemInstruction, _maxTurns, logMode);

                VNAiResult res = null;
                yield return VNAiClient.Send(req, r => res = r);
                while (res == null) yield return null;

                SetThinking(false);
                if (_escConfirmed) break;

                // ② 解析：失败一律降级到兜底轮，绝不把报错甩给玩家
                VNAiTurn aiTurn = null;
                bool fatal = false;

                if (!res.ok)
                {
                    Debug.LogWarning($"[VNAiTalk] 第 {turn + 1} 轮请求失败" +
                                     $"（{res.failure}）：{res.errorMessage}");
                    // NoKey / Auth 是配置错误，重试多少次都一样，说完这句就收场
                    fatal = res.failure == VNAiFailure.NoKey ||
                            res.failure == VNAiFailure.Auth;
                }
                else if (_convo.TryParseTurn(res.text, out VNAiTurn parsed, out string parseError))
                {
                    aiTurn = parsed;
                    _convo.RecordReply(aiTurn);
                    _affectionTotal += aiTurn.affectionDelta;
                }
                else
                {
                    Debug.LogWarning($"[VNAiTalk] 第 {turn + 1} 轮解析失败：{parseError}\n" +
                                     $"原始输出：{res.text}");
                }

                bool degraded = aiTurn == null;
                if (degraded) aiTurn = _convo.BuildFallbackTurn();
                _log.BeginTurn(turn, playerSaid, aiTurn, res, degraded);

                // ③ 演出：换表情 + 打字机 + 漫符
                yield return SpeakCo(aiTurn);
                if (fatal) { FinishWith(OutcomeFailed); yield break; }
                if (_escConfirmed) break;

                // ④ 停一拍再上选项
                _phase = Phase.Pausing;
                if (optionDelay > 0f)
                {
                    float t = 0f;
                    while (t < optionDelay && !_escConfirmed)
                    {
                        t += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
                if (_escConfirmed) break;

                // 最后一轮 / AI 判定收尾 / 兜底轮之后仍失败：不再给选项
                bool lastTurn = turn == _maxTurns - 1 || (aiTurn.shouldEnd && !degraded);
                if (lastTurn) break;

                // ⑤ 玩家三选一
                _phase = Phase.Choosing;
                int picked = -1;
                ShowChoices(aiTurn, i => picked = i);
                while (picked < 0 && !_escConfirmed) yield return null;
                if (_escConfirmed) { _stage?.choicePanel?.ForceClose(); break; }

                int pickedIndex = Mathf.Clamp(picked, 0, aiTurn.options.Count - 1);
                var option = aiTurn.options[pickedIndex];
                _convo.RecordPick(option);
                _log.RecordPick(pickedIndex);
                playerSaid = option.text;
            }

            FinishWith(JudgeOutcome());
        }

        VNAiContext BuildContext(int turnIndex) => new VNAiContext
        {
            playerName = _ctx.Kw("me", "我"),
            topic = turnIndex == 0 ? _ctx.Kw("topic") : null,
            place = _ctx.Kw("place"),
            affectionText = BuildAffectionText(),
            memory = _ctx.Kw("memory"),
            turnsLeft = _maxTurns - turnIndex,
        };

        /// <summary>
        /// 把养成属性翻译成人话喂给 AI。直接丢「好感=42」这种数字，模型只会
        /// 复读数字；说成「算是走得比较近」它才会调整语气。
        /// </summary>
        string BuildAffectionText()
        {
            if (string.IsNullOrEmpty(_statName)) return null;
            int v = VNFlags.Get(_statName);
            string level =
                v >= 80 ? "已经是彼此心照不宣的关系" :
                v >= 60 ? "关系很好，会主动找对方说话" :
                v >= 40 ? "走得比较近的同学" :
                v >= 20 ? "说得上话，但还有点距离" :
                          "还不太熟";
            return $"{_statName} {v}，{level}";
        }

        IEnumerator SpeakCo(VNAiTurn turn)
        {
            _phase = Phase.Speaking;

            // 表情 + 台词 + 口型 + 头像 + 名牌配色，一个调用全带上
            if (_stage != null)
            {
                _stage.Say(_charId, turn.emotion, turn.reply);
                _expressionTouched = true;

                if (!string.IsNullOrEmpty(turn.mark))
                {
                    var active = _stage.Get(_charId);
                    if (active?.marks != null &&
                        VNCharacterMarks.TryParse(turn.mark, out VNMarkKind kind))
                        active.marks.Show(kind, false, null, 1f, 0f);
                }
            }

            // 等打字机（玩家点击会 CompleteTyping，见 Update）
            var box = _stage?.dialogue;
            while (box != null && box.IsTyping && !_escConfirmed) yield return null;
        }

        void ShowChoices(VNAiTurn turn, System.Action<int> onPicked)
        {
            var panel = _stage?.choicePanel;
            if (panel == null)
            {
                Debug.LogError("[VNAiTalk] VNStage 未连线 choicePanel，无法显示候选回复");
                onPicked(0);
                return;
            }

            var options = new VNChoicePanel.Option[turn.options.Count];
            for (int i = 0; i < turn.options.Count; i++)
                options[i] = new VNChoicePanel.Option { text = turn.options[i].text };

            panel.Show(options, onPicked);
        }

        // ──────────────────────────────────────────────────────────
        // 结算
        // ──────────────────────────────────────────────────────────

        string JudgeOutcome()
        {
            if (_affectionTotal > 0) return OutcomeUp;
            if (_affectionTotal < 0) return OutcomeCold;
            return OutcomeNormal;
        }

        void FinishWith(string outcome)
        {
            if (_phase == Phase.Ending) return;
            _phase = Phase.Ending;

            _stage?.choicePanel?.ForceClose();
            SetThinking(false);
            RestoreStage();

            // 属性联动：AI 的好感变化 × 换算率 加到养成属性上
            if (!string.IsNullOrEmpty(_statName) && _affectionTotal != 0 &&
                outcome != OutcomeFailed)
                VNFlags.Add(_statName, _affectionTotal * _statRate);

            // 成绩写 flag，供剧本 if 判断
            var flagLines = new List<string>();
            if (!string.IsNullOrEmpty(_flagPrefix))
            {
                SetFlag(flagLines, _flagPrefix + "轮数", _convo != null ? _convo.TurnCount : 0);
                SetFlag(flagLines, _flagPrefix + "好感变化", _affectionTotal);
                // 只写本场实际用到的语气档——关掉扩展开关时不该留下空的「倾向_毒舌=0」
                if (_convo != null)
                    foreach (string tone in _convo.Tones)
                        SetFlag(flagLines, _flagPrefix + "倾向_" + tone, CountTone(tone));
            }

            // 日志落盘。写盘失败只告警，绝不影响玩法（Save 内部已吞异常）
            _log.End(outcome, _affectionTotal,
                     _convo != null ? _convo.pickedTones : null, flagLines, _escConfirmed);
            string logPath = _log.Save();
            if (!string.IsNullOrEmpty(logPath))
                Debug.Log($"[VNAiTalk] 对话日志已保存：{logPath}");

            Done(outcome);
        }

        /// <summary>写 flag 的同时记一行给日志，省得两处各写一遍容易漏。</summary>
        static void SetFlag(List<string> lines, string key, int value)
        {
            VNFlags.Set(key, value);
            lines.Add($"{key} = {value}");
        }

        int CountTone(string tone)
        {
            if (_convo == null) return 0;
            int n = 0;
            foreach (string t in _convo.pickedTones)
                if (t == tone) n++;
            return n;
        }

        /// <summary>
        /// 还原舞台：把表情放回进入前的样子。正常结束、ESC 退出、调试中断
        /// 三条路径都会走到这里，重复调用无害。
        /// </summary>
        void RestoreStage()
        {
            if (!_expressionTouched || _stage == null) return;
            _expressionTouched = false;

            var active = _stage.Get(_charId);
            if (active == null) return;

            _stage.StopSpeaking();                       // 嘴巴闭上
            active.marks?.ClearAll();                    // 清掉本次的一次性漫符
            if (_originalExpression != null)
                _stage.SetExpression(_charId, _originalExpression);
        }

        // ──────────────────────────────────────────────────────────
        // 自绘 UI（全部 raycastTarget=false，别吃掉选项的点击）
        // ──────────────────────────────────────────────────────────

        void BuildUi()
        {
            _hintRoot = CreateRect("AiTalkHud", (RectTransform)transform);
            Stretch(_hintRoot);

            // 左上角：轮数 + ESC 提示
            _turnLabel = CreateText("TurnLabel", _hintRoot, 24, HintColor, "");
            var r = (RectTransform)_turnLabel.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(32f, -28f);
            r.sizeDelta = new Vector2(520f, 34f);
            _turnLabel.alignment = TextAlignmentOptions.Left;

            // 对话框上方：「正在输入…」
            _thinkingLabel = CreateText("Thinking", _hintRoot, 30, AccentColor, "");
            var tr = (RectTransform)_thinkingLabel.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.34f);
            tr.sizeDelta = new Vector2(600f, 44f);
            _thinkingLabel.gameObject.SetActive(false);
        }

        void RefreshTurnLabel(int turnIndex)
        {
            if (_turnLabel == null) return;
            _turnLabel.text = string.Format(VNLocale.T("aitalk.turn"),
                _persona.DisplayName, turnIndex + 1, _maxTurns);
        }

        void SetThinking(bool on)
        {
            if (_thinkingLabel == null) return;
            _thinkingTween?.Kill();
            _thinkingLabel.gameObject.SetActive(on);
            if (!on) return;

            _thinkingLabel.text = string.Format(VNLocale.T("aitalk.typing"),
                _persona.DisplayName);
            // 用 DOVirtual 而不是 text.DOFade：后者是 DOTween 的 TMP 扩展模块提供的，
            // 模块没启用时会编译不过。这里改成通用补间，零依赖。
            _thinkingTween = DOVirtual.Float(0.35f, 1f, 0.7f, v =>
                {
                    if (_thinkingLabel != null) _thinkingLabel.alpha = v;
                })
                .SetLoops(-1, LoopType.Yoyo).SetUpdate(true).SetLink(gameObject);
        }

        // ── ESC 确认框：唯一允许吃射线的东西 ──

        void ShowConfirm()
        {
            if (_confirmRoot != null) return;
            _phase = Phase.Confirming;

            var dim = CreateImage("ConfirmDim", (RectTransform)transform, null,
                new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim);
            dim.GetComponent<Image>().raycastTarget = true;   // 这一层就是要独占输入
            _confirmRoot = dim;

            var panel = CreateImage("Panel", dim, VNProceduralTextures.RoundedRectSprite,
                PanelColor);
            panel.GetComponent<Image>().type = Image.Type.Sliced;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(620f, 260f);

            var title = CreateText("Title", panel, 34, Color.white,
                VNLocale.T("aitalk.quitTitle"));
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.72f);
            titleRect.sizeDelta = new Vector2(560f, 50f);

            var tip = CreateText("Tip", panel, 22, HintColor,
                VNLocale.T("aitalk.quitTip"));
            var tipRect = (RectTransform)tip.transform;
            tipRect.anchorMin = tipRect.anchorMax = new Vector2(0.5f, 0.5f);
            tipRect.sizeDelta = new Vector2(560f, 40f);

            CreateButton(panel, VNLocale.T("aitalk.quitNo"), new Vector2(0.28f, 0.22f), () =>
            {
                CloseConfirm();
                _phase = _stage != null && _stage.choicePanel != null &&
                         _stage.choicePanel.IsShowing ? Phase.Choosing : Phase.Speaking;
            });
            CreateButton(panel, VNLocale.T("aitalk.quitYes"), new Vector2(0.72f, 0.22f), () =>
            {
                _escConfirmed = true;
                CloseConfirm();
            });

            panel.localScale = Vector3.one * 0.8f;
            panel.DOScale(1f, 0.22f).SetEase(Ease.OutBack).SetUpdate(true)
                 .SetLink(gameObject);
        }

        void CloseConfirm()
        {
            if (_confirmRoot == null) return;
            Destroy(_confirmRoot.gameObject);
            _confirmRoot = null;
        }

        void CreateButton(RectTransform parent, string label, Vector2 anchor,
            UnityEngine.Events.UnityAction onClick)
        {
            var rect = CreateImage("Btn_" + label, parent,
                VNProceduralTextures.RoundedRectSprite, new Color(1f, 1f, 1f, 0.12f));
            var img = rect.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            img.raycastTarget = true;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.sizeDelta = new Vector2(230f, 62f);

            var btn = rect.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var text = CreateText("Label", rect, 26, Color.white, label);
            Stretch((RectTransform)text.transform);
        }

        // ── 程序化 UI 辅助（抄 VNQteModule 的写法，保持一致）──

        static RectTransform CreateRect(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;   // ★ 默认不吃射线，需要的地方单独打开
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
            text.raycastTarget = false;    // ★ 同上
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // ──────────────────────────────────────────────────────────
        // 人格查找
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// 三级查找：模板上的列表 → VNGameConfig → 按角色 id 兜底匹配第一个。
        /// 剧本省略 persona: 时能自动找到该角色的人格，少写一个参数。
        /// </summary>
        VNAiPersonaDef ResolvePersona(string personaId, string charId)
        {
            if (!string.IsNullOrEmpty(personaId))
            {
                foreach (var p in personas)
                    if (p != null && p.id == personaId) return p;
                var fromConfig = VNGameConfig.Active?.FindAiPersona(personaId);
                if (fromConfig != null) return fromConfig;
                Debug.LogWarning($"[VNAiTalk] 人格库里没有「{personaId}」，改按角色 id 找");
            }

            if (!string.IsNullOrEmpty(charId))
            {
                foreach (var p in personas)
                    if (p != null && p.character != null && p.character.id == charId) return p;
                var list = VNGameConfig.Active?.aiPersonas;
                if (list != null)
                    foreach (var p in list)
                        if (p != null && p.character != null && p.character.id == charId) return p;
            }

            return null;
        }
    }
}
