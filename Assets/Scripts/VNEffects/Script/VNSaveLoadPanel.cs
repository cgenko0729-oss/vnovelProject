using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 20 槽存读档界面：4×5 卡片网格，每槽显示截图、时间和最后一句台词。
    /// 全部运行时构建，不要求修改已有场景；F5/F9 由 VNScriptRunner 调用。
    /// </summary>
    public class VNSaveLoadPanel : MonoBehaviour
    {
        readonly List<Texture2D> _loadedThumbnails = new List<Texture2D>();
        readonly List<GameObject> _slotCards = new List<GameObject>();

        VNScriptRunner _runner;
        Canvas _canvas;
        GameObject _panel;
        RectTransform _grid;
        TextMeshProUGUI _title;
        TextMeshProUGUI _hint;
        Image _saveTabImage;
        Image _loadTabImage;
        GameObject _confirm;
        TextMeshProUGUI _confirmText;
        Button _confirmYes;
        Texture2D _pendingThumbnail;
        bool _open;
        bool _saveMode;

        static readonly Color CardColor = new Color(0.055f, 0.075f, 0.13f, 0.96f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.38f, 1f);

        public bool IsOpen => _open;

        public void Initialize(VNScriptRunner runner) => _runner = runner;

        public void PrepareForSaveCapture()
        {
            Build();
            _open = true;
            _panel.SetActive(false); // 截图时不能把存档 UI 自己拍进去
        }

        public void OpenSave(Texture2D thumbnail)
        {
            Build();
            ReplacePendingThumbnail(thumbnail);
            SetMode(true);
            ShowPanel();
        }

        public void OpenLoad()
        {
            Build();
            ReplacePendingThumbnail(null);
            SetMode(false);
            ShowPanel();
        }

        public void ShowLoadMode()
        {
            if (!_open) { OpenLoad(); return; }
            ReplacePendingThumbnail(null);
            SetMode(false);
            _panel.SetActive(true);
        }

        void ShowPanel()
        {
            _panel.SetActive(true);
            _open = true;
            HideConfirm();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (_panel != null) _panel.SetActive(false);
            HideConfirm();
            ClearLoadedThumbnails();
            ReplacePendingThumbnail(null);
            _runner?.OnSaveLoadPanelClosed();
        }

        void Build()
        {
            if (_panel != null) return;

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var canvasGo = new GameObject("VNSaveLoadCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("Panel", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var panelRect = (RectTransform)_panel.transform;
            panelRect.SetParent(canvasGo.transform, false);
            Stretch(panelRect);
            var panelImage = _panel.GetComponent<Image>();
            panelImage.color = new Color(0.012f, 0.018f, 0.038f, 0.975f);
            panelImage.raycastTarget = true;

            // 顶部细金线，让界面保持视觉小说的精致感。
            var topLine = CreateImage(panelRect, "TopGlow", new Color(1.4f, 0.75f, 0.2f, 0.65f));
            var topLineRect = (RectTransform)topLine.transform;
            topLineRect.anchorMin = new Vector2(0f, 1f);
            topLineRect.anchorMax = new Vector2(1f, 1f);
            topLineRect.pivot = new Vector2(0.5f, 1f);
            topLineRect.sizeDelta = new Vector2(0f, 3f);

            _title = CreateText(panelRect, "Title", 42, TextAlignmentOptions.Left);
            _title.fontStyle = FontStyles.Bold;
            var titleRect = (RectTransform)_title.transform;
            titleRect.anchorMin = titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(82f, -30f);
            titleRect.sizeDelta = new Vector2(550f, 58f);

            var saveTab = CreateButton(panelRect, "SaveTab", "保存", new Vector2(720f, -58f),
                new Vector2(180f, 54f), () => _runner?.RequestSavePanel());
            _saveTabImage = saveTab.GetComponent<Image>();
            var loadTab = CreateButton(panelRect, "LoadTab", "读取", new Vector2(914f, -58f),
                new Vector2(180f, 54f), ShowLoadMode);
            _loadTabImage = loadTab.GetComponent<Image>();

            CreateButton(panelRect, "Close", "×", new Vector2(1840f, -55f),
                new Vector2(64f, 54f), Close, 38);

            var gridGo = new GameObject("SlotGrid", typeof(RectTransform), typeof(GridLayoutGroup));
            _grid = (RectTransform)gridGo.transform;
            _grid.SetParent(panelRect, false);
            _grid.anchorMin = new Vector2(0.035f, 0.10f);
            _grid.anchorMax = new Vector2(0.965f, 0.84f);
            _grid.offsetMin = Vector2.zero;
            _grid.offsetMax = Vector2.zero;
            var layout = gridGo.GetComponent<GridLayoutGroup>();
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.cellSize = new Vector2(425f, 144f);
            layout.spacing = new Vector2(14f, 12f);
            layout.childAlignment = TextAnchor.MiddleCenter;

            _hint = CreateText(panelRect, "Hint", 23, TextAlignmentOptions.Center);
            _hint.color = new Color(0.72f, 0.78f, 0.9f, 0.9f);
            var hintRect = (RectTransform)_hint.transform;
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 28f);
            hintRect.sizeDelta = new Vector2(0f, 40f);

            BuildConfirm(panelRect);
            _panel.SetActive(false);
        }

        void SetMode(bool saveMode)
        {
            _saveMode = saveMode;
            _title.text = saveMode ? "SAVE  ·  保存游戏" : "LOAD  ·  读取游戏";
            _hint.text = saveMode
                ? "选择空槽保存；已有记录会要求确认覆盖　　F9 切换读取　·　Esc 关闭"
                : "选择一个槽位读取进度　　F5 切换保存　·　Esc 关闭";
            _saveTabImage.color = saveMode ? Gold : new Color(0.11f, 0.14f, 0.22f, 1f);
            _loadTabImage.color = saveMode ? new Color(0.11f, 0.14f, 0.22f, 1f) : Gold;
            RebuildSlots();
        }

        void RebuildSlots()
        {
            HideConfirm();
            foreach (var card in _slotCards)
            {
                if (card == null) continue;
                card.SetActive(false);
                Destroy(card);
            }
            _slotCards.Clear();
            ClearLoadedThumbnails();

            for (int slot = 1; slot <= VNSaveSystem.SlotCount; slot++)
                _slotCards.Add(CreateSlotCard(slot));
        }

        GameObject CreateSlotCard(int slot)
        {
            VNSaveData data = VNSaveSystem.Peek(slot);
            bool occupied = data != null;
            Texture2D thumbnail = occupied ? VNSaveSystem.LoadThumbnail(slot) : null;
            if (thumbnail != null) _loadedThumbnails.Add(thumbnail);

            var go = new GameObject($"Slot_{slot:00}", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(_grid, false);
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = CardColor;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            // Button 的 ColorBlock 会与目标 Image 颜色相乘，因此这里使用亮度倍率。
            colors.highlightedColor = new Color(1.5f, 1.55f, 1.7f, 1f);
            colors.pressedColor = new Color(0.72f, 0.78f, 0.92f, 1f);
            colors.disabledColor = new Color(0.4f, 0.42f, 0.48f, 0.55f);
            colors.fadeDuration = 0.12f;
            button.colors = colors;
            button.interactable = _saveMode || occupied;
            int capturedSlot = slot;
            button.onClick.AddListener(() => SelectSlot(capturedSlot, occupied));

            var thumbGo = new GameObject("Thumbnail", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(RawImage));
            var thumbRect = (RectTransform)thumbGo.transform;
            thumbRect.SetParent(go.transform, false);
            thumbRect.anchorMin = thumbRect.anchorMax = new Vector2(0f, 0.5f);
            thumbRect.pivot = new Vector2(0f, 0.5f);
            thumbRect.anchoredPosition = new Vector2(12f, 0f);
            thumbRect.sizeDelta = new Vector2(202f, 114f);
            var raw = thumbGo.GetComponent<RawImage>();
            raw.texture = thumbnail != null ? thumbnail : Texture2D.whiteTexture;
            raw.color = thumbnail != null ? Color.white : new Color(0.025f, 0.035f, 0.065f, 1f);
            raw.raycastTarget = false;

            var slotText = CreateText(go.transform, "SlotNumber", 24, TextAlignmentOptions.TopLeft);
            slotText.fontStyle = FontStyles.Bold;
            slotText.color = occupied ? Gold : new Color(0.55f, 0.6f, 0.72f, 1f);
            SetRect(slotText.rectTransform, new Vector2(226f, -11f), new Vector2(182f, 30f));
            slotText.text = $"SLOT {slot:00}";

            var timeText = CreateText(go.transform, "Time", 19, TextAlignmentOptions.TopLeft);
            timeText.color = new Color(0.72f, 0.78f, 0.88f, 1f);
            SetRect(timeText.rectTransform, new Vector2(226f, -42f), new Vector2(184f, 26f));
            timeText.text = occupied && !string.IsNullOrEmpty(data.savedAt) ? data.savedAt : "— EMPTY —";

            var lineText = CreateText(go.transform, "LastLine", 20, TextAlignmentOptions.TopLeft);
            lineText.color = occupied ? new Color(0.93f, 0.94f, 1f, 0.96f)
                                      : new Color(0.5f, 0.54f, 0.64f, 0.8f);
            lineText.textWrappingMode = TextWrappingModes.Normal;
            lineText.overflowMode = TextOverflowModes.Truncate;
            lineText.lineSpacing = -10f; // TMP 行距为字号百分比，-10 ≈ legacy 0.9 倍

            SetRect(lineText.rectTransform, new Vector2(226f, -70f), new Vector2(184f, 62f));
            lineText.text = occupied ? Truncate(data.lastLine, 42) : "空槽位";
            return go;
        }

        void SelectSlot(int slot, bool occupied)
        {
            if (_saveMode)
            {
                if (occupied)
                    ShowConfirm($"覆盖 SLOT {slot:00} 的存档？", () => SaveSlot(slot));
                else
                    SaveSlot(slot);
                return;
            }
            if (occupied)
                ShowConfirm($"读取 SLOT {slot:00}？\n当前未保存进度将会丢失。",
                    () => _runner?.LoadFromPanel(slot));
        }

        void SaveSlot(int slot)
        {
            HideConfirm();
            _runner?.SaveTo(slot, _pendingThumbnail);
            RebuildSlots();
        }

        void BuildConfirm(RectTransform parent)
        {
            _confirm = new GameObject("Confirm", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var blockerRect = (RectTransform)_confirm.transform;
            blockerRect.SetParent(parent, false);
            Stretch(blockerRect);
            _confirm.GetComponent<Image>().color = new Color(0f, 0f, 0.015f, 0.72f);

            var dialogGo = new GameObject("Dialog", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)dialogGo.transform;
            rect.SetParent(blockerRect, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(620f, 260f);
            var image = dialogGo.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.035f, 0.05f, 0.09f, 0.995f);

            _confirmText = CreateText(rect, "Message", 29, TextAlignmentOptions.Center);
            var messageRect = _confirmText.rectTransform;
            messageRect.anchorMin = new Vector2(0f, 0.42f);
            messageRect.anchorMax = new Vector2(1f, 1f);
            messageRect.offsetMin = new Vector2(30f, 0f);
            messageRect.offsetMax = new Vector2(-30f, -20f);

            _confirmYes = CreateButton(rect, "ConfirmYes", "确认", new Vector2(205f, -198f),
                new Vector2(180f, 54f), null).GetComponent<Button>();
            CreateButton(rect, "ConfirmNo", "取消", new Vector2(415f, -198f),
                new Vector2(180f, 54f), HideConfirm);
            _confirm.SetActive(false);
        }

        void ShowConfirm(string message, Action onYes)
        {
            _confirmText.text = message;
            _confirmYes.onClick.RemoveAllListeners();
            _confirmYes.onClick.AddListener(() => onYes?.Invoke());
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
        }

        void HideConfirm()
        {
            if (_confirm != null) _confirm.SetActive(false);
        }

        GameObject CreateButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, Action onClick, int fontSize = 25)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = new Color(0.11f, 0.14f, 0.22f, 1f);
            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(() => onClick());
            var text = CreateText(rect, "Label", fontSize, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.fontStyle = FontStyles.Bold;
            return go;
        }

        Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        TextMeshProUGUI CreateText(Transform parent, string name, int size, TextAlignmentOptions anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = new Color(0.95f, 0.96f, 1f, 1f);
            text.raycastTarget = false;
            return text;
        }

        static void SetRect(RectTransform rect, Vector2 topLeft, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = topLeft;
            rect.sizeDelta = size;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "（无台词记录）";
            value = value.Replace('\n', ' ').Replace('\r', ' ');
            return value.Length <= max ? value : value.Substring(0, max - 1) + "…";
        }

        void ReplacePendingThumbnail(Texture2D texture)
        {
            if (_pendingThumbnail != null && _pendingThumbnail != texture)
                Destroy(_pendingThumbnail);
            _pendingThumbnail = texture;
        }

        void ClearLoadedThumbnails()
        {
            foreach (var texture in _loadedThumbnails)
                if (texture != null) Destroy(texture);
            _loadedThumbnails.Clear();
        }

        void OnDestroy()
        {
            ClearLoadedThumbnails();
            if (_pendingThumbnail != null) Destroy(_pendingThumbnail);
        }
    }
}
