using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>养成属性 HUD 与属性总览：属性值存入 VNFlags，并根据 VNStatDef 显示与更新。</summary>
    public class VNStatsHud : MonoBehaviour
    {
        [Header("属性定义（未登记的属性仍可存储，但不会显示在 HUD）")]
        public List<VNStatDef> stats = new List<VNStatDef>();

        Canvas _canvas;
        GameObject _hudBar;
        GameObject _panel;
        RectTransform _panelContent;
        RectTransform _hudEntryContainer;
        VNStatsHudSkin _hudSkin;
        VNStatsHudEntrySkin _hudTemplate;
        VNStatsPanelSkin _panelSkin;
        VNStatsPanelRowSkin _panelTemplate;
        bool _open;
        bool _dirty;
        bool _hudVisible = true;
        Sprite _dotSprite;

        class HudEntry
        {
            public VNStatDef def;
            public TextMeshProUGUI value;
            public Image bar;
            public Image icon;
            public RectTransform root;   // 就地演出（图标弹跳 / +N 上飘）的定位基准
            public int lastValue;
            public Tween tween;
            public Tween barTween;
            public Tween rollTween;
        }

        /// <summary>属性涨/跌的统一配色（HUD 数字、飘字、提示卡片色条共用）</summary>
        static readonly Color GainColor = new Color(0.45f, 1f, 0.62f, 1f);
        static readonly Color LoseColor = new Color(1f, 0.48f, 0.5f, 1f);
        readonly List<HudEntry> _hudEntries = new List<HudEntry>();

        public bool IsOpen => _open;

        void Awake()
        {
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.stats, ref stats);

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

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _hudBar = null;
            _panel = null;
            _panelContent = null;
            _hudEntryContainer = null;
            _hudSkin = null;
            _hudTemplate = null;
            _panelSkin = null;
            _panelTemplate = null;
            _hudEntries.Clear();
            BuildHud();
        }

        public VNStatDef Find(string id)
        {
            foreach (var s in stats)
                if (s != null && s.id == id) return s;
            return null;
        }

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
        // ------------------------------------------------------------------
        public void Apply(string name, string valueToken, bool silent, int line)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：stat 参数过多；格式应为 stat 属性 +100");
                return;
            }

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
                    Debug.LogWarning($"[VNStats] 第 {line} 行：stat 缺少数值；格式应为 stat {name} +1");
                    return;
                }
            }

            var def = Find(name);
            int old = VNFlags.Get(name);
            int target;
            bool isDelta = valueToken[0] == '+' || valueToken[0] == '-';
            if (!int.TryParse(valueToken, out int parsed))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：stat 数值「{valueToken}」无法解析");
                return;
            }
            target = isDelta ? old + parsed : parsed;
            if (def != null) target = def.Clamp(target);
            if (target == old) return;

            VNFlags.Set(name, target);

            if (silent) return;
            string display = def != null ? def.DisplayName : name;
            int delta = target - old;

            // 卡片：图标+主题色认属性，左侧竖条认涨跌（HUD 的就地演出由 RefreshHud 负责）
            Sprite icon = def != null && def.icon != null ? def.icon : null;
            Color iconColor = def != null ? def.color : new Color(0.8f, 0.85f, 1f);
            Color accent = delta > 0 ? GainColor : delta < 0 ? LoseColor : iconColor;

            string message = isDelta
                ? (delta > 0
                    ? VNLocale.T("stats.toastGain", display, delta)
                    : VNLocale.T("stats.toastLose", display, -delta))
                : VNLocale.T("stats.toastSet", display,
                    def != null ? def.Format(target) : target.ToString());
            VNToast.Show(message, icon, iconColor, accent, 1.8f);
        }

        // ------------------------------------------------------------------
        // ------------------------------------------------------------------

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

        public bool CanAfford(string costOp)
        {
            if (!ParseCostOp(costOp, out string name, out int delta)) return true;
            if (delta >= 0) return true;
            var def = Find(name);
            int floor = def != null && def.useClamp ? def.minValue : 0;
            return VNFlags.Get(name) + delta >= floor;
        }

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

        public void ApplyCost(string costOp, int line)
        {
            if (!ParseCostOp(costOp, out string name, out int delta))
            {
                Debug.LogWarning($"[VNStats] 第 {line} 行：cost 表达式「{costOp}」无法解析；格式示例：金钱-100");
                return;
            }
            Apply(name, (delta >= 0 ? "+" : "") + delta, false, line);
        }

        // ------------------------------------------------------------------
        // ------------------------------------------------------------------

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
            _canvas.sortingOrder = 580;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        void BuildHud()
        {
            EnsureCanvas();
            if (_hudBar != null || !HasHudStats()) return;

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.statsHudPrefab);
            _hudSkin = VNSystemUiSkinUtility.Instantiate<VNStatsHudSkin>(
                skinPrefab, _canvas.transform, "VNStatsHud");
            if (_hudSkin == null)
                throw new System.InvalidOperationException("Stats HUD prefab is missing or invalid.");

            _hudBar = _hudSkin.hudRoot;
            _hudEntryContainer = _hudSkin.entryContainer;
            VNTutorialAnchors.Register(VNUiAnchors.AnchorStats, (RectTransform)_hudBar.transform);
            _hudTemplate = _hudSkin.entryTemplate;
            _hudTemplate.gameObject.SetActive(false);
            _hudEntries.Clear();
            foreach (var def in stats)
            {
                if (def == null || !def.showInHud || string.IsNullOrEmpty(def.id)) continue;
                _hudEntries.Add(CreateCustomHudEntry(def));
            }
            _hudBar.SetActive(_hudVisible);
            RefreshHud(false);
        }

        HudEntry CreateCustomHudEntry(VNStatDef def)
        {
            var go = Instantiate(_hudTemplate.gameObject, _hudEntryContainer, false);
            go.name = def.id;
            go.SetActive(true);
            var skin = go.GetComponent<VNStatsHudEntrySkin>();
            skin.icon.sprite = def.icon != null ? def.icon : DotSprite();
            skin.icon.color = def.icon != null ? Color.white : def.color;
            skin.nameText.text = def.DisplayName;

            bool hasBar = def.Normalized(def.minValue) >= 0f &&
                          skin.barRoot != null && skin.barFill != null;
            if (skin.barRoot != null) skin.barRoot.SetActive(hasBar);
            if (hasBar) skin.barFill.color = def.color;
            VNTutorialAnchors.Register(VNUiAnchors.Stat(def.id), (RectTransform)go.transform);
            return new HudEntry
            {
                def = def,
                value = skin.valueText,
                bar = hasBar ? skin.barFill : null,
                icon = skin.icon,
                root = (RectTransform)go.transform,
                lastValue = VNFlags.Get(def.id),
            };
        }

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            EnsureInitials();
            RefreshHud(true);
            if (_open) RebuildPanelList();
        }

        void RefreshHud(bool animate)
        {
            foreach (var e in _hudEntries)
            {
                if (e.value == null) continue;
                int v = VNFlags.Get(e.def.id);
                bool changed = v != e.lastValue;
                bool play = animate && changed;

                // 数值：变化时滚动到新值，否则直接落位
                if (play) RollValue(e, e.lastValue, v);
                else
                {
                    e.rollTween?.Kill();
                    e.value.text = e.def.Format(v);
                }

                // 进度条：变化时补间推进，否则瞬切
                if (e.bar != null)
                {
                    float n = Mathf.Clamp01(Mathf.Max(0f, e.def.Normalized(v)));
                    var rect = (RectTransform)e.bar.transform;
                    e.barTween?.Kill();
                    if (play)
                    {
                        float from = rect.anchorMax.x;
                        e.barTween = DOVirtual.Float(from, n, 0.45f,
                                x => rect.anchorMax = new Vector2(x, 1f))
                            .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(e.bar.gameObject);
                    }
                    else rect.anchorMax = new Vector2(n, 1f);
                }

                if (play)
                {
                    bool gain = v > e.lastValue;
                    var tint = gain ? GainColor : LoseColor;

                    // 数字弹一下并染色，随后回到常态白
                    e.tween?.Kill(true);
                    e.value.color = tint;
                    e.value.transform.localScale = Vector3.one * 1.35f;
                    e.tween = DOTween.Sequence()
                        .Append(e.value.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack))
                        .Join(e.value.DOColor(new Color(0.97f, 0.97f, 1f, 1f), 0.6f))
                        .SetUpdate(true)
                        .SetLink(e.value.gameObject);

                    // 图标弹跳：把视线拉到"是哪个属性动了"
                    if (e.icon != null)
                    {
                        e.icon.transform.DOKill(true);
                        e.icon.transform.DOPunchScale(Vector3.one * 0.35f, 0.42f, 9, 0.7f)
                              .SetUpdate(true).SetLink(e.icon.gameObject);
                    }

                    SpawnFloatingDelta(e, v - e.lastValue, tint);
                }

                e.lastValue = v;
            }
        }

        /// <summary>数值滚动：20 → 23 逐格跳，比瞬间换字更容易被注意到</summary>
        void RollValue(HudEntry e, int from, int to)
        {
            e.rollTween?.Kill();
            if (Mathf.Abs(to - from) <= 1)
            {
                e.value.text = e.def.Format(to);
                return;
            }
            e.rollTween = DOVirtual.Int(from, to, 0.45f, x => e.value.text = e.def.Format(x))
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(e.value.gameObject);
        }

        /// <summary>
        /// HUD 条目上方冒一个 +3 / -2 并向上飘散。
        /// 挂在 Canvas 根而不是条目下面：HUD 皮肤 prefab 可能带裁剪（Mask/RectMask2D），
        /// 挂在条目里会被切掉一半。
        /// </summary>
        void SpawnFloatingDelta(HudEntry e, int delta, Color color)
        {
            if (delta == 0 || e.root == null || _canvas == null) return;

            var go = new GameObject("Delta",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_canvas.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(160f, 44f);

            // 条目的世界坐标 → Canvas 本地坐标（同一个 Overlay Canvas，直接换算即可）
            var canvasRect = (RectTransform)_canvas.transform;
            Vector2 local = canvasRect.InverseTransformPoint(e.root.position);
            rect.anchoredPosition = local + new Vector2(0f, 6f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = 30;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = color;
            text.text = delta > 0 ? $"+{delta}" : delta.ToString();

            rect.localScale = Vector3.one * 0.7f;
            DOTween.Sequence()
                .Append(rect.DOScale(1f, 0.22f).SetEase(Ease.OutBack))
                .Join(rect.DOAnchorPosY(local.y + 54f, 0.95f).SetEase(Ease.OutCubic))
                .Insert(0.45f, text.DOFade(0f, 0.5f))
                .OnComplete(() => { if (go != null) Destroy(go); })
                .SetUpdate(true)
                .SetLink(go);
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

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.statsPanelPrefab);
            _panelSkin = VNSystemUiSkinUtility.Instantiate<VNStatsPanelSkin>(
                skinPrefab, _canvas.transform, "VNStatsPanel");
            if (_panelSkin != null)
            {
                _panel = _panelSkin.panelRoot;
                _panelContent = _panelSkin.content;
                _panelTemplate = _panelSkin.rowTemplate;
                _panelTemplate.gameObject.SetActive(false);
                _panelSkin.titleText.text = VNLocale.T("stats.title");
                if (_panelSkin.closeButton != null) BindButton(_panelSkin.closeButton, Close);
                if (_panelSkin.backgroundCloseButton != null)
                    BindButton(_panelSkin.backgroundCloseButton, Close);
                _panel.SetActive(false);
                return;
            }

            _panel = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(_canvas.transform, false);
            Stretch(panelRect);

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

        static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void RebuildPanelList()
        {
            if (_panelContent == null) return;
            for (int i = _panelContent.childCount - 1; i >= 0; i--)
            {
                var child = _panelContent.GetChild(i);
                if (_panelTemplate != null && child == _panelTemplate.transform) continue;
                Destroy(child.gameObject);
            }

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

                if (_panelTemplate != null)
                {
                    CreateCustomPanelRow(def, v);
                    continue;
                }

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

        void CreateCustomPanelRow(VNStatDef def, int value)
        {
            var go = Instantiate(_panelTemplate.gameObject, _panelContent, false);
            go.name = def.id;
            go.SetActive(true);
            var row = go.GetComponent<VNStatsPanelRowSkin>();
            if (row.icon != null)
            {
                row.icon.sprite = def.icon != null ? def.icon : DotSprite();
                row.icon.color = def.icon != null ? Color.white : def.color;
            }
            row.nameText.text = def.DisplayName;
            string grade = def.style == VNStatStyle.Grade ? def.GradeOf(value) : "";
            row.valueText.text = string.IsNullOrEmpty(grade)
                ? def.Format(value)
                : $"{def.Format(value)}  <color=#{ColorUtility.ToHtmlStringRGB(def.color)}>{grade}</color>";

            float normalized = def.Normalized(value);
            bool hasBar = normalized >= 0f && row.barRoot != null && row.barFill != null;
            if (row.barRoot != null) row.barRoot.SetActive(hasBar);
            if (hasBar)
            {
                row.barFill.color = def.color;
                var fillRect = (RectTransform)row.barFill.transform;
                fillRect.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
            }
        }

        // ------------------------------------------------------------------
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
