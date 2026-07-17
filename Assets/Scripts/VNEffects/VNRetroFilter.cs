using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>复古滤镜模式</summary>
    public enum VNRetroMode
    {
        None, // 关闭
        Film, // 胶片：颗粒 + 划痕 + 尘点 + 放映抖动（回忆）
        Crt,  // CRT：扫描线 + RGB 荫罩 + 滚动亮带（梦境，柔和版）
    }

    /// <summary>
    /// 胶片颗粒/CRT 滤镜：全屏复古滤镜 overlay（VN/RetroFilter shader）。
    /// 剧本 fx filmgrain on|off（胶片）/ fx crt on|off（CRT），两者互斥自动切换；
    /// mood Memory（回忆）自动上胶片、mood Dream（梦境）自动上 CRT，由 VNStage 联动（可关）。
    /// 挂到 Canvas 下的空 RectTransform 上，Awake 自动建全屏覆盖层，零美术资源。
    /// 嵌套 Canvas 排序 34：盖过舞台/粒子/速度线(25)/水波(26)，低于黑边(35)/对话框(40)。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNRetroFilter : MonoBehaviour
    {
        static readonly int IdIntensity = Shader.PropertyToID("_Intensity");
        static readonly int IdMode = Shader.PropertyToID("_Mode");
        static readonly int IdGrain = Shader.PropertyToID("_GrainAmount");
        static readonly int IdScratch = Shader.PropertyToID("_ScratchAmount");
        static readonly int IdTint = Shader.PropertyToID("_Tint");

        [Tooltip("渲染排序（盖过舞台与水波 26，低于黑边 35 / 对话框 40）")]
        public int sortingOrder = 34;

        [Header("胶片参数")]
        [Tooltip("胶片总强度（0~1）")]
        public float filmIntensity = 1f;
        [Range(0f, 1f)] public float grainAmount = 0.55f;
        [Range(0f, 1f)] public float scratchAmount = 0.7f;
        [Tooltip("胶片色调（微暖泛黄的放映光）")]
        public Color filmTint = new Color(1f, 0.96f, 0.86f, 1f);

        [Header("CRT 参数（柔和版）")]
        [Tooltip("CRT 总强度（0~1；梦境用建议偏低更柔）")]
        public float crtIntensity = 0.8f;
        [Tooltip("CRT 荧光色调（偏冷的屏幕光）")]
        public Color crtTint = new Color(0.85f, 0.95f, 1.1f, 1f);

        [Tooltip("默认淡入/淡出时长（秒）")]
        public float defaultFade = 0.8f;

        [Tooltip("可选：预制的 VN/RetroFilter 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        Material _mat;
        VNRetroMode _mode = VNRetroMode.None;

        public VNRetroMode Mode => _mode;
        public bool IsShown => _mode != VNRetroMode.None;

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

            var go = new GameObject("RetroFilterOverlay",
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
                sourceMaterial.shader.name == "VN/RetroFilter")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/RetroFilter");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/RetroFilter\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _mat.SetFloat(IdIntensity, 0f);
            _img.material = _mat;
            _img.enabled = false;
        }

        /// <summary>切换滤镜模式（None = 淡出关闭）。fade &lt; 0 用默认时长。</summary>
        public void SetMode(VNRetroMode mode, float fade = -1f)
        {
            Build();
            if (_mat == null || mode == _mode) return;
            if (fade < 0f) fade = defaultFade;
            _mode = mode;

            _mat.DOKill();
            if (mode == VNRetroMode.None)
            {
                _mat.DOFloat(0f, IdIntensity, Mathf.Max(0.01f, fade))
                    .OnComplete(() => _img.enabled = false)
                    .SetTarget(_mat).SetLink(gameObject);
                return;
            }

            // 参数按模式配置后淡入；两种滤镜互切时直接换风格再补强度
            bool film = mode == VNRetroMode.Film;
            _mat.SetFloat(IdMode, film ? 0f : 1f);
            _mat.SetFloat(IdGrain, grainAmount);
            _mat.SetFloat(IdScratch, scratchAmount);
            _mat.SetColor(IdTint, film ? filmTint : crtTint);
            _img.enabled = true;
            _mat.DOFloat(film ? filmIntensity : crtIntensity, IdIntensity,
                    Mathf.Max(0.01f, fade))
                .SetTarget(_mat).SetLink(gameObject);
        }

        public void ShowFilm(float fade = -1f) => SetMode(VNRetroMode.Film, fade);
        public void ShowCrt(float fade = -1f) => SetMode(VNRetroMode.Crt, fade);
        public void Hide(float fade = -1f) => SetMode(VNRetroMode.None, fade);

        /// <summary>演示用：无 → 胶片 → CRT → 无 循环</summary>
        public VNRetroMode CycleNext()
        {
            var next = (VNRetroMode)(((int)_mode + 1) % 3);
            SetMode(next);
            return next;
        }

        void OnDestroy()
        {
            if (_mat != null)
            {
                _mat.DOKill();
                Destroy(_mat);
            }
        }
    }
}
