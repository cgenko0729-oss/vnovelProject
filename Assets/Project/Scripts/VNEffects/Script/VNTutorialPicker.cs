#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 教程编辑器在 Play Mode 里用的「现场拾取器」（整个文件只在编辑器里编译）。
    ///
    /// 【它解决什么】
    /// 锚点只在运行时由模块 Register，Edit Mode 下根本不存在；归一化矩形靠脑补更是
    /// 一轮一轮试。这个组件让作者在 Game 视图里 **Ctrl+左键点一下** 要讲的控件：
    ///   - 点中的控件（或它的某级父物体）登记过锚点 → 回传锚点 id
    ///   - 没登记 → 回传它的归一化屏幕矩形，直接写进步骤的 area
    ///
    /// 【点中的是文字还是按钮？】
    /// 射线命中的往往是按钮里的 TMP 文字。文字不是作者想要的，所以先向上找
    /// 最近的 Selectable（Button 之类）当结果；编辑器窗口还有「↑ 父级」可继续放大范围。
    ///
    /// 【不占 VNPause】
    /// 拾取时游戏不冻结——冻结会顺带屏蔽 I/J/C 这些开面板的快捷键，作者就打不开
    /// 要讲的面板了。想让画面静止先开「真机预览」（教程层自己会占暂停），
    /// 预览层的暗幕会吃射线，所以命中它的一律跳过。
    ///
    /// 【生命周期】
    /// 隐藏物体 + HideAndDontSave，退出 Play Mode 随场景一起没了；
    /// 编辑器窗口每次进 Play Mode 后重新 <see cref="Ensure"/>。
    /// </summary>
    public class VNTutorialPicker : MonoBehaviour
    {
        public struct PickResult
        {
            public string anchor;        // 登记过的锚点 id；null = 没登记
            public Rect area;            // 归一化屏幕矩形（左下原点）
            public string path;          // 层级路径（给作者看的）
            public RectTransform rect;   // 实际选中的控件
        }

        const float FlashSeconds = 0.9f;

        static VNTutorialPicker _instance;

        /// <summary>已有的实例；没有返回 null（不自动创建）</summary>
        public static VNTutorialPicker Instance => _instance;

        /// <summary>取或建（仅 Play Mode）</summary>
        public static VNTutorialPicker Ensure()
        {
            if (!Application.isPlaying) return null;
            if (_instance == null)
            {
                var go = new GameObject("VNTutorialPicker");
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<VNTutorialPicker>();
            }
            return _instance;
        }

        /// <summary>Ctrl+左键点中东西后回调（编辑器窗口订阅）</summary>
        public event Action<PickResult> Picked;

        /// <summary>开着才响应 Ctrl+左键</summary>
        public bool Armed { get; set; }

        /// <summary>上一次拾取的控件（「↑ 父级」从它往上走）</summary>
        public RectTransform LastRect { get; private set; }

        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        // ---- 闪一下：独立的最高层 Overlay 画布 + 一个描边框 ----
        Canvas _flashCanvas;
        RectTransform _flashRect;
        Image _flashImage;
        float _flashUntil;

        void Awake()
        {
            if (_instance == null) _instance = this;
        }

        void OnDestroy()
        {
            if (_flashCanvas != null) Destroy(_flashCanvas.gameObject);
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            UpdateFlash();
            if (!Armed) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (!ctrl || !mouse.leftButton.wasPressedThisFrame) return;

            var hit = HitUnder(mouse.position.ReadValue());
            if (hit == null)
            {
                Picked?.Invoke(new PickResult { area = Rect.zero });
                return;
            }
            var result = Describe(hit, raw: false);
            LastRect = result.rect;
            Flash(result.rect);
            Picked?.Invoke(result);
        }

        // ------------------------------------------------------------------

        /// <summary>屏幕坐标下最上层的 UI 控件（跳过教程层自己）</summary>
        RectTransform HitUnder(Vector2 screenPos)
        {
            if (EventSystem.current == null) return null;
            var data = new PointerEventData(EventSystem.current) { position = screenPos };
            _hits.Clear();
            EventSystem.current.RaycastAll(data, _hits);
            foreach (var h in _hits)
            {
                if (h.gameObject == null || IsTutorialLayer(h.gameObject)) continue;
                if (h.gameObject.transform is RectTransform rt) return rt;
            }
            return null;
        }

        static bool IsTutorialLayer(GameObject go)
        {
            if (go.GetComponent<VNTutorialMask>() != null) return true;
            if (go.GetComponentInParent<VNTutorialPlayer>() != null) return true;
            // 教程层搬到独立 Overlay 画布时不在播放器底下，按宿主名认
            var root = go.transform.root;
            return root != null && root.name == "VNTutorialOverlayCanvas";
        }

        /// <summary>
        /// 把一个控件描述成拾取结果。raw = false 时会向上找登记过的锚点 / 最近的 Selectable；
        /// raw = true 就用这个控件本身（「↑ 父级」用）。
        /// </summary>
        public PickResult Describe(RectTransform rt, bool raw)
        {
            if (rt == null) return new PickResult { area = Rect.zero };

            if (!raw)
            {
                // 1) 本身或某级父物体登记过锚点 → 锚点优先（运镜/布局改了照样跟得上）
                for (var t = rt; t != null; t = t.parent as RectTransform)
                {
                    string id = VNTutorialAnchors.FindId(t);
                    if (id != null)
                        return new PickResult
                        { anchor = id, rect = t, area = NormalizedRect(t), path = PathOf(t) };
                }
                // 2) 命中的常是按钮里的文字：向上找最近的 Selectable 当目标
                var sel = rt.GetComponentInParent<Selectable>();
                if (sel != null) rt = (RectTransform)sel.transform;
            }
            return new PickResult { anchor = null, rect = rt, area = NormalizedRect(rt), path = PathOf(rt) };
        }

        /// <summary>把上次拾取的范围放大到父级（一路只取原始矩形，不再向上找锚点）</summary>
        public void PickParent()
        {
            if (LastRect == null || !(LastRect.parent is RectTransform parent)) return;
            LastRect = parent;
            var result = Describe(parent, raw: true);
            Flash(parent);
            Picked?.Invoke(result);
        }

        /// <summary>RectTransform 在屏幕上的归一化矩形（左下原点，0~1，已裁到屏内）</summary>
        public static Rect NormalizedRect(RectTransform rt)
        {
            if (rt == null) return Rect.zero;
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.rootCanvas.worldCamera;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            float w = Mathf.Max(1f, Screen.width), h = Mathf.Max(1f, Screen.height);
            float x0 = Mathf.Clamp01(min.x / w), y0 = Mathf.Clamp01(min.y / h);
            float x1 = Mathf.Clamp01(max.x / w), y1 = Mathf.Clamp01(max.y / h);
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null; cur = cur.parent) parts.Add(cur.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ------------------------------------------------------------------
        // 闪一下
        // ------------------------------------------------------------------

        public void Flash(RectTransform rt)
        {
            if (rt == null) return;
            Flash(NormalizedRect(rt));
        }

        /// <summary>在 Game 视图上按归一化矩形闪一个黄框（悬停锚点清单 / 拾取成功时）</summary>
        public void Flash(Rect normalized)
        {
            EnsureFlash();
            float w = Screen.width, h = Screen.height;
            _flashRect.anchoredPosition = new Vector2(normalized.x * w, normalized.y * h);
            _flashRect.sizeDelta = new Vector2(Mathf.Max(4f, normalized.width * w),
                                               Mathf.Max(4f, normalized.height * h));
            _flashImage.color = new Color(1f, 0.9f, 0.25f, 0.95f);
            _flashCanvas.gameObject.SetActive(true);
            _flashUntil = Time.unscaledTime + FlashSeconds;
        }

        void EnsureFlash()
        {
            if (_flashCanvas != null) return;
            var go = new GameObject("VNTutorialPickerFlash", typeof(Canvas));
            go.hideFlags = HideFlags.HideAndDontSave;
            _flashCanvas = go.GetComponent<Canvas>();
            _flashCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _flashCanvas.sortingOrder = 32000;          // 压过 Toast(999) 与一切

            var frame = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image));
            _flashRect = (RectTransform)frame.transform;
            _flashRect.SetParent(go.transform, false);
            // 没挂 CanvasScaler：1 单位 = 1 像素，直接按屏幕坐标摆
            _flashRect.anchorMin = _flashRect.anchorMax = Vector2.zero;
            _flashRect.pivot = Vector2.zero;
            _flashImage = frame.GetComponent<Image>();
            _flashImage.sprite = VNProceduralTextures.RoundedFrameSprite;
            _flashImage.type = Image.Type.Sliced;
            _flashImage.raycastTarget = false;
            go.SetActive(false);
        }

        void UpdateFlash()
        {
            if (_flashCanvas == null || !_flashCanvas.gameObject.activeSelf) return;
            float left = _flashUntil - Time.unscaledTime;
            if (left <= 0f) { _flashCanvas.gameObject.SetActive(false); return; }
            var c = _flashImage.color;
            c.a = Mathf.Clamp01(left / FlashSeconds) * 0.95f;
            _flashImage.color = c;
        }

        // ------------------------------------------------------------------
        // 抓 Game 视图（Play Mode 专用：要等到帧末，Overlay 画布才画完）
        // ------------------------------------------------------------------

        public void Capture(Action<Texture2D> onDone) => StartCoroutine(CaptureCo(onDone));

        IEnumerator CaptureCo(Action<Texture2D> onDone)
        {
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try { tex = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (Exception e) { Debug.LogWarning("[VNTutorial] 抓屏失败：" + e.Message); }
            onDone?.Invoke(tex);
        }
    }
}
#endif
