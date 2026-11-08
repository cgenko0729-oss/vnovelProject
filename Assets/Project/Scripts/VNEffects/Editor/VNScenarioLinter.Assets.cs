using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public static partial class VNScenarioLinter
    {
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
                        {
                            string fit = c.Kw("fit");
                            if (!string.IsNullOrEmpty(fit) && !VNCgFit.TryParse(fit, out _))
                                Add(issues, VNLintSeverity.Warning, "unknown-cg-fit", f, c.line,
                                    $"cg 的 fit:「{fit}」不认识，运行时会按 CG 条目的登记显示",
                                    $"可用值：{string.Join(" / ", VNCgFit.ScriptNames)}。");
                        }
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
                        if (kind != "dialogue" && kind != "choice" && kind != "name")
                        {
                            Add(issues, VNLintSeverity.Error, "bad-ui-kind", f, c.line,
                                $"ui 命令的对象「{kind}」不认识",
                                "用法：ui dialogue|choice|name <id|default>。");
                        }
                        else if (kind == "name")
                        {
                            // 名字样式是内置预设（不在 VNGameConfig 登记），拼错必然静默无效果，
                            // 所以按 Error 报——它跟皮肤 id 不同，没有「稍后再登记」这种中间状态
                            if (skinId != "default" && !Dynamic(skinId) &&
                                !VNNameplateStyle.TryParseId(skinId, out _))
                            {
                                var names = new System.Text.StringBuilder();
                                foreach (var a in VNNameplateStyle.Aliases)
                                {
                                    if (names.Length > 0) names.Append(' ');
                                    names.Append(a.name);
                                }
                                Add(issues, VNLintSeverity.Error, "unknown-nameplate-style", f, c.line,
                                    $"名字样式「{skinId}」不认识",
                                    $"可用：{names} default。");
                            }
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

                    case "imprint":
                    {
                        // 痕迹 id 拼错运行时只有一条 Warning，画面上什么都不会发生
                        CheckCharacter(issues, f, c.line, c.Arg(0), reg);
                        string trace = c.Arg(1);
                        if (!string.IsNullOrEmpty(trace) && !Dynamic(trace) &&
                            trace != "clear" && trace != "off" && !Dynamic(c.Arg(0)) &&
                            reg.charImprints.TryGetValue(c.Arg(0), out var traces))
                        {
                            if (traces.Count == 0)
                                Add(issues, VNLintSeverity.Warning, "no-imprint", f, c.line,
                                    $"角色「{c.Arg(0)}」还没有配任何立绘痕迹",
                                    "在角色资产 VNCharacterDef.imprints 里登记（id + 痕迹图）。");
                            else if (!traces.Contains(trace))
                                Add(issues, VNLintSeverity.Warning, "unknown-imprint", f, c.line,
                                    $"角色「{c.Arg(0)}」没有痕迹「{trace}」",
                                    $"该角色已有的痕迹：{string.Join(" / ", traces.OrderBy(s3 => s3))}。" +
                                    "清空全部写 `imprint <角色> clear`。");
                        }
                        break;
                    }

                    case "overlay":
                    {
                        // 层名拼错运行时只有一条 Warning，画面上什么都不会发生
                        CheckCharacter(issues, f, c.line, c.Arg(0), reg);
                        string layer = c.Arg(1);
                        if (!string.IsNullOrEmpty(layer) && !Dynamic(layer) &&
                            layer != "clear" && layer != "off" && !Dynamic(c.Arg(0)) &&
                            reg.charOverlays.TryGetValue(c.Arg(0), out var known))
                        {
                            if (known.Count == 0)
                                Add(issues, VNLintSeverity.Warning, "no-overlay", f, c.line,
                                    $"角色「{c.Arg(0)}」还没有配任何情绪叠加层",
                                    "在角色资产 VNCharacterDef.overlays 里登记（id + 透明图）。");
                            else if (!known.Contains(layer))
                                Add(issues, VNLintSeverity.Warning, "unknown-overlay", f, c.line,
                                    $"角色「{c.Arg(0)}」没有叠加层「{layer}」",
                                    $"该角色已有的层：{string.Join(" / ", known.OrderBy(s2 => s2))}。" +
                                    "清空全部写 `overlay <角色> clear`。");
                        }

                        string st = c.Arg(2);
                        if (!string.IsNullOrEmpty(st) && !Dynamic(st) &&
                            float.TryParse(st, out float sv) && (sv < 0f || sv > 1f))
                            Add(issues, VNLintSeverity.Warning, "overlay-range", f, c.line,
                                $"叠加层强度「{st}」超出 0~1，运行时会被钳制",
                                "强度是 0~1 的比例，实际不透明度还要乘该层资产里的 maxAlpha。");
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

                    case "interlude":
                    {
                        // 没登记的过场 id 运行时只有一条 Console 报错，画面上整段静默跳过
                        string iid = c.Arg(0);
                        if (string.IsNullOrEmpty(iid))
                            Add(issues, VNLintSeverity.Error, "interlude-no-id", f, c.line,
                                "interlude 缺少过场 id",
                                "写成 interlude <过场id>，id 是 VNGameConfig「过场库」里 VNInterludeDef 资产的 id。");
                        else if (!Dynamic(iid) && !reg.interludeIds.Contains(iid))
                            Add(issues, VNLintSeverity.Error, "unknown-interlude", f, c.line,
                                $"过场「{iid}」没有登记",
                                "在 VNGameConfig 的「过场库」里登记对应的 VNInterludeDef 资产。");
                        break;
                    }

                    case "tutorial":
                    {
                        // 没登记的教程 id 运行时只有一条 Console 报错，画面上整段静默跳过
                        string tid = c.Arg(0);
                        if (string.IsNullOrEmpty(tid))
                            Add(issues, VNLintSeverity.Error, "tutorial-no-id", f, c.line,
                                "tutorial 缺少教程 id",
                                "写成 tutorial <教程id>，id 是 VNGameConfig「教程库」里 VNTutorialDef 资产的 id。");
                        else if (!Dynamic(tid) && !reg.tutorialIds.Contains(tid))
                            Add(issues, VNLintSeverity.Error, "unknown-tutorial", f, c.line,
                                $"教程「{tid}」没有登记",
                                "在 VNGameConfig 的「教程库」里登记对应的 VNTutorialDef 资产。");
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
                        // 动作清单取自 VNEmoteCatalog（唯一真相），正名与别名都认；
                        // 与角色资产里的中文**表情名**是两回事（易混）
                        string action = c.Arg(1);
                        if (!string.IsNullOrEmpty(action) && !Dynamic(action) &&
                            !VNEmoteCatalog.Contains(action))
                            Add(issues, VNLintSeverity.Error, "bad-emote", f, c.line,
                                $"emote 动作「{action}」不认识",
                                $"可用动作：{VNEmoteCatalog.NamesJoined()}；" +
                                "中文的是角色资产里的**表情名**，那是另一回事" +
                                "（表情写成 `角色 表情: 台词` 或 show 的 expr:）。" +
                                "新动作在 VNCharacterEmotes 打 [VNEmote] 即自动登记。");
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
    }
}
