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

            // ---------- 1. 找到并正确导入两张演示图 ----------
            var (charSprite, bgSprite) = PrepareSprites();

            // ---------- 2. 材质资产 ----------
            EnsureFolder("Assets/VNEffects");
            EnsureFolder(MaterialsDir);
            var imageMat = EnsureMaterial($"{MaterialsDir}/VNImageEffect.mat", "VN/ImageEffect");
            var additiveMat = EnsureMaterial($"{MaterialsDir}/VNAdditive.mat", "VN/Additive");

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

            // ---------- 6. 背景图 ----------
            VNImageEffectController bgFx = null;
            if (bgSprite != null)
            {
                var bgGo = CreateUIImage("Background", canvasGo.transform, bgSprite);
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
            godRaysRect.SetParent(canvasGo.transform, false);
            godRaysRect.anchorMin = Vector2.zero;
            godRaysRect.anchorMax = Vector2.one;
            godRaysRect.offsetMin = Vector2.zero;
            godRaysRect.offsetMax = Vector2.zero;
            var godRays = godRaysGo.AddComponent<VNGodRays>();
            AssignSourceMaterial(godRays, additiveMat);

            // ---------- 7. 立绘 ----------
            VNEntranceAnimator charAnim = null;
            VNImageEffectController charFx = null;
            if (charSprite != null)
            {
                var charGo = CreateUIImage("Character", canvasGo.transform, charSprite);
                var img = charGo.GetComponent<Image>();
                img.preserveAspect = true;
                var charRect = (RectTransform)charGo.transform;
                charRect.anchorMin = charRect.anchorMax = new Vector2(0.5f, 0.5f);
                charRect.pivot = new Vector2(0.5f, 0.5f);
                float h = 980f;
                float aspect = charSprite.rect.width / charSprite.rect.height;
                charRect.sizeDelta = new Vector2(h * aspect, h);
                charRect.anchoredPosition = new Vector2(0f, -40f);

                charFx = charGo.AddComponent<VNImageEffectController>();
                AssignSourceMaterial(charFx, imageMat);

                var backdrop = charGo.AddComponent<VNGlowBackdrop>();
                AssignSourceMaterial(backdrop, additiveMat);

                charAnim = charGo.AddComponent<VNEntranceAnimator>();
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
            hintRect.sizeDelta = new Vector2(-60f, 150f);
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

            // ---------- 11. 保存 ----------
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"[VNEffects] 演示场景已生成并保存到 {ScenePath}。直接点 Play 体验：" +
                      "1~5 出场演出 | Space 重播 | X 退场 | S 扫光 | B 星光爆发 | P 粒子 | H 彩虹 | " +
                      "G 光束 | V 聚焦渐晕 | E 情绪泛光 | W 天气循环。");
        }

        // ------------------------------------------------------------------

        static (Sprite character, Sprite background) PrepareSprites()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Assets" });
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                             .Where(p => p.EndsWith(".png") || p.EndsWith(".jpg"))
                             .OrderBy(p => p)
                             .ToList();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[VNEffects] Assets/Assets 下没有找到图片，场景将不含背景/立绘。");
                return (null, null);
            }

            foreach (var p in paths) EnsureSpriteImport(p);

            // 文件名带 "solo" 的当立绘，其余第一张当背景
            string charPath = paths.FirstOrDefault(p => p.ToLower().Contains("solo")) ?? paths[0];
            string bgPath = paths.FirstOrDefault(p => p != charPath) ?? charPath;

            return (AssetDatabase.LoadAssetAtPath<Sprite>(charPath),
                    AssetDatabase.LoadAssetAtPath<Sprite>(bgPath));
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
