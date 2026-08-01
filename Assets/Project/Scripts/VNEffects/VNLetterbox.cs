using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 电影 Letterbox：上下两条纯黑横条从屏幕外滑入，营造宽银幕电影感。
    /// 剧本命令 letterbox on|off [height:像素] [time:秒]；
    /// mood Memory（回忆）切换时由 VNStage 自动上/撤黑边（可关）。
    /// 挂到 Canvas 下的空 RectTransform 上，Awake 自动创建两条黑边，零美术资源。
    /// 嵌套 Canvas 排序 35：盖过舞台/粒子/速度线(25)，低于对话框(40)。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNLetterbox : MonoBehaviour
    {
        [Header("渲染排序（盖过舞台与速度线 25，低于对话框 40）")]
        public int sortingOrder = 35;

        [Header("默认黑边高度（Canvas 像素；130 ≈ 2.35:1 宽银幕）")]
        public float defaultHeight = 130f;

        [Header("默认滑入/滑出时长（秒）")]
        public float defaultDuration = 0.7f;

        RectTransform _top;
        RectTransform _bottom;
        float _height;   // 当前目标高度
        bool _shown;

        public bool IsShown => _shown;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_top != null) return;

            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;

            _height = defaultHeight;
            _top = CreateBar("LetterboxTop", 1f);
            _bottom = CreateBar("LetterboxBottom", 0f);
        }

        /// <summary>anchorY 1 = 顶边条（pivot 在上缘外滑），0 = 底边条</summary>
        RectTransform CreateBar(string name, float anchorY)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var bar = (RectTransform)go.transform;
            bar.SetParent(transform, false);
            bar.anchorMin = new Vector2(0f, anchorY);
            bar.anchorMax = new Vector2(1f, anchorY);
            bar.pivot = new Vector2(0.5f, anchorY);
            // 横向左右各溢出 20px，防荷兰角/震动时露缝
            bar.offsetMin = new Vector2(-20f, 0f);
            bar.offsetMax = new Vector2(20f, 0f);
            bar.sizeDelta = new Vector2(40f, _height);
            // 初始收在屏幕外
            bar.anchoredPosition = new Vector2(0f, anchorY > 0.5f ? _height : -_height);

            var img = go.GetComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return bar;
        }

        /// <summary>黑边滑入。height/duration ≤ 0 时用默认值。</summary>
        public void Show(float height = -1f, float duration = -1f)
        {
            Build();
            _shown = true;
            if (height > 0f) _height = height;
            else _height = defaultHeight;
            float t = duration >= 0f ? duration : defaultDuration;

            _top.DOKill();
            _bottom.DOKill();
            _top.sizeDelta = new Vector2(_top.sizeDelta.x, _height);
            _bottom.sizeDelta = new Vector2(_bottom.sizeDelta.x, _height);
            _top.DOAnchorPosY(0f, Mathf.Max(0.01f, t))
                .SetEase(Ease.OutCubic).SetLink(gameObject);
            _bottom.DOAnchorPosY(0f, Mathf.Max(0.01f, t))
                .SetEase(Ease.OutCubic).SetLink(gameObject);
        }

        /// <summary>黑边滑出。duration &lt; 0 时用默认值。</summary>
        public void Hide(float duration = -1f)
        {
            if (_top == null) return;
            _shown = false;
            float t = duration >= 0f ? duration : defaultDuration;

            _top.DOKill();
            _bottom.DOKill();
            _top.DOAnchorPosY(_height, Mathf.Max(0.01f, t))
                .SetEase(Ease.InCubic).SetLink(gameObject);
            _bottom.DOAnchorPosY(-_height, Mathf.Max(0.01f, t))
                .SetEase(Ease.InCubic).SetLink(gameObject);
        }

        public void Toggle()
        {
            if (_shown) Hide();
            else Show();
        }
    }
}
