using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把羽毛球模块**增量装进当前场景**，不重建场景。
    ///
    /// 【为什么需要它】
    /// Tools → VN Effects → Create Script Demo Scene 会从零重造场景，
    /// 手工整理过的 Hierarchy 会全部丢失。加一个事件模块不值这个代价——
    /// 这里只做三件事：
    ///   ① 在场景的 VNEventRegistry 下补一个**禁用的** BadmintonTemplate（必须带 RectTransform）
    ///   ② 把工程里全部 VNBadmintonDef 填进模板
    ///   ③ 把对手库登记进 VNGameConfig（重建场景也不会丢）
    /// 重复执行安全：已经装过就只刷新对手列表。
    /// </summary>
    public static class VNBadmintonInstaller
    {
        const string ModuleId = "badminton";
        const string TemplateName = "BadmintonTemplate";
        const string DefsDir = "Assets/VNEffects/Badminton";

        [MenuItem("Tools/VN Effects/Install Badminton Module To Scene", priority = 211)]
        public static void Install()
        {
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("VN Badminton",
                    "当前场景里找不到 VNEventRegistry。\n\n" +
                    "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                    "或用 Tools → VN Effects → Create Script Demo Scene 造一个新场景。", "OK");
                return;
            }

            var report = new List<string>();

            // ① 对手资产
            EnsureFolder(DefsDir);
            var allDefs = AssetDatabase.FindAssets("t:VNBadmintonDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<VNBadmintonDef>)
                .Where(a => a != null)
                .ToList();
            report.Add($"对手 / 难度资产 ×{allDefs.Count}（{DefsDir}）");
            if (allDefs.Count == 0)
                report.Add("⚠ 一个 VNBadmintonDef 都没有——剧本里不写 id: 也能跑（用模板兜底参数），" +
                           "但没有难度差异与台词");

            // ② 场景里的模板（已存在就复用，只刷新对手列表）
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNBadmintonModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install badminton module");

                // ★ 必须带 RectTransform：模块搭 UI 时直接 (RectTransform)transform，
                //   普通 Transform 会在运行时抛 InvalidCastException。
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNBadmintonModule>();
                go.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install badminton module");

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
                Undo.RecordObject(module, "Refresh badminton module");
                report.Add($"模块「{ModuleId}」已存在，只刷新对手列表");
            }

            module.defs = new List<VNBadmintonDef>(allDefs);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ③ 登记进 VNGameConfig（覆盖语义：填了就覆盖模板上的列表，重建场景不丢）
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register badminton defs");
                config.badmintons = new List<VNBadmintonDef>(allDefs);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add($"VNGameConfig 对手库 ×{config.badmintons.Count}");
            }

            string summary = string.Join("\n", report);
            Debug.Log($"[VNBadminton] 已装入当前场景：\n{summary}");
            EditorUtility.DisplayDialog("VN Badminton",
                $"羽毛球模块已装进当前场景：\n\n{summary}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n" +
                "剧本里就可以写：event badminton vs:小雪 id:校队 target:5", "OK");
            Selection.activeObject = module.gameObject;
            EditorGUIUtility.PingObject(module.gameObject);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
