using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 云影飘过：几块巨大的黑色软斑以不同速度缓慢横穿背景（普通透明混合 = 压暗），
    /// 晴天场景的"活气"。只盖背景层（挂在 LayerBack 下、背景图之后），不影响立绘。
    /// Show()/Hide()/Toggle() 开关（默认关闭）。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNCloudShadows : MonoBehaviour
    {
        [Header("云影数量")]
        public int cloudCount = 3;

        [Range(0f, 0.5f)]
        [Header("云影深度（透明度）")]
        public float shadowAlpha = 0.16f;

        [Header("横穿一屏的速度范围（像素/秒）")]
        public Vector2 speedRange = new Vector2(30f, 55f);

        class Cloud
        {
            public RectTransform rect;
            public float speed;
            public float baseY;
            public float phase;
        }

        Cloud[] _clouds;
        CanvasGroup _group;
        bool _shown;
        const float WrapX = 1450f; // 超出此 X 后回绕

        public bool IsShown => _shown;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_clouds != null) return;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            _clouds = new Cloud[cloudCount];
            for (int i = 0; i < cloudCount; i++)
            {
                var go = new GameObject($"CloudShadow_{i}",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                var rect = (RectTransform)go.transform;
                rect.SetParent(transform, false);
                float w = Random.Range(950f, 1500f);
                rect.sizeDelta = new Vector2(w, w * 0.55f);

                var img = go.GetComponent<RawImage>();
                img.texture = VNProceduralTextures.SoftCircle;
                img.raycastTarget = false;
                img.color = new Color(0f, 0f, 0f, shadowAlpha * Random.Range(0.75f, 1.15f));

                _clouds[i] = new Cloud
                {
                    rect = rect,
                    speed = Random.Range(speedRange.x, speedRange.y),
                    baseY = Random.Range(60f, 380f),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                };
                rect.anchoredPosition = new Vector2(Random.Range(-WrapX, WrapX), _clouds[i].baseY);
            }
        }

        void Update()
        {
            if (_clouds == null || _group.alpha <= 0.001f) return;
            float t = Time.time;
            foreach (var c in _clouds)
            {
                var pos = c.rect.anchoredPosition;
                pos.x += c.speed * Time.deltaTime;
                if (pos.x > WrapX) pos.x = -WrapX;
                pos.y = c.baseY + Mathf.Sin(t * 0.1f + c.phase) * 40f;
                c.rect.anchoredPosition = pos;
            }
        }

        public void Show(float fade = 2f)
        {
            Build();
            _shown = true;
            _group.DOKill();
            _group.DOFade(1f, fade).SetLink(gameObject);
        }

        public void Hide(float fade = 1.2f)
        {
            if (_group == null) return;
            _shown = false;
            _group.DOKill();
            _group.DOFade(0f, fade).SetLink(gameObject);
        }

        public void Toggle()
        {
            if (_shown) Hide();
            else Show();
        }
    }
}
