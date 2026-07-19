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
            Build();
        }

        void Build()
        {
            if (_root != null) return;

            if (EventSystem.current == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            _root = new GameObject("QuickToolbar", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasRenderer),
                typeof(Image), typeof(HorizontalLayoutGroup));
            var rect = (RectTransform)_root.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-18f, 9f);
            rect.sizeDelta = new Vector2(1013f, 42f); // 十二个固定宽按钮 + 间距/内边距

            // VNDialogueBox 自己是 overrideSorting 的嵌套 Canvas。工具条需要独立
            // GraphicRaycaster 才能接收按钮点击，同时提高一层排序保证文字在按钮底图之上。
            var parentCanvas = GetComponent<Canvas>();
            var toolbarCanvas = _root.GetComponent<Canvas>();
            toolbarCanvas.overrideSorting = true;
            toolbarCanvas.sortingOrder = parentCanvas != null ? parentCanvas.sortingOrder + 1 : 41;

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
            if (_runner == null || _autoImage == null) return;
            _autoImage.color = _runner.IsAuto ? Active : Normal;
            _skipImage.color = _runner.IsSkipping ? Active : Normal;
        }
    }
}
