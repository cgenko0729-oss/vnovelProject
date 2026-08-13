using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把 AI 自由聊天模块**增量装进当前场景**，不重建场景。
    ///
    /// 【为什么需要它】
    /// Create Script Demo Scene 会 NewScene(EmptyScene) 从零重造，手工整理过的
    /// Hierarchy 全丢。只为了让注册表多一条而重建整个场景代价太大——这里只做三件事：
    ///   ① 在 VNEventRegistry 下补一个**禁用的** AiTalkTemplate（必须带 RectTransform）
    ///   ② 把工程里全部 VNAiPersonaDef 填进模板
    ///   ③ 把人格登记进 VNGameConfig（重建场景也不丢）
    /// 重复执行安全：已装过就只刷新人格列表。
    ///
    /// 装完还会顺手做一次体检：key 有没有配、人格有没有绑角色、角色有没有表情，
    /// 这三样缺一个运行时就会当场翻车，不如装机时就说清楚。
    /// </summary>
    public static class VNAiTalkInstaller
    {
        const string ModuleId = "aitalk";
        const string TemplateName = "AiTalkTemplate";

        [MenuItem("Tools/VN Effects/Install AI Talk Module To Scene", priority = 211)]
        public static void Install() => Install(true);

        /// <summary>
        /// 装机核心。interactive=false 时不弹模态框，只写 Console——
        /// 供自动化/批处理调用（DisplayDialog 会阻塞主线程等用户点击）。
        /// 返回是否成功。
        /// </summary>
        public static bool Install(bool interactive)
        {
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                const string msg =
                    "当前场景里找不到 VNEventRegistry。\n\n" +
                    "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                    "或用 Tools → VN Effects → Create Script Demo Scene 造一个新场景。";
                if (interactive) EditorUtility.DisplayDialog("VN AI Talk", msg, "OK");
                else Debug.LogError("[VNAiTalk] " + msg);
                return false;
            }

            var report = new List<string>();
            var warnings = new List<string>();

            // ① 收集工程里全部人格资产
            var personas = AssetDatabase.FindAssets("t:VNAiPersonaDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<VNAiPersonaDef>)
                .Where(a => a != null)
                .ToList();

            if (personas.Count == 0)
                warnings.Add("工程里一套 VNAiPersonaDef 都没有 —— " +
                             "先 Create → VN → AI Persona 建一套，否则剧本跑起来会直接返回「失败」");
            else
                report.Add($"人格资产 ×{personas.Count}：" +
                           string.Join("、", personas.Select(p => p.id)));

            // ② 场景模板
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNAiTalkModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install AI talk module");

                // ★ 必须带 RectTransform：模块 BuildUi 里直接 (RectTransform)transform，
                //   普通 Transform 会在运行时抛 InvalidCastException（和 quiz 模块同坑）
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNAiTalkModule>();
                go.SetActive(false);   // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install AI talk module");

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
                Undo.RecordObject(module, "Refresh AI talk module");
                report.Add($"模块「{ModuleId}」已存在，只刷新人格列表");
            }

            module.personas = new List<VNAiPersonaDef>(personas);
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ③ 登记进 VNGameConfig
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register AI personas");
                config.aiPersonas = new List<VNAiPersonaDef>(personas);
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add($"VNGameConfig 人格列表 ×{config.aiPersonas.Count}");
            }

            // ④ 体检：这三样缺一个运行时就当场翻车
            if (!VNAiKey.HasKey)
                warnings.Add("找不到 Gemini API Key —— 运行时每轮都会走兜底台词。\n" +
                             "  配置方式见 Tools → VN Effects → AI → Show Key Status");
            foreach (var p in personas)
            {
                var errors = p.Validate();
                if (errors.Count > 0)
                    warnings.Add($"人格「{p.id}」：{string.Join("；", errors)}");
                else if (p.character.expressions == null || p.character.expressions.Count == 0)
                    warnings.Add($"人格「{p.id}」绑定的角色「{p.character.id}」没有配任何表情立绘");
            }

            string summary = string.Join("\n", report);
            if (warnings.Count > 0)
                summary += "\n\n⚠ 需要注意：\n- " + string.Join("\n- ", warnings);

            Debug.Log($"[VNAiTalk] 已装入当前场景：\n{summary}");
            if (interactive)
            {
                EditorUtility.DisplayDialog("VN AI Talk",
                    $"AI 自由聊天模块已装进当前场景：\n\n{summary}\n\n" +
                    "场景已标记为未保存——记得 Ctrl+S。\n\n" +
                    "剧本里就可以写：\n" +
                    "  show 星野结衣 at:center\n" +
                    "  event aitalk vs:星野结衣 turns:8 topic:社团招新 stat:好感 flag:AI聊天_\n" +
                    "  * 好感提升\n  * 普通\n  * 冷场\n  * 失败", "OK");
                Selection.activeObject = module.gameObject;
                EditorGUIUtility.PingObject(module.gameObject);
            }
            return true;
        }
    }
}
