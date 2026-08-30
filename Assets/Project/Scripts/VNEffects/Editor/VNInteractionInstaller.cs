using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把亲密互动模块**增量装进当前场景**（不重建场景，可重复执行）：
    ///   ① 注册表下补一个禁用的 InteractionTemplate（必须带 RectTransform）
    ///   ② 缺资产时铺一套示例（星野结衣的部位框 + 一份互动定义 + 四个道具）
    ///   ③ 把工程里全部 VNInteractionDef / VNTouchZoneDef 填进模板
    /// 已装过则只刷新资产列表。
    /// </summary>
    public static class VNInteractionInstaller
    {
        const string ModuleId = "interact";
        const string TemplateName = "InteractionTemplate";

        const string ZoneDir = "Assets/Art/VNEffects/TouchZones";
        const string InteractionDir = "Assets/Art/VNEffects/Interactions";
        const string ItemDir = "Assets/Art/InteractionMiniGame";

        const string DemoCharacter = "星野结衣";
        const string DemoInteractionId = "初次抚摸";

        [MenuItem("Tools/VN Effects/场景装机 Install To Scene/亲密互动 Interaction Module",
                  priority = 141)]
        public static void Install()
        {
            string report = InstallSilent();
            EditorUtility.DisplayDialog("VN Interaction", report, "OK");
        }

        /// <summary>
        /// 不弹窗版本，返回报告文本。菜单入口与自动化脚本（MCP / 批处理）共用同一条路径，
        /// 免得「手点能装、脚本装不了」两套行为分叉。
        /// </summary>
        public static string InstallSilent()
        {
            var registry = Object.FindAnyObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
                return "当前场景里找不到 VNEventRegistry。\n\n" +
                       "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个）。";

            var report = new List<string>();

            // ① 示例资产（已存在的一律原样保留，绝不覆盖用户调过的框）
            EnsureFolder(ZoneDir);
            EnsureFolder(InteractionDir);
            var zoneDef = EnsureDemoZones(report);
            EnsureDemoInteraction(zoneDef, report);

            var allZones = LoadAll<VNTouchZoneDef>();
            var allInteractions = LoadAll<VNInteractionDef>();
            report.Add($"部位区域资产 ×{allZones.Count}　互动定义资产 ×{allInteractions.Count}");

            // ② 场景模板
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNInteractionModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install interaction module");

                // ★ 必须带 RectTransform：模块 BuildUi 里直接 (RectTransform)transform
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNInteractionModule>();
                go.SetActive(false);        // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install interaction module");

                if (entry == null)
                    registry.modules.Add(new VNEventRegistry.Entry
                    { id = ModuleId, template = module });
                else entry.template = module;

                report.Add($"注册表新增模块「{ModuleId}」→ {TemplateName}（已禁用）");
            }
            else
            {
                Undo.RecordObject(module, "Refresh interaction module");
                report.Add($"模块「{ModuleId}」已存在，只刷新资产列表");
            }

            module.interactions = allInteractions;
            module.zoneDefs = allZones;
            EditorUtility.SetDirty(module);
            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);
            AssetDatabase.SaveAssets();

            return string.Join("\n", report) +
                   "\n\n剧本写法：\n" +
                   $"  event interact vs:{DemoCharacter} id:{DemoInteractionId} zones:on\n" +
                   "  * 满足\n  * 普通\n  * 拒绝";
        }

        // ------------------------------------------------------------------
        // 示例资产
        // ------------------------------------------------------------------

        /// <summary>
        /// 星野结衣的部位框。坐标是**照着 hoshino_normal.png 的真实构图**量的
        /// （那套素材是带背景的半身图，人物只占画面中间一竖条，
        /// 所以框比"通用全身立绘"要窄要靠上）。归一化：(0,0) = 图中心。
        /// </summary>
        static VNTouchZoneDef EnsureDemoZones(List<string> report)
        {
            string path = $"{ZoneDir}/{DemoCharacter}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VNTouchZoneDef>(path);
            if (existing != null)
            {
                report.Add($"部位框「{DemoCharacter}」已存在，保留不动");
                return existing;
            }

            var def = ScriptableObject.CreateInstance<VNTouchZoneDef>();
            def.characterId = DemoCharacter;
            def.baseZones = new List<VNTouchZone>
            {
                Zone("头", "头发", new Vector2(0f, 0.368f), new Vector2(0.26f, 0.23f),
                     priority: 0, gain: 1f),
                Zone("脸", "脸颊", new Vector2(0.008f, 0.153f), new Vector2(0.165f, 0.21f),
                     priority: 10, gain: 1.4f),
                Zone("耳", "耳朵", new Vector2(0.10f, 0.20f), new Vector2(0.045f, 0.06f),
                     priority: 20, gain: 1.6f, unlockStage: 1),
                Zone("颈", "脖颈", new Vector2(0.005f, -0.056f), new Vector2(0.10f, 0.09f),
                     priority: 15, gain: 1.8f, unlockStage: 1),
                Zone("肩", "肩膀", new Vector2(-0.12f, -0.136f), new Vector2(0.12f, 0.075f),
                     priority: 5, gain: 1f),
                Zone("胸", "胸口", new Vector2(0.005f, -0.341f), new Vector2(0.225f, 0.19f),
                     priority: 8, gain: 2.5f, unlockStage: 2),
                Zone("腰", "腰腹", new Vector2(0.005f, -0.468f), new Vector2(0.2f, 0.08f),
                     priority: 5, gain: 2f, unlockStage: 2),
            };

            AssetDatabase.CreateAsset(def, path);
            report.Add($"新建部位框资产 {path}（7 个部位，按 hoshino_normal 构图量的）");
            return def;
        }

        static VNTouchZone Zone(string id, string label, Vector2 center, Vector2 size,
            int priority, float gain, int unlockStage = 0)
        {
            return new VNTouchZone
            {
                id = id,
                displayName = label,
                shape = VNZoneShape.Ellipse,
                center = center,
                size = size,
                priority = priority,
                gainScale = gain,
                unlockStage = unlockStage,
                enabled = true,
            };
        }

        static void EnsureDemoInteraction(VNTouchZoneDef zoneDef, List<string> report)
        {
            string path = $"{InteractionDir}/{DemoInteractionId}.asset";
            if (AssetDatabase.LoadAssetAtPath<VNInteractionDef>(path) != null)
            {
                report.Add($"互动定义「{DemoInteractionId}」已存在，保留不动");
                return;
            }

            var def = ScriptableObject.CreateInstance<VNInteractionDef>();
            def.id = DemoInteractionId;
            def.title = "";
            def.items = BuildDemoItems();

            // 阶段推进的台词一律 blocking：这是「场面转折」，该让玩家停下来看完，
            // 过程中的碎反应才不阻塞（否则一直被打断没法连续抚摸）
            def.stages = new List<VNInteractionStage>
            {
                Stage("平静", 0f, "默认"),
                Stage("心动", 30f, "微笑", "…嗯？怎么突然这样。", "害羞", blocking: true),
                // ② 阶段推进时喷一下：{zx}{zy} = 当前部位中心
                Stage("害羞", 80f, "害羞", "别、别这样…会被人看到的。", "红晕", blocking: true,
                      script: "liquid splash x:{zx} y:{zy} type:water power:1.4"),
                // ③ 到最高阶段后持续喷 + ④ 镜头开始沾水渍（浓度跟着整场进度走）
                //    这两个是**持续状态**，必须靠 cleanupLines 收口
                Stage("情动", 150f, "害羞", "……我已经，不行了。", "心", blocking: true,
                      // 内嵌剧本行示范：字段配不出来的演出写在这里，走 RunInlineCo
                      script: "fx shockwave light\n" +
                              "camera pushin 1.08 2 @\n" +
                              "liquid spray on x:{zx} y:{zy} type:water rate:0.35 power:0.7\n" +
                              "liquid wet on amount:{prog}"),
            };

            def.rules = new List<VNInteractionZoneRule>
            {
                Rule("头", 1f, 8f,
                    Fb("摸头 · 舒服", expr: "微笑", mark: "音符", line: "呼呼…好舒服。"),
                    Fb("摸头 · 点头", emote: "点头", minStage: 1)),
                Rule("脸", 1.2f, 7f,
                    Fb("摸脸 · 害羞", expr: "害羞", mark: "红晕", line: "唔…痒痒的。"),
                    Fb("摸脸 · 侧头", emote: "害羞", minStage: 1)),
                Rule("耳", 1.4f, 6f,
                    Fb("碰耳 · 颤抖", expr: "惊讶", mark: "汗", line: "耳朵…不行啦…", excite: 4f)),
                // ① 摸到特定部位就喷：{cx}{cy} = 光标位置，所以是「摸哪儿喷哪儿」
                Rule("颈", 1.5f, 6f,
                    Fb("脖颈", expr: "害羞", mark: "红晕", line: "呀…！", excite: 5f,
                       script: "liquid splash x:{cx} y:{cy} type:water power:0.8 screen:0.4")),
                Rule("肩", 0.8f, 9f,
                    Fb("肩膀", expr: "微笑", line: "肩膀有点酸呢。")),
                Rule("胸", 2f, 5f,
                    Fb("胸口", expr: "害羞", mark: "心", line: "…笨蛋。", excite: 8f)),
                Rule("腰", 1.8f, 6f,
                    Fb("腰", expr: "惊讶", mark: "汗", line: "腰…好痒…", excite: 6f)),
            };

            def.rejectFeedbacks = new List<VNInteractionFeedback>
            {
                Fb("拒绝 · 生气", expr: "生气", mark: "怒", emote: "生气",
                   line: "喂……现在还不行。", excite: -6f, cooldown: 1.5f),
                Fb("拒绝 · 躲开", expr: "惊讶", emote: "摇头",
                   line: "等、等一下！", excite: -4f, cooldown: 1.5f),
            };

            def.dragPixelsPerUnit = 60f;
            def.clickUnits = 0.6f;
            def.exciteDecayPerSecond = 0f;
            def.rejectLimit = 3;
            def.rejectCooldown = 1.2f;
            def.targetStage = 3;
            def.timeLimit = 0f;
            def.allowManualEnd = true;
            def.autoEndOnTarget = true;
            def.flagPrefix = "抚摸";
            // 四条退出路径（正常结束/收手/ESC/调试中断）都会执行 ——
            // 不写的话玩家中途退出，spray 会一直喷下去
            def.cleanupLines = "liquid spray off\nliquid dry";

            def.endSatisfied = Fb("结局 · 满足", expr: "害羞", mark: "心",
                                  line: "……今天，就到这里吧。");
            def.endNormal = Fb("结局 · 普通", expr: "微笑", line: "嗯，谢谢你。");
            def.endRejected = Fb("结局 · 拒绝", expr: "生气", line: "……我先走了。");

            AssetDatabase.CreateAsset(def, path);
            report.Add($"新建互动定义 {path}（4 阶段 / 7 部位规则 / {def.items.Count} 个道具）");
        }

        static List<VNInteractionItem> BuildDemoItems()
        {
            var items = new List<VNInteractionItem>();

            // item4 = 写实手掌，item2 = 红色手掌，item1 / item3 = 道具
            items.Add(Item("手", "手", "item4", VNCursorIdleAnim.SwingX,
                idleFreq: 1.2f, idleAmp: 12f,
                press: VNCursorPressAnim.FastSwing, pressFreq: 9f, pressAmp: 9f,
                gain: 1f, hotspot: new Vector2(0f, 0.28f)));

            items.Add(Item("手掌", "手掌", "item2", VNCursorIdleAnim.Rock,
                idleFreq: 1f, idleAmp: 8f,
                press: VNCursorPressAnim.Press, pressFreq: 6f, pressAmp: 6f,
                gain: 1.1f, hotspot: new Vector2(0f, 0.25f)));

            items.Add(Item("按摩棒", "按摩棒", "item1", VNCursorIdleAnim.SwingY,
                idleFreq: 0.8f, idleAmp: 6f,
                press: VNCursorPressAnim.Vibrate, pressFreq: 22f, pressAmp: 5f,
                gain: 1.6f, hotspot: new Vector2(0f, 0.42f)));

            items.Add(Item("玩具", "玩具", "item3", VNCursorIdleAnim.Breathe,
                idleFreq: 0.9f, idleAmp: 5f,
                press: VNCursorPressAnim.Vibrate, pressFreq: 18f, pressAmp: 4f,
                gain: 1.5f, hotspot: new Vector2(0f, 0.4f)));

            return items;
        }

        static VNInteractionItem Item(string id, string label, string iconFile,
            VNCursorIdleAnim idle, float idleFreq, float idleAmp,
            VNCursorPressAnim press, float pressFreq, float pressAmp,
            float gain, Vector2 hotspot)
        {
            return new VNInteractionItem
            {
                id = id,
                displayName = label,
                icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{ItemDir}/{iconFile}.png"),
                cursorHeight = 170f,
                hotspot = hotspot,
                idleAnim = idle,
                idleFrequency = idleFreq,
                idleAmplitude = idleAmp,
                pressAnim = press,
                pressFrequency = pressFreq,
                pressAmplitude = pressAmp,
                tiltWithMotion = true,
                tiltMax = 22f,
                gainScale = gain,
            };
        }

        static VNInteractionStage Stage(string name, float threshold, string idleExpr,
            string enterLine = null, string enterMark = null, bool blocking = false,
            string script = null)
        {
            var fb = enterLine == null && enterMark == null && script == null
                ? new VNInteractionFeedback()
                : Fb($"进入 {name}", mark: enterMark, line: enterLine, blocking: blocking);
            fb.scriptLines = script;
            return new VNInteractionStage
            {
                name = name,
                threshold = threshold,
                idleExpression = idleExpr,
                enterFeedback = fb,
            };
        }

        static VNInteractionZoneRule Rule(string zoneId, float gainPerUnit,
            float feedbackEvery, params VNInteractionFeedback[] feedbacks)
        {
            return new VNInteractionZoneRule
            {
                zoneId = zoneId,
                itemId = "",
                gainPerUnit = gainPerUnit,
                feedbackEvery = feedbackEvery,
                feedbacks = new List<VNInteractionFeedback>(feedbacks),
            };
        }

        static VNInteractionFeedback Fb(string note, string expr = null, string mark = null,
            string emote = null, string line = null, float excite = 0f,
            int minStage = -1, float cooldown = 2.5f, bool blocking = false,
            string script = null)
        {
            return new VNInteractionFeedback
            {
                scriptLines = script,
                note = note,
                expression = expr,
                mark = mark,
                emote = emote,
                line = line,
                excite = excite,
                minStage = minStage,
                maxStage = -1,
                weight = 1f,
                cooldown = cooldown,
                blocking = blocking,
                voicePool = new List<string>(),
            };
        }

        // ------------------------------------------------------------------

        static List<T> LoadAll<T>() where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(a => a != null)
                .ToList();

        static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parts = dir.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
