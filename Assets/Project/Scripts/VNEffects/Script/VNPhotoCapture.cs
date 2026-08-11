using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 把取景框那块区域拍成一张 Texture2D。
    ///
    /// 做法与参考实现一致：算出取景框在屏幕上的矩形 → 等这一帧画完 → 整屏抓图 → 裁剪。
    /// 好处是照片天然带上 URP 的 Bloom / Vignette（大头贴要的就是这个味道），
    /// 代价是必须保证快门那一帧取景框上没有别的 UI —— 调用方传 hideDuringShot，
    /// 抓图前一帧把左右装扮栏、倒数数字统统关掉，抓完再开回来。
    ///
    /// ★ 整个「怎么拍」都关在这个文件里：以后想改成独立 Camera + RenderTexture
    ///   （分辨率不再受窗口大小限制），只改这里，模块不用动。
    /// </summary>
    public static class VNPhotoCapture
    {
        /// <summary>照片最大边长上限，防止 4K 屏拍出巨大的 PNG</summary>
        public const int MaxSide = 1600;

        /// <summary>
        /// 拍下 target 覆盖的屏幕区域。协程，需要由 MonoBehaviour 启动。
        /// onDone 收到的 Texture2D 归调用方所有（用完记得 Destroy）；失败时回调 null。
        /// </summary>
        public static IEnumerator Capture(RectTransform target, Canvas canvas,
            IList<GameObject> hideDuringShot, Action<Texture2D> onDone)
        {
            if (target == null || canvas == null)
            {
                onDone?.Invoke(null);
                yield break;
            }

            // 1) 先把不该入镜的 UI 关掉，并记住原状态（只恢复本来就开着的）
            var restore = new List<GameObject>();
            if (hideDuringShot != null)
                foreach (var go in hideDuringShot)
                {
                    if (go == null || !go.activeSelf) continue;
                    go.SetActive(false);
                    restore.Add(go);
                }

            // 2) 关掉之后要等一帧，Canvas 才会真正重画（否则抓到的还是旧画面）
            yield return null;
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            Texture2D full = null;
            try
            {
                RectInt area = ScreenRectOf(target, canvas);
                if (area.width >= 2 && area.height >= 2)
                {
                    full = ScreenCapture.CaptureScreenshotAsTexture();
                    shot = Crop(full, area);
                }
                else
                {
                    Debug.LogWarning("[VNPhoto] 取景框在屏幕上的区域为空，拍照取消");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNPhoto] 抓图失败：{e.Message}");
            }
            finally
            {
                if (full != null) UnityEngine.Object.Destroy(full);
                foreach (var go in restore) if (go != null) go.SetActive(true);
            }

            onDone?.Invoke(shot);
        }

        /// <summary>RectTransform 覆盖的屏幕像素矩形（已裁到屏幕范围内）</summary>
        public static RectInt ScreenRectOf(RectTransform target, Canvas canvas)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);   // 0=左下 1=左上 2=右上 3=右下

            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera;

            Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

            int x0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(a.x, b.x)), 0, Screen.width);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(a.y, b.y)), 0, Screen.height);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(a.x, b.x)), 0, Screen.width);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(a.y, b.y)), 0, Screen.height);

            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>从整屏抓图里裁一块，必要时等比缩到 MaxSide 以内</summary>
        static Texture2D Crop(Texture2D full, RectInt area)
        {
            if (full == null) return null;

            // 抓图尺寸可能因为 DPI 缩放与 Screen.width 不一致，按比例换算
            float sx = (float)full.width / Screen.width;
            float sy = (float)full.height / Screen.height;

            int x = Mathf.Clamp(Mathf.RoundToInt(area.x * sx), 0, full.width - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(area.y * sy), 0, full.height - 1);
            int w = Mathf.Clamp(Mathf.RoundToInt(area.width * sx), 1, full.width - x);
            int h = Mathf.Clamp(Mathf.RoundToInt(area.height * sy), 1, full.height - y);

            var pixels = full.GetPixels(x, y, w, h);
            var shot = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "VNPhotoShot",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            shot.SetPixels(pixels);
            shot.Apply(false, false);

            int longest = Mathf.Max(w, h);
            if (longest <= MaxSide) return shot;

            // 超过上限就等比缩一次（双线性，够用；照片本来就不做放大观察）
            float scale = (float)MaxSide / longest;
            var scaled = Downscale(shot, Mathf.RoundToInt(w * scale), Mathf.RoundToInt(h * scale));
            UnityEngine.Object.Destroy(shot);
            return scaled;
        }

        static Texture2D Downscale(Texture2D source, int width, int height)
        {
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "VNPhotoShot",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = source.GetPixelBilinear(
                        (x + 0.5f) / width, (y + 0.5f) / height);
            result.SetPixels(pixels);
            result.Apply(false, false);
            return result;
        }
    }
}
