using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 教程编辑器画布的底图来源：抓 Game 视图 / 手选图片，存成 PNG 放在仓库外。
    ///
    /// 【Edit Mode 怎么抓】
    /// 走 <see cref="RenderPipeline.SubmitRenderRequest{RequestData}"/>（URP 支持，含后处理），
    /// 主 Canvas 是 Screen Space - Camera 所以一起画进去。Overlay 画布（HUD / 面板）
    /// 不走相机，Edit Mode 下抓不到——但那些本来就是运行时才生成的，Edit Mode 里没有。
    ///
    /// 【Play Mode 怎么抓】
    /// 交给 <see cref="VNTutorialPicker.Capture"/>：帧末 ScreenCapture，Overlay 画布也在。
    ///
    /// 【存哪】
    /// <c>&lt;项目根&gt;/TutorialEditor/Backdrops/</c>，不进 git（同 AiTalkStudio 的记忆预设）。
    /// 底图只是排版参考，不是资产，不该跟着教程一起提交。
    /// </summary>
    public static class VNTutorialBackdrop
    {
        public static string Dir =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TutorialEditor", "Backdrops"));

        /// <summary>Edit Mode：用主相机渲一帧到贴图。失败返回 null 并给出原因。</summary>
        public static Texture2D CaptureEditMode(out string error)
        {
            error = null;
            var cam = Camera.main;
            if (cam == null) cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (cam == null) { error = "场景里没有相机"; return null; }

            int w = Mathf.Max(16, cam.pixelWidth);
            int h = Mathf.Max(16, cam.pixelHeight);
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    // 内置管线兜底
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prev;
                }
                return ReadBack(rt, w, h);
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static Texture2D ReadBack(RenderTexture rt, int w, int h)
        {
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            return tex;
        }

        /// <summary>存成 PNG，返回绝对路径。文件名带时间戳，不覆盖旧图。</summary>
        public static string SavePng(Texture2D tex, string baseName)
        {
            if (tex == null) return null;
            Directory.CreateDirectory(Dir);
            string safe = string.IsNullOrEmpty(baseName) ? "backdrop" : baseName;
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            string path = Path.Combine(Dir, $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            return path;
        }

        /// <summary>
        /// 按记录的路径加载：<c>Assets/…</c> 走 AssetDatabase（手选的项目图），
        /// 其余当绝对路径读 PNG。不存在返回 null。
        /// </summary>
        public static Texture2D Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) return tex;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                return sprite != null ? sprite.texture : null;
            }
            if (!File.Exists(path)) return null;
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            if (!loaded.LoadImage(File.ReadAllBytes(path)))
            {
                UnityEngine.Object.DestroyImmediate(loaded);
                return null;
            }
            return loaded;
        }
    }
}
