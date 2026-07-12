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

        [Header("星尘/热浪/轮廓光（feature/breathing-rim-stardust-haze）")]
        public VNMouseStardust stardust;
        public VNHeatHaze heatHaze;

        [Header("说话者/波光/震动/对话框（feature/speaker-highlight）")]
        public VNEntranceAnimator characterB;
        public VNImageEffectController characterBFx;
        public VNSpeakerHighlight speakerHighlight;
        public VNScreenShake screenShake;
        public VNDialogueBox dialogue;

        [Header("视差/荷兰角（feature/parallax-ripple-eyelid-dutch）")]
        public VNParallax parallax;
        public VNDutchAngle dutchAngle;

        int _speakerIndex = -1; // -1 无 / 0 角色A / 1 角色B
        bool _shimmerOn;
        int _lineIndex = -1;

        // 演示台词：(说话者序号, 名字, 内容)
        static readonly (int who, string name, string text)[] DemoLines =
        {
            (0, "亚里沙", "今天的晚霞真漂亮啊……整片天空都烧起来了一样。"),
            (1, "小雪", "是啊。你看，连云的边缘都镶上了金色。"),
            (0, "亚里沙", "要是每天都能这样，和你一起看着天空发呆就好了。"),
            (1, "小雪", "笨蛋……说什么呢。走吧，再不回去天就黑了。"),
        };

        int _transitionIndex = -1;
        int _bgIndex;
        int _rimIndex; // 0 关 / 1 夕阳橙 / 2 月夜蓝

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

            // 第二角色（如果有）延迟一点滑入
            if (characterB != null)
            {
                DOVirtual.DelayedCall(0.5f, () =>
                    characterB.PlayEntrance(VNEntrancePreset.FadeSlideUp)
                              .OnComplete(() => characterB.StartIdleEffects()))
                    .SetLink(characterB.gameObject);
            }

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

            if (kb.cKey.wasPressedThisFrame && stardust != null) stardust.Toggle();

            if (kb.zKey.wasPressedThisFrame && heatHaze != null) heatHaze.Toggle();

            if (kb.rKey.wasPressedThisFrame && characterFx != null) CycleRimLight();

            if (kb.yKey.wasPressedThisFrame && speakerHighlight != null) CycleSpeaker();

            if (kb.uKey.wasPressedThisFrame && backgroundFx != null)
            {
                _shimmerOn = !_shimmerOn;
                if (_shimmerOn)
                {
                    backgroundFx.SetWaterShimmer(0f);
                    backgroundFx.DOShimmerAmount(0.85f, 1f);
                }
                else backgroundFx.DOShimmerAmount(0f, 0.8f);
            }

            if (screenShake != null)
            {
                if (kb.jKey.wasPressedThisFrame) screenShake.Shake(VNShakeLevel.Light);
                if (kb.kKey.wasPressedThisFrame) screenShake.Shake(VNShakeLevel.Medium);
                if (kb.lKey.wasPressedThisFrame) screenShake.Shake(VNShakeLevel.Heavy);
            }

            if (kb.enterKey.wasPressedThisFrame && dialogue != null) AdvanceDialogue();

            if (kb.oKey.wasPressedThisFrame && parallax != null) parallax.Toggle();

            if (kb.iKey.wasPressedThisFrame && dutchAngle != null) dutchAngle.Toggle();

            if (kb.fKey.wasPressedThisFrame && transition != null && !transition.IsPlaying)
                transition.Play(VNTransition.Eyelid, SwapBackground);

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

        void CycleSpeaker()
        {
            // A → B → 无 循环（没有 B 时 A → 无）
            int count = characterBFx != null ? 3 : 2;
            _speakerIndex = (_speakerIndex + 2) % count; // 从 -1 起步也正确
            if (_speakerIndex == 0) speakerHighlight.SetSpeaker(characterFx);
            else if (_speakerIndex == 1 && characterBFx != null)
                speakerHighlight.SetSpeaker(characterBFx);
            else
            {
                speakerHighlight.ClearSpeaker();
                _speakerIndex = -1;
            }
        }

        void AdvanceDialogue()
        {
            if (dialogue.IsTyping)
            {
                dialogue.CompleteTyping();
                return;
            }
            _lineIndex = (_lineIndex + 1) % DemoLines.Length;
            var line = DemoLines[_lineIndex];
            dialogue.Say(line.name, line.text);

            // 联动说话者高亮
            if (speakerHighlight != null)
            {
                var fx = line.who == 1 && characterBFx != null ? characterBFx : characterFx;
                speakerHighlight.SetSpeaker(fx);
            }
        }

        void CycleRimLight()
        {
            _rimIndex = (_rimIndex + 1) % 3;
            switch (_rimIndex)
            {
                case 0:
                    characterFx.DORimAmount(0f, 0.6f);
                    break;
                case 1: // 夕阳：右上方橙色轮廓光
                    characterFx.SetRimLight(new Color(2.2f, 1.05f, 0.4f), 0f, 0.022f, 40f);
                    characterFx.DORimAmount(1.2f, 0.8f);
                    break;
                case 2: // 月夜：左上方冷蓝轮廓光
                    characterFx.SetRimLight(new Color(0.75f, 1.05f, 2.3f), 0f, 0.022f, 140f);
                    characterFx.DORimAmount(1.2f, 0.8f);
                    break;
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
                "6 惊讶 | 7 生气 | 8 害羞 | 9 沮丧/恢复 | 0 点头 | N 摇头\n" +
                "R 轮廓光 | Z 热浪 | C 星尘 | Y 说话者 | U 水面波光\n" +
                "J/K/L 震动 | Enter 对话 | O 视差 | I 荷兰角 | F 眨眼转场 | 点击=涟漪";
        }
    }
}
