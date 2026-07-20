using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 背包（I 键）：左侧道具一览 + 右侧 7 格装备栏 + 底部介绍区。
    /// 持有数全部存 VNFlags（flag 名 = 道具_&lt;id&gt;），装备状态见 VNEquipment；
    /// 文案/图标/装备数据从登记的 VNShopDef 商品清单里找（未登记的道具用 id 照常显示）。
    /// 道具行左键选中看介绍，右键弹出 装备/卸下/使用 菜单；装备格右键直接卸下。
    /// 外观支持全局主题 prefab（VNSystemUiSkinSet.inventoryPrefab + VNInventorySkin 槽位），
    /// 缺失或校验失败时退回程序化默认 UI，参照 VNStatsHud 的皮肤接法。
    /// </summary>
    public class VNInventory : MonoBehaviour
    {
        [Header("道具文案来源（商店定义资产；同 id 取第一个命中的商品条目）")]
        public List<VNShopDef> shops = new List<VNShopDef>();

        Canvas _canvas;
        VNInventorySkin _skin;   // 皮肤实例或程序化构建后统一入口
        GameObject _panel;
        ScrollRect _scroll;
        VNStatsHud _statsHud;
        GameObject _contextMenu;
        string _selectedId;
        bool _open;

        public bool IsOpen => _open;

        void Awake()
        {
            // 道具文案来源 = 商店定义，同样优先读 VNGameConfig
            var cfg = VNGameConfig.Active;
            if (cfg != null) VNGameConfig.ApplyList(cfg.shops, ref shops);

            VNEquipment.ItemResolver = FindItem; // 装备系统查道具走同一张目录
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnDestroy()
        {
            VNLocale.LanguageChanged -= OnLanguageChanged;
            if (VNEquipment.ItemResolver != null &&
                ReferenceEquals(VNEquipment.ItemResolver.Target, this))
                VNEquipment.ItemResolver = null;
        }

        void OnLanguageChanged()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
            _canvas = null;
            _skin = null;
            _panel = null;
            _scroll = null;
            _contextMenu = null;
        }

        /// <summary>按道具 id 找商品条目（跨全部登记商店，找不到返回 null）</summary>
        public VNShopDef.Item FindItem(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var shop in shops)
            {
                if (shop == null) continue;
                var item = shop.FindItem(id);
                if (item != null) return item;
            }
            return null;
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
            _selectedId = null;
            RefreshAll();
            _panel.SetActive(true);
            _open = true;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        public void Close()
        {
            if (!_open) return;
            CloseContextMenu();
            _panel.SetActive(false);
            _open = false;
        }

        // ------------------------------------------------------------------
        // 构建（皮肤优先，退回程序化默认）
        // ------------------------------------------------------------------

        void Build()
        {
            if (_panel != null) return;
            _statsHud = FindFirstObjectByType<VNStatsHud>();

            var canvasGo = new GameObject("VNInventoryCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 600; // 与任务日志/回想同层：同一时刻只会开一个
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.inventoryPrefab);
            _skin = VNSystemUiSkinUtility.Instantiate<VNInventorySkin>(
                skinPrefab, _canvas.transform, "VNInventory");
            if (_skin == null) _skin = BuildDefault();

            _panel = _skin.panelRoot;
            _scroll = _skin.itemContent.GetComponentInParent<ScrollRect>(true);
            _skin.rowTemplate.gameObject.SetActive(false);

            _skin.titleText.text = VNLocale.T("inventory.title");
            if (_skin.hintText != null) _skin.hintText.text = VNLocale.T("inventory.hint");
            if (_skin.emptyText != null) _skin.emptyText.text = VNLocale.T("inventory.empty");
            if (_skin.closeButton != null)
            {
                _skin.closeButton.onClick.RemoveAllListeners();
                _skin.closeButton.onClick.AddListener(Close);
            }
            if (_skin.backgroundCloseButton != null)
            {
                _skin.backgroundCloseButton.onClick.RemoveAllListeners();
                _skin.backgroundCloseButton.onClick.AddListener(Close);
            }

            for (int i = 1; i <= 7; i++)
            {
                var slot = (VNEquipSlot)i;
                var view = _skin.Slot(slot);
                view.slotLabel.text = VNEquipment.SlotName(slot);
                var relay = view.button.gameObject.AddComponent<ClickRelay>();
                relay.onLeft = () => SelectSlot(slot);
                relay.onRight = pos => ShowSlotMenu(slot, pos);
            }

            _panel.SetActive(false);
        }

        // ------------------------------------------------------------------
        // 刷新
        // ------------------------------------------------------------------

        void RefreshAll()
        {
            RebuildList();
            RefreshSlots();
            RefreshDetail();
        }

        void RebuildList()
        {
            var content = _skin.itemContent;
            var template = _skin.rowTemplate;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                if (child != template.gameObject) Destroy(child);
            }

            // 从 flags 反查持有道具（道具_ 前缀且数量 > 0）
            var owned = new List<KeyValuePair<string, int>>();
            foreach (var kv in VNFlags.All)
            {
                if (!kv.Key.StartsWith(VNShopDef.ItemFlagPrefix) || kv.Value <= 0) continue;
                owned.Add(new KeyValuePair<string, int>(
                    kv.Key.Substring(VNShopDef.ItemFlagPrefix.Length), kv.Value));
            }

            if (_skin.emptyText != null)
                _skin.emptyText.gameObject.SetActive(owned.Count == 0);

            foreach (var pair in owned)
            {
                string id = pair.Key;
                var item = FindItem(id);
                var row = Instantiate(template, content);
                row.gameObject.SetActive(true);
                row.gameObject.name = "Item_" + id;

                bool hasIcon = item != null && item.icon != null;
                row.icon.sprite = hasIcon ? item.icon : VNProceduralTextures.RoundedRectSprite;
                row.icon.color = hasIcon ? Color.white : new Color(0.5f, 0.65f, 1f, 0.5f);

                string display = item != null ? item.DisplayName : id;
                if (row.countText != null)
                {
                    row.nameText.text = display;
                    row.countText.text = "×" + pair.Value;
                }
                else
                {
                    row.nameText.text = $"{display}  <color=#ffd27f>×{pair.Value}</color>";
                }
                if (row.equippedMark != null)
                    row.equippedMark.SetActive(VNEquipment.IsEquipped(id));

                var relay = row.button.gameObject.AddComponent<ClickRelay>();
                relay.onLeft = () => Select(id);
                relay.onRight = pos => ShowItemMenu(id, pos);
            }
        }

        void RefreshSlots()
        {
            for (int i = 1; i <= 7; i++)
            {
                var slot = (VNEquipSlot)i;
                var view = _skin.Slot(slot);
                string itemId = VNEquipment.ItemInSlot(slot);
                var item = FindItem(itemId);
                bool occupied = itemId != null;

                bool hasIcon = item != null && item.icon != null;
                view.icon.enabled = occupied;
                if (occupied)
                {
                    view.icon.sprite = hasIcon
                        ? item.icon : VNProceduralTextures.RoundedRectSprite;
                    view.icon.color = hasIcon
                        ? Color.white : new Color(0.5f, 0.65f, 1f, 0.5f);
                }
                if (view.itemNameText != null)
                    view.itemNameText.text = !occupied ? ""
                        : item != null ? item.DisplayName : itemId;
                if (view.emptyMark != null) view.emptyMark.SetActive(!occupied);
            }
        }

        void Select(string id)
        {
            CloseContextMenu();
            _selectedId = id;
            RefreshDetail();
        }

        void SelectSlot(VNEquipSlot slot)
        {
            string itemId = VNEquipment.ItemInSlot(slot);
            if (itemId != null) Select(itemId);
        }

        void RefreshDetail()
        {
            if (_selectedId != null && VNFlags.Get(VNShopDef.ItemFlagName(_selectedId)) <= 0)
                _selectedId = null; // 用完/卖光后取消选中

            if (_selectedId == null)
            {
                _skin.detailText.text =
                    $"<color=#a8aab8>{VNLocale.T("inventory.hint")}</color>";
                return;
            }
            _skin.detailText.text = DescribeItem(_selectedId);
        }

        /// <summary>介绍区文本：名字×数量 + 介绍文 + 装备部位/加成/特殊效果/使用效果</summary>
        string DescribeItem(string id)
        {
            var item = FindItem(id);
            int count = VNFlags.Get(VNShopDef.ItemFlagName(id));
            var sb = new StringBuilder();
            string display = item != null ? item.DisplayName : id;
            sb.Append($"<b><color=#ffd27f>{display}</color></b>  ×{count}");
            if (VNEquipment.IsEquipped(id))
                sb.Append($"  <color=#8fe08f>[{VNLocale.T("equip.equippedMark")}]</color>");

            if (item == null) return sb.ToString();
            if (!string.IsNullOrEmpty(item.LocalizedDescription))
                sb.Append($"\n{item.LocalizedDescription}");

            if (item.IsEquippable)
            {
                sb.Append($"\n<color=#a8d8ff>{VNLocale.T("equip.detail.slot", VNEquipment.SlotName(item.equipSlot))}</color>");
                string bonuses = JoinStatOps(item.statBonuses);
                if (bonuses.Length > 0)
                    sb.Append($"\n<color=#a8d8ff>{VNLocale.T("equip.detail.bonus", bonuses)}</color>");
                var effects = new List<string>();
                foreach (var effect in item.passiveEffects)
                    if (effect != null && !string.IsNullOrEmpty(effect.effectId))
                        effects.Add(effect.DisplayLabel);
                if (effects.Count > 0)
                    sb.Append($"\n<color=#d8b8ff>{VNLocale.T("equip.detail.effect", string.Join("、", effects))}</color>");
            }
            if (item.IsUsable)
            {
                string ops = JoinStatOps(item.useOps);
                sb.Append($"\n<color=#b8e8c8>{VNLocale.T("equip.detail.use", ops)}</color>");
                if (item.consumeOnUse) sb.Append($"<color=#a8aab8>{VNLocale.T("equip.detail.consume")}</color>");
            }
            return sb.ToString();
        }

        string JoinStatOps(List<VNShopDef.StatOp> ops)
        {
            var parts = new List<string>();
            foreach (var op in ops)
            {
                if (op == null || string.IsNullOrEmpty(op.statId) || op.amount == 0) continue;
                var def = _statsHud != null ? _statsHud.Find(op.statId) : null;
                string name = def != null ? def.DisplayName : op.statId;
                parts.Add($"{name} {(op.amount > 0 ? "+" : "")}{op.amount}");
            }
            return string.Join("、", parts);
        }

        // ------------------------------------------------------------------
        // 右键菜单
        // ------------------------------------------------------------------

        void ShowItemMenu(string id, Vector2 screenPos)
        {
            Select(id);
            var item = FindItem(id);
            var entries = new List<(string, System.Action)>();
            if (item != null && item.IsEquippable && !VNEquipment.IsEquipped(id))
                entries.Add((VNLocale.T("equip.menu.equip"), () =>
                {
                    VNEquipment.Equip(item);
                    RefreshAll();
                }));
            if (VNEquipment.IsEquipped(id))
                entries.Add((VNLocale.T("equip.menu.unequip"), () =>
                {
                    VNEquipment.Unequip(id);
                    RefreshAll();
                }));
            if (item != null && item.IsUsable)
                entries.Add((VNLocale.T("equip.menu.use"), () =>
                {
                    VNEquipment.Use(item);
                    RefreshAll();
                }));
            if (entries.Count > 0) ShowContextMenu(entries, screenPos);
        }

        void ShowSlotMenu(VNEquipSlot slot, Vector2 screenPos)
        {
            string itemId = VNEquipment.ItemInSlot(slot);
            if (itemId == null) return;
            Select(itemId);
            ShowContextMenu(new List<(string, System.Action)>
            {
                (VNLocale.T("equip.menu.unequip"), () =>
                {
                    VNEquipment.Unequip(itemId);
                    RefreshAll();
                }),
            }, screenPos);
        }

        void ShowContextMenu(List<(string label, System.Action action)> entries, Vector2 screenPos)
        {
            CloseContextMenu();

            _contextMenu = new GameObject("ContextMenu", typeof(RectTransform));
            var menuRoot = (RectTransform)_contextMenu.transform;
            menuRoot.SetParent(_canvas.transform, false);
            Stretch(menuRoot);

            // 全屏透明捕获层：点菜单外任意处关闭
            var catcherGo = new GameObject("Catcher",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var catcherRect = (RectTransform)catcherGo.transform;
            catcherRect.SetParent(menuRoot, false);
            Stretch(catcherRect);
            catcherGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.004f);
            catcherGo.GetComponent<Button>().onClick.AddListener(CloseContextMenu);

            var panelGo = new GameObject("Menu", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(menuRoot, false);
            var bg = panelGo.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.07f, 0.09f, 0.16f, 0.98f);
            var layout = panelGo.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 4f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            panelGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // 屏幕坐标 → 画布局部坐标；按象限翻转 pivot 保证菜单不出屏
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, screenPos, null, out var local);
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(local.x > 0f ? 1f : 0f, local.y > 0f ? 1f : 0f);
            panelRect.anchoredPosition = local;
            panelRect.sizeDelta = new Vector2(190f, 0f);

            foreach (var entry in entries)
            {
                var btnGo = new GameObject("Option", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
                btnGo.transform.SetParent(panelRect, false);
                btnGo.GetComponent<LayoutElement>().preferredHeight = 46f;
                var btnBg = btnGo.GetComponent<Image>();
                btnBg.sprite = VNProceduralTextures.RoundedRectSprite;
                btnBg.type = Image.Type.Sliced;
                btnBg.color = new Color(0.12f, 0.16f, 0.27f, 0.96f);
                var button = btnGo.GetComponent<Button>();
                button.targetGraphic = btnBg;
                var action = entry.action;
                button.onClick.AddListener(() =>
                {
                    CloseContextMenu();
                    action();
                });
                var label = CreateText(btnGo.transform, 24, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.text = entry.label;
            }
        }

        void CloseContextMenu()
        {
            if (_contextMenu == null) return;
            Destroy(_contextMenu);
            _contextMenu = null;
        }

        /// <summary>左/右键分发（Button 之外的补充；同物体上的 Button 只负责按压视觉）</summary>
        class ClickRelay : MonoBehaviour, IPointerClickHandler
        {
            public System.Action onLeft;
            public System.Action<Vector2> onRight;

            public void OnPointerClick(PointerEventData eventData)
            {
                if (eventData.button == PointerEventData.InputButton.Left) onLeft?.Invoke();
                else if (eventData.button == PointerEventData.InputButton.Right)
                    onRight?.Invoke(eventData.position);
            }
        }

        // ------------------------------------------------------------------
        // 程序化默认 UI（皮肤缺失/校验失败时兜底；结构与导出模板一致）
        // ------------------------------------------------------------------

        VNInventorySkin BuildDefault()
        {
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            var panelRect = (RectTransform)panelGo.transform;
            panelRect.SetParent(_canvas.transform, false);
            Stretch(panelRect);
            var skin = panelGo.AddComponent<VNInventorySkin>();
            skin.panelRoot = panelGo;

            var dimGo = new GameObject("Dim",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var dimRect = (RectTransform)dimGo.transform;
            dimRect.SetParent(panelRect, false);
            Stretch(dimRect);
            var dimImage = dimGo.GetComponent<Image>();
            dimImage.color = new Color(0f, 0.01f, 0.02f, 0.88f);
            skin.backgroundCloseButton = dimGo.GetComponent<Button>();
            skin.backgroundCloseButton.targetGraphic = dimImage;

            skin.titleText = CreateText(panelRect, 34, TextAlignmentOptions.Center);
            skin.titleText.fontStyle = FontStyles.Bold;
            SetAnchored(skin.titleText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(0f, 48f));

            skin.hintText = CreateText(panelRect, 20, TextAlignmentOptions.Center);
            skin.hintText.color = new Color(1f, 1f, 1f, 0.55f);
            SetAnchored(skin.hintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(0f, 30f));

            // 左：道具列表
            var scrollGo = new GameObject("Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.SetParent(panelRect, false);
            scrollRect.anchorMin = new Vector2(0.05f, 0.16f);
            scrollRect.anchorMax = new Vector2(0.47f, 0.87f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;

            var viewportGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.SetParent(scrollRect, false);
            Stretch(viewportRect);
            viewportGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var contentGo = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var content = (RectTransform)contentGo.transform;
            content.SetParent(viewportRect, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            // sizeDelta 默认 (100,100)，横向拉伸下会比视口宽 100px 被 RectMask2D 裁掉，必须清零
            content.sizeDelta = Vector2.zero;
            var contentLayout = contentGo.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 10f;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentGo.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewportRect;
            scroll.content = content;
            skin.itemContent = content;

            skin.emptyText = CreateText(scrollRect, 26, TextAlignmentOptions.Center);
            skin.emptyText.color = new Color(1f, 1f, 1f, 0.55f);
            Stretch(skin.emptyText.rectTransform);
            skin.emptyText.raycastTarget = false;

            skin.rowTemplate = BuildDefaultRow(content);

            // 右：7 格装备栏
            var slotsGo = new GameObject("Slots",
                typeof(RectTransform), typeof(VerticalLayoutGroup));
            var slotsRect = (RectTransform)slotsGo.transform;
            slotsRect.SetParent(panelRect, false);
            slotsRect.anchorMin = new Vector2(0.53f, 0.16f);
            slotsRect.anchorMax = new Vector2(0.95f, 0.87f);
            slotsRect.offsetMin = Vector2.zero;
            slotsRect.offsetMax = Vector2.zero;
            var slotsLayout = slotsGo.GetComponent<VerticalLayoutGroup>();
            slotsLayout.spacing = 10f;
            slotsLayout.childControlWidth = true;
            slotsLayout.childControlHeight = true;
            slotsLayout.childForceExpandHeight = true;

            skin.headSlot = BuildDefaultSlot(slotsRect);
            skin.faceSlot = BuildDefaultSlot(slotsRect);
            skin.upperBodySlot = BuildDefaultSlot(slotsRect);
            skin.handsSlot = BuildDefaultSlot(slotsRect);
            skin.lowerBodySlot = BuildDefaultSlot(slotsRect);
            skin.feetSlot = BuildDefaultSlot(slotsRect);
            skin.specialSlot = BuildDefaultSlot(slotsRect);

            // 底：介绍区
            var detailGo = new GameObject("Detail",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var detailRect = (RectTransform)detailGo.transform;
            detailRect.SetParent(panelRect, false);
            detailRect.anchorMin = new Vector2(0.05f, 0.02f);
            detailRect.anchorMax = new Vector2(0.95f, 0.14f);
            detailRect.offsetMin = Vector2.zero;
            detailRect.offsetMax = Vector2.zero;
            var detailBg = detailGo.GetComponent<Image>();
            detailBg.sprite = VNProceduralTextures.RoundedRectSprite;
            detailBg.type = Image.Type.Sliced;
            detailBg.color = new Color(1f, 1f, 1f, 0.04f);
            detailBg.raycastTarget = false;
            skin.detailText = CreateText(detailRect, 22, TextAlignmentOptions.TopLeft);
            Stretch(skin.detailText.rectTransform);
            skin.detailText.rectTransform.offsetMin = new Vector2(18f, 10f);
            skin.detailText.rectTransform.offsetMax = new Vector2(-18f, -10f);
            skin.detailText.textWrappingMode = TextWrappingModes.Normal;

            return skin;
        }

        VNInventoryRowSkin BuildDefaultRow(RectTransform parent)
        {
            var rowGo = new GameObject("RowTemplate", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rowRect = (RectTransform)rowGo.transform;
            rowRect.SetParent(parent, false);
            rowGo.GetComponent<LayoutElement>().preferredHeight = 64f;
            var rowBg = rowGo.GetComponent<Image>();
            rowBg.sprite = VNProceduralTextures.RoundedRectSprite;
            rowBg.type = Image.Type.Sliced;
            rowBg.color = new Color(1f, 1f, 1f, 0.05f);
            var rowSkin = rowGo.AddComponent<VNInventoryRowSkin>();
            rowSkin.button = rowGo.GetComponent<Button>();
            rowSkin.button.targetGraphic = rowBg;

            var iconGo = new GameObject("Icon", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rowRect, false);
            SetAnchored(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(48f, 48f));
            rowSkin.icon = iconGo.GetComponent<Image>();
            rowSkin.icon.preserveAspect = true;
            rowSkin.icon.raycastTarget = false;

            rowSkin.nameText = CreateText(rowRect, 26, TextAlignmentOptions.MidlineLeft);
            Stretch(rowSkin.nameText.rectTransform);
            rowSkin.nameText.rectTransform.offsetMin = new Vector2(68f, 0f);
            rowSkin.nameText.rectTransform.offsetMax = new Vector2(-140f, 0f);

            rowSkin.countText = CreateText(rowRect, 24, TextAlignmentOptions.MidlineRight);
            rowSkin.countText.color = new Color(1f, 0.82f, 0.5f, 1f);
            SetAnchored(rowSkin.countText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(80f, 0f));

            var markText = CreateText(rowRect, 21, TextAlignmentOptions.Center);
            markText.text = VNLocale.T("equip.equippedMark");
            markText.color = new Color(0.56f, 0.88f, 0.56f, 1f);
            markText.fontStyle = FontStyles.Bold;
            SetAnchored(markText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(1f, 0.5f), new Vector2(-96f, 0f), new Vector2(40f, 0f));
            rowSkin.equippedMark = markText.gameObject;

            return rowSkin;
        }

        VNInventorySlotSkin BuildDefaultSlot(RectTransform parent)
        {
            var slotGo = new GameObject("Slot", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image), typeof(Button));
            var slotRect = (RectTransform)slotGo.transform;
            slotRect.SetParent(parent, false);
            var slotBg = slotGo.GetComponent<Image>();
            slotBg.sprite = VNProceduralTextures.RoundedRectSprite;
            slotBg.type = Image.Type.Sliced;
            slotBg.color = new Color(1f, 1f, 1f, 0.05f);
            var slotSkin = slotGo.AddComponent<VNInventorySlotSkin>();
            slotSkin.button = slotGo.GetComponent<Button>();
            slotSkin.button.targetGraphic = slotBg;

            slotSkin.slotLabel = CreateText(slotRect, 24, TextAlignmentOptions.MidlineLeft);
            slotSkin.slotLabel.color = new Color(0.66f, 0.85f, 1f, 1f);
            SetAnchored(slotSkin.slotLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(130f, 0f));

            var iconGo = new GameObject("Icon", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(slotRect, false);
            SetAnchored(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0.5f), new Vector2(152f, 0f), new Vector2(46f, 46f));
            slotSkin.icon = iconGo.GetComponent<Image>();
            slotSkin.icon.preserveAspect = true;
            slotSkin.icon.raycastTarget = false;

            slotSkin.itemNameText = CreateText(slotRect, 25, TextAlignmentOptions.MidlineLeft);
            Stretch(slotSkin.itemNameText.rectTransform);
            slotSkin.itemNameText.rectTransform.offsetMin = new Vector2(212f, 0f);
            slotSkin.itemNameText.rectTransform.offsetMax = new Vector2(-14f, 0f);

            var emptyText = CreateText(slotRect, 23, TextAlignmentOptions.MidlineLeft);
            emptyText.text = VNLocale.T("equip.emptySlot");
            emptyText.color = new Color(1f, 1f, 1f, 0.28f);
            Stretch(emptyText.rectTransform);
            emptyText.rectTransform.offsetMin = new Vector2(212f, 0f);
            emptyText.rectTransform.offsetMax = new Vector2(-14f, 0f);
            slotSkin.emptyMark = emptyText.gameObject;

            return slotSkin;
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
            t.lineSpacing = 15f;
            t.raycastTarget = false;
            return t;
        }

        static void SetAnchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = sizeDelta;
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
