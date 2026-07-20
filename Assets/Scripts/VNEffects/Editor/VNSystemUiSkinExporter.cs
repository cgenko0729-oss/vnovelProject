using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VNEffects.EditorTools;

namespace VNEffects
{
    /// <summary>把系统 UI 默认外观导出为可直接编辑的 prefab，并登记为唯一全局主题。</summary>
    public static class VNSystemUiSkinExporter
    {
        const string Dir = "Assets/VNEffects/SystemUISkins";
        const string SetPath = Dir + "/VNSystemUiSkinSet_Default.asset";
        const string RoundedPath = "Assets/VNEffects/UISkins/Textures/VN_RoundedRect.png";
        static readonly Color Panel = new Color(0.025f, 0.038f, 0.075f, 0.98f);
        static readonly Color ButtonColor = new Color(0.07f, 0.09f, 0.16f, 0.96f);
        static readonly Color Gold = new Color(1f, 0.78f, 0.38f, 1f);
        static Sprite _rounded;
        static TMP_FontAsset _font;

        [MenuItem("Tools/VN Effects/System UI Skins/Export Default Prefabs")]
        public static void ExportAll()
        {
            EnsureFolder(Dir);
            _rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
            _font = VNFontAssetBuilder.EnsureFontAsset();

            var set = AssetDatabase.LoadAssetAtPath<VNSystemUiSkinSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<VNSystemUiSkinSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            set.titleMenuPrefab = BuildTitle();
            set.configPanelPrefab = BuildConfig();
            set.quickToolbarPrefab = BuildToolbar();
            set.saveLoadPrefab = BuildSaveLoad();
            set.cgGalleryPrefab = BuildGallery();
            set.backlogPrefab = BuildBacklog();
            set.statsHudPrefab = BuildStatsHud();
            set.statsPanelPrefab = BuildStatsPanel();
            set.inventoryPrefab = BuildInventory();
            set.planPrefab = BuildPlan();
            set.resultPopupPrefab = BuildResultPopup();
            EditorUtility.SetDirty(set);

            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg != null)
            {
                cfg.systemUiSkin = set;
                EditorUtility.SetDirty(cfg);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateAllInternal(set);
            Selection.activeObject = set;
            EditorGUIUtility.PingObject(set);
            Debug.Log($"[VNSystemUiSkin] 默认系统 UI prefab 已导出到 {Dir}，并登记为全局主题。" +
                      "复制 prefab 后修改图片、锚点和布局即可；必需槽位缺失时运行时自动退回程序化 UI。");
        }

        /// <summary>只导出排程面板与结算弹窗两个默认 prefab，不碰其余 9 个
        /// （已有主题里手改过的 prefab 不会被覆盖）。</summary>
        [MenuItem("Tools/VN Effects/System UI Skins/Export Event Panel Prefabs (Plan & Result)")]
        public static void ExportEventPanels()
        {
            var set = AssetDatabase.LoadAssetAtPath<VNSystemUiSkinSet>(SetPath);
            if (set == null)
            {
                Debug.LogError($"[VNSystemUiSkin] 找不到全局主题资产 {SetPath}，" +
                               "请先运行 Export Default Prefabs。");
                return;
            }

            EnsureFolder(Dir);
            _rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedPath);
            _font = VNFontAssetBuilder.EnsureFontAsset();

            set.planPrefab = BuildPlan();
            set.resultPopupPrefab = BuildResultPopup();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(set);
            Debug.Log("[VNSystemUiSkin] 排程面板与结算弹窗默认 prefab 已导出并登记" +
                      "（其余系统 UI prefab 未改动）。");
        }

        [MenuItem("Tools/VN Effects/System UI Skins/Validate Global Theme")]
        public static void ValidateAll()
        {
            var set = AssetDatabase.LoadAssetAtPath<VNSystemUiSkinSet>(SetPath);
            if (set == null) throw new System.InvalidOperationException($"找不到系统 UI 全局主题：{SetPath}");

            ValidateAllInternal(set);
            Debug.Log("[VNSystemUiSkin] 全局主题和 11 个 prefab 的必需槽位校验通过。");
        }

        static void ValidateAllInternal(VNSystemUiSkinSet set)
        {
            ValidatePrefab<VNTitleMenuSkin>(set.titleMenuPrefab, "标题菜单");
            ValidatePrefab<VNConfigPanelSkin>(set.configPanelPrefab, "设置菜单");
            ValidatePrefab<VNQuickToolbarSkin>(set.quickToolbarPrefab, "快捷功能条");
            ValidatePrefab<VNSaveLoadSkin>(set.saveLoadPrefab, "存读档菜单");
            ValidatePrefab<VNCgGallerySkin>(set.cgGalleryPrefab, "CG 鉴赏");
            ValidatePrefab<VNBacklogSkin>(set.backlogPrefab, "Backlog");
            ValidatePrefab<VNStatsHudSkin>(set.statsHudPrefab, "顶部属性 HUD");
            ValidatePrefab<VNStatsPanelSkin>(set.statsPanelPrefab, "完整属性页");
            ValidatePrefab<VNInventorySkin>(set.inventoryPrefab, "背包");
            ValidatePrefab<VNPlanSkin>(set.planPrefab, "排程面板");
            ValidatePrefab<VNResultPopupSkin>(set.resultPopupPrefab, "结算弹窗");

            var config = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (config != null && config.systemUiSkin != set)
                throw new System.InvalidOperationException("VNGameConfig 没有引用当前系统 UI 全局主题。");
        }

