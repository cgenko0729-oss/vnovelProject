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
        [Header("立绘 A 出场动画器")]
        public VNEntranceAnimator character;
        [Header("立绘 A 特效控制器")]
        public VNImageEffectController characterFx;
        [Header("背景特效控制器")]
        public VNImageEffectController backgroundFx;
        [Header("氛围粒子列表（P 键开关）")]
        public VNAmbientParticles[] ambientParticles;
        [Header("底部按键提示文字")]
        public Text hintText;

        [Header("氛围特效（feature/atmosphere-effects）")]
        public VNGodRays godRays;
        [Header("聚焦渐晕（V 键）")]
        public VNVignetteFocus vignetteFocus;
        [Header("情绪边缘泛光（E 键循环）")]
        public VNEdgeGlow edgeGlow;
        [Header("天气控制器（W 键循环）")]
        public VNWeatherController weather;

        [Header("色调/动作/转场（feature/mood-emotes-transitions）")]
        public VNMoodGrading mood;
        [Header("全屏转场（T/F 键）")]
        public VNScreenTransition transition;
        [Header("立绘 A 情绪动作（6~0/N 键）")]
        public VNCharacterEmotes emotes;
        [Header("转场时轮换的背景图")]
        public Sprite[] backgroundVariants;

        [Header("星尘/热浪/轮廓光（feature/breathing-rim-stardust-haze）")]
        public VNMouseStardust stardust;
        [Header("热浪扭曲（Z 键）")]
        public VNHeatHaze heatHaze;

        [Header("说话者/波光/震动/对话框（feature/speaker-highlight）")]
        public VNEntranceAnimator characterB;
        [Header("立绘 B 特效控制器")]
        public VNImageEffectController characterBFx;
        [Header("说话者高亮（Y 键循环）")]
        public VNSpeakerHighlight speakerHighlight;
        [Header("画面震动（J/K/L 键）")]
        public VNScreenShake screenShake;
        [Header("对话框（Enter 键推进）")]
        public VNDialogueBox dialogue;

        [Header("视差/荷兰角（feature/parallax-ripple-eyelid-dutch）")]
        public VNParallax parallax;
        [Header("荷兰角（I 键）")]
        public VNDutchAngle dutchAngle;

        [Header("镜头/心跳/樱吹雪（feature/camera-heartbeat-sakura）")]
        public VNCamera vnCamera;
        [Header("心跳脉动（A 键）")]
        public VNHeartbeat heartbeat;
        [Header("樱吹雪（D 键）")]
        public VNSakuraBurst sakura;

        [Header("景深/云影/色调匹配/选项（feature/depth-polish-choices）")]
        public VNFakeDoF fakeDoF;
        [Header("云影（] 键）")]
        public VNCloudShadows cloudShadows;
        [Header("立绘色调匹配")]
        public VNToneMatch toneMatch;
        [Header("选项面板（退格键）")]
        public VNChoicePanel choicePanel;

        [Header("漫画速度线（agent/manga-speed-lines）")]
        public VNSpeedLines speedLines;

        [Header("全屏情绪水波（agent/screen-shockwave）")]
        public VNScreenShockwave shockwave;

        [Header("胶片/CRT 复古滤镜（agent/retro-film-filter）")]
        public VNRetroFilter retroFilter;

        [Header("背景 Ken Burns 漂移（agent/kenburns-drift）")]
        public VNKenBurns kenBurns;

        [Header("电影黑边（agent/cinema-letterbox）")]
        public VNLetterbox letterbox;

        [Header("夜空氛围（agent/night-sky-ambience）")]
        public VNShootingStars shootingStars;
        [Header("云本体缓移（; 键）")]
        public VNDriftingClouds driftingClouds;

        static readonly string[] DemoChoices =
            { "牵起她的手", "假装没看见远处的烟火", "转身逃跑" };

        int _cameraIndex = -1;
        static readonly string[] CameraMoveNames =
            { "缓推(PushIn)", "急推(SnapZoom)", "平移(Pan)", "眩晕(DollyZoom)", "复位(Reset)" };

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
                // 背景微弱亮度呼吸；缓慢缩放+平移交给 VNKenBurns（旧场景自愈补挂）
                backgroundFx.StartBreathingGlow(new Color(0.9f, 0.95f, 1f), 0.05f, 6f);
                if (kenBurns == null)
                {
                    kenBurns = backgroundFx.GetComponent<VNKenBurns>();
                    if (kenBurns == null)
                        kenBurns = backgroundFx.gameObject.AddComponent<VNKenBurns>();
                }
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

            // 开场时立绘就匹配初始背景色调
            if (toneMatch != null && backgroundFx != null)
            {
                var bgImg = backgroundFx.GetComponent<Image>();
                if (bgImg != null && bgImg.sprite != null)
                    DOVirtual.DelayedCall(0.2f, () => toneMatch.MatchTo(bgImg.sprite))
                             .SetLink(gameObject);
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

            if (kb.qKey.wasPressedThisFrame && vnCamera != null) CycleCameraMove();

            if (kb.aKey.wasPressedThisFrame && heartbeat != null) heartbeat.Toggle();

            if (kb.dKey.wasPressedThisFrame && sakura != null) sakura.Play();

            if (kb.leftBracketKey.wasPressedThisFrame && fakeDoF != null) fakeDoF.Toggle();

            if (kb.rightBracketKey.wasPressedThisFrame && cloudShadows != null)
                cloudShadows.Toggle();

            if (kb.commaKey.wasPressedThisFrame && speedLines != null) speedLines.Toggle();

            if (kb.periodKey.wasPressedThisFrame && speedLines != null) speedLines.Burst();

            if (kb.quoteKey.wasPressedThisFrame && letterbox != null) letterbox.Toggle();

            if (kb.minusKey.wasPressedThisFrame && shockwave != null) shockwave.Play();

            if (kb.equalsKey.wasPressedThisFrame && retroFilter != null)
            {
                retroFilter.CycleNext();
                UpdateHint();
            }

            if (kb.backslashKey.wasPressedThisFrame && kenBurns != null) kenBurns.Toggle();

            if (kb.slashKey.wasPressedThisFrame && shootingStars != null)
                shootingStars.Toggle();

            if (kb.semicolonKey.wasPressedThisFrame && driftingClouds != null)
                driftingClouds.Toggle();

            if (kb.tabKey.wasPressedThisFrame && character != null)
                character.PlayEntrance(VNEntrancePreset.AfterimageDash)
                         .OnComplete(() => character.StartIdleEffects());

            if (kb.backspaceKey.wasPressedThisFrame && choicePanel != null && !choicePanel.IsShowing)
            {
                choicePanel.Show(DemoChoices, idx =>
                {
                    if (dialogue != null)
                        dialogue.Say("旁白", $"你选择了「{DemoChoices[idx]}」。");
                });
            }

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

        void CycleCameraMove()
        {
            _cameraIndex = (_cameraIndex + 1) % 5;
            Vector2? focus = characterFx != null
                ? characterFx.Rect.anchoredPosition : (Vector2?)null;
            switch (_cameraIndex)
            {
                case 0: vnCamera.PushIn(1.06f, 4f, focus); break;
                case 1: vnCamera.SnapZoom(1.12f, 0.16f, focus, screenShake); break;
                case 2:
                    Vector2 panTarget = characterBFx != null
                        ? characterBFx.Rect.anchoredPosition
                        : new Vector2(380f, 0f);
                    vnCamera.Pan(panTarget, 0.6f, 1.2f);
                    break;
                case 3: vnCamera.DollyZoom(1.3f, 3f); break;
                default: vnCamera.ResetCamera(1f); break;
            }
            UpdateHint();
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

            // 放射类转场从立绘位置扩散，其余居中
            if ((type == VNTransition.CircleWipe || type == VNTransition.InkSpread ||
                 type == VNTransition.Shatter || type == VNTransition.Ripple ||
                 type == VNTransition.InkBleed)
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

            // 立绘色调自动匹配新背景
            if (toneMatch != null) toneMatch.MatchTo(backgroundVariants[_bgIndex]);
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
                "J/K/L 震动 | Enter 对话 | O 视差 | I 荷兰角 | F 眨眼转场 | 点击=涟漪\n" +
                $"Q 运镜循环({(_cameraIndex >= 0 ? CameraMoveNames[_cameraIndex] : "-")}) | " +
                "A 心跳演出 | D 樱吹雪告白\n" +
                "[ 伪景深 | ] 云影 | Tab 残影冲入 | 退格 选项演出（色调匹配/脚影自动）\n" +
                ", 速度线开关 | . 速度线冲击 | ' 电影黑边 | / 流星 | ; 云缓移 | - 全屏水波\n" +
                $"= 复古滤镜循环({(retroFilter != null ? retroFilter.Mode.ToString() : "-")}：无→胶片→CRT) | " +
                "\\ 背景 Ken Burns 漂移";
        }
    }
}
