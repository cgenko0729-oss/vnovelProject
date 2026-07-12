using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 演示驱动：用键盘快速体验整套视觉小说特效系统。
    ///   1~5  —— 播放五种出场演出预设
    ///   Space —— 重播当前预设
    ///   X    —— 溶解退场
    ///   S    —— 手动触发一次扫光
    ///   B    —— 星光爆发（在立绘位置）
    ///   P    —— 开关悬浮氛围粒子
    ///   H    —— 色相偏移彩虹演示（再按恢复）
    /// </summary>
    public class VNEffectsDemo : MonoBehaviour
    {
        [Header("引用（由 Demo 场景生成器自动填好）")]
        public VNEntranceAnimator character;
        public VNImageEffectController characterFx;
        public VNImageEffectController backgroundFx;
        public VNAmbientParticles[] ambientParticles;
        public Text hintText;

        VNEntrancePreset _preset = VNEntrancePreset.DissolveGlow;
        bool _hueDemo;
        Tween _hueTween;

        void Start()
        {
            if (backgroundFx != null)
            {
                // 背景：轻微 Ken Burns 缓慢缩放 + 微弱亮度呼吸，让画面不死板
                backgroundFx.transform.DOScale(1.06f, 14f)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo)
                    .SetLink(backgroundFx.gameObject);
                backgroundFx.StartBreathingGlow(new Color(0.9f, 0.95f, 1f), 0.05f, 6f);
            }

            PlayCurrent();
            UpdateHint();
        }

        void PlayCurrent()
        {
            if (character == null) return;
            character.PlayEntrance(_preset)
                     .OnComplete(() => character.StartIdleEffects());
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || character == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SetPreset(VNEntrancePreset.DissolveGlow);
            if (kb.digit2Key.wasPressedThisFrame) SetPreset(VNEntrancePreset.FadeSlideUp);
            if (kb.digit3Key.wasPressedThisFrame) SetPreset(VNEntrancePreset.ScaleBounce);
            if (kb.digit4Key.wasPressedThisFrame) SetPreset(VNEntrancePreset.ShineReveal);
            if (kb.digit5Key.wasPressedThisFrame) SetPreset(VNEntrancePreset.FlashBloom);

            if (kb.spaceKey.wasPressedThisFrame) PlayCurrent();

            if (kb.xKey.wasPressedThisFrame) character.PlayExitDissolve();

            if (kb.sKey.wasPressedThisFrame && characterFx != null) characterFx.PlayShine();

            if (kb.bKey.wasPressedThisFrame && characterFx != null)
                VNAmbientParticles.PlaySparkleBurst(
                    characterFx.Rect.position, new Color(1f, 0.92f, 0.6f), 30);

            if (kb.pKey.wasPressedThisFrame && ambientParticles != null)
            {
                foreach (var p in ambientParticles)
                    if (p != null) p.SetPlaying(!p.IsEmitting);
            }

            if (kb.hKey.wasPressedThisFrame && characterFx != null) ToggleHueDemo();
        }

        void SetPreset(VNEntrancePreset preset)
        {
            _preset = preset;
            PlayCurrent();
            UpdateHint();
        }

        void ToggleHueDemo()
        {
            _hueDemo = !_hueDemo;
            _hueTween?.Kill();
            if (_hueDemo)
            {
                _hueTween = DOTween.To(
                        () => -0.5f,
                        v => characterFx.Mat.SetFloat(Shader.PropertyToID("_HueShift"), v),
                        0.5f, 4f)
                    .SetEase(Ease.Linear).SetLoops(-1, LoopType.Restart)
                    .SetLink(characterFx.gameObject);
            }
            else
            {
                characterFx.Mat.SetFloat(Shader.PropertyToID("_HueShift"), 0f);
            }
        }

        void UpdateHint()
        {
            if (hintText == null) return;
            hintText.text =
                $"当前出场预设: <b>{_preset}</b>\n" +
                "1 溶解辉光  2 滑入淡现  3 弹性弹出  4 扫光揭示  5 爆闪登场\n" +
                "Space 重播 | X 溶解退场 | S 扫光 | B 星光爆发 | P 粒子开关 | H 彩虹色相";
        }
    }
}
