using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>choice 命令的一个选项</summary>
    public class VNChoiceOption
    {
        public string text;      // 选项显示文本
        public string flagOp;    // 可选：flag 操作（如 勇气+1）
        public string jumpLabel; // 可选：跳转标签（空 = 顺序继续）
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

        // choice 命令专用
        public List<VNChoiceOption> options;

        public string Arg(int i, string def = null) => i < args.Count ? args[i] : def;

        public float ArgF(int i, float def)
        {
            if (i < args.Count && float.TryParse(args[i], out float v)) return v;
            return def;
        }

        public string Kw(string key, string def = null) =>
            kwargs.TryGetValue(key, out var v) ? v : def;
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
            "bg", "show", "hide", "emote", "wait",
            "camera", "shake", "weather", "mood", "fx",
            "sakura", "transition",
            "label", "jump", "flag", "if", "choice", // P1 预留
        };

        public static List<VNScriptCommand> Parse(string source)
        {
            var result = new List<VNScriptCommand>();
            if (string.IsNullOrEmpty(source)) return result;

            var lines = source.Replace("\r\n", "\n").Split('\n');
            VNScriptCommand lastChoice = null;
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

                if (cmd.keyword == "choice")
                {
                    cmd.options = new List<VNChoiceOption>();
                    lastChoice = cmd;
                }
                else
                {
                    lastChoice = null; // 选项行必须紧跟 choice 块
                }

                result.Add(cmd);
            }
            return result;
        }

        /// <summary>解析选项行：* 文本 [flag:名字+1] [-> 标签]</summary>
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

            string flagOp = null;
            int fi = s.IndexOf("flag:", System.StringComparison.Ordinal);
            if (fi >= 0)
            {
                flagOp = s.Substring(fi + 5).Trim();
                s = s.Substring(0, fi).Trim();
            }

            choiceCmd.options.Add(new VNChoiceOption
            {
                text = s,
                flagOp = string.IsNullOrEmpty(flagOp) ? null : flagOp,
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
                int colon = tokens[t].IndexOf(':');
                if (colon > 0 && colon < tokens[t].Length - 1)
                    cmd.kwargs[tokens[t].Substring(0, colon)] = tokens[t].Substring(colon + 1);
                else
                    cmd.args.Add(tokens[t]);
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
