using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>CG 鉴赏画廊：按分组展示已解锁 CG，并提供全屏浏览与组内翻页。</summary>
    public class VNCgGallery : MonoBehaviour
    {
        [Header("CG 目录来源（留空 = 自动查找 VNStage）")]
        public VNStage stage;

        [Header("网格布局（缩略图建议使用 16:9 横图）")]
        public int columns = 4;
        public float cellWidth = 296f;

        Canvas _canvas;
        GameObject _panel;
        RectTransform _grid;
        ScrollRect _scroll;
        TMP_Text _progress;
        TMP_Text _title;
        TMP_Text _hint;
        VNCgGallerySkin _skin;
        VNCgCellSkin _cellTemplate;

        GameObject _viewer;
        Image _viewerImage;
        TMP_Text _viewerCaption;

        bool _open;

        readonly List<List<VNStage.CgEntry>> _groups = new List<List<VNStage.CgEntry>>();
        int _viewerGroup = -1;
        int _viewerIndex;

        public bool IsOpen => _open;
        public bool IsViewerOpen => _viewer != null && _viewer.activeSelf;

        void Awake() => VNLocale.LanguageChanged += OnLanguageChanged;

        void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _panel = null;
            _grid = null;
            _scroll = null;
            _progress = null;
            _viewer = null;
            _viewerImage = null;
            _viewerCaption = null;
            _title = null;
            _hint = null;
            _skin = null;
            _cellTemplate = null;
        }

        // ==============================================================
        // ==============================================================

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        public void Open()
        {
            if (_open) return;
            Build();
            RebuildGrid();
            _panel.SetActive(true);
            _open = true;
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!_open) return;
            CloseViewer();
            _panel.SetActive(false);
            _open = false;
        }

        // ==============================================================
        // ==============================================================

        List<VNStage.CgEntry> Catalog()
        {
            if (stage == null) stage = FindFirstObjectByType<VNStage>();
            if (stage != null && stage.cgLibrary != null && stage.cgLibrary.Count > 0)
                return stage.cgLibrary;

            var cfg = VNGameConfig.Active;
            return cfg != null ? cfg.cgLibrary : new List<VNStage.CgEntry>();
        }

        void BuildGroups()
        {
            _groups.Clear();
            var index = new Dictionary<string, int>();
            foreach (var entry in Catalog())
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                string key = string.IsNullOrEmpty(entry.group) ? "\0" + entry.id : entry.group;
                if (index.TryGetValue(key, out int at))
                {
                    _groups[at].Add(entry);
                }
                else
                {
                    index[key] = _groups.Count;
                    _groups.Add(new List<VNStage.CgEntry> { entry });
                }
            }
        }

        static bool Unlocked(VNStage.CgEntry e) => e != null && VNCgUnlocks.IsUnlocked(e.id);

        static int FirstUnlocked(List<VNStage.CgEntry> group)
        {
            for (int i = 0; i < group.Count; i++)
                if (Unlocked(group[i])) return i;
            return -1;
        }

        // ==============================================================
        // ==============================================================

        void RebuildGrid()
        {
            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var child = _grid.GetChild(i);
                if (_cellTemplate != null && child == _cellTemplate.transform) continue;
                Destroy(child.gameObject);
            }

            BuildGroups();

            int total = 0, unlocked = 0;
            foreach (var g in _groups)
                foreach (var e in g)
                {
                    total++;
                    if (Unlocked(e)) unlocked++;
                }

            _progress.text = total > 0
                ? VNLocale.T("gallery.progress", unlocked, total)
                : "";

            if (_groups.Count == 0)
            {
                var empty = CreateText(_grid, 28, TextAlignmentOptions.Center);
                empty.text = VNLocale.T("gallery.empty");
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = cellWidth * columns;
                le.preferredHeight = 120f;
                return;
            }

            for (int g = 0; g < _groups.Count; g++) CreateCell(g);
        }

        void CreateCell(int groupIndex)
        {
            var group = _groups[groupIndex];
            int shown = FirstUnlocked(group);
            bool locked = shown < 0;
            CreateCustomCell(groupIndex, group, shown, locked);
        }

        void CreateCustomCell(int groupIndex, List<VNStage.CgEntry> group, int shown, bool locked)
        {
            var go = Instantiate(_cellTemplate.gameObject, _grid, false);
            go.name = "Cell_" + groupIndex;
            go.SetActive(true);
            var cell = go.GetComponent<VNCgCellSkin>();
            cell.lockedRoot.SetActive(locked);
            cell.thumbnail.sprite = locked ? null : group[shown].sprite;
            cell.thumbnail.color = locked ? new Color(0f, 0f, 0f, 0.35f) : Color.white;
            if (cell.lockedLabel != null) cell.lockedLabel.text = VNLocale.T("gallery.locked");
            if (cell.frameGraphic != null)
                cell.frameGraphic.color = locked ? cell.lockedFrameColor : cell.unlockedFrameColor;

            if (cell.countBadge != null)
            {
                int got = 0;
                foreach (var entry in group) if (Unlocked(entry)) got++;
                cell.countBadge.gameObject.SetActive(!locked && group.Count > 1);
                cell.countBadge.text = $"{got}/{group.Count}";
            }

            cell.button.onClick.RemoveAllListeners();
            cell.button.interactable = !locked;
            int captured = groupIndex;
            cell.button.onClick.AddListener(() => OpenViewer(captured));
        }

        // ==============================================================
        // ==============================================================

        void OpenViewer(int groupIndex)
        {
            if (groupIndex < 0 || groupIndex >= _groups.Count) return;
            int first = FirstUnlocked(_groups[groupIndex]);
            if (first < 0) return;

            _viewerGroup = groupIndex;
            _viewerIndex = first;
            _viewer.SetActive(true);
            _viewer.transform.SetAsLastSibling();
            ApplyViewer();
        }

        public void CloseViewer()
        {
            if (_viewer == null || !_viewer.activeSelf) return;
            _viewer.SetActive(false);
            _viewerGroup = -1;
        }

        public void ViewerNext() => StepViewer(1);
        public void ViewerPrev() => StepViewer(-1);

        void StepViewer(int dir)
        {
            if (!IsViewerOpen || _viewerGroup < 0) return;
            var group = _groups[_viewerGroup];
            if (group.Count <= 1) return;

            int i = _viewerIndex;
            for (int step = 0; step < group.Count; step++)
            {
                i = (i + dir + group.Count) % group.Count;
                if (Unlocked(group[i]))
                {
                    if (i == _viewerIndex) return;
                    _viewerIndex = i;
                    ApplyViewer();
                    return;
                }
            }
        }

        void ApplyViewer()
        {
            var group = _groups[_viewerGroup];
            var entry = group[_viewerIndex];
            _viewerImage.sprite = entry.sprite;
            _viewerImage.color = Color.white;

            _viewerImage.DOKill();
            _viewerImage.color = new Color(1f, 1f, 1f, 0.25f);
            _viewerImage.DOFade(1f, 0.18f)
                .SetLink(_viewerImage.gameObject)
                .SetUpdate(true);

            string caption = string.IsNullOrEmpty(entry.group) ? entry.id : entry.group;
            if (group.Count > 1) caption += $"  ({_viewerIndex + 1}/{group.Count})";
            _viewerCaption.text = caption;
        }

        // ==============================================================
        // ==============================================================

        void Build()
        {
            if (_panel != null) return;

            var canvasGo = new GameObject("VNCgGalleryCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.cgGalleryPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNCgGallerySkin>(
                skinPrefab, canvasGo.transform, "VNCgGallery");
            if (_skin == null)
                throw new System.InvalidOperationException("CG gallery prefab is missing or invalid.");

            BindCustomSkin(_skin);
            _panel.SetActive(false);
        }


        void BindCustomSkin(VNCgGallerySkin skin)
        {
            _panel = skin.panelRoot;
            _title = skin.titleText;
            _progress = skin.progressText;
            _hint = skin.hintText;
            _scroll = skin.scroll;
            _grid = skin.grid;
            _cellTemplate = skin.cellTemplate;
            _cellTemplate.gameObject.SetActive(false);
            _viewer = skin.viewerRoot;
            _viewerImage = skin.viewerImage;
            _viewerCaption = skin.viewerCaption;

            _title.text = VNLocale.T("gallery.title");
            if (_hint != null) _hint.text = VNLocale.T("gallery.hint");
            if (skin.closeButton != null) BindButton(skin.closeButton, Close);
            if (skin.backgroundCloseButton != null) BindButton(skin.backgroundCloseButton, Close);
            BindButton(skin.viewerCloseButton, CloseViewer);
            if (skin.viewerPreviousButton != null) BindButton(skin.viewerPreviousButton, ViewerPrev);
            if (skin.viewerNextButton != null) BindButton(skin.viewerNextButton, ViewerNext);
            _viewer.SetActive(false);
        }

        static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

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

    }
}
