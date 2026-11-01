using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>快捷功能条摆在哪（VNGameConfig.toolbarPlacement）</summary>
    public enum VNToolbarPlacement
    {
        /// <summary>停靠对话框：皮肤 toolbarAnchor > 皮肤 panel > 对话框根（老行为）</summary>
        DialogueDock,
        /// <summary>右下角自动隐藏：平时沉在屏幕底下只露小把手，鼠标移到把手附近才滑出</summary>
        CornerAutoHide,
    }

    public enum VNScreenEdge { Top, Bottom }
    public enum VNHandleAlign { Left, Right }

    /// <summary>贴边自动隐藏的参数（单位：秒 / 画布像素，1920×1080 基准）</summary>
    [Serializable]
    public class VNEdgeAutoHideParams
    {
        [Tooltip("滑出用时（秒）")]
        public float revealDuration = 0.25f;
        [Tooltip("鼠标离开后停多久才开始收起（秒）。太短的话鼠标在边缘晃一下就闪")]
        public float hideDelay = 0.8f;
        [Tooltip("收起用时（秒）")]
        public float hideDuration = 0.4f;
        [Tooltip("展开后离屏幕边的距离。只对快捷功能条生效（属性条位置沿用 prefab）")]
        public Vector2 margin = new Vector2(12f, 10f);
        [Tooltip("收起状态下的感应区尺寸：以把手为中心、贴着屏幕边")]
        public Vector2 triggerSize = new Vector2(240f, 100f);
        [Tooltip("展开后，鼠标在条外多少像素内仍算「还在」，免得移去点按钮时它先缩回去")]
        public float keepPadding = 24f;
        [Tooltip("被代码主动叫出来（如属性变化）时，到位后至少停留几秒")]
        public float peekHold = 2f;
    }

    /// <summary>
    /// 贴边自动隐藏：一条 UI 平时藏到屏幕边外只露一个小把手，鼠标移到把手附近才滑出来。
    /// 用在右下角快捷功能条（Bottom/Right）与顶部属性条（Top/Left）。
    ///
    /// 层级：
    ///   Holder（铺满父级；可选自带 overrideSorting 的 Canvas）
    ///   ├── Trigger  收起时的感应区（不挂 Graphic）
    ///   └── Slot     铺满、只做上下平移
    ///       ├── Handle  小把手（每帧贴到条的端点外侧）
    ///       └── 被收纳的 UI 根（锚点原样保留，所以 prefab 摆在哪就还在哪）
    ///
    /// 【为什么感应区不挂 Image】判断鼠标靠近走 Mouse.current 的位置，不走射线。
    /// 铺透明 raycastTarget Image 当感应区的话点击会被吃掉，玩家在那里点一下推进台词就没反应。
    ///
    /// 【为什么藏起位置每帧重算】隐藏偏移取决于画布尺寸，而 CanvasScaler 在创建当帧
    /// 可能还没生效、玩家也可能改分辨率；收起且没在补间时直接贴回去最省心。
    /// </summary>
    public class VNEdgeAutoHide : MonoBehaviour
    {
        const float HandleInset = 16f;

        VNEdgeAutoHideParams _cfg;
        VNScreenEdge _edge;
        VNHandleAlign _align;
        Canvas _rootCanvas;
        RectTransform _holder;
        RectTransform _slot;
        RectTransform _trigger;
        RectTransform _handle;
        RectTransform _bar;
        Transform _moveRoot;
        CanvasGroup _group;
        CanvasGroup _handleGroup;

        bool _expanded;
        bool _suppressed;
        float _leaveTimer;
        float _peekUntil;
        float _waitSince = -1f;
        Tween _slideTween;
        Tween _handleTween;
        readonly Vector3[] _corners = new Vector3[4];

        VNTutorialPlayer _tutorial;
        float _nextTutorialLookup;

        /// <param name="sortingOrder">≥0 时自带 overrideSorting Canvas（父级排序压不住别的层时用）</param>
        public static VNEdgeAutoHide Create(Transform parent, string name, VNScreenEdge edge,
            VNHandleAlign align, VNEdgeAutoHideParams cfg, int sortingOrder = -1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            if (sortingOrder >= 0)
            {
                var canvas = go.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
            }

            var hide = go.AddComponent<VNEdgeAutoHide>();
            hide._cfg = cfg ?? new VNEdgeAutoHideParams();
            hide._edge = edge;
            hide._align = align;
            hide._holder = rect;
            var parentCanvas = parent.GetComponentInParent<Canvas>();
            hide._rootCanvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            hide._group = go.AddComponent<CanvasGroup>();
            hide.BuildFrame();
            return hide;
        }

        bool IsTop => _edge == VNScreenEdge.Top;
        bool IsLeft => _align == VNHandleAlign.Left;

        void BuildFrame()
        {
            _trigger = NewRect("Trigger", transform);
            float edgeY = IsTop ? 1f : 0f;
            _trigger.anchorMin = _trigger.anchorMax = new Vector2(0f, edgeY);
            _trigger.pivot = new Vector2(0.5f, edgeY);
            _trigger.sizeDelta = _cfg.triggerSize;

            _slot = NewRect("Slot", transform);
            Stretch(_slot);

            BuildHandle();
        }

        void BuildHandle()
        {
            _handle = NewRect("Handle", _slot);
            _handle.anchorMin = _handle.anchorMax = new Vector2(0.5f, 0.5f);
            // 上边的条：把手挂在条底下；下边的条：把手顶在条上面
            _handle.pivot = new Vector2(IsLeft ? 0f : 1f, IsTop ? 1f : 0f);
            _handle.sizeDelta = new Vector2(64f, 20f);
            _handleGroup = _handle.gameObject.AddComponent<CanvasGroup>();
            _handleGroup.blocksRaycasts = false;

            var bg = _handle.gameObject.AddComponent<Image>();
            bg.sprite = VNProceduralTextures.RoundedRectSprite;
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 2.5f; // 9-slice 边距 22px 比把手还高，缩一下圆角
            bg.color = new Color(0.08f, 0.09f, 0.14f, 0.55f);
            bg.raycastTarget = false;

            // 「≡」三道横线：用 Image 画而不用文字，不依赖字体里有没有这个字形
            for (int i = 0; i < 3; i++)
            {
                var line = NewRect("Line" + i, _handle);
                line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 0.5f);
                line.anchoredPosition = new Vector2(0f, (i - 1) * 4.5f);
                line.sizeDelta = new Vector2(22f, 2f);
                var img = line.gameObject.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.85f);
                img.raycastTarget = false;
            }
        }

        /// <summary>
        /// 收纳一条 UI。moveRoot = 要搬进来的根（锚点原样保留），bar = 用来量尺寸、
        /// 判定范围、贴把手的那块矩形（皮肤 prefab 里两者可能不是同一个物体）。
        /// </summary>
        public void Attach(Transform moveRoot, RectTransform bar)
        {
            _moveRoot = moveRoot;
            _bar = bar;
            moveRoot.SetParent(_slot, false);
            SnapCollapsed();
        }

        /// <summary>整套（含把手）藏起来并直接收回去，例如「隐藏界面」</summary>
        public void SetSuppressed(bool suppressed)
        {
            _suppressed = suppressed;
            _group.alpha = suppressed ? 0f : 1f;
            _group.blocksRaycasts = !suppressed;
            if (suppressed) SnapCollapsed();
        }

        /// <summary>代码主动叫出来，到位后至少停留 peekHold 秒（之后照常走 hideDelay 收起）</summary>
        public void Peek()
        {
            _peekUntil = Time.unscaledTime + _cfg.revealDuration + _cfg.peekHold;
        }

        /// <summary>
        /// 给「先等条滑出来再播演出」用：返回 true = 还在滑，调用方这一帧先别播。
        /// 条被隐藏 / 被压制时直接返回 false；最多等 revealDuration + 0.5 秒防卡死。
        /// </summary>
        public bool HoldForReveal()
        {
            if (!CanShow)
            {
                _waitSince = -1f;
                return false;
            }
            Peek();
            bool shown = _expanded && (_slideTween == null || !_slideTween.IsActive());
            if (shown)
            {
                _waitSince = -1f;
                return false;
            }
            if (_waitSince < 0f) _waitSince = Time.unscaledTime;
            if (Time.unscaledTime - _waitSince > _cfg.revealDuration + 0.5f)
            {
                _waitSince = -1f;
                return false;
            }
            return true;
        }

        public void SnapCollapsed()
        {
            _slideTween?.Kill();
            _handleTween?.Kill();
            _expanded = false;
            _leaveTimer = 0f;
            _peekUntil = 0f;
            if (_bar != null) _slot.anchoredPosition = new Vector2(0f, HiddenOffset());
            _handleGroup.alpha = 1f;
        }

        bool BarOn => _bar != null && _moveRoot != null &&
                      _moveRoot.gameObject.activeInHierarchy && _bar.gameObject.activeInHierarchy;
        bool CanShow => !_suppressed && BarOn;

        void Update()
        {
            if (_bar == null) return;

            bool barOn = BarOn;
            if (_handle.gameObject.activeSelf != barOn) _handle.gameObject.SetActive(barOn);
            if (!barOn || _suppressed) return;

            bool want = Time.unscaledTime < _peekUntil || IsTutorialHighlighting() || IsMouseNear();
            if (want)
            {
                _leaveTimer = 0f;
                if (!_expanded) Expand();
            }
            else if (_expanded)
            {
                _leaveTimer += Time.unscaledDeltaTime;
                if (_leaveTimer >= _cfg.hideDelay) Collapse();
            }

            if (!_expanded && (_slideTween == null || !_slideTween.IsActive()))
                _slot.anchoredPosition = new Vector2(0f, HiddenOffset());
        }

        void LateUpdate()
        {
            if (!BarOn) return;
            // 把手贴在条的端点外侧（条和把手同在 Slot 里一起平移）
            _bar.GetWorldCorners(_corners);
            int idx = IsTop ? (IsLeft ? 0 : 3) : (IsLeft ? 1 : 2); // BL / BR / TL / TR
            Vector3 c = _slot.InverseTransformPoint(_corners[idx]);
            _handle.localPosition = new Vector3(c.x + (IsLeft ? HandleInset : -HandleInset), c.y, 0f);

            // 感应区跟着把手的横向位置（贴着屏幕边，不随 Slot 动）
            Vector3 hc = _holder.InverseTransformPoint(_handle.TransformPoint(_handle.rect.center));
            _trigger.anchoredPosition = new Vector2(hc.x - _holder.rect.xMin, 0f);
        }

        /// <summary>条在 Holder 空间里的上下沿（扣掉 Slot 当前的平移）</summary>
        void BarBoundsY(out float yMin, out float yMax)
        {
            _bar.GetWorldCorners(_corners);
            yMin = float.MaxValue;
            yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float y = _holder.InverseTransformPoint(_corners[i]).y;
                yMin = Mathf.Min(yMin, y);
                yMax = Mathf.Max(yMax, y);
            }
            float off = _slot.anchoredPosition.y;
            yMin -= off;
            yMax -= off;
        }

        /// <summary>条整个移出屏幕时 Slot 的平移量：条的内侧边正好贴屏幕边，把手刚好全露</summary>
        float HiddenOffset()
        {
            BarBoundsY(out float yMin, out float yMax);
            Rect r = _holder.rect;
            return IsTop ? r.yMax - yMin : r.yMin - yMax;
        }

        Camera EventCamera =>
            _rootCanvas == null || _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : _rootCanvas.worldCamera;

        bool IsMouseNear()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 pos = mouse.position.ReadValue();
            Camera cam = EventCamera;
            if (RectTransformUtility.RectangleContainsScreenPoint(_trigger, pos, cam)) return true;
            return _expanded && InKeepZone(pos, cam);
        }

        /// <summary>展开后的「还在」范围：条外扩 keepPadding，朝屏幕边一侧延伸到底，并含把手</summary>
        bool InKeepZone(Vector2 screen, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_holder, screen, cam, out var p))
                return false;
            _bar.GetWorldCorners(_corners);
            float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 v = _holder.InverseTransformPoint(_corners[i]);
                xMin = Mathf.Min(xMin, v.x); xMax = Mathf.Max(xMax, v.x);
                yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
            }
            float pad = _cfg.keepPadding;
            float handleH = _handle.rect.height;
            xMin -= pad;
            xMax += pad;
            if (IsTop) { yMin -= pad + handleH; yMax = float.MaxValue; }
            else { yMax += pad + handleH; yMin = float.MinValue; }
            return p.x >= xMin && p.x <= xMax && p.y >= yMin && p.y <= yMax;
        }

        /// <summary>教程讲到这条 UI 时强制展开（否则洞挖在屏幕外）。播放器查找限频，没有播放器时别每帧 Find。</summary>
        bool IsTutorialHighlighting()
        {
            if (_tutorial == null)
            {
                if (Time.unscaledTime < _nextTutorialLookup) return false;
                _nextTutorialLookup = Time.unscaledTime + 1f;
                _tutorial = VNTutorialPlayer.Instance;
                if (_tutorial == null) return false;
            }
            return _tutorial.IsHighlighting(_bar);
        }

        void Expand()
        {
            _expanded = true;
            _slideTween?.Kill();
            _handleTween?.Kill();
            _slideTween = _slot.DOAnchorPosY(0f, _cfg.revealDuration)
                .SetEase(Ease.OutCubic).SetUpdate(true).SetLink(gameObject);
            _handleTween = _handleGroup.DOFade(0f, _cfg.revealDuration * 0.6f)
                .SetUpdate(true).SetLink(gameObject);
        }

        void Collapse()
        {
            _expanded = false;
            _leaveTimer = 0f;
            _slideTween?.Kill();
            _handleTween?.Kill();
            _slideTween = _slot.DOAnchorPosY(HiddenOffset(), _cfg.hideDuration)
                .SetEase(Ease.InOutSine).SetUpdate(true).SetLink(gameObject);
            _handleTween = _handleGroup.DOFade(1f, _cfg.hideDuration)
                .SetDelay(_cfg.hideDuration * 0.5f).SetUpdate(true).SetLink(gameObject);
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
