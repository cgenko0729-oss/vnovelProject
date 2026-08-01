using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 默认表情自动眨眼：用完整闭眼立绘短暂替换当前 Image，不经过表情交叉溶解。
    /// 非默认表情、未启用或未配置闭眼图时不会改写角色立绘。
    /// </summary>
    public class VNCharacterBlink : MonoBehaviour
    {
        Image _image;
        VNCharacterDef _definition;
        Sprite _openSprite;
        Tween _blinkTween;
        bool _usingDefaultExpression;
        bool _showingClosedSprite;

        public void Initialize(Image image, VNCharacterDef definition)
        {
            _image = image;
            _definition = definition;
            _openSprite = definition != null ? definition.DefaultSprite : null;
            _usingDefaultExpression = true;
            ValidateSpriteAlignment();
            ScheduleNextBlink();
        }

        /// <summary>
        /// 切换表情前恢复睁眼图并取消旧计时，避免把闭眼帧拿去做表情交叉溶解。
        /// </summary>
        public void PrepareForExpressionChange()
        {
            CancelBlink(true);
        }

        /// <summary>通知眨眼组件当前基础表情；只有默认表情会继续排程。</summary>
        public void SetExpression(Sprite sprite, bool isDefaultExpression)
        {
            CancelBlink(false);
            _openSprite = sprite;
            _usingDefaultExpression = isDefaultExpression;
            _showingClosedSprite = false;
            if (_image != null) _image.sprite = sprite;
            ScheduleNextBlink();
        }

        void ScheduleNextBlink()
        {
            if (!CanBlink()) return;

            float min = Mathf.Max(0.1f, _definition.blinkIntervalMin);
            float max = Mathf.Max(min, _definition.blinkIntervalMax);
            float wait = Random.Range(min, max);
            float closedTime = Mathf.Clamp(_definition.blinkDuration, 0.03f, 0.5f);

            var sequence = DOTween.Sequence();
            sequence.AppendInterval(wait);
            sequence.AppendCallback(ShowClosedSprite);
            sequence.AppendInterval(closedTime);
            sequence.AppendCallback(ShowOpenSprite);
            sequence.OnComplete(ScheduleNextBlink);
            sequence.SetLink(gameObject);
            _blinkTween = sequence;
        }

        bool CanBlink() =>
            isActiveAndEnabled &&
            _definition != null &&
            _definition.enableBlink &&
            _definition.blinkSprite != null &&
            _image != null &&
            _openSprite != null &&
            _usingDefaultExpression;

        void ShowClosedSprite()
        {
            if (!CanBlink() || _image.sprite != _openSprite) return;
            _image.sprite = _definition.blinkSprite;
            _showingClosedSprite = true;
        }

        void ShowOpenSprite()
        {
            if (_image != null && _showingClosedSprite)
                _image.sprite = _openSprite;
            _showingClosedSprite = false;
        }

        void CancelBlink(bool restoreOpenSprite)
        {
            if (_blinkTween != null && _blinkTween.IsActive())
                _blinkTween.Kill();
            _blinkTween = null;

            if (restoreOpenSprite) ShowOpenSprite();
            else _showingClosedSprite = false;
        }

        void OnDisable()
        {
            CancelBlink(true);
        }

        void OnEnable()
        {
            if (_definition != null && (_blinkTween == null || !_blinkTween.IsActive()))
                ScheduleNextBlink();
        }

        void OnDestroy()
        {
            CancelBlink(false);
        }

        void ValidateSpriteAlignment()
        {
            if (_definition == null || !_definition.enableBlink ||
                _definition.DefaultSprite == null || _definition.blinkSprite == null)
                return;

            var open = _definition.DefaultSprite;
            var closed = _definition.blinkSprite;
            float openAspect = open.rect.width / Mathf.Max(1f, open.rect.height);
            float closedAspect = closed.rect.width / Mathf.Max(1f, closed.rect.height);
            bool differentAspect = Mathf.Abs(openAspect - closedAspect) > 0.01f;
            bool differentPivot = Vector2.Distance(open.pivot, closed.pivot) > 0.5f;
            if (differentAspect || differentPivot)
            {
                Debug.LogWarning(
                    $"[VNScript] 角色 {_definition.id} 的闭眼立绘与默认立绘尺寸比例或 Pivot 不一致，" +
                    "眨眼时可能发生缩放或位置跳动。建议使用完全对齐的完整全身图片。",
                    _definition);
            }
        }
    }
}
