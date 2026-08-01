using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 角色说话口型：原立绘保持闭嘴，在其上方开关一张同画布坐标的透明张嘴图。
    /// 有语音时跟随语音与打字机，无语音时跟随打字机；停止后必定恢复闭嘴。
    /// </summary>
    public class VNCharacterMouth : MonoBehaviour
    {
        VNCharacterDef _definition;
        Image _overlay;
        VNDialogueBox _dialogue;
        VNAudio _audio;
        Tween _toggleTween;
        bool _speaking;
        bool _followVoice;
        bool _expressionAllowed = true;
        bool _mouthOpen;

        public void Initialize(Image baseImage, VNCharacterDef definition, Material sharedMaterial)
        {
            _definition = definition;

            var go = new GameObject("MouthOverlay",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            _overlay = go.GetComponent<Image>();
            _overlay.sprite = definition != null ? definition.openMouthSprite : null;
            _overlay.preserveAspect = false;
            _overlay.raycastTarget = false;
            _overlay.enabled = false;
            if (sharedMaterial != null) _overlay.material = sharedMaterial;

            _expressionAllowed = definition == null ||
                !definition.mouthDefaultExpressionOnly || definition.IsDefaultExpression(null);
            ValidateSpriteAlignment(baseImage != null ? baseImage.sprite : null);
        }

        /// <summary>开始本句对白的口型；followVoice 表示本句前有成功播放的 voice 命令。</summary>
        public void BeginSpeaking(bool followVoice, VNDialogueBox dialogue, VNAudio audio)
        {
            ForceClosed();
            _dialogue = dialogue;
            _audio = audio;
            _followVoice = followVoice;

            if (!Configured)
            {
                _dialogue = null;
                _audio = null;
                _followVoice = false;
                return;
            }
            _speaking = true;
            if (CanAnimate) ScheduleToggle(0.03f);
        }

        /// <summary>换表情前先闭嘴，避免旧嘴型残留在表情交叉溶解中。</summary>
        public void PrepareForExpressionChange()
        {
            CancelToggle();
            SetMouthOpen(false);
        }

        /// <summary>更新当前表情是否允许使用这张嘴部图。</summary>
        public void SetExpression(string expressionName)
        {
            _expressionAllowed = _definition == null ||
                !_definition.mouthDefaultExpressionOnly ||
                _definition.IsDefaultExpression(expressionName);

            if (!CanAnimate)
            {
                CancelToggle();
                SetMouthOpen(false);
            }
            else if (_toggleTween == null || !_toggleTween.IsActive())
            {
                ScheduleToggle(0.03f);
            }
        }

        /// <summary>无论当前处于哪个嘴型，都立即恢复原立绘的闭嘴状态。</summary>
        public void ForceClosed()
        {
            _speaking = false;
            _followVoice = false;
            _dialogue = null;
            _audio = null;
            CancelToggle();
            SetMouthOpen(false);
        }

        void Update()
        {
            if (!_speaking) return;

            bool typing = _dialogue != null && _dialogue.IsTyping;
            bool voice = _followVoice && _audio != null && _audio.IsVoicePlaying;
            if (!typing && !voice)
            {
                ForceClosed();
                return;
            }

            if (CanAnimate && (_toggleTween == null || !_toggleTween.IsActive()))
                ScheduleToggle();
        }

        bool Configured =>
            _definition != null &&
            _definition.enableMouthFlap &&
            _definition.openMouthSprite != null &&
            _overlay != null;

        bool CanAnimate => _speaking && Configured && _expressionAllowed && isActiveAndEnabled;

        void ScheduleToggle(float delay = -1f)
        {
            if (!CanAnimate) return;

            float min = Mathf.Max(0.03f, _definition.mouthIntervalMin);
            float max = Mathf.Max(min, _definition.mouthIntervalMax);
            float wait = delay >= 0f ? delay : Random.Range(min, max);
            _toggleTween = DOVirtual.DelayedCall(wait, () =>
            {
                _toggleTween = null;
                if (!CanAnimate)
                {
                    SetMouthOpen(false);
                    return;
                }
                SetMouthOpen(!_mouthOpen);
                ScheduleToggle();
            }).SetUpdate(false).SetLink(gameObject);
        }

        void SetMouthOpen(bool open)
        {
            _mouthOpen = open && CanShowOverlay;
            if (_overlay != null) _overlay.enabled = _mouthOpen;
        }

        bool CanShowOverlay => Configured && _expressionAllowed;

        void CancelToggle()
        {
            if (_toggleTween != null && _toggleTween.IsActive())
                _toggleTween.Kill();
            _toggleTween = null;
        }

        void OnDisable()
        {
            ForceClosed();
        }

        void OnDestroy()
        {
            CancelToggle();
        }

        void ValidateSpriteAlignment(Sprite baseSprite)
        {
            if (_definition == null || !_definition.enableMouthFlap ||
                baseSprite == null || _definition.openMouthSprite == null)
                return;

            Sprite mouth = _definition.openMouthSprite;
            float baseAspect = baseSprite.rect.width / Mathf.Max(1f, baseSprite.rect.height);
            float mouthAspect = mouth.rect.width / Mathf.Max(1f, mouth.rect.height);
            bool differentAspect = Mathf.Abs(baseAspect - mouthAspect) > 0.01f;
            bool differentPivot = Vector2.Distance(baseSprite.pivot, mouth.pivot) > 0.5f;
            if (differentAspect || differentPivot)
            {
                Debug.LogWarning(
                    $"[VNScript] 角色 {_definition.id} 的张嘴图与默认立绘宽高比或 Pivot 不一致，" +
                    "嘴部叠加可能错位。建议保留与原立绘完全相同的透明画布。",
                    _definition);
            }
        }
    }
}
