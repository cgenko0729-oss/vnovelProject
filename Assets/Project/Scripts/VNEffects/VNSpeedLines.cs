using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 漫画速度线/集中线：全屏放射线 overlay。
    /// 三张程序化贴图变体轮换"闪帧"（换贴图 + 微旋转/缩放抖动），模拟手绘逐帧闪化；
    /// 加法混合 + HDR 颜色，配合 Bloom 有轻微辉光。
    /// Show()/Hide()/Toggle() 持续开关（剧本 fx speedlines on/off）；
    /// Burst() 一次性冲击演出（拉满后自动淡出，剧本 fx speedlines burst）。
    /// 挂到 Canvas 下的空 RectTransform 上，Awake 自动建全屏贴图，零美术资源。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNSpeedLines : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        [Header("渲染排序（盖过粒子 10~31 与情绪泛光 20，低于对话框 40）")]
        public int sortingOrder = 25;

        [Header("线条颜色（alpha 为整体强度）")]
        public Color color = new Color(1f, 1f, 1f, 0.8f);

        [Header("HDR 强度倍率，>1 时配合 Bloom 产生辉光")]
        public float hdrIntensity = 1.25f;

        [Header("闪帧间隔（秒）：每隔这么久换一张贴图变体并抖动角度")]
        public float flickerInterval = 0.09f;

        [Header("闪帧时的随机旋转抖动幅度（度）")]
        public float rotationJitter = 4f;

        [Header("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        RectTransform _imgRect;
        Material _mat;
        CanvasGroup _group;
        bool _shown;
        int _variant;
        float _timer;
        Tween _burstTween;

        public bool IsShown => _shown;

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

            // 嵌套 Canvas 覆盖排序：盖住舞台和粒子，仍在对话框（40）之下
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var go = new GameObject("SpeedLineImage",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            _imgRect = (RectTransform)go.transform;
            _imgRect.SetParent(transform, false);
            _imgRect.anchorMin = Vector2.zero;
            _imgRect.anchorMax = Vector2.one;
            // 四边溢出 25%，旋转/缩放抖动时不露出贴图边缘
            _imgRect.offsetMin = new Vector2(-480f, -270f);
            _imgRect.offsetMax = new Vector2(480f, 270f);

            _img = go.GetComponent<RawImage>();
            _img.texture = VNProceduralTextures.SpeedLines(0);
            _img.raycastTarget = false;

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Additive")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Additive");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Additive\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _mat.mainTexture = _img.texture;
            var hdr = color * hdrIntensity;
            hdr.a = color.a;
            _mat.SetColor(IdTintColor, hdr);
            _img.material = _mat;
        }

        void Update()
        {
            if (_group == null || _group.alpha <= 0.001f) return;
            _timer += Time.deltaTime;
            if (_timer < flickerInterval) return;
            _timer = 0f;

            // 闪帧：轮换贴图变体 + 微旋转/缩放抖动
            _variant = (_variant + 1) % VNProceduralTextures.SpeedLineVariantCount;
            var tex = VNProceduralTextures.SpeedLines(_variant);
            _img.texture = tex;
            _mat.mainTexture = tex;
            _imgRect.localRotation = Quaternion.Euler(0f, 0f,
                Random.Range(-rotationJitter, rotationJitter));
            _imgRect.localScale = Vector3.one * Random.Range(1f, 1.045f);
        }

        /// <summary>淡入显示（持续闪帧，直到 Hide）</summary>
        public void Show(float fade = 0.25f)
        {
            Build();
            _burstTween?.Kill();
            _shown = true;
            _group.DOKill();
            _group.DOFade(1f, fade).SetLink(gameObject);
        }

        /// <summary>淡出隐藏</summary>
        public void Hide(float fade = 0.2f)
        {
            if (_group == null) return;
            _burstTween?.Kill();
            _shown = false;
            _group.DOKill();
            _group.DOFade(0f, fade).SetLink(gameObject);
        }

        public void Toggle()
        {
            if (_shown) Hide();
            else Show();
        }

        /// <summary>一次性冲击演出：瞬间拉满 → 保持 → 快速淡出（决断/惊愕瞬间用）</summary>
        public void Burst(float duration = 0.7f)
        {
            Build();
            _shown = false;
            _group.DOKill();
            _burstTween?.Kill();
            _group.alpha = 1f;
            _burstTween = DOVirtual.DelayedCall(Mathf.Max(0.05f, duration - 0.25f),
                    () => _group.DOFade(0f, 0.25f).SetLink(gameObject))
                .SetLink(gameObject);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
