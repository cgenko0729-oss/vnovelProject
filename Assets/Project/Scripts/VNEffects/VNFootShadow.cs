using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 立绘脚下阴影：在角色脚下生成一个扁椭圆软影（柔光圆压扁 + 黑色半透明），
    /// 让角色"站在地上"而不是"贴在屏幕上"。
    /// 每帧与悬浮同步：角色浮得越高，影子越小越淡（离地感）；
    /// 同时跟随角色横向移动、透明度联动 CanvasGroup 与溶解度。
    /// 挂到立绘上即可，零配置。
    /// </summary>
    [RequireComponent(typeof(VNImageEffectController))]
    public class VNFootShadow : MonoBehaviour
    {
        [Header("影子宽度 = 立绘宽度 × 此比例")]
        public float widthRatio = 0.52f;

        [Header("影子高宽比（扁度）")]
        public float heightRatio = 0.22f;

        [Range(0f, 1f)]
        [Header("基础透明度")]
        public float baseAlpha = 0.38f;

        [Header("相对脚底的位置偏移")]
        public Vector2 offset = new Vector2(0f, 6f);

        VNImageEffectController _fx;
        VNEntranceAnimator _animator; // 有则动态读取基准位（角色可能被剧本换位）
        RectTransform _charRect;
        CanvasGroup _charGroup;
        RawImage _shadow;
        RectTransform _shadowRect;
        float _baseY;       // 角色基准 Y（无出场器时的后备）
        float _halfHeight;  // 角色半高（算脚底位置用）
        bool _built;
        float _impact = 1f; // 落地冲击的横向倍率（1 = 静止；stepin 登场时被推高再回落）
        Tween _impactTween;

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
            _animator = GetComponent<VNEntranceAnimator>();
            _charRect = _fx.Rect;
            _charGroup = GetComponent<CanvasGroup>();
        }

        void Start()
        {
            Build(); // 等一帧布局，尺寸才可靠
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            _baseY = _animator != null ? _animator.BasePosition.y : _charRect.anchoredPosition.y;
            float charW = _charRect.rect.width;
            float charH = _charRect.rect.height;
            _halfHeight = charH * 0.5f * _charRect.localScale.y;
            float w = charW * widthRatio;

            var go = new GameObject($"{name}_FootShadow",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            _shadowRect = (RectTransform)go.transform;
            _shadowRect.SetParent(_charRect.parent, false);
            _shadowRect.SetSiblingIndex(_charRect.GetSiblingIndex()); // 渲染在角色背后
            _shadowRect.anchorMin = _charRect.anchorMin;
            _shadowRect.anchorMax = _charRect.anchorMax;
            _shadowRect.pivot = new Vector2(0.5f, 0.5f);
            _shadowRect.sizeDelta = new Vector2(w, w * heightRatio);

            _shadow = go.GetComponent<RawImage>();
            _shadow.texture = VNProceduralTextures.SoftCircle;
            _shadow.raycastTarget = false;
            _shadow.color = new Color(0f, 0f, 0f, baseAlpha);
        }

        /// <summary>
        /// 落地冲击：影子瞬间横向摊开、纵向压扁，再缓回原状。
        /// 供 StepIn 登场在"脚落地"那一帧调用——影子跟着动，重量感才成立。
        /// </summary>
        public void Impact(float strength = 1.4f, float duration = 0.3f)
        {
            Build(); // Start 还没跑到时也能用
            _impactTween?.Kill();
            _impact = Mathf.Max(1f, strength);
            _impactTween = DOTween.To(() => _impact, v => _impact = v, 1f, duration)
                                  .SetEase(Ease.OutCubic).SetLink(gameObject);
        }

        void LateUpdate()
        {
            if (_shadow == null) return;

            // 基准位动态取自出场器（角色可能被剧本换位）
            float baseY = _animator != null ? _animator.BasePosition.y : _baseY;
            float groundY = baseY - _halfHeight + offset.y;

            // 悬浮高度差 → 影子缩小变淡
            float lift = Mathf.Max(0f, _charRect.anchoredPosition.y - baseY);
            float shrink = Mathf.Clamp01(1f - lift * 0.008f);

            float alpha = baseAlpha * Mathf.Clamp01(1f - lift * 0.02f);
            if (_charGroup != null) alpha *= _charGroup.alpha;    // 淡入淡出联动
            alpha *= Mathf.Clamp01(_fx.GetDissolve() * 1.5f);     // 溶解出场联动

            _shadow.color = new Color(0f, 0f, 0f, alpha);
            // 落地冲击期间横向摊开、纵向压扁（体积守恒的观感）
            _shadowRect.localScale = new Vector3(
                shrink * _impact, shrink * (2f - _impact), 1f);
            _shadowRect.anchoredPosition = new Vector2(
                _charRect.anchoredPosition.x + offset.x, groundY);
        }

        void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }
    }
}
