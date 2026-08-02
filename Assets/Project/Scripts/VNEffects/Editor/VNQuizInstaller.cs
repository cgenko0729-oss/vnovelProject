using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把限时问答模块**增量装进当前场景**，不重建场景。
    ///
    /// 【为什么需要它】
    /// Tools → VN Effects → Create Script Demo Scene 会 NewScene(EmptyScene) 从零重造，
    /// 手工整理过的 Hierarchy 会全部丢失。加了新事件模块以后只为了"让注册表多一条"
    /// 而重建整个场景，代价太大——这里只做三件事：
    ///   ① 在场景的 VNEventRegistry 下补一个**禁用的** QuizTemplate（带 RectTransform）
    ///   ② 确保示例题库资产存在，并把工程里全部 VNQuizDef 填进模板
    ///   ③ 把题库登记进 VNGameConfig（重建场景也不会丢）
    /// 重复执行安全：已经装过就只刷新题库列表。
    /// </summary>
    public static class VNQuizInstaller
    {
        const string ModuleId = "quiz";
        const string TemplateName = "QuizTemplate";

        [MenuItem("Tools/VN Effects/Install Quiz Module To Scene", priority = 210)]
        public static void Install()
        {
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("VN Quiz",
                    "当前场景里找不到 VNEventRegistry。\n\n" +
                    "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                    "或用 Tools → VN Effects → Create Script Demo Scene 造一个新场景。", "OK");
                return;
            }

            var report = new List<string>();

            // ① 题库资产（示例题库不存在才造；已有的原样保留）
            VNEffectsDemoSetup.EnsureFolder(VNEffectsDemoSetup.QuizzesDir);
            var demoQuiz = VNEffectsDemoSetup.EnsureQuizDef();
            var allQuizzes = AssetDatabase.FindAssets("t:VNQuizDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<VNQuizDef>)
                .Where(a => a != null)
                .ToList();
            if (allQuizzes.Count == 0 && demoQuiz != null) allQuizzes.Add(demoQuiz);
            report.Add($"题库资产 ×{allQuizzes.Count}（{VNEffectsDemoSetup.QuizzesDir}）");

            // ② 场景里的模板（已存在就复用，只刷新题库列表）
            var entry = registry.modules.FirstOrDefault(
                e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNQuizModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install quiz module");

                // ★ 必须带 RectTransform：模块 BuildUi 里直接 (RectTransform)transform，
                //   普通 Transform 会在运行时抛 InvalidCastException。
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNQuizModule>();
                go.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install quiz module");

                if (entry == null)
                {
                    entry = new VNEventRegistry.Entry { id = ModuleId, template = module };
                    registry.modules.Add(entry);
                }
                else entry.template = module;

                report.Add($"注册表新增模块「{ModuleId}」→ {TemplateName}（已禁用）");
            }
            else
            {
                Undo.RecordObject(module, "Refresh quiz module");
                report.Add($"模块「{ModuleId}」已存在，只刷新题库列表");
            }

            module.quizzes = new List<VNQuizDef>(allQuizzes);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ③ 登记进 VNGameConfig（覆盖语义：填了就覆盖模板上的列表，重建场景不丢）
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register quizzes");
                config.quizzes = new List<VNQuizDef>(allQuizzes);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add($"VNGameConfig 题库列表 ×{config.quizzes.Count}");
            }

            string summary = string.Join("\n", report);
            Debug.Log($"[VNQuiz] 已装入当前场景：\n{summary}");
            EditorUtility.DisplayDialog("VN Quiz",
                $"限时问答模块已装进当前场景：\n\n{summary}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n" +
                "剧本里就可以写：event quiz id:社团常识 count:3 time:15 pass:2", "OK");
            Selection.activeObject = module.gameObject;
            EditorGUIUtility.PingObject(module.gameObject);
        }
    }
}
