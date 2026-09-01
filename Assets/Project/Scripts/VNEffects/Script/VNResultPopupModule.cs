using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>事件结果弹窗：播放判定冲条、等级揭晓和星光爆发演出。</summary>
    public class VNResultPopupModule : VNEventModule
    {
        [Header("大字揭晓后多少秒才接受点击（防止连点误触）")]
        public float inputDelay = 0.4f;

        [Header("判定冲条的演出时长（秒）")]
        public float suspenseDuration = 0.9f;


        class GradeStyle
        {
            public string bigLabel;
            public string localeKey;
            public Color color;
            public bool burst;
        }

        static GradeStyle StyleOf(string grade)
        {
            switch (grade)
            {
                case "fail": return new GradeStyle
                {
                    bigLabel = "FAIL…", localeKey = "result.fail",
                    color = new Color(0.62f, 0.65f, 0.75f, 1f),
                    burst = false,
                };
                case "good": return new GradeStyle
                {
                    bigLabel = "GOOD!", localeKey = "result.good",
                    color = new Color(1f, 0.55f, 0.72f, 1f),
                    burst = true,
                };
                case "great": return new GradeStyle
                {
                    bigLabel = "GREAT!!", localeKey = "result.great",
                    color = new Color(1f, 0.84f, 0.32f, 1f),
                    burst = true,
                };
                default: return new GradeStyle
                {
                    bigLabel = "OK", localeKey = "result.normal",
                    color = new Color(0.75f, 0.88f, 1f, 1f),
                    burst = false,
                };
            }
        }

        string _grade;
        float _shownAt;
        bool _closing;
        RectTransform _panel;
        VNResultPopupSkin _skin;

        protected override void OnLaunch(VNEventContext ctx)
        {
            _grade = ctx.Kw("grade", "normal");
            if (_grade != "fail" && _grade != "normal" && _grade != "good" && _grade != "great")
            {
                Debug.LogWarning($"[VNEvent] 第 {ctx.line} 行：result 等级「{_grade}」无效；应为 fail/normal/good/great，已按 normal 处理");
                _grade = "normal";
            }

            string se = ctx.Kw("se");
            if (!string.IsNullOrEmpty(se))
                ctx.stage?.vnAudio?.PlaySe(se, false, 1f, ctx.line);

            _shownAt = float.MaxValue;

            var style = StyleOf(_grade);
            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.resultPopupPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNResultPopupSkin>(
                skinPrefab, transform, "VNResultPopup");
            if (_skin == null)
                throw new System.InvalidOperationException("Result popup prefab is missing or invalid.");
            BuildFromSkin(style, ctx.Kw("title"), ctx.Kw("sub"));
        }

        void Update()
        {
            if (VNPause.IsPaused) return;   // 教程讲解中：别让结算弹窗被同一下点击收掉
            if (_closing || VNTime.Time - _shownAt < inputDelay) return;

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
        // ------------------------------------------------------------------

        void BuildFromSkin(GradeStyle style, string title, string sub)
        {
            _panel = _skin.panelRoot;
            //even i have a prefab with certain color, this line will force change the color to panelTint, so i comment out it
            //if (_skin.panelBackground != null) _skin.panelBackground.color = style.panelTint;

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
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // ------------------------------------------------------------------

        void PopInPanel()
        {
            _panel.localScale = Vector3.one * 0.7f;
            _panel.DOScale(1f, 0.32f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);
        }
        void PlaySuspense(GameObject barRoot, RectTransform barFill, TMP_Text percentText,
            System.Action reveal)
        {
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
            _shownAt = VNTime.Time;
        }

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

    }
}
