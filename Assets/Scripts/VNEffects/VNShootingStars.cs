using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 夜晚偶发流星：随机间隔在天空区划过一颗流星。
    /// 每颗流星 = 程序化拖尾贴图（MeteorStreak）+ 一条 DOTween 直线位移路径 +
    /// 前段淡入/后段淡出，飞完自动销毁。
    /// 挂在 LayerBack 下的全屏 Rect 上（背景之上、立绘之下），fx meteor on|off。
    /// 与萤火虫天气（weather Fireflies）搭配即完整夜空氛围。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNShootingStars : MonoBehaviour
    {
        static readonly int IdTintColor = Shader.PropertyToID("_TintColor");

        [Tooltip("两颗流星的随机间隔范围（秒）")]
        public Vector2 intervalRange = new Vector2(2.5f, 7f);

        [Tooltip("流星颜色（alpha 为峰值透明度）")]
        public Color color = new Color(0.85f, 0.95f, 1f, 0.9f);

        [Tooltip("HDR 强度倍率，>1 时配合 Bloom 产生辉光")]
        public float hdrIntensity = 1.6f;

        [Tooltip("单颗流星的飞行时长范围（秒）")]
        public Vector2 durationRange = new Vector2(0.55f, 0.95f);

        [Tooltip("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        Material _mat;
        CanvasGroup _group;
        bool _shown;
        Tween _scheduler;

        public bool IsShown => _shown;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_group != null) return;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
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
            _mat.mainTexture = VNProceduralTextures.MeteorStreak;
            var hdr = color * hdrIntensity;
            hdr.a = 1f; // 透明度交给每颗流星的顶点色
            _mat.SetColor(IdTintColor, hdr);
        }

        /// <summary>开启流星（渐显 + 开始随机排程）</summary>
        public void Show(float fade = 1f)
        {
            Build();
            _shown = true;
            _group.DOKill();
            _group.DOFade(1f, fade).SetLink(gameObject);
            ScheduleNext(Random.Range(0.4f, 1.2f)); // 开启后第一颗稍快出现
        }

        /// <summary>关闭流星（渐隐、停止排程；已在飞的自然飞完销毁）</summary>
        public void Hide(float fade = 0.8f)
        {
            if (_group == null) return;
            _shown = false;
            _scheduler?.Kill();
            _group.DOKill();
            _group.DOFade(0f, fade).SetLink(gameObject);
        }

        public void Toggle()
        {
            if (_shown) Hide();
            else Show();
        }

        void ScheduleNext(float delay)
        {
            _scheduler?.Kill();
            _scheduler = DOVirtual.DelayedCall(delay, () =>
            {
                if (!_shown) return;
                SpawnMeteor();
                ScheduleNext(Random.Range(intervalRange.x, intervalRange.y));
            }).SetLink(gameObject);
        }

        void SpawnMeteor()
        {
            var go = new GameObject("Meteor",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            float scale = Random.Range(0.7f, 1.3f);
            rect.sizeDelta = new Vector2(300f, 44f) * scale;

            // 起点：上半屏随机；方向：斜向下（左右随机）
            bool toRight = Random.value > 0.5f;
            var start = new Vector2(Random.Range(-750f, 750f), Random.Range(140f, 470f));
            float angle = toRight
                ? Random.Range(-38f, -20f)     // 右下
                : Random.Range(-160f, -142f);  // 左下
            var dir = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
            var end = start + dir * Random.Range(480f, 900f);

            rect.anchoredPosition = start;
            rect.localRotation = Quaternion.Euler(0f, 0f, angle); // 贴图 +X = 流星头朝向

            var img = go.GetComponent<RawImage>();
            img.texture = VNProceduralTextures.MeteorStreak;
            img.material = _mat;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0f);

            // 一条 DOTween 直线路径 + 前段淡入/后段淡出
            float dur = Random.Range(durationRange.x, durationRange.y);
            float peak = color.a * Random.Range(0.75f, 1f);
            var seq = DOTween.Sequence().SetLink(go);
            seq.Insert(0f, rect.DOAnchorPos(end, dur).SetEase(Ease.Linear));
            seq.Insert(0f, img.DOFade(peak, dur * 0.2f).SetEase(Ease.OutQuad));
            seq.Insert(dur * 0.55f, img.DOFade(0f, dur * 0.45f).SetEase(Ease.InQuad));
            seq.OnComplete(() => Destroy(go));
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
