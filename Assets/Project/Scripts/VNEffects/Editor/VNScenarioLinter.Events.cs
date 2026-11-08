using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VNEffects.EditorTools
{
    public static partial class VNScenarioLinter
    {
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

                // 大头贴：vs 是必填的，主题/边框拼错都会静默降级
                if (module == "photo")
                {
                    string vs = c.Kw("vs");
                    if (string.IsNullOrEmpty(vs))
                        Add(issues, VNLintSeverity.Error, "photo-missing-vs", f, c.line,
                            "event photo 必须写 vs:<角色 id>",
                            "没有合影对象，模块会直接返回「完成」，整段拍照白跑。");
                    else CheckCharacter(issues, f, c.line, vs, reg);

                    CheckCharacter(issues, f, c.line, c.Kw("me"), reg);

                    string themeId = c.Kw("theme");
                    if (!string.IsNullOrEmpty(themeId) && !Dynamic(themeId) &&
                        reg.photoThemeIds.Count > 0 && !reg.photoThemeIds.Contains(themeId))
                        Add(issues, VNLintSeverity.Warning, "unknown-photo-theme", f, c.line,
                            $"没有 id 为「{themeId}」的拍照主题资产",
                            "主题是 VN/Photo Theme 资产（Assets/VNEffects/Photo/Themes）。" +
                            "拼错会静默退回自由拍照——那样只返回「完成」，" +
                            "写在下面的 完美/普通/失败 结果行一条都接不住。" +
                            $"当前已有：{string.Join(" / ", reg.photoThemeIds.OrderBy(s => s))}");

                    string frameId = c.Kw("frame");
                    if (!string.IsNullOrEmpty(frameId) && !Dynamic(frameId) &&
                        reg.photoFrameIds.Count > 0 && !reg.photoFrameIds.Contains(frameId))
                        Add(issues, VNLintSeverity.Warning, "unknown-photo-frame", f, c.line,
                            $"没有 id 为「{frameId}」的边框资产",
                            "边框是 VN/Photo Frame 资产（Assets/VNEffects/Photo/Frames）；" +
                            "拼错只会静默退回「无边框」开局。" +
                            $"当前已有：{string.Join(" / ", reg.photoFrameIds.OrderBy(s => s))}");

                    string bgId = c.Kw("bg");
                    if (!string.IsNullOrEmpty(bgId) && !Dynamic(bgId) &&
                        reg.photoBackdropIds.Count > 0 && !reg.photoBackdropIds.Contains(bgId))
                        Add(issues, VNLintSeverity.Warning, "unknown-photo-backdrop", f, c.line,
                            $"没有 id 为「{bgId}」的照片背景资产",
                            "背景是 VN/Photo Backdrop 资产（Assets/VNEffects/Photo/Backdrops）；" +
                            "拼错只会静默退回「无背景」开局。" +
                            $"当前已有：{string.Join(" / ", reg.photoBackdropIds.OrderBy(s => s))}");

                    // 评分模式与结果行必须对得上（两套结果名互斥）
                    bool free = string.IsNullOrEmpty(themeId) || c.Kw("mode") == "free";
                    if (c.options != null)
                        foreach (var o in c.options)
                        {
                            string outcome = o.text?.Trim();
                            if (string.IsNullOrEmpty(outcome) || Dynamic(outcome)) continue;
                            if (free && outcome != "完成")
                                Add(issues, VNLintSeverity.Warning, "photo-outcome-mode",
                                    f, o.line,
                                    $"自由拍照不会返回「{outcome}」",
                                    "没写 theme:（或写了 mode:free）就是自由拍照，不评分，" +
                                    "只返回「完成」。要分档结果就补上 theme:。");
                            else if (!free && outcome == "完成")
                                Add(issues, VNLintSeverity.Warning, "photo-outcome-mode",
                                    f, o.line,
                                    "评分模式不会返回「完成」",
                                    "写了 theme: 就会评分，结果是 完美 / 普通 / 失败 三选一。");
                        }
                }

                // 擦雾：id 拼错 = 事件直接返回空结果，整段被静默跳过
                if (module == "wipefog")
                {
                    // ★ 谜底提前揭晓：event 之前先 cg 出同一张图。
                    // 模块要到 OnLaunch 才铺得出雾，剧本先 cg 的话，从 cg 的转场开始
                    // 到事件真正启动为止，清晰的画面一直摆在玩家眼前（中间再夹一句台词
                    // 还得等玩家点一下，暴露得更久）。整个玩法的前提就没了。
                    //
                    // 只在「先 cg 的那张 == 要擦的那张」时报——先 cg 出别的图再擦另一张
                    // 是正常写法，那张的谜底并没有被暴露。
                    VNScriptCommand lastCg = null;
                    foreach (var other in f.cmds)
                    {
                        if (other.keyword != "cg" || other.line >= c.line) continue;
                        if (lastCg == null || other.line > lastCg.line) lastCg = other;
                    }
                    string shownCg = lastCg?.Arg(0);
                    if (!string.IsNullOrEmpty(shownCg) && shownCg != "off")
                    {
                        string wantCg = c.Kw("cg");
                        // cg: 留空 = 用舞台当前那张，也就是刚被 cg 出来的这张
                        bool exposed = string.IsNullOrEmpty(wantCg) || Dynamic(wantCg)
                                       || shownCg == wantCg;
                        if (exposed)
                            Add(issues, VNLintSeverity.Warning, "wipefog-cg-before-event",
                                f, c.line,
                                $"第 {lastCg.line} 行的「cg {shownCg}」会让谜底在开始擦之前就揭晓",
                                "擦雾的雾要到事件启动才铺得出来，先 cg 的话清晰画面会先摆在" +
                                "玩家眼前。改成删掉那行 cg、在 event 上写 " +
                                $"`cg:{shownCg}` 让模块自己铺底图，进事件的第一帧就是盖满雾的" +
                                "状态；要让画面在事件结束后留下继续演，就在「* 结果行」" +
                                "跳到的分支里再写 cg（同一帧交接，不会闪）。");
                    }

                    string fogId = c.Kw("id");
                    if (!string.IsNullOrEmpty(fogId) && !Dynamic(fogId) &&
                        reg.fogWipeIds.Count > 0 && !reg.fogWipeIds.Contains(fogId))
                        Add(issues, VNLintSeverity.Warning, "unknown-fogwipe", f, c.line,
                            $"没有 id 为「{fogId}」的擦雾定义资产",
                            "擦雾定义是 VN/Fog Wipe Definition 资产，fogWipeId 要和剧本写的一致；" +
                            "拼错会让整段擦雾被静默跳过。" +
                            $"当前已有：{string.Join(" / ", reg.fogWipeIds.OrderBy(s => s))}");

                    // 手残的玩家会拿到「失败」，没接住就整段剧情静默跳过
                    if (c.options != null && c.options.Count > 0)
                    {
                        bool hasFailure = false;
                        foreach (var o in c.options)
                            if (o.text?.Trim() == "失败") hasFailure = true;
                        if (!hasFailure)
                            Add(issues, VNLintSeverity.Warning, "wipefog-no-failure-branch",
                                f, c.line,
                                "event wipefog 没有接住「* 失败」结果行",
                                "玩家没擦到门槛时模块会返回「失败」，没接住就会静默按顺序继续。" +
                                "不想要失败分支的话，把资产的「普通」门槛调到 0 更稳。");
                    }
                }

                // AI 自由聊天：三种配置错误都会让整段对话退化成兜底台词
                if (module == "aitalk")
                {
                    string vs = c.Kw("vs");
                    string personaId = c.Kw("persona");

                    if (string.IsNullOrEmpty(vs) && string.IsNullOrEmpty(personaId))
                        Add(issues, VNLintSeverity.Error, "aitalk-missing-target", f, c.line,
                            "event aitalk 至少要写 vs:<角色 id> 或 persona:<人格 id>",
                            "两个都没有时模块找不到人格，会直接返回「失败」。");

                    CheckCharacter(issues, f, c.line, vs, reg);

                    if (!string.IsNullOrEmpty(personaId) && !Dynamic(personaId) &&
                        reg.aiPersonaIds.Count > 0 && !reg.aiPersonaIds.Contains(personaId))
                        Add(issues, VNLintSeverity.Warning, "unknown-ai-persona", f, c.line,
                            $"没有 id 为「{personaId}」的 AI 人格资产",
                            "人格是 VN/AI Persona 资产，id 要和剧本写的一致。" +
                            $"当前已有：{string.Join(" / ", reg.aiPersonaIds.OrderBy(s => s))}");

                    // persona 绑的角色和 vs 对不上 = 台词是 A 的、立绘换的是 B 的
                    if (!string.IsNullOrEmpty(personaId) && !string.IsNullOrEmpty(vs) &&
                        !Dynamic(personaId) && !Dynamic(vs) &&
                        reg.aiPersonaCharacter.TryGetValue(personaId, out string bound) &&
                        bound != vs)
                        Add(issues, VNLintSeverity.Warning, "aitalk-persona-mismatch",
                            f, c.line,
                            $"人格「{personaId}」绑的是角色「{bound}」，但 vs 写的是「{vs}」",
                            "模块会用 vs 的立绘演 persona 的台词，人设和长相会对不上。");

                    // 断网时模块返回「失败」，剧本没接住就会静默继续往下跑
                    if (c.options != null && c.options.Count > 0)
                    {
                        bool hasFailure = false;
                        foreach (var o in c.options)
                            if (o.text?.Trim() == VNAiTalkModule.OutcomeFailed) hasFailure = true;
                        if (!hasFailure)
                            Add(issues, VNLintSeverity.Warning, "aitalk-no-failure-branch",
                                f, c.line,
                                "event aitalk 没有接住「* 失败」结果行",
                                "玩家断网、没配 key 或被内容安全拦下时模块会返回「失败」，" +
                                "没接住就会静默按顺序继续，剧情会像什么都没发生一样往下走。");
                    }
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
    }
}
