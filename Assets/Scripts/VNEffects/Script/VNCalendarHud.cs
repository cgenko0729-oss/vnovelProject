using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 日程日历 HUD（养成模式右下角小面板）：显示当前月份与剩余月数。
    /// 数据全部来自 VNFlags（月份 / 剩余月数），由剧本 time 命令驱动；
    /// 「月份」flag 不存在时整个面板隐藏——纯剧情章节零干扰。
    /// UI 程序化构建在独立 Overlay Canvas 上，随 VNFlags.Changed 标脏刷新。
    /// </summary>
    public class VNCalendarHud : MonoBehaviour
    {
        public const string MonthFlag = "月份";
        public const string RemainFlag = "剩余月数";

        Canvas _canvas;
        GameObject _root;
        TextMeshProUGUI _monthText;
        TextMeshProUGUI _remainText;
        bool _dirty = true;
        bool _visible = true;

        void Awake()
        {
            VNFlags.Changed += MarkDirty;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnDestroy()
        {
            VNFlags.Changed -= MarkDirty;
            VNLocale.LanguageChanged -= OnLanguageChanged;
        }

        void MarkDirty() => _dirty = true;

        void OnLanguageChanged()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _root = null;
            _monthText = null;
            _remainText = null;
            _dirty = true;
        }

        /// <summary>右键隐藏 UI 时一起藏</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _dirty = true;
        }

        void Update()
        {
            if (!_dirty) return;
            _dirty = false;
            Refresh();
        }

        void Refresh()
        {
            bool active = _visible && VNFlags.All.ContainsKey(MonthFlag);
            if (!active)
            {
                if (_root != null) _root.SetActive(false);
                return;
            }

            Build();
            _root.SetActive(true);

            int month = Mathf.Clamp(VNFlags.Get(MonthFlag), 1, 12);
            _monthText.text = VNLocale.T("calendar.month", month);

            bool hasRemain = VNFlags.All.ContainsKey(RemainFlag);
            _remainText.gameObject.SetActive(hasRemain);
            if (hasRemain)
                _remainText.text = VNLocale.T("calendar.remain", VNFlags.Get(RemainFlag));
        }

        void Build()
        {
            if (_root != null) return;

            var canvasGo = new GameObject("VNCalendarCanvas",
                typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 578; // 与属性 HUD(580) 同档，低于各面板(600)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _root = new GameObject("Calendar", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(canvasGo.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-24f, 88f); // 避开对话框右下角
            rect.sizeDelta = new Vector2(196f, 92f);

            var bg = _root.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.018f, 0.026f, 0.052f, 0.82f);
            bg.raycastTarget = false;

            _monthText = CreateText(rect, 34);
            _monthText.fontStyle = FontStyles.Bold;
            _monthText.color = new Color(1f, 0.92f, 0.7f, 1f);
            var monthRect = (RectTransform)_monthText.transform;
            monthRect.anchorMin = new Vector2(0f, 0.42f);
            monthRect.anchorMax = new Vector2(1f, 1f);
            monthRect.offsetMin = Vector2.zero;
            monthRect.offsetMax = Vector2.zero;

            _remainText = CreateText(rect, 19);
            _remainText.color = new Color(0.78f, 0.8f, 0.88f, 1f);
            var remainRect = (RectTransform)_remainText.transform;
            remainRect.anchorMin = new Vector2(0f, 0f);
            remainRect.anchorMax = new Vector2(1f, 0.42f);
            remainRect.offsetMin = new Vector2(0f, 8f);
            remainRect.offsetMax = Vector2.zero;
        }

        TextMeshProUGUI CreateText(Transform parent, int size)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            return t;
        }
    }
}
