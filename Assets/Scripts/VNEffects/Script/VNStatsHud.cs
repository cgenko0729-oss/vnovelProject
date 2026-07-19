using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 养成属性系统：数值全部落在 VNFlags（flag 名 = VNStatDef.id），因此存档、
    /// if 分支、choice 加减、调试重建全部复用现有设施。本组件负责三件事：
    ///   ① 执行剧本 stat 命令（按定义钳制 + VNToast 飘字；silent = 调试重建静默重放）
    ///   ② 顶栏 HUD（showInHud 的属性：图标色点 + 数值 + 变化滚动动画）
    ///   ③ C 键属性总览面板（全部属性：数值条 + 等级评价）
    /// 没有定义资产的属性也能用 stat 命令读写（不钳制、飘字用 id 当名字）。
    /// UI 全程序化构建在独立 Overlay Canvas 上，参照 VNQuestLog / VNToast。
    /// </summary>
    public class VNStatsHud : MonoBehaviour
    {
        [Header("属性定义资产（钳制范围/显示规则）；未登记的属性照常读写但不上 HUD")]
        public List<VNStatDef> stats = new List<VNStatDef>();

        Canvas _canvas;
        GameObject _hudBar;
        GameObject _panel;
        RectTransform _panelContent;
        bool _open;
        bool _dirty;
        bool _hudVisible = true;
        Sprite _dotSprite;

        // HUD 条目缓存：值变化时做滚动/闪色动画
        class HudEntry
        {
            public VNStatDef def;
            public TextMeshProUGUI value;
            public Image bar;
            public int lastValue;
            public Tween tween;
        }
        readonly List<HudEntry> _hudEntries = new List<HudEntry>();

        public bool IsOpen => _open;

        void Awake()
        {
            VNLocale.LanguageChanged += OnLanguageChanged;
            VNFlags.Changed += MarkDirty;
        }

        void Start()
        {
            EnsureInitials();
            BuildHud();
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNFlags.Changed -= MarkDirty;
        }

        void MarkDirty() => _dirty = true;

        /// <summary>语言切换：销毁缓存，HUD 立即重建、面板下次打开时重建</summary>
        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _hudBar = null;
            _panel = null;
            _panelContent = null;
            _hudEntries.Clear();
            BuildHud();
        }

        public VNStatDef Find(string id)
        {
            foreach (var s in stats)
                if (s != null && s.id == id) return s;
            return null;
        }

        /// <summary>定义了初始值的属性：flag 尚不存在时写入初始值（进场 / 读档后各调一次）</summary>
        public void EnsureInitials()
        {
            foreach (var def in stats)
            {
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (!VNFlags.All.ContainsKey(def.id))
                    VNFlags.Set(def.id, def.Clamp(def.initialValue));
            }
        }

        // ------------------------------------------------------------------
        // stat 命令执行
        // ------------------------------------------------------------------

        /// <summary>
        /// 执行 stat 命令：stat 名字 +5 / stat 名字 -3 / stat 名字 500。
        /// 与 flag 命令的区别：按定义钳制到 [min,max]，并 VNToast 飘字。
        /// silent = 调试重建时只写状态不弹 Toast。
        /// </summary>
        public void Apply(string name, string valueToken, bool silent, int line)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：stat 需要属性名（stat 金钱 +100）");
                return;
            }

            // 兼容黏连写法 stat 金钱+100（与 flag 命令一致）
            if (string.IsNullOrEmpty(valueToken))
            {
                for (int i = 1; i < name.Length; i++)
                {
                    if (name[i] == '+' || name[i] == '-')
                    {
                        valueToken = name.Substring(i);
                        name = name.Substring(0, i).Trim();
                        break;
                    }
                }
                if (string.IsNullOrEmpty(valueToken))
                {
                    Debug.LogWarning($"[VNStats] 第 {line} 行：stat 缺少数值（stat {name} +1）");
                    return;
                }
            }

            var def = Find(name);
            int old = VNFlags.Get(name);
            int target;
            bool isDelta = valueToken[0] == '+' || valueToken[0] == '-';
            if (!int.TryParse(valueToken, out int parsed))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：stat 值「{valueToken}」无法识别");
                return;
            }
            target = isDelta ? old + parsed : parsed;
            if (def != null) target = def.Clamp(target);
            if (target == old) return;

            VNFlags.Set(name, target);

            if (silent) return;
            string display = def != null ? def.DisplayName : name;
            int delta = target - old;
            if (isDelta)
                VNToast.Show(delta > 0
                    ? VNLocale.T("stats.toastGain", display, delta)
                    : VNLocale.T("stats.toastLose", display, -delta), 1.8f);
            else
                VNToast.Show(VNLocale.T("stats.toastSet", display,
                    def != null ? def.Format(target) : target.ToString()), 1.8f);
        }

        // ------------------------------------------------------------------
        // 选项花费（choice 的 cost: 参数）
        // ------------------------------------------------------------------

        /// <summary>解析花费串「金钱-100」「行动力-1」→ (属性名, 增减量)</summary>
        public static bool ParseCostOp(string costOp, out string name, out int delta)
        {
            name = null;
            delta = 0;
            if (string.IsNullOrEmpty(costOp)) return false;
            costOp = costOp.Trim();
            for (int i = 1; i < costOp.Length; i++)
            {
                if (costOp[i] != '+' && costOp[i] != '-') continue;
                name = costOp.Substring(0, i).Trim();
                return int.TryParse(costOp.Substring(i), out delta);
            }
            return false;
        }

        /// <summary>付得起吗？扣减后不得低于定义下限（无定义资产按 0 兜底）；增益恒真</summary>
        public bool CanAfford(string costOp)
        {
            if (!ParseCostOp(costOp, out string name, out int delta)) return true;
            if (delta >= 0) return true;
            var def = Find(name);
            int floor = def != null && def.useClamp ? def.minValue : 0;
            return VNFlags.Get(name) + delta >= floor;
        }

        /// <summary>花费的显示标签：有单位「-100G」，无单位「-1 行动力」</summary>
        public string FormatCostLabel(string costOp)
        {
            if (!ParseCostOp(costOp, out string name, out int delta)) return costOp;
            var def = Find(name);
            string sign = delta < 0 ? "-" : "+";
            int abs = Mathf.Abs(delta);
            if (def != null && def.style == VNStatStyle.Number && !string.IsNullOrEmpty(def.unit))
                return $"{sign}{abs}{def.unit}";
            string display = def != null ? def.DisplayName : name;
            return $"{sign}{abs} {display}";
        }

        /// <summary>应用花费（复用 Apply 的钳制 + 飘字）</summary>
        public void ApplyCost(string costOp, int line)
        {
            if (!ParseCostOp(costOp, out string name, out int delta))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：cost 串「{costOp}」无法识别" +
                                 "（应为 属性名±数值，如 金钱-100）");
                return;
            }
            Apply(name, (delta >= 0 ? "+" : "") + delta, false, line);
        }

        // ------------------------------------------------------------------
        // 顶栏 HUD
        // ------------------------------------------------------------------

        /// <summary>右键隐藏 UI 时连同 HUD 一起藏（面板不受影响：隐藏 UI 时本来打不开）</summary>
        public void SetHudVisible(bool visible)
        {
            _hudVisible = visible;
            if (_hudBar != null) _hudBar.SetActive(visible && HasHudStats());
        }

        bool HasHudStats()
        {
            foreach (var s in stats)
                if (s != null && s.showInHud) return true;
            return false;
        }

        void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("VNStatsCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 580; // HUD 常驻：低于任务日志/回想(600)，高于对话框(40)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        void BuildHud()
        {
            EnsureCanvas();
            if (_hudBar != null || !HasHudStats()) return;

            _hudBar = new GameObject("HudBar", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)_hudBar.transform;
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.sizeDelta = new Vector2(0f, 46f);

            var bg = _hudBar.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.018f, 0.026f, 0.052f, 0.78f);
            bg.raycastTarget = false;

            var layout = _hudBar.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 6, 6);
            layout.spacing = 26f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            _hudBar.GetComponent<ContentSizeFitter>().horizontalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _hudEntries.Clear();
            foreach (var def in stats)
            {
                if (def == null || !def.showInHud || string.IsNullOrEmpty(def.id)) continue;
                _hudEntries.Add(CreateHudEntry(def));
            }

            _hudBar.SetActive(_hudVisible);
            RefreshHud(false);
        }

        HudEntry CreateHudEntry(VNStatDef def)
        {
            var root = new GameObject(def.id, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            root.transform.SetParent(_hudBar.transform, false);
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // 图标（资产未配则用主题色小圆点）
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(LayoutElement));
            iconGo.transform.SetParent(root.transform, false);
            var iconLayout = iconGo.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 22f;
            iconLayout.preferredHeight = 22f;
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = def.icon != null ? def.icon : DotSprite();
            icon.color = def.icon != null ? Color.white : def.color;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            var name = CreateText(root.transform, 18, TextAlignmentOptions.MidlineLeft);
            name.text = def.DisplayName;
            name.color = new Color(0.78f, 0.8f, 0.88f, 1f);

            var value = CreateText(root.transform, 22, TextAlignmentOptions.MidlineLeft);
            value.fontStyle = FontStyles.Bold;
            value.color = new Color(0.97f, 0.97f, 1f, 1f);

            // 百分比/分数类属性在数值右侧加一根迷你进度条
            Image bar = null;
            if (def.Normalized(def.minValue) >= 0f)
            {
                var barBgGo = new GameObject("BarBg", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image), typeof(LayoutElement));
                barBgGo.transform.SetParent(root.transform, false);
                var barLayout = barBgGo.GetComponent<LayoutElement>();
                barLayout.preferredWidth = 64f;
                barLayout.preferredHeight = 8f;
                var barBg = barBgGo.GetComponent<Image>();
                barBg.sprite = VNProceduralTextures.RoundedRectSprite;
                barBg.type = Image.Type.Sliced;
                barBg.color = new Color(1f, 1f, 1f, 0.14f);
                barBg.raycastTarget = false;

                var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(Image));
                var fillRect = (RectTransform)fillGo.transform;
                fillRect.SetParent(barBgGo.transform, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.pivot = new Vector2(0f, 0.5f);
                fillRect.offsetMin = Vector2.zero;
                fillRect.offsetMax = Vector2.zero;
                bar = fillGo.GetComponent<Image>();
                bar.sprite = VNProceduralTextures.RoundedRectSprite;
                bar.type = Image.Type.Sliced;
                bar.color = def.color;
                bar.raycastTarget = false;
            }

            return new HudEntry
            {
                def = def, value = value, bar = bar,
                lastValue = VNFlags.Get(def.id),
            };
        }

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            EnsureInitials(); // 读档可能清掉了新属性的 flag，补回初始值
            RefreshHud(true);
            if (_open) RebuildPanelList();
        }

        void RefreshHud(bool animate)
        {
            foreach (var e in _hudEntries)
            {
                if (e.value == null) continue; // 语言切换销毁重建的间隙
                int v = VNFlags.Get(e.def.id);
                e.value.text = e.def.Format(v);
                if (e.bar != null)
                {
                    float n = Mathf.Max(0f, e.def.Normalized(v));
                    var rect = (RectTransform)e.bar.transform;
                    rect.anchorMax = new Vector2(n, 1f);
                }
                if (animate && v != e.lastValue)
                {
                    e.tween?.Kill(true);
                    e.value.color = v > e.lastValue
                        ? new Color(0.55f, 1f, 0.6f, 1f)
                        : new Color(1f, 0.5f, 0.5f, 1f);
                    e.value.transform.localScale = Vector3.one * 1.35f;
                    e.tween = DOTween.Sequence()
                        .Append(e.value.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack))
                        .Join(e.value.DOColor(new Color(0.97f, 0.97f, 1f, 1f), 0.6f))
                        .SetUpdate(true)
                        .SetLink(e.value.gameObject);
                }
                e.lastValue = v;
            }
        }

        Sprite DotSprite()
        {
            if (_dotSprite == null)
            {
                var tex = VNProceduralTextures.SoftCircle;
                _dotSprite = Sprite.Create(tex,
                    new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            return _dotSprite;
        }

        // ------------------------------------------------------------------
        // C 键属性总览面板
        // ------------------------------------------------------------------

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open) return;
            BuildPanel();
            RebuildPanelList();
            _panel.SetActive(true);
            _open = true;
        }

        public void Close()
        {
            if (!_open) return;
            _panel.SetActive(false);
            _open = false;
        }

        void BuildPanel()
        {
            if (_panel != null) return;
            EnsureCanvas();

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(_canvas.transform, false);
            Stretch(panelRect);

            // 半透明暗底（点击关闭）
            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(panelRect, false);
            Stretch(dimRect);
            dimGo.GetComponent<Image>().color = new Color(0f, 0.01f, 0.02f, 0.86f);
            dimGo.GetComponent<Button>().onClick.AddListener(Close);

            var title = CreateText(panelRect, 34, TextAlignmentOptions.Center);
            title.text = VNLocale.T("stats.title");
            title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)title.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -60f);
            titleRect.sizeDelta = new Vector2(0f, 50f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            _panelContent = (RectTransform)contentGo.transform;
            _panelContent.SetParent(panelRect, false);
            _panelContent.anchorMin = new Vector2(0.3f, 1f);
            _panelContent.anchorMax = new Vector2(0.7f, 1f);
            _panelContent.pivot = new Vector2(0.5f, 1f);
            _panelContent.anchoredPosition = new Vector2(0f, -150f);
            // sizeDelta 默认 (100,100)，横向拉伸下 = 比锚点区宽 100px（此处无遮罩不会被裁，
            // 但属性行会比设计宽度宽 100px），显式清零保证布局与锚点一致
            _panelContent.sizeDelta = Vector2.zero;
            var layout = contentGo.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 20f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            _panel.SetActive(false);
        }

        void RebuildPanelList()
        {
            if (_panelContent == null) return;
            for (int i = _panelContent.childCount - 1; i >= 0; i--)
                Destroy(_panelContent.GetChild(i).gameObject);

            if (stats.Count == 0)
            {
                var empty = CreateText(_panelContent, 28, TextAlignmentOptions.Center);
                empty.text = VNLocale.T("stats.empty");
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                return;
            }

            foreach (var def in stats)
            {
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                int v = VNFlags.Get(def.id);

                var row = new GameObject(def.id, typeof(RectTransform), typeof(LayoutElement));
                row.transform.SetParent(_panelContent, false);
                row.GetComponent<LayoutElement>().preferredHeight = 46f;
                var rowRect = (RectTransform)row.transform;

                var name = CreateText(rowRect, 27, TextAlignmentOptions.MidlineLeft);
                name.text = def.DisplayName;
                name.color = new Color(0.86f, 0.88f, 0.95f, 1f);
                var nameRect = (RectTransform)name.transform;
                nameRect.anchorMin = new Vector2(0f, 0f);
                nameRect.anchorMax = new Vector2(0.3f, 1f);
                nameRect.offsetMin = Vector2.zero;
                nameRect.offsetMax = Vector2.zero;

                // 中段：进度条（可画条的属性）
                float n = def.Normalized(v);
                if (n >= 0f)
                {
                    var barBgGo = new GameObject("BarBg", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image));
                    var barBgRect = (RectTransform)barBgGo.transform;
                    barBgRect.SetParent(rowRect, false);
                    barBgRect.anchorMin = new Vector2(0.32f, 0.32f);
                    barBgRect.anchorMax = new Vector2(0.74f, 0.68f);
                    barBgRect.offsetMin = Vector2.zero;
                    barBgRect.offsetMax = Vector2.zero;
                    var barBg = barBgGo.GetComponent<Image>();
                    barBg.sprite = VNProceduralTextures.RoundedRectSprite;
                    barBg.type = Image.Type.Sliced;
                    barBg.color = new Color(1f, 1f, 1f, 0.12f);
                    barBg.raycastTarget = false;

                    var fillGo = new GameObject("Fill", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image));
                    var fillRect = (RectTransform)fillGo.transform;
                    fillRect.SetParent(barBgRect, false);
                    fillRect.anchorMin = Vector2.zero;
                    fillRect.anchorMax = new Vector2(Mathf.Max(0.001f, n), 1f);
                    fillRect.offsetMin = Vector2.zero;
                    fillRect.offsetMax = Vector2.zero;
                    var fill = fillGo.GetComponent<Image>();
                    fill.sprite = VNProceduralTextures.RoundedRectSprite;
                    fill.type = Image.Type.Sliced;
                    fill.color = def.color;
                    fill.raycastTarget = false;
                }

                var value = CreateText(rowRect, 27, TextAlignmentOptions.MidlineRight);
                value.fontStyle = FontStyles.Bold;
                string grade = def.style == VNStatStyle.Grade ? def.GradeOf(v) : "";
                value.text = string.IsNullOrEmpty(grade)
                    ? def.Format(v)
                    : $"{def.Format(v)}  <color=#{ColorUtility.ToHtmlStringRGB(def.color)}>{grade}</color>";
                var valueRect = (RectTransform)value.transform;
                valueRect.anchorMin = new Vector2(0.76f, 0f);
                valueRect.anchorMax = new Vector2(1f, 1f);
                valueRect.offsetMin = Vector2.zero;
                valueRect.offsetMax = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------
        // 共用小件
        // ------------------------------------------------------------------

        TextMeshProUGUI CreateText(Transform parent, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.raycastTarget = false;
            return t;
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
