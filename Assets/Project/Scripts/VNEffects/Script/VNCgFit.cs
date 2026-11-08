using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// CG 的显示方式（非 16:9 的竖图 / 方图用）。
    /// Stretch = 老行为（直接拉满舞台，比例不对就变形），所以是默认值——
    /// 已登记的 CG 一张都不会变样，要修的那几张自己在条目里选或剧本里写 fit:。
    /// </summary>
    public enum VNCgFitMode
    {
        [InspectorName("拉伸 Stretch（老行为）")] Stretch = 0,
        [InspectorName("裁满 Cover")] Cover = 1,
        [InspectorName("模糊填充 Blur")] Blur = 2,
        [InspectorName("模糊+暗角 BlurVig")] BlurVignette = 3,
        [InspectorName("主色渐变 Gradient")] Gradient = 4,
    }

    /// <summary>
    /// 把一张任意比例的 CG **合成成一张舞台尺寸的 sprite**，再交给原来的换图流程。
    ///
    /// 为什么不拆成「模糊底 + 前景」两层 Image：转场（直接过渡几何 shader）、Ken Burns、
    /// 色调匹配、bgscroll 全都假设舞台主图是一张铺满的 sprite；拆两层每个都要改，
    /// 而且转场时两层会分开换、画面割裂。合成一次后它们全部零改动。
    ///
    /// 代价：每种 (sprite, 模式, 尺寸) 首次显示时一次 GPU 合成 + ReadPixels（毫秒级），
    /// 结果缓存（上限 <see cref="CacheLimit"/> 张、约 8MB/张），超了淘汰最久没用、且
    /// 不在画面上的那张。
    /// </summary>
    public static class VNCgFit
    {
        // ---- 视觉参数（与离线对比图一致；用户确认过「就这样」） ----
        const float BgBrightness = 0.65f;
        const float BgSaturation = 0.8f;
        const float BlurVignette = 0.35f;
        const float BlurVigVignette = 0.6f;
        const float BlurVigTint = 0.45f;
        const float GradientVignette = 0.3f;
        const float ShadowAlpha = 0.4f;
        const float ShadowPx = 70f;
        const int BlurSmallWidth = 96;   // 模糊底降采样到这么宽再糊（越小越糊）
        const int BlurPasses = 4;

        const int CacheLimit = 6;
        const int MaxOutputWidth = 2560;

        static Material _mat;
        static bool _shaderMissingWarned;

        struct Key
        {
            public int sprite; public VNCgFitMode mode; public int w, h;
        }

        class Entry { public Key key; public Sprite sprite; public Texture2D tex; public int lastUse; }

        static readonly List<Entry> _cache = new List<Entry>();
        static int _useCounter;

        // ------------------------------------------------------------------
        // 解析（剧本 fit: 参数 / 存档字符串）
        // ------------------------------------------------------------------

        /// <summary>剧本里能写的全部值（编辑器下拉与 Lint 白名单共用）</summary>
        public static readonly string[] ScriptNames = { "stretch", "cover", "blur", "blurvig", "gradient" };

        public static bool TryParse(string s, out VNCgFitMode mode)
        {
            mode = VNCgFitMode.Stretch;
            if (string.IsNullOrEmpty(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "stretch": case "拉伸": mode = VNCgFitMode.Stretch; return true;
                case "cover": case "裁满": mode = VNCgFitMode.Cover; return true;
                case "blur": case "模糊": mode = VNCgFitMode.Blur; return true;
                case "blurvig": case "blurvignette": case "模糊暗角":
                    mode = VNCgFitMode.BlurVignette; return true;
                case "gradient": case "渐变": mode = VNCgFitMode.Gradient; return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // 合成
        // ------------------------------------------------------------------

        /// <summary>
        /// 按模式返回要显示的 sprite。Stretch、尺寸未知、或 shader 缺失时原样返回 source
        /// （退化成老行为，画面至少不会黑）。displaying = 当前舞台上的 sprite，淘汰缓存时跳过它。
        /// </summary>
        public static Sprite Resolve(Sprite source, VNCgFitMode mode, Vector2 stageSize, Sprite displaying = null)
        {
            if (source == null || mode == VNCgFitMode.Stretch) return source;
            if (stageSize.x < 16f || stageSize.y < 16f) stageSize = new Vector2(1920f, 1080f);

            int w = Mathf.RoundToInt(stageSize.x), h = Mathf.RoundToInt(stageSize.y);
            if (w > MaxOutputWidth) { h = Mathf.RoundToInt(h * (float)MaxOutputWidth / w); w = MaxOutputWidth; }

            var key = new Key { sprite = source.GetHashCode(), mode = mode, w = w, h = h };
            foreach (var e in _cache)
            {
                if (e.sprite == null) continue;
                if (e.key.sprite == key.sprite && e.key.mode == mode && e.key.w == w && e.key.h == h)
                {
                    e.lastUse = ++_useCounter;
                    return e.sprite;
                }
            }

            if (!EnsureMaterial()) return source;

            var made = Compose(source, mode, w, h);
            if (made == null) return source;

            Evict(displaying);
            _cache.Add(new Entry { key = key, sprite = made, tex = made.texture, lastUse = ++_useCounter });
            return made;
        }

        static bool EnsureMaterial()
        {
            if (_mat != null) return true;
            var shader = Shader.Find("VN/CgFit");
            if (shader == null || !shader.isSupported)
            {
                if (!_shaderMissingWarned)
                {
                    _shaderMissingWarned = true;
                    Debug.LogWarning("[VNCgFit] 找不到 shader「VN/CgFit」（Build 里要放进 Always Included Shaders），" +
                                     "CG 显示方式退回拉伸");
                }
                return false;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        static Sprite Compose(Sprite source, VNCgFitMode mode, int w, int h)
        {
            var tex = source.texture;
            if (tex == null) return null;

            // sprite 在贴图里的 uv 子区域（图集 sprite 的 texcoord 不是 0~1，同 _UVRect 的坑）
            Rect tr;
            try { tr = source.textureRect; }
            catch { tr = source.rect; } // tight 打包的 sprite 拿不到 textureRect，退回 rect
            var srcRect = new Vector4(tr.x / tex.width, tr.y / tex.height,
                                      tr.width / tex.width, tr.height / tex.height);
            float srcAspect = tr.width / Mathf.Max(1f, tr.height);
            float outAspect = (float)w / h;

            // 前景在输出 uv 里的位置
            Vector4 fgRect;
            if (mode == VNCgFitMode.Cover)
            {
                // 等比放大铺满，多出来的那一维居中裁掉
                if (srcAspect > outAspect) { float fw = srcAspect / outAspect; fgRect = new Vector4(0.5f - fw / 2, 0, 0.5f + fw / 2, 1); }
                else { float fh = outAspect / srcAspect; fgRect = new Vector4(0, 0.5f - fh / 2, 1, 0.5f + fh / 2); }
            }
            else
            {
                // 完整显示（contain），空出来的地方给底图
                if (srcAspect < outAspect) { float fw = srcAspect / outAspect; fgRect = new Vector4(0.5f - fw / 2, 0, 0.5f + fw / 2, 1); }
                else { float fh = outAspect / srcAspect; fgRect = new Vector4(0, 0.5f - fh / 2, 1, 0.5f + fh / 2); }
            }

            var prevActive = RenderTexture.active;
            var temps = new List<RenderTexture>();
            RenderTexture Temp(int tw, int th)
            {
                var rt = RenderTexture.GetTemporary(Mathf.Max(1, tw), Mathf.Max(1, th), 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.filterMode = FilterMode.Bilinear;
                rt.wrapMode = TextureWrapMode.Clamp;
                temps.Add(rt);
                return rt;
            }

            try
            {
                _mat.SetVector("_SrcRect", srcRect);

                // ---- 模糊底：源图按 cover 裁出舞台比例的那块 → 逐级减半降采样 → 小图上反复糊 ----
                RenderTexture blur = null;
                if (mode == VNCgFitMode.Blur || mode == VNCgFitMode.BlurVignette)
                {
                    Vector4 crop;
                    if (srcAspect > outAspect) { float cw = outAspect / srcAspect; crop = new Vector4(0.5f - cw / 2, 0, cw, 1); }
                    else { float ch = srcAspect / outAspect; crop = new Vector4(0, 0.5f - ch / 2, 1, ch); }
                    _mat.SetVector("_CropRect", crop);

                    int cw0 = Mathf.Max(1, Mathf.RoundToInt(tr.width * crop.z));
                    int curW = Mathf.Max(BlurSmallWidth, cw0 / 2);
                    var cur = Temp(curW, Mathf.RoundToInt(curW / outAspect));
                    Graphics.Blit(tex, cur, _mat, 0);
                    while (cur.width / 2 >= BlurSmallWidth)
                    {
                        var next = Temp(cur.width / 2, Mathf.RoundToInt(cur.width / 2 / outAspect));
                        Graphics.Blit(cur, next); // 双线性减半：每级 4 像素平均，不会闪烁走样
                        cur = next;
                    }
                    for (int p = 0; p < BlurPasses; p++)
                    {
                        var next = Temp(cur.width, cur.height);
                        Graphics.Blit(cur, next, _mat, 1);
                        cur = next;
                    }
                    blur = cur;
                }

                _mat.SetTexture("_BlurTex", blur != null ? (Texture)blur : Texture2D.blackTexture);
                _mat.SetVector("_FgRect", fgRect);
                _mat.SetVector("_OutSize", new Vector4(w, h, 0, 0));
                _mat.SetFloat("_Mode", ShaderMode(mode));
                _mat.SetFloat("_BgBrightness", BgBrightness);
                _mat.SetFloat("_BgSaturation", BgSaturation);
                _mat.SetFloat("_Vignette", mode == VNCgFitMode.BlurVignette ? BlurVigVignette
                                         : mode == VNCgFitMode.Gradient ? GradientVignette
                                         : mode == VNCgFitMode.Cover ? 0f : BlurVignette);
                _mat.SetFloat("_TintStrength", BlurVigTint);
                _mat.SetFloat("_ShadowAlpha", mode == VNCgFitMode.Cover ? 0f : ShadowAlpha);
                _mat.SetFloat("_ShadowPx", ShadowPx);

                var outRt = Temp(w, h);
                Graphics.Blit(tex, outRt, _mat, 2);

                // 读回成 Texture2D：Sprite 只能建在 Texture2D 上
                RenderTexture.active = outRt;
                var result = new Texture2D(w, h, TextureFormat.RGBA32, false, false)
                {
                    name = $"CgFit_{source.name}_{mode}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                result.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                result.Apply(false, false); // 保持可读：VNToneMatch 可能要采样像素

                var sprite = Sprite.Create(result, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
                                           source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = result.name;
                sprite.hideFlags = HideFlags.DontSave;
                return sprite;
            }
            finally
            {
                RenderTexture.active = prevActive;
                foreach (var rt in temps) RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>shader 里 _Mode 的编号（与枚举值不同：枚举 0 是 Stretch，根本不进 shader）</summary>
        static float ShaderMode(VNCgFitMode m)
        {
            switch (m)
            {
                case VNCgFitMode.Blur: return 0;
                case VNCgFitMode.BlurVignette: return 1;
                case VNCgFitMode.Gradient: return 2;
                default: return 3; // Cover
            }
        }

        static void DestroyObj(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        static void Evict(Sprite displaying)
        {
            _cache.RemoveAll(e => e.sprite == null);
            while (_cache.Count >= CacheLimit)
            {
                Entry oldest = null;
                foreach (var e in _cache)
                    if (e.sprite != displaying && (oldest == null || e.lastUse < oldest.lastUse)) oldest = e;
                if (oldest == null) break;
                _cache.Remove(oldest);
                DestroyObj(oldest.sprite);
                DestroyObj(oldest.tex);
            }
        }
    }
}