        static void ValidatePrefab<T>(GameObject prefab, string displayName)
            where T : VNSystemUiSkinBehaviour
        {
            if (prefab == null)
                throw new System.InvalidOperationException($"系统 UI 全局主题缺少 {displayName} prefab。");

            var skin = prefab.GetComponent<T>();
            if (skin == null)
                throw new System.InvalidOperationException($"{displayName} prefab 缺少 {typeof(T).Name}。");
            if (!skin.IsValid(out string error))
                throw new System.InvalidOperationException($"{displayName} prefab 缺少必需槽位：{error}。");
        }

        static GameObject BuildTitle()
        {
            var root = Root("TitleMenu_Default");
            var group = root.AddComponent<CanvasGroup>();
            var skin = root.AddComponent<VNTitleMenuSkin>();
            skin.canvasGroup = group;
            FullImage(root.transform, "Dim", new Color(0f, 0f, 0.03f, 0.42f));
            skin.gameTitle = Text(root.transform, "GameTitle", 108, TextAlignmentOptions.Left,
                new Vector2(118, -150), new Vector2(1400, 150));
            skin.titleAnimationTarget = skin.gameTitle.rectTransform;
            skin.versionText = Text(root.transform, "Version", 20, TextAlignmentOptions.BottomRight,
                new Vector2(1560, -1018), new Vector2(320, 34));

            var menu = Node(root.transform, "Buttons", typeof(VerticalLayoutGroup));
            SetRect((RectTransform)menu.transform, new Vector2(118, -420), new Vector2(440, 450));
            var layout = menu.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14; layout.childControlHeight = false; layout.childForceExpandHeight = false;
            (skin.startButton, skin.startLabel) = MenuButton(menu.transform, "Start");
            (skin.continueButton, skin.continueLabel) = MenuButton(menu.transform, "Continue");
            skin.continueTimeText = Text(skin.continueButton.transform, "SavedAt", 16,
                TextAlignmentOptions.Right, new Vector2(210, -15), new Vector2(190, 32));
            (skin.loadButton, skin.loadLabel) = MenuButton(menu.transform, "Load");
            (skin.galleryButton, skin.galleryLabel) = MenuButton(menu.transform, "Gallery");
            (skin.configButton, skin.configLabel) = MenuButton(menu.transform, "Config");
            (skin.quitButton, skin.quitLabel) = MenuButton(menu.transform, "Quit");

            skin.quitConfirmRoot = FullImage(root.transform, "QuitConfirm", new Color(0, 0, 0.02f, .78f)).gameObject;
            var dialog = Image(skin.quitConfirmRoot.transform, "Dialog", Panel);
            SetCenter(dialog.rectTransform, new Vector2(620, 250));
            skin.quitConfirmMessage = Text(dialog.transform, "Message", 29, TextAlignmentOptions.Center,
                new Vector2(30, -28), new Vector2(560, 100));
            (skin.quitConfirmButton, skin.quitConfirmLabel) = FixedButton(dialog.transform, "Confirm",
                new Vector2(100, -165), new Vector2(180, 54));
            (skin.quitCancelButton, skin.quitCancelLabel) = FixedButton(dialog.transform, "Cancel",
                new Vector2(340, -165), new Vector2(180, 54));
            skin.quitConfirmRoot.SetActive(false);
            return Save(root);
        }

        static GameObject BuildConfig()
        {
            var root = Root("ConfigPanel_Default");
            var skin = root.AddComponent<VNConfigPanelSkin>();
            skin.panelRoot = root;
            skin.backgroundCloseButton = FullButton(root.transform, "Dim", new Color(0, 0, .02f, .82f));
            var window = Image(root.transform, "Window", Panel);
            SetCenter(window.rectTransform, new Vector2(780, 740));
            skin.titleText = Text(window.transform, "Title", 38, TextAlignmentOptions.Left,
                new Vector2(58, -34), new Vector2(560, 56));
            (skin.closeButton, _) = FixedButton(window.transform, "Close", new Vector2(700, -34), new Vector2(48, 48), "×");
            BuildSliderRow(window.transform, "Bgm", 126, out skin.bgmLabel, out skin.bgmSlider, out skin.bgmValue);
            BuildSliderRow(window.transform, "Se", 212, out skin.seLabel, out skin.seSlider, out skin.seValue);
            BuildSliderRow(window.transform, "Voice", 298, out skin.voiceLabel, out skin.voiceSlider, out skin.voiceValue);
            BuildSliderRow(window.transform, "TextSpeed", 384, out skin.textSpeedLabel, out skin.textSpeedSlider, out skin.textSpeedValue);
            skin.languageLabel = Text(window.transform, "LanguageLabel", 24, TextAlignmentOptions.Left,
                new Vector2(82, -470), new Vector2(170, 42));
            (skin.chineseButton, skin.chineseLabel) = FixedButton(window.transform, "Chinese", new Vector2(260, -474), new Vector2(140, 50));
            (skin.englishButton, skin.englishLabel) = FixedButton(window.transform, "English", new Vector2(408, -474), new Vector2(140, 50));
            (skin.japaneseButton, skin.japaneseLabel) = FixedButton(window.transform, "Japanese", new Vector2(556, -474), new Vector2(140, 50));
            (skin.fullscreenButton, skin.fullscreenLabel) = FixedButton(window.transform, "Fullscreen", new Vector2(82, -584), new Vector2(616, 58));
            skin.hintText = Text(window.transform, "Hint", 19, TextAlignmentOptions.Center,
                new Vector2(80, -668), new Vector2(620, 34));
            return Save(root);
        }

