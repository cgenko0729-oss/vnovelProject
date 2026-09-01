using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：限时问答。一题一屏、限时选择，可连答 N 题，最后按正确率分档返回。
    /// 题目来自 VNQuizDef 题库资产（模板 Inspector / VNGameConfig 登记）。
    ///
    /// 剧本用法：
    ///   event quiz id:社团常识 count:3 time:15 pass:2
    ///   * 全对 -> 完美结局
    ///   * 及格 -> 勉强过关
    ///   * 失败 -> 糟了
    ///
    /// kwargs：
    ///   id:      题库 id（只登记一套时可省略）
    ///   count:   出几题（默认全部；随机抽取）
    ///   pick:    指定题号（1 开始，逗号分隔，如 pick:1,4,7；写了则忽略 count 与随机）
    ///   time:    每题限时秒（默认题库 defaultTimeLimit；单题 timeLimit 优先级更高）
    ///   pass:    及格线 = 至少答对几题（默认为题数的一半，向上取整）
    ///   title:   面板标题覆盖
    ///   flag:    成绩 flag 前缀覆盖（默认取题库 flagPrefix）
    ///
    /// 结果："全对" / "及格" / "失败"；同时写 flag「&lt;前缀&gt;正确数」「&lt;前缀&gt;总数」，
    /// 剧本可再用 if 细分。超时按答错处理。
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) / 全部 Tween SetLink。
    /// </summary>
    public class VNQuizModule : VNEventModule
    {
        [Header("本模板登记的题库资产（event quiz id:xx 按 quizId 查找）")]
        public List<VNQuizDef> quizzes = new List<VNQuizDef>();

        /// <summary>成绩 flag 后缀（前缀由题库 flagPrefix / 剧本 flag: 决定）</summary>
        public const string CorrectFlagSuffix = "正确数";
        public const string TotalFlagSuffix = "总数";

        /// <summary>剩余时间进入这个秒数后开始"最后冲刺"演出（变红/脉动/轻抖）</summary>
        const float UrgentSeconds = 3f;

        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.14f, 0.95f);
        static readonly Color AccentColor = new Color(0.45f, 0.8f, 1f, 1f);
        static readonly Color OptionColor = new Color(0.13f, 0.16f, 0.26f, 0.96f);
        static readonly Color CorrectColor = new Color(0.3f, 0.85f, 0.5f, 1f);
        static readonly Color WrongColor = new Color(0.9f, 0.35f, 0.4f, 1f);
        static readonly Color UrgentColor = new Color(1f, 0.4f, 0.35f, 1f);

        enum Phase { Idle, Asking, Feedback, Ending }

        Phase _phase = Phase.Idle;
        VNQuizDef _quiz;
        List<VNQuizDef.Question> _picked = new List<VNQuizDef.Question>();
        int _index;              // 当前第几题（0 开始）
        int _correct;            // 已答对数
        int _passLine;           // 及格线
        float _defaultTime;      // 剧本 time: 或题库默认
        string _flagPrefix;
        float _timeLeft;
        float _questionTime;     // 本题总时长（画条用）

        VNStatsHud _statsHud;
        VNAudio _audio;

        RectTransform _panel;
        Vector2 _panelHome;
        TextMeshProUGUI _titleText, _progressText, _timerText, _questionText, _feedbackText;
        RectTransform _timerFill;
        Image _timerFillImage;
        readonly List<Image> _optionImages = new List<Image>();
        readonly List<Button> _optionButtons = new List<Button>();
        readonly List<TextMeshProUGUI> _optionTexts = new List<TextMeshProUGUI>();
        readonly List<RectTransform> _optionRects = new List<RectTransform>();

        // ------------------------------------------------------------------
        // 启动
        // ------------------------------------------------------------------

        protected override void OnLaunch(VNEventContext ctx)
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.quizzes, ref quizzes);

            string id = ctx.Kw("id");
            _quiz = null;
            foreach (var q in quizzes)
                if (q != null && q.quizId == id) { _quiz = q; break; }
            if (_quiz == null && quizzes.Count == 1 && string.IsNullOrEmpty(id))
                _quiz = quizzes[0]; // 只登记了一套时 id 可省略

            if (_quiz == null)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：问答模板没有登记 id「{id}」" +
                                 "的 VNQuizDef，直接返回");
                Done("");
                return;
            }

            var pool = _quiz.ValidQuestions();
            if (pool.Count == 0)
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：题库「{_quiz.quizId}」" +
                                 "没有可用的题目（题干为空 / 选项少于 2 个 / 答案序号越界），直接返回");
                Done("");
                return;
            }

            _picked = SelectQuestions(pool, ctx);
            _defaultTime = Mathf.Max(1f, ctx.KwF("time", _quiz.defaultTimeLimit));
            _passLine = Mathf.Clamp(
                ctx.KwI("pass", Mathf.CeilToInt(_picked.Count / 2f)), 0, _picked.Count);
            _flagPrefix = ctx.Kw("flag", _quiz.flagPrefix);
            if (string.IsNullOrEmpty(_flagPrefix)) _flagPrefix = "答题";

            _statsHud = FindFirstObjectByType<VNStatsHud>();
            _audio = ctx.stage != null ? ctx.stage.vnAudio : null;

            BuildUi(ctx.Kw("title", _quiz.DisplayTitle));
            ShowQuestion();
        }

        /// <summary>选题：pick: 指定题号优先，否则从题库随机抽 count 题（不重复）</summary>
        List<VNQuizDef.Question> SelectQuestions(List<VNQuizDef.Question> pool, VNEventContext ctx)
        {
            var result = new List<VNQuizDef.Question>();

            string pick = ctx.Kw("pick");
            if (!string.IsNullOrEmpty(pick))
            {
                // pick 的题号按**资产里的原始顺序**数（1 开始），坏题也占号，
                // 这样 Inspector 里第几条就是第几号，不会因为某题填一半而错位。
                var all = _quiz.questions;
                foreach (var token in pick.Split(','))
                {
                    if (!int.TryParse(token.Trim(), out int n)) continue;
                    int at = n - 1;
                    if (at < 0 || all == null || at >= all.Count || all[at] == null ||
                        !all[at].IsValid)
                    {
                        Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：pick 的题号「{token.Trim()}」" +
                                         $"在题库「{_quiz.quizId}」里不存在或题目没填全，已跳过");
                        continue;
                    }
                    result.Add(all[at]);
                }
                if (result.Count > 0) return result;
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：pick 一道有效题都没选到，改为随机抽题");
            }

            int count = Mathf.Clamp(ctx.KwI("count", pool.Count), 1, pool.Count);
            var bag = new List<VNQuizDef.Question>(pool);
            for (int i = 0; i < count; i++)
            {
                int at = Random.Range(0, bag.Count);
                result.Add(bag[at]);
                bag.RemoveAt(at);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 出题 / 计时 / 判定
        // ------------------------------------------------------------------

        void ShowQuestion()
        {
            var q = _picked[_index];
            _questionTime = q.timeLimit > 0f ? q.timeLimit : _defaultTime;
            _timeLeft = _questionTime;

            _progressText.text = VNLocale.T("quiz.progress", _index + 1, _picked.Count);
            _questionText.text = q.Display;
            _feedbackText.text = "";

            int shown = Mathf.Min(q.options.Count, VNQuizDef.MaxOptions);
            for (int i = 0; i < _optionImages.Count; i++)
            {
                bool active = i < shown;
                _optionRects[i].gameObject.SetActive(active);
                if (!active) continue;
                _optionImages[i].color = OptionColor;
                _optionButtons[i].interactable = true;
                _optionTexts[i].text = $"{i + 1}.  {q.options[i].Display}";
                _optionTexts[i].color = new Color(0.95f, 0.96f, 1f, 1f);
                _optionRects[i].localScale = Vector3.one;
            }

            _phase = Phase.Asking;
            RefreshTimer();

            // 题干轻微弹入，给"换了一题"的节奏感
            _questionText.transform.localScale = Vector3.one * 0.94f;
            _questionText.transform.DOScale(1f, 0.22f).SetEase(Ease.OutBack)
                         .SetUpdate(true).SetLink(gameObject);
        }

        void Update()
        {
            if (VNPause.IsPaused) return;        // 教程讲解中：倒计时与数字键一起冻住
            if (_phase != Phase.Asking) return;

            _timeLeft -= VNTime.Delta;           // 不受快进 timeScale 影响、受 VNPause 冻结
            RefreshTimer();

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) TryAnswer(0);
                else if (kb.digit2Key.wasPressedThisFrame) TryAnswer(1);
                else if (kb.digit3Key.wasPressedThisFrame) TryAnswer(2);
                else if (kb.digit4Key.wasPressedThisFrame) TryAnswer(3);
            }

            if (_phase == Phase.Asking && _timeLeft <= 0f) Answer(-1);
        }

        /// <summary>数字键入口：本题没有这个选项就当没按</summary>
        void TryAnswer(int index)
        {
            if (index < 0 || index >= _optionRects.Count) return;
            if (!_optionRects[index].gameObject.activeSelf) return;
            Answer(index);
        }

        void RefreshTimer()
        {
            float left = Mathf.Max(0f, _timeLeft);
            _timerText.text = VNLocale.T("quiz.timer", left.ToString("0.0"));

            float t = _questionTime > 0f ? Mathf.Clamp01(left / _questionTime) : 0f;
            _timerFill.anchorMax = new Vector2(t, 1f);

            bool urgent = left <= UrgentSeconds && _phase == Phase.Asking;
            if (urgent)
            {
                // 最后冲刺：条与数字变红 + 数字脉动 + 面板轻抖（全部用 unscaled 时间算，
                // 不开 Tween，避免每帧堆积补间）
                float pulse = 1f + 0.12f * Mathf.Abs(Mathf.Sin(VNTime.Time * 8f));
                _timerText.transform.localScale = Vector3.one * pulse;
                _timerText.color = UrgentColor;
                _timerFillImage.color = UrgentColor;
                float shake = Mathf.Sin(VNTime.Time * 34f) * 2.5f;
                _panel.anchoredPosition = _panelHome + new Vector2(shake, 0f);
            }
            else
            {
                _timerText.transform.localScale = Vector3.one;
                _timerText.color = new Color(1f, 1f, 1f, 0.8f);
                _timerFillImage.color = AccentColor;
                _panel.anchoredPosition = _panelHome;
            }
        }

        /// <summary>index &lt; 0 = 超时（按答错处理）</summary>
        void Answer(int index)
        {
            if (_phase != Phase.Asking) return;
            _phase = Phase.Feedback;

            var q = _picked[_index];
            bool timeout = index < 0;
            bool correct = !timeout && index == q.answerIndex;
            if (correct) _correct++;

            _panel.anchoredPosition = _panelHome;
            _timerText.transform.localScale = Vector3.one;

            foreach (var b in _optionButtons) b.interactable = false;

            // 正确项永远高亮绿；答错时把玩家选的那项标红
            int answerAt = q.answerIndex;
            if (answerAt < _optionImages.Count && _optionRects[answerAt].gameObject.activeSelf)
                _optionImages[answerAt].color = CorrectColor;
            if (!correct && !timeout && index < _optionImages.Count)
                _optionImages[index].color = WrongColor;

            if (!timeout && index < _optionRects.Count)
            {
                _optionRects[index].DOKill(true);
                _optionRects[index].DOPunchScale(Vector3.one * 0.05f, 0.22f, 8, 0.6f)
                                   .SetUpdate(true).SetLink(gameObject);
            }
            if (_audio != null) _audio.PlaySe("se1");

            ApplyStatOps(correct ? q.rewardOnCorrect : q.penaltyOnWrong);

            string head = VNLocale.T(timeout ? "quiz.timeout"
                : correct ? "quiz.correct" : "quiz.wrong");
            string explain = q.DisplayExplain;
            _feedbackText.text = string.IsNullOrEmpty(explain) ? head : $"{head}\n{explain}";
            _feedbackText.color = correct ? CorrectColor : WrongColor;
            _feedbackText.transform.localScale = Vector3.one * 0.9f;
            _feedbackText.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack)
                         .SetUpdate(true).SetLink(gameObject);

            float wait = string.IsNullOrEmpty(explain) ? 1.1f : 1.9f;
            DOVirtual.DelayedCall(wait, NextQuestion, true).SetLink(gameObject);
        }

        void NextQuestion()
        {
            _index++;
            if (_index < _picked.Count) ShowQuestion();
            else Finish();
        }

        /// <summary>属性奖励/惩罚：有 HUD 就走它（钳制 + 飘字），否则退回裸 flag 加减</summary>
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
        // 结算
        // ------------------------------------------------------------------

        void Finish()
        {
            _phase = Phase.Ending;

            VNFlags.Set(_flagPrefix + CorrectFlagSuffix, _correct);
            VNFlags.Set(_flagPrefix + TotalFlagSuffix, _picked.Count);

            string outcome = _correct >= _picked.Count ? "全对"
                : _correct >= _passLine ? "及格" : "失败";
            bool good = outcome != "失败";

            foreach (var rect in _optionRects) rect.gameObject.SetActive(false);
            _timerText.text = "";
            _timerFill.anchorMax = new Vector2(0f, 1f);

            _questionText.text = VNLocale.T("quiz.score", _correct, _picked.Count);
            _questionText.transform.DOKill();
            _questionText.transform.localScale = Vector3.one * 1.3f;
            _questionText.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack)
                         .SetUpdate(true).SetLink(gameObject);

            _feedbackText.text = VNLocale.T("quiz.grade." + (
                outcome == "全对" ? "perfect" : outcome == "及格" ? "pass" : "fail"));
            _feedbackText.color = good ? CorrectColor : WrongColor;
            _feedbackText.transform.DOKill();
            _feedbackText.transform.localScale = Vector3.one * 0.85f;
            _feedbackText.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack)
                         .SetUpdate(true).SetLink(gameObject);

            DOVirtual.DelayedCall(1.4f, () => Done(outcome), true).SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 程序化 UI
        // ------------------------------------------------------------------

        void BuildUi(string titleText)
        {
            var dim = CreateImage("Dim", (RectTransform)transform, null,
                new Color(0f, 0f, 0f, 0.62f));
            Stretch(dim);

            _panel = CreateImage("Panel", (RectTransform)transform,
                VNProceduralTextures.RoundedRectSprite, PanelColor);
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(1120f, 660f);
            _panelHome = _panel.anchoredPosition;

            _titleText = CreateText("Title", _panel, 40, AccentColor, titleText);
            var titleRect = (RectTransform)_titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0.7f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(40f, -26f);
            titleRect.sizeDelta = new Vector2(0f, 56f);
            _titleText.alignment = TextAlignmentOptions.Left;

            _progressText = CreateText("Progress", _panel, 30,
                new Color(1f, 1f, 1f, 0.75f), "");
            var progressRect = (RectTransform)_progressText.transform;
            progressRect.anchorMin = new Vector2(1f, 1f);
            progressRect.anchorMax = new Vector2(1f, 1f);
            progressRect.pivot = new Vector2(1f, 1f);
            progressRect.anchoredPosition = new Vector2(-40f, -30f);
            progressRect.sizeDelta = new Vector2(240f, 46f);
            _progressText.alignment = TextAlignmentOptions.Right;

            // 倒计时条：背景 + 按 anchorMax.x 收缩的填充
            var barBg = CreateImage("TimerBg", _panel, VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.1f));
            barBg.GetComponent<Image>().type = Image.Type.Sliced;
            barBg.anchorMin = new Vector2(0f, 1f);
            barBg.anchorMax = new Vector2(1f, 1f);
            barBg.pivot = new Vector2(0.5f, 1f);
            barBg.offsetMin = new Vector2(40f, 0f);
            barBg.offsetMax = new Vector2(-40f, 0f);
            barBg.anchoredPosition = new Vector2(0f, -96f);
            barBg.sizeDelta = new Vector2(barBg.sizeDelta.x, 18f);

            _timerFill = CreateImage("TimerFill", barBg, VNProceduralTextures.RoundedRectSprite,
                AccentColor);
            _timerFillImage = _timerFill.GetComponent<Image>();
            _timerFillImage.type = Image.Type.Sliced;
            _timerFill.anchorMin = Vector2.zero;
            _timerFill.anchorMax = new Vector2(1f, 1f);
            _timerFill.offsetMin = Vector2.zero;
            _timerFill.offsetMax = Vector2.zero;

            _timerText = CreateText("Timer", _panel, 28, new Color(1f, 1f, 1f, 0.8f), "");
            var timerRect = (RectTransform)_timerText.transform;
            timerRect.anchorMin = new Vector2(1f, 1f);
            timerRect.anchorMax = new Vector2(1f, 1f);
            timerRect.pivot = new Vector2(1f, 1f);
            timerRect.anchoredPosition = new Vector2(-40f, -108f);
            timerRect.sizeDelta = new Vector2(160f, 44f);
            _timerText.alignment = TextAlignmentOptions.Right;

            _questionText = CreateText("Question", _panel, 38, Color.white, "");
            _questionText.textWrappingMode = TextWrappingModes.Normal;
            var questionRect = (RectTransform)_questionText.transform;
            questionRect.anchorMin = new Vector2(0f, 1f);
            questionRect.anchorMax = new Vector2(1f, 1f);
            questionRect.pivot = new Vector2(0.5f, 1f);
            questionRect.offsetMin = new Vector2(60f, 0f);
            questionRect.offsetMax = new Vector2(-60f, 0f);
            questionRect.anchoredPosition = new Vector2(0f, -140f);
            questionRect.sizeDelta = new Vector2(questionRect.sizeDelta.x, 120f);

            // 选项按钮：竖排 4 个，超出本题选项数的隐藏
            for (int i = 0; i < VNQuizDef.MaxOptions; i++)
            {
                int captured = i;
                var go = CreateButton(_panel, () => TryAnswer(captured));
                var rect = (RectTransform)go.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(940f, 76f);
                rect.anchoredPosition = new Vector2(0f, -288f - i * 86f);

                _optionRects.Add(rect);
                _optionImages.Add(go.GetComponent<Image>());
                _optionButtons.Add(go.GetComponent<Button>());
                _optionTexts.Add(go.GetComponentInChildren<TextMeshProUGUI>());
            }

            _feedbackText = CreateText("Feedback", _panel, 30, CorrectColor, "");
            _feedbackText.textWrappingMode = TextWrappingModes.Normal;
            var feedbackRect = (RectTransform)_feedbackText.transform;
            feedbackRect.anchorMin = new Vector2(0f, 0f);
            feedbackRect.anchorMax = new Vector2(1f, 0f);
            feedbackRect.pivot = new Vector2(0.5f, 0f);
            feedbackRect.offsetMin = new Vector2(60f, 0f);
            feedbackRect.offsetMax = new Vector2(-60f, 0f);
            feedbackRect.anchoredPosition = new Vector2(0f, 24f);
            feedbackRect.sizeDelta = new Vector2(feedbackRect.sizeDelta.x, 92f);

            _panel.localScale = Vector3.one * 0.85f;
            _panel.DOScale(1f, 0.28f).SetEase(Ease.OutBack).SetUpdate(true)
                  .SetLink(gameObject);
        }

        GameObject CreateButton(RectTransform parent, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Option", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = OptionColor;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var text = CreateText("Label", rect, 30, new Color(0.95f, 0.96f, 1f, 1f), "");
            text.alignment = TextAlignmentOptions.Left;
            var textRect = (RectTransform)text.transform;
            Stretch(textRect);
            textRect.offsetMin = new Vector2(28f, 0f);
            textRect.offsetMax = new Vector2(-28f, 0f);
            return go;
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
