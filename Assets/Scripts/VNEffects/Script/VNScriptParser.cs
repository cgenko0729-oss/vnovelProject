using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>choice 命令的一个选项</summary>
    public class VNChoiceOption
    {
        public string text;      // 选项显示文本
        public string flagOp;    // 可选：flag 操作（如 勇气+1）
        public string condition; // 可选：if:条件（不满足则不显示该选项）
        public string costOp;    // 可选：cost:花费（如 金钱-100，不够时选项置灰）
        public string jumpLabel; // 可选：跳转标签（空 = 顺序继续）
        public int line;

        // 当前语言的译文（VNScriptLocale.Apply 标注；null/空 = 显示原文）。
        // 只影响显示：event 结果行是逻辑标识符（结果匹配 / 去过_xx flag），不参与翻译。
        public string localizedText;
    }

    /// <summary>camseq 命令的一个路径点（点位在运行时解析）</summary>
    public class VNCamWaypointDef
    {
        public string point;    // 目标点 token：锚点名/角色[:部位]/x,y
        public float zoom = 1f;
        public float duration = 0.8f;
        public string ease;     // 可选缓动名
        public float fade;      // >0 = 交叉淡化到本点（xfade:秒），代替平移/瞬切
        public int line;
    }

    /// <summary>一条剧本命令（解析结果）</summary>
    public class VNScriptCommand
    {
        public string keyword;                 // 命令关键字；台词行为 "say"
        public List<string> args = new List<string>();              // 位置参数
        public Dictionary<string, string> kwargs = new Dictionary<string, string>(); // key:value 参数
        public bool isAsync;                   // 行尾 @ = 不等待完成
        public int line;                       // 源文件行号（报错定位用）

        // 台词行专用
        public string speaker;     // 说话者 id（空 = 旁白）
        public string expression;  // 可选表情
        public string text;        // 台词内容

        // 当前语言的译文（VNScriptLocale.Apply 标注；null/空 = 显示原文）
        public string localizedText;

        // choice 命令专用
        public List<VNChoiceOption> options;

        // camseq 命令专用
        public List<VNCamWaypointDef> camPoints;

        public string Arg(int i, string def = null) => i < args.Count ? args[i] : def;

        public float ArgF(int i, float def)
        {
            if (i < args.Count && float.TryParse(args[i], out float v)) return v;
            return def;
        }

        public string Kw(string key, string def = null) =>
            kwargs.TryGetValue(key, out var v) ? v : def;

        public float KwF(string key, float def)
        {
            if (kwargs.TryGetValue(key, out var v) && float.TryParse(v, out float f)) return f;
            return def;
        }
    }

    /// <summary>
    /// Ren'Py 风格轻量剧本解析器。语法：
    ///   # 注释
    ///   bg bg1 transition:NoiseDissolve      ← 命令 + 位置参数 + key:value 参数
    ///   show 亚里沙 at:left with:DissolveGlow @   ← 行尾 @ = 异步（不等待）
    ///   亚里沙 微笑: 今天天气真好。            ← 台词行（说话者 [表情]: 内容）
    ///   旁白: ……                              ← 旁白（说话者不是注册角色时只显示名字）
    ///   : 纯旁白无名牌                          ← 冒号开头 = 无名牌旁白
    /// </summary>
    public static class VNScriptParser
    {
        /// <summary>P0 已实现 + 为 P1 预留的关键字（label/jump/choice 等先解析不执行）</summary>
        static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "bg", "cg", "show", "hide", "emote", "wait",
            "camera", "shake", "weather", "mood", "fx",
            "sakura", "transition", "reset",
            "label", "jump", "call", "return", "params", "flag", "stat", "time", "if", "choice",
            "chapter",
            "move", "bgm", "se", "voice", "volume",
            "camseq", "camcut", "camto",
            "portrait",
            "event", "quest",
            "letterbox",
            "ui",
        };

        /// <summary>命令关键字集合（剧本编辑器等工具用，与解析行为保持单一来源）</summary>
        public static IEnumerable<string> CommandKeywords => Keywords;

        public static List<VNScriptCommand> Parse(string source)
        {
            var result = new List<VNScriptCommand>();
            if (string.IsNullOrEmpty(source)) return result;

            var lines = source.Replace("\r\n", "\n").Split('\n');
            VNScriptCommand lastChoice = null;
            VNScriptCommand lastCamseq = null;
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i].Trim();
                if (raw.Length == 0 || raw.StartsWith("#")) continue;

                // 选项行：* 文本 [flag:操作] [-> 标签]，挂到上一个 choice 命令
                if (raw.StartsWith("*"))
                {
                    if (lastChoice == null)
                        Debug.LogWarning($"[VNScript] 第 {i + 1} 行：选项行前面没有 choice 命令，已忽略");
                    else
                        ParseChoiceOption(lastChoice, raw, i + 1);
                    continue;
                }

                // 镜头路径点行：> 目标点 zoom 时长 [ease:名]，挂到上一个 camseq 命令
                if (raw.StartsWith(">"))
                {
                    if (lastCamseq == null)
                        Debug.LogWarning($"[VNScript] 第 {i + 1} 行：路径点行前面没有 camseq 命令，已忽略");
                    else
                        ParseCamWaypoint(lastCamseq, raw, i + 1);
                    continue;
                }

                var cmd = new VNScriptCommand { line = i + 1 };

                // 行尾 @ = 异步
                if (raw.EndsWith("@"))
                {
                    cmd.isAsync = true;
                    raw = raw.Substring(0, raw.Length - 1).TrimEnd();
                }

                string firstToken = FirstToken(raw);
                if (Keywords.Contains(firstToken))
                    ParseCommand(cmd, raw);
                else
                    ParseSay(cmd, raw);

                // event 复用 choice 的「* 附属行」机制：结果名 → 跳转标签
                if (cmd.keyword == "choice" || cmd.keyword == "event")
                {
                    cmd.options = new List<VNChoiceOption>();
                    lastChoice = cmd;
                    lastCamseq = null;
                }
                else if (cmd.keyword == "camseq")
                {
                    cmd.camPoints = new List<VNCamWaypointDef>();
                    lastCamseq = cmd;
                    lastChoice = null;
                }
                else
                {
                    lastChoice = null;  // 块的附属行必须紧跟块命令
                    lastCamseq = null;
                }

                result.Add(cmd);
            }
            return result;
        }

        /// <summary>解析镜头路径点行：> 目标点 [zoom] [时长] [ease:名] [xfade:秒]</summary>
        static void ParseCamWaypoint(VNScriptCommand camseqCmd, string raw, int line)
        {
            var tokens = raw.Substring(1).Trim()
                .Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                Debug.LogWarning($"[VNScript] 第 {line} 行：空的路径点行");
                return;
            }

            var wp = new VNCamWaypointDef { point = tokens[0], line = line };
            int numIndex = 0; // 第 1 个数字 = zoom，第 2 个 = 时长
            for (int t = 1; t < tokens.Length; t++)
            {
                if (tokens[t].StartsWith("ease:"))
                {
                    wp.ease = tokens[t].Substring(5);
                }
                else if (tokens[t].StartsWith("xfade:"))
                {
                    if (float.TryParse(tokens[t].Substring(6), out float f) && f > 0f)
                        wp.fade = f;
                    else
                        Debug.LogWarning($"[VNScript] 第 {line} 行：xfade 时长「{tokens[t]}」应为正数");
                }
                else if (float.TryParse(tokens[t], out float v))
                {
                    if (numIndex == 0) wp.zoom = v;
                    else if (numIndex == 1) wp.duration = v;
                    numIndex++;
                }
            }
            camseqCmd.camPoints.Add(wp);
        }

        /// <summary>
        /// 解析选项行：* 文本 [if:条件] [cost:金钱-100] [flag:名字+1] [-> 标签]。
        /// if/cost/flag 参数是行尾的空格分隔 token，可任意顺序；选项文本本身可含空格。
        /// </summary>
        static void ParseChoiceOption(VNScriptCommand choiceCmd, string raw, int line)
        {
            string s = raw.Substring(1).Trim();

            string target = null;
            int arrow = s.LastIndexOf("->", System.StringComparison.Ordinal);
            if (arrow >= 0)
            {
                target = s.Substring(arrow + 2).Trim();
                s = s.Substring(0, arrow).Trim();
            }

            // 从行尾逐个摘掉参数 token（保持旧剧本「flag: 必须在文本之后」的语义）
            string flagOp = null, condition = null, costOp = null;
            while (true)
            {
                int sp = s.LastIndexOfAny(new[] { ' ', '\t' });
                if (sp < 0) break;
                string tail = s.Substring(sp + 1);
                if (tail.StartsWith("flag:")) flagOp = tail.Substring(5);
                else if (tail.StartsWith("if:")) condition = tail.Substring(3);
                else if (tail.StartsWith("cost:")) costOp = tail.Substring(5);
                else break;
                s = s.Substring(0, sp).TrimEnd();
            }

            choiceCmd.options.Add(new VNChoiceOption
            {
                text = s,
                flagOp = string.IsNullOrEmpty(flagOp) ? null : flagOp,
                condition = string.IsNullOrEmpty(condition) ? null : condition,
                costOp = string.IsNullOrEmpty(costOp) ? null : costOp,
                jumpLabel = string.IsNullOrEmpty(target) ? null : target,
                line = line,
            });
        }

        static string FirstToken(string s)
        {
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            return sp < 0 ? s : s.Substring(0, sp);
        }

        static void ParseCommand(VNScriptCommand cmd, string raw)
        {
            var tokens = raw.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            cmd.keyword = tokens[0];
            for (int t = 1; t < tokens.Length; t++)
            {
                string token = tokens[t];
                int colon = token.IndexOf(':');

                // 限定地址「文件::标签」不是 key:value。
                // 判据：第一个冒号后面**紧跟另一个冒号**时，整个 token 是位置参数
                // （jump / call / chapter 的目标）。
                // 反例保护：`title:第1章::序` 的第一个冒号后面是「第」，仍按 key:value 处理，
                // 值里保留 :: 原样传给 VNStoryAddress。
                bool qualifiedAddress =
                    colon >= 0 && colon + 1 < token.Length && token[colon + 1] == ':';

                // camto / camcut 的第一个参数是「目标点」，可能长成 角色:部位
                // （如 camto 亚里沙:head 1.6 0.8），与 key:value 撞形。
                // 这两个命令的**首个**位置参数一律不当 kwarg。
                // （camseq 的 > 路径点行走 ParseCamWaypoint，本来就无条件取 tokens[0]，不受影响。）
                bool camPointArg =
                    t == 1 && (cmd.keyword == "camto" || cmd.keyword == "camcut");

                if (!qualifiedAddress && !camPointArg && colon > 0 && colon < token.Length - 1)
                    cmd.kwargs[token.Substring(0, colon)] = token.Substring(colon + 1);
                else
                    cmd.args.Add(token);
            }
        }

        static void ParseSay(VNScriptCommand cmd, string raw)
        {
            cmd.keyword = "say";

            // 找第一个分隔冒号（支持全角/半角）
            int idx = -1;
            for (int c = 0; c < raw.Length; c++)
            {
                if (raw[c] == '：' || raw[c] == ':')
                {
                    idx = c;
                    break;
                }
            }

            if (idx < 0)
            {
                // 无冒号：整行当作无名牌旁白
                cmd.speaker = "";
                cmd.text = raw;
                return;
            }

            string left = raw.Substring(0, idx).Trim();
            cmd.text = raw.Substring(idx + 1).Trim();

            if (left.Length == 0)
            {
                cmd.speaker = ""; // 冒号开头 = 无名牌旁白
                return;
            }

            var parts = left.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            cmd.speaker = parts[0];
            if (parts.Length > 1) cmd.expression = parts[1];
        }

        /// <summary>解析枚举（失败时告警并返回默认值）</summary>
        public static T ParseEnum<T>(string value, T def, int line) where T : struct
        {
            if (string.IsNullOrEmpty(value)) return def;
            if (System.Enum.TryParse(value, true, out T parsed)) return parsed;
            Debug.LogWarning($"[VNScript] 第 {line} 行：无法识别的 {typeof(T).Name}「{value}」，使用默认值 {def}");
            return def;
        }
    }
}
