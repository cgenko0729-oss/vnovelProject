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
    public static class VNScenarioLinter
    {
        const string ScenariosDir = "Assets/Scenarios";

        // emote 的动作是**英文枚举**，与角色资产里的中文表情名是两回事（易混）
        static readonly HashSet<string> EmoteActions = new HashSet<string>
        {
            "Surprise", "Angry", "Shy", "Dejected", "Recover", "Nod", "HeadShake",
        };

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
                ["plan"] = new HashSet<string> { "confirm", "next", "end" },
                ["result"] = new HashSet<string> { "fail", "normal", "good", "great" },
                ["battle"] = new HashSet<string> { "胜利", "失败", "逃跑" },
                ["quiz"] = new HashSet<string> { "全对", "及格", "失败" },
                // badminton 的「结束」只在 mode:free 下出现，正式赛只有胜利/失败
                ["badminton"] = new HashSet<string> { "胜利", "失败", "结束" },
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
            public Dictionary<string, HashSet<string>> charExpressions =
                new Dictionary<string, HashSet<string>>();
            public HashSet<string> eventModules = new HashSet<string>();
            public HashSet<string> mapLocations = new HashSet<string>();
            public HashSet<string> quizIds = new HashSet<string>();
            public HashSet<string> badmintonIds = new HashSet<string>();
            public HashSet<string> weatherIds = new HashSet<string>();
            public HashSet<string> dialogueSkins = new HashSet<string>();
            public HashSet<string> choiceSkins = new HashSet<string>();
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
            CheckEmptyLibraries(files, reg, issues);
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
        // 规则：整个库为空
        // ==============================================================

        /// <summary>
        /// 逐条报"未登记的音效 X"在库整个都空的时候会刷屏，所以那种情况下逐条检查会跳过；
        /// 但"库是空的而剧本引用了 N 个 id"本身就是**最该被发现的问题**（全部静音/不显示），
        /// 所以在这里汇总成一条高信噪比的提示。
        /// </summary>
        static void CheckEmptyLibraries(List<ScriptFile> files, Registry reg,
            List<VNLintIssue> issues)
        {
            var se = new HashSet<string>();
            var bgm = new HashSet<string>();
            var voice = new HashSet<string>();
            var bg = new HashSet<string>();
            var cg = new HashSet<string>();
            bool usesMap = false;

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    switch (c.keyword)
                    {
                        case "se":
                            Collect(se, c.Arg(0) == "stop" ? c.Arg(1) : c.Arg(0)); break;
                        case "bgm":
                            if (c.Arg(0) == "play") Collect(bgm, c.Arg(1)); break;
                        case "voice": Collect(voice, c.Arg(0)); break;
                        case "bg": Collect(bg, c.Arg(0)); break;
                        case "cg": if (c.Arg(0) != "off") Collect(cg, c.Arg(0)); break;
                        case "event": if (c.Arg(0) == "map") usesMap = true; break;
                    }
                }

            Report(issues, files, se, reg.ses, "音效（SE）", "Se Library", "全部静音");
            Report(issues, files, bgm, reg.bgms, "BGM", "Bgm Library", "全部静音");
            Report(issues, files, voice, reg.voices, "语音", "Voice Library", "全部静音");
            Report(issues, files, bg, reg.backgrounds, "背景", "Backgrounds", "背景不会切换");
            Report(issues, files, cg, reg.cgs, "CG", "Cg Library", "CG 不会显示");

            if (usesMap && reg.mapLocations.Count == 0)
                issues.Add(new VNLintIssue
                {
                    severity = VNLintSeverity.Warning,
                    code = "empty-map",
                    file = "（全局）",
                    assetPath = VNGameConfig.AssetPath,
                    line = 0,
                    message = "剧本用了 event map，但一个地点都没配",
                    hint = "在 VNGameConfig 的 Map Locations 里配地点（名字 = 剧本结果名，" +
                           "坐标 0~1）。没有地点时事件会直接返回、剧本相当于跳过这段。",
                });
        }

        static void Collect(HashSet<string> set, string id)
        {
            if (!string.IsNullOrEmpty(id) && !Dynamic(id)) set.Add(id);
        }

        static void Report(List<VNLintIssue> issues, List<ScriptFile> files,
            HashSet<string> used, HashSet<string> registered, string what, string field,
            string consequence)
        {
            if (used.Count == 0 || registered.Count > 0) return;
            issues.Add(new VNLintIssue
            {
                severity = VNLintSeverity.Warning,
                code = "empty-library",
                file = "（全局）",
                assetPath = VNGameConfig.AssetPath,
                line = 0,
                message = $"{what}库是空的，但剧本引用了 {used.Count} 个 id → {consequence}",
                hint = $"用到的 id：{string.Join("、", used.OrderBy(s => s).Take(12))}" +
                       (used.Count > 12 ? " …" : "") +
                       $"。在 VNGameConfig 的 {field} 里登记。",
            });
        }

        // ==============================================================
        // 规则：label
        // ==============================================================

        static void CheckLabels(ScriptFile f, List<VNLintIssue> issues)
        {
            foreach (string dup in f.dupLabels)
            {
                int at = dup.LastIndexOf('@');
                string name = dup.Substring(0, at);
                int line = int.Parse(dup.Substring(at + 1));
                Add(issues, VNLintSeverity.Error, "dup-label", f, line,
                    $"label「{name}」重名",
                    "同一文件里 label 必须唯一，否则跳转目标不确定。改名或删掉其中一个。");
            }
        }

        static void CheckParams(ScriptFile f, List<VNLintIssue> issues)
        {
            for (int i = 0; i < f.cmds.Count; i++)
            {
                if (f.cmds[i].keyword != "params") continue;
                // params 必须紧跟 label（中间不能有别的命令）
                if (i == 0 || f.cmds[i - 1].keyword != "label")
                    Add(issues, VNLintSeverity.Error, "params-not-first", f, f.cmds[i].line,
                        "params 没有紧跟在 label 后面",
                        "params 必须是目标 label 之后的第一条有效命令，否则参数不会被绑定。");
            }
        }

        // ==============================================================
        // 规则：资产引用
        // ==============================================================

        static void CheckAssets(ScriptFile f, Registry reg, List<VNLintIssue> issues)
        {
            foreach (var c in f.cmds)
            {
                switch (c.keyword)
                {
                    case "bg":
                        CheckId(issues, f, c.line, c.Arg(0), reg.backgrounds, "背景",
                            "unknown-bg", "在 VNGameConfig 的 Backgrounds 里登记 id→图。");
                        break;

                    case "cg":
                        if (c.Arg(0) != "off")
                            CheckId(issues, f, c.line, c.Arg(0), reg.cgs, "CG",
                                "unknown-cg", "把图放进 Assets/CG（文件名=id）后跑 Rescan Asset Folders。");
                        break;

                    case "bgm":
                        if (c.Arg(0) == "play")
                            CheckId(issues, f, c.line, c.Arg(1), reg.bgms, "BGM",
                                "unknown-bgm", "在 VNGameConfig 的 Bgm Library 里登记。");
                        break;

                    case "se":
                        // se <id> [loop] / se stop <id>
                        CheckId(issues, f, c.line, c.Arg(0) == "stop" ? c.Arg(1) : c.Arg(0),
                            reg.ses, "音效", "unknown-se",
                            "在 VNGameConfig 的 Se Library 里登记。");
                        break;

                    case "voice":
                        CheckId(issues, f, c.line, c.Arg(0), reg.voices, "语音",
                            "unknown-voice", "在 VNGameConfig 的 Voice Library 里登记。");
                        break;

                    case "ui":
                    {
                        // ui dialogue|choice <id|default>；default 永远合法
                        string kind = c.Arg(0);
                        string skinId = c.Arg(1, "default");
                        if (kind != "dialogue" && kind != "choice")
                        {
                            Add(issues, VNLintSeverity.Error, "bad-ui-kind", f, c.line,
                                $"ui 命令的对象「{kind}」不认识",
                                "用法：ui dialogue|choice <皮肤id|default>。");
                        }
                        else if (skinId != "default" && !Dynamic(skinId))
                        {
                            var known = kind == "dialogue" ? reg.dialogueSkins : reg.choiceSkins;
                            if (!known.Contains(skinId))
                                Add(issues, VNLintSeverity.Warning, "unknown-ui-skin", f, c.line,
                                    $"UI 皮肤「{skinId}」未登记",
                                    $"在 VNGameConfig 的 {(kind == "dialogue" ? "Dialogue" : "Choice")} " +
                                    "Skins 里登记 id→prefab；运行时未登记的皮肤会报错并保持现状。");
                        }
                        break;
                    }

                    case "show":
                    case "hide":
                    case "move":
                        CheckCharacter(issues, f, c.line, c.Arg(0), reg);
                        if (c.keyword == "show")
                        {
                            CheckExpression(issues, f, c.line, c.Arg(0), c.Kw("expr"), reg);
                            CheckEnum<VNEntrancePreset>(issues, f, c.line, c.Kw("with"),
                                "show 的登场预设");
                            CheckSide(issues, f, c.line, c.Kw("from"), "show 的 from:");
                        }
                        else if (c.keyword == "hide")
                        {
                            CheckEnum<VNExitPreset>(issues, f, c.line, c.Kw("with"),
                                "hide 的退场预设");
                            CheckSide(issues, f, c.line, c.Kw("to"), "hide 的 to:");
                        }
                        break;

                    case "mark":
                    {
                        CheckCharacter(issues, f, c.line, c.Arg(0), reg);
                        string symbol = c.Arg(1);
                        if (!string.IsNullOrEmpty(symbol) && !Dynamic(symbol) &&
                            symbol != "clear" && !VNCharacterMarks.TryParse(symbol, out _))
                            Add(issues, VNLintSeverity.Error, "bad-mark", f, c.line,
                                $"漫符「{symbol}」不是合法符号名",
                                "可用英文正名 " + string.Join("/", VNCharacterMarks.CanonicalNames) +
                                "，或中文别名（汗/怒/惊/问/心/音符/红晕/灯泡/省略/眩晕/蒸汽）；" +
                                "清空全部写 `mark <角色> clear`。");

                        string markMode = c.Arg(2);
                        if (!string.IsNullOrEmpty(markMode) && !Dynamic(markMode) &&
                            markMode != "keep" && markMode != "off")
                            Add(issues, VNLintSeverity.Error, "bad-mark-mode", f, c.line,
                                $"mark 的第三个参数「{markMode}」不合法",
                                "第三个参数只能是 keep（常驻）或 off（移除该符号），留空 = 弹一下就消失。");
                        break;
                    }

                    case "weather":
                    {
                        // 认不出的天气 id 运行时会静默变成「无天气」——画面少了一整层演出
                        // 而没有任何报错，所以这里必须提前抓
                        string wid = c.Arg(0);
                        if (!string.IsNullOrEmpty(wid) && !Dynamic(wid) &&
                            !wid.Equals("none", System.StringComparison.OrdinalIgnoreCase) &&
                            !wid.Equals("off", System.StringComparison.OrdinalIgnoreCase) &&
                            !reg.weatherIds.Contains(wid) &&
                            VNWeatherDef.ParseBuiltinId(wid) == null &&
                            !System.Enum.TryParse<VNWeather>(wid, true, out _))
                            Add(issues, VNLintSeverity.Error, "unknown-weather", f, c.line,
                                $"天气「{wid}」不认识",
                                "飘落类可写 petals/sakura/落樱、maple/枫叶、ginkgo/银杏、" +
                                "leaves/落叶、bamboo/竹叶；其余写 Rain / Snow / Fireflies / None；" +
                                "自定义参数资产要在 VNGameConfig 的飘落天气库里登记 id。");
                        break;
                    }

                    case "liquid":
                    {
                        // 子命令拼错时运行时只有一条 Console 警告，画面上什么都不会发生
                        string act = c.Arg(0, "splash");
                        if (!Dynamic(act) && !LiquidActions.Contains(act))
                            Add(issues, VNLintSeverity.Error, "bad-liquid-action", f, c.line,
                                $"liquid 子命令「{act}」不认识",
                                "可用 splash（一次性爆溅）/ spray（间歇喷射开关）/ " +
                                "click（点击喷水模式）/ wet（常驻湿镜头）/ dry（擦干）/ " +
                                "cover（水渍盖不盖对话框）。");

                        string ltype = c.Kw("type");
                        if (!string.IsNullOrEmpty(ltype) && !Dynamic(ltype) &&
                            !System.Enum.TryParse<VNLiquidType>(ltype, true, out _) &&
                            !LiquidTypeAliases.Contains(ltype))
                            Add(issues, VNLintSeverity.Error, "bad-liquid-type", f, c.line,
                                $"液体类型「{ltype}」不认识",
                                "可写 water / blood / ink / slime，或中文 水 / 血 / 墨 / 黏液。");

                        // x/y 是屏幕比例 0~1，按像素写（如 x:960）会把喷射点甩到画面外，
                        // 运行时完全没有提示，只会看到"命令好像没生效"
                        CheckLiquidRange(issues, f, c, "x");
                        CheckLiquidRange(issues, f, c, "y");

                        // spray/click/wet/cover 是开关型，第二个参数必须写 on 或 off；
                        // 省略视作 on，写别的（比如 start / true 之外的词）会被当成 on 而无声吞掉
                        string sw = c.Arg(1);
                        if (!string.IsNullOrEmpty(sw) && !Dynamic(sw) &&
                            sw != "on" && sw != "off" && sw != "true" && sw != "false" &&
                            sw != "0" && sw != "1")
                            Add(issues, VNLintSeverity.Warning, "bad-liquid-switch", f, c.line,
                                $"liquid 的开关位「{sw}」不是 on/off",
                                "第二个位置参数只能是 on 或 off（省略 = on）；" +
                                "其它参数一律写成 key:value，比如 power:1.5 type:blood。");
                        break;
                    }

                    case "emote":
                        CheckCharacter(issues, f, c.line, c.Arg(0), reg);
                        string action = c.Arg(1);
                        if (!string.IsNullOrEmpty(action) && !Dynamic(action) &&
                            !EmoteActions.Contains(action))
                            Add(issues, VNLintSeverity.Error, "bad-emote", f, c.line,
                                $"emote 动作「{action}」不是合法枚举",
                                "emote 用**英文**动作名（Surprise/Angry/Shy/Dejected/Recover/Nod/" +
                                "HeadShake）；中文的是角色资产里的**表情名**，那是另一回事" +
                                "（表情写成 `角色 表情: 台词` 或 show 的 expr:）。");
                        break;

                    case "say":
                        // 说话者可以不是注册角色（旁白/我/路人），这是合法写法，不报。
                        // 但如果**是**注册角色，表情就必须存在。
                        if (!string.IsNullOrEmpty(c.speaker) &&
                            reg.charExpressions.ContainsKey(c.speaker))
                            CheckExpression(issues, f, c.line, c.speaker, c.expression, reg);
                        break;
                }
            }
        }

        /// <summary>
        /// 枚举值校验：拼错时运行时只会告警并静默退回默认预设，
        /// 演出变了但没人发现——所以这里提前抓。大小写不敏感（同运行时 ParseEnum）。
        /// </summary>
        static void CheckEnum<T>(List<VNLintIssue> issues, ScriptFile f, int line,
            string value, string what) where T : struct
        {
            if (string.IsNullOrEmpty(value) || Dynamic(value)) return;
            if (System.Enum.TryParse<T>(value, true, out _)) return;
            Add(issues, VNLintSeverity.Error, "bad-preset", f, line,
                $"{what}「{value}」不是合法预设",
                $"可用：{string.Join(" / ", System.Enum.GetNames(typeof(T)))}" +
                "（大小写随意）。拼错时运行时会静默退回默认预设。");
        }

        static readonly HashSet<string> SideNames =
            new HashSet<string> { "left", "right", "top", "bottom", "auto" };

        static void CheckSide(List<VNLintIssue> issues, ScriptFile f, int line,
            string value, string what)
        {
            if (string.IsNullOrEmpty(value) || Dynamic(value)) return;
            if (SideNames.Contains(value.ToLower())) return;
            Add(issues, VNLintSeverity.Error, "bad-side", f, line,
                $"{what}「{value}」不是合法方向",
                "可用 left / right / top / bottom；留空 = 按角色站位自动推断。");
        }

        static void CheckId(List<VNLintIssue> issues, ScriptFile f, int line, string id,
            HashSet<string> known, string label, string code, string hint)
        {
            if (string.IsNullOrEmpty(id) || Dynamic(id)) return;
            // 整个库都空 = 还没开始配素材，逐条报没有意义，交给汇总提示
            if (known.Count == 0) return;
            if (known.Contains(id)) return;
            Add(issues, VNLintSeverity.Warning, code, f, line,
                $"未登记的{label}「{id}」", hint);
        }

        /// <summary>
        /// liquid 的 x/y 是屏幕比例（0~1）。写成像素（x:960）是最容易犯的错，
        /// 而且运行时喷射点直接飞到画面外，什么提示都没有，只会以为命令没生效。
        /// </summary>
        static void CheckLiquidRange(List<VNLintIssue> issues, ScriptFile f,
            VNScriptCommand c, string key)
        {
            string raw = c.Kw(key);
            if (string.IsNullOrEmpty(raw) || Dynamic(raw)) return;
            if (!float.TryParse(raw, out float v)) return;
            if (v >= -0.5f && v <= 1.5f) return;
            Add(issues, VNLintSeverity.Warning, "liquid-coord-range", f, c.line,
                $"liquid 的 {key}:{raw} 超出屏幕比例范围",
                "x/y 用 0~1 的屏幕比例，不是像素：画面正中是 x:0.5 y:0.5，" +
                "左下角是 x:0 y:0。");
        }

        static void CheckCharacter(List<VNLintIssue> issues, ScriptFile f, int line,
            string id, Registry reg)
        {
            if (string.IsNullOrEmpty(id) || Dynamic(id)) return;
            if (reg.charExpressions.Count == 0) return;
            if (reg.charExpressions.ContainsKey(id)) return;
            Add(issues, VNLintSeverity.Warning, "unknown-character", f, line,
                $"未定义的角色「{id}」",
                "在 Assets/VNEffects/Characters/ 建 VNCharacterDef 资产（id 要一致），" +
                "然后跑 Game Config → Rescan Asset Folders。");
        }

        static void CheckExpression(List<VNLintIssue> issues, ScriptFile f, int line,
            string charId, string expr, Registry reg)
        {
            if (string.IsNullOrEmpty(expr) || Dynamic(expr) || Dynamic(charId)) return;
            if (!reg.charExpressions.TryGetValue(charId, out var set)) return;
            if (set.Count == 0 || set.Contains(expr)) return;
            Add(issues, VNLintSeverity.Warning, "unknown-expression", f, line,
                $"角色「{charId}」没有表情「{expr}」",
                $"该角色已有表情：{string.Join(" / ", set.OrderBy(s => s))}。" +
                "在角色资产的 expressions 列表里补，或改用已有的表情名。");
        }

        // ==============================================================
        // 规则：choice
        // ==============================================================

        static void CheckChoices(ScriptFile f, List<VNLintIssue> issues)
        {
            foreach (var c in f.cmds)
            {
                if (c.keyword != "choice" || c.options == null) continue;

                if (c.options.Count == 0)
                {
                    Add(issues, VNLintSeverity.Error, "empty-choice", f, c.line,
                        "choice 下面没有任何 * 选项行",
                        "choice 的下一行起用 `* 文本 [-> 标签]` 列出选项。");
                    continue;
                }

                // 所有选项都带 if: → 条件同时不满足时会一个都不显示
                if (c.options.All(o => !string.IsNullOrEmpty(o.condition)))
                    Add(issues, VNLintSeverity.Warning, "all-options-conditional", f, c.line,
                        "这组 choice 的每个选项都有 if: 条件",
                        "所有条件同时不满足时玩家会看到空选单、流程卡死。" +
                        "留一个无条件的保底选项。");

                foreach (var o in c.options)
                {
                    // ★ 选项 flag: 只吃一个无空格 token，写不了「flag:名 值」——
                    //   多出来的值会被当成选项文字，变量根本不会被写（静默失败）。
                    if (!string.IsNullOrEmpty(o.flagOp)) continue;
                    if (string.IsNullOrEmpty(o.text)) continue;

                    var m = System.Text.RegularExpressions.Regex.Match(
                        o.text, @"flag:(\S+)\s+(-?\d+)\s*$");
                    if (m.Success)
                        Add(issues, VNLintSeverity.Error, "choice-flag-assign", f, o.line,
                            $"选项里的「flag:{m.Groups[1].Value} {m.Groups[2].Value}」不会生效",
                            "选项 flag: 只支持 `名字` / `名字+n` / `名字-n`，**不能赋任意值**；" +
                            $"多出来的「{m.Groups[2].Value}」被当成了选项文字。" +
                            "改成：选项只写 `-> 标签`，把 " +
                            $"`flag {m.Groups[1].Value} {m.Groups[2].Value}` 放进目标 label。");
                }
            }
        }

        // ==============================================================
        // 规则：event
        // ==============================================================

        static void CheckEvents(ScriptFile f, Registry reg, List<VNLintIssue> issues)
        {
            foreach (var c in f.cmds)
            {
                if (c.keyword != "event") continue;
                string module = c.Arg(0);
                if (string.IsNullOrEmpty(module) || Dynamic(module)) continue;

                if (reg.sceneRegistryFound && !reg.eventModules.Contains(module))
                {
                    Add(issues, VNLintSeverity.Warning, "unknown-event-module", f, c.line,
                        $"未注册的事件模块「{module}」",
                        "模块要登记进场景 VNEventRegistry 的 modules 列表。" +
                        $"当前已注册：{string.Join(" / ", reg.eventModules.OrderBy(s => s))}");
                    continue;
                }

                // quiz 的题库 id 拼错 = 事件直接返回空结果，整段问答被静默跳过
                if (module == "quiz")
                {
                    string quizId = c.Kw("id");
                    if (!string.IsNullOrEmpty(quizId) && !Dynamic(quizId) &&
                        reg.quizIds.Count > 0 && !reg.quizIds.Contains(quizId))
                        Add(issues, VNLintSeverity.Warning, "unknown-quiz", f, c.line,
                            $"没有 id 为「{quizId}」的题库资产",
                            "题库是 VN/Quiz Definition 资产（Assets/VNEffects/Quizzes），" +
                            $"quizId 要和剧本写的一致。当前已有：" +
                            $"{string.Join(" / ", reg.quizIds.OrderBy(s => s))}");
                }

                // badminton 的对手 id 拼错 = 静默退回兜底难度，一局白打
                if (module == "badminton")
                {
                    string defId = c.Kw("id");
                    if (!string.IsNullOrEmpty(defId) && !Dynamic(defId) &&
                        reg.badmintonIds.Count > 0 && !reg.badmintonIds.Contains(defId))
                        Add(issues, VNLintSeverity.Warning, "unknown-badminton", f, c.line,
                            $"没有 id 为「{defId}」的羽球对手资产",
                            "对手是 VN/Badminton Definition 资产（Assets/VNEffects/Badminton），" +
                            "badmintonId 要和剧本写的一致；拼错只会静默退回兜底难度。" +
                            $"当前已有：{string.Join(" / ", reg.badmintonIds.OrderBy(s => s))}");
                }

                if (c.options == null || c.options.Count == 0) continue;

                // 结果名拼错 = 静默走顺序继续（技术债清单里记录在案的坑）
                HashSet<string> valid = null;
                if (module == "map") valid = reg.mapLocations;
                else BuiltinOutcomes.TryGetValue(module, out valid);
                if (valid == null || valid.Count == 0) continue;

                // plan 排程面板返回 confirm，op:next 返回 next/end——同模块两种用法，合并接受
                foreach (var o in c.options)
                {
                    string outcome = o.text?.Trim();
                    if (string.IsNullOrEmpty(outcome) || Dynamic(outcome)) continue;
                    if (valid.Contains(outcome)) continue;
                    Add(issues, VNLintSeverity.Warning, "bad-event-outcome", f, o.line,
                        $"模块「{module}」不会返回结果「{outcome}」",
                        $"该模块的结果名：{string.Join(" / ", valid.OrderBy(s => s))}。" +
                        "结果名匹配不上时会静默跳过该行、按顺序继续执行。");
                }
            }
        }

        // ==============================================================
        // 规则：sns 手机聊天
        // ==============================================================

        static readonly HashSet<string> SnsSubcommands = new HashSet<string>
        {
            "open", "close", "voice", "image", "typing", "read", "time", "system", "reply",
        };

        static void CheckSns(ScriptFile f, Registry reg, List<VNLintIssue> issues)
        {
            // 跨 label / 跨文件的会话结构没法静态判定，这里只做"整文件有没有 open"
            // 和"最后一次 open 之后有没有 close"这两种低误报检查。
            bool anyOpen = false, anySns = false;
            int lastOpenLine = 0;
            bool closedAfterLastOpen = true;

            foreach (var c in f.cmds)
            {
                if (c.keyword != "sns") continue;
                anySns = true;
                string sub = (c.Arg(0) ?? "").ToLower();

                if (!SnsSubcommands.Contains(sub))
                {
                    Add(issues, VNLintSeverity.Error, "bad-sns-sub", f, c.line,
                        $"sns 子命令「{c.Arg(0)}」不认识",
                        "可用：" + string.Join(" / ", SnsSubcommands.OrderBy(s => s)) + "。");
                    continue;
                }

                switch (sub)
                {
                    case "open":
                        anyOpen = true;
                        lastOpenLine = c.line;
                        closedAfterLastOpen = false;
                        if (string.IsNullOrEmpty(c.Arg(1)))
                            Add(issues, VNLintSeverity.Error, "sns-open-no-peer", f, c.line,
                                "sns open 没写对方角色 id",
                                "用法：sns open <角色id> [id:会话] [title:标题] [me:玩家说话者名]。");
                        else
                            CheckCharacter(issues, f, c.line, c.Arg(1), reg);
                        break;

                    case "close":
                        closedAfterLastOpen = true;
                        break;

                    case "voice":
                        if (string.IsNullOrEmpty(c.Arg(2)))
                            Add(issues, VNLintSeverity.Error, "sns-voice-no-id", f, c.line,
                                "sns voice 缺少语音 id",
                                "用法：sns voice <发送者> <语音id> [text:文字稿]。");
                        else
                            CheckId(issues, f, c.line, c.Arg(2), reg.voices, "语音",
                                "unknown-voice", "在 VNGameConfig 的 Voice Library 里登记。");
                        break;

                    case "image":
                        if (string.IsNullOrEmpty(c.Arg(2)))
                            Add(issues, VNLintSeverity.Error, "sns-image-no-id", f, c.line,
                                "sns image 缺少 CG id",
                                "用法：sns image <发送者> <CG id> [unlock:no]。");
                        else
                            CheckId(issues, f, c.line, c.Arg(2), reg.cgs, "CG",
                                "unknown-cg",
                                "把图放进 Assets/CG（文件名=id）后跑 Rescan Asset Folders。");
                        break;

                    case "reply":
                    {
                        if (c.options == null || c.options.Count == 0)
                        {
                            Add(issues, VNLintSeverity.Error, "empty-sns-reply", f, c.line,
                                "sns reply 下面没有任何 * 回复行",
                                "下一行起用 `* 文本 [if:条件] [flag:好感+1] [-> 标签]` 列出候选回复。");
                            break;
                        }
                        string timeout = c.Kw("timeout");
                        if (!string.IsNullOrEmpty(timeout) && string.IsNullOrEmpty(c.Kw("late")))
                            Add(issues, VNLintSeverity.Warning, "sns-timeout-no-late", f, c.line,
                                "sns reply 写了 timeout: 但没写 late:",
                                "超时（已读不回）没有去向，运行时会退回成不限时。" +
                                "补 late:<标签> 指定超时后跳哪，可再配 lateflag:好感-1。");
                        if (c.options.All(o => !string.IsNullOrEmpty(o.condition)))
                            Add(issues, VNLintSeverity.Warning, "all-replies-conditional", f, c.line,
                                "这组 sns reply 的每条回复都有 if: 条件",
                                "条件同时不满足时会没有回复可选。留一条无条件的保底回复。");
                        break;
                    }
                }
            }

            if (anySns && !anyOpen)
                Add(issues, VNLintSeverity.Error, "sns-not-open", f, 0,
                    "这个剧本用了 sns 子命令，但整份文件里没有 sns open",
                    "聊天界面必须先 `sns open <角色id>` 打开；" +
                    "若会话是在别的文件里打开的，请忽略本条。");

            if (anyOpen && !closedAfterLastOpen)
                Add(issues, VNLintSeverity.Warning, "sns-not-closed", f, lastOpenLine,
                    "这次 sns open 之后没有对应的 sns close",
                    "聊天界面会一直盖在画面上、台词继续走气泡。" +
                    "聊完记得 `sns close`（若在别的文件里关闭，请忽略本条）。");
        }

        // ==============================================================
        // 规则：跳转目标（跨文件）
        // ==============================================================

        static void CheckJumpTargets(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = files.ToDictionary(f => f.name, f => f, System.StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    switch (c.keyword)
                    {
                        case "jump":
                            Resolve(issues, byName, f, c.line, c.Arg(0), "jump");
                            break;
                        case "call":
                            Resolve(issues, byName, f, c.line, c.Arg(0), "call");
                            break;
                        case "if":
                            int at = LastJumpIndex(c);
                            if (at >= 0 && at + 1 < c.args.Count)
                                Resolve(issues, byName, f, c.line, c.args[at + 1], "if…jump");
                            break;
                        case "sns":
                            // sns reply 的 late:<标签> 是超时（已读不回）的去向
                            if (c.Arg(0) == "reply")
                                Resolve(issues, byName, f, c.line, c.Kw("late"), "sns reply late:");
                            break;
                        case "chapter":
                            string target = (c.Arg(0) ?? "").Replace(".vn.txt", "");
                            if (!string.IsNullOrEmpty(target) && !Dynamic(target) &&
                                !byName.ContainsKey(target))
                                Add(issues, VNLintSeverity.Error, "bad-chapter", f, c.line,
                                    $"chapter 目标文件「{c.Arg(0)}」不存在",
                                    $"剧本必须放在 {ScenariosDir} 下才会被登记。检查文件名拼写。");
                            break;
                    }

                    if (c.options != null)
                        foreach (var o in c.options)
                            if (!string.IsNullOrEmpty(o.jumpLabel))
                                Resolve(issues, byName, f, o.line, o.jumpLabel, "选项 ->");
                }
        }

        static void Resolve(List<VNLintIssue> issues, Dictionary<string, ScriptFile> byName,
            ScriptFile from, int line, string address, string what)
        {
            if (string.IsNullOrEmpty(address) || Dynamic(address)) return;

            if (!VNStoryAddress.TryParse(address, out string file, out string label, out string err))
            {
                Add(issues, VNLintSeverity.Error, "bad-address", from, line,
                    $"{what} 地址「{address}」无效：{err}", "格式是 `标签` 或 `文件::标签`。");
                return;
            }

            ScriptFile target = from;
            if (!string.IsNullOrEmpty(file))
            {
                string key = file.Replace(".vn.txt", "");
                if (!byName.TryGetValue(key, out target))
                {
                    Add(issues, VNLintSeverity.Error, "dangling-jump", from, line,
                        $"{what} 的目标文件「{file}」不存在",
                        $"剧本必须放在 {ScenariosDir} 下。检查文件名拼写。");
                    return;
                }
            }

            if (!target.labels.ContainsKey(label))
                Add(issues, VNLintSeverity.Error, "dangling-jump", from, line,
                    $"{what} 的目标 label「{label}」在 {target.name} 里不存在",
                    Suggest(label, target.labels.Keys));
        }

        // ==============================================================
        // 规则：call 契约（必填参数）
        // ==============================================================

        static void CheckCallContracts(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = files.ToDictionary(f => f.name, f => f, System.StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "call") continue;
                    string address = c.Arg(0);
                    if (string.IsNullOrEmpty(address) || Dynamic(address)) continue;
                    if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                        continue;

                    ScriptFile target = f;
                    if (!string.IsNullOrEmpty(file) &&
                        !byName.TryGetValue(file.Replace(".vn.txt", ""), out target)) continue;
                    if (!target.labels.TryGetValue(label, out int at)) continue;

                    // params 是目标 label 的下一条命令
                    int p = at + 1;
                    if (p >= target.cmds.Count || target.cmds[p].keyword != "params") continue;

                    var required = new List<string>();
                    foreach (string decl in target.cmds[p].args)
                        if (!decl.Contains("=")) required.Add(decl);

                    foreach (string need in required)
                        if (!c.kwargs.ContainsKey(need))
                            Add(issues, VNLintSeverity.Error, "call-missing-arg", f, c.line,
                                $"call {address} 缺少必填参数「{need}」",
                                $"目标声明的参数：{string.Join(" ", target.cmds[p].args)}。" +
                                "少传必填参数时本次 call 会报错并跳过子程序。");

                    foreach (var kv in c.kwargs)
                        if (!target.cmds[p].args.Any(a => a == kv.Key || a.StartsWith(kv.Key + "=")))
                            Add(issues, VNLintSeverity.Warning, "call-unknown-arg", f, c.line,
                                $"call {address} 传了未声明的参数「{kv.Key}」",
                                $"目标声明的参数：{string.Join(" ", target.cmds[p].args)}。" +
                                "多传的参数会被忽略（通常是拼写错误）。");
                }
        }

        // ==============================================================
        // 规则：子程序必须能 return
        // ==============================================================

        static void CheckSubroutineReturns(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = files.ToDictionary(f => f.name, f => f, System.StringComparer.OrdinalIgnoreCase);

            // 收集所有 call 目标（去重），只对它们做可达性分析
            var targets = new HashSet<(string file, string label)>();
            var origin = new Dictionary<(string, string), (ScriptFile f, int line)>();

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "call") continue;
                    string address = c.Arg(0);
                    if (string.IsNullOrEmpty(address) || Dynamic(address)) continue;
                    if (!VNStoryAddress.TryParse(address, out string file, out string label, out _))
                        continue;
                    string fileName = string.IsNullOrEmpty(file) ? f.name : file.Replace(".vn.txt", "");
                    if (!byName.ContainsKey(fileName)) continue;
                    if (!byName[fileName].labels.ContainsKey(label)) continue;
                    var key = (fileName, label);
                    if (targets.Add(key)) origin[key] = (f, c.line);
                }

            foreach (var (fileName, label) in targets)
            {
                var target = byName[fileName];
                var visited = new HashSet<(string, int)>();
                string bad = null;

                if (ReachesEnd(byName, target, target.labels[label] + 1, visited, ref bad))
                {
                    var src = origin[(fileName, label)];
                    Add(issues, VNLintSeverity.Error, "missing-return", target,
                        target.cmds[target.labels[label]].line,
                        $"子程序「{label}」存在走不到 return 的路径（{bad}）",
                        $"它被 {src.f.name}:{src.line} 用 call 调用。" +
                        "带着调用栈跑到文件末尾会报错停机——每条分支末尾都要有 return。");
                }
            }
        }

        /// <summary>
        /// 从 index 开始沿控制流走，判断是否存在"走到文件末尾/chapter 却没遇到 return"的路径。
        /// return 是好的终点；chapter 会清空调用栈（子程序里用 = 回不去了）；EOF 直接报错。
        /// </summary>
        static bool ReachesEnd(Dictionary<string, ScriptFile> byName, ScriptFile f, int index,
            HashSet<(string, int)> visited, ref string reason)
        {
            while (true)
            {
                if (index >= f.cmds.Count)
                {
                    reason = $"{f.name} 文件末尾";
                    return true;
                }
                if (!visited.Add((f.name, index))) return false; // 走过了，此路无新结论

                var c = f.cmds[index];
                switch (c.keyword)
                {
                    case "return":
                        return false; // 正常终止

                    case "chapter":
                        reason = $"{f.name}:{c.line} 的 chapter（会清空调用栈）";
                        return true;

                    case "jump":
                    {
                        if (!Follow(byName, f, c.Arg(0), out var nf, out int ni)) return false;
                        f = nf; index = ni;
                        continue;
                    }

                    case "if":
                    {
                        int at = LastJumpIndex(c);
                        if (at >= 0 && at + 1 < c.args.Count &&
                            Follow(byName, f, c.args[at + 1], out var nf, out int ni) &&
                            ReachesEnd(byName, nf, ni, visited, ref reason)) return true;
                        index++;
                        continue;
                    }

                    case "choice":
                    case "event":
                    {
                        bool fallthrough = false;
                        if (c.options != null)
                            foreach (var o in c.options)
                            {
                                if (string.IsNullOrEmpty(o.jumpLabel)) { fallthrough = true; continue; }
                                if (Follow(byName, f, o.jumpLabel, out var nf, out int ni) &&
                                    ReachesEnd(byName, nf, ni, visited, ref reason)) return true;
                            }
                        if (c.options == null || c.options.Count == 0) fallthrough = true;
                        if (!fallthrough) return false;
                        index++;
                        continue;
                    }

                    default:
                        index++; // call 会回到下一行，其余命令顺序执行
                        continue;
                }
            }
        }

        static bool Follow(Dictionary<string, ScriptFile> byName, ScriptFile from, string address,
            out ScriptFile file, out int index)
        {
            file = null; index = 0;
            if (string.IsNullOrEmpty(address) || Dynamic(address)) return false;
            if (!VNStoryAddress.TryParse(address, out string fileName, out string label, out _))
                return false;
            var target = from;
            if (!string.IsNullOrEmpty(fileName) &&
                !byName.TryGetValue(fileName.Replace(".vn.txt", ""), out target)) return false;
            if (!target.labels.TryGetValue(label, out int at)) return false;
            file = target; index = at + 1;
            return true;
        }

        // ==============================================================
        // 规则：跨文件回跳的死循环守卫
        // ==============================================================

        /// <summary>
        /// 典型事故：主循环里 `if 月份==6 jump 第2章::序`，第2章演完 `jump 主循环::月初`，
        /// 于是条件再次成立 → 无限循环。
        /// 判据：条件里引用的 flag，**没有任何一个**在目标文件里被写过 → 大概率缺守卫。
        /// </summary>
        static void CheckLoopGuards(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var byName = files.ToDictionary(f => f.name, f => f, System.StringComparer.OrdinalIgnoreCase);

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword != "if") continue;
                    int at = LastJumpIndex(c);
                    if (at < 0 || at + 1 >= c.args.Count) continue;

                    string address = c.args[at + 1];
                    if (Dynamic(address) || !address.Contains("::")) continue; // 只查跨文件
                    if (!VNStoryAddress.TryParse(address, out string fileName, out string label, out _))
                        continue;
                    if (string.IsNullOrEmpty(fileName)) continue;
                    if (!byName.TryGetValue(fileName.Replace(".vn.txt", ""), out var target)) continue;

                    // 目标文件是否会跳回本文件？不跳回就没有循环风险
                    bool jumpsBack = target.cmds.Any(tc =>
                        (tc.keyword == "jump" || tc.keyword == "chapter") &&
                        (tc.Arg(0) ?? "").Replace(".vn.txt", "")
                            .StartsWith(f.name, System.StringComparison.OrdinalIgnoreCase));
                    if (!jumpsBack) continue;

                    string cond = string.Join(" ", c.args.Take(at));
                    var names = FlagNames(cond);
                    if (names.Count == 0) continue;
                    if (names.Any(n => target.writtenFlags.Contains(n))) continue; // 有守卫

                    Add(issues, VNLintSeverity.Warning, "loop-risk", f, c.line,
                        $"跳到「{target.name}」后可能死循环（条件里没有该章节会设置的守卫 flag）",
                        $"{target.name} 会跳回 {f.name}，而条件「{cond}」引用的 flag " +
                        $"（{string.Join("、", names)}）在 {target.name} 里从未被写入，" +
                        "所以回来后条件仍然成立。" +
                        $"惯例：在 {target.name} 的开头写 `flag {target.name}已看 1`，" +
                        $"并在条件里加 `&& !{target.name}已看`。");
                }
        }

        static List<string> FlagNames(string condition)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(condition)) return result;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         condition, @"[^\s!()&|<>=+\-*/%]+"))
            {
                string t = m.Value.Trim();
                if (t.Length == 0 || int.TryParse(t, out _)) continue;
                if (!result.Contains(t)) result.Add(t);
            }
            return result;
        }

        // ==============================================================
        // 规则：从未被引用的 label
        // ==============================================================

        static void CheckUnreferencedLabels(List<ScriptFile> files, List<VNLintIssue> issues)
        {
            var referenced = new HashSet<string>(); // "文件::label"（小写）

            void Mark(ScriptFile from, string address)
            {
                if (string.IsNullOrEmpty(address) || Dynamic(address)) return;
                if (!VNStoryAddress.TryParse(address, out string file, out string label, out _)) return;
                string f = string.IsNullOrEmpty(file) ? from.name : file.Replace(".vn.txt", "");
                referenced.Add((f + "::" + label).ToLowerInvariant());
            }

            foreach (var f in files)
                foreach (var c in f.cmds)
                {
                    if (c.keyword == "jump" || c.keyword == "call") Mark(f, c.Arg(0));
                    else if (c.keyword == "if")
                    {
                        int at = LastJumpIndex(c);
                        if (at >= 0 && at + 1 < c.args.Count) Mark(f, c.args[at + 1]);
                    }
                    if (c.options != null)
                        foreach (var o in c.options) Mark(f, o.jumpLabel);
                }

            foreach (var f in files)
                foreach (var kv in f.labels)
                {
                    if (referenced.Contains((f.name + "::" + kv.Key).ToLowerInvariant())) continue;
                    // 文件第一条命令之前的 label = 章节入口，靠 chapter 从头进入，不算孤立
                    if (kv.Value <= 1) continue;
                    Add(issues, VNLintSeverity.Info, "unreferenced-label", f,
                        f.cmds[kv.Value].line,
                        $"label「{kv.Key}」从未被跳转引用",
                        "可能是拼写不一致，或是写完忘了接线；确认无用可以删掉。");
                }
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

        static void Add(List<VNLintIssue> issues, VNLintSeverity sev, string code,
            ScriptFile f, int line, string message, string hint)
        {
            issues.Add(new VNLintIssue
            {
                severity = sev,
                code = code,
                file = f.name,
                assetPath = f.assetPath,
                line = line,
                message = message,
                hint = hint,
            });
        }
    }
}
