using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：结果结算大弹窗（火山的女儿式 GOOD! / COMPLETE! 演出）。
    /// 四档大字弹出 + 星光爆发（good/great），点击/回车/空格继续。
    /// 属性增减仍由剧本的 stat 命令负责（VNStatsHud 自带飘字），弹窗只管演出。
    ///
    /// 剧本用法（通常配 flag rand: 掷骰分级后调用，见 WeekPlanDemo.vn.txt）：
    ///   event result grade:great title:剑术训练 [sub:一句说明] [se:音效id]
    ///   grade：fail（失败）/ normal（普通）/ good（成功）/ great（大成功）
    /// 结果名 = grade 值，可用「* great -> 标签」接住，不写结果行则顺序继续。
    ///
    /// 遵守事件模块三铁律：不碰舞台演出 / unscaled 计时 + SetUpdate(true) /
    /// 全部 Tween SetLink。
    /// </summary>
    public class VNResultPopupModule : VNEventModule
    {
        [Header("弹出后多少秒才接受点击（防连点误触跳过）")]
        public float inputDelay = 0.4f;

        class GradeStyle
        {
            public string bigLabel;      // 大字（风格化英文，不随语言切换）
            public string localeKey;     // 小字（本地化：失败……/大成功！！）
            public Color color;
            public Color panelTint;
            public bool burst;           // 是否播星光爆发
        }

        static GradeStyle StyleOf(string grade)
        {
            switch (grade)
            {
                case "fail": return new GradeStyle
                {
                    bigLabel = "FAIL…", localeKey = "result.fail",
                    color = new Color(0.62f, 0.65f, 0.75f, 1f),
                    panelTint = new Color(0.09f, 0.09f, 0.13f, 0.97f), burst = false,
                };
                case "good": return new GradeStyle
                {
                    bigLabel = "GOOD!", localeKey = "result.good",
                    color = new Color(1f, 0.55f, 0.72f, 1f),
                    panelTint = new Color(0.13f, 0.08f, 0.11f, 0.97f), burst = true,
                };
                case "great": return new GradeStyle
                {
                    bigLabel = "GREAT!!", localeKey = "result.great",
                    color = new Color(1f, 0.84f, 0.32f, 1f),
                    panelTint = new Color(0.13f, 0.11f, 0.06f, 0.97f), burst = true,
                };
                default: return new GradeStyle
                {
                    bigLabel = "OK", localeKey = "result.normal",
                    color = new Color(0.75f, 0.88f, 1f, 1f),
                    panelTint = new Color(0.06f, 0.08f, 0.13f, 0.97f), burst = false,
                };
            }
        }

        string _grade;
        float _shownAt;
        bool _closing;
        RectTransform _panel;

        protected override void OnLaunch(VNEventContext ctx)
        {
            _grade = ctx.Kw("grade", "normal");
            if (_grade != "fail" && _grade != "normal" && _grade != "good" && _grade != "great")
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：result 未知等级「{_grade}」" +
                                 "（fail/normal/good/great），按 normal 处理");
                _grade = "normal";
            }

            string se = ctx.Kw("se");
            if (!string.IsNullOrEmpty(se))
                ctx.stage?.vnAudio?.PlaySe(se, false, 1f, ctx.line);

            BuildUi(StyleOf(_grade), ctx.Kw("title"), ctx.Kw("sub"));
            _shownAt = Time.unscaledTime;
        }

        void Update()
        {
            if (_closing || Time.unscaledTime - _shownAt < inputDelay) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool pressed =
                (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                (kb != null && (kb.enterKey.wasPressedThisFrame ||
                                kb.spaceKey.wasPressedThisFrame));
            if (!pressed) return;

            _closing = true;
            if (_panel != null)
            {
                _panel.DOScale(0.9f, 0.16f).SetEase(Ease.InQuad)
                      .SetUpdate(true).SetLink(gameObject);
                DOVirtual.DelayedCall(0.16f, () => Done(_grade), true).SetLink(gameObject);
            }
            else Done(_grade);
        }

        // ------------------------------------------------------------------
        // 程序化 UI
        // ------------------------------------------------------------------

        void BuildUi(GradeStyle style, string title, string sub)
        {
            var root = (RectTransform)transform;

            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim);

            _panel = CreateImage("Panel", root, VNProceduralTextures.RoundedRectSprite,
                style.panelTint);
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.52f);
            _panel.sizeDelta = new Vector2(720f, 380f);
            _panel.localScale = Vector3.one * 0.7f;
            _panel.DOScale(1f, 0.32f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);

            // 大字等级：超大缩放砸落
            var big = CreateText("Grade", _panel, 96, style.color, style.bigLabel);
            big.fontStyle = FontStyles.Bold | FontStyles.Italic;
            var bigRect = (RectTransform)big.transform;
            bigRect.anchorMin = bigRect.anchorMax = new Vector2(0.5f, 0.68f);
            bigRect.sizeDelta = new Vector2(680f, 130f);
            bigRect.localScale = Vector3.one * 2.6f;
            bigRect.DOScale(1f, 0.38f).SetEase(Ease.InCubic)
                   .SetUpdate(true).SetLink(gameObject)
                   .OnComplete(() =>
                   {
                       bigRect.DOPunchScale(Vector3.one * 0.12f, 0.25f, 6, 0.7f)
                              .SetUpdate(true).SetLink(gameObject);
                       if (style.burst) PlayStarBurst(style.color);
                   });

            // 本地化等级小字
            var small = CreateText("GradeLocal", _panel, 30, style.color,
                VNLocale.T(style.localeKey));
            var smallRect = (RectTransform)small.transform;
            smallRect.anchorMin = smallRect.anchorMax = new Vector2(0.5f, 0.44f);
            smallRect.sizeDelta = new Vector2(600f, 44f);

            // 行动名（title:）与补充说明（sub:）
            if (!string.IsNullOrEmpty(title))
            {
                var titleText = CreateText("Title", _panel, 34,
                    new Color(0.96f, 0.97f, 1f, 1f), title);
                titleText.fontStyle = FontStyles.Bold;
                var titleRect = (RectTransform)titleText.transform;
                titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.28f);
                titleRect.sizeDelta = new Vector2(660f, 48f);
            }
            if (!string.IsNullOrEmpty(sub))
            {
                var subText = CreateText("Sub", _panel, 24,
                    new Color(1f, 1f, 1f, 0.65f), sub);
                var subRect = (RectTransform)subText.transform;
                subRect.anchorMin = subRect.anchorMax = new Vector2(0.5f, 0.17f);
                subRect.sizeDelta = new Vector2(660f, 36f);
            }

            // 继续提示（呼吸闪烁）
            var hint = CreateText("Hint", _panel, 22, new Color(1f, 1f, 1f, 0.55f),
                VNLocale.T("result.continue"));
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.06f);
            hintRect.sizeDelta = new Vector2(400f, 32f);
            hint.DOFade(0.2f, 0.7f).SetLoops(-1, LoopType.Yoyo)
                .SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>good/great 时从面板中心向外撒一圈星光（程序化四芒星贴图）</summary>
        void PlayStarBurst(Color color)
        {
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                var star = CreateImage("Star", _panel, VNProceduralTextures.SparkleSprite, color);
                star.anchorMin = star.anchorMax = new Vector2(0.5f, 0.68f);
                star.sizeDelta = Vector2.one * Random.Range(26f, 48f);
                star.localScale = Vector3.zero;
                star.GetComponent<Image>().raycastTarget = false;

                float angle = (360f / count) * i + Random.Range(-14f, 14f);
                Vector2 dir = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 target = dir * Random.Range(220f, 340f);
                float life = Random.Range(0.5f, 0.8f);

                star.DOScale(1f, 0.16f).SetEase(Ease.OutBack)
                    .SetUpdate(true).SetLink(gameObject);
                star.DOAnchorPos(target, life).SetEase(Ease.OutCubic)
                    .SetUpdate(true).SetLink(gameObject);
                star.GetComponent<Image>().DOFade(0f, life * 0.6f).SetDelay(life * 0.4f)
                    .SetUpdate(true).SetLink(gameObject);
            }
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

        static TextMeshProUGUI CreateText(string name, RectTransform parent, int size,
            Color color, string content)
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
