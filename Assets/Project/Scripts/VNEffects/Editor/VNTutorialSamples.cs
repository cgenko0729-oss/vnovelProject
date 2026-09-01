using System.IO;
using UnityEditor;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 示例教程资产的一键生成。
    ///
    /// 手写 .asset 的 YAML 需要脚本 GUID（Unity 导入后才有），所以走菜单生成，
    /// 顺带登记进 VNGameConfig 的教程库 —— 和项目里其它 Export 菜单一个路子。
    /// 已存在同名资产时**只补空缺不覆盖**：改过的示例不会被这条菜单打回去。
    /// </summary>
    public static class VNTutorialSamples
    {
        const string Dir = "Assets/VNEffects/Tutorials";
        const string BadmintonId = "羽毛球基础";

        [MenuItem("Tools/VN Effects/教程 Tutorials/导出羽毛球示例教程 Export Badminton Sample", priority = 150)]
        public static void ExportBadminton()
        {
            EnsureFolder(Dir);
            string path = $"{Dir}/{BadmintonId}.asset";

            var def = AssetDatabase.LoadAssetAtPath<VNTutorialDef>(path);
            if (def != null)
            {
                Register(def);
                Debug.Log($"[VNTutorial] 示例教程已存在（{path}），未覆盖，只确认了登记。");
                Selection.activeObject = def;
                EditorGUIUtility.PingObject(def);
                return;
            }

            def = ScriptableObject.CreateInstance<VNTutorialDef>();
            def.id = BadmintonId;
            def.dim = 0.74f;
            def.steps.Clear();

            // 第 1 步：不挖洞的开场页（anchor 与 area 都不填 = 整屏压暗）
            def.steps.Add(new VNTutorialStep
            {
                anchor = "",
                area = new Rect(0f, 0f, 0f, 0f),
                title = "羽毛球对战",
                titleEn = "Badminton",
                titleJa = "バドミントン",
                body = "先花十几秒认识一下界面和操作。\n讲解期间比赛是暂停的，随时按 ESC 可以跳过。",
                bodyEn = "A quick look at the screen and the controls.\n" +
                         "The match is paused while you read. Press ESC to skip.",
                bodyJa = "画面と操作をざっと確認しましょう。\n" +
                         "説明中は試合が止まります。ESC でスキップできます。",
                card = VNTutorialCardSpot.Center,
            });

            def.steps.Add(new VNTutorialStep
            {
                anchor = VNBadmintonModule.AnchorScore,
                padding = 14f,
                corner = 26f,
                title = "记分板",
                titleEn = "Scoreboard",
                titleJa = "スコアボード",
                body = "左边是你的分数，右边是对手的。\n下面那行写着这一局打到几分算赢。",
                bodyEn = "Your score on the left, your opponent's on the right.\n" +
                         "The line below shows the target score for this match.",
                bodyJa = "左があなた、右が相手のスコアです。\n下の行はこの試合の目標点です。",
                card = VNTutorialCardSpot.Bottom,
            });

            def.steps.Add(new VNTutorialStep
            {
                anchor = VNBadmintonModule.AnchorMe,
                shape = VNTutorialHole.Ellipse,
                padding = 26f,
                title = "移动",
                titleEn = "Move",
                titleJa = "移動",
                body = "A / D（或 ← / →）左右跑位。\n站到球的落点下面才接得到。",
                bodyEn = "A / D (or ← / →) to move.\n" +
                         "Get under the shuttle's landing spot to reach it.",
                bodyJa = "A / D（または ← / →）で移動。\n落下地点の下に入ると届きます。",
                card = VNTutorialCardSpot.Top,
            });

            def.steps.Add(new VNTutorialStep
            {
                anchor = VNBadmintonModule.AnchorBall,
                shape = VNTutorialHole.Ellipse,
                padding = 46f,
                title = "击球与扣杀",
                titleEn = "Hit and Smash",
                titleJa = "打つ・スマッシュ",
                body = "J 键挥拍。在球最靠近球拍的那一瞬间挥出去，判定为「精准」，球更快更刁。\n" +
                       "K 键起跳，在空中挥拍就是扣杀。",
                bodyEn = "Press J to swing. Swinging at the closest moment counts as a " +
                         "perfect hit — faster and sharper.\nPress K to jump; swinging " +
                         "mid-air is a smash.",
                bodyJa = "J でスイング。最接近の瞬間に振ると「パーフェクト」になり、" +
                         "速く鋭い球になります。\nK でジャンプ、空中でのスイングがスマッシュです。",
                card = VNTutorialCardSpot.Bottom,
            });

            def.steps.Add(new VNTutorialStep
            {
                anchor = VNBadmintonModule.AnchorHint,
                padding = 12f,
                corner = 14f,
                title = "随时可以回看",
                titleEn = "Controls Reminder",
                titleJa = "操作の確認",
                body = "操作说明一直留在右下角。\n中途想退出按 ESC —— 正式比赛里认输算输。",
                bodyEn = "The controls stay in the bottom-right corner.\n" +
                         "ESC quits — in a ranked match that counts as a loss.",
                bodyJa = "操作説明は右下に常時表示されます。\n" +
                         "ESC で中断できますが、公式戦では負け扱いです。",
                card = VNTutorialCardSpot.Top,
            });

            AssetDatabase.CreateAsset(def, path);
            Register(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
            Debug.Log($"[VNTutorial] 示例教程已生成：{path}，并登记进 VNGameConfig 教程库。\n" +
                      "试玩方式：剧本写 tutorial 羽毛球基础 force:on，" +
                      "或在羽毛球模块模板的 tutorialId 填「羽毛球基础」后 event badminton。");
        }

        static void Register(VNTutorialDef def)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg == null)
            {
                Debug.LogWarning($"[VNTutorial] 找不到 {VNGameConfig.AssetPath}，" +
                                 "请手动把教程资产登记进教程库。");
                return;
            }
            if (cfg.tutorials.Contains(def)) return;
            cfg.tutorials.Add(def);
            EditorUtility.SetDirty(cfg);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
