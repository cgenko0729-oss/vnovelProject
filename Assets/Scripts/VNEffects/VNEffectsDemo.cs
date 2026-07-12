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
    ///   G    —— 开关 God Rays 斜射光束
    ///   V    —— 聚焦渐晕：暗角对准立绘加深/恢复
    ///   E    —— 循环切换情绪边缘泛光（心动→危险→悲伤→温馨→关）
    ///   W    —— 循环切换天气（花瓣→雨→雪→萤火虫→无）
    /// </summary>
    public class VNEffectsDemo : MonoBehaviour
    {
        [Header("引用（由 Demo 场景生成器自动填好）")]
        public VNEntranceAnimator character;
        public VNImageEffectController characterFx;
        public VNImageEffectController backgroundFx;
        public VNAmbientParticles[] ambientParticles;
        public Text hintText;

        [Header("氛围特效（feature/atmosphere-effects）")]
        public VNGodRays godRays;
        public VNVignetteFocus vignetteFocus;
        public VNEdgeGlow edgeGlow;
        public VNWeatherController weather;

        [Header("色调/动作/转场（feature/mood-emotes-transitions）")]
        public VNMoodGrading mood;
        public VNScreenTransition transition;
        public VNCharacterEmotes emotes;
        [Tooltip("转场时轮换的背景图")]
        public Sprite[] backgroundVariants;

        int _transitionIndex = -1;
        int _bgIndex;

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

            if (kb.gKey.wasPressedThisFrame && godRays != null) godRays.Toggle();

            if (kb.vKey.wasPressedThisFrame && vignetteFocus != null && characterFx != null)
            {
                if (vignetteFocus.IsFocused) vignetteFocus.ClearFocus();
                else vignetteFocus.FocusOn(characterFx.Rect);
            }

            if (kb.eKey.wasPressedThisFrame && edgeGlow != null)
            {
                edgeGlow.CycleNext();
                UpdateHint();
            }

            if (kb.wKey.wasPressedThisFrame && weather != null)
            {
                weather.CycleNext();
                UpdateHint();
            }

            if (kb.mKey.wasPressedThisFrame && mood != null)
            {
                mood.CycleNext();
                UpdateHint();
            }

            if (kb.tKey.wasPressedThisFrame && transition != null && !transition.IsPlaying)
                PlayNextTransition();

            // 情绪演出动作
            if (emotes != null)
            {
                if (kb.digit6Key.wasPressedThisFrame) emotes.Surprise();
                if (kb.digit7Key.wasPressedThisFrame) emotes.Angry();
                if (kb.digit8Key.wasPressedThisFrame) emotes.Shy();
                if (kb.digit9Key.wasPressedThisFrame)
                {
                    if (emotes.IsDejected) emotes.Recover();
                    else emotes.Dejected();
                }
                if (kb.digit0Key.wasPressedThisFrame) emotes.Nod();
                if (kb.nKey.wasPressedThisFrame) emotes.HeadShake();
            }
        }

        void PlayNextTransition()
        {
            var types = (VNTransition[])System.Enum.GetValues(typeof(VNTransition));
            _transitionIndex = (_transitionIndex + 1) % types.Length;
            var type = types[_transitionIndex];

            // 圆形扩散/水墨从立绘位置扩散，其余居中
            if ((type == VNTransition.CircleWipe || type == VNTransition.InkSpread)
                && characterFx != null)
                transition.PlayFrom(type, characterFx.Rect, SwapBackground);
            else
                transition.Play(type, SwapBackground);
            UpdateHint();
        }

        void SwapBackground()
        {
            if (backgroundFx == null || backgroundVariants == null || backgroundVariants.Length == 0)
                return;
            var img = backgroundFx.GetComponent<Image>();
            if (img == null) return;
            _bgIndex = (_bgIndex + 1) % backgroundVariants.Length;
            img.sprite = backgroundVariants[_bgIndex];
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
            string emotion = edgeGlow != null ? edgeGlow.Current.ToString() : "-";
            string weatherName = weather != null ? weather.Current.ToString() : "-";
            string moodName = mood != null ? mood.Current.ToString() : "-";
            string transName = _transitionIndex >= 0
                ? ((VNTransition)_transitionIndex).ToString() : "-";
            hintText.text =
                $"出场: <b>{_preset}</b>  泛光: <b>{emotion}</b>  天气: <b>{weatherName}</b>  " +
                $"色调: <b>{moodName}</b>  转场: <b>{transName}</b>\n" +
                "1~5 出场演出 | Space 重播 | X 退场 | S 扫光 | B 星光 | P 粒子 | H 彩虹\n" +
                "G 光束 | V 聚焦渐晕 | E 情绪泛光 | W 天气 | M 色调 | T 转场换背景\n" +
                "6 惊讶 | 7 生气 | 8 害羞 | 9 沮丧/恢复 | 0 点头 | N 摇头";
        }
    }
}
