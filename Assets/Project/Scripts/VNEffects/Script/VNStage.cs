using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 舞台管理器：剧本命令 → 现有 VNEffects API 的落地层。
    /// 负责：角色运行时生成（完整特效组件栈）/ 表情切换 / 背景切换 /
    /// 台词分发（联动说话者高亮）/ fx 开关分发。
    /// 场景生成器（Create Script Demo Scene）会自动连线全部引用。
    /// </summary>
    public partial class VNStage : MonoBehaviour
    {
        [Header("内容总配置（留空 = 自动读 Resources/VNGameConfig）\n" +
                "资产里填了的项覆盖下面的场景设置，留空的项保持场景原样")]
        public VNGameConfig config;

        [Header("角色与背景库")]
        public List<VNCharacterDef> characters = new List<VNCharacterDef>();

        // ★ 列表元素里的字段说明一律用 [Tooltip]，**绝对不要用 [Header]**。
        //   [Header] 是 DecoratorDrawer，Unity 画它的方式是把控件区域往下推
        //   （position.yMin += decorator.GetHeight()，约 26px）。列表里每一项都会重画一遍，
        //   既让一个条目占掉 6~7 行，又会在自定义 drawer 用固定 rect 画子属性时
        //   把控件推出 rect —— 表现为文字叠印且**输入框点不进去**。详见 WhatAiDo 一一九章。
        [System.Serializable]
        public class BackgroundEntry
        {
            [Tooltip("剧本 bg 命令引用的背景 id")]
            public string id;
            [Tooltip("背景图")]
            public Sprite sprite;
        }
        public List<BackgroundEntry> backgrounds = new List<BackgroundEntry>();

        [System.Serializable]
        public class CgEntry
        {
            [Tooltip("剧本 cg 命令引用的 CG id")]
            public string id;
            [Tooltip("CG 一枚绘")]
            public Sprite sprite;
            [Tooltip("差分组名（同组 CG 在鉴赏画廊里归为一格翻页；留空 = 独立）")]
            public string group;
            [Tooltip("显示方式（竖图/方图用）：拉伸=老行为；裁满=等比放大铺满；" +
                     "模糊填充/模糊+暗角/主色渐变=完整显示、两侧补底。剧本 cg <id> fit: 可临时覆盖")]
            public VNCgFitMode fit = VNCgFitMode.Stretch;
        }
        [Header("CG 库（生成器从 Assets/CG 自动灌入，文件名 = id）")]
        public List<CgEntry> cgLibrary = new List<CgEntry>();

        [Header("舞台引用（生成器自动连线）")]
        [Header("立绘层容器（LayerFront）")]
        public RectTransform characterLayer; // LayerFront
        [Header("背景 Image")]
        public Image backgroundImage;
        [Header("背景特效控制器")]
        public VNImageEffectController backgroundFx;
        [Header("对话框")]
        public VNDialogueBox dialogue;
        [Header("全屏转场")]
        public VNScreenTransition transition;
        [Header("天气控制器")]
        public VNWeatherController weather;
        [Header("情绪色调")]
        public VNMoodGrading mood;
        [Header("运镜（驱动 ZoomRoot）")]
        public VNCamera vnCamera;
        [Header("画面震动（驱动 SceneRoot）")]
        public VNScreenShake screenShake;
        [Header("荷兰角（驱动 TiltRoot）")]
        public VNDutchAngle dutchAngle;
        [Header("心跳脉动")]
        public VNHeartbeat heartbeat;
        [Header("樱吹雪组合技")]
        public VNSakuraBurst sakura;
        [Header("伪景深")]
        public VNFakeDoF fakeDoF;
        [Header("地面云影")]
        public VNCloudShadows cloudShadows;
        [Header("斜射光束")]
        public VNGodRays godRays;
        [Header("漫画速度线")]
        public VNSpeedLines speedLines;
        [Header("全屏情绪水波")]
        public VNScreenShockwave shockwave;
        [Header("胶片/CRT 复古滤镜")]
        public VNRetroFilter retroFilter;
        [Header("背景 Ken Burns 漂移")]
        public VNKenBurns kenBurns;
        [Header("背景无限滚动")]
        public VNBackgroundScroll bgScroll;
        [Header("电影黑边")]
        public VNLetterbox letterbox;
        [Header("夜晚流星")]
        public VNShootingStars shootingStars;
        [Header("云本体缓移")]
        public VNDriftingClouds driftingClouds;
        [Header("热浪扭曲")]
        public VNHeatHaze heatHaze;
        [Header("聚焦渐晕")]
        public VNVignetteFocus vignetteFocus;
        [Header("说话者高亮")]
        public VNSpeakerHighlight speakerHighlight;
        [Header("立绘色调匹配背景")]
        public VNToneMatch toneMatch;
        [Header("选项面板")]
        public VNChoicePanel choicePanel;
        [Header("音频管理器（BGM/SE/语音三通道）")]
        public VNAudio vnAudio;
        [Header("玩法事件注册表（event 命令）")]
        public VNEventRegistry eventRegistry;
        [Header("SNS 手机聊天视图（sns 命令；留空自动创建）")]
        public VNSnsView sns;
        [Header("过场层（interlude 命令；留空自动创建，会挂到主 Canvas 下）")]
        public VNInterludeScreen interlude;
        [Header("教程层（tutorial 命令 / 模块首次自动播；留空自动创建，会挂到主 Canvas 下）")]
        public VNTutorialPlayer tutorial;
        [Header("液体喷溅（舞台层：空中飞的水珠）")]
        public VNLiquidSplash liquidSplash;
        [Header("镜头水渍（屏幕层：溅在镜头玻璃上挂着往下淌的）")]
        public VNWetScreen wetScreen;

        [Header("表情切换的交叉溶解时长（0 = 瞬间切换）")]
        public float expressionCrossfade = 0.25f;

        [Header("角色生成参数")]
        public float characterHeight = 880f;

        public class ActiveCharacter
        {
            public VNCharacterDef def;
            public GameObject go;
            public Image image;
            public RectTransform rect;
            public VNImageEffectController fx;
            public VNEntranceAnimator animator;
            public VNCharacterEmotes emotes;
            public VNCharacterBlink blink;
            /// <summary>分层眨眼；与 blink 互斥，按 def.blinkMode 只有一个会真正工作</summary>
            public VNCharacterBlinkOverlay blinkOverlay;
            public VNCharacterMouth mouth;
            public VNCharacterMarks marks;
            /// <summary>情绪叠加层（潮红/汗/泪）；强度进存档</summary>
            public VNCharacterOverlay overlay;
            /// <summary>立绘痕迹（掌印等）；临时演出，不进存档</summary>
            public VNCharacterImprints imprints;
            public string expression;
            /// <summary>登场用的是日常向预设 → 常驻效果里不开周期扫光（进存档）</summary>
            public bool casualEntrance;
        }

        readonly Dictionary<string, ActiveCharacter> _active =
            new Dictionary<string, ActiveCharacter>();

        public ActiveCharacter Get(string id) =>
            id != null && _active.TryGetValue(id, out var c) ? c : null;

        /// <summary>全部在场角色（偷拍模式锁定目标用；只读，别在遍历时进出场）</summary>
        public IEnumerable<ActiveCharacter> ActiveCharacters => _active.Values;

        void Awake()
        {
            ApplyGameConfig();
            AutoWire();
        }

        /// <summary>
        /// 从 VNGameConfig 资产覆盖内容库（角色/背景/CG）。
        /// 场景重建会清空组件上的这些列表，但资产不会丢 —— 这就是它存在的意义。
        /// 覆盖语义：资产里非空的列表才覆盖，留空的保持场景原值。
        /// </summary>
        void ApplyGameConfig()
        {
            if (config != null) VNGameConfig.SetActive(config);
            var cfg = config != null ? config : VNGameConfig.Active;
            if (cfg == null) return;

            VNGameConfig.ApplyList(cfg.characters, ref characters);
            VNGameConfig.ApplyList(cfg.backgrounds, ref backgrounds);
            VNGameConfig.ApplyList(cfg.cgLibrary, ref cgLibrary);
        }

        /// <summary>
        /// 自动补线：Inspector 里为空的引用自动在场景中查找。
        /// 这样给 VNStage 加新字段后，旧场景不重新生成也能正常工作。
        /// </summary>
        void AutoWire()
        {
            if (dialogue == null) dialogue = FindFirstObjectByType<VNDialogueBox>();
            if (transition == null) transition = FindFirstObjectByType<VNScreenTransition>();
            if (weather == null) weather = FindFirstObjectByType<VNWeatherController>();
            if (mood == null) mood = FindFirstObjectByType<VNMoodGrading>();
            if (vnCamera == null) vnCamera = FindFirstObjectByType<VNCamera>();
            if (screenShake == null) screenShake = FindFirstObjectByType<VNScreenShake>();
            if (dutchAngle == null) dutchAngle = FindFirstObjectByType<VNDutchAngle>();
            if (heartbeat == null) heartbeat = FindFirstObjectByType<VNHeartbeat>();
            if (sakura == null) sakura = FindFirstObjectByType<VNSakuraBurst>();
            if (fakeDoF == null) fakeDoF = FindFirstObjectByType<VNFakeDoF>();
            if (cloudShadows == null) cloudShadows = FindFirstObjectByType<VNCloudShadows>();
            if (godRays == null) godRays = FindFirstObjectByType<VNGodRays>();
            if (speedLines == null) speedLines = FindFirstObjectByType<VNSpeedLines>();
            if (shockwave == null) shockwave = FindFirstObjectByType<VNScreenShockwave>();
            if (retroFilter == null) retroFilter = FindFirstObjectByType<VNRetroFilter>();
            if (letterbox == null) letterbox = FindFirstObjectByType<VNLetterbox>();
            if (shootingStars == null) shootingStars = FindFirstObjectByType<VNShootingStars>();
            if (driftingClouds == null) driftingClouds = FindFirstObjectByType<VNDriftingClouds>();
            if (heatHaze == null) heatHaze = FindFirstObjectByType<VNHeatHaze>();
            if (vignetteFocus == null) vignetteFocus = FindFirstObjectByType<VNVignetteFocus>();
            if (speakerHighlight == null) speakerHighlight = FindFirstObjectByType<VNSpeakerHighlight>();
            if (toneMatch == null) toneMatch = FindFirstObjectByType<VNToneMatch>();
            if (choicePanel == null) choicePanel = FindFirstObjectByType<VNChoicePanel>();
            if (vnAudio == null)
            {
                vnAudio = FindFirstObjectByType<VNAudio>();
                if (vnAudio == null) // 旧场景自愈：自动创建
                    vnAudio = new GameObject("VNAudio").AddComponent<VNAudio>();
            }
            if (sns == null)
            {
                sns = FindFirstObjectByType<VNSnsView>();
                if (sns == null) // 旧场景自愈：自动创建（UI 到 sns open 时才搭）
                    sns = new GameObject("VNSnsView").AddComponent<VNSnsView>();
            }
            if (interlude == null)
            {
                interlude = FindFirstObjectByType<VNInterludeScreen>(FindObjectsInactive.Include);
                if (interlude == null) // 旧场景自愈：自动创建（UI 到 interlude 命令时才搭）
                {
                    var go = new GameObject("VNInterludeScreen", typeof(RectTransform));
                    // 必须挂进主 Canvas，才能被排序 100 的全屏转场盖住（见 VNInterludeScreen 注释）
                    Transform host = transition != null ? transition.transform.parent
                                   : dialogue != null ? dialogue.transform.parent : null;
                    if (host != null) go.transform.SetParent(host, false);
                    interlude = go.AddComponent<VNInterludeScreen>();
                }
            }
            if (tutorial == null)
            {
                tutorial = FindFirstObjectByType<VNTutorialPlayer>(FindObjectsInactive.Include);
                if (tutorial == null) // 旧场景自愈：自动创建（UI 到第一次播教程时才搭）
                {
                    var go = new GameObject("VNTutorialPlayer", typeof(RectTransform));
                    // 同过场层：必须挂进主 Canvas，否则盖不住事件层、也吃不到 Bloom
                    Transform host = transition != null ? transition.transform.parent
                                   : dialogue != null ? dialogue.transform.parent : null;
                    if (host != null) go.transform.SetParent(host, false);
                    tutorial = go.AddComponent<VNTutorialPlayer>();
                }
            }

            if (wetScreen == null) wetScreen = FindFirstObjectByType<VNWetScreen>();
            if (liquidSplash == null) liquidSplash = FindFirstObjectByType<VNLiquidSplash>();
            // 两层是一个效果的两半：喷溅拿不到水渍层就只剩"有水在飞"，接上才完整
            if (liquidSplash != null && liquidSplash.wetScreen == null)
                liquidSplash.wetScreen = wetScreen;

            if (characterLayer == null)
            {
                var go = GameObject.Find("LayerFront");
                if (go != null) characterLayer = (RectTransform)go.transform;
            }
            if (backgroundImage == null)
            {
                var go = GameObject.Find("Background");
                if (go != null) backgroundImage = go.GetComponent<Image>();
            }
            if (backgroundFx == null && backgroundImage != null)
                backgroundFx = backgroundImage.GetComponent<VNImageEffectController>();
            // 情绪色调的分层调色主目标（老场景自愈：Inspector 没连也能工作）
            if (mood != null && backgroundFx != null) mood.RegisterBackground(backgroundFx);
            if (kenBurns == null) kenBurns = FindFirstObjectByType<VNKenBurns>();
            if (kenBurns == null && backgroundImage != null) // 旧场景自愈：自动补挂
                kenBurns = backgroundImage.gameObject.AddComponent<VNKenBurns>();
            // Ken Burns 默认开启：种入 fx 状态表，存档才能正确记录"仍开着"
            _fxStates["kenburns"] = kenBurns != null && kenBurns.playOnAwake;
            if (bgScroll == null) bgScroll = FindFirstObjectByType<VNBackgroundScroll>();
            if (bgScroll == null && backgroundImage != null) // 旧场景自愈：自动补挂
                bgScroll = backgroundImage.gameObject.AddComponent<VNBackgroundScroll>();
        }

    }
}
