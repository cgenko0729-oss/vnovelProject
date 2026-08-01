using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>剧情回想面板：记录已读台词，并使用 Prefab 条目模板重建滚动列表。</summary>
    public class VNBacklog : MonoBehaviour
    {
        [Header("最大记录条数")]
        public int maxEntries = 200;

        struct Entry
        {
            public string name;
            public string text;
        }

        readonly List<Entry> _entries = new List<Entry>();

        Canvas _canvas;
        GameObject _panel;
        RectTransform _content;
        ScrollRect _scroll;
        VNBacklogSkin _skin;
        VNBacklogEntrySkin _entryTemplate;
        bool _open;

        public bool IsOpen => _open;

        void Awake() => VNLocale.LanguageChanged += OnLanguageChanged;

        void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _content = null;
            _scroll = null;
            _skin = null;
            _entryTemplate = null;
        }

        public void Record(string displayName, string text)
        {
            _entries.Add(new Entry { name = displayName, text = text });
            if (_entries.Count > maxEntries)
                _entries.RemoveAt(0);
        }

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open) return;
            Build();
            RebuildList();
            _panel.SetActive(true);
            _open = true;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
        }

        public void Close()
        {
            if (!_open) return;
            _panel.SetActive(false);
            _open = false;
        }

        // ------------------------------------------------------------------

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNBacklogCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.backlogPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNBacklogSkin>(
                skinPrefab, canvasGo.transform, "VNBacklog");
            if (_skin == null)
                throw new System.InvalidOperationException("Backlog prefab is missing or invalid.");

            BindCustomSkin(_skin);
            _panel.SetActive(false);
        }


        void BindCustomSkin(VNBacklogSkin skin)
        {
            _panel = skin.panelRoot;
            _scroll = skin.scroll;
            _content = skin.content;
            _entryTemplate = skin.entryTemplate;
            _entryTemplate.gameObject.SetActive(false);
            skin.titleText.text = VNLocale.T("backlog.title");
            if (skin.closeButton != null) BindButton(skin.closeButton, Close);
            if (skin.backgroundCloseButton != null) BindButton(skin.backgroundCloseButton, Close);
        }

        static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        void RebuildList()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i);
                if (child == _entryTemplate.transform) continue;
                Destroy(child.gameObject);
            }

            foreach (var entryData in _entries)
            {
                var go = Instantiate(_entryTemplate.gameObject, _content, false);
                go.SetActive(true);
                var entry = go.GetComponent<VNBacklogEntrySkin>();
                bool hasSpeaker = !string.IsNullOrEmpty(entryData.name);
                entry.speakerText.gameObject.SetActive(hasSpeaker);
                entry.speakerText.text = hasSpeaker ? entryData.name : "";
                entry.bodyText.text = entryData.text;
            }
        }

    }
}
