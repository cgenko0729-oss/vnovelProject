using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public enum VNLintSeverity { Error, Warning, Info }

    public class VNLintIssue
    {
        public VNLintSeverity severity;
        public string code;        // 规则代号，便于日后按规则屏蔽
        public string file;        // 剧本名（不含 .vn.txt）
        public string assetPath;
        public int line;           // 1 起；0 = 全文件级
        public string message;
        public string hint;        // 怎么修

        public string Short => $"{file}:{line}  {message}";
    }

    /// <summary>
    /// 剧本静态校验器 —— 把"只有运行到那一行才会发现"的错误提前到编辑期。
    ///
    /// 【最重要的设计决定：复用 VNScriptParser.Parse】
    /// 校验器**不自己分词**，而是调用运行时用的同一个解析器。
    /// 理由：分词规则一旦有两套实现就会漂移，校验器就会"说谎"——
    /// 项目历史上正是"文档描述"与"解析器实际行为"不一致
    /// （`文件::标签` 被当成 key:value）导致 94 处跳转静默失效。
    /// 复用解析器意味着：**校验器看到的命令流，与 Runner 执行的完全一致**。
    ///
    /// 【严重度约定】
    ///   Error   —— 一定会坏（悬空跳转、重名 label、子程序回不去、emote 枚举名写错）
    ///   Warning —— 很可能坏，但存在合理例外（素材还没登记、事件结果名不认识）
    ///   Info    —— 卫生问题（label 从未被引用）
    ///
    /// 【为什么资产类问题是 Warning 而不是 Error】
    /// 边写剧本边补素材是正常工作流，报 Error 会让人习惯性忽略全部输出——
    /// 那样校验器就废了。宁可少喊，也不要喊狼来了。
    /// </summary>
    public static partial class VNScenarioLinter
    {
        const string ScenariosDir = "Assets/Scenarios";

        // liquid 的子命令与液体类型的中文别名（英文名走 VNLiquidType 枚举反射）
        static readonly HashSet<string> LiquidActions = new HashSet<string>
        {
            "splash", "spray", "click", "wet", "dry", "cover",
        };

        static readonly HashSet<string> LiquidTypeAliases = new HashSet<string>
        {
            "水", "清水", "血", "鲜血", "墨", "墨水", "黏液", "粘液",
        };

        // 内置事件模块的结果名（用于拼写校验；未知模块跳过不报）
        static readonly Dictionary<string, HashSet<string>> BuiltinOutcomes =
            new Dictionary<string, HashSet<string>>
            {
                ["qte"] = new HashSet<string> { "success", "fail" },
                ["shop"] = new HashSet<string> { "离开" },
                // questboard 的「接取」= 这次至少接下了一份委托，「离开」= 一份都没接
                ["questboard"] = new HashSet<string> { "接取", "离开" },
                ["plan"] = new HashSet<string> { "confirm", "next", "end" },
                ["result"] = new HashSet<string> { "fail", "normal", "good", "great" },
                ["battle"] = new HashSet<string> { "胜利", "失败", "逃跑" },
                ["quiz"] = new HashSet<string> { "全对", "及格", "失败" },
                // badminton 的「结束」只在 mode:free 下出现，正式赛只有胜利/失败
                ["badminton"] = new HashSet<string> { "胜利", "失败", "结束" },
                // photo 的「完成」只在自由拍照（不写 theme: 或 mode:free）下出现
                ["photo"] = new HashSet<string> { "完美", "普通", "失败", "完成" },
                // aitalk 的「失败」= 断网/无 key/被内容安全拦下，剧本必须接住
                ["aitalk"] = new HashSet<string> { "好感提升", "普通", "冷场", "失败" },
                // wipefog 的三档结果名可以在资产里改，这里只列默认值——
                // 改过名的资产会让拼写校验误报，改名时记得同步这一行
                ["wipefog"] = new HashSet<string> { "完美", "普通", "失败" },
                // map 的结果名 = 地点名，取自场景模板，运行时补
            };

        // ==============================================================
        // 剧本索引
        // ==============================================================

        class ScriptFile
        {
            public string name;         // 不含 .vn.txt
            public string assetPath;
            public TextAsset asset;
            public List<VNScriptCommand> cmds;
            public Dictionary<string, int> labels = new Dictionary<string, int>();
            public List<string> dupLabels = new List<string>();
            public HashSet<string> writtenFlags = new HashSet<string>();
        }

        class Registry
        {
            public HashSet<string> backgrounds = new HashSet<string>();
            public HashSet<string> cgs = new HashSet<string>();
            public HashSet<string> bgms = new HashSet<string>();
            public HashSet<string> ses = new HashSet<string>();
            public HashSet<string> voices = new HashSet<string>();
            public Dictionary<string, HashSet<string>> charImprints =
                new Dictionary<string, HashSet<string>>();
            public Dictionary<string, HashSet<string>> charOverlays =
                new Dictionary<string, HashSet<string>>();
            public Dictionary<string, HashSet<string>> charExpressions =
                new Dictionary<string, HashSet<string>>();
            public HashSet<string> eventModules = new HashSet<string>();
            public HashSet<string> mapLocations = new HashSet<string>();
            public HashSet<string> quizIds = new HashSet<string>();
            public HashSet<string> badmintonIds = new HashSet<string>();
            public HashSet<string> aiPersonaIds = new HashSet<string>();
            public HashSet<string> fogWipeIds = new HashSet<string>();
            /// <summary>人格 id → 它绑定的角色 id（校验 vs: 和 persona: 是否对得上）</summary>
            public Dictionary<string, string> aiPersonaCharacter =
                new Dictionary<string, string>();
            public HashSet<string> photoThemeIds = new HashSet<string>();
            public HashSet<string> photoFrameIds = new HashSet<string>();
            public HashSet<string> photoBackdropIds = new HashSet<string>();
            public HashSet<string> weatherIds = new HashSet<string>();
            public HashSet<string> interludeIds = new HashSet<string>();
            public HashSet<string> tutorialIds = new HashSet<string>();
            public HashSet<string> dialogueSkins = new HashSet<string>();
            public HashSet<string> choiceSkins = new HashSet<string>();
            // 任务系统：条件与奖励里的 id 全靠这几张表兜住拼写错误
            public List<VNQuestDef> questDefs = new List<VNQuestDef>();
            public HashSet<string> questIds = new HashSet<string>();
            public HashSet<string> statIds = new HashSet<string>();
            public HashSet<string> shopItemIds = new HashSet<string>();
            public List<VNTrackerEntry> trackers = new List<VNTrackerEntry>();
            public bool sceneRegistryFound;   // 场景里有没有 VNEventRegistry
        }

        // ==============================================================
        // 入口
        // ==============================================================

        public static List<VNLintIssue> LintAll()
        {
            var issues = new List<VNLintIssue>();
            var files = LoadScripts(issues);
            if (files.Count == 0)
            {
                issues.Add(new VNLintIssue
                {
                    severity = VNLintSeverity.Info,
                    code = "no-scripts",
                    file = "-",
                    line = 0,
                    message = $"{ScenariosDir} 下没有找到任何 .vn.txt",
                    hint = "剧本必须放在这个目录才会被自动登记为章节。",
                });
                return issues;
            }

            var reg = CollectRegistry();

            foreach (var f in files)
            {
                CheckLabels(f, issues);
                CheckParams(f, issues);
                CheckAssets(f, reg, issues);
                CheckChoices(f, issues);
                CheckEvents(f, reg, issues);
                CheckSns(f, reg, issues);
            }

            // 跨文件检查（需要全量索引）
            CheckQuests(files, reg, issues);
            CheckEmptyLibraries(files, reg, issues);
            CheckDuplicateNames(files, issues);
            CheckJumpTargets(files, issues);
            CheckCallContracts(files, issues);
            CheckSubroutineReturns(files, issues);
            CheckLoopGuards(files, issues);
            CheckUnreferencedLabels(files, issues);

            return issues
                .OrderBy(i => i.severity)
                .ThenBy(i => i.file)
                .ThenBy(i => i.line)
                .ToList();
        }

        static List<ScriptFile> LoadScripts(List<VNLintIssue> issues)
        {
            var result = new List<ScriptFile>();
            if (!AssetDatabase.IsValidFolder(ScenariosDir)) return result;

            var paths = AssetDatabase.FindAssets("t:TextAsset", new[] { ScenariosDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".vn.txt"))
                .OrderBy(p => p);

            foreach (string path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (asset == null) continue;

                var f = new ScriptFile
                {
                    name = System.IO.Path.GetFileName(path).Replace(".vn.txt", ""),
                    assetPath = path,
                    asset = asset,
                    // ★ 复用运行时解析器：校验器与 Runner 看到的命令流完全一致
                    cmds = VNScriptParser.Parse(asset.text),
                };

                for (int i = 0; i < f.cmds.Count; i++)
                {
                    var c = f.cmds[i];
                    if (c.keyword == "label")
                    {
                        string n = c.Arg(0);
                        if (string.IsNullOrEmpty(n)) continue;
                        if (f.labels.ContainsKey(n)) f.dupLabels.Add(n + "@" + c.line);
                        else f.labels[n] = i;
                    }
                    else if (c.keyword == "flag" || c.keyword == "stat")
                    {
                        string n = c.Arg(0);
                        if (!string.IsNullOrEmpty(n)) f.writtenFlags.Add(n);
                    }
                }
                result.Add(f);
            }
            return result;
        }

        /// <summary>
        /// 已登记素材：以 VNGameConfig 资产为准，并与**当前打开场景**里的组件取并集
        /// （有人可能还在用旧的场景内配置方式，不该因此被误报）。
        /// </summary>
        static Registry CollectRegistry()
        {
            var reg = new Registry();

            var cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(VNGameConfig.AssetPath);
            if (cfg == null)
            {
                var found = AssetDatabase.FindAssets("t:VNGameConfig");
                if (found.Length > 0)
                    cfg = AssetDatabase.LoadAssetAtPath<VNGameConfig>(
                        AssetDatabase.GUIDToAssetPath(found[0]));
            }
            if (cfg != null)
            {
                foreach (var b in cfg.backgrounds) if (b != null) reg.backgrounds.Add(b.id);
                foreach (var c in cfg.cgLibrary) if (c != null) reg.cgs.Add(c.id);
                foreach (var a in cfg.bgmLibrary) if (a != null) reg.bgms.Add(a.id);
                foreach (var a in cfg.seLibrary) if (a != null) reg.ses.Add(a.id);
                foreach (var a in cfg.voiceLibrary) if (a != null) reg.voices.Add(a.id);
                foreach (var l in cfg.mapLocations) if (l != null) reg.mapLocations.Add(l.name);
                foreach (var s in cfg.dialogueSkins)
                    if (s != null && !string.IsNullOrEmpty(s.id)) reg.dialogueSkins.Add(s.id);
                foreach (var s in cfg.choiceSkins)
                    if (s != null && !string.IsNullOrEmpty(s.id)) reg.choiceSkins.Add(s.id);
                foreach (var d in cfg.interludes)
                {
                    if (d == null) continue;
                    // id 留空按资产文件名认，与运行时 VNScriptRunner.FindInterlude 一致
                    reg.interludeIds.Add(string.IsNullOrEmpty(d.id) ? d.name : d.id);
                }
                foreach (var d in cfg.tutorials)
                {
                    if (d == null) continue;
                    reg.tutorialIds.Add(string.IsNullOrEmpty(d.id) ? d.name : d.id);
                }
                foreach (var q in cfg.quests)
                    if (q != null && !string.IsNullOrEmpty(q.id)) reg.questDefs.Add(q);
                foreach (var s in cfg.stats)
                    if (s != null && !string.IsNullOrEmpty(s.id)) reg.statIds.Add(s.id);
                if (cfg.trackers != null)
                    foreach (var t in cfg.trackers)
                        if (t != null && !string.IsNullOrEmpty(t.sourceFlag)) reg.trackers.Add(t);
            }

            // 任务与属性：配置里没登记就退回扫全工程资产（同角色定义的做法，最可靠）
            if (reg.questDefs.Count == 0)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:VNQuestDef"))
                {
                    var q = AssetDatabase.LoadAssetAtPath<VNQuestDef>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (q != null && !string.IsNullOrEmpty(q.id)) reg.questDefs.Add(q);
                }
            }
            foreach (var q in reg.questDefs) reg.questIds.Add(q.id);

            if (reg.statIds.Count == 0)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:VNStatDef"))
                {
                    var s = AssetDatabase.LoadAssetAtPath<VNStatDef>(
                        AssetDatabase.GUIDToAssetPath(guid));
                    if (s != null && !string.IsNullOrEmpty(s.id)) reg.statIds.Add(s.id);
                }
            }

            // 商店商品：奖励发的道具要能对上（道具 id 分散在各 VNShopDef 里）
            foreach (string guid in AssetDatabase.FindAssets("t:VNShopDef"))
            {
                var shop = AssetDatabase.LoadAssetAtPath<VNShopDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (shop == null || shop.items == null) continue;
                foreach (var item in shop.items)
                    if (item != null && !string.IsNullOrEmpty(item.id)) reg.shopItemIds.Add(item.id);
            }

            var stage = Object.FindFirstObjectByType<VNStage>(FindObjectsInactive.Include);
            if (stage != null)
            {
                foreach (var b in stage.backgrounds) if (b != null) reg.backgrounds.Add(b.id);
                foreach (var c in stage.cgLibrary) if (c != null) reg.cgs.Add(c.id);
            }
            var audio = Object.FindFirstObjectByType<VNAudio>(FindObjectsInactive.Include);
            if (audio != null)
            {
                foreach (var a in audio.bgmLibrary) if (a != null) reg.bgms.Add(a.id);
                foreach (var a in audio.seLibrary) if (a != null) reg.ses.Add(a.id);
                foreach (var a in audio.voiceLibrary) if (a != null) reg.voices.Add(a.id);
            }

            // 角色定义永远扫资产（与场景无关，最可靠）
            foreach (string guid in AssetDatabase.FindAssets("t:VNCharacterDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNCharacterDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                var set = new HashSet<string>();
                foreach (var e in def.expressions)
                    if (e != null && !string.IsNullOrEmpty(e.name)) set.Add(e.name);
                reg.charExpressions[def.id] = set;

                var layers = new HashSet<string>();
                if (def.overlays != null)
                    foreach (var o in def.overlays)
                        if (o != null && !string.IsNullOrEmpty(o.id)) layers.Add(o.id);
                reg.charOverlays[def.id] = layers;

                var traceIds = new HashSet<string>();
                if (def.imprints != null)
                    foreach (var im in def.imprints)
                        if (im != null && !string.IsNullOrEmpty(im.id)) traceIds.Add(im.id);
                reg.charImprints[def.id] = traceIds;
            }

            var registry = Object.FindFirstObjectByType<VNEventRegistry>(FindObjectsInactive.Include);
            if (registry != null)
            {
                reg.sceneRegistryFound = true;
                foreach (var m in registry.modules)
                    if (m != null && !string.IsNullOrEmpty(m.id)) reg.eventModules.Add(m.id);
            }
            var map = Object.FindFirstObjectByType<VNMapModule>(FindObjectsInactive.Include);
            if (map != null)
                foreach (var l in map.locations)
                    if (l != null && !string.IsNullOrEmpty(l.name)) reg.mapLocations.Add(l.name);

            // 题库 id：扫资产而不是读场景模板——新建题库后不必重建场景就能被校验到
            foreach (var guid in AssetDatabase.FindAssets("t:VNQuizDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNQuizDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.quizId)) reg.quizIds.Add(def.quizId);
            }

            // 羽球对手 id：同理扫资产，新建对手后不必重建场景就能被校验到
            foreach (var guid in AssetDatabase.FindAssets("t:VNBadmintonDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNBadmintonDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.badmintonId))
                    reg.badmintonIds.Add(def.badmintonId);
            }

            // 擦雾定义 id：同理扫资产，新建后不必重建场景就能被校验到
            foreach (var guid in AssetDatabase.FindAssets("t:VNFogWipeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNFogWipeDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.fogWipeId))
                    reg.fogWipeIds.Add(def.fogWipeId);
            }

            // AI 人格 id：同理扫资产，新建人格后不必重建场景就能被校验到
            foreach (var guid in AssetDatabase.FindAssets("t:VNAiPersonaDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNAiPersonaDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                reg.aiPersonaIds.Add(def.id);
                if (def.character != null && !string.IsNullOrEmpty(def.character.id))
                    reg.aiPersonaCharacter[def.id] = def.character.id;
            }

            // 大头贴的主题与边框 id：同理扫资产
            foreach (var guid in AssetDatabase.FindAssets("t:VNPhotoThemeDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNPhotoThemeDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.themeId))
                    reg.photoThemeIds.Add(def.themeId);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:VNPhotoFrameDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNPhotoFrameDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.frameId))
                    reg.photoFrameIds.Add(def.frameId);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:VNPhotoBackdropDef"))
            {
                var def = AssetDatabase.LoadAssetAtPath<VNPhotoBackdropDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def != null && !string.IsNullOrEmpty(def.backdropId))
                    reg.photoBackdropIds.Add(def.backdropId);
            }

            // 自定义飘落天气资产（内置叶型别名与 VNWeather 枚举另行判定，不进这个集合）
            foreach (var guid in AssetDatabase.FindAssets("t:VNWeatherDef"))
            {
                var wd = AssetDatabase.LoadAssetAtPath<VNWeatherDef>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (wd != null && !string.IsNullOrEmpty(wd.id)) reg.weatherIds.Add(wd.id);
            }

            return reg;
        }

        // ==============================================================
        // 工具
        // ==============================================================

        /// <summary>含 ${参数} 的 token 在运行时才确定，静态期跳过不报</summary>
        static bool Dynamic(string s) => !string.IsNullOrEmpty(s) && s.Contains("${");

        static int LastJumpIndex(VNScriptCommand c)
        {
            for (int i = c.args.Count - 2; i >= 1; i--)
                if (c.args[i] == "jump") return i;
            return -1;
        }

        /// <summary>拼写建议：找编辑距离最近的已有 label</summary>
        static string Suggest(string wrong, IEnumerable<string> candidates)
        {
            string best = null;
            int bestDist = int.MaxValue;
            foreach (string c in candidates)
            {
                int d = Distance(wrong, c);
                if (d < bestDist) { bestDist = d; best = c; }
            }
            // 差太多就别乱猜
            if (best != null && bestDist <= Mathf.Max(2, wrong.Length / 3))
                return $"是不是想写「{best}」？";
            return "检查 label 拼写，或确认目标文件写对了。";
        }

        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Mathf.Min(
                        Mathf.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

    }
}
