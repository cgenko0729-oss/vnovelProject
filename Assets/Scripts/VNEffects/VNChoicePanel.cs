using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 选项按钮演出面板（零新特效，纯组合现有件）：
    ///   - 选项错落飞入（右侧滑入 + 淡入，依次延迟）
    ///   - 悬停：扫光掠过 + 微放大（VNImageEffectController 直接挂在按钮上）
    ///   - 选中：被选项闪光确认，其余选项噪声溶解消散
    /// 用法：choicePanel.Show(new[]{"选项A","选项B"}, idx => { ... });
    /// 需要场景里有 EventSystem（场景生成器自动创建）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNChoicePanel : MonoBehaviour
    {
        [Header("渲染排序（高于对话框 40，低于全屏转场 100）")]
        public int sortingOrder = 45;

        [Header("选项按钮底色")]
        public Color buttonColor = new Color(0.07f, 0.09f, 0.16f, 0.88f);
        [Header("按钮尺寸（像素）")]
        public Vector2 buttonSize = new Vector2(560f, 84f);
        [Header("按钮纵向间距（像素）")]
        public float buttonSpacing = 26f;

        class Entry
        {
            public GameObject go;
            public RectTransform rect;
            public CanvasGroup group;
            public VNImageEffectController fx;
        }

        readonly List<Entry> _entries = new List<Entry>();
        CanvasGroup _group;
        System.Action<int> _callback;
        bool _built;
        bool _busy;

        public bool IsShowing => _entries.Count > 0;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            // 嵌套 Canvas 需要自己的 Raycaster 才能接收点击
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
        }

        /// <summary>显示一组选项，玩家选择后回调（回调在演出结束后触发）</summary>
        public void Show(string[] options, System.Action<int> onChosen)
        {
            Build();
            if (_busy || options == null || options.Length == 0) return;
            ClearEntries();
            _callback = onChosen;
            _group.blocksRaycasts = true;

            float totalH = options.Length * buttonSize.y + (options.Length - 1) * buttonSpacing;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            for (int i = 0; i < options.Length; i++)
            {
                float y = totalH * 0.5f - buttonSize.y * 0.5f - i * (buttonSize.y + buttonSpacing);
                var entry = CreateButton(options[i], i, new Vector2(0f, y + 60f), font);
                _entries.Add(entry);

                // 错落飞入：右侧 90px 外滑入 + 淡入
                var target = new Vector2(0f, y + 60f);
                entry.rect.anchoredPosition = target + new Vector2(90f, 0f);
                entry.group.alpha = 0f;
                entry.rect.DOAnchorPos(target, 0.45f)
                          .SetEase(Ease.OutCubic).SetDelay(i * 0.09f).SetLink(entry.go);
                entry.group.DOFade(1f, 0.35f).SetDelay(i * 0.09f).SetLink(entry.go);
            }
        }

        Entry CreateButton(string label, int index, Vector2 pos, Font font)
        {
            var go = new GameObject($"Choice_{index}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = buttonSize;
            rect.anchoredPosition = pos;

            var img = go.GetComponent<Image>();
            img.sprite = VNProceduralTextures.RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = buttonColor;
            img.raycastTarget = true;

            var group = go.AddComponent<CanvasGroup>();

            // 特效控制器：悬停扫光 / 选中闪光 / 落选溶解，全部现成 API
            var fx = go.AddComponent<VNImageEffectController>();
            fx.SetShineStyle(0.28f, 30f, new Color(1.8f, 1.6f, 1.1f, 0.7f));

            var text = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var textRect = (RectTransform)text.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var t = text.GetComponent<Text>();
            t.font = font;
            t.fontSize = 30;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = new Color(1f, 1f, 1f, 0.95f);
            t.raycastTarget = false;
            t.text = label;

            var button = go.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            int idx = index;
            button.onClick.AddListener(() => Choose(idx));

            // 悬停演出：扫光掠过 + 微放大
            var trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (_busy) return;
                fx.PlayShine(0.5f);
                rect.DOScale(1.045f, 0.15f).SetEase(Ease.OutQuad).SetLink(go);
            });
            trigger.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (_busy) return;
                rect.DOScale(1f, 0.15f).SetEase(Ease.OutQuad).SetLink(go);
            });
            trigger.triggers.Add(exit);

            return new Entry { go = go, rect = rect, group = group, fx = fx };
        }

        void Choose(int index)
        {
            if (_busy) return;
            _busy = true;
            _group.blocksRaycasts = false;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (i == index)
                {
                    // 被选项：闪光确认 + 扫光 + 轻弹
                    e.fx.DOFlash(0.45f, 0.35f);
                    e.fx.PlayShine(0.4f);
                    e.rect.DOScale(1.07f, 0.18f).SetEase(Ease.OutBack).SetLink(e.go);
                }
                else
                {
                    // 落选项：噪声溶解消散
                    e.fx.DODissolve(0f, 0.45f);
                    e.group.DOFade(0.6f, 0.45f).SetLink(e.go);
                }
            }

            int chosen = index;
            DOVirtual.DelayedCall(0.8f, () =>
            {
                var cb = _callback;
                _callback = null;
                HideAll(() =>
                {
                    _busy = false;
                    cb?.Invoke(chosen);
                });
            }).SetLink(gameObject);
        }

        void HideAll(System.Action onDone)
        {
            int pending = _entries.Count;
            if (pending == 0) { onDone?.Invoke(); return; }
            foreach (var e in _entries)
            {
                e.group.DOFade(0f, 0.25f).SetLink(e.go)
                       .OnComplete(() => { if (--pending == 0) { ClearEntries(); onDone?.Invoke(); } });
            }
        }

        void ClearEntries()
        {
            foreach (var e in _entries)
                if (e.go != null) Destroy(e.go);
            _entries.Clear();
        }

        /// <summary>立即关闭并丢弃当前选项（读档/中断剧本时用）</summary>
        public void ForceClose()
        {
            _callback = null;
            _busy = false;
            if (_group != null) _group.blocksRaycasts = false;
            ClearEntries();
        }
    }
}
