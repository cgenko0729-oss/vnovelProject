using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 亲密互动的道具光标：系统光标藏起来，用一张 UI 图跟着鼠标跑。
    ///
    /// **为什么不用 Cursor.SetCursor（系统硬件光标）**：硬件光标只能换图，
    /// 做不了持续摆动、按住震动、跟随速度倾斜、悬停 HDR 发光这几件事，
    /// 而这些恰恰是这个玩法的手感来源。代价是光标会受帧率影响、
    /// 且必须自己保证退出时把系统光标还回去（见 <see cref="Dispose"/>）。
    ///
    /// 层次：root 只负责跟随鼠标；图标的摆动/旋转/缩放全作用在子物体 Icon 上，
    /// 两件事分开才不会互相打架（跟随每帧写位置，动画也每帧写位置）。
    ///
    /// 动画不走 DOTween 循环而是 Update 里直接算正弦：光标每帧都要重定位，
    /// 补间和逐帧写位置混在一起只会互相覆盖。
    /// </summary>
    public class VNTouchCursor : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        RectTransform _root;      // 跟随鼠标
        RectTransform _icon;      // 承载动画
        RectTransform _glow;      // 悬停发光
        Image _iconImage;
        Image _glowImage;
        Material _glowMat;

        Camera _uiCamera;
        RectTransform _area;      // 坐标换算用的父 Rect
        VNInteractionItem _item;

        bool _pressed;
        bool _hover;
        float _hoverT;            // 悬停发光的淡入淡出 0~1
        float _tilt;              // 当前倾斜角（平滑后）
        Vector2 _lastScreen;
        bool _hasLast;
        bool _cursorHidden;

        /// <summary>悬停发光色（HDR：>1 才会被 Bloom 抓到；uGUI 顶点色会被钳到 1，
        /// 所以必须走 VN/Additive 材质的 _TintColor）</summary>
        public Color hoverGlowColor = new Color(2.2f, 1.1f, 1.5f, 1f);

        public void Initialize(RectTransform area, Camera uiCamera)
        {
            _area = area;
            _uiCamera = uiCamera;

            _root = (RectTransform)transform;
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = Vector2.zero;

            // 发光层在图标背后
            _glow = NewImage("Glow", _root, VNProceduralTextures.RadialGlowSprite, out _glowImage);
            var shader = Shader.Find("VN/Additive");
            if (shader != null)
            {
                _glowMat = new Material(shader) { hideFlags = HideFlags.DontSave };
                _glowImage.material = _glowMat;
            }
            SetGlowAlpha(0f);

            _icon = NewImage("Icon", _root, null, out _iconImage);
            _iconImage.preserveAspect = true;

            HideSystemCursor();
        }

        public void SetItem(VNInteractionItem item)
        {
            _item = item;
            if (_iconImage == null) return;

            _iconImage.sprite = item != null ? item.icon : null;
            _iconImage.enabled = _iconImage.sprite != null;

            if (item != null && item.icon != null)
            {
                float h = Mathf.Max(8f, item.cursorHeight);
                float aspect = item.icon.rect.width / Mathf.Max(1f, item.icon.rect.height);
                _icon.sizeDelta = new Vector2(h * aspect, h);
                _glow.sizeDelta = Vector2.one * (h * 1.15f);
            }
        }

        /// <summary>模块每帧告知：是否压在可互动部位上 / 是否按住左键</summary>
        public void SetState(bool hover, bool pressed)
        {
            _hover = hover;
            _pressed = pressed;
        }

        void Update()
        {
            if (_root == null || _area == null) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screen = mouse.position.ReadValue();

            // ---- 跟随 ----
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _area, screen, _uiCamera, out Vector2 local))
                _root.anchoredPosition = local;

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            Vector2 velocity = _hasLast ? (screen - _lastScreen) / dt : Vector2.zero;
            _lastScreen = screen;
            _hasLast = true;

            if (_item == null) return;
            float t = Time.unscaledTime;

            // ---- 摆动：待机与按住各一套参数 ----
            bool usePress = _pressed && _item.pressAnim != VNCursorPressAnim.Same;
            float freq = usePress ? _item.pressFrequency : _item.idleFrequency;
            float amp = usePress ? _item.pressAmplitude : _item.idleAmplitude;
            float phase = Mathf.Sin(t * freq * Mathf.PI * 2f);

            Vector2 offset = Vector2.zero;
            float rock = 0f;
            float scale = 1f;

            var anim = usePress ? PressToIdle(_item.pressAnim, _item.idleAnim) : _item.idleAnim;
            switch (anim)
            {
                case VNCursorIdleAnim.SwingX: offset.x = phase * amp; break;
                case VNCursorIdleAnim.SwingY: offset.y = phase * amp; break;
                case VNCursorIdleAnim.Rock: rock = phase * amp; break;
                case VNCursorIdleAnim.Breathe: scale = 1f + phase * amp * 0.01f; break;
            }

            // 震动是两轴都抖，且相位错开，才不会看成一条直线来回
            if (usePress && _item.pressAnim == VNCursorPressAnim.Vibrate)
            {
                offset.x = Mathf.Sin(t * freq * Mathf.PI * 2f) * amp;
                offset.y = Mathf.Sin(t * freq * Mathf.PI * 2f * 1.37f) * amp * 0.6f;
            }
            if (usePress && _item.pressAnim == VNCursorPressAnim.Press)
                scale = 0.88f;

            // ---- 跟随拖动方向倾斜 ----
            float targetTilt = 0f;
            if (_item.tiltWithMotion)
                targetTilt = Mathf.Clamp(-velocity.x * 0.02f, -_item.tiltMax, _item.tiltMax);
            _tilt = Mathf.Lerp(_tilt, targetTilt, 1f - Mathf.Exp(-10f * dt));

            // ---- 悬停发光 + 放大 ----
            _hoverT = Mathf.MoveTowards(_hoverT, _hover ? 1f : 0f, dt * 4f);
            if (_hoverT > 0f)
            {
                float pulse = 1f + Mathf.Sin(t * 4f) * 0.06f;
                scale *= 1f + 0.12f * _hoverT * pulse;
            }
            SetGlowAlpha(_hoverT * (0.55f + 0.25f * Mathf.Sin(t * 5f)));

            // ---- 落位：热点对准鼠标 ----
            Vector2 hotspotOffset = -new Vector2(
                _item.hotspot.x * _icon.sizeDelta.x,
                _item.hotspot.y * _icon.sizeDelta.y);

            _icon.anchoredPosition = hotspotOffset + offset;
            _icon.localRotation = Quaternion.Euler(0f, 0f, _item.iconRotation + rock + _tilt);
            _icon.localScale = Vector3.one * scale;
            _glow.anchoredPosition = hotspotOffset;
        }

        /// <summary>按住动画映射到一种基础摆动（Vibrate/Press 由后面单独覆盖）</summary>
        static VNCursorIdleAnim PressToIdle(VNCursorPressAnim press, VNCursorIdleAnim idle)
        {
            switch (press)
            {
                case VNCursorPressAnim.FastSwing:
                    // 待机不动的道具按住时给个左右快摆，否则「按住有反馈」这件事就没了
                    return idle == VNCursorIdleAnim.None ? VNCursorIdleAnim.SwingX : idle;
                case VNCursorPressAnim.Vibrate: return VNCursorIdleAnim.None;
                case VNCursorPressAnim.Press: return VNCursorIdleAnim.None;
                default: return idle;
            }
        }

        void SetGlowAlpha(float a)
        {
            if (_glowImage == null) return;
            a = Mathf.Clamp01(a);
            if (_glowMat != null)
            {
                var c = hoverGlowColor;
                c.a = a;
                _glowMat.SetColor(IdTintColor, c);   // HDR 走材质，顶点色会被钳到 1
            }
            else
            {
                var c = hoverGlowColor;
                c.a = a;
                _glowImage.color = c;                // 没有 VN/Additive 时的退化路径
            }
            _glowImage.enabled = a > 0.001f;
        }

        void HideSystemCursor()
        {
            if (_cursorHidden) return;
            Cursor.visible = false;
            _cursorHidden = true;
        }

        /// <summary>
        /// 还原系统光标。**任何退出路径都必须走到这里** ——
        /// 漏了的话玩家的鼠标指针会一直消失，是所有 bug 里最难受的一种。
        /// </summary>
        public void Dispose()
        {
            if (_cursorHidden)
            {
                Cursor.visible = true;
                _cursorHidden = false;
            }
            if (_glowMat != null)
            {
                Destroy(_glowMat);
                _glowMat = null;
            }
        }

        void OnDestroy() => Dispose();
        void OnDisable() => Dispose();     // 模块被禁用（调试中断）也要还回去

        static RectTransform NewImage(string name, RectTransform parent, Sprite sprite,
            out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;   // 光标绝不能吃射线，否则道具栏和结束钮点不动
            return rect;
        }
    }
}
