using System.Collections.Generic;
using System.Text;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 立绘情绪叠加层：在立绘之上叠若干张透明图（潮红 / 汗 / 泪），
    /// 每层各有 0~1 的强度，可**多层共存**并连续变化。
    ///
    /// 为什么不做成表情：「表情 × 潮红三档」是乘法爆炸，每个组合都得画一张完整立绘；
    /// 叠加层是加法，一张潮红图配所有表情，而且强度能连续补间而不是跳变。
    ///
    /// 做法与 <see cref="VNCharacterBlinkOverlay"/> / <see cref="VNCharacterMouth"/> 一致：
    /// 子物体铺满、共用立绘的材质实例（溶解/闪白/调色对叠加层同步生效）、不吃射线。
    /// </summary>
    public class VNCharacterOverlay : MonoBehaviour
    {
        class Layer
        {
            public string id;
            public Image image;
            public float maxAlpha;
            public float strength;
            public Tween tween;
        }

        readonly List<Layer> _layers = new List<Layer>();

        public void Initialize(VNCharacterDef definition, Material sharedMaterial)
        {
            if (definition == null || definition.overlays == null) return;

            foreach (var def in definition.overlays)
            {
                if (def == null || string.IsNullOrEmpty(def.id) || def.sprite == null) continue;

                var go = new GameObject("Overlay_" + def.id,
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);

                var img = go.GetComponent<Image>();
                img.sprite = def.sprite;
                img.preserveAspect = false;
                img.raycastTarget = false;
                if (sharedMaterial != null) img.material = sharedMaterial;

                var color = img.color;
                color.a = 0f;
                img.color = color;
                img.enabled = false;

                _layers.Add(new Layer
                {
                    id = def.id,
                    image = img,
                    maxAlpha = Mathf.Clamp01(def.maxAlpha),
                    strength = 0f,
                });
            }
        }

        /// <summary>设置某层强度 0~1。duration &lt;= 0 立即生效。找不到该层返回 false。</summary>
        public bool SetStrength(string id, float strength, float duration = 0.35f)
        {
            var layer = Find(id);
            if (layer == null) return false;

            strength = Mathf.Clamp01(strength);
            layer.strength = strength;
            layer.tween?.Kill();

            float target = strength * layer.maxAlpha;
            if (target > 0.001f) layer.image.enabled = true;

            if (duration <= 0.01f)
            {
                SetAlpha(layer, target);
                return true;
            }

            layer.tween = DOTween.To(() => layer.image.color.a, a => SetAlpha(layer, a),
                    target, duration)
                .SetUpdate(true)
                .SetLink(gameObject);
            return true;
        }

        public float GetStrength(string id) => Find(id)?.strength ?? 0f;

        public void ClearAll(float duration = 0.3f)
        {
            foreach (var layer in _layers) SetStrength(layer.id, 0f, duration);
        }

        void SetAlpha(Layer layer, float a)
        {
            var c = layer.image.color;
            c.a = a;
            layer.image.color = c;
            if (a <= 0.001f) layer.image.enabled = false;
        }

        Layer Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var layer in _layers)
                if (layer.id == id) return layer;
            return null;
        }

        // ------------------------------------------------------------------
        // 存档（格式：id=强度|id=强度，与漫符的 SerializeKeep 同风格）
        // ------------------------------------------------------------------

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var layer in _layers)
            {
                if (layer.strength <= 0.001f) continue;
                if (sb.Length > 0) sb.Append('|');
                sb.Append(layer.id).Append('=').Append(layer.strength.ToString("0.###"));
            }
            return sb.ToString();
        }

        /// <summary>读档 / 调试重建：直接落位，不补间</summary>
        public void Restore(string data)
        {
            foreach (var layer in _layers) SetStrength(layer.id, 0f, 0f);
            if (string.IsNullOrEmpty(data)) return;

            foreach (var part in data.Split('|'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string id = part.Substring(0, eq);
                if (float.TryParse(part.Substring(eq + 1), out float v))
                    SetStrength(id, v, 0f);
            }
        }
    }
}
