using System.Collections.Generic;
using System.Text;

namespace VNEffects.EditorTools
{
    public enum VNRowKind { Raw, Say, Command }

    /// <summary>choice 块里的一个选项</summary>
    public class VNChoiceOptionRow
    {
        public string text = "";
        public string flagOp = "";   // 如 好感度+1
        public string jump = "";     // 空 = 顺序继续
    }

    /// <summary>
    /// 文档里的一行（往返无损的最小单位）。
    /// Raw = 注释/空行/无法识别行，原样保留；Say = 台词；Command = 已解析命令。
    /// choice 的选项行、camseq 的路径点行归属到所在块的 Row 里。
    /// </summary>
    public class VNRow
    {
        public VNRowKind kind = VNRowKind.Raw;
        public string raw = "";      // Raw 行原文
        public bool isAsync;

        // Say
        public string speaker = "";
        public string expression = "";
        public string text = "";

        // Command
        public string keyword = "";
        public readonly Dictionary<string, string> values = new Dictionary<string, string>();
        public readonly List<string> extraTokens = new List<string>();

        // 块附属
        public List<VNChoiceOptionRow> options;   // choice
        public List<string> camLines;             // camseq 的 "> ..." 行（原样）

        public string Get(string id) => values.TryGetValue(id, out var v) ? v : "";
        public void Set(string id, string v)
        {
            if (string.IsNullOrEmpty(v)) values.Remove(id);
            else values[id] = v;
        }

        public VNRow Clone()
        {
            var r = new VNRow
            {
                kind = kind, raw = raw, isAsync = isAsync,
                speaker = speaker, expression = expression, text = text,
                keyword = keyword,
            };
            foreach (var kv in values) r.values[kv.Key] = kv.Value;
            r.extraTokens.AddRange(extraTokens);
            if (options != null)
            {
                r.options = new List<VNChoiceOptionRow>();
                foreach (var o in options)
                    r.options.Add(new VNChoiceOptionRow
                        { text = o.text, flagOp = o.flagOp, jump = o.jump });
            }
            if (camLines != null) r.camLines = new List<string>(camLines);
            return r;
        }
    }

    /// <summary>一条校验结果</summary>
    public class VNIssue
    {
        public int rowIndex;
        public bool isError;   // false = warning
        public string message;
    }

    /// <summary>
    /// 剧本文档模型：.vn.txt ↔ 行列表 双向转换 + 校验。
    /// 文本仍是唯一真相；保存 = 逐行重新生成（格式会被规范化，注释/空行原样保留）。
    /// </summary>
    public class VNScenarioDoc
    {
        public readonly List<VNRow> rows = new List<VNRow>();

        static HashSet<string> _keywords;
        static HashSet<string> Keywords =>
            _keywords ?? (_keywords = new HashSet<string>(VNScriptParser.CommandKeywords));

        // ------------------------------------------------------------------
        // 解析
        // ------------------------------------------------------------------

        public static VNScenarioDoc Parse(string source)
        {
            var doc = new VNScenarioDoc();
            if (string.IsNullOrEmpty(source)) return doc;

            var lines = source.Replace("\r\n", "\n").Split('\n');
            VNRow lastChoice = null, lastCamseq = null;

            foreach (var lineRaw in lines)
            {
                string line = lineRaw.Trim();

                if (line.Length == 0 || line.StartsWith("#"))
                {
                    doc.rows.Add(new VNRow { kind = VNRowKind.Raw, raw = line });
                    continue;
                }
                if (line.StartsWith("*"))
                {
                    if (lastChoice != null) lastChoice.options.Add(ParseChoiceOption(line));
                    else doc.rows.Add(new VNRow { kind = VNRowKind.Raw, raw = line });
                    continue;
                }
                if (line.StartsWith(">"))
                {
                    if (lastCamseq != null) lastCamseq.camLines.Add(line);
                    else doc.rows.Add(new VNRow { kind = VNRowKind.Raw, raw = line });
                    continue;
                }

                var row = new VNRow();
                string body = line;
                if (body.EndsWith("@"))
                {
                    row.isAsync = true;
                    body = body.Substring(0, body.Length - 1).TrimEnd();
                }

                string first = FirstToken(body);
                if (Keywords.Contains(first))
                {
                    ParseCommand(row, body);
                    lastChoice = row.keyword == "choice" || row.keyword == "event"
                        ? row : null;
                    lastCamseq = row.keyword == "camseq" ? row : null;
                }
                else
                {
                    ParseSay(row, body);
                    lastChoice = null;
                    lastCamseq = null;
                }
                doc.rows.Add(row);
            }
            return doc;
        }

