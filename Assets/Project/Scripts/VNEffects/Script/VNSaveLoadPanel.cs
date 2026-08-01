using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>存读档面板：使用 Prefab 卡片模板展示存档截图、时间与最后一句台词。</summary>
    public class VNSaveLoadPanel : MonoBehaviour
    {
        readonly List<Texture2D> _loadedThumbnails = new List<Texture2D>();
        readonly List<GameObject> _slotCards = new List<GameObject>();

        VNScriptRunner _runner;
        Canvas _canvas;
        GameObject _panel;
        RectTransform _grid;
        TMP_Text _title;
        TMP_Text _hint;
        Graphic _saveTabImage;
        Graphic _loadTabImage;
        VNSaveLoadSkin _skin;
        VNSaveSlotSkin _slotTemplate;
        GameObject _confirm;
        TMP_Text _confirmText;
        Button _confirmYes;
        Texture2D _pendingThumbnail;
        bool _open;
        bool _saveMode;


        public bool IsOpen => _open;

        public void Initialize(VNScriptRunner runner)
        {
            _runner = runner;
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _title = null;
            _hint = null;
            _saveTabImage = null;
            _loadTabImage = null;
            _confirm = null;
            _confirmText = null;
            _confirmYes = null;
            _skin = null;
            _slotTemplate = null;
            _slotCards.Clear();
        }

        public void PrepareForSaveCapture()
        {
            Build();
            _open = true;
            _panel.SetActive(false);
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

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.saveLoadPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNSaveLoadSkin>(
                skinPrefab, canvasGo.transform, "VNSaveLoadPanel");
            if (_skin == null)
                throw new InvalidOperationException("Save/load prefab is missing or invalid.");

            BindCustomSkin(_skin);
            _panel.SetActive(false);
        }


        void BindCustomSkin(VNSaveLoadSkin skin)
        {
            _panel = skin.panelRoot;
            _title = skin.titleText;
            _hint = skin.hintText;
            _grid = skin.slotContainer;
            _slotTemplate = skin.slotTemplate;
            _slotTemplate.gameObject.SetActive(false);
            _saveTabImage = skin.saveTab.targetGraphic;
            _loadTabImage = skin.loadTab.targetGraphic;

            BindButton(skin.saveTab, () => _runner?.RequestSavePanel());
            BindButton(skin.loadTab, ShowLoadMode);
            BindButton(skin.closeButton, Close);
            if (skin.saveTabLabel != null) skin.saveTabLabel.text = VNLocale.T("save.tabSave");
            if (skin.loadTabLabel != null) skin.loadTabLabel.text = VNLocale.T("save.tabLoad");

            _confirm = skin.confirmRoot;
            _confirmText = skin.confirmMessage;
            _confirmYes = skin.confirmYes;
            BindButton(skin.confirmNo, HideConfirm);
            if (skin.confirmYesLabel != null) skin.confirmYesLabel.text = VNLocale.T("common.confirm");
            if (skin.confirmNoLabel != null) skin.confirmNoLabel.text = VNLocale.T("common.cancel");
            _confirm.SetActive(false);
        }

        static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void SetMode(bool saveMode)
        {
            _saveMode = saveMode;
            _title.text = saveMode ? VNLocale.T("save.titleSave") : VNLocale.T("save.titleLoad");
            _hint.text = saveMode ? VNLocale.T("save.hintSave") : VNLocale.T("save.hintLoad");
            Color selected = _skin.selectedTabColor;
            Color normal = _skin.normalTabColor;
            if (_saveTabImage != null) _saveTabImage.color = saveMode ? selected : normal;
            if (_loadTabImage != null) _loadTabImage.color = saveMode ? normal : selected;
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
            return CreateCustomSlotCard(slot, data, occupied, thumbnail);
        }

        GameObject CreateCustomSlotCard(int slot, VNSaveData data, bool occupied, Texture2D thumbnail)
        {
            var go = Instantiate(_slotTemplate.gameObject, _grid, false);
            go.name = $"Slot_{slot:00}";
            go.SetActive(true);
            var card = go.GetComponent<VNSaveSlotSkin>();

            card.thumbnail.texture = thumbnail != null ? thumbnail : Texture2D.whiteTexture;
            card.thumbnail.color = thumbnail != null ? Color.white : card.emptyColor;
            card.slotNumber.text = $"SLOT {slot:00}";
            card.slotNumber.color = occupied ? card.occupiedNumberColor : card.emptyNumberColor;
            card.savedAt.text = occupied && !string.IsNullOrEmpty(data.savedAt)
                ? data.savedAt : "— EMPTY —";
            card.lastLine.text = occupied ? Truncate(data.lastLine, 42) : VNLocale.T("save.emptySlot");
            if (card.cardGraphic != null)
                card.cardGraphic.color = occupied ? card.occupiedColor : card.emptyColor;

            card.button.onClick.RemoveAllListeners();
            card.button.interactable = _saveMode || occupied;
            int capturedSlot = slot;
            card.button.onClick.AddListener(() => SelectSlot(capturedSlot, occupied));
            return go;
        }

        void SelectSlot(int slot, bool occupied)
        {
            if (_saveMode)
            {
                if (occupied)
                    ShowConfirm(VNLocale.T("save.confirmOverwrite", slot), () => SaveSlot(slot));
                else
                    SaveSlot(slot);
                return;
            }
            if (occupied)
                ShowConfirm(VNLocale.T("save.confirmLoad", slot),
                    () => _runner?.LoadFromPanel(slot));
        }

        void SaveSlot(int slot)
        {
            HideConfirm();
            _runner?.SaveTo(slot, _pendingThumbnail);
            RebuildSlots();
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

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value)) return VNLocale.T("save.noLine");
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
            VNLocale.LanguageChanged -= OnLanguageChanged;
            ClearLoadedThumbnails();
            if (_pendingThumbnail != null) Destroy(_pendingThumbnail);
        }
    }
}
