using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 把一张 Sprite 构造成互不共享顶点的三角碎片网格。
    /// 每个三角形把中心和随机种子写入额外 UV，交给 DirectBackgroundTransition Shader
    /// 在一个 draw call 中完成独立位移、旋转、缩小与淡出。
    /// </summary>
    public class VNShatterGraphic : MaskableGraphic
    {
        [SerializeField] Sprite sprite;
        [Min(2)] public int columns = 14;
        [Min(2)] public int rows = 8;

        public Sprite Sprite
        {
            get => sprite;
            set
            {
                if (sprite == value) return;
                sprite = value;
                SetVerticesDirty();
                SetMaterialDirty();
            }
        }

        public override Texture mainTexture => sprite != null && sprite.texture != null
            ? sprite.texture : Texture2D.whiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (sprite == null) return;

            Rect rect = GetPixelAdjustedRect();
            Vector4 outerUv = DataUtility.GetOuterUV(sprite);
            int cols = Mathf.Max(2, columns);
            int rowCount = Mathf.Max(2, rows);

            for (int y = 0; y < rowCount; y++)
            {
                float y0 = (float)y / rowCount;
                float y1 = (float)(y + 1) / rowCount;
                for (int x = 0; x < cols; x++)
                {
                    float x0 = (float)x / cols;
                    float x1 = (float)(x + 1) / cols;
                    Vector2 p00 = Position(rect, x0, y0);
                    Vector2 p10 = Position(rect, x1, y0);
                    Vector2 p01 = Position(rect, x0, y1);
                    Vector2 p11 = Position(rect, x1, y1);
                    Vector2 uv00 = TextureUv(outerUv, x0, y0);
                    Vector2 uv10 = TextureUv(outerUv, x1, y0);
                    Vector2 uv01 = TextureUv(outerUv, x0, y1);
                    Vector2 uv11 = TextureUv(outerUv, x1, y1);

                    // 交替对角线，避免所有碎片呈现过于规则的同向纹理。
                    if (((x + y) & 1) == 0)
                    {
                        AddShard(vh, rect, p00, p10, p11, uv00, uv10, uv11, x, y, 0);
                        AddShard(vh, rect, p00, p11, p01, uv00, uv11, uv01, x, y, 1);
                    }
                    else
                    {
                        AddShard(vh, rect, p00, p10, p01, uv00, uv10, uv01, x, y, 0);
                        AddShard(vh, rect, p10, p11, p01, uv10, uv11, uv01, x, y, 1);
                    }
                }
            }
        }

        void AddShard(VertexHelper vh, Rect rect, Vector2 a, Vector2 b, Vector2 c,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, int x, int y, int half)
        {
            Vector2 center = (a + b + c) / 3f;
            Vector2 center01 = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, center.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, center.y));
            float seed = Hash01(x * 2 + half, y);
            int start = vh.currentVertCount;
            AddVertex(vh, a, uvA, center01, seed);
            AddVertex(vh, b, uvB, center01, seed);
            AddVertex(vh, c, uvC, center01, seed);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        void AddVertex(VertexHelper vh, Vector2 position, Vector2 uv,
            Vector2 center01, float seed)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = position;
            v.color = color;
            v.uv0 = uv;
            v.uv1 = center01;
            v.uv2 = new Vector2(seed, 0f);
            vh.AddVert(v);
        }

        static Vector2 Position(Rect rect, float x, float y) =>
            new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, x), Mathf.Lerp(rect.yMin, rect.yMax, y));

        static Vector2 TextureUv(Vector4 uv, float x, float y) =>
            new Vector2(Mathf.Lerp(uv.x, uv.z, x), Mathf.Lerp(uv.y, uv.w, y));

        static float Hash01(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
