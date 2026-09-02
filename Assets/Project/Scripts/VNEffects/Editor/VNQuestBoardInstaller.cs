using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 把委托板模块**增量装进当前场景**，不重建场景（同 VNQuizInstaller 的做法）。
    /// 做三件事：
    ///   ① 在场景的 VNEventRegistry 下补一个**禁用的** QuestBoardTemplate（带 RectTransform）
    ///   ② 确保场景里有 VNQuestLog（任务面板 + 引擎驱动都靠它）
    ///   ③ 把工程里全部 VNQuestDef 登记进 VNGameConfig 的任务库
    /// 重复执行安全：已经装过就只刷新任务列表。
    /// </summary>
    public static class VNQuestBoardInstaller
    {
        const string ModuleId = "questboard";
        const string TemplateName = "QuestBoardTemplate";

        [MenuItem("Tools/VN Effects/场景装机 Install To Scene/委托板 Quest Board", priority = 141)]
        public static void Install()
        {
            var registry = Object.FindFirstObjectByType<VNEventRegistry>(
                FindObjectsInactive.Include);
            if (registry == null)
            {
                EditorUtility.DisplayDialog("VN Quest Board",
                    "当前场景里找不到 VNEventRegistry。\n\n" +
                    "事件模块要挂在注册表下面。请先打开剧本场景（含 VNEventRegistry 的那个），" +
                    "或用 Tools → VN Effects → 演示场景 Demo Scenes → " +
                    "重建剧本演示场景 Create Script Demo Scene 造一个新场景。", "OK");
                return;
            }

            var report = new List<string>();

            // ① 工程里的任务资产（缺哪个示例补哪个，已有的原样保留）
            int made = EnsureSampleQuests();
            if (made > 0) report.Add($"新建示例任务 ×{made}");
            var allQuests = AssetDatabase.FindAssets("t:VNQuestDef")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<VNQuestDef>)
                .Where(a => a != null && !string.IsNullOrEmpty(a.id))
                .ToList();
            report.Add($"任务资产 ×{allQuests.Count}");

            // ② 场景里的模块模板
            var entry = registry.modules.FirstOrDefault(e => e != null && e.id == ModuleId);
            var module = entry != null ? entry.template as VNQuestBoardModule : null;

            if (module == null)
            {
                Undo.RecordObject(registry, "Install quest board module");

                // ★ 必须带 RectTransform：模块 OnLaunch 里直接 (RectTransform)transform
                var go = new GameObject(TemplateName, typeof(RectTransform));
                go.transform.SetParent(registry.transform, false);
                module = go.AddComponent<VNQuestBoardModule>();
                go.SetActive(false); // 模板保持禁用，运行时 Instantiate 后才激活
                Undo.RegisterCreatedObjectUndo(go, "Install quest board module");

                if (entry == null)
                    registry.modules.Add(new VNEventRegistry.Entry
                    {
                        id = ModuleId, template = module,
                    });
                else entry.template = module;

                report.Add($"注册表新增模块「{ModuleId}」→ {TemplateName}（已禁用）");
            }
            else
            {
                report.Add($"模块「{ModuleId}」已存在");
            }

            // ③ 任务面板（J 键 + 引擎求值都靠它；Runner 找不到会自己造一个，
            //    但那样就拿不到场景里手工配的统计声明了）
            var log = Object.FindFirstObjectByType<VNQuestLog>(FindObjectsInactive.Include);
            if (log == null)
            {
                var go = new GameObject("VNQuestLog");
                log = go.AddComponent<VNQuestLog>();
                Undo.RegisterCreatedObjectUndo(go, "Create quest log");
                report.Add("场景新增 VNQuestLog（任务面板 + 引擎驱动）");
            }
            Undo.RecordObject(log, "Refresh quest list");
            log.quests = new List<VNQuestDef>(allQuests);
            EditorUtility.SetDirty(log);

            EditorUtility.SetDirty(registry);
            EditorSceneManager.MarkSceneDirty(registry.gameObject.scene);

            // ④ 登记进 VNGameConfig（覆盖语义：重建场景也不丢）
            var config = VNGameConfigTools.LoadOrCreate();
            if (config != null)
            {
                Undo.RecordObject(config, "Register quests");
                config.quests = new List<VNQuestDef>(allQuests);

                // 统计声明：没配过就铺一套默认的。没有它，任务条件里的
                // 羽球_我方得分@最高 永远读不到值（小游戏只写「本次成绩」）
                if (config.trackers == null || config.trackers.Count == 0)
                {
                    config.trackers = new List<VNTrackerEntry>
                    {
                        new VNTrackerEntry
                        {
                            sourceFlag = "羽球_我方得分",
                            trackMax = true, trackSum = true, trackCount = true,
                        },
                        new VNTrackerEntry { sourceFlag = "羽球_最长回合", trackMax = true },
                        new VNTrackerEntry
                        {
                            sourceFlag = "照片_分数", trackMax = true, trackCount = true,
                        },
                        new VNTrackerEntry { sourceFlag = "擦雾_清晰度", trackMax = true },
                    };
                    report.Add($"VNGameConfig 统计声明 ×{config.trackers.Count}（默认一套）");
                }
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
                VNGameConfig.ClearCache();
                report.Add($"VNGameConfig 任务库 ×{config.quests.Count}");
            }

            string summary = string.Join("\n", report);
            Debug.Log($"[VNQuestBoard] 已装入当前场景：\n{summary}");
            EditorUtility.DisplayDialog("VN Quest Board",
                $"委托板模块已装进当前场景：\n\n{summary}\n\n" +
                "场景已标记为未保存——记得 Ctrl+S。\n" +
                "剧本里就可以写：event questboard tag:社团 max:3", "OK");
            // 广播「素材库改了」：剧本编辑器收到就重建 quest 的 id 下拉候选，
            // 不然装机之后还得手点一次 Refresh Sources 才搜得到新任务
            VNAssetLibraryEvents.RaiseChanged();

            Selection.activeObject = module.gameObject;
            EditorGUIUtility.PingObject(module.gameObject);
        }

        // ==============================================================
        // 示例任务
        // ==============================================================

        const string DefaultQuestsDir = "Assets/VNEffects/Quests";

        /// <summary>
        /// 造三个示例任务：
        ///   球场之王   —— 两阶段 + 统计条件（羽球_我方得分@最高）+ 阶段递增奖励
        ///   摄影社的委托 —— 委托板可接 + 限时 3 个月 + 多子目标带进度条
        ///   每月的陪练 —— 可重复日常 + 自动接取
        /// 这三个刚好覆盖了「自动判定 / 委托板 / 限时 / 日常 / 多子目标 / 任务链」全部路径。
        ///
        /// **逐个按 id 判断存不存在**，不能用「工程里一个 VNQuestDef 都没有才造」——
        /// 工程里早就有「告白大作战」，那样写等于三个示例永远造不出来
        /// （症状：剧本编辑器的 id 下拉搜不到，运行时任务日志只有光秃秃一个标题）。
        /// </summary>
        static int EnsureSampleQuests()
        {
            // 造在既有任务资产所在的目录，跟着工程的目录整理走
            string dir = DefaultQuestsDir;
            var existing = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:VNQuestDef"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var q = AssetDatabase.LoadAssetAtPath<VNQuestDef>(path);
                if (q == null || string.IsNullOrEmpty(q.id)) continue;
                existing.Add(q.id);
                dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            }

            if (existing.Contains("球场之王") && existing.Contains("摄影社的委托") &&
                existing.Contains("每月的陪练"))
                return 0;

            VNEffectsDemoSetup.EnsureFolder(dir);
            string QuestsDir = dir;
            int made = 0;

            // ── 球场之王：靠统计层派生值判定，两阶段 ──
            var king = ScriptableObject.CreateInstance<VNQuestDef>();
            king.id = "球场之王";
            king.title = "球场之王";
            king.titleEn = "King of the Court";
            king.titleJa = "コートの王者";
            king.description = "在羽毛球对战里证明自己。";
            king.descriptionEn = "Prove yourself on the badminton court.";
            king.descriptionJa = "バドミントンで自分の実力を証明しよう。";
            king.priority = 10;
            king.stageDefs.Add(new VNQuestDef.Stage
            {
                text = "先赢下一场比赛",
                textEn = "Win a single match",
                textJa = "まずは一勝",
                objectives =
                {
                    new VNQuestDef.Objective
                    {
                        text = "打赢一场羽毛球",
                        textEn = "Win one badminton match",
                        textJa = "バドミントンで1勝する",
                        condition = "羽球_我方得分@最高>=21",
                    },
                },
                rewards =
                {
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Stat, target = "金钱", amount = 200,
                    },
                },
            });
            king.stageDefs.Add(new VNQuestDef.Stage
            {
                text = "单场拿下 5000 分",
                textEn = "Score 5000 in a single match",
                textJa = "1試合で5000点",
                objectives =
                {
                    new VNQuestDef.Objective
                    {
                        text = "单场得分达到 5000",
                        textEn = "Reach 5000 points in one match",
                        textJa = "1試合で5000点に到達",
                        condition = "羽球_我方得分@最高>=5000",
                        progressFlag = "羽球_我方得分@最高",
                        progressTarget = 5000,
                    },
                },
                rewards =
                {
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Stat, target = "金钱", amount = 1000,
                    },
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Flag, target = "待触发_球王庆功", amount = 1,
                        note = "解锁新剧情",
                        noteEn = "Unlocks a new scene",
                        noteJa = "新しいシーンが解放",
                    },
                },
            });
            if (existing.Contains("球场之王")) Object.DestroyImmediate(king);   // 已经有了，别覆盖玩家改过的
            else { AssetDatabase.CreateAsset(king, $"{QuestsDir}/球场之王.asset"); made++; }

            // ── 摄影社的委托：委托板可接 + 限时 + 多子目标 ──
            var photo = ScriptableObject.CreateInstance<VNQuestDef>();
            photo.id = "摄影社的委托";
            photo.title = "摄影社的委托";
            photo.titleEn = "The Photo Club's Request";
            photo.titleJa = "写真部からの依頼";
            photo.description = "帮摄影社凑齐这期社刊的照片。";
            photo.descriptionEn = "Help the photo club fill this issue of their magazine.";
            photo.descriptionJa = "写真部の部誌に載せる写真を集めよう。";
            photo.acceptFromBoard = true;
            photo.boardTag = "社团";
            photo.clientCharacterId = "摄影社部长";
            photo.deadlineMonths = 3;
            photo.expireToFail = true;
            photo.stageDefs.Add(new VNQuestDef.Stage
            {
                text = "拍出能上社刊的照片",
                textEn = "Take photos worth publishing",
                textJa = "部誌に載せられる写真を撮る",
                objectives =
                {
                    new VNQuestDef.Objective
                    {
                        text = "拍到一张「完美」评价的照片",
                        textEn = "Get a Perfect rating once",
                        textJa = "「パーフェクト」評価を1回取る",
                        condition = "照片_分数@最高>=90",
                    },
                    new VNQuestDef.Objective
                    {
                        text = "累计拍 5 张照片",
                        textEn = "Take 5 photos in total",
                        textJa = "写真を合計5枚撮る",
                        condition = "照片_分数@次数>=5",
                        progressFlag = "照片_分数@次数",
                        progressTarget = 5,
                    },
                },
                rewards =
                {
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Stat, target = "金钱", amount = 500,
                    },
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Quest, target = "每月的陪练",
                    },
                },
            });
            photo.penalties.Add(new VNQuestReward
            {
                kind = VNQuestRewardKind.Stat, target = "金钱", amount = -100,
                note = "违约金 -100",
                noteEn = "Penalty -100",
                noteJa = "違約金 -100",
            });
            if (existing.Contains("摄影社的委托")) Object.DestroyImmediate(photo);   // 已经有了，别覆盖玩家改过的
            else { AssetDatabase.CreateAsset(photo, $"{QuestsDir}/摄影社的委托.asset"); made++; }

            // ── 每月的陪练：可重复日常 + 自动接取 ──
            var daily = ScriptableObject.CreateInstance<VNQuestDef>();
            daily.id = "每月的陪练";
            daily.title = "每月的陪练";
            daily.titleEn = "Monthly Practice Partner";
            daily.titleJa = "今月の練習相手";
            daily.description = "这个月也陪她打一场吧。";
            daily.descriptionEn = "Play a match with her again this month.";
            daily.descriptionJa = "今月も彼女と一戦交えよう。";
            daily.acceptAuto = true;
            daily.repeatable = true;
            daily.cooldownMonths = 1;
            daily.maxTimes = 0;
            daily.stageDefs.Add(new VNQuestDef.Stage
            {
                text = "本月陪她打一场",
                textEn = "Play one match with her this month",
                textJa = "今月中に一戦する",
                objectives =
                {
                    new VNQuestDef.Objective
                    {
                        text = "打一场羽毛球",
                        textEn = "Play one badminton match",
                        textJa = "バドミントンを1試合",
                        condition = "羽球_我方得分@次数>=1",
                    },
                },
                rewards =
                {
                    new VNQuestReward
                    {
                        kind = VNQuestRewardKind.Stat, target = "好感度", amount = 3,
                    },
                },
            });
            if (existing.Contains("每月的陪练")) Object.DestroyImmediate(daily);   // 已经有了，别覆盖玩家改过的
            else { AssetDatabase.CreateAsset(daily, $"{QuestsDir}/每月的陪练.asset"); made++; }

            AssetDatabase.SaveAssets();
            if (made > 0)
                Debug.Log($"[VNQuestBoard] 已生成 {made} 个示例任务到 {QuestsDir}");
            return made;
        }
    }
}
