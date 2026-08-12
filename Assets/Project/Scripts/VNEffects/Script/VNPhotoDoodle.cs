using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 涂鸦画布的输入板：把指针位置换成画布 uv 交给 VNPhotoDoodle。
    /// 只在「涂鸦」标签页打开时接收射线，别的时候完全透明不挡人物与贴纸的操作。
    /// </summary>
    public class VNPhotoDoodleInput : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public VNPhotoDoodle doodle;

        RectTransform _rect;

        void Awake() => _rect = (RectTransform)transform;

        public void OnPointerDown(PointerEventData e)
        {
            if (doodle == null || !ToUv(e, out var uv)) return;
            doodle.BeginStroke();
            doodle.StrokeTo(uv);
        }

        public void OnDrag(PointerEventData e)
        {
            if (doodle == null || !ToUv(e, out var uv)) return;
            doodle.StrokeTo(uv);
        }

        public void OnPointerUp(PointerEventData e) => doodle?.EndStroke();

        bool ToUv(PointerEventData e, out Vector2 uv)
        {
            uv = Vector2.zero;
            if (_rect == null) _rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rect, e.position, e.pressEventCamera, out Vector2 local))
                return false;

            var size = _rect.rect.size;
            if (size.x <= 0f || size.y <= 0f) return false;
            uv = new Vector2(local.x / size.x + 0.5f, local.y / size.y + 0.5f);
            return true;
        }
    }

    /// <summary>
    /// 照片上的涂鸦（落書き）：不同颜色的笔画上去、橡皮擦掉、撤销、一键清空。
    ///
    /// 【为什么用位图而不是矢量线段】
    /// 需求里有「擦除」。位图擦除就是把 alpha 抹掉，一行代码；矢量要么只能整笔删除，
    /// 要么得再叠一层遮罩去挖洞——同样的效果，复杂度差一个量级。
    ///
    /// 【为什么是两张画布】
    /// 荧光笔要发光，走的是 VN/Additive（Blend SrcAlpha One + HDR _TintColor），
    /// 普通笔要正常的 Alpha 混合——两种混合模式没法在同一张图里共存，
    /// 所以普通笔与荧光笔各占一张，叠着显示。一笔只会落在其中一张上，
    /// 撤销快照因此也只需要存被改动的那一张（橡皮除外，它两张一起擦）。
    ///
    /// 画布分辨率 768×576，显示时拉伸到取景框（1040×780）——放大倍率控制在 1.35x
    /// 以内，笔刷自带柔边就看不出马赛克；换来的是每帧 Apply 只要半毫秒、
    /// 撤销快照只要 1.7MB。取景框再放大的话这两个数要一起调。
    /// </summary>
    public class VNPhotoDoodle
    {
        public const int Width = 768;
        public const int Height = 576;

        /// <summary>撤销步数。一步最多 3.4MB（橡皮会同时动两张画布）</summary>
        const int MaxUndo = 5;

        // ---- 画笔状态（UI 直接改这几个字段）----
        public Color penColor = new Color(1f, 0.35f, 0.55f, 1f);
        /// <summary>笔尖半径（画布像素）。UI 滑块直接写它</summary>
        public float penSize = 12f;
        public bool eraser;
        public bool glowPen;

        Texture2D _normalTex, _glowTex;
        Color32[] _normal, _glow;
        bool _normalDirty, _glowDirty;
        RawImage _normalImage, _glowImage;
        Image _inputPad;
        Material _glowMaterial;

        /// <summary>一次撤销记录：只存这一笔真正动过的那张画布</summary>
        class Snapshot
        {
            public Color32[] normal;   // null = 这一笔没动普通层
            public Color32[] glow;
        }

        readonly List<Snapshot> _undo = new List<Snapshot>();

        Vector2 _lastPixel;
        bool _hasLast;

        public bool CanUndo => _undo.Count > 0;
        public bool HasContent { get; private set; }

        // ==================================================================
        // 搭建
        // ==================================================================

        /// <summary>在 parent 下铺两张画布 + 一块输入板（默认不接收射线）</summary>
        public void Build(RectTransform parent)
        {
            _normal = NewBuffer();
            _glow = NewBuffer();
            _normalTex = NewTexture("VNDoodleNormal");
            _glowTex = NewTexture("VNDoodleGlow");
            _normalTex.SetPixels32(_normal);
            _normalTex.Apply(false);
            _glowTex.SetPixels32(_glow);
            _glowTex.Apply(false);

            _normalImage = NewLayer("DoodleNormal", parent, _normalTex);

            _glowImage = NewLayer("DoodleGlow", parent, _glowTex);
            var shader = Shader.Find("VN/Additive");
            if (shader != null)
            {
                _glowMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                // HDR >1 才会被 URP 的 Bloom（阈值 1.0）拾取——这就是「荧光」的来源
                _glowMaterial.SetColor("_TintColor", new Color(1.7f, 1.7f, 1.7f, 1f));
                _glowImage.material = _glowMaterial;
            }

            var padGo = new GameObject("DoodleInput", typeof(RectTransform));
            var padRect = (RectTransform)padGo.transform;
            padRect.SetParent(parent, false);
            Stretch(padRect);
            _inputPad = padGo.AddComponent<Image>();
            _inputPad.color = new Color(0f, 0f, 0f, 0f);
            _inputPad.raycastTarget = false;          // 只有进涂鸦页才打开
            padGo.AddComponent<VNPhotoDoodleInput>().doodle = this;
        }

        static Color32[] NewBuffer()
        {
            var buffer = new Color32[Width * Height];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = clear;
            return buffer;
        }

        static Texture2D NewTexture(string name) => new Texture2D(
            Width, Height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

        static RawImage NewLayer(string name, RectTransform parent, Texture2D tex)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            var image = go.AddComponent<RawImage>();
            image.texture = tex;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>进出「涂鸦」标签页时调用：决定这块画布吃不吃鼠标</summary>
        public void SetInteractive(bool on)
        {
            if (_inputPad != null) _inputPad.raycastTarget = on;
        }

        public void Destroy()
        {
            if (_normalTex != null) Object.Destroy(_normalTex);
            if (_glowTex != null) Object.Destroy(_glowTex);
            if (_glowMaterial != null) Object.Destroy(_glowMaterial);
            _undo.Clear();
        }

        // ==================================================================
        // 画
        // ==================================================================

        public void BeginStroke()
        {
            PushUndo(eraser || !glowPen, eraser || glowPen);
            _hasLast = false;
        }

        /// <summary>uv 是取景框内的归一化坐标（左下 0,0）</summary>
        public void StrokeTo(Vector2 uv)
        {
            var p = new Vector2(uv.x * Width, uv.y * Height);

            if (_hasLast)
            {
                // 鼠标一帧能跑很远，必须沿线段补点，否则画出来是一串断掉的圆
                float dist = Vector2.Distance(_lastPixel, p);
                int steps = Mathf.CeilToInt(dist / Mathf.Max(1f, penSize * 0.3f));
                for (int i = 1; i <= steps; i++)
                    Stamp(Vector2.Lerp(_lastPixel, p, i / (float)steps));
            }
            else Stamp(p);

            _lastPixel = p;
            _hasLast = true;
            Flush();
        }

        public void EndStroke()
        {
            _hasLast = false;
            Flush();
        }

        void Stamp(Vector2 center)
        {
            float radius = Mathf.Max(1f, penSize);
            int cx = Mathf.RoundToInt(center.x);
            int cy = Mathf.RoundToInt(center.y);
            int r = Mathf.CeilToInt(radius);
            float feather = Mathf.Max(1f, radius * 0.35f);   // 柔边宽度

            var target = (!eraser && glowPen) ? _glow : _normal;
            var color32 = (Color32)penColor;

            for (int y = -r; y <= r; y++)
            {
                int py = cy + y;
                if (py < 0 || py >= Height) continue;

                for (int x = -r; x <= r; x++)
                {
                    int px = cx + x;
                    if (px < 0 || px >= Width) continue;

                    float d = Mathf.Sqrt(x * x + y * y);
                    float a = Mathf.Clamp01((radius - d) / feather);
                    if (a <= 0f) continue;

                    int idx = py * Width + px;
                    if (eraser)
                    {
                        Erase(_normal, idx, a);
                        Erase(_glow, idx, a);
                    }
                    else Paint(target, idx, a, color32);
                }
            }

            if (eraser) { _normalDirty = true; _glowDirty = true; }
            else if (glowPen) _glowDirty = true;
            else _normalDirty = true;

            if (!eraser) HasContent = true;
        }

        static void Erase(Color32[] buffer, int idx, float strength)
        {
            var c = buffer[idx];
            c.a = (byte)(c.a * (1f - strength));
            buffer[idx] = c;
        }

        /// <summary>
        /// 往画布上抹一笔。不做严格的 source-over 合成——涂鸦不需要，
        /// 直接朝笔色 lerp 反而让反复涂抹更接近真实马克笔的手感。
        /// </summary>
        static void Paint(Color32[] buffer, int idx, float strength, Color32 color)
        {
            var dst = buffer[idx];
            dst.r = (byte)Mathf.Lerp(dst.r, color.r, strength);
            dst.g = (byte)Mathf.Lerp(dst.g, color.g, strength);
            dst.b = (byte)Mathf.Lerp(dst.b, color.b, strength);
            dst.a = (byte)Mathf.Max(dst.a, (byte)(strength * color.a));
            buffer[idx] = dst;
        }

        void Flush()
        {
            if (_normalDirty)
            {
                _normalTex.SetPixels32(_normal);
                _normalTex.Apply(false);
                _normalDirty = false;
            }
            if (_glowDirty)
            {
                _glowTex.SetPixels32(_glow);
                _glowTex.Apply(false);
                _glowDirty = false;
            }
        }

        // ==================================================================
        // 撤销 / 清空
        // ==================================================================

        void PushUndo(bool normal, bool glow)
        {
            var snapshot = new Snapshot
            {
                normal = normal ? (Color32[])_normal.Clone() : null,
                glow = glow ? (Color32[])_glow.Clone() : null,
            };
            _undo.Add(snapshot);
            while (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        }

        public void Undo()
        {
            if (_undo.Count == 0) return;
            var snapshot = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            if (snapshot.normal != null)
            {
                System.Array.Copy(snapshot.normal, _normal, _normal.Length);
                _normalDirty = true;
            }
            if (snapshot.glow != null)
            {
                System.Array.Copy(snapshot.glow, _glow, _glow.Length);
                _glowDirty = true;
            }
            Flush();
            RecheckContent();
        }

        public void Clear()
        {
            PushUndo(true, true);
            var blank = new Color32(255, 255, 255, 0);
            for (int i = 0; i < _normal.Length; i++) { _normal[i] = blank; _glow[i] = blank; }
            _normalDirty = _glowDirty = true;
            Flush();
            HasContent = false;
        }

        void RecheckContent()
        {
            // 撤销之后画布可能已经空了，抽样判断就够（只用来点亮/灰掉按钮）
            for (int i = 0; i < _normal.Length; i += 97)
                if (_normal[i].a > 4 || _glow[i].a > 4) { HasContent = true; return; }
            HasContent = false;
        }
    }
}