        static string FirstToken(string s)
        {
            int sp = s.IndexOfAny(new[] { ' ', '\t' });
            return sp < 0 ? s : s.Substring(0, sp);
        }

        static void ParseCommand(VNRow row, string body)
        {
            row.kind = VNRowKind.Command;
            var tokens = body.Split(new[] { ' ', '\t' },
                System.StringSplitOptions.RemoveEmptyEntries);
            row.keyword = tokens[0];
            var def = VNScenarioSchema.Find(row.keyword);

            if (row.keyword == "choice" || row.keyword == "event")
                row.options = new List<VNChoiceOptionRow>();
            if (row.keyword == "camseq") row.camLines = new List<string>();

            // if 特殊语法：if <cond> jump <label>
            if (row.keyword == "if")
            {
                if (tokens.Length >= 4 && tokens[2] == "jump")
                {
                    row.Set("condition", tokens[1]);
                    row.Set("target", tokens[3]);
                    for (int t = 4; t < tokens.Length; t++) row.extraTokens.Add(tokens[t]);
                }
                else
                {
                    for (int t = 1; t < tokens.Length; t++) row.extraTokens.Add(tokens[t]);
                }
                return;
            }

            var positional = new List<VNParamDef>();
            if (def != null) positional.AddRange(def.Positional());
            int posIndex = 0;

            for (int t = 1; t < tokens.Length; t++)
            {
                string tok = tokens[t];
                int colon = tok.IndexOf(':');
                if (colon > 0 && colon < tok.Length - 1)
                {
                    string key = tok.Substring(0, colon);
                    if (def != null && def.FindKwarg(key) != null)
                    {
                        row.Set(key, tok.Substring(colon + 1));
                        continue;
                    }
                    row.extraTokens.Add(tok); // 未知 key:value（含 "[fade:2]" 这类笔误）
                    continue;
                }
                if (posIndex < positional.Count)
                    row.Set(positional[posIndex++].id, tok);
                else
                    row.extraTokens.Add(tok);
            }
        }

        static void ParseSay(VNRow row, string body)
        {
            row.kind = VNRowKind.Say;
            int idx = -1;
            for (int c = 0; c < body.Length; c++)
            {
                if (body[c] == '：' || body[c] == ':') { idx = c; break; }
            }
            if (idx < 0)
            {
                row.speaker = "";
                row.text = body;
                return;
            }
            string left = body.Substring(0, idx).Trim();
            row.text = body.Substring(idx + 1).Trim();
            if (left.Length == 0) { row.speaker = ""; return; }
            var parts = left.Split(new[] { ' ', '\t' },
                System.StringSplitOptions.RemoveEmptyEntries);
            row.speaker = parts[0];
            if (parts.Length > 1) row.expression = parts[1];
        }

        static VNChoiceOptionRow ParseChoiceOption(string line)
        {
            var opt = new VNChoiceOptionRow();
            string s = line.Substring(1).Trim();

            int arrow = s.LastIndexOf("->", System.StringComparison.Ordinal);
            if (arrow >= 0)
            {
                opt.jump = s.Substring(arrow + 2).Trim();
                s = s.Substring(0, arrow).Trim();
            }
            int fi = s.IndexOf("flag:", System.StringComparison.Ordinal);
            if (fi >= 0)
            {
                opt.flagOp = s.Substring(fi + 5).Trim();
                s = s.Substring(0, fi).Trim();
            }
            opt.text = s;
            return opt;
        }

