using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    public partial class VNStage
    {
        // ------------------------------------------------------------------
        // fx 开关分发
        // ------------------------------------------------------------------

        /// <summary>可开关型 fx 的当前状态（存档用）</summary>
        readonly Dictionary<string, bool> _fxStates = new Dictionary<string, bool>();

        static readonly string[] ToggleFxNames =
            { "godrays", "dof", "clouds", "haze", "shimmer", "heartbeat", "dutch",
              "speedlines", "letterbox", "meteor", "skycloud", "filmgrain", "crt",
              "kenburns" };

        [Header("mood Memory（回忆）自动上电影黑边、离开回忆自动撤掉")]
        public bool autoMemoryLetterbox = true;

        [Header("mood Memory（回忆）自动上胶片滤镜、mood Dream（梦境）自动上 CRT 滤镜")]
        public bool autoMoodRetroFilter = true;

        bool _letterboxAuto; // 当前黑边是否由回忆色调自动打开（离开回忆时才自动撤）
        bool _retroAuto;     // 当前复古滤镜是否由 mood 自动打开（离开对应 mood 时才自动撤）

        /// <summary>mood 命令入口：切换情绪色调 + 回忆黑边/复古滤镜自动联动</summary>
        public void SetMood(VNMood m, float duration = -1f)
        {
            if (mood != null)
            {
                if (duration > 0f) mood.SetMood(m, duration);
                else mood.SetMood(m);
            }
            if (autoMemoryLetterbox && letterbox != null)
            {
                if (m == VNMood.Memory && !letterbox.IsShown)
                {
                    letterbox.Show();
                    _fxStates["letterbox"] = true;
                    _letterboxAuto = true;
                }
                else if (m != VNMood.Memory && _letterboxAuto)
                {
                    letterbox.Hide();
                    _fxStates["letterbox"] = false;
                    _letterboxAuto = false;
                }
            }
            if (autoMoodRetroFilter && retroFilter != null)
            {
                if (m == VNMood.Memory && !retroFilter.IsShown)
                {
                    retroFilter.ShowFilm();
                    _fxStates["filmgrain"] = true;
                    _retroAuto = true;
                }
                else if (m == VNMood.Dream && !retroFilter.IsShown)
                {
                    retroFilter.ShowCrt();
                    _fxStates["crt"] = true;
                    _retroAuto = true;
                }
                else if (m != VNMood.Memory && m != VNMood.Dream && _retroAuto)
                {
                    retroFilter.Hide();
                    _fxStates["filmgrain"] = false;
                    _fxStates["crt"] = false;
                    _retroAuto = false;
                }
            }
        }

        // ------------------------------------------------------------------
        // 天气（飘落类的剧本覆盖参数由 VNStage 统一持有 —— 存档、CG 暂停恢复、
        // 调试重建三处都从这里取，避免各自维护一份而走样）
        // ------------------------------------------------------------------

        float _weatherDensity, _weatherSpeed, _weatherSize, _weatherWind;
        bool _weatherWindSet;

        void ClearWeatherOverrides()
        {
            _weatherDensity = _weatherSpeed = _weatherSize = _weatherWind = 0f;
            _weatherWindSet = false;
        }

        /// <summary>用当前记录的覆盖参数应用一个天气 id（CG 恢复 / 读档共用）</summary>
        void ApplyWeather(string id, float transition)
        {
            weather?.SetWeatherId(id, transition, _weatherDensity,
                _weatherWindSet ? _weatherWind : float.NaN, _weatherSpeed, _weatherSize);
        }

        /// <summary>
        /// weather 命令入口：weather &lt;id&gt; [density:] [wind:] [speed:] [size:]
        /// 覆盖参数留空 = 用资产里的值；换天气时覆盖参数一并重置。
        /// </summary>
        public void SetWeather(string id, float density, float wind, bool windSet,
            float speed, float size, float transition = 1.5f)
        {
            _weatherDensity = density;
            _weatherSpeed = speed;
            _weatherSize = size;
            _weatherWind = wind;
            _weatherWindSet = windSet;
            ApplyWeather(id, transition);
        }

        // ------------------------------------------------------------------
        // 液体喷溅（liquid 命令）
        // ------------------------------------------------------------------

        /// <summary>
        /// liquid 命令入口。子动作：
        ///   splash  一次性大爆溅（瞬发）
        ///   spray   间歇喷射开关（噗——噗——，持续状态，进存档）
        ///   click   点击喷水模式开关（持续状态，进存档）
        ///   wet     常驻湿镜头开关（隔窗看雨，持续状态，进存档）
        ///   dry     擦干现有水渍（瞬发）
        ///   cover   水渍层盖不盖对话框（持续状态，进存档）
        /// 参数含义见 <see cref="VNLiquidArgs"/>。
        /// </summary>
        public void Liquid(string action, VNLiquidArgs a, int line = 0)
        {
            action = string.IsNullOrEmpty(action) ? "splash" : action.ToLower();
            var preset = VNLiquidPreset.Get(a.type, line);

            switch (action)
            {
                case "splash":
                    if (liquidSplash == null) break;
                    liquidSplash.Burst(new Vector2(a.x, a.y), preset,
                        a.power, a.dir, a.spread, a.screen);
                    break;

                case "spray":
                    if (liquidSplash == null) break;
                    if (a.on)
                        liquidSplash.StartSpray(new Vector2(a.x, a.y), preset,
                            a.power, a.dir, a.spread, a.rate, a.screen);
                    else
                        liquidSplash.StopSpray();
                    break;

                case "click":
                    if (liquidSplash == null) break;
                    liquidSplash.SetClickMode(a.on, preset, a.power, a.screen);
                    break;

                case "wet":
                    if (wetScreen == null) break;
                    if (a.on) wetScreen.SetWet(true, preset, a.amount);
                    else wetScreen.SetWet(false);
                    break;

                case "dry":
                    wetScreen?.Dry();
                    break;

                case "cover":
                    wetScreen?.SetCover(a.on);
                    break;

                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知 liquid 子命令「{action}」，" +
                                     "可用 splash / spray / click / wet / dry / cover");
                    break;
            }
        }

        /// <summary>把液体的持续状态全部停掉并清干净（清场 / 读档 / 章节转场）</summary>
        public void ResetLiquid()
        {
            liquidSplash?.ClearInstant();
            wetScreen?.ClearInstant();
        }

        /// <summary>
        /// 液体状态写入存档。直接从两个组件读，不在 VNStage 另存一份镜像——
        /// 天气那边之所以要 VNStage 持有覆盖参数，是因为 CG 暂停期间组件上的值会被改掉；
        /// 液体没有暂停语义，组件上的值任何时刻都是权威。
        /// </summary>
        void CaptureLiquid(VNSaveData data)
        {
            var l = data.liquid ?? (data.liquid = new VNSaveData.LiquidSave());

            if (liquidSplash != null)
            {
                l.sprayOn = liquidSplash.IsSpraying;
                l.sprayType = liquidSplash.SprayType.ToString();
                l.sprayX = liquidSplash.SprayPos.x;
                l.sprayY = liquidSplash.SprayPos.y;
                l.sprayPower = liquidSplash.SprayPower;
                // NaN 不能进 JSON，拆成「有没有指定」+「指定的值」两个字段
                float dir = liquidSplash.SprayDir;
                l.sprayDirSet = !float.IsNaN(dir);
                l.sprayDir = l.sprayDirSet ? dir : 0f;
                l.spraySpread = liquidSplash.SpraySpread;
                l.sprayRate = liquidSplash.SprayRate;
                l.sprayScreen = liquidSplash.SprayScreen;

                l.clickOn = liquidSplash.IsClickMode;
                l.clickType = liquidSplash.ClickType.ToString();
                l.clickPower = liquidSplash.ClickPower;
                l.clickScreen = liquidSplash.ClickScreen;
            }
            else
            {
                l.sprayOn = false;
                l.clickOn = false;
            }

            if (wetScreen != null)
            {
                l.wetOn = wetScreen.IsWet;
                l.wetType = wetScreen.WetType.ToString();
                l.wetAmount = wetScreen.WetAmount;
                l.cover = wetScreen.coverDialogue;
            }
            else
            {
                l.wetOn = false;
            }
        }

        /// <summary>按存档恢复液体状态（先清干净再按开关重开，避免叠加上一次的残留）</summary>
        void RestoreLiquid(VNSaveData data)
        {
            ResetLiquid();
            var l = data.liquid;
            if (l == null) return; // 旧存档：等价于什么都没开

            wetScreen?.SetCover(l.cover);

            if (l.wetOn && wetScreen != null)
                wetScreen.SetWet(true, VNLiquidPreset.Get(l.wetType),
                    l.wetAmount > 0f ? l.wetAmount : 1f);

            if (liquidSplash == null) return;

            if (l.sprayOn)
                liquidSplash.StartSpray(new Vector2(l.sprayX, l.sprayY),
                    VNLiquidPreset.Get(l.sprayType),
                    l.sprayPower > 0f ? l.sprayPower : 1f,
                    l.sprayDirSet ? l.sprayDir : float.NaN, l.spraySpread,
                    l.sprayRate > 0f ? l.sprayRate : 1f,
                    l.sprayScreen);

            if (l.clickOn)
                liquidSplash.SetClickMode(true, VNLiquidPreset.Get(l.clickType),
                    l.clickPower > 0f ? l.clickPower : 1f, l.clickScreen);
        }

        /// <summary>letterbox 命令入口：手动控制会接管自动黑边</summary>
        public void SetLetterbox(bool on, float height = -1f, float duration = -1f)
        {
            _letterboxAuto = false;
            _fxStates["letterbox"] = on;
            if (letterbox == null) return;
            if (on) letterbox.Show(height, duration);
            else letterbox.Hide(duration);
        }

        /// <summary>章节转场用：关闭天气、情绪色调和全部持续型画面特效。</summary>
        public void ResetEffects()
        {
            // CG 期间 reset effects：暂停恢复清单一并作废，cg off 后不再回放旧特效/天气
            _cgPausedFx.Clear();
            _cgSavedWeatherId = "";
            ClearWeatherOverrides();
            weather?.SetWeatherId("", 0.8f);
            mood?.SetMood(VNMood.Neutral, 0.8f);
            ResetLiquid();
            foreach (var name in ToggleFxNames) Fx(name, "off");
            Fx("focus", "off");
            // Ken Burns 是默认开启的常驻氛围（"永不静止"），重置回默认开而非关
            if (kenBurns != null && kenBurns.playOnAwake) Fx("kenburns", "on");
        }

        /// <summary>fx 命令：fx godrays on / fx dof off / fx focus 亚里沙 / fx heartbeat on …</summary>
        public void Fx(string name, string arg, int line = 0)
        {
            bool on = arg == "on" || arg == "true" || string.IsNullOrEmpty(arg);
            if (System.Array.IndexOf(ToggleFxNames, name) >= 0)
                _fxStates[name] = on && arg != "off"; // focus 等非开关型不记录
            switch (name)
            {
                case "godrays":
                    if (godRays == null) break;
                    if (on) godRays.Show(); else godRays.Hide();
                    break;
                case "dof":
                    if (fakeDoF != null) fakeDoF.SetFocus(on);
                    break;
                case "clouds":
                    if (cloudShadows == null) break;
                    if (on) cloudShadows.Show(); else cloudShadows.Hide();
                    break;
                case "haze":
                    if (heatHaze != null) heatHaze.SetActive(on);
                    break;
                case "shimmer":
                    if (backgroundFx == null) break;
                    if (on)
                    {
                        backgroundFx.SetWaterShimmer(0f);
                        backgroundFx.DOShimmerAmount(0.85f, 1f);
                    }
                    else backgroundFx.DOShimmerAmount(0f, 0.8f);
                    break;
                case "meteor":
                    if (shootingStars == null) break;
                    if (on) shootingStars.Show(); else shootingStars.Hide();
                    break;
                case "skycloud":
                    if (driftingClouds == null) break;
                    if (on) driftingClouds.Show(); else driftingClouds.Hide();
                    break;
                case "letterbox":
                    _letterboxAuto = false;
                    if (letterbox == null) break;
                    if (on) letterbox.Show(); else letterbox.Hide();
                    break;
                case "speedlines":
                    if (speedLines == null) break;
                    if (arg == "burst") speedLines.Burst(); // 一次性冲击，不记录开关状态
                    else if (on) speedLines.Show();
                    else speedLines.Hide();
                    break;
                case "shockwave": // 一次性演出，不记录开关状态
                    if (shockwave == null) break;
                    shockwave.Play(arg == "heavy" ? 1.4f : arg == "light" ? 0.6f : 1f);
                    break;
                case "filmgrain": // 与 crt 互斥：手动控制会接管 mood 自动滤镜
                    _retroAuto = false;
                    if (on) _fxStates["crt"] = false;
                    if (retroFilter == null) break;
                    if (on) retroFilter.ShowFilm(); else retroFilter.Hide();
                    break;
                case "crt":
                    _retroAuto = false;
                    if (on) _fxStates["filmgrain"] = false;
                    if (retroFilter == null) break;
                    if (on) retroFilter.ShowCrt(); else retroFilter.Hide();
                    break;
                case "kenburns":
                    if (kenBurns != null) kenBurns.SetPlaying(on);
                    break;
                case "heartbeat":
                    if (heartbeat == null) break;
                    if (on) heartbeat.StartBeat(); else heartbeat.StopBeat();
                    break;
                case "dutch":
                    if (dutchAngle == null) break;
                    if (on) dutchAngle.SetTilt(dutchAngle.angle); else dutchAngle.Clear();
                    break;
                case "focus":
                    if (vignetteFocus == null) break;
                    if (arg == "off" || string.IsNullOrEmpty(arg)) vignetteFocus.ClearFocus();
                    else
                    {
                        var c = Get(arg);
                        if (c != null) vignetteFocus.FocusOn(c.fx.Rect);
                        else Debug.LogWarning($"[VNScript] 第 {line} 行：fx focus 的角色「{arg}」不在场上");
                    }
                    break;
                default:
                    Debug.LogWarning($"[VNScript] 第 {line} 行：未知 fx「{name}」");
                    break;
            }
        }
    }
}
