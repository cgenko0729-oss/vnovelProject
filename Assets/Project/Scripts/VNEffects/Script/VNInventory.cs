using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static VNEffects.VNUi;

namespace VNEffects
{
    /// <summary>
    /// 背包（I 键）：左侧道具一览 + 右侧 7 格装备栏 + 底部介绍区。
    /// 持有数全部存 VNFlags（flag 名 = 道具_&lt;id&gt;），装备状态见 VNEquipment；
    /// 文案/图标/装备数据从登记的 VNShopDef 商品清单里找（未登记的道具用 id 照常显示）。
    /// 道具行左键选中看介绍，右键弹出 装备/卸下/使用 菜单；装备格右键直接卸下。
    /// 外观支持全局主题 prefab（VNSystemUiSkinSet.inventoryPrefab + VNInventorySkin 槽位），
    /// 缺失或校验失败时直接抛异常。
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
        // 构建
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
            if (_skin == null)
                throw new System.InvalidOperationException("Inventory prefab is missing or invalid.");

            _panel = _skin.panelRoot;
            _scroll = _skin.itemContent.GetComponentInParent<ScrollRect>(true);
            _skin.rowTemplate.gameObject.SetActive(false);

            // 教程锚点（面板 / 道具列表 / 介绍区；装备格在下面的循环里逐个登记）
            VNTutorialAnchors.Register(VNUiAnchors.AnchorInventory, (RectTransform)_panel.transform);
            VNTutorialAnchors.Register(VNUiAnchors.AnchorInventoryItems,
                _scroll != null ? (RectTransform)_scroll.transform : _skin.itemContent);
            if (_skin.detailText != null)
                VNTutorialAnchors.Register(VNUiAnchors.AnchorInventoryDetail,
                    (RectTransform)_skin.detailText.transform);

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
                VNTutorialAnchors.Register(VNUiAnchors.InventorySlot(slot),
                    (RectTransform)view.button.transform);
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

    }
}
