using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>对话框快捷工具栏：绑定存档、自动、快进、回想、设置等常用操作。</summary>
    public class VNQuickToolbar : MonoBehaviour
    {
        VNScriptRunner _runner;
        GameObject _root;
        VNToolbarActionSlot _autoSlot;
        VNToolbarActionSlot _skipSlot;
        VNQuickToolbarSkin _toolbarSkin;
        RectTransform _dock;


        public void Initialize(VNScriptRunner runner)
        {
            _runner = runner;
            Build();
            VNLocale.LanguageChanged -= OnLanguageChanged;
            VNLocale.LanguageChanged += OnLanguageChanged;
        }

        void OnDestroy() => VNLocale.LanguageChanged -= OnLanguageChanged;

        void OnLanguageChanged()
        {
            if (_root == null) return;
            Destroy(_root);
            _root = null;
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
            if (_toolbarSkin == null)
                throw new System.InvalidOperationException("Quick toolbar prefab is missing or invalid.");

            _root = _toolbarSkin.gameObject;
            AttachRoot();
            ConfigureCanvas();
            BindCustomSlots();
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
        }

        void Update()
        {
            if (_runner == null) return;
            _autoSlot?.SetActiveState(_runner.IsAuto);
            _skipSlot?.SetActiveState(_runner.IsSkipping);
        }
    }
}
