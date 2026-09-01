using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把擦雾模块**增量装进当前场景**，不重建场景（同 VNQuizInstaller 的做法）。
    ///
    /// 做三件事：
    ///   ① 场景的 VNEventRegistry 下补一个**禁用的** FogWipeTemplate（必须带 RectTransform）
    ///   ② 确保示例定义资产存在，并把工程里全部 VNFogWipeDef 填进模板
    ///   ③ 把定义登记进 VNGameConfig（重建场景也不会丢）
    /// 重复执行安全：已经装过就只刷新列表。
    /// </summary>
    public static class VNFogWipeInstaller
    {
        const string ModuleId = "wipefog";
        const string TemplateName = "FogWipeTemplate";
        const string FogWipesDir = "Assets/VNEffects/FogWipes";

        [MenuItem("Tools/VN Effects/场景装机 Install To Scene/擦雾 Fog Wipe", priority = 145)]
        public static void Install()
        {
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("VN Fog Wipe",
                    "当前场景里找不到 VNEventRegistry。\n\n" +
                    "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                    "或用 Tools → VN Effects → 演示场景 Demo Scenes → " +
                    "重建剧本演示场景 Create Script Demo Scene 造一个新场景。", "OK");
                return;
            }

            var report = new List<string>();

            // ① 定义资产（示例不存在才造；已有的原样保留）
            VNEffectsDemoSetup.EnsureFolder(FogWipesDir);
            var demo = EnsureDemoDef();
            var all = AssetDatabase.FindAssets("t:VNFogWipeDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<VNFogWipeDef>)
                .Where(a => a != null)
                .ToList();
            if (all.Count == 0 && demo != null) all.Add(demo);
            report.Add($"擦雾定义资产 ×{all.Count}（{FogWipesDir}）");

            // ② 场景里的模板
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNFogWipeModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install fog wipe module");

                // ★ 必须带 RectTransform：模块 BuildUi 里直接 (RectTransform)transform，
                //   普通 Transform 会在运行时抛 InvalidCastException。
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNFogWipeModule>();
                go.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install fog wipe module");

                if (entry == null)
                    registry.modules.Add(new VNEventRegistry.Entry
                    {
                        id = ModuleId,
                        template = module,
                    });
                else entry.template = module;

                report.Add($"注册表新增模块「{ModuleId}」→ {TemplateName}（已禁用）");
            }
            else
            {
                Undo.RecordObject(module, "Refresh fog wipe module");
                report.Add($"模块「{ModuleId}」已存在，只刷新定义列表");
            }

            module.fogWipes = new List<VNFogWipeDef>(all);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ③ 登记进 VNGameConfig（覆盖语义：填了就覆盖模板上的列表，重建场景不丢）
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register fog wipes");
                config.fogWipes = new List<VNFogWipeDef>(all);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add($"VNGameConfig 擦雾库 ×{config.fogWipes.Count}");
            }

            // 雾层 shader 缺失的话运行时只会打一条 error，装机时先提醒更省事
            if (Shader.Find("VN/FogWipe") == null)
                report.Add("⚠ 找不到 shader「VN/FogWipe」" +
                           "（检查 Assets/Art/Shaders/VNFogWipe.shader 是否已导入）");

            string summary = string.Join("\n", report);
            Debug.Log($"[VNFogWipe] 已装入当前场景：\n{summary}");
            EditorUtility.DisplayDialog("VN Fog Wipe",
                $"擦雾模块已装进当前场景：\n\n{summary}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n" +
                "剧本里就可以写：event wipefog id:浴室镜面 time:60", "OK");
            Selection.activeObject = module.gameObject;
            EditorGUIUtility.PingObject(module.gameObject);
        }

        /// <summary>
        /// 示例定义：参数用的是实施计划里算出来的那组默认值
        /// （笔刷 170px / 回雾 3%/秒 / 60 秒 / 90-65 门槛 ≈ 认真擦 50 秒到 80%）。
        /// 已存在就原样返回，绝不覆盖用户调过的数。
        /// </summary>
        static VNFogWipeDef EnsureDemoDef()
        {
            const string path = FogWipesDir + "/浴室镜面.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VNFogWipeDef>(path);
            if (existing != null) return existing;

            var def = ScriptableObject.CreateInstance<VNFogWipeDef>();
            def.fogWipeId = "浴室镜面";
            def.flagPrefix = "擦雾";

            def.stages = new List<VNFogWipeDef.Stage>
            {
                new VNFogWipeDef.Stage
                {
                    note = "刚擦开一点",
                    threshold = 30f,
                    lines = new List<VNFogWipeDef.Line>
                    {
                        new VNFogWipeDef.Line
                        {
                            text = "……喂，你在外面干什么啦。",
                            textEn = "...Hey, what are you doing out there?",
                            textJa = "……ちょっと、そこで何してるの。",
                        },
                        new VNFogWipeDef.Line
                        {
                            text = "镜子擦得那么起劲，是想看什么？",
                            textEn = "Wiping the mirror so eagerly. What are you hoping to see?",
                            textJa = "そんなに鏡を拭いて、何が見たいわけ？",
                        },
                    },
                },
                new VNFogWipeDef.Stage
                {
                    note = "看得见轮廓了",
                    threshold = 60f,
                    lines = new List<VNFogWipeDef.Line>
                    {
                        new VNFogWipeDef.Line
                        {
                            text = "…别、别看这边啦。",
                            textEn = "D-don't look over here...",
                            textJa = "…こ、こっち見ないでよ。",
                        },
                    },
                },
                new VNFogWipeDef.Stage
                {
                    note = "快擦干净了",
                    threshold = 88f,
                    lines = new List<VNFogWipeDef.Line>
                    {
                        new VNFogWipeDef.Line
                        {
                            text = "……看清楚了？笨蛋。",
                            textEn = "...Got a good look? Idiot.",
                            textJa = "……よく見えた？ばか。",
                        },
                    },
                },
            };

            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            return def;
        }
    }
}
