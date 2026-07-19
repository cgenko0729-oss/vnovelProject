using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 场景生成器（两个菜单）：
    ///   Tools → VN Effects → Create Demo Scene        键盘演示场景（体验全部特效）
    ///   Tools → VN Effects → Create Script Demo Scene 剧本演示场景（VNStage+VNScriptRunner）
    /// 两者共享 BuildStageRig()：相机/后处理/Canvas/容器层级/全部特效管理器。
    /// </summary>
    public static class VNEffectsDemoSetup
    {
        const string MaterialsDir = "Assets/VNEffects/Materials";
        const string CharactersDir = "Assets/VNEffects/Characters";
        const string QuestsDir = "Assets/VNEffects/Quests";
        const string StatsDir = "Assets/VNEffects/Stats";
        const string ShopsDir = "Assets/VNEffects/Shops";
        const string PlansDir = "Assets/VNEffects/Plans";
        const string ScenePath = "Assets/Scenes/VNEffectsDemo.unity";
        const string ScriptScenePath = "Assets/Scenes/VNScriptDemo.unity";
        const string ProfilePath = "Assets/VNEffects/VNEffectsVolumeProfile.asset";
        const string DemoScriptPath = "Assets/Scenarios/Demo.vn.txt";

        /// <summary>BuildStageRig 的产物：舞台所有引用</summary>
        class RigRefs
        {
            public Camera cam;
            public GameObject canvasGo;
            public RectTransform sceneRoot, zoomRoot, tiltRoot, layerBack, layerMid, layerFront;
            public VNImageEffectController bgFx;
            public Image bgImage;
            public VNKenBurns kenBurns;
            public VNGodRays godRays;
            public VNEdgeGlow edgeGlow;
            public VNVignetteFocus vignetteFocus;
            public VNWeatherController weather;
            public VNMoodGrading mood;
            public VNScreenTransition transition;
            public VNScreenShake screenShake;
            public VNParallax parallax;
            public VNDutchAngle dutchAngle;
            public VNCamera vnCamera;
            public VNHeartbeat heartbeat;
            public VNSakuraBurst sakura;
            public VNFakeDoF fakeDoF;
            public VNCloudShadows cloudShadows;
            public VNSpeedLines speedLines;
            public VNScreenShockwave shockwave;
            public VNRetroFilter retroFilter;
            public VNLetterbox letterbox;
            public VNShootingStars shootingStars;
            public VNDriftingClouds driftingClouds;
            public VNToneMatch toneMatch;
            public VNChoicePanel choicePanel;
            public VNMouseStardust stardust;
            public VNHeatHaze heatHaze;
            public VNDialogueBox dialogueBox;
            public VNSpeakerHighlight speakerHighlight;
            public VNAmbientParticles[] particles;
            public Material imageMat, additiveMat, transitionMat;
            public Sprite charSprite, charSprite2, bgSprite;
            public System.Collections.Generic.List<Sprite> allSprites;
        }

        // ==================================================================
        // 共享舞台装配
        // ==================================================================

        static RigRefs BuildStageRig()
        {
            var rig = new RigRefs();

            // ---------- 1. 找到并正确导入演示图 ----------
            var (charSprite, charSprite2, bgSprite, allSprites) = PrepareSprites();
            rig.charSprite = charSprite;
            rig.charSprite2 = charSprite2;
            rig.bgSprite = bgSprite;
            rig.allSprites = allSprites;

            // ---------- 2. 材质资产 ----------
            EnsureFolder("Assets/VNEffects");
            EnsureFolder(MaterialsDir);
            rig.imageMat = EnsureMaterial($"{MaterialsDir}/VNImageEffect.mat", "VN/ImageEffect");
            rig.additiveMat = EnsureMaterial($"{MaterialsDir}/VNAdditive.mat", "VN/Additive");
            rig.transitionMat = EnsureMaterial($"{MaterialsDir}/VNScreenTransition.mat", "VN/ScreenTransition");

            // ---------- 3. 后处理 Volume Profile（Bloom + Vignette）----------
            var profile = EnsureVolumeProfile();

            // ---------- 4. 新场景：相机 + 全局 Volume ----------
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.06f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            rig.cam = cam;

            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // ---------- 5. Canvas（Screen Space - Camera）----------
            var canvasGo = new GameObject("Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            canvas.sortingOrder = 0;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            rig.canvasGo = canvasGo;

            // ---------- 5.5 画面容器层级 ----------
            // SceneRoot(震动+心跳) > ZoomRoot(运镜) > TiltRoot(荷兰角) > 视差三层
            rig.sceneRoot = CreateStretchRect("SceneRoot", canvasGo.transform);
            rig.zoomRoot = CreateStretchRect("ZoomRoot", rig.sceneRoot);
            rig.tiltRoot = CreateStretchRect("TiltRoot", rig.zoomRoot);
            rig.layerBack = CreateStretchRect("LayerBack", rig.tiltRoot);
            rig.layerMid = CreateStretchRect("LayerMid", rig.tiltRoot);
            rig.layerFront = CreateStretchRect("LayerFront", rig.tiltRoot);

            // ---------- 6. 背景图 ----------
            if (bgSprite != null)
            {
                var bgGo = CreateUIImage("Background", rig.layerBack, bgSprite);
                var bgRect = (RectTransform)bgGo.transform;
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                // 四边溢出 60px，给 Ken Burns / 视差留余量
                bgRect.offsetMin = new Vector2(-60f, -60f);
                bgRect.offsetMax = new Vector2(60f, 60f);
                rig.bgImage = bgGo.GetComponent<Image>();
                rig.bgFx = bgGo.AddComponent<VNImageEffectController>();
                AssignSourceMaterial(rig.bgFx, rig.imageMat);
                rig.kenBurns = bgGo.AddComponent<VNKenBurns>(); // 背景永不静止
            }

            // ---------- 6.5 God Rays（背景之后、立绘之前）----------
            var godRaysRect = CreateStretchRect("GodRays", rig.layerMid);
            rig.godRays = godRaysRect.gameObject.AddComponent<VNGodRays>();
            AssignSourceMaterial(rig.godRays, rig.additiveMat);

            // ---------- 7. 悬浮氛围粒子 ----------
            rig.particles = new[]
            {
                CreateAmbient("Ambient_Dust", VNAmbientParticles.Preset.Dust,
                    new Color(1f, 0.97f, 0.88f), 11, rig.additiveMat),
                CreateAmbient("Ambient_Sparkles", VNAmbientParticles.Preset.Sparkles,
                    new Color(1f, 0.93f, 0.65f), 12, rig.additiveMat),
                CreateAmbient("Ambient_Orbs", VNAmbientParticles.Preset.Orbs,
                    new Color(0.75f, 0.85f, 1f), 10, rig.additiveMat),
            };

            // ---------- 8. 边缘情绪泛光 ----------
            var edgeGlowGo = new GameObject("EdgeGlow", typeof(RectTransform));
            edgeGlowGo.transform.SetParent(canvasGo.transform, false);
            rig.edgeGlow = edgeGlowGo.AddComponent<VNEdgeGlow>();
            AssignSourceMaterial(rig.edgeGlow, rig.additiveMat);

            // ---------- 8.5 漫画速度线/集中线 ----------
            var speedLinesGo = new GameObject("SpeedLines", typeof(RectTransform));
            speedLinesGo.transform.SetParent(canvasGo.transform, false);
            rig.speedLines = speedLinesGo.AddComponent<VNSpeedLines>();
            AssignSourceMaterial(rig.speedLines, rig.additiveMat);

            // ---------- 8.55 全屏情绪水波 ----------
            var shockwaveGo = new GameObject("ScreenShockwave", typeof(RectTransform));
            shockwaveGo.transform.SetParent(canvasGo.transform, false);
            rig.shockwave = shockwaveGo.AddComponent<VNScreenShockwave>();
            rig.shockwave.targets = rig.bgFx != null
                ? new[] { rig.bgFx } : new VNImageEffectController[0];
            // screenShake 在步骤 11 创建后回填

            // ---------- 8.58 胶片颗粒/CRT 复古滤镜 ----------
            var retroGo = new GameObject("RetroFilter", typeof(RectTransform));
            retroGo.transform.SetParent(canvasGo.transform, false);
            rig.retroFilter = retroGo.AddComponent<VNRetroFilter>();

            // ---------- 8.6 电影 Letterbox 黑边 ----------
            var letterboxGo = new GameObject("Letterbox", typeof(RectTransform));
            letterboxGo.transform.SetParent(canvasGo.transform, false);
            rig.letterbox = letterboxGo.AddComponent<VNLetterbox>();

            // ---------- 9. 聚焦渐晕（挂在 Volume 上）----------
            rig.vignetteFocus = volGo.AddComponent<VNVignetteFocus>();
            rig.vignetteFocus.volume = vol;

            // ---------- 10. 天气 / 色调 / 转场 ----------
            var weatherGo = new GameObject("WeatherController");
            rig.weather = weatherGo.AddComponent<VNWeatherController>();
            rig.weather.additiveMaterial = rig.additiveMat;
            rig.weather.moodTargets = rig.bgFx != null
                ? new[] { rig.bgFx } : new VNImageEffectController[0];

            rig.mood = new GameObject("MoodGrading").AddComponent<VNMoodGrading>();

            var transitionGo = new GameObject("ScreenTransition", typeof(RectTransform));
            transitionGo.transform.SetParent(canvasGo.transform, false);
            rig.transition = transitionGo.AddComponent<VNScreenTransition>();
            AssignSourceMaterial(rig.transition, rig.transitionMat);

            // ---------- 11. 震动 / 视差 / 荷兰角 / 涟漪 ----------
            rig.screenShake = new GameObject("ScreenShake").AddComponent<VNScreenShake>();
            rig.screenShake.target = rig.sceneRoot;
            rig.shockwave.screenShake = rig.screenShake;

            rig.parallax = new GameObject("Parallax").AddComponent<VNParallax>();
            rig.parallax.layers.Add(new VNParallax.Layer { rect = rig.layerBack, strength = 8f });
            rig.parallax.layers.Add(new VNParallax.Layer { rect = rig.layerMid, strength = 13f });
            rig.parallax.layers.Add(new VNParallax.Layer { rect = rig.layerFront, strength = 19f });

            rig.dutchAngle = new GameObject("DutchAngle").AddComponent<VNDutchAngle>();
            rig.dutchAngle.target = rig.tiltRoot;

            var rippleGo = new GameObject("ClickRipple", typeof(ParticleSystem));
            var clickRipple = rippleGo.AddComponent<VNClickRipple>();
            AssignSourceMaterial(clickRipple, rig.additiveMat);

            // ---------- 12. 伪景深 / 云影 / 色调匹配 / 选项面板 / EventSystem ----------
            rig.fakeDoF = new GameObject("FakeDoF").AddComponent<VNFakeDoF>();
            rig.fakeDoF.backgroundFx = rig.bgFx;
            rig.fakeDoF.backLayer = rig.layerBack;

            var cloudRect = CreateStretchRect("CloudShadows", rig.layerBack);
            rig.cloudShadows = cloudRect.gameObject.AddComponent<VNCloudShadows>();

            // ---------- 12.5 云本体缓移 / 夜晚流星（背景层，立绘之下）----------
            var skyCloudRect = CreateStretchRect("DriftingClouds", rig.layerBack);
            rig.driftingClouds = skyCloudRect.gameObject.AddComponent<VNDriftingClouds>();

            var meteorRect = CreateStretchRect("ShootingStars", rig.layerBack);
            rig.shootingStars = meteorRect.gameObject.AddComponent<VNShootingStars>();
            AssignSourceMaterial(rig.shootingStars, rig.additiveMat);

            rig.toneMatch = new GameObject("ToneMatch").AddComponent<VNToneMatch>();
            rig.toneMatch.characters = new VNImageEffectController[0];

            var choiceGo = new GameObject("ChoicePanel", typeof(RectTransform));
            choiceGo.transform.SetParent(canvasGo.transform, false);
            rig.choicePanel = choiceGo.AddComponent<VNChoicePanel>();

            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            }

            // ---------- 13. 运镜 / 心跳 / 樱吹雪 ----------
            rig.vnCamera = new GameObject("VNCamera").AddComponent<VNCamera>();
            rig.vnCamera.target = rig.zoomRoot;

            // 镜头交叉淡化覆盖层（camseq start:fade / end:fade / xfade: 用）
            var camFadeGo = new GameObject("CameraFade", typeof(RectTransform));
            camFadeGo.transform.SetParent(canvasGo.transform, false);
            rig.vnCamera.cameraFade = camFadeGo.AddComponent<VNCameraFade>();

            rig.heartbeat = new GameObject("Heartbeat").AddComponent<VNHeartbeat>();
            rig.heartbeat.target = rig.sceneRoot;
            rig.heartbeat.edgeGlow = rig.edgeGlow;

            rig.sakura = new GameObject("SakuraBurst").AddComponent<VNSakuraBurst>();
            rig.sakura.additiveMaterial = rig.additiveMat;
            rig.sakura.heartbeat = rig.heartbeat;

            // ---------- 14. 说话者高亮 / 对话框 ----------
            rig.speakerHighlight = new GameObject("SpeakerHighlight").AddComponent<VNSpeakerHighlight>();

            var dialogueGo = new GameObject("DialogueBox", typeof(RectTransform));
            var dialogueRect = (RectTransform)dialogueGo.transform;
            dialogueRect.SetParent(canvasGo.transform, false);
            dialogueRect.anchorMin = new Vector2(0.05f, 0f);
            dialogueRect.anchorMax = new Vector2(0.95f, 0f);
            dialogueRect.pivot = new Vector2(0.5f, 0f);
            dialogueRect.anchoredPosition = new Vector2(0f, 28f);
            dialogueRect.sizeDelta = new Vector2(0f, 230f);
            rig.dialogueBox = dialogueGo.AddComponent<VNDialogueBox>();

            // ---------- 15. 鼠标星尘 / 热浪 ----------
            var stardustGo = new GameObject("MouseStardust", typeof(ParticleSystem));
            rig.stardust = stardustGo.AddComponent<VNMouseStardust>();
            AssignSourceMaterial(rig.stardust, rig.additiveMat);

            rig.heatHaze = new GameObject("HeatHaze").AddComponent<VNHeatHaze>();
            rig.heatHaze.targets = rig.bgFx != null
                ? new[] { rig.bgFx } : new VNImageEffectController[0];
            rig.heatHaze.additiveMaterial = rig.additiveMat;

            return rig;
        }

        // ==================================================================
        // 菜单一：键盘演示场景
        // ==================================================================

        [MenuItem("Tools/VN Effects/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var rig = BuildStageRig();

            // ---------- 立绘（有两张 solo 图时创建双角色）----------
            VNEntranceAnimator charAnim = null, charAnimB = null;
            VNImageEffectController charFx = null, charFxB = null;
            VNCharacterEmotes charEmotes = null;
            bool twoChars = rig.charSprite != null && rig.charSprite2 != null;
            float charHeight = twoChars ? 880f : 980f;

            if (rig.charSprite != null)
            {
                var pos = twoChars ? new Vector2(-380f, -60f) : new Vector2(0f, -40f);
                var (anim, fx) = CreateCharacter("Character", rig.charSprite, rig.layerFront,
                    pos, charHeight, rig.imageMat, rig.additiveMat);
                charAnim = anim;
                charFx = fx;
                charEmotes = fx.gameObject.AddComponent<VNCharacterEmotes>();
            }
            if (twoChars)
            {
                var (anim, fx) = CreateCharacter("CharacterB", rig.charSprite2, rig.layerFront,
                    new Vector2(380f, -60f), charHeight, rig.imageMat, rig.additiveMat);
                charAnimB = anim;
                charFxB = fx;
            }

            // 角色相关的注册
            if (charFx != null) rig.vnCamera.dollyCharacters.Add(charFx);
            if (charFxB != null) rig.vnCamera.dollyCharacters.Add(charFxB);
            if (charFx != null) rig.speakerHighlight.characters.Add(charFx);
            if (charFxB != null) rig.speakerHighlight.characters.Add(charFxB);
            rig.weather.moodTargets = (rig.bgFx != null && charFx != null)
                ? new[] { rig.bgFx, charFx }
                : rig.weather.moodTargets;
            rig.toneMatch.characters = (charFx != null && charFxB != null)
                ? new[] { charFx, charFxB }
                : (charFx != null ? new[] { charFx } : new VNImageEffectController[0]);

            // ---------- 操作提示文字 ----------
            var hint = CreateHintText(rig.canvasGo.transform, 320f);

            // ---------- 演示驱动 ----------
            var demo = new GameObject("VNEffectsDemo").AddComponent<VNEffectsDemo>();
            demo.character = charAnim;
            demo.characterFx = charFx;
            demo.backgroundFx = rig.bgFx;
            demo.ambientParticles = rig.particles;
            demo.hintText = hint;
            demo.godRays = rig.godRays;
            demo.vignetteFocus = rig.vignetteFocus;
            demo.edgeGlow = rig.edgeGlow;
            demo.weather = rig.weather;
            demo.mood = rig.mood;
            demo.transition = rig.transition;
            demo.emotes = charEmotes;
            demo.backgroundVariants = rig.allSprites.ToArray();
            demo.stardust = rig.stardust;
            demo.heatHaze = rig.heatHaze;
            demo.characterB = charAnimB;
            demo.characterBFx = charFxB;
            demo.speakerHighlight = rig.speakerHighlight;
            demo.screenShake = rig.screenShake;
            demo.dialogue = rig.dialogueBox;
            demo.parallax = rig.parallax;
            demo.dutchAngle = rig.dutchAngle;
            demo.vnCamera = rig.vnCamera;
            demo.heartbeat = rig.heartbeat;
            demo.sakura = rig.sakura;
            demo.fakeDoF = rig.fakeDoF;
            demo.cloudShadows = rig.cloudShadows;
            demo.speedLines = rig.speedLines;
            demo.shockwave = rig.shockwave;
            demo.retroFilter = rig.retroFilter;
            demo.kenBurns = rig.kenBurns;
            demo.letterbox = rig.letterbox;
            demo.shootingStars = rig.shootingStars;
            demo.driftingClouds = rig.driftingClouds;
            demo.toneMatch = rig.toneMatch;
            demo.choicePanel = rig.choicePanel;

            // ---------- 保存 ----------
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"[VNEffects] 键盘演示场景已保存到 {ScenePath}。按键说明见画面底部提示。");
        }

        // ==================================================================
        // 菜单二：剧本演示场景（VNStage + VNScriptRunner）
        // ==================================================================

        [MenuItem("Tools/VN Effects/Create Script Demo Scene")]
        public static void CreateScriptDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var rig = BuildStageRig();

            // ---------- 内容总配置（重建场景不丢人工绑定的关键） ----------
            // 存在 Assets/Resources/VNGameConfig.asset 时，下面所有"内容类"设置
            // 都以它为准；本生成器只负责把它挂上去，不再覆盖用户数据。
            var config = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            bool hasConfigBackgrounds = config != null && config.backgrounds.Count > 0;
            bool hasConfigCharacters = config != null && config.characters.Count > 0;
            bool hasConfigCg = config != null && config.cgLibrary.Count > 0;

            // ---------- 角色定义资产 ----------
            EnsureFolder(CharactersDir);
            var defA = rig.charSprite != null
                ? EnsureCharacterDef("亚里沙", new Color(0.45f, 0.3f, 0.75f, 0.9f), rig.charSprite)
                : null;
            var defB = rig.charSprite2 != null
                ? EnsureCharacterDef("小雪", new Color(0.25f, 0.45f, 0.8f, 0.9f), rig.charSprite2)
                : null;

            // ---------- 演示剧本 ----------
            var scriptAsset = EnsureDemoScript();

            // ---------- VNStage ----------
            var stage = new GameObject("VNStage").AddComponent<VNStage>();
            stage.config = config;

            // 角色：扫 Characters 目录全部登记（新建角色资产后无需手动拖）
            if (hasConfigCharacters)
            {
                stage.characters.AddRange(config.characters);
            }
            else
            {
                stage.characters.AddRange(FindAllAssets<VNCharacterDef>());
                if (stage.characters.Count == 0)
                {
                    if (defA != null) stage.characters.Add(defA);
                    if (defB != null) stage.characters.Add(defB);
                }
            }

            // 背景库：配置资产里有就用它（语义 id），否则退回 demo 的 bg1/bg2… 自动编号
            if (hasConfigBackgrounds)
            {
                stage.backgrounds.AddRange(config.backgrounds);
            }
            else
            {
                int bgIndex = 1;
                if (rig.bgSprite != null)
                    stage.backgrounds.Add(new VNStage.BackgroundEntry
                        { id = $"bg{bgIndex++}", sprite = rig.bgSprite });
                foreach (var s in rig.allSprites)
                {
                    if (s == rig.bgSprite) continue;
                    stage.backgrounds.Add(new VNStage.BackgroundEntry
                        { id = $"bg{bgIndex++}", sprite = s });
                }
            }

            // CG 库：Assets/CG 下的图自动灌入（文件名 = id）
            if (hasConfigCg) stage.cgLibrary.AddRange(config.cgLibrary);
            else FillCgLibrary(stage);

            stage.characterLayer = rig.layerFront;
            stage.backgroundImage = rig.bgImage;
            stage.backgroundFx = rig.bgFx;
            stage.dialogue = rig.dialogueBox;
            stage.transition = rig.transition;
            stage.weather = rig.weather;
            stage.mood = rig.mood;
            stage.vnCamera = rig.vnCamera;
            stage.screenShake = rig.screenShake;
            stage.dutchAngle = rig.dutchAngle;
            stage.heartbeat = rig.heartbeat;
            stage.sakura = rig.sakura;
            stage.fakeDoF = rig.fakeDoF;
            stage.cloudShadows = rig.cloudShadows;
            stage.godRays = rig.godRays;
            stage.speedLines = rig.speedLines;
            stage.shockwave = rig.shockwave;
            stage.retroFilter = rig.retroFilter;
            stage.kenBurns = rig.kenBurns;
            stage.letterbox = rig.letterbox;
            stage.shootingStars = rig.shootingStars;
            stage.driftingClouds = rig.driftingClouds;
            stage.heatHaze = rig.heatHaze;
            stage.vignetteFocus = rig.vignetteFocus;
            stage.speakerHighlight = rig.speakerHighlight;
            stage.toneMatch = rig.toneMatch;
            stage.choicePanel = rig.choicePanel;
            var vnAudio = new GameObject("VNAudio").AddComponent<VNAudio>();
            vnAudio.config = config; // 音频库与音量标定由配置资产提供（生成器不再造空库）
            if (config != null)
            {
                vnAudio.bgmLibrary.AddRange(config.bgmLibrary);
                vnAudio.seLibrary.AddRange(config.seLibrary);
                vnAudio.voiceLibrary.AddRange(config.voiceLibrary);
                if (config.typingTick != null) vnAudio.typingTick = config.typingTick;
            }
            stage.vnAudio = vnAudio;

            // ---------- 事件模块注册表（含 QTE 连打示例） ----------
            var registry = new GameObject("VNEventRegistry").AddComponent<VNEventRegistry>();
            var qteGo = new GameObject("QteTemplate", typeof(RectTransform));
            qteGo.transform.SetParent(registry.transform, false);
            qteGo.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
            var qte = qteGo.AddComponent<VNQteModule>();
            registry.modules.Add(new VNEventRegistry.Entry { id = "qte", template = qte });

            var mapGo = new GameObject("MapTemplate", typeof(RectTransform));
            mapGo.transform.SetParent(registry.transform, false);
            mapGo.SetActive(false);
            var mapModule = mapGo.AddComponent<VNMapModule>();
            // 地图底图与地点：配置资产优先（坐标/条件是人工数据，重建不能丢）
            if (config != null && config.mapLocations.Count > 0)
            {
                mapModule.mapSprite = config.mapSprite != null ? config.mapSprite : rig.bgSprite;
                mapModule.locations.AddRange(config.mapLocations);
            }
            else
            {
                mapModule.mapSprite = rig.bgSprite; // 演示用背景当地图底图
                mapModule.locations.Add(new VNMapModule.Location
                    { name = "教室", position = new Vector2(0.28f, 0.55f) });
                mapModule.locations.Add(new VNMapModule.Location
                    { name = "图书馆", position = new Vector2(0.68f, 0.6f) });
                mapModule.locations.Add(new VNMapModule.Location
                    { name = "天台", position = new Vector2(0.5f, 0.82f), condition = "好感度>=2" });
            }
            registry.modules.Add(new VNEventRegistry.Entry { id = "map", template = mapModule });

            // 商店模块（event shop id:服装店）
            EnsureFolder(ShopsDir);
            var shopGo = new GameObject("ShopTemplate", typeof(RectTransform));
            shopGo.transform.SetParent(registry.transform, false);
            shopGo.SetActive(false);
            var shopModule = shopGo.AddComponent<VNShopModule>();
            var demoShop = EnsureShopDef();
            // 扫 Shops 目录全部登记（新建商店资产后无需手动拖）
            shopModule.shops.AddRange(FindAllAssets<VNShopDef>());
            if (shopModule.shops.Count == 0) shopModule.shops.Add(demoShop);
            registry.modules.Add(new VNEventRegistry.Entry { id = "shop", template = shopModule });

            // 日程排程模块（event plan slots:7 pool:… / event plan op:next）
            EnsureFolder(PlansDir);
            var planGo = new GameObject("PlanTemplate", typeof(RectTransform));
            planGo.transform.SetParent(registry.transform, false);
            planGo.SetActive(false);
            var planModule = planGo.AddComponent<VNPlanModule>();
            EnsurePlanDef();
            planModule.plans.AddRange(FindAllAssets<VNPlanDef>());
            registry.modules.Add(new VNEventRegistry.Entry { id = "plan", template = planModule });

            // 结果结算弹窗模块（event result grade:great title:xx）
            var resultGo = new GameObject("ResultTemplate", typeof(RectTransform));
            resultGo.transform.SetParent(registry.transform, false);
            resultGo.SetActive(false);
            var resultModule = resultGo.AddComponent<VNResultPopupModule>();
            registry.modules.Add(new VNEventRegistry.Entry { id = "result", template = resultModule });

            // 回合制小战斗模块（event battle enemy:xx ehp:26 …；结果 胜利/失败/逃跑）
            var battleGo = new GameObject("BattleTemplate", typeof(RectTransform));
            battleGo.transform.SetParent(registry.transform, false);
            battleGo.SetActive(false);
            var battleModule = battleGo.AddComponent<VNBattleModule>();
            registry.modules.Add(new VNEventRegistry.Entry { id = "battle", template = battleModule });
            stage.eventRegistry = registry;

            // ---------- 任务系统（示例任务定义 + 日志组件） ----------
            EnsureFolder(QuestsDir);
            var questLog = new GameObject("VNQuestLog").AddComponent<VNQuestLog>();
            EnsureQuestDef();
            questLog.quests.AddRange(FindAllAssets<VNQuestDef>());

            // ---------- 养成属性系统（示例属性定义 + HUD/面板组件） ----------
            EnsureFolder(StatsDir);
            var statsHud = new GameObject("VNStatsHud").AddComponent<VNStatsHud>();
            EnsureStatDefs();
            statsHud.stats.AddRange(FindAllAssets<VNStatDef>());

            // ---------- 物品栏（道具文案来源 = 商店定义） ----------
            var inventory = new GameObject("VNInventory").AddComponent<VNInventory>();
            inventory.shops.AddRange(shopModule.shops);

            // ---------- CG 鉴赏画廊（目录取 VNStage.cgLibrary，解锁取 VNCgUnlocks） ----------
            var cgGallery = new GameObject("VNCgGallery").AddComponent<VNCgGallery>();
            cgGallery.stage = stage;

            // ---------- 标题菜单（开始/继续/读档/鉴赏/设置/退出；Runner 启动时接管） ----------
            // 标题文字/背景/BGM 在 VNGameConfig 的"标题画面"区配置；调试剧情嫌它挡路时
            // 把场景里这个物体的 showOnStart 勾掉即可（重建场景会恢复默认开）。
            new GameObject("VNTitleMenu").AddComponent<VNTitleMenu>();

            // ---------- VNScriptRunner + Backlog ----------
            var runner = new GameObject("VNScriptRunner").AddComponent<VNScriptRunner>();
            runner.stage = stage;
            runner.config = config;
            // 入口剧本：配置资产指定了就用它，否则用 demo
            runner.script = config != null && config.entryScript != null
                ? config.entryScript
                : scriptAsset;
            runner.playOnStart = true;
            // 章节列表：扫 Assets/Scenarios 下全部 *.vn.txt 自动登记。
            // 以前只登记两个 demo，导致重建后所有 jump 文件::标签 全部报错。
            runner.chapters.AddRange(ScanScenarioAssets());
            new GameObject("VNBacklog").AddComponent<VNBacklog>();

            // ---------- 极简提示 ----------
            var hint = CreateHintText(rig.canvasGo.transform, 70f);
            hint.text = "Enter/空格/点击 推进（打字中=催促） | H/滚轮上滑 回想 | A 自动 | S 快进\n" +
                        "F5 存档界面 | F9 读档界面 | J 任务日志 | C 属性面板 | I 物品栏 | G CG鉴赏";

            // ---------- 保存 ----------
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScriptScenePath);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScriptScenePath));
            Debug.Log($"[VNScript] 剧本演示场景已保存到 {ScriptScenePath}。" +
                      $"入口剧本：{(runner.script != null ? runner.script.name : "(无)")}，" +
                      $"章节 {runner.chapters.Count} 个，角色 {stage.characters.Count} 个，" +
                      $"背景 {stage.backgrounds.Count} 条。");
            if (config == null)
                Debug.LogWarning("[VNScript] 未找到 " + VNGameConfig.AssetPath +
                                 "：背景/音频/入口剧本等人工绑定仍然只存在场景里，下次重建会丢失。\n" +
                                 "建议执行 Tools → VN Effects → Game Config → Import From Scene " +
                                 "把当前绑定搬进资产。");
            else
                Debug.Log($"[VNScript] 已挂载内容配置 {VNGameConfig.AssetPath}：" +
                          $"背景 {config.backgrounds.Count} / BGM {config.bgmLibrary.Count} / " +
                          $"SE {config.seLibrary.Count} / 地图地点 {config.mapLocations.Count}。");
        }

        // ==================================================================
        // 资产扫描（约定优于配置：新建资产丢进目录即自动登记，重建场景不会漏）
        // ==================================================================

        /// <summary>按类型扫描整个工程里的资产，按路径排序返回。</summary>
        static List<T> FindAllAssets<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();
        }

        /// <summary>扫描 Assets/Scenarios 下的全部 *.vn.txt 作为章节列表。</summary>
        static List<TextAsset> ScanScenarioAssets()
        {
            const string dir = "Assets/Scenarios";
            if (!AssetDatabase.IsValidFolder(dir)) return new List<TextAsset>();
            return AssetDatabase.FindAssets("t:TextAsset", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".vn.txt"))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<TextAsset>)
                .Where(a => a != null)
                .ToList();
        }

        // ==================================================================
        // 剧本系统辅助
        // ==================================================================

        /// <summary>示例属性定义：顶栏四项（金钱/行动力/压力/善恶）+ 面板四维（体力/智力/魅力/感性）</summary>
        static List<VNStatDef> EnsureStatDefs()
        {
            var defs = new List<VNStatDef>();

            VNStatDef Ensure(string id, System.Action<VNStatDef> setup)
            {
                string path = $"{StatsDir}/{id}.asset";
                var def = AssetDatabase.LoadAssetAtPath<VNStatDef>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<VNStatDef>();
                    def.id = id;
                    def.displayName = id;
                    setup(def);
                    AssetDatabase.CreateAsset(def, path);
                }
                defs.Add(def);
                return def;
            }

            void Grades(VNStatDef def)
            {
                def.style = VNStatStyle.Grade;
                def.useClamp = true;
                def.minValue = 0;
                def.maxValue = 500;
                def.showInHud = false; // 四维属性只进 C 键面板，顶栏保持紧凑
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 0, label = "E" });
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 50, label = "D" });
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 100, label = "C" });
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 200, label = "B" });
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 350, label = "A" });
                def.gradeSteps.Add(new VNStatDef.GradeStep { threshold = 500, label = "S" });
            }

            Ensure("金钱", def =>
            {
                def.displayNameEn = "Money"; def.displayNameJa = "所持金";
                def.style = VNStatStyle.Number; def.unit = "G";
                def.useClamp = true; def.minValue = 0; def.maxValue = 999999;
                def.initialValue = 500;
                def.color = new Color(1f, 0.82f, 0.32f);
            });
            Ensure("行动力", def =>
            {
                def.displayNameEn = "Energy"; def.displayNameJa = "行動力";
                def.style = VNStatStyle.OutOfMax;
                def.useClamp = true; def.minValue = 0; def.maxValue = 10;
                def.initialValue = 10;
                def.color = new Color(0.45f, 0.85f, 1f);
            });
            Ensure("压力", def =>
            {
                def.displayNameEn = "Stress"; def.displayNameJa = "ストレス";
                def.style = VNStatStyle.Percent;
                def.useClamp = true; def.minValue = 0; def.maxValue = 100;
                def.initialValue = 0;
                def.color = new Color(1f, 0.55f, 0.3f);
            });
            Ensure("善恶", def =>
            {
                def.displayNameEn = "Morality"; def.displayNameJa = "善悪";
                def.style = VNStatStyle.Percent;
                def.useClamp = true; def.minValue = 0; def.maxValue = 100;
                def.initialValue = 50;
                def.color = new Color(0.75f, 0.55f, 1f);
            });
            Ensure("体力", def =>
            {
                def.displayNameEn = "Vitality"; def.displayNameJa = "体力";
                def.initialValue = 70; def.color = new Color(1f, 0.45f, 0.45f);
                Grades(def);
            });
            Ensure("智力", def =>
            {
                def.displayNameEn = "Intelligence"; def.displayNameJa = "知力";
                def.initialValue = 20; def.color = new Color(0.45f, 0.65f, 1f);
                Grades(def);
            });
            Ensure("魅力", def =>
            {
                def.displayNameEn = "Charm"; def.displayNameJa = "魅力";
                def.initialValue = 20; def.color = new Color(1f, 0.5f, 0.85f);
                Grades(def);
            });
            Ensure("感性", def =>
            {
                def.displayNameEn = "Sensitivity"; def.displayNameJa = "感性";
                def.initialValue = 20; def.color = new Color(0.5f, 0.9f, 0.55f);
                Grades(def);
            });
            return defs;
        }

        /// <summary>示例商店：服装店（event shop id:服装店），含买入/卖出/条件商品</summary>
        static VNShopDef EnsureShopDef()
        {
            string path = $"{ShopsDir}/服装店.asset";
            var def = AssetDatabase.LoadAssetAtPath<VNShopDef>(path);
            if (def != null) return def;

            def = ScriptableObject.CreateInstance<VNShopDef>();
            def.shopId = "服装店";
            def.shopName = "服装店";
            def.shopNameEn = "Boutique";
            def.shopNameJa = "服屋";
            def.currencyStat = "金钱";
            def.items.Add(new VNShopDef.Item
            {
                id = "蝴蝶结发饰", displayName = "蝴蝶结发饰",
                displayNameEn = "Ribbon Hairpin", displayNameJa = "リボンの髪飾り",
                description = "可爱的蝴蝶结，送人应该会很开心。",
                descriptionEn = "A cute ribbon. Would make a lovely gift.",
                descriptionJa = "かわいいリボン。プレゼントに喜ばれそう。",
                price = 120, sellPrice = 60, maxOwned = 0,
            });
            def.items.Add(new VNShopDef.Item
            {
                id = "洋装", displayName = "洋装",
                displayNameEn = "Dress", displayNameJa = "ドレス",
                description = "漂亮但有点贵的洋装。",
                descriptionEn = "A pretty but pricey dress.",
                descriptionJa = "きれいだけど少し高いドレス。",
                price = 300, sellPrice = 150, maxOwned = 1,
            });
            def.items.Add(new VNShopDef.Item
            {
                id = "神秘挂坠", displayName = "神秘挂坠",
                displayNameEn = "Mysterious Pendant", displayNameJa = "神秘のペンダント",
                description = "只卖给有眼缘的客人。（魅力≥50 时上架）",
                descriptionEn = "Only sold to special customers. (Charm ≥ 50)",
                descriptionJa = "特別なお客様だけに。（魅力50以上）",
                price = 200, sellPrice = 100, maxOwned = 1,
                condition = "魅力>=50",
            });
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        /// <summary>示例日程方案：周日程（event plan id:周日程），四个行动与 WeekPlanDemo 剧本编号对应</summary>
        static VNPlanDef EnsurePlanDef()
        {
            string path = $"{PlansDir}/周日程.asset";
            var def = AssetDatabase.LoadAssetAtPath<VNPlanDef>(path);
            if (def != null) return def;

            def = ScriptableObject.CreateInstance<VNPlanDef>();
            def.planId = "周日程";
            def.title = "安排这一周";
            def.titleEn = "Plan Your Week";
            def.titleJa = "一週間の予定を組もう";
            def.actions.Add(new VNPlanDef.ActionDef
            {
                id = "打工", number = 1, displayName = "便利店打工",
                displayNameEn = "Part-time Job", displayNameJa = "コンビニバイト",
                gainText = "金钱+150　压力+8",
                gainTextEn = "Money +150 / Stress +8",
                gainTextJa = "所持金+150　ストレス+8",
            });
            def.actions.Add(new VNPlanDef.ActionDef
            {
                id = "学习", number = 2, displayName = "图书馆学习",
                displayNameEn = "Study", displayNameJa = "図書館で勉強",
                gainText = "智力+12　压力+6",
                gainTextEn = "Int +12 / Stress +6",
                gainTextJa = "知力+12　ストレス+6",
            });
            def.actions.Add(new VNPlanDef.ActionDef
            {
                id = "剑术训练", number = 3, displayName = "剑术训练",
                displayNameEn = "Sword Training", displayNameJa = "剣術訓練",
                gainText = "体力+15　压力+12",
                gainTextEn = "Vit +15 / Stress +12",
                gainTextJa = "体力+15　ストレス+12",
            });
            def.actions.Add(new VNPlanDef.ActionDef
            {
                id = "休息", number = 4, displayName = "好好休息",
                displayNameEn = "Rest", displayNameJa = "ゆっくり休む",
                gainText = "压力-20",
                gainTextEn = "Stress -20",
                gainTextJa = "ストレス-20",
            });
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        static VNQuestDef EnsureQuestDef()
        {
            string path = $"{QuestsDir}/告白大作战.asset";
            var def = AssetDatabase.LoadAssetAtPath<VNQuestDef>(path);
            if (def != null) return def;

            def = ScriptableObject.CreateInstance<VNQuestDef>();
            def.id = "告白大作战";
            def.title = "告白大作战";
            def.description = "在晚霞下把心意传达给小雪。";
            def.stages.Add("和小雪一起看晚霞");
            def.stages.Add("鼓起勇气说出心意");
            // 本地化演示文案（id 与 flag 名保持中文不动）
            def.titleEn = "Operation: Confession";
            def.descriptionEn = "Tell Koyuki how you feel under the sunset.";
            def.stagesEn.Add("Watch the sunset with Koyuki");
            def.stagesEn.Add("Gather your courage and confess");
            def.titleJa = "告白大作戦";
            def.descriptionJa = "夕焼けの下で、小雪に想いを伝えよう。";
            def.stagesJa.Add("小雪と一緒に夕焼けを見る");
            def.stagesJa.Add("勇気を出して想いを伝える");
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        static VNCharacterDef EnsureCharacterDef(string id, Color nameColor, Sprite sprite)
        {
            string path = $"{CharactersDir}/{id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(path);
            if (def != null) return def;

            def = ScriptableObject.CreateInstance<VNCharacterDef>();
            def.id = id;
            def.displayName = id;
            // 演示角色的名牌译名（正式角色请在 .asset 的 Inspector 里填）
            if (id.StartsWith("亚里沙")) { def.displayNameEn = "Arisa"; def.displayNameJa = "亜里沙"; }
            else if (id.StartsWith("小雪")) { def.displayNameEn = "Koyuki"; def.displayNameJa = "小雪"; }
            def.nameColor = nameColor;
            def.expressions.Add(new VNCharacterDef.Expression { name = "默认", sprite = sprite });
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        static TextAsset EnsureDemoScript()
        {
            EnsureFolder("Assets/Scenarios");
            if (!File.Exists(DemoScriptPath))
            {
                File.WriteAllText(DemoScriptPath, DemoScriptContent, System.Text.Encoding.UTF8);
                AssetDatabase.ImportAsset(DemoScriptPath);
            }
            else
            {
                Debug.Log($"[VNScript] {DemoScriptPath} 已存在，未覆盖你的修改。" +
                          "想获取最新演示剧本（含分支/选项/flag），删除该文件后重新生成场景即可。");
            }
            return AssetDatabase.LoadAssetAtPath<TextAsset>(DemoScriptPath);
        }

        const string DemoScriptContent =
@"# ============================================
# VN 剧本演示（P0+P1）— 直接编辑本文件后重新 Play 即可
# 语法速查：
#   bg <背景id> [transition:转场名]
#   show <角色> [at:left|center|right] [expr:表情] [with:出场预设]
#   hide <角色> [with:dissolve|fade]
#   emote <角色> <Surprise|Angry|Shy|Dejected|Recover|Nod|HeadShake>
#   角色 [表情]: 台词        /  旁白: 台词  /  : 无名牌旁白
#   wait <秒> | shake <light|medium|heavy> | sakura
#   camera <pushin|snapzoom|pan|dolly|reset> [参数] [focus:角色]
#   weather <Petals|Rain|Snow|Fireflies|None> | mood <Sunset|Night|...>
#   fx <godrays|dof|clouds|haze|shimmer|heartbeat|dutch|speedlines|meteor|skycloud> <on|off>
#   fx speedlines burst        漫画集中线一次性冲击（决断/惊愕瞬间）
#   fx shockwave [light|heavy] 全屏情绪水波：受击/震惊时整个画面荡一圈波纹
#   fx filmgrain on|off        胶片滤镜（颗粒+划痕）；mood Memory 回忆自动上
#   fx crt on|off              CRT 滤镜（扫描线，柔和）；mood Dream 梦境自动上
#   fx kenburns on|off         背景 Ken Burns 缓慢漂移（默认开启，off 可定格画面）
#   letterbox on|off [height:130] [time:0.7]   电影黑边；mood Memory 回忆自动上黑边
#   行尾加 @ = 不等待该演出完成（异步）
# ---- P1 分支 ----
#   label <名字> / jump <名字>
#   flag <名字> [数值|+1|-1]
#   if <条件> jump <名字>       条件不能有空格：勇气 / !勇气 / 好感度>=2
#   choice                       下一行起用 * 列出选项：
#   * 选项文本 [flag:名字+1] [-> 标签]   （无 -> = 顺序继续）
# ---- 事件（玩法接口）----
#   event <模块id> [key:value…]      调起 VNEventRegistry 登记的玩法模块
#   * 结果名 [flag:名字+1] [-> 标签]   按模块返回的结果分支（同 choice 写法）
#                                     整数结果会同时写入 flag「事件结果」
#   示例模块：qte（连打条 time:/target:/title:）
#            map（地图选地点 title:/bg:，地点选中自动 flag 去过_<地点>+1）
# ---- 任务 ----
#   quest start|stage|done|fail <id> [阶段]   状态存 flag「任务_<id>」，J 键看日志
# ============================================

bg bg1
mood Sunset
fx godrays on
fx skycloud on
weather Petals

show 亚里沙 at:left with:DissolveGlow
wait 0.4
show 小雪 at:right with:FadeSlideUp

quest start 告白大作战
亚里沙: 今天的晚霞真漂亮啊……整片天空都烧起来了一样。
小雪: 是啊。你看，连云的边缘都镶上了金色。

camera pushin 1.05 5 focus:亚里沙 @
亚里沙: 那个……小雪。
emote 小雪 Surprise
小雪: 怎、怎么了？突然这么严肃。

fx heartbeat on
quest stage 告白大作战 2
亚里沙: 我一直……有件事想告诉你。

choice
* 鼓起勇气说出来 -> 告白线
* 还是算了…… -> 退缩线

label 告白线
camera snapzoom 1.1 focus:亚里沙 @
event qte time:3 target:12 title:鼓起勇气连打！
* success flag:好感度+2 -> 告白成功
* fail -> 退缩线

label 告白成功
sakura
亚里沙: 小雪……我喜欢你！从很久以前开始就是！
emote 小雪 Surprise
wait 0.5
emote 小雪 Shy
小雪: ……笨蛋。怎么突然、说这种话啦。
quest done 告白大作战
jump 夜晚

label 退缩线
fx heartbeat off
emote 亚里沙 Dejected
亚里沙: ……没、没什么！忘了吧！
emote 小雪 HeadShake
小雪: 什么嘛，真是的。
quest fail 告白大作战
emote 亚里沙 Recover
jump 夜晚

label 夜晚
fx heartbeat off
camera reset @
bg bg2 transition:Eyelid
mood Night
weather Fireflies
fx godrays off
fx meteor on
旁白: ——那天夜里，萤火虫漫天飞舞，偶尔有流星划过。

event map title:夜晚去哪里走走？
* 教室 -> 教室夜话
* 图书馆 -> 图书馆夜话
* 天台 -> 天台夜话

label 教室夜话
: （夜晚的教室安安静静，只有月光落在课桌上。）
jump 结算

label 图书馆夜话
: （月光洒在书架之间，白天的喧嚣仿佛很遥远。）
jump 结算

label 天台夜话
: （晚风拂面，萤火虫绕着两人打转。）
小雪: 原来你也会来天台啊。……陪我看一会儿星星吧。
jump 结算

label 结算
if 好感度>=2 jump 好结局
亚里沙: （总有一天，我一定会说出口的。）
jump 落幕

label 好结局
亚里沙: （今天……是最棒的一天。）
小雪: （明天也想……和她一起看晚霞。）

label 落幕
hide 小雪 with:fade
hide 亚里沙 with:dissolve
: 第一章　完
";

        static TextMeshProUGUI CreateHintText(Transform canvasParent, float height)
        {
            var hintGo = new GameObject("HintText",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            var hintRect = (RectTransform)hintGo.transform;
            hintRect.SetParent(canvasParent, false);
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 18f);
            hintRect.sizeDelta = new Vector2(-60f, height);
            var hint = hintGo.GetComponent<TextMeshProUGUI>();
            // 编辑期创建、随场景保存的 TMP 文字必须引用持久化字体资产，
            // 不能用 VNFont 运行时动态创建的临时资产（保存场景后会变 Missing）
            hint.font = VNFontAssetBuilder.EnsureFontAsset();
            hint.fontSize = 26;
            hint.alignment = TextAlignmentOptions.Bottom;
            hint.color = new Color(1f, 1f, 1f, 0.85f);
            hint.richText = true;
            hint.raycastTarget = false;
            return hint;
        }

        // ==================================================================
        // 通用辅助（与旧版一致）
        // ==================================================================

        static (Sprite character, Sprite character2, Sprite background,
                System.Collections.Generic.List<Sprite> all) PrepareSprites()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Assets" });
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                             .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
                             .OrderBy(p => p)
                             .ToList();
            var all = new System.Collections.Generic.List<Sprite>();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[VNEffects] Assets/Assets 下没有找到图片，场景将不含背景/立绘。");
                return (null, null, null, all);
            }

            foreach (var p in paths) EnsureSpriteImport(p);

            // 文件名带 "solo" 的前两张当立绘 A/B，其余第一张当背景
            var soloPaths = paths.Where(p => p.ToLower().Contains("solo")).ToList();
            string charPath = soloPaths.Count > 0 ? soloPaths[0] : paths[0];
            string charPath2 = soloPaths.Count > 1 ? soloPaths[1] : null;
            string bgPath = paths.FirstOrDefault(p => p != charPath && p != charPath2) ?? charPath;

            // 除立绘外的"大图"才作为背景（过滤掉按钮/对话框等小 UI 素材）
            foreach (var p in paths)
            {
                if (p == charPath || p == charPath2) continue;
                var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (s != null && s.rect.width >= 900f && s.rect.height >= 600f)
                    all.Add(s);
            }

            return (AssetDatabase.LoadAssetAtPath<Sprite>(charPath),
                    charPath2 != null ? AssetDatabase.LoadAssetAtPath<Sprite>(charPath2) : null,
                    AssetDatabase.LoadAssetAtPath<Sprite>(bgPath),
                    all);
        }

        /// <summary>把 Assets/CG 下的全部图片灌入 VNStage.cgLibrary（文件名 = 剧本 cg id）</summary>
        static void FillCgLibrary(VNStage stage)
        {
            EnsureFolder("Assets/CG");
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/CG" });
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                             .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
                             .OrderBy(p => p)
                             .ToList();
            if (paths.Count == 0)
            {
                Debug.Log("[VNEffects] Assets/CG 目前是空的：放入 CG 图片后重新生成场景，" +
                          "即可用剧本 cg 命令调用（文件名 = id）");
                return;
            }

            foreach (var p in paths)
            {
                EnsureSpriteImport(p);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                if (sprite == null) continue;
                stage.cgLibrary.Add(new VNStage.CgEntry
                    { id = Path.GetFileNameWithoutExtension(p), sprite = sprite });
            }
            Debug.Log($"[VNEffects] CG 库已灌入 {stage.cgLibrary.Count} 张（来自 Assets/CG）");
        }

        static void EnsureSpriteImport(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) return;
            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            if (settings.spriteMeshType != SpriteMeshType.FullRect)
            {
                settings.spriteMeshType = SpriteMeshType.FullRect; // 全矩形网格，溶解/扫光 UV 才均匀
                importer.SetTextureSettings(settings);
                dirty = true;
            }

            if (dirty) importer.SaveAndReimport();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static Material EnsureMaterial(string path, string shaderName)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[VNEffects] 找不到 Shader \"{shaderName}\"，请等编译完成后重试。");
                return null;
            }
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static VolumeProfile EnsureVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var bloom = profile.Add<Bloom>(false);
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.0f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 1.4f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.7f;
            AssetDatabase.AddObjectToAsset(bloom, profile);

            var vignette = profile.Add<Vignette>(false);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.22f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.45f;
            AssetDatabase.AddObjectToAsset(vignette, profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static RectTransform CreateStretchRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        static (VNEntranceAnimator anim, VNImageEffectController fx) CreateCharacter(
            string name, Sprite sprite, Transform parent, Vector2 pos, float height,
            Material imageMat, Material additiveMat)
        {
            var go = CreateUIImage(name, parent, sprite);
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            float aspect = sprite.rect.width / sprite.rect.height;
            rect.sizeDelta = new Vector2(height * aspect, height);
            rect.anchoredPosition = pos;

            var fx = go.AddComponent<VNImageEffectController>();
            AssignSourceMaterial(fx, imageMat);

            var backdrop = go.AddComponent<VNGlowBackdrop>();
            AssignSourceMaterial(backdrop, additiveMat);

            var anim = go.AddComponent<VNEntranceAnimator>();
            go.AddComponent<VNFootShadow>(); // 脚下椭圆软影，自动同步悬浮
            return (anim, fx);
        }

        static GameObject CreateUIImage(string name, Transform parent, Sprite sprite)
        {
            var go = new GameObject(name,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return go;
        }

        static VNAmbientParticles CreateAmbient(string name, VNAmbientParticles.Preset preset,
            Color tint, int sortingOrder, Material mat)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.position = new Vector3(0f, 0f, -1f);
            var amb = go.AddComponent<VNAmbientParticles>();
            amb.preset = preset;
            amb.tint = tint;
            amb.sortingOrder = sortingOrder;
            AssignSourceMaterial(amb, mat);
            return amb;
        }

        /// <summary>给组件的私有 [SerializeField] sourceMaterial 字段赋材质资产</summary>
        static void AssignSourceMaterial(Component comp, Material mat)
        {
            if (mat == null) return;
            var so = new SerializedObject(comp);
            var prop = so.FindProperty("sourceMaterial");
            if (prop != null)
            {
                prop.objectReferenceValue = mat;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }
}
