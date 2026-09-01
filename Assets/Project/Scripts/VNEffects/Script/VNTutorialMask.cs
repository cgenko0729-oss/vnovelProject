using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 教程暗幕：铺满整层的一张 RawImage + `VN/TutorialMask` 材质，
    /// 按目标 RectTransform 在暗幕上挖洞（最多 4 个）。
    ///
    /// 【坐标怎么算】
    /// 取目标的**世界四角**再换算到本图的本地坐标，而不是抄 anchoredPosition ——
    /// 立绘挂在 ZoomRoot / TiltRoot 底下，运镜的缩放旋转会让 anchoredPosition
    /// 与屏幕上的实际位置对不上。世界四角天然含了整条父级链的变换。
    ///
    /// 【每帧更新】
    /// 高亮的东西可能在动（立绘、飞行中的球、刚弹出的面板还在做补间），
    /// 所以洞的位置每帧现算。开销就是 4 次 GetWorldCorners，可以忽略。
    ///
    /// 【自己的动画不受暂停影响】
    /// 描边呼吸用 <c>Time.unscaledTime</c> 而不是 <see cref="VNTime"/>：
    /// 教程期间全局是暂停态，用 VNTime 的话描边会僵在那儿不动。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class VNTutorialMask : MonoBehaviour
    {
        const int MaxHoles = 4;

        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdEdgeColor = Shader.PropertyToID("_EdgeColor");
        static readonly int IdEdgeWidth = Shader.PropertyToID("_EdgeWidth");
        static readonly int IdFeather = Shader.PropertyToID("_Feather");
        static readonly int IdCorner = Shader.PropertyToID("_Corner");
        static readonly int IdAspect = Shader.PropertyToID("_Aspect");
        static readonly int IdShape = Shader.PropertyToID("_Shape");
        static readonly int IdHoleCount = Shader.PropertyToID("_HoleCount");
        static readonly int IdHoles = Shader.PropertyToID("_Holes");

        /// <summary>一个洞：要么跟着一个 RectTransform，要么是固定的归一化屏幕矩形</summary>
        public struct Hole
        {
            public RectTransform target;
            public Rect area;       // 归一化（左下原点）；target 为空时用它
            public float padding;   // 像素外扩
        }

        RectTransform _rect;
        RawImage _image;
        Material _mat;

        readonly List<Hole> _holes = new List<Hole>();
        readonly Vector4[] _holeData = new Vector4[MaxHoles];

        /// <summary>可选的材质来源（见 VNTutorialPlayer.maskMaterial 的说明）。Awake 前赋值。</summary>
        public Material sourceMaterial;

        Color _dimColor = new Color(0f, 0f, 0.01f, 0.72f);
        Color _edgeColor = new Color(1.6f, 1.35f, 0.75f, 1f);
        float _edgeWidthPx = 3.5f;
        float _featherPx = 18f;
        float _cornerPx = 22f;
        float _pulse = 0.45f;
        VNTutorialHole _shape = VNTutorialHole.RoundedRect;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _image = GetComponent<RawImage>();
            _image.texture = Texture2D.whiteTexture;
            _image.raycastTarget = true; // 只读演示：洞外洞内一律吃掉点击

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/TutorialMask")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/TutorialMask");
                if (shader == null)
                {
                    // 没有 shader 时退化成一整块纯暗幕：教学文字仍然看得见，
                    // 只是没有洞（比整个教程不显示要好）
                    Debug.LogError("[VNTutorial] 找不到 Shader \"VN/TutorialMask\"，" +
                                   "暗幕退化为整屏压暗（洞挖不出来）。", this);
                    _image.color = _dimColor;
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _image.material = _mat;
            _image.color = Color.white; // 顶点色只当整体 alpha 用，颜色走材质
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        /// <summary>换一步：设置洞与外观参数。holes 传空表示整屏压暗、不挖洞。</summary>
        public void Apply(VNTutorialDef def, VNTutorialStep step, IEnumerable<Hole> holes)
        {
            _holes.Clear();
            if (holes != null)
            {
                foreach (var h in holes)
                {
                    if (_holes.Count >= MaxHoles) break;
                    _holes.Add(h);
                }
            }

            if (def != null)
            {
                _dimColor = new Color(0f, 0f, 0.01f, Mathf.Clamp01(def.dim));
                _edgeColor = def.edgeColor;
                _edgeWidthPx = Mathf.Max(0f, def.edgeWidth);
                _pulse = Mathf.Clamp01(def.edgePulse);
            }
            if (step != null)
            {
                _shape = step.shape;
                _featherPx = Mathf.Max(0f, step.feather);
                _cornerPx = Mathf.Max(0f, step.corner);
            }

            if (_mat == null)
            {
                if (_image != null) _image.color = _dimColor;
                return;
            }
            _mat.SetColor(IdColor, _dimColor);
            _mat.SetFloat(IdShape, _shape == VNTutorialHole.Ellipse ? 1f : 0f);
            Sync();
        }

        void LateUpdate()
        {
            if (_mat == null) return;
            Sync();
        }

        void Sync()
        {
            Rect local = _rect.rect;
            float w = Mathf.Max(1f, local.width);
            float h = Mathf.Max(1f, local.height);

            int count = 0;
            for (int i = 0; i < _holes.Count && count < MaxHoles; i++)
            {
                if (!TryResolve(_holes[i], local, out Vector4 data)) continue;
                _holeData[count++] = data;
            }
            for (int i = count; i < MaxHoles; i++) _holeData[i] = Vector4.zero;

            // 宽高比校正后的空间里，1 单位 = h 像素（见 shader 注释），
            // 所以像素参数一律除以高度
            _mat.SetVectorArray(IdHoles, _holeData);
            _mat.SetFloat(IdHoleCount, count);
            _mat.SetFloat(IdAspect, w / h);
            _mat.SetFloat(IdCorner, _cornerPx / h);
            _mat.SetFloat(IdFeather, Mathf.Max(0.0005f, _featherPx / h));
            _mat.SetFloat(IdEdgeWidth, Mathf.Max(0.0005f, _edgeWidthPx / h));

            // 描边呼吸：教程期间全局暂停，所以必须用真实时间
            float k = _pulse <= 0f
                ? 1f
                : 1f + _pulse * 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f);
            _mat.SetColor(IdEdgeColor, new Color(
                _edgeColor.r * k, _edgeColor.g * k, _edgeColor.b * k, _edgeColor.a));
        }

        /// <summary>把一个洞换算成 uv 空间的 (中心x, 中心y, 半宽, 半高)</summary>
        bool TryResolve(Hole hole, Rect local, out Vector4 data)
        {
            data = Vector4.zero;
            float w = Mathf.Max(1f, local.width);
            float h = Mathf.Max(1f, local.height);

            Vector2 min, max;
            if (hole.target != null)
            {
                // 世界四角 → 本图本地坐标（含整条父级链的缩放/旋转/运镜）
                var corners = _corners;
                hole.target.GetWorldCorners(corners);
                Vector3 p0 = _rect.InverseTransformPoint(corners[0]);
                min = max = new Vector2(p0.x, p0.y);
                for (int i = 1; i < 4; i++)
                {
                    Vector3 p = _rect.InverseTransformPoint(corners[i]);
                    min = Vector2.Min(min, new Vector2(p.x, p.y));
                    max = Vector2.Max(max, new Vector2(p.x, p.y));
                }
                float pad = hole.padding;
                min -= new Vector2(pad, pad);
                max += new Vector2(pad, pad);
            }
            else if (hole.area.width > 0.0001f && hole.area.height > 0.0001f)
            {
                // 归一化屏幕矩形：直接就是 uv，只需把 padding 折算进去
                float padU = hole.padding / w;
                float padV = hole.padding / h;
                data = new Vector4(
                    hole.area.center.x, hole.area.center.y,
                    hole.area.width * 0.5f + padU, hole.area.height * 0.5f + padV);
                return true;
            }
            else return false;

            Vector2 uvMin = new Vector2((min.x - local.xMin) / w, (min.y - local.yMin) / h);
            Vector2 uvMax = new Vector2((max.x - local.xMin) / w, (max.y - local.yMin) / h);
            Vector2 half = (uvMax - uvMin) * 0.5f;
            if (half.x <= 0.0001f || half.y <= 0.0001f) return false;
            data = new Vector4(uvMin.x + half.x, uvMin.y + half.y, half.x, half.y);
            return true;
        }

        static readonly Vector3[] _corners = new Vector3[4];

        /// <summary>第一个洞在屏幕上的归一化矩形（卡片自动避让用）；没有洞返回 false</summary>
        public bool TryGetFirstHoleUv(out Rect uv)
        {
            uv = default;
            if (_holes.Count == 0) return false;
            if (!TryResolve(_holes[0], _rect.rect, out Vector4 d)) return false;
            uv = new Rect(d.x - d.z, d.y - d.w, d.z * 2f, d.w * 2f);
            return true;
        }
    }
}
