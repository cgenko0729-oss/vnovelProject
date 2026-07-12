using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>情绪泛光预设</summary>
    public enum VNEmotionGlow
    {
        None,      // 关闭
        HeartBeat, // 心动：粉色，心跳节奏双脉冲
        Danger,    // 危险：红色，快速脉动
        Sadness,   // 悲伤：蓝色，缓慢起伏
        Warmth,    // 温馨：暖橙色，极缓慢呼吸
    }

    /// <summary>
    /// 屏幕边缘情绪泛光：全屏边框渐变（中心透明、边缘发光）+ 加法混合 + HDR 颜色，
    /// 按不同情绪以不同颜色和节奏脉动。
    /// 挂到 Canvas 下的一个物体上，Awake 时自动创建覆盖全屏的泛光框，
    /// 并用嵌套 Canvas 提升排序，保证盖在粒子和 UI 之上。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNEdgeGlow : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        [Tooltip("渲染排序（要高于氛围粒子的 sortingOrder）")]
        public int sortingOrder = 20;

        [Tooltip("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        RawImage _img;
        Material _mat;
        Sequence _pattern;
        Color _color = Color.white;
        float _curAlpha;
        VNEmotionGlow _current = VNEmotionGlow.None;

        public VNEmotionGlow Current => _current;

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

            // 嵌套 Canvas 覆盖排序，保证泛光框渲染在粒子（sortingOrder 10~12）之上
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            var go = new GameObject("EdgeGlowFrame",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var frameRect = (RectTransform)go.transform;
            frameRect.SetParent(transform, false);
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;

            _img = go.GetComponent<RawImage>();
            _img.texture = VNProceduralTextures.EdgeGlowFrame;
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
            _mat.mainTexture = VNProceduralTextures.EdgeGlowFrame;
            _img.material = _mat;

            ApplyAlpha(0f);
        }

        void ApplyAlpha(float a)
        {
            _curAlpha = a;
            if (_mat == null) return;
            var c = _color;
            c.a = a;
            _mat.SetColor(IdTintColor, c);
        }

        Tween AlphaTo(float to, float duration, Ease ease = Ease.InOutSine)
            => DOTween.To(() => _curAlpha, ApplyAlpha, to, duration).SetEase(ease);

        // ------------------------------------------------------------------

        /// <summary>按情绪预设显示泛光</summary>
        public void Show(VNEmotionGlow emotion, float fadeIn = 0.7f)
        {
            Build();
            if (emotion == VNEmotionGlow.None)
            {
                Hide();
                return;
            }
            _current = emotion;
            switch (emotion)
            {
                case VNEmotionGlow.HeartBeat:
                    ShowCustom(new Color(1f, 0.38f, 0.58f) * 1.7f, 0.42f, fadeIn, HeartBeatPattern);
                    break;
                case VNEmotionGlow.Danger:
                    ShowCustom(new Color(1f, 0.12f, 0.08f) * 1.8f, 0.5f, fadeIn,
                        () => YoyoPattern(0.5f, 0.5f));
                    break;
                case VNEmotionGlow.Sadness:
                    ShowCustom(new Color(0.35f, 0.5f, 1f) * 1.4f, 0.34f, fadeIn,
                        () => YoyoPattern(0.4f, 3.8f));
                    break;
                case VNEmotionGlow.Warmth:
                    ShowCustom(new Color(1f, 0.72f, 0.38f) * 1.4f, 0.3f, fadeIn,
                        () => YoyoPattern(0.35f, 5f));
                    break;
            }
        }

        /// <summary>自定义颜色/节奏（patternStarter 在淡入完成后启动循环脉动）</summary>
        public void ShowCustom(Color hdrColor, float baseAlpha, float fadeIn,
            System.Action patternStarter)
        {
            Build();
            KillPattern();
            _color = hdrColor;
            _baseAlphaForPattern = baseAlpha;
            AlphaTo(baseAlpha, fadeIn, Ease.OutQuad)
                .SetTarget(this).SetLink(gameObject)
                .OnComplete(() => patternStarter?.Invoke());
        }

        /// <summary>淡出关闭</summary>
        public void Hide(float fade = 0.6f)
        {
            _current = VNEmotionGlow.None;
            KillPattern();
            AlphaTo(0f, fade, Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        /// <summary>循环切换到下一个情绪（演示用）</summary>
        public VNEmotionGlow CycleNext()
        {
            var next = (VNEmotionGlow)(((int)_current + 1) % 5);
            Show(next);
            return next;
        }

        // ------------------------------------------------------------------
        // 脉动节奏
        // ------------------------------------------------------------------

        float _baseAlphaForPattern = 0.4f;

        void KillPattern()
        {
            _pattern?.Kill();
            _pattern = null;
            DOTween.Kill(this);
        }

        /// <summary>心跳节奏：咚-咚——停，循环</summary>
        void HeartBeatPattern()
        {
            float hi = _baseAlphaForPattern;
            float mid = hi * 0.45f;
            float lo = hi * 0.22f;
            _pattern = DOTween.Sequence()
                .Append(AlphaTo(hi, 0.1f, Ease.OutQuad))
                .Append(AlphaTo(mid, 0.16f, Ease.InOutSine))
                .Append(AlphaTo(hi * 0.85f, 0.1f, Ease.OutQuad))
                .Append(AlphaTo(lo, 0.42f, Ease.OutQuad))
                .AppendInterval(0.38f)
                .SetLoops(-1)
                .SetLink(gameObject);
        }

        /// <summary>简单往复脉动</summary>
        void YoyoPattern(float lowRatio, float period)
        {
            float hi = _baseAlphaForPattern;
            float lo = hi * lowRatio;
            _pattern = DOTween.Sequence()
                .Append(AlphaTo(lo, period * 0.5f))
                .Append(AlphaTo(hi, period * 0.5f))
                .SetLoops(-1)
                .SetLink(gameObject);
        }

        void OnDestroy()
        {
            _pattern?.Kill();
            DOTween.Kill(this);
            if (_mat != null) Destroy(_mat);
        }
    }
}