        static GameObject BuildToolbar()
        {
            var root = Node(null, "QuickToolbar_Default", typeof(Image), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(1013, 42);
            var image = root.GetComponent<Image>(); image.sprite = _rounded; image.type = UnityEngine.UI.Image.Type.Sliced; image.color = Panel;
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(7, 7, 5, 5); layout.spacing = 5;
            layout.childControlWidth = false; layout.childForceExpandWidth = false;
            var skin = root.AddComponent<VNQuickToolbarSkin>(); skin.root = rect;
            foreach (VNToolbarAction action in System.Enum.GetValues(typeof(VNToolbarAction)))
            {
                var pair = LayoutButton(root.transform, action.ToString(), action == VNToolbarAction.HideUi ? 100 : 72);
                var slot = pair.Item1.gameObject.AddComponent<VNToolbarActionSlot>();
                slot.action = action; slot.button = pair.Item1; slot.label = pair.Item2;
                slot.activeGraphic = pair.Item1.targetGraphic;
            }
            return Save(root);
        }

        static GameObject BuildSaveLoad()
        {
            var root = Root("SaveLoad_Default");
            root.AddComponent<Image>().color = new Color(.012f, .018f, .038f, .975f);
            var skin = root.AddComponent<VNSaveLoadSkin>(); skin.panelRoot = root;
            skin.titleText = Text(root.transform, "Title", 42, TextAlignmentOptions.Left,
                new Vector2(82, -30), new Vector2(550, 58));
            (skin.saveTab, skin.saveTabLabel) = FixedButton(root.transform, "SaveTab", new Vector2(720, -58), new Vector2(180, 54));
            (skin.loadTab, skin.loadTabLabel) = FixedButton(root.transform, "LoadTab", new Vector2(914, -58), new Vector2(180, 54));
            (skin.closeButton, _) = FixedButton(root.transform, "Close", new Vector2(1840, -55), new Vector2(64, 54), "×");
            var grid = Node(root.transform, "SlotGrid", typeof(GridLayoutGroup));
            var gridRect = (RectTransform)grid.transform;
            gridRect.anchorMin = new Vector2(.035f, .10f); gridRect.anchorMax = new Vector2(.965f, .84f);
            gridRect.offsetMin = gridRect.offsetMax = Vector2.zero;
            var gl = grid.GetComponent<GridLayoutGroup>(); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 4; gl.cellSize = new Vector2(425, 144); gl.spacing = new Vector2(14, 12);
            skin.slotContainer = gridRect;
            skin.slotTemplate = BuildSaveSlot(grid.transform); skin.slotTemplate.gameObject.SetActive(false);
            skin.hintText = Text(root.transform, "Hint", 23, TextAlignmentOptions.Center,
                new Vector2(500, -1015), new Vector2(920, 40));
            BuildConfirm(root.transform, skin);
            return Save(root);
        }

        static VNSaveSlotSkin BuildSaveSlot(Transform parent)
        {
            var pair = LayoutButton(parent, "SlotTemplate", 425); var go = pair.Item1.gameObject;
            ((RectTransform)go.transform).sizeDelta = new Vector2(425, 144);
            var skin = go.AddComponent<VNSaveSlotSkin>(); skin.button = pair.Item1; skin.cardGraphic = pair.Item1.targetGraphic;
            Object.DestroyImmediate(pair.Item2.gameObject);
            var thumb = Node(go.transform, "Thumbnail", typeof(RawImage));
            SetRect((RectTransform)thumb.transform, new Vector2(12, -15), new Vector2(202, 114)); skin.thumbnail = thumb.GetComponent<RawImage>();
            skin.slotNumber = Text(go.transform, "SlotNumber", 24, TextAlignmentOptions.TopLeft, new Vector2(226, -11), new Vector2(182, 30));
            skin.savedAt = Text(go.transform, "SavedAt", 19, TextAlignmentOptions.TopLeft, new Vector2(226, -42), new Vector2(184, 26));
            skin.lastLine = Text(go.transform, "LastLine", 20, TextAlignmentOptions.TopLeft, new Vector2(226, -70), new Vector2(184, 62));
            return skin;
        }

        static GameObject BuildGallery()
        {
            var root = Root("CgGallery_Default"); var skin = root.AddComponent<VNCgGallerySkin>(); skin.panelRoot = root;
            skin.backgroundCloseButton = FullButton(root.transform, "Dim", new Color(0, .01f, .02f, .92f));
            skin.titleText = Text(root.transform, "Title", 34, TextAlignmentOptions.Center, new Vector2(600, -22), new Vector2(720, 46));
            skin.progressText = Text(root.transform, "Progress", 22, TextAlignmentOptions.Center, new Vector2(600, -66), new Vector2(720, 28));
            skin.hintText = Text(root.transform, "Hint", 20, TextAlignmentOptions.Center, new Vector2(600, -1028), new Vector2(720, 28));
            (skin.closeButton, _) = FixedButton(root.transform, "Close", new Vector2(1840, -30), new Vector2(58, 52), "×");
            BuildGrid(root.transform, out skin.scroll, out skin.grid);
            skin.cellTemplate = BuildCgCell(skin.grid); skin.cellTemplate.gameObject.SetActive(false);
            BuildViewer(root.transform, skin);
            return Save(root);
        }

        static GameObject BuildBacklog()
        {
            var root = Root("Backlog_Default"); var skin = root.AddComponent<VNBacklogSkin>(); skin.panelRoot = root;
            skin.backgroundCloseButton = FullButton(root.transform, "Dim", new Color(0, 0, .02f, .86f));
            skin.titleText = Text(root.transform, "Title", 34, TextAlignmentOptions.Center, new Vector2(600, -26), new Vector2(720, 50));
            (skin.closeButton, _) = FixedButton(root.transform, "Close", new Vector2(1840, -30), new Vector2(58, 52), "×");
            BuildScroll(root.transform, new Vector2(.12f, .08f), new Vector2(.88f, .9f), out skin.scroll, out skin.content);
            var entry = Node(skin.content, "EntryTemplate", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var es = entry.AddComponent<VNBacklogEntrySkin>();
            es.speakerText = Text(entry.transform, "Speaker", 25, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(0, 34)); es.speakerText.color = Gold;
            es.bodyText = Text(entry.transform, "Body", 28, TextAlignmentOptions.TopLeft, Vector2.zero, new Vector2(0, 50)); es.bodyText.textWrappingMode = TextWrappingModes.Normal;
            entry.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            skin.entryTemplate = es; entry.SetActive(false);
            return Save(root);
        }

        static GameObject BuildStatsHud()
        {
            var root = Node(null, "StatsHud_Default", typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)root.transform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(0, 46);
            var bg = root.GetComponent<Image>(); bg.sprite = _rounded; bg.type = UnityEngine.UI.Image.Type.Sliced; bg.color = new Color(.018f, .026f, .052f, .78f);
            var layout = root.GetComponent<HorizontalLayoutGroup>(); layout.padding = new RectOffset(18, 18, 6, 6); layout.spacing = 26; layout.childForceExpandWidth = false;
            root.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var skin = root.AddComponent<VNStatsHudSkin>(); skin.hudRoot = root; skin.entryContainer = rect;
            var entry = Node(root.transform, "EntryTemplate", typeof(HorizontalLayoutGroup));
            var es = entry.AddComponent<VNStatsHudEntrySkin>(); entry.GetComponent<HorizontalLayoutGroup>().spacing = 7;
            es.icon = Image(entry.transform, "Icon", Color.white); ((RectTransform)es.icon.transform).sizeDelta = new Vector2(22, 22);
            es.nameText = Text(entry.transform, "Name", 18, TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(70, 32));
            es.valueText = Text(entry.transform, "Value", 22, TextAlignmentOptions.MidlineLeft, Vector2.zero, new Vector2(60, 32));
            es.barRoot = Image(entry.transform, "Bar", new Color(1, 1, 1, .14f)).gameObject; ((RectTransform)es.barRoot.transform).sizeDelta = new Vector2(64, 8);
            es.barFill = FullImage(es.barRoot.transform, "Fill", Gold);
            skin.entryTemplate = es; entry.SetActive(false);
            return Save(root);
        }

        static GameObject BuildStatsPanel()
        {
            var root = Root("StatsPanel_Default"); var skin = root.AddComponent<VNStatsPanelSkin>(); skin.panelRoot = root;
            skin.backgroundCloseButton = FullButton(root.transform, "Dim", new Color(0, .01f, .02f, .86f));
            skin.titleText = Text(root.transform, "Title", 34, TextAlignmentOptions.Center, new Vector2(600, -60), new Vector2(720, 50));
            (skin.closeButton, _) = FixedButton(root.transform, "Close", new Vector2(1840, -30), new Vector2(58, 52), "×");
            var content = Node(root.transform, "Content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            SetRect((RectTransform)content.transform, new Vector2(576, -150), new Vector2(768, 800));
            content.GetComponent<VerticalLayoutGroup>().spacing = 20; content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            skin.content = (RectTransform)content.transform;
            var row = Node(content.transform, "RowTemplate", typeof(LayoutElement)); row.GetComponent<LayoutElement>().preferredHeight = 50;
            var rs = row.AddComponent<VNStatsPanelRowSkin>();
            rs.icon = Image(row.transform, "Icon", Color.white); SetRect(rs.icon.rectTransform, new Vector2(0, -8), new Vector2(34, 34));
            rs.nameText = Text(row.transform, "Name", 27, TextAlignmentOptions.MidlineLeft, new Vector2(48, 0), new Vector2(190, 50));
            rs.barRoot = Image(row.transform, "Bar", new Color(1, 1, 1, .12f)).gameObject; SetRect((RectTransform)rs.barRoot.transform, new Vector2(250, -18), new Vector2(300, 14));
            rs.barFill = FullImage(rs.barRoot.transform, "Fill", Gold);
            rs.valueText = Text(row.transform, "Value", 27, TextAlignmentOptions.MidlineRight, new Vector2(570, 0), new Vector2(190, 50));
            skin.rowTemplate = rs; row.SetActive(false);
            return Save(root);
        }

        static GameObject BuildInventory()
        {
            var root = Root("Inventory_Default"); var skin = root.AddComponent<VNInventorySkin>(); skin.panelRoot = root;
            skin.backgroundCloseButton = FullButton(root.transform, "Dim", new Color(0, .01f, .02f, .88f));
            skin.titleText = Text(root.transform, "Title", 34, TextAlignmentOptions.Center, new Vector2(600, -22), new Vector2(720, 46));
            skin.hintText = Text(root.transform, "Hint", 20, TextAlignmentOptions.Center, new Vector2(600, -74), new Vector2(720, 28)); skin.hintText.color = new Color(1, 1, 1, .55f);
            (skin.closeButton, _) = FixedButton(root.transform, "Close", new Vector2(1840, -30), new Vector2(58, 52), "×");

            BuildScroll(root.transform, new Vector2(.05f, .16f), new Vector2(.47f, .87f), out var scroll, out var content);
            skin.itemContent = content;
            skin.emptyText = Text(scroll.transform, "Empty", 26, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            Stretch(skin.emptyText.rectTransform); skin.emptyText.color = new Color(1, 1, 1, .55f);
            skin.rowTemplate = BuildInventoryRow(content); skin.rowTemplate.gameObject.SetActive(false);

            var slots = Node(root.transform, "Slots", typeof(VerticalLayoutGroup));
            var slotsRect = (RectTransform)slots.transform; slotsRect.anchorMin = new Vector2(.53f, .16f); slotsRect.anchorMax = new Vector2(.95f, .87f); slotsRect.offsetMin = slotsRect.offsetMax = Vector2.zero;
            var slotsLayout = slots.GetComponent<VerticalLayoutGroup>(); slotsLayout.spacing = 10; slotsLayout.childControlWidth = true; slotsLayout.childControlHeight = true; slotsLayout.childForceExpandHeight = true;
            skin.headSlot = BuildInventorySlot(slots.transform, "Slot_Head");
            skin.faceSlot = BuildInventorySlot(slots.transform, "Slot_Face");
            skin.upperBodySlot = BuildInventorySlot(slots.transform, "Slot_UpperBody");
            skin.handsSlot = BuildInventorySlot(slots.transform, "Slot_Hands");
            skin.lowerBodySlot = BuildInventorySlot(slots.transform, "Slot_LowerBody");
            skin.feetSlot = BuildInventorySlot(slots.transform, "Slot_Feet");
            skin.specialSlot = BuildInventorySlot(slots.transform, "Slot_Special");

            var detail = Image(root.transform, "Detail", new Color(1, 1, 1, .04f));
            detail.rectTransform.anchorMin = new Vector2(.05f, .02f); detail.rectTransform.anchorMax = new Vector2(.95f, .14f); detail.rectTransform.offsetMin = detail.rectTransform.offsetMax = Vector2.zero;
            skin.detailText = Text(detail.transform, "Text", 22, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            Stretch(skin.detailText.rectTransform); skin.detailText.rectTransform.offsetMin = new Vector2(18, 10); skin.detailText.rectTransform.offsetMax = new Vector2(-18, -10);
            skin.detailText.textWrappingMode = TextWrappingModes.Normal;
            return Save(root);
        }

        static VNInventoryRowSkin BuildInventoryRow(Transform parent)
        {
            var pair = LayoutButton(parent, "RowTemplate", 0); var go = pair.Item1.gameObject; Object.DestroyImmediate(pair.Item2.gameObject);
            go.GetComponent<LayoutElement>().preferredHeight = 64; go.GetComponent<Image>().color = new Color(1, 1, 1, .05f);
            var skin = go.AddComponent<VNInventoryRowSkin>(); skin.button = pair.Item1;
            skin.icon = Image(go.transform, "Icon", Color.white); SetSide(skin.icon.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(8, 0), new Vector2(48, 48)); skin.icon.preserveAspect = true;
            skin.nameText = Text(go.transform, "Name", 26, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            Stretch(skin.nameText.rectTransform); skin.nameText.rectTransform.offsetMin = new Vector2(68, 0); skin.nameText.rectTransform.offsetMax = new Vector2(-140, 0);
            skin.countText = Text(go.transform, "Count", 24, TextAlignmentOptions.MidlineRight, Vector2.zero, Vector2.zero);
            SetSide(skin.countText.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(80, 0)); skin.countText.color = new Color(1, .82f, .5f, 1);
            var mark = Text(go.transform, "EquippedMark", 21, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero); mark.text = "E"; mark.fontStyle = FontStyles.Bold;
            SetSide(mark.rectTransform, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, .5f), new Vector2(-96, 0), new Vector2(40, 0)); mark.color = new Color(.56f, .88f, .56f, 1);
            skin.equippedMark = mark.gameObject;
            return skin;
        }

        static VNInventorySlotSkin BuildInventorySlot(Transform parent, string name)
        {
            var pair = LayoutButton(parent, name, 0); var go = pair.Item1.gameObject; Object.DestroyImmediate(pair.Item2.gameObject);
            go.GetComponent<Image>().color = new Color(1, 1, 1, .05f);
            var skin = go.AddComponent<VNInventorySlotSkin>(); skin.button = pair.Item1;
            skin.slotLabel = Text(go.transform, "SlotLabel", 24, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            SetSide(skin.slotLabel.rectTransform, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, .5f), new Vector2(16, 0), new Vector2(130, 0)); skin.slotLabel.color = new Color(.66f, .85f, 1, 1);
            skin.icon = Image(go.transform, "Icon", Color.white); SetSide(skin.icon.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(152, 0), new Vector2(46, 46)); skin.icon.preserveAspect = true;
            skin.itemNameText = Text(go.transform, "ItemName", 25, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            Stretch(skin.itemNameText.rectTransform); skin.itemNameText.rectTransform.offsetMin = new Vector2(212, 0); skin.itemNameText.rectTransform.offsetMax = new Vector2(-14, 0);
            var empty = Text(go.transform, "Empty", 23, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero); empty.text = "——"; empty.color = new Color(1, 1, 1, .28f);
            Stretch(empty.rectTransform); empty.rectTransform.offsetMin = new Vector2(212, 0); empty.rectTransform.offsetMax = new Vector2(-14, 0);
            skin.emptyMark = empty.gameObject;
            return skin;
        }

        static GameObject BuildPlan()
        {
            var root = Root("PlanPanel_Default");
            var skin = root.AddComponent<VNPlanSkin>();
            FullImage(root.transform, "Dim", new Color(0f, 0f, 0f, .72f)).raycastTarget = true;

            var panel = Image(root.transform, "Panel", Panel);
            panel.rectTransform.anchorMin = new Vector2(.14f, .08f);
            panel.rectTransform.anchorMax = new Vector2(.86f, .92f);
            panel.rectTransform.offsetMin = panel.rectTransform.offsetMax = Vector2.zero;
            skin.panelRoot = panel.rectTransform;

            skin.titleText = Text(panel.transform, "Title", 40, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.titleText.fontStyle = FontStyles.Bold; skin.titleText.color = Gold;
            var titleRect = skin.titleText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f); titleRect.anchorMax = Vector2.one; titleRect.pivot = new Vector2(.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -26f); titleRect.sizeDelta = new Vector2(0f, 52f);

            BuildScroll(panel.transform, new Vector2(.04f, .14f), new Vector2(.48f, .86f), out _, out var actionContent);
            skin.actionContent = actionContent;
            skin.actionTemplate = BuildPlanActionRow(actionContent);
            skin.actionTemplate.gameObject.SetActive(false);

            BuildScroll(panel.transform, new Vector2(.52f, .14f), new Vector2(.96f, .86f), out _, out var slotContent);
            skin.slotContent = slotContent;
            skin.slotTemplate = BuildPlanSlotRow(slotContent);
            skin.slotTemplate.gameObject.SetActive(false);

            (skin.resetButton, skin.resetLabel) = BottomButton(panel.transform, "Reset", new Vector2(.35f, 0f));
            (skin.confirmButton, skin.confirmLabel) = BottomButton(panel.transform, "Confirm", new Vector2(.65f, 0f));
            skin.confirmButton.targetGraphic.color = new Color(.92f, .61f, .18f, .98f);
            return Save(root);
        }

        static VNPlanActionRowSkin BuildPlanActionRow(Transform parent)
        {
            var pair = LayoutButton(parent, "ActionTemplate", 0); var go = pair.Item1.gameObject;
            go.GetComponent<LayoutElement>().preferredHeight = 84; go.GetComponent<Image>().color = new Color(1, 1, 1, .06f);
            var skin = go.AddComponent<VNPlanActionRowSkin>(); skin.button = pair.Item1;
            skin.icon = Image(go.transform, "Icon", new Color(.5f, .65f, 1f, .5f));
            SetSide(skin.icon.rectTransform, new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(14, 0), new Vector2(56, 56));
            skin.icon.preserveAspect = true;
            skin.nameText = pair.Item2; skin.nameText.fontSize = 27; skin.nameText.alignment = TextAlignmentOptions.MidlineLeft;
            skin.nameText.rectTransform.offsetMin = new Vector2(84, 30); skin.nameText.rectTransform.offsetMax = new Vector2(-12, -6);
            skin.gainText = Text(go.transform, "Gain", 20, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            Stretch(skin.gainText.rectTransform);
            skin.gainText.rectTransform.offsetMin = new Vector2(84, 8); skin.gainText.rectTransform.offsetMax = new Vector2(-12, -48);
            skin.gainText.color = new Color(.66f, .85f, .66f, 1f);
            return skin;
        }

        static VNPlanSlotRowSkin BuildPlanSlotRow(Transform parent)
        {
            var pair = LayoutButton(parent, "SlotTemplate", 0); var go = pair.Item1.gameObject;
            go.GetComponent<LayoutElement>().preferredHeight = 64;
            var skin = go.AddComponent<VNPlanSlotRowSkin>(); skin.button = pair.Item1;
            skin.background = go.GetComponent<Image>(); skin.background.color = skin.emptyColor;
            skin.dayText = pair.Item2; skin.dayText.fontSize = 24; skin.dayText.alignment = TextAlignmentOptions.MidlineLeft;
            skin.dayText.color = new Color(1f, .92f, .7f, .9f);
            var dayRect = skin.dayText.rectTransform;
            dayRect.anchorMin = Vector2.zero; dayRect.anchorMax = new Vector2(.32f, 1f);
            dayRect.offsetMin = new Vector2(16, 0); dayRect.offsetMax = Vector2.zero;
            skin.assignedText = Text(go.transform, "Assigned", 26, TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
            var assignedRect = skin.assignedText.rectTransform;
            assignedRect.anchorMin = new Vector2(.32f, 0f); assignedRect.anchorMax = Vector2.one;
            assignedRect.offsetMin = Vector2.zero; assignedRect.offsetMax = new Vector2(-12, 0);
            return skin;
        }

        static GameObject BuildResultPopup()
        {
            var root = Root("ResultPopup_Default");
            var skin = root.AddComponent<VNResultPopupSkin>();
            FullImage(root.transform, "Dim", new Color(0f, 0f, 0f, .6f)).raycastTarget = true;

            var panel = Image(root.transform, "Panel", new Color(.06f, .08f, .13f, .97f));
            At(panel.rectTransform, new Vector2(.5f, .52f), new Vector2(720, 380));
            skin.panelRoot = panel.rectTransform; skin.panelBackground = panel;

            skin.gradeText = Text(panel.transform, "Grade", 96, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.gradeText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            At(skin.gradeText.rectTransform, new Vector2(.5f, .68f), new Vector2(680, 130));
            skin.gradeLocalText = Text(panel.transform, "GradeLocal", 30, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            At(skin.gradeLocalText.rectTransform, new Vector2(.5f, .44f), new Vector2(600, 44));
            skin.titleText = Text(panel.transform, "Title", 34, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.titleText.fontStyle = FontStyles.Bold;
            At(skin.titleText.rectTransform, new Vector2(.5f, .28f), new Vector2(660, 48));
            skin.subText = Text(panel.transform, "Sub", 24, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.subText.color = new Color(1, 1, 1, .65f);
            At(skin.subText.rectTransform, new Vector2(.5f, .17f), new Vector2(660, 36));
            skin.hintText = Text(panel.transform, "Hint", 22, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.hintText.color = new Color(1, 1, 1, .55f);
            At(skin.hintText.rectTransform, new Vector2(.5f, .06f), new Vector2(400, 32));

            var bar = Image(panel.transform, "Bar", new Color(1, 1, 1, .12f));
            At(bar.rectTransform, new Vector2(.5f, .5f), new Vector2(440, 20));
            skin.barRoot = bar.gameObject;
            var fill = Image(bar.transform, "Fill", Gold);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            skin.barFill = fill.rectTransform;
            skin.percentText = Text(panel.transform, "Percent", 64, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            skin.percentText.fontStyle = FontStyles.Bold; skin.percentText.color = Gold;
            At(skin.percentText.rectTransform, new Vector2(.5f, .72f), new Vector2(400, 80));

            skin.burstOrigin = skin.gradeText.rectTransform;
            return Save(root);
        }

        static (Button, TextMeshProUGUI) BottomButton(Transform parent, string name, Vector2 anchor)
        {
            var pair = LayoutButton(parent, name, 200);
            var rect = (RectTransform)pair.Item1.transform;
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 22f); rect.sizeDelta = new Vector2(200f, 54f);
            pair.Item2.fontSize = 26;
            return pair;
        }

        static void At(RectTransform rect, Vector2 anchor, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
        }

        static void SetSide(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.pivot = pivot; rect.anchoredPosition = pos; rect.sizeDelta = size;
        }

        static void BuildConfirm(Transform parent, VNSaveLoadSkin skin)
        {
            skin.confirmRoot = FullImage(parent, "Confirm", new Color(0, 0, .015f, .72f)).gameObject;
            var dialog = Image(skin.confirmRoot.transform, "Dialog", Panel); SetCenter(dialog.rectTransform, new Vector2(620, 260));
            skin.confirmMessage = Text(dialog.transform, "Message", 29, TextAlignmentOptions.Center, new Vector2(30, -25), new Vector2(560, 120));
            (skin.confirmYes, skin.confirmYesLabel) = FixedButton(dialog.transform, "Yes", new Vector2(100, -190), new Vector2(180, 54));
            (skin.confirmNo, skin.confirmNoLabel) = FixedButton(dialog.transform, "No", new Vector2(340, -190), new Vector2(180, 54));
            skin.confirmRoot.SetActive(false);
        }

        static void BuildViewer(Transform parent, VNCgGallerySkin skin)
        {
            skin.viewerRoot = FullImage(parent, "Viewer", new Color(0, 0, 0, .97f)).gameObject;
            skin.viewerImage = Image(skin.viewerRoot.transform, "Cg", Color.white); Stretch(skin.viewerImage.rectTransform); skin.viewerImage.rectTransform.offsetMin = new Vector2(70, 70); skin.viewerImage.rectTransform.offsetMax = new Vector2(-70, -70); skin.viewerImage.preserveAspect = true;
            skin.viewerCaption = Text(skin.viewerRoot.transform, "Caption", 22, TextAlignmentOptions.Center, new Vector2(500, -1030), new Vector2(920, 30));
            (skin.viewerCloseButton, _) = FixedButton(skin.viewerRoot.transform, "Close", new Vector2(1820, -30), new Vector2(70, 56), "×");
            (skin.viewerPreviousButton, _) = FixedButton(skin.viewerRoot.transform, "Previous", new Vector2(35, -500), new Vector2(70, 70), "‹");
            (skin.viewerNextButton, _) = FixedButton(skin.viewerRoot.transform, "Next", new Vector2(1815, -500), new Vector2(70, 70), "›");
            skin.viewerRoot.SetActive(false);
        }

        static VNCgCellSkin BuildCgCell(Transform parent)
        {
            var pair = LayoutButton(parent, "CellTemplate", 280); var go = pair.Item1.gameObject; Object.DestroyImmediate(pair.Item2.gameObject);
            ((RectTransform)go.transform).sizeDelta = new Vector2(280, 158);
            var skin = go.AddComponent<VNCgCellSkin>(); skin.button = pair.Item1; skin.frameGraphic = pair.Item1.targetGraphic;
            skin.thumbnail = FullImage(go.transform, "Thumbnail", Color.white); skin.thumbnail.preserveAspect = true; skin.thumbnail.rectTransform.offsetMin = new Vector2(6, 6); skin.thumbnail.rectTransform.offsetMax = new Vector2(-6, -6);
            skin.lockedRoot = FullImage(go.transform, "Locked", new Color(0, 0, 0, .5f)).gameObject;
            skin.lockedLabel = Text(skin.lockedRoot.transform, "Question", 46, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero); Stretch(skin.lockedLabel.rectTransform);
            skin.countBadge = Text(go.transform, "Count", 20, TextAlignmentOptions.BottomRight, new Vector2(150, -125), new Vector2(110, 26));
            return skin;
        }

        static void BuildGrid(Transform parent, out ScrollRect scroll, out RectTransform grid)
        {
            var scrollGo = Node(parent, "Scroll", typeof(Image), typeof(ScrollRect)); var sr = (RectTransform)scrollGo.transform;
            sr.anchorMin = new Vector2(.08f, .1f); sr.anchorMax = new Vector2(.92f, .86f); sr.offsetMin = sr.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1, 1, 1, .02f); scroll = scrollGo.GetComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Node(scrollGo.transform, "Viewport", typeof(Image), typeof(RectMask2D)); Stretch((RectTransform)viewport.transform); viewport.GetComponent<Image>().color = new Color(0, 0, 0, .01f);
            var gridGo = Node(viewport.transform, "Grid", typeof(GridLayoutGroup), typeof(ContentSizeFitter)); grid = (RectTransform)gridGo.transform; grid.anchorMin = new Vector2(0, 1); grid.anchorMax = new Vector2(1, 1); grid.pivot = new Vector2(.5f, 1); grid.sizeDelta = Vector2.zero;
            var gl = gridGo.GetComponent<GridLayoutGroup>(); gl.cellSize = new Vector2(280, 158); gl.spacing = new Vector2(14, 14); gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 4;
            gridGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.viewport = (RectTransform)viewport.transform; scroll.content = grid;
        }

        static void BuildScroll(Transform parent, Vector2 min, Vector2 max, out ScrollRect scroll, out RectTransform content)
        {
            var scrollGo = Node(parent, "Scroll", typeof(Image), typeof(ScrollRect)); var sr = (RectTransform)scrollGo.transform; sr.anchorMin = min; sr.anchorMax = max; sr.offsetMin = sr.offsetMax = Vector2.zero;
            scroll = scrollGo.GetComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Node(scrollGo.transform, "Viewport", typeof(Image), typeof(RectMask2D)); Stretch((RectTransform)viewport.transform); viewport.GetComponent<Image>().color = new Color(0, 0, 0, .01f);
            var contentGo = Node(viewport.transform, "Content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content = (RectTransform)contentGo.transform; content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.sizeDelta = Vector2.zero;
            contentGo.GetComponent<VerticalLayoutGroup>().spacing = 18; contentGo.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; scroll.viewport = (RectTransform)viewport.transform; scroll.content = content;
        }

        static void BuildSliderRow(Transform parent, string name, float top, out TMP_Text label, out Slider slider, out TMP_Text value)
        {
            label = Text(parent, name + "Label", 24, TextAlignmentOptions.Left, new Vector2(82, -top), new Vector2(170, 42));
            var go = Node(parent, name + "Slider", typeof(Image), typeof(Slider)); SetRect((RectTransform)go.transform, new Vector2(260, -top), new Vector2(340, 44));
            var hitArea = go.GetComponent<Image>(); hitArea.color = Color.clear; hitArea.raycastTarget = true;
            slider = go.GetComponent<Slider>();
            var bg = Image(go.transform, "Background", new Color(.1f, .13f, .21f, 1)); Stretch(bg.rectTransform); bg.rectTransform.offsetMin = new Vector2(0, 17); bg.rectTransform.offsetMax = new Vector2(0, -17);
            var fillArea = Node(go.transform, "FillArea"); Stretch((RectTransform)fillArea.transform); ((RectTransform)fillArea.transform).offsetMin = new Vector2(7, 17); ((RectTransform)fillArea.transform).offsetMax = new Vector2(-7, -17);
            var fill = FullImage(fillArea.transform, "Fill", new Color(1, .62f, .2f, 1)); slider.fillRect = fill.rectTransform;
            var handleArea = Node(go.transform, "HandleArea"); Stretch((RectTransform)handleArea.transform); ((RectTransform)handleArea.transform).offsetMin = new Vector2(10, 0); ((RectTransform)handleArea.transform).offsetMax = new Vector2(-10, 0);
            var handle = Image(handleArea.transform, "Handle", new Color(1, .88f, .62f, 1)); handle.rectTransform.sizeDelta = new Vector2(24, 32); slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            value = Text(parent, name + "Value", 21, TextAlignmentOptions.Right, new Vector2(614, -top), new Vector2(84, 42));
        }

        static (Button, TextMeshProUGUI) MenuButton(Transform parent, string name)
        {
            var pair = LayoutButton(parent, name, 400); ((RectTransform)pair.Item1.transform).sizeDelta = new Vector2(400, 62); pair.Item2.alignment = TextAlignmentOptions.Left; pair.Item2.rectTransform.offsetMin = new Vector2(26, 0); return pair;
        }

        static (Button, TextMeshProUGUI) LayoutButton(Transform parent, string name, float width)
        {
            var go = Node(parent, name, typeof(Image), typeof(Button), typeof(LayoutElement)); var image = go.GetComponent<Image>(); image.sprite = _rounded; image.type = UnityEngine.UI.Image.Type.Sliced; image.color = ButtonColor; image.raycastTarget = true;
            var button = go.GetComponent<Button>(); button.targetGraphic = image; go.GetComponent<LayoutElement>().preferredWidth = width;
            var label = Text(go.transform, "Label", 20, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero); Stretch(label.rectTransform); return (button, label);
        }

        static (Button, TextMeshProUGUI) FixedButton(Transform parent, string name, Vector2 pos, Vector2 size, string text = "")
        {
            var pair = LayoutButton(parent, name, size.x); SetRect((RectTransform)pair.Item1.transform, pos, size); pair.Item2.text = text; return pair;
        }

        static Button FullButton(Transform parent, string name, Color color)
        {
            var go = Node(parent, name, typeof(Image), typeof(Button)); Stretch((RectTransform)go.transform); var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = true; var button = go.GetComponent<Button>(); button.targetGraphic = image; return button;
        }

        static Image FullImage(Transform parent, string name, Color color) { var image = Image(parent, name, color); Stretch(image.rectTransform); return image; }
        static Image Image(Transform parent, string name, Color color)
        {
            var go = Node(parent, name, typeof(Image)); var image = go.GetComponent<Image>(); image.sprite = _rounded; image.type = UnityEngine.UI.Image.Type.Sliced; image.color = color; image.raycastTarget = false; return image;
        }
        static TextMeshProUGUI Text(Transform parent, string name, int size, TextAlignmentOptions align, Vector2 pos, Vector2 rectSize)
        {
            var go = Node(parent, name, typeof(TextMeshProUGUI)); var text = go.GetComponent<TextMeshProUGUI>(); text.font = _font; text.fontSize = size; text.alignment = align; text.color = Color.white; text.raycastTarget = false; if (rectSize != Vector2.zero) SetRect(text.rectTransform, pos, rectSize); return text;
        }
        static GameObject Root(string name) { var go = Node(null, name); Stretch((RectTransform)go.transform); return go; }
        static GameObject Node(Transform parent, string name, params System.Type[] extra)
        {
            var types = new System.Type[extra.Length + 1]; types[0] = typeof(RectTransform); for (int i = 0; i < extra.Length; i++) types[i + 1] = extra[i];
            var go = new GameObject(name, types); if (parent != null) go.transform.SetParent(parent, false); return go;
        }
        static GameObject Save(GameObject temp) { string path = Dir + "/" + temp.name + ".prefab"; var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path); Object.DestroyImmediate(temp); return prefab; }
        static void SetRect(RectTransform rect, Vector2 topLeft, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = topLeft; rect.sizeDelta = size; }
        static void SetCenter(RectTransform rect, Vector2 size) { rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size; }
        static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
    }
}
