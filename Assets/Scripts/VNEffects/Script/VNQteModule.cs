using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 示例事件模块：QTE 连打条。限定时间内点击/按空格达到目标次数。
    /// 结果："success" / "fail"。UI 全程序化生成，无素材依赖。
    ///
    /// 剧本用法：
    ///   event qte time:3 target:20 title:挣脱束缚！
    ///   * success -> 挣脱了
    ///   * fail -> 被抓住
    /// </summary>
    public class VNQteModule : VNEventModule
    {
        [Header("默认参数（剧本 kwargs 可覆盖）")]
        [Header("限时（秒），剧本 time: 覆盖")]
        public float duration = 4f;
        [Header("目标点击次数，剧本 target: 覆盖")]
        public int targetCount = 15;

        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.14f, 0.92f);
        static readonly Color AccentColor = new Color(0.35f, 0.85f, 1f, 1f);
        static readonly Color SuccessColor = new Color(0.4f, 1f, 0.6f, 1f);
        static readonly Color FailColor = new Color(0.75f, 0.75f, 0.78f, 1f);

        enum Phase { Idle, Playing, Ending }
        Phase _phase = Phase.Idle;
        float _timeLeft;
        int _count;

        RectTransform _panel;
        RectTransform _fill;
        Text _title, _counter, _timer;

        protected override void OnLaunch(VNEventContext ctx)
        {
            _timeLeft = Mathf.Max(0.5f, ctx.KwF("time", duration));
            targetCount = Mathf.Max(1, ctx.KwI("target", targetCount));
            _count = 0;

            BuildUi(ctx.Kw("title", "连打！"));
            _phase = Phase.Playing;
        }

        void Update()
        {
            if (_phase != Phase.Playing) return;

            _timeLeft -= Time.unscaledDeltaTime; // 不受快进 timeScale 影响

            bool pressed =
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
            if (pressed)
            {
                _count++;
                _panel.DOKill(true);
                _panel.DOPunchScale(Vector3.one * 0.04f, 0.15f, 8, 0.6f)
                      .SetUpdate(true).SetLink(gameObject);
            }

            RefreshHud();

            if (_count >= targetCount) Finish(true);
            else if (_timeLeft <= 0f) Finish(false);
        }

        void Finish(bool success)
        {
            _phase = Phase.Ending;
            _title.text = success ? "成功！" : "失败…";
            _title.color = success ? SuccessColor : FailColor;
            _title.transform.DOKill();
            _title.transform.localScale = Vector3.one * (success ? 1.4f : 1f);
            _title.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack)
                  .SetUpdate(true).SetLink(gameObject);

            DOVirtual.DelayedCall(0.8f, () => Done(success ? "success" : "fail"), true)
                     .SetLink(gameObject);
        }

        void RefreshHud()
        {
            _counter.text = $"{_count} / {targetCount}";
            _timer.text = $"{Mathf.Max(0f, _timeLeft):0.0}s";
            float t = Mathf.Clamp01(_count / (float)targetCount);
            _fill.anchorMax = new Vector2(t, 1f);
        }

        // ------------------------------------------------------------------
        // 程序化 UI
        // ------------------------------------------------------------------

        void BuildUi(string titleText)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 全屏暗幕（拦截点击，也是"点哪都算"的感受来源）
            var dim = CreateImage("Dim", (RectTransform)transform, null,
                new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim);

            // 中央面板
            _panel = CreateImage("Panel", (RectTransform)transform,
                VNProceduralTextures.RoundedRectSprite, PanelColor);
            var img = _panel.GetComponent<Image>();
            img.type = Image.Type.Sliced;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = new Vector2(640f, 300f);

            _title = CreateText("Title", _panel, font, 52, AccentColor, titleText);
            var titleRect = (RectTransform)_title.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 0.72f);
            titleRect.sizeDelta = new Vector2(600f, 70f);

            // 进度条：背景 + 按 anchorMax.x 伸长的填充
            var barBg = CreateImage("BarBg", _panel, VNProceduralTextures.RoundedRectSprite,
                new Color(1f, 1f, 1f, 0.12f));
            barBg.GetComponent<Image>().type = Image.Type.Sliced;
            barBg.anchorMin = barBg.anchorMax = new Vector2(0.5f, 0.42f);
            barBg.sizeDelta = new Vector2(520f, 34f);

            _fill = CreateImage("BarFill", barBg, VNProceduralTextures.RoundedRectSprite,
                AccentColor);
            _fill.GetComponent<Image>().type = Image.Type.Sliced;
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;

            _counter = CreateText("Counter", _panel, font, 30, Color.white, "");
            var counterRect = (RectTransform)_counter.transform;
            counterRect.anchorMin = counterRect.anchorMax = new Vector2(0.5f, 0.2f);
            counterRect.sizeDelta = new Vector2(300f, 40f);

            _timer = CreateText("Timer", _panel, font, 26,
                new Color(1f, 1f, 1f, 0.7f), "");
            var timerRect = (RectTransform)_timer.transform;
            timerRect.anchorMin = timerRect.anchorMax = new Vector2(0.88f, 0.85f);
            timerRect.sizeDelta = new Vector2(140f, 40f);

            RefreshHud();

            // 面板弹入
            _panel.localScale = Vector3.one * 0.7f;
            _panel.DOScale(1f, 0.3f).SetEase(Ease.OutBack).SetUpdate(true)
                  .SetLink(gameObject);
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

        static Text CreateText(string name, RectTransform parent, Font font,
            int size, Color color, string content)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
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
