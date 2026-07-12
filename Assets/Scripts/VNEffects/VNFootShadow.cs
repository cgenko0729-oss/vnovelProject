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
        [Tooltip("影子宽度 = 立绘宽度 × 此比例")]
        public float widthRatio = 0.52f;

        [Tooltip("影子高宽比（扁度）")]
        public float heightRatio = 0.22f;

        [Range(0f, 1f)]
        [Tooltip("基础透明度")]
        public float baseAlpha = 0.38f;

        [Tooltip("相对脚底的位置偏移")]
        public Vector2 offset = new Vector2(0f, 6f);

        VNImageEffectController _fx;
        RectTransform _charRect;
        CanvasGroup _charGroup;
        RawImage _shadow;
        RectTransform _shadowRect;
        float _baseY;      // 角色基准 Y（未悬浮时）
        float _groundY;    // 影子的地面 Y
        bool _built;

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
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

            _baseY = _charRect.anchoredPosition.y;
            float charW = _charRect.rect.width;
            float charH = _charRect.rect.height;
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

            // 地面 = 角色底边（pivot 在中心）
            _groundY = _baseY - charH * 0.5f * _charRect.localScale.y + offset.y;

            _shadow = go.GetComponent<RawImage>();
            _shadow.texture = VNProceduralTextures.SoftCircle;
            _shadow.raycastTarget = false;
            _shadow.color = new Color(0f, 0f, 0f, baseAlpha);
        }

        void LateUpdate()
        {
            if (_shadow == null) return;

            // 悬浮高度差 → 影子缩小变淡
            float lift = Mathf.Max(0f, _charRect.anchoredPosition.y - _baseY);
            float shrink = Mathf.Clamp01(1f - lift * 0.008f);

            float alpha = baseAlpha * Mathf.Clamp01(1f - lift * 0.02f);
            if (_charGroup != null) alpha *= _charGroup.alpha;    // 淡入淡出联动
            alpha *= Mathf.Clamp01(_fx.GetDissolve() * 1.5f);     // 溶解出场联动

            _shadow.color = new Color(0f, 0f, 0f, alpha);
            _shadowRect.localScale = new Vector3(shrink, shrink, 1f);
            _shadowRect.anchoredPosition = new Vector2(
                _charRect.anchoredPosition.x + offset.x, _groundY);
        }

        void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }
    }
}