        // ------------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------------

        public string GenerateText()
        {
            var sb = new StringBuilder();
            foreach (var row in rows) AppendRow(sb, row);
            return sb.ToString();
        }

        static void AppendRow(StringBuilder sb, VNRow row)
        {
            switch (row.kind)
            {
                case VNRowKind.Raw:
                    sb.Append(row.raw).Append('\n');
                    return;

                case VNRowKind.Say:
                {
                    if (string.IsNullOrEmpty(row.speaker))
                    {
                        sb.Append(": ").Append(row.text);
                    }
                    else
                    {
                        sb.Append(row.speaker);
                        if (!string.IsNullOrEmpty(row.expression))
                            sb.Append(' ').Append(row.expression);
                        sb.Append(": ").Append(row.text);
                    }
                    if (row.isAsync) sb.Append(" @");
                    sb.Append('\n');
                    return;
                }

                case VNRowKind.Command:
                {
                    sb.Append(row.keyword);

                    if (row.keyword == "if")
                    {
                        sb.Append(' ').Append(row.Get("condition"))
                          .Append(" jump ").Append(row.Get("target"));
                    }
                    else
                    {
                        var def = VNScenarioSchema.Find(row.keyword);
                        if (def != null)
                        {
                            // 位置参数：输出到最后一个非空为止，中间空位用默认值补
                            var pos = new List<VNParamDef>(def.Positional());
                            int last = -1;
                            for (int i = 0; i < pos.Count; i++)
                                if (!string.IsNullOrEmpty(row.Get(pos[i].id))) last = i;
                            for (int i = 0; i <= last; i++)
                            {
                                string v = row.Get(pos[i].id);
                                if (string.IsNullOrEmpty(v))
                                    v = string.IsNullOrEmpty(pos[i].defaultValue)
                                        ? "0" : pos[i].defaultValue;
                                sb.Append(' ').Append(v);
                            }
                            foreach (var p in def.parameters)
                            {
                                if (!p.kwarg) continue;
                                string v = row.Get(p.id);
                                if (!string.IsNullOrEmpty(v))
                                    sb.Append(' ').Append(p.id).Append(':').Append(v);
                            }
                        }
                    }
                    foreach (var extra in row.extraTokens) sb.Append(' ').Append(extra);
                    if (row.isAsync) sb.Append(" @");
                    sb.Append('\n');

                    if (row.options != null)
                    {
                        foreach (var o in row.options)
                        {
                            sb.Append("* ").Append(o.text);
                            if (!string.IsNullOrEmpty(o.flagOp))
                                sb.Append(" flag:").Append(o.flagOp);
                            if (!string.IsNullOrEmpty(o.jump))
                                sb.Append(" -> ").Append(o.jump);
                            sb.Append('\n');
                        }
                    }
                    if (row.camLines != null)
                        foreach (var l in row.camLines) sb.Append(l).Append('\n');
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        // 文档级信息收集（下拉数据源 / 校验共用）
        // ------------------------------------------------------------------

        public List<string> CollectLabels()
        {
            var list = new List<string>();
            foreach (var r in rows)
                if (r.kind == VNRowKind.Command && r.keyword == "label")
                {
                    string n = r.Get("name");
                    if (!string.IsNullOrEmpty(n)) list.Add(n);
                }
            return list;
        }

        public List<string> CollectFlags()
        {
            var set = new HashSet<string>();
            foreach (var r in rows)
            {
                if (r.kind != VNRowKind.Command) continue;
                if (r.keyword == "flag") AddFlagBase(set, r.Get("name"));
                if (r.keyword == "if") AddFlagBase(set, r.Get("condition"));
                if (r.options != null)
                    foreach (var o in r.options) AddFlagBase(set, o.flagOp);
            }
            var list = new List<string>(set);
            list.Sort();
            return list;
        }

        static void AddFlagBase(HashSet<string> set, string expr)
        {
            if (string.IsNullOrEmpty(expr)) return;
            int cut = expr.IndexOfAny(new[] { '+', '-', '>', '<', '=', '!' });
            string name = (cut > 0 ? expr.Substring(0, cut) : expr).TrimStart('!').Trim();
            if (name.Length > 0) set.Add(name);
        }

        // ------------------------------------------------------------------
        // 校验
        // ------------------------------------------------------------------

        /// <summary>ctx 提供场景/资产数据源；某来源不可用时跳过对应检查</summary>
        public List<VNIssue> Validate(VNScenarioSourceContext ctx)
        {
            var issues = new List<VNIssue>();
            var labels = CollectLabels();
            var labelSet = new HashSet<string>();
            var duplicated = new HashSet<string>();
            foreach (var l in labels)
                if (!labelSet.Add(l)) duplicated.Add(l);

            void Err(int i, string msg) =>
                issues.Add(new VNIssue { rowIndex = i, isError = true, message = msg });
            void Warn(int i, string msg) =>
                issues.Add(new VNIssue { rowIndex = i, isError = false, message = msg });

            void CheckLabelRef(int i, string target, string what)
            {
                if (!string.IsNullOrEmpty(target) && !labelSet.Contains(target))
                    Err(i, $"{what} target label \"{target}\" does not exist");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                if (r.kind == VNRowKind.Raw)
                {
                    // 孤儿选项行 / 路径点行（前面没有对应块命令）
                    if (r.raw.StartsWith("*"))
                        Err(i, "option line has no preceding \"choice\" command");
                    else if (r.raw.StartsWith(">"))
                        Err(i, "waypoint line has no preceding \"camseq\" command");
                    continue;
                }

                if (r.kind == VNRowKind.Say)
                {
                    // 空说话者 + 首词疑似打错的命令关键字
                    if (string.IsNullOrEmpty(r.speaker) && LooksLikeTypoKeyword(r.text, out var kw))
                        Warn(i, $"looks like a mistyped command (did you mean \"{kw}\"?)");
                    if (!string.IsNullOrEmpty(r.speaker) && ctx.HasCharacters &&
                        System.Array.IndexOf(ctx.characterIds, r.speaker) < 0)
                        Warn(i, $"speaker \"{r.speaker}\" is not a registered character " +
                                "(name will show as-is / narration)");
                    if (!string.IsNullOrEmpty(r.expression) &&
                        ctx.HasCharacters && !ctx.HasExpression(r.speaker, r.expression))
                        Warn(i, $"character \"{r.speaker}\" has no expression \"{r.expression}\"");
                    continue;
                }

                // ---- Command ----
                var def = VNScenarioSchema.Find(r.keyword);
                if (def == null)
                {
                    Err(i, $"unknown command \"{r.keyword}\"");
                    continue;
                }

                foreach (var tok in r.extraTokens)
                {
                    if (tok == ":" || tok.StartsWith("[") || tok.EndsWith(":"))
                        Err(i, $"suspicious token \"{tok}\" — key:value must have no spaces/brackets");
                    else
                        Warn(i, $"unrecognized token \"{tok}\" (preserved as-is)");
                }

                foreach (var p in def.parameters)
                {
                    string v = r.Get(p.id);
                    if (string.IsNullOrEmpty(v)) continue;
                    switch (p.source)
                    {
                        case VNParamSource.Number:
                            if (!float.TryParse(v, out _))
                                Err(i, $"{r.keyword} {p.label}: \"{v}\" is not a number");
                            break;
                        case VNParamSource.Character:
                            if (ctx.HasCharacters &&
                                System.Array.IndexOf(ctx.characterIds, v) < 0)
                                Err(i, $"{r.keyword}: character \"{v}\" not found");
                            break;
                        case VNParamSource.Expression:
                            if (ctx.HasCharacters &&
                                !ctx.HasExpression(r.Get(p.dependsOn), v))
                                Warn(i, $"{r.keyword}: expression \"{v}\" not found on " +
                                        $"\"{r.Get(p.dependsOn)}\"");
                            break;
                        case VNParamSource.Background:
                            if (ctx.HasBackgrounds &&
                                System.Array.IndexOf(ctx.backgroundIds, v) < 0)
                                Err(i, $"{r.keyword}: background \"{v}\" not registered on VNStage");
                            break;
                        case VNParamSource.AudioBgm:
                            if (ctx.HasBgm && v != "stop" &&
                                System.Array.IndexOf(ctx.bgmIds, v) < 0)
                                Err(i, $"{r.keyword}: audio id \"{v}\" not in VNAudio.bgmLibrary");
                            break;
                        case VNParamSource.AudioSe:
                            if (ctx.HasSe && v != "stop" &&
                                System.Array.IndexOf(ctx.seIds, v) < 0)
                                Err(i, $"{r.keyword}: audio id \"{v}\" not in VNAudio.seLibrary");
                            break;
                        case VNParamSource.AudioVoice:
                            if (ctx.HasVoice && v != "stop" &&
                                System.Array.IndexOf(ctx.voiceIds, v) < 0)
                                Err(i, $"{r.keyword}: audio id \"{v}\" not in VNAudio.voiceLibrary");
                            break;
                        case VNParamSource.Label:
                            CheckLabelRef(i, v, r.keyword);
                            break;
                        case VNParamSource.EventId:
                            if (ctx.HasEvents &&
                                System.Array.IndexOf(ctx.eventIds, v) < 0)
                                Err(i, $"{r.keyword}: event module \"{v}\" not in VNEventRegistry");
                            break;
                    }
                }

                switch (r.keyword)
                {
                    case "label":
                        if (string.IsNullOrEmpty(r.Get("name"))) Err(i, "label needs a name");
                        else if (duplicated.Contains(r.Get("name")))
                            Err(i, $"label \"{r.Get("name")}\" is defined more than once");
                        break;
                    case "jump":
                        if (string.IsNullOrEmpty(r.Get("label"))) Err(i, "jump needs a target label");
                        break;
                    case "if":
                        if (string.IsNullOrEmpty(r.Get("condition")) ||
                            string.IsNullOrEmpty(r.Get("target")))
                            Err(i, "if syntax: if <cond> jump <label>");
                        else if (!System.Text.RegularExpressions.Regex.IsMatch(
                            r.Get("condition"), @"^!?[^\s<>=!]+([<>=!]=?-?\d+)?$"))
                            Err(i, $"condition \"{r.Get("condition")}\" is invalid " +
                                   "(no spaces; e.g. 勇气 / !勇气 / 好感度>=2)");
                        break;
                    case "bg":
                        if (string.IsNullOrEmpty(r.Get("id"))) Err(i, "bg needs a background id");
                        break;
                    case "bgm":
                        if (r.Get("op") != "stop" && string.IsNullOrEmpty(r.Get("id")))
                            Err(i, "bgm play needs an audio id");
                        break;
                    case "choice":
                        if (r.options == null || r.options.Count == 0)
                            Err(i, "choice has no option lines");
                        else
                            foreach (var o in r.options)
                            {
                                if (string.IsNullOrEmpty(o.text))
                                    Warn(i, "choice option has empty text");
                                CheckLabelRef(i, o.jump, "choice option");
                            }
                        break;
                    case "camseq":
                        if (r.camLines == null || r.camLines.Count == 0)
                            Warn(i, "camseq has no \"> waypoint\" lines");
                        else
                        {
                            foreach (var l in r.camLines)
                                foreach (var tok in l.Substring(1).Split(new[] { ' ', '\t' },
                                    System.StringSplitOptions.RemoveEmptyEntries))
                                    if (tok == ":" || tok.EndsWith(":") || tok.StartsWith("["))
                                        Err(i, $"waypoint token \"{tok}\": " +
                                               "colon must have no surrounding spaces/brackets");
                            if (r.Get("start") == "cut" && !FirstWaypointIsCut(r))
                                Warn(i, "start:cut expects the first waypoint duration to be 0");
                        }
                        break;
                }
            }
            return issues;
        }

        static bool FirstWaypointIsCut(VNRow camseq)
        {
            if (camseq.camLines == null || camseq.camLines.Count == 0) return false;
            var tokens = camseq.camLines[0].Substring(1).Split(new[] { ' ', '\t' },
                System.StringSplitOptions.RemoveEmptyEntries);
            int numIndex = 0;
            for (int t = 1; t < tokens.Length; t++)
            {
                if (tokens[t].Contains(":")) continue;
                if (float.TryParse(tokens[t], out float v))
                {
                    if (numIndex == 1) return v <= 0.001f; // 第二个数字 = 时长
                    numIndex++;
                }
            }
            return true; // 没写时长 → 默认 0.8，不是瞬切
        }

        /// <summary>首词是否疑似打错的命令关键字（编辑距离 ≤1 的纯 ASCII 词）</summary>
        static bool LooksLikeTypoKeyword(string text, out string keyword)
        {
            keyword = null;
            if (string.IsNullOrEmpty(text)) return false;
            string first = FirstToken(text.Trim());
            if (first.Length < 2 || first.Length > 12) return false;
            foreach (char c in first)
                if (c > 127 || !char.IsLetter(c)) return false;

            string lower = first.ToLower();
            foreach (var kw in Keywords)
            {
                if (lower == kw) continue; // 完全相同不可能到这（会被解析成命令）
                if (EditDistanceLeq1(lower, kw)) { keyword = kw; return true; }
            }
            return false;
        }

        static bool EditDistanceLeq1(string a, string b)
        {
            if (System.Math.Abs(a.Length - b.Length) > 1) return false;
            if (a.Length == b.Length)
            {
                int diff = 0;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i] && ++diff > 1) return false;
                return diff == 1;
            }
            // 长度差 1：允许一次插入/删除
            string s = a.Length < b.Length ? a : b;
            string l = a.Length < b.Length ? b : a;
            int si = 0, li = 0; bool skipped = false;
            while (si < s.Length && li < l.Length)
            {
                if (s[si] == l[li]) { si++; li++; continue; }
                if (skipped) return false;
                skipped = true;
                li++;
            }
            return true;
        }
    }

    /// <summary>下拉候选与校验用的数据源快照（由编辑器窗口负责刷新）</summary>
    public class VNScenarioSourceContext
    {
        public string[] characterIds = System.Array.Empty<string>();
        public Dictionary<string, string[]> expressions = new Dictionary<string, string[]>();
        public string[] backgroundIds = System.Array.Empty<string>();
        public string[] bgmIds = System.Array.Empty<string>();
        public string[] seIds = System.Array.Empty<string>();
        public string[] voiceIds = System.Array.Empty<string>();
        public string[] eventIds = System.Array.Empty<string>();

        public bool HasCharacters => characterIds.Length > 0;
        public bool HasBackgrounds => backgroundIds.Length > 0;
        public bool HasBgm => bgmIds.Length > 0;
        public bool HasSe => seIds.Length > 0;
        public bool HasVoice => voiceIds.Length > 0;
        public bool HasEvents => eventIds.Length > 0;

        public bool HasExpression(string characterId, string expr)
        {
            if (string.IsNullOrEmpty(characterId) || string.IsNullOrEmpty(expr)) return true;
            if (!expressions.TryGetValue(characterId, out var list)) return true;
            return System.Array.IndexOf(list, expr) >= 0;
        }
    }
}
