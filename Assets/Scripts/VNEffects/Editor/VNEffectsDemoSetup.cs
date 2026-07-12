using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 一键生成视觉小说特效演示场景：
    /// 菜单 Tools → VN Effects → Create Demo Scene
    /// 自动完成：贴图导入设置 → 材质资产 → Bloom Volume → 相机 →
    /// Canvas + 背景/立绘 + 特效组件 → 悬浮粒子 → 演示驱动 → 保存场景。
    /// </summary>
    public static class VNEffectsDemoSetup
    {
        const string MaterialsDir = "Assets/VNEffects/Materials";
        const string ScenePath = "Assets/Scenes/VNEffectsDemo.unity";
        const string ProfilePath = "Assets/VNEffects/VNEffectsVolumeProfile.asset";

        [MenuItem("Tools/VN Effects/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // ---------- 1. 找到并正确导入演示图 ----------
            var (charSprite, charSprite2, bgSprite, allSprites) = PrepareSprites();

            // ---------- 2. 材质资产 ----------
            EnsureFolder("Assets/VNEffects");
            EnsureFolder(MaterialsDir);
            var imageMat = EnsureMaterial($"{MaterialsDir}/VNImageEffect.mat", "VN/ImageEffect");
            var additiveMat = EnsureMaterial($"{MaterialsDir}/VNAdditive.mat", "VN/Additive");
            var transitionMat = EnsureMaterial($"{MaterialsDir}/VNScreenTransition.mat", "VN/ScreenTransition");

            // ---------- 3. 后处理 Volume Profile（Bloom + Vignette）----------
            var profile = EnsureVolumeProfile();

            // ---------- 4. 新场景 ----------
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 相机：正交、HDR 后处理开
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.03f, 0.06f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;

            // 全局 Volume
            var volGo = new GameObject("Global Volume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // ---------- 5. Canvas（Screen Space - Camera，粒子才能叠在 UI 前后）----------
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

            // ---------- 5.5 画面容器层级 ----------
            // SceneRoot(屏幕震动) > TiltRoot(荷兰角) > LayerBack/Mid/Front(视差三层)
            var sceneRoot = CreateStretchRect("SceneRoot", canvasGo.transform);
            var tiltRoot = CreateStretchRect("TiltRoot", sceneRoot);
            var layerBack = CreateStretchRect("LayerBack", tiltRoot);
            var layerMid = CreateStretchRect("LayerMid", tiltRoot);
            var layerFront = CreateStretchRect("LayerFront", tiltRoot);

            // ---------- 6. 背景图 ----------
            VNImageEffectController bgFx = null;
            if (bgSprite != null)
            {
                var bgGo = CreateUIImage("Background", layerBack, bgSprite);
                var bgRect = (RectTransform)bgGo.transform;
                bgRect.anchorMin = Vector2.zero;
                bgRect.anchorMax = Vector2.one;
                // 四边溢出 60px，给 Ken Burns 缓慢缩放留余量
                bgRect.offsetMin = new Vector2(-60f, -60f);
                bgRect.offsetMax = new Vector2(60f, 60f);
                bgFx = bgGo.AddComponent<VNImageEffectController>();
                AssignSourceMaterial(bgFx, imageMat);
            }

            // ---------- 6.5 God Rays 斜射光束（渲染在背景之后、立绘之前）----------
            var godRaysGo = new GameObject("GodRays", typeof(RectTransform));
            var godRaysRect = (RectTransform)godRaysGo.transform;
            godRaysRect.SetParent(layerMid, false);
            godRaysRect.anchorMin = Vector2.zero;
            godRaysRect.anchorMax = Vector2.one;
            godRaysRect.offsetMin = Vector2.zero;
            godRaysRect.offsetMax = Vector2.zero;
            var godRays = godRaysGo.AddComponent<VNGodRays>();
            AssignSourceMaterial(godRays, additiveMat);

            // ---------- 7. 立绘（有两张 solo 图时创建双角色）----------
            VNEntranceAnimator charAnim = null, charAnimB = null;
            VNImageEffectController charFx = null, charFxB = null;
            VNCharacterEmotes charEmotes = null;
            bool twoChars = charSprite != null && charSprite2 != null;
            float charHeight = twoChars ? 880f : 980f;

            if (charSprite != null)
            {
                var pos = twoChars ? new Vector2(-380f, -60f) : new Vector2(0f, -40f);
                var (anim, fx) = CreateCharacter("Character", charSprite, layerFront,
                    pos, charHeight, imageMat, additiveMat);
                charAnim = anim;
                charFx = fx;
                charEmotes = fx.gameObject.AddComponent<VNCharacterEmotes>();
            }
            if (twoChars)
            {
                var (anim, fx) = CreateCharacter("CharacterB", charSprite2, layerFront,
                    new Vector2(380f, -60f), charHeight, imageMat, additiveMat);
                charAnimB = anim;
                charFxB = fx;
            }

            // ---------- 8. 悬浮氛围粒子（尘埃 + 星光 + 光斑）----------
            var particles = new[]
            {
                CreateAmbient("Ambient_Dust", VNAmbientParticles.Preset.Dust,
                    new Color(1f, 0.97f, 0.88f), 11, additiveMat),
                CreateAmbient("Ambient_Sparkles", VNAmbientParticles.Preset.Sparkles,
                    new Color(1f, 0.93f, 0.65f), 12, additiveMat),
                CreateAmbient("Ambient_Orbs", VNAmbientParticles.Preset.Orbs,
                    new Color(0.75f, 0.85f, 1f), 10, additiveMat),
            };

            // ---------- 9. 操作提示文字 ----------
            Text hint = null;
            var hintGo = new GameObject("HintText",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var hintRect = (RectTransform)hintGo.transform;
            hintRect.SetParent(canvasGo.transform, false);
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 0f);
            hintRect.pivot = new Vector2(0.5f, 0f);
            hintRect.anchoredPosition = new Vector2(0f, 18f);
            hintRect.sizeDelta = new Vector2(-60f, 250f);
            hint = hintGo.GetComponent<Text>();
            hint.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hint.fontSize = 26;
            hint.alignment = TextAnchor.LowerCenter;
            hint.color = new Color(1f, 1f, 1f, 0.85f);
            hint.supportRichText = true;
            hint.raycastTarget = false;

            // ---------- 9.5 边缘情绪泛光（Canvas 最后一个子物体，嵌套 Canvas 排序最高）----------
            var edgeGlowGo = new GameObject("EdgeGlow", typeof(RectTransform));
            edgeGlowGo.transform.SetParent(canvasGo.transform, false);
            var edgeGlow = edgeGlowGo.AddComponent<VNEdgeGlow>();
            AssignSourceMaterial(edgeGlow, additiveMat);

            // ---------- 9.6 聚焦渐晕（挂在 Volume 上）----------
            var vignetteFocus = volGo.AddComponent<VNVignetteFocus>();
            vignetteFocus.volume = vol;

            // ---------- 9.7 天气控制器 ----------
            var weatherGo = new GameObject("WeatherController");
            var weatherCtrl = weatherGo.AddComponent<VNWeatherController>();
            weatherCtrl.additiveMaterial = additiveMat;
            weatherCtrl.moodTargets = (bgFx != null && charFx != null)
                ? new[] { bgFx, charFx }
                : (bgFx != null ? new[] { bgFx } : new VNImageEffectController[0]);

            // ---------- 9.8 色调情绪预设系统 ----------
            var moodGo = new GameObject("MoodGrading");
            var moodGrading = moodGo.AddComponent<VNMoodGrading>();

            // ---------- 9.9 全屏转场（Canvas 最后子物体，嵌套 Canvas 排序 100 盖住一切）----------
            var transitionGo = new GameObject("ScreenTransition", typeof(RectTransform));
            transitionGo.transform.SetParent(canvasGo.transform, false);
            var screenTransition = transitionGo.AddComponent<VNScreenTransition>();
            AssignSourceMaterial(screenTransition, transitionMat);

            // ---------- 9.12 屏幕震动（震 SceneRoot，UI 保持稳定）----------
            var shakeGo = new GameObject("ScreenShake");
            var screenShake = shakeGo.AddComponent<VNScreenShake>();
            screenShake.target = sceneRoot;

            // ---------- 9.15 多层视差（远景/中景/近景强度递增）----------
            var parallaxGo = new GameObject("Parallax");
            var parallax = parallaxGo.AddComponent<VNParallax>();
            parallax.layers.Add(new VNParallax.Layer { rect = layerBack, strength = 8f });
            parallax.layers.Add(new VNParallax.Layer { rect = layerMid, strength = 13f });
            parallax.layers.Add(new VNParallax.Layer { rect = layerFront, strength = 19f });

            // ---------- 9.16 荷兰角（作用于 TiltRoot）----------
            var dutchGo = new GameObject("DutchAngle");
            var dutchAngle = dutchGo.AddComponent<VNDutchAngle>();
            dutchAngle.target = tiltRoot;

            // ---------- 9.17 点击涟漪 ----------
            var rippleGo = new GameObject("ClickRipple", typeof(ParticleSystem));
            var clickRipple = rippleGo.AddComponent<VNClickRipple>();
            AssignSourceMaterial(clickRipple, additiveMat);

            // ---------- 9.13 说话者高亮 ----------
            var speakerGo = new GameObject("SpeakerHighlight");
            var speakerHighlight = speakerGo.AddComponent<VNSpeakerHighlight>();
            if (charFx != null) speakerHighlight.characters.Add(charFx);
            if (charFxB != null) speakerHighlight.characters.Add(charFxB);

            // ---------- 9.14 对话框（底部，排序 40）----------
            var dialogueGo = new GameObject("DialogueBox", typeof(RectTransform));
            var dialogueRect = (RectTransform)dialogueGo.transform;
            dialogueRect.SetParent(canvasGo.transform, false);
            dialogueRect.anchorMin = new Vector2(0.05f, 0f);
            dialogueRect.anchorMax = new Vector2(0.95f, 0f);
            dialogueRect.pivot = new Vector2(0.5f, 0f);
            dialogueRect.anchoredPosition = new Vector2(0f, 28f);
            dialogueRect.sizeDelta = new Vector2(0f, 230f);
            var dialogueBox = dialogueGo.AddComponent<VNDialogueBox>();

            // ---------- 9.10 鼠标轨迹星尘 ----------
            var stardustGo = new GameObject("MouseStardust", typeof(ParticleSystem));
            var stardust = stardustGo.AddComponent<VNMouseStardust>();
            AssignSourceMaterial(stardust, additiveMat);

            // ---------- 9.11 热浪/空气扭曲（默认关闭，Z 键开启）----------
            var hazeGo = new GameObject("HeatHaze");
            var heatHaze = hazeGo.AddComponent<VNHeatHaze>();
            heatHaze.targets = bgFx != null
                ? new[] { bgFx } : new VNImageEffectController[0];
            heatHaze.additiveMaterial = additiveMat;

            // ---------- 10. 演示驱动 ----------
            var demoGo = new GameObject("VNEffectsDemo");
            var demo = demoGo.AddComponent<VNEffectsDemo>();
            demo.character = charAnim;
            demo.characterFx = charFx;
            demo.backgroundFx = bgFx;
            demo.ambientParticles = particles;
            demo.hintText = hint;
            demo.godRays = godRays;
            demo.vignetteFocus = vignetteFocus;
            demo.edgeGlow = edgeGlow;
            demo.weather = weatherCtrl;
            demo.mood = moodGrading;
            demo.transition = screenTransition;
            demo.emotes = charEmotes;
            demo.backgroundVariants = allSprites.ToArray();
            demo.stardust = stardust;
            demo.heatHaze = heatHaze;
            demo.characterB = charAnimB;
            demo.characterBFx = charFxB;
            demo.speakerHighlight = speakerHighlight;
            demo.screenShake = screenShake;
            demo.dialogue = dialogueBox;
            demo.parallax = parallax;
            demo.dutchAngle = dutchAngle;

            // ---------- 11. 保存 ----------
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"[VNEffects] 演示场景已生成并保存到 {ScenePath}。直接点 Play 体验：" +
                      "1~5 出场演出 | Space 重播 | X 退场 | S 扫光 | B 星光爆发 | P 粒子 | H 彩虹 | " +
                      "G 光束 | V 聚焦渐晕 | E 情绪泛光 | W 天气 | M 色调情绪 | T 转场换背景 | " +
                      "6~0/N 情绪动作 | Y 说话者高亮 | U 水面波光 | J/K/L 分级震动 | Enter 对话框演示。");
        }

        // ------------------------------------------------------------------

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

            // 除立绘外的"大图"才作为转场轮换背景（过滤掉按钮/对话框等小 UI 素材）
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
