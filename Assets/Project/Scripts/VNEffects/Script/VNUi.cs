using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 程序化 UI 的公共构件。文件头写 <c>using static VNEffects.VNUi;</c> 后可直接调用。
    /// 默认值与各模块原有写法保持一致：文字不吃射线，图片默认吃射线（要穿透传 raycast:false）。
    /// </summary>
    public static class VNUi
    {
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform CreateImage(string name, Transform parent, Sprite sprite,
            Color color, bool raycast = true)
        {
            var rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            return rect;
        }

        /// <summary>单行文字（默认居中、不换行）。</summary>
        public static TextMeshProUGUI CreateText(string name, Transform parent, int size, Color color,
            string content, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = VNFont.Asset;
            text.fontSize = size;
            text.color = color;
            text.text = content;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>面板正文（对象名固定 Text、略透明白；lineSpacing 是 TMP 的百分比行距）。</summary>
        public static TextMeshProUGUI CreateText(Transform parent, int size, TextAlignmentOptions anchor,
            float lineSpacing = 0f)
        {
            var go = new GameObject("Text",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = VNFont.Asset;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = new Color(1f, 1f, 1f, 0.94f);
            t.lineSpacing = lineSpacing;
            t.raycastTarget = false;
            return t;
        }

        public static void BindButton(Button button, UnityAction action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }
    }
}
