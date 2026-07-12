using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>全屏转场类型</summary>
    public enum VNTransition
    {
        NoiseDissolve, // 噪声溶解（带辉光边缘）
        Blinds,        // 百叶窗
        Tiles,         // 瓦片翻转
        CircleWipe,    // 圆形扩散（可从说话角色位置扩散）
        InkSpread,     // 水墨晕染
        WhiteFlash,    // 爆闪（HDR 白 + Bloom 超亮一瞬间）
        BokehOrbs,     // 光斑虚化（大光斑涌满屏幕，进入回忆）
    }

    /// <summary>
    /// 花式全屏转场库。
    /// 用法：transition.Play(VNTransition.NoiseDissolve, () => { 在全屏被盖住时切换场景内容 });
    /// 流程：图案覆盖率 0→1（转出）→ 执行 onCovered 回调 → 1→0（转入揭示新画面）。
    /// 覆盖层用嵌套 Canvas 排序 100，盖住一切（含粒子）；转场期间拦截点击。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNScreenTransition : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdEdgeColor = Shader.PropertyToID("_EdgeColor");
        static readonly int IdProgress = Shader.PropertyToID("_Progress");
        static readonly int IdMode = Shader.PropertyToID("_Mode");
        static readonly int IdNoiseScale = Shader.PropertyToID("_NoiseScale");
        static readonly int IdCount = Shader.PropertyToID("_Count");
        static readonly int IdCenter = Shader.PropertyToID("_Center");
        static readonly int IdAspect = Shader.PropertyToID("_Aspect");

        [Tooltip("渲染排序（要盖住一切，包括粒子和边缘泛光）")]
        public int sortingOrder = 100;

        [Tooltip("可选：预制的 VN/ScreenTransition 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        Material _mat;
        Sequence _seq;
        VNAmbientParticles _bokehOrbs;

        public bool IsPlaying => _seq != null && _seq.IsActive() && _seq.IsPlaying();

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
            // 拦截转场期间的点击需要 Raycaster
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            var go = new GameObject("TransitionOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var overlayRect = (RectTransform)go.transform;
            overlayRect.SetParent(transform, false);
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;

            _img = go.GetComponent<RawImage>();
            _img.texture = Texture2D.whiteTexture;
            _img.raycastTarget = true; // 转场时挡住输入

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/ScreenTransition")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/ScreenTransition");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/ScreenTransition\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _img.material = _mat;
            _img.enabled = false;
        }

        /// <summary>
        /// 播放转场。onCovered 在画面完全被盖住的瞬间调用（此时切换背景/场景内容）。
        /// outDuration / inDuration 传负值时使用该转场类型的推荐时长。
        /// viewportCenter：CircleWipe / InkSpread 的扩散中心（视口坐标 0~1）。
        /// </summary>
        public Sequence Play(VNTransition type, Action onCovered = null,
            float outDuration = -1f, float inDuration = -1f,
            Color? color = null, Vector2? viewportCenter = null)
        {
            Build();
            _seq?.Kill();

            GetDefaults(type, out float defOut, out float defIn, out Color defColor,
                out int mode, out float noiseScale, out float count);
            if (outDuration < 0f) outDuration = defOut;
            if (inDuration < 0f) inDuration = defIn;

            _mat.SetFloat(IdMode, mode);
            _mat.SetColor(IdColor, color ?? defColor);
            _mat.SetFloat(IdNoiseScale, noiseScale);
            _mat.SetFloat(IdCount, count);
            _mat.SetVector(IdCenter, viewportCenter ?? new Vector2(0.5f, 0.5f));
            _mat.SetFloat(IdAspect, (float)Screen.width / Screen.height);
            _mat.SetFloat(IdProgress, 0f);
            _img.enabled = true;

            if (type == VNTransition.BokehOrbs) StartBokehOrbs();

            var outEase = type == VNTransition.WhiteFlash ? Ease.OutQuad : Ease.InOutSine;

            _seq = DOTween.Sequence()
                .Append(_mat.DOFloat(1f, IdProgress, outDuration).SetEase(outEase))
                .AppendCallback(() => onCovered?.Invoke())
                .AppendInterval(0.08f)
                .Append(_mat.DOFloat(0f, IdProgress, inDuration).SetEase(Ease.InOutSine))
                .OnComplete(() =>
                {
                    _img.enabled = false;
                    if (type == VNTransition.BokehOrbs) StopBokehOrbs();
                })
                .SetLink(gameObject);
            return _seq;
        }

        /// <summary>从某个世界坐标目标（如说话角色）扩散的圆形转场</summary>
        public Sequence PlayFrom(VNTransition type, Transform worldTarget,
            Action onCovered = null, float outDuration = -1f, float inDuration = -1f)
        {
            Vector2 vp = new Vector2(0.5f, 0.5f);
            var cam = Camera.main;
            if (cam != null && worldTarget != null)
            {
                Vector3 p = cam.WorldToViewportPoint(worldTarget.position);
                vp = new Vector2(p.x, p.y);
            }
            return Play(type, onCovered, outDuration, inDuration, null, vp);
        }

        static void GetDefaults(VNTransition type, out float outDur, out float inDur,
            out Color color, out int mode, out float noiseScale, out float count)
        {
            outDur = 0.8f; inDur = 0.8f;
            color = Color.black;
            mode = 0; noiseScale = 7f; count = 9f;
            switch (type)
            {
                case VNTransition.NoiseDissolve:
                    mode = 0; outDur = 0.9f; inDur = 0.9f;
                    break;
                case VNTransition.Blinds:
                    mode = 1; outDur = 0.7f; inDur = 0.7f; count = 9f;
                    break;
                case VNTransition.Tiles:
                    mode = 2; outDur = 0.85f; inDur = 0.85f; count = 10f;
                    break;
                case VNTransition.CircleWipe:
                    mode = 3; outDur = 0.7f; inDur = 0.7f;
                    break;
                case VNTransition.InkSpread:
                    mode = 4; outDur = 1.1f; inDur = 1.0f; noiseScale = 5f;
                    break;
                case VNTransition.WhiteFlash:
                    mode = 5; outDur = 0.22f; inDur = 0.75f;
                    color = new Color(2.2f, 2.15f, 2.0f, 1f); // HDR 白，Bloom 爆亮
                    break;
                case VNTransition.BokehOrbs:
                    mode = 5; outDur = 1.3f; inDur = 1.1f;
                    color = new Color(1.25f, 1.15f, 0.95f, 0.8f); // 柔暖光罩
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 光斑虚化转场的粒子部分
        // ------------------------------------------------------------------

        void StartBokehOrbs()
        {
            if (_bokehOrbs == null)
            {
                _bokehOrbs = VNAmbientParticles.Create(
                    VNAmbientParticles.Preset.Orbs,
                    new Color(1f, 0.9f, 0.72f), sortingOrder + 1,
                    null, 14f, transform, 1.5f);
            }
            _bokehOrbs.SetPlaying(true);
        }

        void StopBokehOrbs()
        {
            if (_bokehOrbs != null) _bokehOrbs.SetPlaying(false);
        }

        void OnDestroy()
        {
            _seq?.Kill();
            if (_mat != null) Destroy(_mat);
        }
    }
}
