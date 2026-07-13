using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 镜头交叉淡化：截取当前整屏画面盖在场景上，镜头瞬切到新状态后把截图淡出，
    /// 两个镜头视角之间就有了"叠化"过渡（camseq 的 start:fade / end:fade / xfade: 用）。
    /// 覆盖层用嵌套 Canvas 排序 90：盖住场景与对话框、但在 ScreenTransition(100) 之下。
    /// 截图是整屏静帧，淡出的零点几秒内粒子等动态元素在旧画面里是冻结的——
    /// camseq 通常在两句台词之间执行，实际观感无差别（各家 VN 引擎通用做法）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNCameraFade : MonoBehaviour
    {
        /// <summary>截屏纵向翻转策略（不同图形 API 的后备缓冲 UV 原点不同）</summary>
        public enum FlipMode
        {
            Auto,      // 按 SystemInfo.graphicsUVStartsAtTop 判断（D3D/Metal 翻转）
            ForceFlip, // 强制翻转
            NoFlip,    // 强制不翻转
        }

        [Tooltip("渲染排序（对话框 40 之上、全屏转场 100 之下）")]
        public int sortingOrder = 90;

        [Tooltip("截图上下颠倒时改这里（Auto 已按平台自动判断）")]
        public FlipMode flip = FlipMode.Auto;

        RawImage _img;
        RenderTexture _rt;
        Tween _fade;

        public bool IsShowing => _img != null && _img.enabled;

        void Build()
        {
            if (_img != null) return;

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            var go = new GameObject("CameraFadeOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var overlayRect = (RectTransform)go.transform;
            overlayRect.SetParent(transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            _img = go.GetComponent<RawImage>();
            _img.raycastTarget = false; // 淡化很短，不拦截输入
            _img.enabled = false;
        }

        void EnsureRT()
        {
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_rt == null)
                _rt = new RenderTexture(w, h, 0);
        }

        /// <summary>
        /// 截取当前帧画面并以不透明状态盖住全屏。
        /// 必须在协程里 yield：截屏要等到帧末（WaitForEndOfFrame）才能拿到完整画面。
        /// 截完后立即改镜头（Cut/复位），下一帧新视角被旧画面盖住 → 再 FadeOut 露出。
        /// </summary>
        public IEnumerator CaptureCo()
        {
            Build();
            _fade?.Kill();
            yield return new WaitForEndOfFrame();
            EnsureRT();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
            _img.texture = _rt;
            bool flipY = ShouldFlipY();
            _img.uvRect = flipY ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
            _img.color = Color.white;
            _img.enabled = true;
        }

        /// <summary>
        /// 复用镜头淡化的帧末截屏管线，生成适合存档网格的低分辨率、方向统一 Texture2D。
        /// 返回纹理由调用方负责 Destroy；不会显示 CameraFadeOverlay。
        /// </summary>
        public IEnumerator CaptureThumbnailCo(int width, int height,
            System.Action<Texture2D> onCaptured)
        {
            yield return new WaitForEndOfFrame();
            EnsureRT();
            ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);

            width = Mathf.Max(16, width);
            height = Mathf.Max(9, height);
            var small = RenderTexture.GetTemporary(width, height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            small.filterMode = FilterMode.Bilinear;

            bool flipY = ShouldFlipY();
            Graphics.Blit(_rt, small, new Vector2(1f, flipY ? -1f : 1f),
                new Vector2(0f, flipY ? 1f : 0f));

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = small;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false)
            {
                name = "PendingSaveThumbnail",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            texture.Apply(false, false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(small);
            onCaptured?.Invoke(texture);
        }

        bool ShouldFlipY() => flip == FlipMode.ForceFlip ||
                              (flip == FlipMode.Auto && SystemInfo.graphicsUVStartsAtTop);

        /// <summary>把截图淡出，露出新镜头画面</summary>
        public Tween FadeOut(float duration)
        {
            if (_img == null || !_img.enabled) return null;
            _fade?.Kill();
            _fade = _img.DOFade(0f, duration).SetEase(Ease.InOutSine)
                .OnComplete(Hide).SetLink(gameObject);
            return _fade;
        }

        public void Hide()
        {
            if (_img != null) _img.enabled = false;
        }

        void OnDestroy()
        {
            _fade?.Kill();
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
        }
    }
}
