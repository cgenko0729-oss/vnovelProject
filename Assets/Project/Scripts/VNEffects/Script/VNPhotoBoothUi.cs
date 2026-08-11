using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 照片上的一张贴纸：左键拖动 / 滚轮缩放 / Shift+滚轮旋转 / 右键删除。
    /// locked = true 的（边框自带装饰）只画不响应。
    ///
    /// 拖动用 uGUI 事件接口（不需要读键盘），缩放旋转要读滚轮，
    /// 所以走新版 Input System 的 Mouse.current —— 项目禁用旧版 Input API。
    /// </summary>
    public class VNPhotoStickerItem : MonoBehaviour,
        IPointerDownHandler, IBeginDragHandler, IDragHandler,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public string stickerId;
        public bool locked;

        /// <summary>可摆放范围（取景框的半宽/半高，允许略微出界）</summary>
        public Vector2 bounds = new Vector2(500f, 380f);

        public Action<VNPhotoStickerItem> onDelete;
        public Action onChanged;      // 任何一次拖/缩/转之后（模块用来重置「未动过」提示）

        RectTransform _rect;
        RectTransform _canvasRect;
        bool _hover;
        float _scale = 1f;
        float _rotation;

        const float MinScale = 0.35f;
        const float MaxScale = 3f;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvasRect = _rect.parent as RectTransform;
        }

        /// <summary>初始摆放（边框自带装饰用资产里的位置，玩家新贴的用中心偏移）</summary>
        public void Place(Vector2 position, float scale, float rotation)
        {
            _rect = (RectTransform)transform;
            _scale = Mathf.Clamp(scale, MinScale, MaxScale);
            _rotation = rotation;
            _rect.anchoredPosition = Clamp(position);
            _rect.localScale = Vector3.one * _scale;
            _rect.localRotation = Quaternion.Euler(0f, 0f, _rotation);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (locked) return;
            transform.SetAsLastSibling();   // 拿起来的那张排到最前
        }

        public void OnBeginDrag(PointerEventData e) { }

        public void OnDrag(PointerEventData e)
        {
            if (locked || _canvasRect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, e.position, e.pressEventCamera, out Vector2 local))
                return;
            _rect.anchoredPosition = Clamp(local);
            onChanged?.Invoke();
        }

        public void OnPointerEnter(PointerEventData e) => _hover = true;
        public void OnPointerExit(PointerEventData e) => _hover = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (locked) return;
            if (e.button == PointerEventData.InputButton.Right)
                onDelete?.Invoke(this);
        }

        void Update()
        {
            if (locked || !_hover) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) < 0.01f) return;

            var keyboard = Keyboard.current;
            bool shift = keyboard != null &&
                         (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);

            if (shift)
            {
                _rotation += Mathf.Sign(wheel) * 12f;
                _rect.localRotation = Quaternion.Euler(0f, 0f, _rotation);
            }
            else
            {
                _scale = Mathf.Clamp(_scale + Mathf.Sign(wheel) * 0.12f, MinScale, MaxScale);
                _rect.localScale = Vector3.one * _scale;
            }
            onChanged?.Invoke();
        }

        Vector2 Clamp(Vector2 pos) => new Vector2(
            Mathf.Clamp(pos.x, -bounds.x, bounds.x),
            Mathf.Clamp(pos.y, -bounds.y, bounds.y));
    }

    /// <summary>
    /// 大头贴模块的程序化 UI 辅助（与 VNBadmintonUi 同一路数：模块只管逻辑，
    /// 怎么摆节点、怎么做滚动列表都在这里，换皮肤时只动这个文件）。
    ///
    /// 全部尺寸按 1920×1080 基准，Canvas 的 CanvasScaler 会等比缩放。
    /// </summary>
    public static class VNPhotoBoothUi
    {
        // ---- 配色（机身米色 + 粉色点缀，对齐参考项目的大头贴机） ----
        public static readonly Color Backdrop = new Color(0.04f, 0.04f, 0.07f, 0.78f);
        public static readonly Color MachineBody = new Color(0.90f, 0.86f, 0.80f, 1f);
        public static readonly Color MachineInner = new Color(0.14f, 0.14f, 0.16f, 1f);
        public static readonly Color PanelBg = new Color(1f, 0.92f, 0.95f, 1f);
        public static readonly Color Accent = new Color(0.24f, 0.51f, 0.96f, 1f);
        public static readonly Color AccentSoft = new Color(0.98f, 0.75f, 0.85f, 1f);
        public static readonly Color CellBg = new Color(0.96f, 0.84f, 0.89f, 1f);
        public static readonly Color CellSelected = new Color(0.99f, 0.62f, 0.76f, 1f);
        public static readonly Color TextDark = new Color(0.22f, 0.18f, 0.22f, 1f);
        public static readonly Color Urgent = new Color(1f, 0.35f, 0.35f, 1f);

        // ---- 基础节点 ----

        public static RectTransform CreateNode(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static Image CreateImage(string name, RectTransform parent, Sprite sprite,
            Color color, bool raycast = false)
        {
            var rect = CreateNode(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            if (sprite != null && sprite.border != Vector4.zero) image.type = Image.Type.Sliced;
            return image;
        }

        public static TextMeshProUGUI CreateText(string name, RectTransform parent, int size,
            Color color, string content,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rect = CreateNode(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        /// <summary>一个矩形按钮（圆角底 + 居中文字）。返回按钮，文字挂在 label 上。</summary>
        public static Button CreateButton(string name, RectTransform parent, Vector2 size,
            Vector2 pos, string label, Color bg, Color textColor, int fontSize,
            out TextMeshProUGUI text)
        {
            var image = CreateImage(name, parent, VNProceduralTextures.RoundedRectSprite, bg, true);
            var rect = (RectTransform)image.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(bg, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(bg, Color.black, 0.12f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            text = CreateText(name + "Label", rect, fontSize, textColor, label);
            Stretch((RectTransform)text.transform);
            return button;
        }

        /// <summary>竖排 / 网格滚动列表。content 是往里塞格子的父节点。</summary>
        public static ScrollRect CreateScrollList(string name, RectTransform parent,
            Vector2 size, Vector2 pos, int columns, Vector2 cellSize, float spacing,
            out RectTransform content)
        {
            var root = CreateNode(name, parent);
            root.sizeDelta = size;
            root.anchoredPosition = pos;

            var viewport = CreateNode("Viewport", root);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            // ScrollRect 需要 viewport 上有 Graphic 才能接住拖动
            var vpImage = viewport.gameObject.AddComponent<Image>();
            vpImage.color = new Color(0f, 0f, 0f, 0f);
            vpImage.raycastTarget = true;

            content = CreateNode("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = new Vector2(spacing, spacing);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;
            return scroll;
        }

        // ---- 立绘取景 ----

        /// <summary>
        /// 把角色摆进取景框（或表情格）。
        ///
        /// ★ 两条硬规则，都是踩过才知道的：
        ///
        /// 1. **必须用表情立绘 GetSprite，不能用 GetPortrait。**
        ///    GetPortrait 在 portraits 列表里找不到同名表情时会退回 portraits[0]，
        ///    也就是不管选哪个表情都返回同一张图——而切表情正是大头贴的核心玩法。
        ///
        /// 2. **不能复用角色资产的 portraitScale。**
        ///    那个值（项目里是 4.96）是为对话框那个 230px 小窗「把脸怼满」标定的；
        ///    合影要的是「头带肩、两人并排」，放大倍率完全不同，所以取景倍率由调用方给。
        ///
        /// slotWidth = 这个角色分到的横向空间；fit = 图片宽度相对 slotWidth 的倍率；
        /// faceAnchor = 脸在立绘里的纵向位置（0 = 图顶，1 = 图底），用来把脸推到窗口中心。
        /// </summary>
        public static void ApplyPortrait(Image image, VNCharacterDef def, string expression,
            float slotWidth, float fit, float faceAnchor, Vector2 extraOffset, bool mirror)
        {
            if (image == null) return;
            if (def == null)
            {
                image.enabled = false;
                return;
            }

            var sprite = def.GetSprite(expression);
            if (sprite == null) sprite = def.GetPortrait(expression);
            if (sprite == null)
            {
                image.enabled = false;
                return;
            }

            image.enabled = true;
            image.sprite = sprite;
            image.preserveAspect = true;

            float width = slotWidth * Mathf.Max(0.05f, fit);
            float height = sprite.rect.width > 0f
                ? sprite.rect.height / sprite.rect.width * width : width;
            var rect = (RectTransform)image.transform;
            rect.sizeDelta = new Vector2(width, height);

            // 把立绘里 faceAnchor 那条横线拉到窗口垂直中心。
            // 脸在图片上半部（faceAnchor < 0.5）→ 图片要往【下】移，所以是负号。
            float lift = -height * (0.5f - Mathf.Clamp01(faceAnchor));
            rect.anchoredPosition = extraOffset + new Vector2(0f, lift);
            rect.localScale = new Vector3(mirror ? -1f : 1f, 1f, 1f);
        }

        // ---- 杂项 ----

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static RectTransform Rect(Component c) => (RectTransform)c.transform;

        /// <summary>让节点在父级里居中定位（默认锚点就是中心，这里只是把意图写明白）</summary>
        public static RectTransform Center(RectTransform rect, Vector2 size, Vector2 pos)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = pos;
            return rect;
        }
    }
}
