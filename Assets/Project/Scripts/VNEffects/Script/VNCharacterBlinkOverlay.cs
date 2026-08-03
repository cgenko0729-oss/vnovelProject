using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 默认表情自动眨眼（分层叠加版）：原立绘保持睁眼不动，在其上方同画布坐标短暂开关
    /// 一张透明闭眼图，只盖住眼部而不替换整张立绘——做法与 <see cref="VNCharacterMouth"/> 一致。
    /// 与整张替换版 <see cref="VNCharacterBlink"/> 互斥，由 VNCharacterDef.blinkMode 二选一。
    /// 只有默认表情会眨眼（其他表情眼部构图不同，叠上去必然错位）。
    /// </summary>
    public class VNCharacterBlinkOverlay : MonoBehaviour
    {
        VNCharacterDef _definition;
        Image _overlay;
        Tween _blinkTween;
        bool _usingDefaultExpression = true;

        public void Initialize(Image baseImage, VNCharacterDef definition, Material sharedMaterial)
        {
            _definition = definition;

            var go = new GameObject("BlinkOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            _overlay = go.GetComponent<Image>();
            _overlay.sprite = definition != null ? definition.blinkOverlaySprite : null;
            _overlay.preserveAspect = false;
            _overlay.raycastTarget = false;
            _overlay.enabled = false;
            // 共用立绘的材质实例：溶解/闪白/HSV 等单图特效对闭眼层同步生效
            if (sharedMaterial != null) _overlay.material = sharedMaterial;

            ValidateSpriteAlignment(baseImage != null ? baseImage.sprite : null);
            ScheduleNextBlink();
        }

        /// <summary>切换表情前收起闭眼层并取消旧计时，避免闭眼帧被卷进表情交叉溶解。</summary>
        public void PrepareForExpressionChange()
        {
            CancelBlink();
        }

        /// <summary>通知当前基础表情；只有默认表情会继续排程。</summary>
        public void SetExpression(bool isDefaultExpression)
        {
            CancelBlink();
            _usingDefaultExpression = isDefaultExpression;
            ScheduleNextBlink();
        }

        void ScheduleNextBlink()
        {
            if (!CanBlink) return;

            float min = Mathf.Max(0.1f, _definition.blinkIntervalMin);
            float max = Mathf.Max(min, _definition.blinkIntervalMax);
            float wait = Random.Range(min, max);
            float closedTime = Mathf.Clamp(_definition.blinkDuration, 0.03f, 0.5f);

            var sequence = DOTween.Sequence();
            sequence.AppendInterval(wait);
            sequence.AppendCallback(() => SetClosed(true));
            sequence.AppendInterval(closedTime);
            sequence.AppendCallback(() => SetClosed(false));
            sequence.OnComplete(ScheduleNextBlink);
            sequence.SetLink(gameObject);
            _blinkTween = sequence;
        }

        bool Configured =>
            _definition != null &&
            _definition.enableBlink &&
            _definition.blinkMode == VNBlinkMode.Overlay &&
            _definition.blinkOverlaySprite != null &&
            _overlay != null;

        bool CanBlink => isActiveAndEnabled && Configured && _usingDefaultExpression;

        void SetClosed(bool closed)
        {
            if (_overlay == null) return;
            _overlay.enabled = closed && Configured && _usingDefaultExpression;
        }

        void CancelBlink()
        {
            if (_blinkTween != null && _blinkTween.IsActive())
                _blinkTween.Kill();
            _blinkTween = null;
            SetClosed(false);
        }

        void OnDisable()
        {
            CancelBlink();
        }

        void OnEnable()
        {
            if (_definition != null && (_blinkTween == null || !_blinkTween.IsActive()))
                ScheduleNextBlink();
        }

        void OnDestroy()
        {
            CancelBlink();
        }

        void ValidateSpriteAlignment(Sprite baseSprite)
        {
            if (_definition == null || !_definition.enableBlink ||
                _definition.blinkMode != VNBlinkMode.Overlay ||
                baseSprite == null || _definition.blinkOverlaySprite == null)
                return;

            Sprite closed = _definition.blinkOverlaySprite;
            float baseAspect = baseSprite.rect.width / Mathf.Max(1f, baseSprite.rect.height);
            float closedAspect = closed.rect.width / Mathf.Max(1f, closed.rect.height);
            bool differentAspect = Mathf.Abs(baseAspect - closedAspect) > 0.01f;
            bool differentPivot = Vector2.Distance(baseSprite.pivot, closed.pivot) > 0.5f;
            if (differentAspect || differentPivot)
            {
                Debug.LogWarning(
                    $"[VNScript] 角色 {_definition.id} 的闭眼叠加图与默认立绘宽高比或 Pivot 不一致，" +
                    "眼部叠加可能错位。建议保留与原立绘完全相同的透明画布。",
                    _definition);
            }
        }
    }
}
