using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 事件模块：结果结算大弹窗（火山的女儿式 GOOD! / COMPLETE! 演出）。
    /// 面板弹入 → 判定冲条（填充 0→100 + 数字跳动，纯演出不代表数值）→
    /// 冲满闪白淡出 → 四档大字砸落 + 星光爆发（good/great），点击/回车/空格继续。
    /// 属性增减仍由剧本的 stat 命令负责（VNStatsHud 自带飘字），弹窗只管演出。
    ///
    /// 外观走系统主题 VNSystemUiSkinSet.resultPopupPrefab（VNResultPopupSkin 槽位），
    /// 缺失或校验失败时退回程序化默认 UI；皮肤没配冲条三槽时直接揭晓大字。
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
        [Header("大字揭晓后多少秒才接受点击（防连点误触跳过）")]
        public float inputDelay = 0.4f;

        [Header("判定冲条时长（秒；大字揭晓前的悬念演出）")]
        public float suspenseDuration = 0.9f;

        static readonly Color BarAccent = new Color(1f, 0.84f, 0.5f, 1f);

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
        VNResultPopupSkin _skin;   // 皮肤实例；null = 程序化默认 UI

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

            // 揭晓前不接受点击（Reveal 时改为当前时间，之后再等 inputDelay）
            _shownAt = float.MaxValue;

            var style = StyleOf(_grade);
            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.resultPopupPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNResultPopupSkin>(
                skinPrefab, transform, "VNResultPopup");
            if (_skin != null) BuildFromSkin(style, ctx.Kw("title"), ctx.Kw("sub"));
            else BuildDefault(style, ctx.Kw("title"), ctx.Kw("sub"));
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
        // 皮肤路径
        // ------------------------------------------------------------------

        void BuildFromSkin(GradeStyle style, string title, string sub)
        {
            _panel = _skin.panelRoot;
            if (_skin.panelBackground != null) _skin.panelBackground.color = style.panelTint;

            _skin.gradeText.text = style.bigLabel;
            _skin.gradeText.color = style.color;
            _skin.gradeText.gameObject.SetActive(false);
            if (_skin.gradeLocalText != null)
            {
                _skin.gradeLocalText.text = VNLocale.T(style.localeKey);
                _skin.gradeLocalText.color = style.color;
                _skin.gradeLocalText.gameObject.SetActive(false);
            }
            if (_skin.titleText != null)
            {
                _skin.titleText.text = title ?? "";
                _skin.titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
            }
            if (_skin.subText != null)
            {
                _skin.subText.text = sub ?? "";
                _skin.subText.gameObject.SetActive(!string.IsNullOrEmpty(sub));
            }
            if (_skin.hintText != null)
            {
                _skin.hintText.text = VNLocale.T("result.continue");
                _skin.hintText.gameObject.SetActive(false);
            }

            PopInPanel();

            bool hasBar = _skin.barRoot != null && _skin.barFill != null &&
                          _skin.percentText != null;
            if (hasBar)
                PlaySuspense(_skin.barRoot, _skin.barFill, _skin.percentText,
                    () => Reveal(style, _skin.gradeText, _skin.gradeLocalText,
                                 _skin.hintText, _skin.burstOrigin));
            else
                Reveal(style, _skin.gradeText, _skin.gradeLocalText,
                       _skin.hintText, _skin.burstOrigin);
        }

        // ------------------------------------------------------------------
        // 程序化默认 UI
        // ------------------------------------------------------------------

        void BuildDefault(GradeStyle style, string title, string sub)
        {
            var root = (RectTransform)transform;

            var dim = CreateImage("Dim", root, null, new Color(0f, 0f, 0f, 0.6f));
            Stretch(dim);

            _panel = CreateImage("Panel", root, VNProceduralTextures.RoundedRectSprite,
                style.panelTint);
            _panel.GetComponent<Image>().type = Image.Type.Sliced;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.52f);
            _panel.sizeDelta = new Vector2(720f, 380f);
            PopInPanel();

            // 大字等级：先隐藏，冲条结束后揭晓
            var big = CreateText("Grade", _panel, 96, style.color, style.bigLabel);
            big.fontStyle = FontStyles.Bold | FontStyles.Italic;
            var bigRect = (RectTransform)big.transform;
            bigRect.anchorMin = bigRect.anchorMax = new Vector2(0.5f, 0.68f);
            bigRect.sizeDelta = new Vector2(680f, 130f);
            big.gameObject.SetActive(false);

            // 本地化等级小字（随大字一起揭晓）
            var small = CreateText("GradeLocal", _panel, 30, style.color,
                VNLocale.T(style.localeKey));
            var smallRect = (RectTransform)small.transform;
            smallRect.anchorMin = smallRect.anchorMax = new Vector2(0.5f, 0.44f);
            smallRect.sizeDelta = new Vector2(600f, 44f);
            small.gameObject.SetActive(false);

            // 行动名（title:）与补充说明（sub:）——冲条阶段就显示
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

            // 继续提示（揭晓后才显示并开始呼吸闪烁）
            var hint = CreateText("Hint", _panel, 22, new Color(1f, 1f, 1f, 0.55f),
                VNLocale.T("result.continue"));
            var hintRect = (RectTransform)hint.transform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0.06f);
            hintRect.sizeDelta = new Vector2(400f, 32f);
            hint.gameObject.SetActive(false);

            // 判定冲条：跳动数字 + 填充条（占大字位置，揭晓前淡出）
            var percent = CreateText("Percent", _panel, 64, BarAccent, "0");
            percent.fontStyle = FontStyles.Bold;
            var percentRect = (RectTransform)percent.transform;
            percentRect.anchorMin = percentRect.anchorMax = new Vector2(0.5f, 0.66f);
            percentRect.sizeDelta = new Vector2(400f, 80f);

            var barBg = CreateImage("BarBg", _panel, VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.12f));
            barBg.GetComponent<Image>().type = Image.Type.Sliced;
            barBg.anchorMin = barBg.anchorMax = new Vector2(0.5f, 0.48f);
            barBg.sizeDelta = new Vector2(440f, 20f);

            var fill = CreateImage("BarFill", barBg, VNProceduralTextures.RoundedRectSprite,
                BarAccent);
            fill.GetComponent<Image>().type = Image.Type.Sliced;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            PlaySuspense(barBg.gameObject, fill, percent,
                () => Reveal(style, big, small, hint, bigRect));
        }

        // ------------------------------------------------------------------
        // 共用演出：面板弹入 / 冲条 / 揭晓
        // ------------------------------------------------------------------

        void PopInPanel()
        {
            _panel.localScale = Vector3.one * 0.7f;
            _panel.DOScale(1f, 0.32f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);
        }

        /// <summary>
        /// 判定冲条：填充 0→100 加速冲刺 + 数字跳动 → 冲满数字punch、
        /// 填充闪白 → 整组淡出后回调揭晓。纯演出，不代表任何数值。
        /// </summary>
        void PlaySuspense(GameObject barRoot, RectTransform barFill, TMP_Text percentText,
            System.Action reveal)
        {
            // 填充条按 anchorMax.x 驱动，保留皮肤作者设置的纵向锚点
            float minY = barFill.anchorMin.y;
            float maxY = barFill.anchorMax.y;
            barFill.anchorMin = new Vector2(0f, minY);
            barFill.anchorMax = new Vector2(0f, maxY);
            percentText.text = "0";

            DOVirtual.Float(0f, 100f, suspenseDuration, v =>
                {
                    barFill.anchorMax = new Vector2(v / 100f, maxY);
                    percentText.text = Mathf.RoundToInt(v).ToString();
                })
                .SetEase(Ease.InQuad)
                .SetUpdate(true).SetLink(gameObject)
                .OnComplete(() =>
                {
                    percentText.rectTransform.DOPunchScale(Vector3.one * 0.25f, 0.2f, 8, 0.8f)
                        .SetUpdate(true).SetLink(gameObject);
                    var fillImage = barFill.GetComponent<Image>();
                    if (fillImage != null)
                        fillImage.DOColor(Color.white, 0.08f).SetLoops(2, LoopType.Yoyo)
                            .SetUpdate(true).SetLink(gameObject);

                    DOVirtual.DelayedCall(0.22f, () =>
                    {
                        foreach (var graphic in barRoot.GetComponentsInChildren<Graphic>())
                            graphic.DOFade(0f, 0.18f).SetUpdate(true).SetLink(gameObject);
                        percentText.DOFade(0f, 0.18f).SetUpdate(true).SetLink(gameObject);
                        reveal();
                    }, true).SetLink(gameObject);
                });
        }

        /// <summary>大字砸落 + punch + 星光爆发（good/great），并开始接受点击</summary>
        void Reveal(GradeStyle style, TMP_Text big, TMP_Text small, TMP_Text hint,
            RectTransform burstOrigin)
        {
            big.gameObject.SetActive(true);
            if (small != null) small.gameObject.SetActive(true);

            var bigRect = big.rectTransform;
            var origin = burstOrigin != null ? burstOrigin : bigRect;
            bigRect.localScale = Vector3.one * 2.6f;
            bigRect.DOScale(1f, 0.38f).SetEase(Ease.InCubic)
                   .SetUpdate(true).SetLink(gameObject)
                   .OnComplete(() =>
                   {
                       bigRect.DOPunchScale(Vector3.one * 0.12f, 0.25f, 6, 0.7f)
                              .SetUpdate(true).SetLink(gameObject);
                       if (style.burst) PlayStarBurst(style.color, origin);
                   });

            if (hint != null)
            {
                hint.gameObject.SetActive(true);
                hint.DOFade(0.2f, 0.7f).SetLoops(-1, LoopType.Yoyo)
                    .SetUpdate(true).SetLink(gameObject);
            }
            _shownAt = Time.unscaledTime;
        }

        /// <summary>good/great 时从原点向外撒一圈星光（程序化四芒星贴图）</summary>
        void PlayStarBurst(Color color, RectTransform origin)
        {
            const int count = 10;
            for (int i = 0; i < count; i++)
            {
                var star = CreateImage("Star", origin, VNProceduralTextures.SparkleSprite, color);
                star.anchorMin = star.anchorMax = new Vector2(0.5f, 0.5f);
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
