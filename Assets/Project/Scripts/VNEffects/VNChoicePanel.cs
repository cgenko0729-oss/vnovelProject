using System.Collections.Generic;
using DG.Tweening;
using TMPro;
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
    ///
    /// 【皮肤系统】按钮外观两条路（演出行为两条路共用）：
    ///   1. 程序化默认（零素材兜底）：代码拼按钮，与老版本一致；
    ///   2. 皮肤 prefab：ApplySkin(VNChoiceSkin) 后按 buttonTemplate 克隆，
    ///      容器挂 LayoutGroup 则排版交给它（入场改淡入缩放），
    ///      否则以模板锚点为首项向下堆叠——模板锚右侧 = 选项出现在右侧。
    /// 剧本 `ui choice &lt;id&gt;` 经 VNStage 调到这里。
    /// 用法：choicePanel.Show(new[]{"选项A","选项B"}, idx => { ... });
    /// 需要场景里有 EventSystem（场景生成器自动创建）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNChoicePanel : MonoBehaviour
    {
        [Header("渲染排序（高于对话框 40，低于全屏转场 100）")]
        public int sortingOrder = 45;

        [Header("选项按钮底色（程序化默认皮肤用）")]
        public Color buttonColor = new Color(0.07f, 0.09f, 0.16f, 0.88f);
        [Header("按钮尺寸（像素，程序化默认皮肤用）")]
        public Vector2 buttonSize = new Vector2(560f, 84f);
        [Header("按钮纵向间距（像素，程序化默认皮肤用）")]
        public float buttonSpacing = 26f;

        /// <summary>一个选项的显示描述（花费/置灰是 P2 选项花费扩展）</summary>
        public class Option
        {
            public string text;
            public string costLabel;         // 右侧小字（如 -100G），null = 不显示
            public bool interactable = true; // false = 置灰不可选（如钱不够）
        }

        class Entry
        {
            public GameObject go;
            public RectTransform rect;
            public CanvasGroup group;
            public VNImageEffectController fx; // 模板根无 Image 时为 null（演出降级）
        }

        readonly List<Entry> _entries = new List<Entry>();
        CanvasGroup _group;
        System.Action<int> _callback;
        bool _built;
        bool _busy;

        // ---- 皮肤 ----
        VNChoiceSkin _skin;        // 绑定的皮肤实例（null = 程序化默认）
        GameObject _customRoot;    // 皮肤实例根（切回默认时销毁）
        bool _skinUsesLayout;      // container 挂了 LayoutGroup：排版交给它
        string _labelPath;         // template → 文字槽 的相对路径（克隆后按路径找）
        string _costPath;          // template → 花费槽 的相对路径（可空）

        public bool IsShowing => _entries.Count > 0;
        /// <summary>当前是否在用自定义皮肤 prefab（false = 程序化默认样式）</summary>
        public bool HasCustomSkin => _skin != null;

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

        // ------------------------------------------------------------------
        // 皮肤切换
        // ------------------------------------------------------------------

        /// <summary>切换皮肤：skinPrefab = null 回程序化默认。丢弃正在显示的选项。</summary>
        public void ApplySkin(VNChoiceSkin skinPrefab)
        {
            Build();
            ForceClose(); // 选项在旧皮肤容器里，先清干净

            if (_customRoot != null)
            {
                Destroy(_customRoot);
                _customRoot = null;
            }
            _skin = null;
            _skinUsesLayout = false;
            _labelPath = _costPath = null;

            if (skinPrefab == null) return;

            _customRoot = Instantiate(skinPrefab.gameObject, transform);
            _customRoot.name = "Skin_" + skinPrefab.gameObject.name;
            var instRect = (RectTransform)_customRoot.transform;
            instRect.anchorMin = Vector2.zero;
            instRect.anchorMax = Vector2.one;
            instRect.offsetMin = Vector2.zero;
            instRect.offsetMax = Vector2.zero;
            _customRoot.SetActive(true);

            var skin = _customRoot.GetComponent<VNChoiceSkin>();
            if (skin == null || skin.container == null || skin.buttonTemplate == null ||
                skin.buttonLabel == null)
            {
                Debug.LogError("[VNChoice] 皮肤 prefab 缺少 VNChoiceSkin 或必填槽位" +
                               "（container/buttonTemplate/buttonLabel），回退默认样式");
                Destroy(_customRoot);
                _customRoot = null;
                return;
            }

            _skin = skin;
            _skin.buttonTemplate.gameObject.SetActive(false); // 模板保持禁用
            _skinUsesLayout = skin.container.GetComponent<LayoutGroup>() != null;
            _labelPath = PathBetween(skin.buttonTemplate.transform, skin.buttonLabel.transform);
            _costPath = skin.buttonCost != null
                ? PathBetween(skin.buttonTemplate.transform, skin.buttonCost.transform)
                : null;
        }

        /// <summary>root 到 target 的层级相对路径（克隆体上按同路径找对应槽位）</summary>
        static string PathBetween(Transform root, Transform target)
        {
            if (target == root) return "";
            var parts = new List<string>();
            for (var t = target; t != null && t != root; t = t.parent)
                parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        static TMP_Text FindByPath(Transform root, string path)
        {
            if (path == null) return null;
            if (path.Length == 0) return root.GetComponent<TMP_Text>();
            var t = root.Find(path);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        // ------------------------------------------------------------------
        // 显示
        // ------------------------------------------------------------------

        /// <summary>显示一组纯文本选项，玩家选择后回调（回调在演出结束后触发）</summary>
        public void Show(string[] options, System.Action<int> onChosen)
        {
            if (options == null) return;
            var wrapped = new Option[options.Length];
            for (int i = 0; i < options.Length; i++)
                wrapped[i] = new Option { text = options[i] };
            Show(wrapped, onChosen);
        }

        /// <summary>显示一组带花费/置灰状态的选项</summary>
        public void Show(Option[] options, System.Action<int> onChosen)
        {
            Build();
            if (_busy || options == null || options.Length == 0) return;
            ClearEntries();
            _callback = onChosen;
            _group.blocksRaycasts = true;

            for (int i = 0; i < options.Length; i++)
            {
                Entry entry = _skin != null
                    ? CreateSkinButton(options[i], i, options.Length)
                    : CreateDefaultButton(options[i], i, options.Length);
                _entries.Add(entry);
                PlayEntrance(entry, i);
            }
        }

        void PlayEntrance(Entry entry, int index)
        {
            entry.group.alpha = 0f;
            entry.group.DOFade(1f, 0.35f).SetDelay(index * 0.09f).SetLink(entry.go);

            if (_skin != null && _skinUsesLayout)
            {
                // Layout 管位置：位移交不出去，改缩放入场
                entry.rect.localScale = Vector3.one * 0.92f;
                entry.rect.DOScale(1f, 0.35f).SetEase(Ease.OutCubic)
                          .SetDelay(index * 0.09f).SetLink(entry.go);
                return;
            }
            // 错落飞入：右侧 90px 外滑入
            var target = entry.rect.anchoredPosition;
            entry.rect.anchoredPosition = target + new Vector2(90f, 0f);
            entry.rect.DOAnchorPos(target, 0.45f)
                      .SetEase(Ease.OutCubic).SetDelay(index * 0.09f).SetLink(entry.go);
        }

        /// <summary>皮肤路径：克隆 buttonTemplate，按路径填文字/花费，加演出与点击</summary>
        Entry CreateSkinButton(Option option, int index, int total)
        {
            var go = Instantiate(_skin.buttonTemplate.gameObject, _skin.container);
            go.name = $"Choice_{index}";
            go.SetActive(true);
            var rect = (RectTransform)go.transform;

            if (!_skinUsesLayout)
            {
                // 以模板锚点为首项向下堆叠（模板的锚点/pivot 已随克隆保留）
                float step = rect.sizeDelta.y + _skin.spacing;
                rect.anchoredPosition = _skin.buttonTemplate.anchoredPosition +
                                        new Vector2(0f, -index * step);
            }

            var label = FindByPath(rect, _labelPath);
            if (label != null)
            {
                label.text = option.text;
                if (!option.interactable)
                {
                    var c = label.color;
                    label.color = new Color(c.r, c.g, c.b, c.a * 0.45f);
                }
            }

            var cost = FindByPath(rect, _costPath);
            if (cost != null)
            {
                cost.gameObject.SetActive(!string.IsNullOrEmpty(option.costLabel));
                if (!string.IsNullOrEmpty(option.costLabel))
                {
                    cost.text = option.costLabel;
                    if (!option.interactable) // 付不起：花费小字转红
                        cost.color = new Color(1f, 0.38f, 0.38f, 0.9f);
                }
            }

            var img = go.GetComponent<Image>();
            if (img != null)
            {
                img.raycastTarget = true; // 置灰项也接收 raycast：挡住穿透点击推进剧情
                if (!option.interactable)
                    img.color = new Color(img.color.r * 0.55f, img.color.g * 0.55f,
                                          img.color.b * 0.55f, img.color.a * 0.8f);
            }

            return FinishButton(go, rect, img, option, index);
        }

        /// <summary>程序化默认路径：与老版本一致的代码拼按钮</summary>
        Entry CreateDefaultButton(Option option, int index, int total)
        {
            float totalH = total * buttonSize.y + (total - 1) * buttonSpacing;
            float y = totalH * 0.5f - buttonSize.y * 0.5f - index * (buttonSize.y + buttonSpacing);

            var go = new GameObject($"Choice_{index}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = buttonSize;
            rect.anchoredPosition = new Vector2(0f, y + 60f);

            var img = go.GetComponent<Image>();
            img.sprite = VNProceduralTextures.RoundedRectSprite;
            img.type = Image.Type.Sliced;
            img.color = option.interactable
                ? buttonColor
                : new Color(buttonColor.r * 0.55f, buttonColor.g * 0.55f,
                            buttonColor.b * 0.55f, buttonColor.a * 0.8f);
            img.raycastTarget = true; // 置灰项也接收 raycast：挡住穿透点击推进剧情

            var text = new GameObject("Label",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)text.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var t = text.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = 30;
            t.alignment = TextAlignmentOptions.Center;
            t.color = option.interactable
                ? new Color(1f, 1f, 1f, 0.95f)
                : new Color(1f, 1f, 1f, 0.45f);
            t.raycastTarget = false;
            t.text = option.text;

            // 右侧花费小字：可选=金色，钱不够=红色
            if (!string.IsNullOrEmpty(option.costLabel))
            {
                var costGo = new GameObject("Cost",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                var costRect = (RectTransform)costGo.transform;
                costRect.SetParent(rect, false);
                costRect.anchorMin = new Vector2(1f, 0f);
                costRect.anchorMax = new Vector2(1f, 1f);
                costRect.pivot = new Vector2(1f, 0.5f);
                costRect.anchoredPosition = new Vector2(-18f, 0f);
                costRect.sizeDelta = new Vector2(160f, 0f);
                var costText = costGo.GetComponent<TextMeshProUGUI>();
                costText.font = VNFont.Asset;
                costText.fontSize = 22;
                costText.alignment = TextAlignmentOptions.MidlineRight;
                costText.fontStyle = FontStyles.Bold;
                costText.color = option.interactable
                    ? new Color(1f, 0.84f, 0.42f, 0.95f)
                    : new Color(1f, 0.38f, 0.38f, 0.9f);
                costText.raycastTarget = false;
                costText.text = option.costLabel;
            }

            return FinishButton(go, rect, img, option, index);
        }

        /// <summary>两条路共用的收尾：CanvasGroup、特效控制器、点击与悬停演出</summary>
        Entry FinishButton(GameObject go, RectTransform rect, Image img, Option option, int index)
        {
            var group = go.GetComponent<CanvasGroup>();
            if (group == null) group = go.AddComponent<CanvasGroup>();

            // 特效控制器：悬停扫光 / 选中闪光 / 落选溶解（根上没 Image 就降级无特效）
            VNImageEffectController fx = null;
            if (img != null)
            {
                fx = go.GetComponent<VNImageEffectController>();
                if (fx == null) fx = go.AddComponent<VNImageEffectController>();
                fx.SetShineStyle(0.28f, 30f, new Color(1.8f, 1.6f, 1.1f, 0.7f));
            }

            if (option.interactable)
            {
                var button = go.GetComponent<Button>();
                if (button == null) button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                int idx = index;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => Choose(idx));

                // 悬停演出：扫光掠过 + 微放大
                var trigger = go.GetComponent<EventTrigger>();
                if (trigger == null) trigger = go.AddComponent<EventTrigger>();
                trigger.triggers.Clear();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ =>
                {
                    if (_busy) return;
                    fx?.PlayShine(0.5f);
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
            }
            else
            {
                // 模板可能自带 Button（美术直接拿现成按钮改）：置灰项禁用交互
                var button = go.GetComponent<Button>();
                if (button != null) button.interactable = false;
            }

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
                    e.fx?.DOFlash(0.45f, 0.35f);
                    e.fx?.PlayShine(0.4f);
                    e.rect.DOScale(1.07f, 0.18f).SetEase(Ease.OutBack).SetLink(e.go);
                }
                else
                {
                    // 落选项：噪声溶解消散
                    e.fx?.DODissolve(0f, 0.45f);
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
