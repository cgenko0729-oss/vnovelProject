using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 四点自由四边形（uGUI 画不出梯形，Image 只能是矩形）。
    /// 球场的透视地面、场地线的收敛边都靠它。
    /// 四个角是**相对本 RectTransform 原点的局部坐标**。
    ///
    /// ★ [RequireComponent(CanvasRenderer)] 必须**在子类上再写一遍**：
    ///   Graphic 基类上的那条不会被 AddComponent 走继承链读到，
    ///   少了它 CanvasRenderer 不会被自动补上，结果是「一切状态都对但就是不画」。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class VNBadmintonQuad : MaskableGraphic
    {
        public Vector2 bottomLeft, bottomRight, topRight, topLeft;

        public void SetCorners(Vector2 bl, Vector2 br, Vector2 tr, Vector2 tl)
        {
            bottomLeft = bl; bottomRight = br; topRight = tr; topLeft = tl;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var c = color;
            vh.AddVert(bottomLeft, c, new Vector2(0f, 0f));
            vh.AddVert(topLeft, c, new Vector2(0f, 1f));
            vh.AddVert(topRight, c, new Vector2(1f, 1f));
            vh.AddVert(bottomRight, c, new Vector2(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }
    }

    /// <summary>
    /// 羽球模块共用的程序化 UI 辅助（抄 VNQteModule 的 CreateImage/CreateText 那一套，
    /// 因为球场 / 角色 / HUD 三个文件都要用，抽出来避免三份重复）。
    /// </summary>
    public static class VNBadmintonUi
    {
        public static RectTransform CreateNode(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform CreateImage(string name, RectTransform parent,
            Sprite sprite, Color color)
        {
            var rect = CreateNode(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        public static VNBadmintonQuad CreateQuad(string name, RectTransform parent, Color color)
        {
            var rect = CreateNode(name, parent);
            rect.gameObject.AddComponent<CanvasRenderer>();   // 显式补，别依赖 RequireComponent
            var quad = rect.gameObject.AddComponent<VNBadmintonQuad>();
            quad.color = color;
            quad.raycastTarget = false;
            return quad;
        }

        /// <summary>两点之间的一条线（旋转的细长矩形）。粗细单位 px。</summary>
        public static RectTransform CreateLine(string name, RectTransform parent,
            Vector2 from, Vector2 to, float thickness, Color color)
        {
            var rect = CreateImage(name, parent, null, color);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0f, 0.5f);
            Vector2 delta = to - from;
            rect.anchoredPosition = from;
            rect.sizeDelta = new Vector2(delta.magnitude, thickness);
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            return rect;
        }

        public static TextMeshProUGUI CreateText(string name, RectTransform parent,
            int size, Color color, string content,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var rect = CreateNode(name, parent);
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

        public static RectTransform Rect(Component c) => (RectTransform)c.transform;

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>把节点锚到「底边中心」——羽球全场统一用这套坐标（与换算表一致）</summary>
        public static RectTransform AnchorBottomCenter(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }
    }
}
