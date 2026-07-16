using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 云本体缓移：几朵程序化云团（CloudPuff 贴图）横穿天空，
    /// 每朵一条 Linear DOTween 横移路径（无限循环回绕）+ 轻微纵向浮动。
    /// 与 VNCloudShadows（地面云影压暗）互补：那个是影，这个是云本体。
    /// 挂在 LayerBack 下的全屏 Rect 上（背景之上、立绘之下），fx skycloud on|off。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNDriftingClouds : MonoBehaviour
    {
        [Tooltip("云朵数量")]
        public int cloudCount = 3;

        [Tooltip("云色（alpha 为基础透明度；夜晚场景可调暗调蓝）")]
        public Color tint = new Color(1f, 1f, 1f, 0.4f);

        [Tooltip("横穿一屏耗时范围（秒，越大越慢）")]
        public Vector2 driftSecondsRange = new Vector2(70f, 120f);

        [Tooltip("云朵分布的纵向范围（Canvas 像素，画面上部）")]
        public Vector2 heightRange = new Vector2(170f, 430f);

        const float WrapX = 1400f; // 出界回绕的横向边界

        CanvasGroup _group;
        bool _shown;
        bool _built;

        public bool IsShown => _shown;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_built) return;
            _built = true;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            for (int i = 0; i < cloudCount; i++)
                CreateCloud(i);
        }

        void CreateCloud(int index)
        {
            var go = new GameObject($"SkyCloud_{index}",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            float w = Random.Range(520f, 950f);
            rect.sizeDelta = new Vector2(w, w * 0.42f);
            float y = Random.Range(heightRange.x, heightRange.y);

            var img = go.GetComponent<RawImage>();
            img.texture = VNProceduralTextures.CloudPuff;
            img.raycastTarget = false;
            var c = tint;
            c.a *= Random.Range(0.7f, 1.1f);
            img.color = c;

            // 起点均匀铺开（加随机抖动），向右缓移
            float startX = Mathf.Lerp(-WrapX, WrapX, (index + 0.5f) / Mathf.Max(1, cloudCount))
                           + Random.Range(-150f, 150f);
            startX = Mathf.Clamp(startX, -WrapX, WrapX - 50f);
            rect.anchoredPosition = new Vector2(startX, y);

            float fullDuration = Random.Range(driftSecondsRange.x, driftSecondsRange.y);
            // 先按剩余路程等速补完第一段，之后进入整屏无限循环
            float firstLeg = fullDuration * (WrapX - startX) / (WrapX * 2f);
            rect.DOAnchorPosX(WrapX, Mathf.Max(0.1f, firstLeg))
                .SetEase(Ease.Linear).SetLink(go)
                .OnComplete(() =>
                {
                    rect.anchoredPosition = new Vector2(-WrapX, rect.anchoredPosition.y);
                    rect.DOAnchorPosX(WrapX, fullDuration)
                        .SetEase(Ease.Linear)
                        .SetLoops(-1, LoopType.Restart)
                        .SetLink(go);
                });

            // 轻微纵向浮动
            rect.DOAnchorPosY(y + Random.Range(14f, 34f), Random.Range(9f, 15f))
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(go);
        }

        /// <summary>云朵渐显</summary>
        public void Show(float fade = 2f)
        {
            Build();
            _shown = true;
            _group.DOKill();
            _group.DOFade(1f, fade).SetLink(gameObject);
        }

        /// <summary>云朵渐隐（横移 Tween 继续跑，重新 Show 时位置自然）</summary>
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
