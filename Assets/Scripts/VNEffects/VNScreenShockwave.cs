using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 全屏情绪水波：点击涟漪的全屏版——受击/震惊时整个画面荡开一圈波纹。
    /// 三件套合成一次冲击：
    ///   1. VN/Shockwave overlay：HDR 波峰环 + 尾随涟漪 + 波谷微暗从中心扩散；
    ///   2. targets（通常是背景）的波浪 UV 扭曲脉冲（快起慢落包络），画面真的在"荡"；
    ///   3. 可选联动 VNScreenShake 轻震动，强化受击感。
    /// 剧本 fx shockwave [light|heavy]；一次性演出，不记录开关状态。
    /// 挂到 Canvas 下的空 RectTransform 上，Awake 自动建全屏覆盖层，零美术资源。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNScreenShockwave : MonoBehaviour
    {
        static readonly int IdProgress = Shader.PropertyToID("_Progress");
        static readonly int IdStrength = Shader.PropertyToID("_Strength");
        static readonly int IdCenter = Shader.PropertyToID("_Center");
        static readonly int IdAspect = Shader.PropertyToID("_Aspect");

        [Tooltip("渲染排序（盖过粒子与速度线 25，低于黑边 35 / 对话框 40）")]
        public int sortingOrder = 26;

        [Tooltip("单次波纹扩散时长（秒）")]
        public float duration = 0.95f;

        [Tooltip("被波纹扭曲的图片（通常只放背景；立绘不加可避免脸部扭曲）")]
        public VNImageEffectController[] targets;

        [Tooltip("扭曲脉冲峰值（UV 偏移量，strength=1 时）")]
        public float waveAmount = 0.014f;
        public float waveSpeed = 26f;
        public float waveFrequency = 30f;

        [Tooltip("可选：联动轻震动强化受击感")]
        public VNScreenShake screenShake;
        public bool withShake = true;

        [Tooltip("可选：预制的 VN/Shockwave 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        Material _mat;
        Sequence _seq;
        Tween _waveTween;

        void Awake()
        {
            Build();
        }

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

            var go = new GameObject("ShockwaveOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var overlayRect = (RectTransform)go.transform;
            overlayRect.SetParent(transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            _img = go.GetComponent<RawImage>();
            _img.texture = Texture2D.whiteTexture;
            _img.raycastTarget = false;

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Shockwave")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Shockwave");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Shockwave\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _img.material = _mat;
            _img.enabled = false;
        }

        /// <summary>
        /// 荡一圈波纹。strength：0.6 轻 / 1 标准 / 1.4 重；
        /// viewportCenter：波源（视口坐标 0~1），默认屏幕中心。
        /// </summary>
        public void Play(float strength = 1f, Vector2? viewportCenter = null)
        {
            Build();
            if (_mat == null) return;

            _seq?.Kill();
            _waveTween?.Kill();

            _mat.SetVector(IdCenter, viewportCenter ?? new Vector2(0.5f, 0.5f));
            _mat.SetFloat(IdAspect, (float)Screen.width / Screen.height);
            _mat.SetFloat(IdStrength, Mathf.Clamp(strength, 0.2f, 2f));
            _mat.SetFloat(IdProgress, 0f);
            _img.enabled = true;

            // 波纹环扩散：先快后慢，像真实水波逐渐减速
            _seq = DOTween.Sequence()
                .Append(_mat.DOFloat(1f, IdProgress, duration).SetEase(Ease.OutQuad))
                .OnComplete(() => _img.enabled = false)
                .SetLink(gameObject);

            // 画面扭曲脉冲：前 15% 快速拉满，剩余时间缓慢归零
            if (targets != null && targets.Length > 0)
            {
                float peak = waveAmount * strength;
                _waveTween = DOVirtual.Float(0f, 1f, duration, v =>
                {
                    float envelope = v < 0.15f ? v / 0.15f : 1f - (v - 0.15f) / 0.85f;
                    foreach (var t in targets)
                        if (t != null) t.SetWave(peak * envelope, waveSpeed, waveFrequency);
                }).SetEase(Ease.Linear).SetLink(gameObject)
                  .OnKill(() =>
                  {
                      foreach (var t in targets)
                          if (t != null) t.SetWave(0f);
                  });
            }

            if (withShake && screenShake != null)
                screenShake.Shake(strength >= 1.2f ? VNShakeLevel.Medium : VNShakeLevel.Light);
        }

        /// <summary>从世界坐标目标（如受击角色）位置荡开</summary>
        public void PlayFrom(Transform worldTarget, float strength = 1f)
        {
            Vector2 vp = new Vector2(0.5f, 0.5f);
            var cam = Camera.main;
            if (cam != null && worldTarget != null)
            {
                Vector3 p = cam.WorldToViewportPoint(worldTarget.position);
                vp = new Vector2(p.x, p.y);
            }
            Play(strength, vp);
        }

        void OnDestroy()
        {
            _seq?.Kill();
            _waveTween?.Kill();
            if (_mat != null) Destroy(_mat);
        }
    }
}
