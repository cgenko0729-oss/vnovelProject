using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VNEffects
{
    /// <summary>
    /// 动态暗角/聚焦渐晕：重要对话或特写时，把 URP Vignette 的强度动态加深
    /// 并把暗角中心对准目标（如说话角色），自然引导玩家视线。
    /// 挂在带 Volume 的物体上（或手动指定 volume 引用）。
    /// 运行时操作的是 volume.profile 的实例副本，不会弄脏磁盘上的资产。
    /// </summary>
    public class VNVignetteFocus : MonoBehaviour
    {
        [Header("目标 Volume；留空则取本物体上的 Volume 组件")]
        public Volume volume;

        [Header("聚焦时的暗角强度")]
        [Range(0f, 1f)] public float focusIntensity = 0.46f;

        [Header("聚焦时的暗角平滑度")]
        [Range(0.01f, 1f)] public float focusSmoothness = 0.6f;

        Camera _cam;
        Vignette _vignette;
        float _baseIntensity = 0.22f;
        float _baseSmoothness = 0.45f;
        bool _focused;

        public bool IsFocused => _focused;

        void Awake()
        {
            if (volume == null) volume = GetComponent<Volume>();
            _cam = Camera.main;
        }

        bool EnsureVignette()
        {
            if (_vignette != null) return true;
            if (volume == null)
            {
                Debug.LogError("[VNEffects] VNVignetteFocus 没有可用的 Volume。", this);
                return false;
            }
            // volume.profile（非 sharedProfile）：运行时实例副本，改动不影响资产
            if (!volume.profile.TryGet(out _vignette))
                _vignette = volume.profile.Add<Vignette>(false);

            _vignette.intensity.overrideState = true;
            _vignette.smoothness.overrideState = true;
            _vignette.center.overrideState = true;

            _baseIntensity = _vignette.intensity.value;
            _baseSmoothness = _vignette.smoothness.value;
            return true;
        }

        /// <summary>聚焦到一个世界坐标目标（如立绘的 RectTransform）</summary>
        public void FocusOn(Transform target, float duration = 0.8f)
        {
            if (target == null || !EnsureVignette()) return;
            if (_cam == null) _cam = Camera.main;
            Vector3 vp = _cam != null
                ? _cam.WorldToViewportPoint(target.position)
                : new Vector3(0.5f, 0.5f, 0f);
            FocusOnViewport(new Vector2(vp.x, vp.y), duration);
        }

        /// <summary>聚焦到视口坐标（0~1，左下为原点）</summary>
        public void FocusOnViewport(Vector2 viewportPos, float duration = 0.8f)
        {
            if (!EnsureVignette()) return;
            _focused = true;
            DOTween.Kill(this);
            TweenTo(focusIntensity, focusSmoothness, viewportPos, duration);
        }

        /// <summary>恢复到基础暗角（居中、原强度）</summary>
        public void ClearFocus(float duration = 0.8f)
        {
            if (!EnsureVignette()) return;
            _focused = false;
            DOTween.Kill(this);
            TweenTo(_baseIntensity, _baseSmoothness, new Vector2(0.5f, 0.5f), duration);
        }

        void TweenTo(float intensity, float smoothness, Vector2 center, float duration)
        {
            DOTween.To(() => _vignette.intensity.value,
                       v => _vignette.intensity.value = v, intensity, duration)
                   .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
            DOTween.To(() => _vignette.smoothness.value,
                       v => _vignette.smoothness.value = v, smoothness, duration)
                   .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
            DOTween.To(() => _vignette.center.value,
                       v => _vignette.center.value = v, center, duration)
                   .SetEase(Ease.InOutSine).SetTarget(this).SetLink(gameObject);
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
