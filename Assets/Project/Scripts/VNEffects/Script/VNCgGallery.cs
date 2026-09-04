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

        // ---- 照片页（大头贴拍的照片，与 CG 共用同一套网格与全屏浏览）----
        // ---- 私密页（偷拍模式拍的，存储完全独立；解锁后或相册里有照片时才出现）----
        enum Page { Cg, Photo, Secret }
        Page _page = Page.Cg;

        /// <summary>照片页 / 私密页共用的一条：两个相册的条目类型不同，这里抹平成文件名 + 说明</summary>
        struct PhotoItem
        {
            public string file;
            public string caption;
        }
        readonly List<PhotoItem> _photos = new List<PhotoItem>();
        int _photoIndex = -1;
        Button _cgTab, _photoTab, _secretTab;
        TMP_Text _cgTabText, _photoTabText, _secretTabText;
        bool IsPhotoPage => _page != Page.Cg;
        GameObject _deleteButtonRoot, _confirmRoot;
        Vector2 _cgCellSize;
        GridLayoutGroup _gridLayout;

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
            // 私密页的出现条件每次打开重算（解锁 flag 会变）；正停在私密页却不该看见时退回 CG 页
            bool secretVisible = VNSecretPhotoMode.AlbumVisible;
            if (_secretTab != null) _secretTab.gameObject.SetActive(secretVisible);
            if (_page == Page.Secret && !secretVisible) _page = Page.Cg;
            RefreshTabs();
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
            HideConfirm();
            _panel.SetActive(false);
            _open = false;
            // 相册的纹理与缩略图都在这里放掉——关了界面就没有必要再占内存
            VNPhotoAlbum.ClearCache();
            VNSecretAlbum.ClearCache();
        }

        /// <summary>切到 CG 页 / 照片页</summary>
        void SelectPage(Page page)
        {
            if (_page == page && _grid.childCount > 0) return;
            _page = page;
            CloseViewer();
            HideConfirm();
            RefreshTabs();
            RebuildGrid();
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        void RefreshTabs()
        {
            if (_cgTab == null || _photoTab == null) return;
            PaintTab(_cgTab, _cgTabText, _page == Page.Cg);
            PaintTab(_photoTab, _photoTabText, _page == Page.Photo);
            PaintTab(_secretTab, _secretTabText, _page == Page.Secret);
        }

        static void PaintTab(Button tab, TMP_Text text, bool on)
        {
            if (tab == null) return;
            if (tab.targetGraphic is Image image) image.color = on ? TabOn : TabOff;
            if (text != null) text.color = on ? Color.white : TabTextOff;
        }

        static readonly Color TabOn = new Color(0.98f, 0.62f, 0.76f, 1f);
        static readonly Color TabOff = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color TabTextOff = new Color(1f, 1f, 1f, 0.7f);

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
                // ★ 必须先脱离父节点再销毁：Destroy 要等到帧末才真正执行，
                //   在那之前旧格子仍挂在 GridLayoutGroup 下参与布局——
                //   切页时会看到上一页的格子和新格子挤在一起。
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            if (IsPhotoPage) { RebuildPhotoGrid(); return; }

            // CG 页恢复原本的格子尺寸（照片页会把它改成 4:3）
            if (_gridLayout != null && _cgCellSize != Vector2.zero)
                _gridLayout.cellSize = _cgCellSize;

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

        // ==============================================================
        // 照片页
        // ==============================================================

        bool Secret => _page == Page.Secret;

        Sprite LoadThumb(string file) =>
            Secret ? VNSecretAlbum.LoadThumbnail(file) : VNPhotoAlbum.LoadThumbnail(file);

        Sprite LoadFull(string file) =>
            Secret ? VNSecretAlbum.LoadSprite(file) : VNPhotoAlbum.LoadSprite(file);

        void DeleteFile(string file)
        {
            if (Secret) VNSecretAlbum.Delete(file);
            else VNPhotoAlbum.Delete(file);
        }

        /// <summary>私密照片的说明：拍摄时间 + 角色 + 缩放 + 背景（拍摄信息来自相册索引）</summary>
        static string SecretCaption(VNSecretAlbum.Entry e)
        {
            string who = string.IsNullOrEmpty(e.character) ? "" : e.character;
            var cfg = VNGameConfig.Active;
            if (cfg != null && !string.IsNullOrEmpty(who))
            {
                var def = cfg.characters.Find(c => c != null && c.id == who);
                if (def != null) who = def.LocalizedDisplayName;
            }
            var sb = new System.Text.StringBuilder();
            sb.Append(e.Time.ToString("yyyy/MM/dd HH:mm"));
            if (!string.IsNullOrEmpty(who)) sb.Append("    ").Append(who);
            sb.Append("    ").Append(e.Zoom.ToString("0.0")).Append('x');
            if (!string.IsNullOrEmpty(e.background)) sb.Append("    ").Append(e.background);
            return sb.ToString();
        }

        void RebuildPhotoGrid()
        {
            _photos.Clear();
            if (Secret)
            {
                foreach (var e in VNSecretAlbum.All)
                    if (e != null) _photos.Add(new PhotoItem { file = e.file, caption = SecretCaption(e) });
            }
            else
            {
                foreach (var e in VNPhotoAlbum.All)
                    if (e != null) _photos.Add(new PhotoItem
                    {
                        file = e.file,
                        caption = e.Time.ToString("yyyy/MM/dd HH:mm"),
                    });
            }

            // 大头贴是 4:3、偷拍是整屏 16:9、CG 是 16:9——同一套网格，切页时换格子尺寸
            if (_gridLayout != null)
            {
                if (_cgCellSize == Vector2.zero) _cgCellSize = _gridLayout.cellSize;
                _gridLayout.cellSize = Secret ? _cgCellSize
                    : new Vector2(_cgCellSize.x, _cgCellSize.x * 0.75f);
            }

            _progress.text = Secret
                ? VNLocale.T("gallery.secretCount", _photos.Count, VNSecretAlbum.Capacity)
                : VNLocale.T("gallery.photoCount", _photos.Count, VNPhotoAlbum.Capacity);

            if (_photos.Count == 0)
            {
                var empty = CreateText(_grid, 28, TextAlignmentOptions.Center);
                empty.text = VNLocale.T(Secret ? "gallery.secretEmpty" : "gallery.photoEmpty");
                empty.color = new Color(1f, 1f, 1f, 0.55f);
                var le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = cellWidth * columns;
                le.preferredHeight = 120f;
                return;
            }

            for (int i = 0; i < _photos.Count; i++) CreatePhotoCell(i);
        }

        void CreatePhotoCell(int index)
        {
            var go = Instantiate(_cellTemplate.gameObject, _grid, false);
            go.name = "Photo_" + index;
            go.SetActive(true);
            var cell = go.GetComponent<VNCgCellSkin>();

            if (cell.lockedRoot != null) cell.lockedRoot.SetActive(false);
            if (cell.countBadge != null) cell.countBadge.gameObject.SetActive(false);
            if (cell.frameGraphic != null) cell.frameGraphic.color = cell.unlockedFrameColor;

            cell.thumbnail.sprite = LoadThumb(_photos[index].file);
            cell.thumbnail.color = Color.white;
            cell.thumbnail.preserveAspect = true;

            cell.button.onClick.RemoveAllListeners();
            cell.button.interactable = true;
            int captured = index;
            cell.button.onClick.AddListener(() => OpenPhotoViewer(captured));
        }

        void OpenPhotoViewer(int index)
        {
            if (index < 0 || index >= _photos.Count) return;
            _photoIndex = index;
            _viewerGroup = -1;
            _viewer.SetActive(true);
            _viewer.transform.SetAsLastSibling();
            ApplyPhotoViewer();
        }

        void ApplyPhotoViewer()
        {
            var entry = _photos[_photoIndex];
            _viewerImage.sprite = LoadFull(entry.file);
            _viewerImage.preserveAspect = true;

            _viewerImage.DOKill();
            _viewerImage.color = new Color(1f, 1f, 1f, 0.25f);
            _viewerImage.DOFade(1f, 0.18f)
                .SetLink(_viewerImage.gameObject)
                .SetUpdate(true);

            _viewerCaption.text = $"{entry.caption}    ({_photoIndex + 1}/{_photos.Count})";
            if (_deleteButtonRoot != null) _deleteButtonRoot.SetActive(true);
        }

        void StepPhotoViewer(int dir)
        {
            if (_photos.Count <= 1) return;
            _photoIndex = (_photoIndex + dir + _photos.Count) % _photos.Count;
            ApplyPhotoViewer();
        }

        /// <summary>删掉当前正在看的这张（磁盘 PNG 一并删）。误删不可逆，所以要确认。</summary>
        void DeleteCurrentPhoto()
        {
            HideConfirm();
            if (_photoIndex < 0 || _photoIndex >= _photos.Count) return;

            string file = _photos[_photoIndex].file;
            DeleteFile(file);

            CloseViewer();
            RebuildGrid();
            Canvas.ForceUpdateCanvases();
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
            _photoIndex = -1;
            HideConfirm();
            if (_deleteButtonRoot != null) _deleteButtonRoot.SetActive(false);
        }

        public void ViewerNext() => StepViewer(1);
        public void ViewerPrev() => StepViewer(-1);

        void StepViewer(int dir)
        {
            if (!IsViewerOpen) return;
            if (IsPhotoPage) { StepPhotoViewer(dir); return; }
            if (_viewerGroup < 0) return;
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
            BuildPhotoExtras();
            _panel.SetActive(false);
        }

        /// <summary>
        /// 照片页需要的三样东西（标签条 / 删除按钮 / 删除确认）。
        /// 皮肤 prefab 里没有这些槽位，所以在这里程序化补——
        /// 老 prefab 不必重新导出，符合项目「单项缺失只退回该项程序化 UI」的规则。
        /// </summary>
        void BuildPhotoExtras()
        {
            var panelRect = _panel.transform as RectTransform;
            if (panelRect == null) return;

            _gridLayout = _grid.GetComponent<GridLayoutGroup>();
            if (_gridLayout != null) _cgCellSize = _gridLayout.cellSize;

            // ---- 顶部标签条（标题与进度都从 x=600 开始，左上这块是空的）----
            _cgTab = CreateTabButton(panelRect, "TabCg", new Vector2(40f, -26f),
                VNLocale.T("gallery.tab.cg"), out _cgTabText);
            _photoTab = CreateTabButton(panelRect, "TabPhoto", new Vector2(180f, -26f),
                VNLocale.T("gallery.tab.photo"), out _photoTabText);
            _secretTab = CreateTabButton(panelRect, "TabSecret", new Vector2(320f, -26f),
                VNLocale.T("gallery.tab.secret"), out _secretTabText);
            _cgTab.onClick.AddListener(() => SelectPage(Page.Cg));
            _photoTab.onClick.AddListener(() => SelectPage(Page.Photo));
            _secretTab.onClick.AddListener(() => SelectPage(Page.Secret));
            _secretTab.gameObject.SetActive(false); // Open() 里按解锁状态决定
            RefreshTabs();

            // ---- 全屏里的删除按钮（只在照片页显示）----
            var viewerRect = _viewer.transform as RectTransform;
            if (viewerRect != null)
            {
                var deleteButton = CreateSimpleButton(viewerRect, "DeletePhoto",
                    new Vector2(0f, 1f), new Vector2(150f, -26f), new Vector2(180f, 52f),
                    VNLocale.T("gallery.photoDelete"),
                    new Color(0.75f, 0.26f, 0.32f, 0.95f), out _);
                deleteButton.onClick.AddListener(ShowConfirm);
                _deleteButtonRoot = deleteButton.gameObject;
                _deleteButtonRoot.SetActive(false);
            }

            BuildConfirm(panelRect);
        }

        void BuildConfirm(RectTransform parent)
        {
            var dim = new GameObject("DeleteConfirm", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)dim.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var dimImage = dim.GetComponent<Image>();
            dimImage.color = new Color(0f, 0f, 0f, 0.72f);
            dimImage.raycastTarget = true;
            _confirmRoot = dim;

            var box = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var boxRect = (RectTransform)box.transform;
            boxRect.SetParent(rect, false);
            boxRect.sizeDelta = new Vector2(720f, 260f);
            box.GetComponent<Image>().color = new Color(0.14f, 0.13f, 0.17f, 0.98f);

            var text = CreateText(boxRect, 30, TextAlignmentOptions.Center);
            text.text = VNLocale.T("gallery.photoConfirm");
            var textRect = (RectTransform)text.transform;
            textRect.sizeDelta = new Vector2(660f, 110f);
            textRect.anchoredPosition = new Vector2(0f, 40f);

            CreateSimpleButton(boxRect, "Cancel", new Vector2(0.5f, 0.5f),
                new Vector2(-130f, -60f), new Vector2(220f, 66f),
                VNLocale.T("gallery.photoCancel"),
                new Color(0.3f, 0.32f, 0.4f, 1f), out _).onClick.AddListener(HideConfirm);

            CreateSimpleButton(boxRect, "Confirm", new Vector2(0.5f, 0.5f),
                new Vector2(130f, -60f), new Vector2(220f, 66f),
                VNLocale.T("gallery.photoDelete"),
                new Color(0.78f, 0.26f, 0.32f, 1f), out _)
                .onClick.AddListener(DeleteCurrentPhoto);

            dim.SetActive(false);
        }

        void ShowConfirm()
        {
            if (_confirmRoot == null) return;
            _confirmRoot.transform.SetAsLastSibling();
            _confirmRoot.SetActive(true);
        }

        void HideConfirm()
        {
            if (_confirmRoot != null) _confirmRoot.SetActive(false);
        }

        Button CreateTabButton(RectTransform parent, string name, Vector2 pos,
            string label, out TMP_Text text) =>
            CreateSimpleButton(parent, name, new Vector2(0f, 1f), pos,
                new Vector2(130f, 46f), label, TabOff, out text);

        Button CreateSimpleButton(RectTransform parent, string name, Vector2 anchor,
            Vector2 pos, Vector2 size, string label, Color color, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = pos + new Vector2(size.x * 0.5f, 0f) *
                                    (anchor.x < 0.5f ? 1f : 0f);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            text = CreateText(rect, 24, TextAlignmentOptions.Center);
            text.text = label;
            var textRect = (RectTransform)text.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
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
