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
        Font _font;

        static readonly Color Normal = new Color(0.045f, 0.06f, 0.105f, 0.94f);
        static readonly Color Active = new Color(0.92f, 0.61f, 0.18f, 0.98f);

        public void Initialize(VNScriptRunner runner)
        {
            _runner = runner;
            Build();
        }

        void Build()
        {
            if (_root != null) return;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            rect.sizeDelta = new Vector2(616f, 42f);

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

            CreateButton("Save", 78f, () => _runner?.RequestSavePanel());
            CreateButton("Load", 78f, () => _runner?.RequestLoadPanel());
            _autoImage = CreateButton("Auto", 78f,
                () => { if (_runner != null) _runner.SetAuto(!_runner.IsAuto); });
            _skipImage = CreateButton("Skip", 78f,
                () => { if (_runner != null) _runner.SetSkip(!_runner.IsSkipping); });
            CreateButton("Log", 72f, () => _runner?.RequestBacklog());
            CreateButton("Config", 88f, () => _runner?.RequestConfigPanel());
            CreateButton("隐藏 UI", 100f, () => _runner?.SetInterfaceHidden(true));
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
                typeof(CanvasRenderer), typeof(Text));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(go.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.font = _font;
            text.text = label;
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
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
