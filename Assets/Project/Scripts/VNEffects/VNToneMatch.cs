using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 立绘色调自动匹配背景：切背景时用 GPU 把背景图缩到 4×4 取平均色，
    /// 让所有立绘轻微向该色调偏移（默认 9%）—— 消除"立绘像贴纸"的违和感。
    /// 通过 Image.color 做乘法微染色（归一化保持亮度），不占用特效 shader 的任何参数。
    /// 用法：toneMatch.MatchTo(newBackgroundSprite);（换背景时调用）
    /// </summary>
    public class VNToneMatch : MonoBehaviour
    {
        [Header("被染色的立绘控制器")]
        public VNImageEffectController[] characters;

        [Range(0f, 0.3f)]
        [Header("染色强度（0.05~0.12 为宜，太高会明显变色）")]
        public float strength = 0.09f;

        [Header("染色过渡时长")]
        public float transition = 1.2f;

        /// <summary>匹配到某张背景 Sprite 的平均色</summary>
        public void MatchTo(Sprite bgSprite)
        {
            if (bgSprite == null) { Clear(); return; }
            MatchTo(bgSprite.texture);
        }

        /// <summary>匹配到某张贴图的平均色</summary>
        public void MatchTo(Texture tex)
        {
            if (tex == null) { Clear(); return; }
            Color avg = SampleAverageColor(tex);
            // 归一化（最大分量拉到 1）：只取"色调"，不改变立绘整体亮度
            float m = Mathf.Max(avg.r, Mathf.Max(avg.g, avg.b));
            if (m < 0.001f) { Clear(); return; }
            var toneColor = new Color(avg.r / m, avg.g / m, avg.b / m, 1f);
            var target = Color.Lerp(Color.white, toneColor, strength);
            ApplyTint(target);
        }

        /// <summary>恢复无染色</summary>
        public void Clear() => ApplyTint(Color.white);

        void ApplyTint(Color target)
        {
            if (characters == null) return;
            foreach (var c in characters)
            {
                if (c == null) continue;
                var img = c.GetComponent<Image>();
                if (img == null) continue;
                img.DOColor(target, transition).SetLink(c.gameObject);
            }
        }

        /// <summary>
        /// GPU 均值采样：Blit 到 4×4 RenderTexture 再回读 16 像素求平均。
        /// 不要求贴图开启 Read/Write。
        /// </summary>
        public static Color SampleAverageColor(Texture tex)
        {
            var rt = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var reader = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            reader.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
            reader.Apply(false);
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = reader.GetPixels();
            Color sum = Color.black;
            foreach (var p in pixels) sum += p;
            Object.Destroy(reader);
            return sum / pixels.Length;
        }
    }
}
