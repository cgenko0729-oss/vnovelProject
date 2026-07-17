using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 光线穿透效果（God Rays）：从画面上方斜射下来数道半透明光束，
    /// 缓慢摆动 + 透明度呼吸，适合教室、树林、窗边等场景。
    /// 挂到 Canvas 下的一个空 RectTransform 上（渲染顺序放在背景之后、立绘之前），
    /// Awake 时程序化生成所有光束，零美术资源。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNGodRays : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        [Header("光束外观")]
        [Header("光束数量")]
        public int beamCount = 3;

        [Header("光束颜色（alpha 为基础透明度）")]
        public Color color = new Color(1f, 0.95f, 0.78f, 0.22f);

        [Header("HDR 强度倍率，>1 时配合 Bloom 产生柔光")]
        public float hdrIntensity = 1.35f;

        [Header("光束倾斜基准角度（度，正值向左下方斜）")]
        public float baseAngle = 26f;

        [Header("每道光束在基准角度上的随机偏差（度）")]
        public float angleSpread = 7f;

        [Header("光束宽度随机范围（Canvas 像素）")]
        public Vector2 widthRange = new Vector2(130f, 300f);

        [Header("光束长度（Canvas 像素，要足够长贯穿画面）")]
        public float beamLength = 1800f;

        [Header("光束顶端沿画面顶边分布的 X 范围（相对画面中心，Canvas 像素）")]
        public Vector2 topOffsetRange = new Vector2(-150f, 800f);

        [Header("动态")]
        [Header("摆动幅度（度）")]
        public float swayDegrees = 2.2f;

        [Header("摆动周期（秒）")]
        public float swayPeriod = 8f;

        [Range(0f, 1f)]
        [Header("透明度呼吸幅度")]
        public float alphaPulseStrength = 0.45f;

        [Header("透明度呼吸周期（秒）")]
        public float alphaPulsePeriod = 5f;

        [Header("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        readonly List<RawImage> _beams = new List<RawImage>();
        Material _mat;
        CanvasGroup _group;
        bool _visible = true;

        public bool IsVisible => _visible;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_beams.Count > 0) return;

            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

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
            _mat.mainTexture = VNProceduralTextures.LightBeam;
            var hdr = color * hdrIntensity;
            hdr.a = 1f; // 透明度交给每道光束的顶点色
            _mat.SetColor(IdTintColor, hdr);

            for (int i = 0; i < beamCount; i++)
                CreateBeam(i);
        }

        void CreateBeam(int index)
        {
            var go = new GameObject($"Beam_{index}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);

            // 锚定在容器顶部中央，pivot 在光束顶端 → 旋转时绕顶端摆动
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);

            float t = beamCount <= 1 ? 0.5f : (float)index / (beamCount - 1);
            float x = Mathf.Lerp(topOffsetRange.x, topOffsetRange.y, t)
                      + Random.Range(-40f, 40f);
            rect.anchoredPosition = new Vector2(x, 60f); // 顶端略高出画面
            rect.sizeDelta = new Vector2(Random.Range(widthRange.x, widthRange.y), beamLength);

            float angle = baseAngle + Random.Range(-angleSpread, angleSpread);

            var img = go.GetComponent<RawImage>();
            img.texture = VNProceduralTextures.LightBeam;
            img.material = _mat;
            img.raycastTarget = false;
            float baseAlpha = color.a * Random.Range(0.7f, 1.15f);
            img.color = new Color(1f, 1f, 1f, baseAlpha);
            _beams.Add(img);

            // 摆动：从 angle-sway 到 angle+sway 往复
            float sway = swayDegrees * Random.Range(0.7f, 1.3f);
            float period = swayPeriod * Random.Range(0.8f, 1.3f);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle - sway);
            rect.DOLocalRotate(new Vector3(0f, 0f, angle + sway), period * 0.5f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetDelay(Random.Range(0f, period * 0.5f))
                .SetLink(go);

            // 透明度呼吸
            float pulsePeriod = alphaPulsePeriod * Random.Range(0.75f, 1.35f);
            img.DOFade(baseAlpha * (1f - alphaPulseStrength), pulsePeriod * 0.5f)
               .SetEase(Ease.InOutSine)
               .SetLoops(-1, LoopType.Yoyo)
               .SetDelay(Random.Range(0f, pulsePeriod))
               .SetLink(go);
        }

        /// <summary>淡入显示光束</summary>
        public void Show(float fade = 1.2f)
        {
            Build();
            _visible = true;
            _group.DOKill();
            _group.DOFade(1f, fade).SetLink(gameObject);
        }

        /// <summary>淡出隐藏光束</summary>
        public void Hide(float fade = 0.8f)
        {
            if (_group == null) return;
            _visible = false;
            _group.DOKill();
            _group.DOFade(0f, fade).SetLink(gameObject);
        }

        public void Toggle()
        {
            if (_visible) Hide();
            else Show();
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
