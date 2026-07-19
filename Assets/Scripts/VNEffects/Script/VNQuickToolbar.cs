using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>对话框右上角快捷功能条：Save / Load / Auto / Skip / Log / Config / Hide UI。</summary>
    public class VNQuickToolbar : MonoBehaviour
    {
        VNScriptRunner _runner;
        GameObject _root;
        Image _autoImage;
        Image _skipImage;
        VNToolbarActionSlot _autoSlot;
        VNToolbarActionSlot _skipSlot;
        VNQuickToolbarSkin _toolbarSkin;
        RectTransform _dock; // 停靠点（皮肤 toolbarAnchor/panel）；null = 对话框根（老位置）

        static readonly Color Normal = new Color(0.045f, 0.06f, 0.105f, 0.94f);
        static readonly Color Active = new Color(0.92f, 0.61f, 0.18f, 0.98f);

        public void Initialize(VNScriptRunner runner)
        {
            _runner = runner;
            Build();
            // Initialize 可能被多次调用，先退订保证只挂一次
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

        /// <summary>语言切换：功能条常驻可见，销毁重建即刷新全部按钮文案</summary>
        void OnLanguageChanged()
        {
            if (_root == null) return;
            Destroy(_root);
            _root = null;
            _autoImage = null;
            _skipImage = null;
            _autoSlot = null;
            _skipSlot = null;
            _toolbarSkin = null;
            Build();
        }

        void Build()
        {
            if (_root != null) return;

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var skinPrefab = VNSystemUiSkinUtility.Prefab(s => s.quickToolbarPrefab);
            _toolbarSkin = VNSystemUiSkinUtility.Instantiate<VNQuickToolbarSkin>(
                skinPrefab, transform, "VNQuickToolbar");
            if (_toolbarSkin != null)
            {
                _root = _toolbarSkin.gameObject;
                AttachRoot();
                ConfigureCanvas();
                BindCustomSlots();
                return;
            }

            _root = new GameObject("QuickToolbar", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasRenderer),
                typeof(Image), typeof(HorizontalLayoutGroup));
            AttachRoot();
            var rect = (RectTransform)_root.transform;
            rect.sizeDelta = new Vector2(1013f, 42f); // 十二个固定宽按钮 + 间距/内边距

            ConfigureCanvas();

            // VNDialogueBox 自己是 overrideSorting 的嵌套 Canvas。工具条需要独立
            // GraphicRaycaster 才能接收按钮点击，同时提高一层排序保证文字在按钮底图之上。
            var bg = _root.GetComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.color = new Color(0.018f, 0.026f, 0.052f, 0.92f);

            var layout = _root.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(7, 7, 5, 5);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            CreateButton(VNLocale.T("toolbar.save"), 78f, () => _runner?.RequestSavePanel());
            CreateButton(VNLocale.T("toolbar.load"), 78f, () => _runner?.RequestLoadPanel());
            CreateButton(VNLocale.T("toolbar.qsave"), 78f, () => _runner?.QuickSave());
            CreateButton(VNLocale.T("toolbar.qload"), 78f, () => _runner?.QuickLoad());
            _autoImage = CreateButton(VNLocale.T("toolbar.auto"), 78f,
                () => { if (_runner != null) _runner.SetAuto(!_runner.IsAuto); });
            _skipImage = CreateButton(VNLocale.T("toolbar.skip"), 78f,
                () => { if (_runner != null) _runner.SetSkip(!_runner.IsSkipping); });
            CreateButton(VNLocale.T("toolbar.log"), 72f, () => _runner?.RequestBacklog());
            CreateButton(VNLocale.T("toolbar.quest"), 72f, () => _runner?.RequestQuestLog());
            CreateButton(VNLocale.T("toolbar.stats"), 72f, () => _runner?.RequestStatsPanel());
            CreateButton(VNLocale.T("toolbar.inventory"), 72f, () => _runner?.RequestInventory());
            CreateButton(VNLocale.T("toolbar.gallery"), 62f, () => _runner?.RequestCgGallery());
            CreateButton(VNLocale.T("toolbar.config"), 88f, () => _runner?.RequestConfigPanel());
            CreateButton(VNLocale.T("toolbar.hideui"), 100f, () => _runner?.SetInterfaceHidden(true));
        }

        void ConfigureCanvas()
        {
            var toolbarCanvas = _root.GetComponent<Canvas>();
            if (toolbarCanvas == null) toolbarCanvas = _root.AddComponent<Canvas>();
            if (_root.GetComponent<GraphicRaycaster>() == null) _root.AddComponent<GraphicRaycaster>();
            var parentCanvas = GetComponent<Canvas>();
            toolbarCanvas.overrideSorting = true;
            toolbarCanvas.sortingOrder = parentCanvas != null ? parentCanvas.sortingOrder + 1 : 41;
        }

        void BindCustomSlots()
        {
            foreach (var slot in _toolbarSkin.Slots)
            {
                if (slot == null || slot.button == null) continue;
                if (slot.label != null) slot.label.text = LabelFor(slot.action);
                slot.button.onClick.RemoveAllListeners();
                slot.button.onClick.AddListener(() => Execute(slot.action));
                if (slot.action == VNToolbarAction.Auto) _autoSlot = slot;
                else if (slot.action == VNToolbarAction.Skip) _skipSlot = slot;
            }
        }

        static string LabelFor(VNToolbarAction action)
        {
            switch (action)
            {
                case VNToolbarAction.Save: return VNLocale.T("toolbar.save");
                case VNToolbarAction.Load: return VNLocale.T("toolbar.load");
                case VNToolbarAction.QuickSave: return VNLocale.T("toolbar.qsave");
                case VNToolbarAction.QuickLoad: return VNLocale.T("toolbar.qload");
                case VNToolbarAction.Auto: return VNLocale.T("toolbar.auto");
                case VNToolbarAction.Skip: return VNLocale.T("toolbar.skip");
                case VNToolbarAction.Backlog: return VNLocale.T("toolbar.log");
                case VNToolbarAction.Quest: return VNLocale.T("toolbar.quest");
                case VNToolbarAction.Stats: return VNLocale.T("toolbar.stats");
                case VNToolbarAction.Inventory: return VNLocale.T("toolbar.inventory");
                case VNToolbarAction.Gallery: return VNLocale.T("toolbar.gallery");
                case VNToolbarAction.Config: return VNLocale.T("toolbar.config");
                case VNToolbarAction.HideUi: return VNLocale.T("toolbar.hideui");
                default: return action.ToString();
            }
        }

        void Execute(VNToolbarAction action)
        {
            switch (action)
            {
                case VNToolbarAction.Save: _runner?.RequestSavePanel(); break;
                case VNToolbarAction.Load: _runner?.RequestLoadPanel(); break;
                case VNToolbarAction.QuickSave: _runner?.QuickSave(); break;
                case VNToolbarAction.QuickLoad: _runner?.QuickLoad(); break;
                case VNToolbarAction.Auto:
                    if (_runner != null) _runner.SetAuto(!_runner.IsAuto);
                    break;
                case VNToolbarAction.Skip:
                    if (_runner != null) _runner.SetSkip(!_runner.IsSkipping);
                    break;
                case VNToolbarAction.Backlog: _runner?.RequestBacklog(); break;
                case VNToolbarAction.Quest: _runner?.RequestQuestLog(); break;
                case VNToolbarAction.Stats: _runner?.RequestStatsPanel(); break;
                case VNToolbarAction.Inventory: _runner?.RequestInventory(); break;
                case VNToolbarAction.Gallery: _runner?.RequestCgGallery(); break;
                case VNToolbarAction.Config: _runner?.RequestConfigPanel(); break;
                case VNToolbarAction.HideUi: _runner?.SetInterfaceHidden(true); break;
            }
        }

        /// <summary>
        /// 对话框皮肤切换时的停靠：挂到 dock 的右上角（null = 挂回对话框根的老位置）。
        /// 旧皮肤销毁是延迟的，本帧内重新挂接即可把功能条从将亡层级里救出来。
        /// </summary>
        public void SetDock(RectTransform dock)
        {
            _dock = dock;
            if (_root == null) { Build(); return; }
            AttachRoot();
        }

        void AttachRoot()
        {
            var rect = (RectTransform)_root.transform;
            Transform parent = _dock != null ? (Transform)_dock : transform;
            rect.SetParent(parent, false);
            if (_toolbarSkin != null) return; // 自定义 prefab 保留自己的锚点、轴心和位置
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-18f, 9f);
        }

        Image CreateButton(string label, float width, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_root.transform, false);
            go.GetComponent<LayoutElement>().preferredWidth = width;
            var image = go.GetComponent<Image>();
            image.sprite = VNProceduralTextures.RoundedRectSprite;
            image.type = Image.Type.Sliced;
            image.color = Normal;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            var textGo = new GameObject("Label", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(go.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.text = label;
            text.fontSize = 16;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.94f, 0.95f, 1f, 1f);
            text.raycastTarget = false;
            return image;
        }

        void Update()
        {
            if (_runner == null) return;
            if (_autoImage != null) _autoImage.color = _runner.IsAuto ? Active : Normal;
            if (_skipImage != null) _skipImage.color = _runner.IsSkipping ? Active : Normal;
            _autoSlot?.SetActiveState(_runner.IsAuto);
            _skipSlot?.SetActiveState(_runner.IsSkipping);
        }
    }
}
