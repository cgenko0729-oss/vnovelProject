using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>camseq 路径点的目标点类型（由 point token 反推，不额外存）</summary>
    public enum VNCamPointKind { Anchor, Character, Coords }

    /// <summary>某一行的舞台推算快照（剧本编辑器算，镜头编排窗口画）</summary>
    public class VNRowStageInfo
    {
        public string bgId;
        public string cgId;
        public Sprite backdrop;   // CG 优先，否则背景
        public readonly List<VNRowStageChar> characters = new List<VNRowStageChar>();
    }

    /// <summary>快照里的一个在场角色</summary>
    public struct VNRowStageChar
    {
        public string id;
        public int slot;      // 0=left 1=center 2=right
        public Sprite sprite; // 默认表情立绘
    }

    /// <summary>
    /// camseq 的一个 "&gt; ..." 路径点行的结构化视图。
    ///
    /// 【存储约定】文档里的唯一真相仍是 <see cref="VNRow.camLines"/> 里的原始字符串——
    /// 本类只是「解析 ↔ 格式化」的临时中转：编辑器每帧现解析、用户改完立刻格式化写回。
    /// 因此 VNScenarioDoc 的往返生成、行号换算（SourceLineForRow / RowForSourceLine）、
    /// 校验全都不用动；任何解析不了的写法也能原样留在文件里（UI 退回纯文本行显示）。
    ///
    /// 语法与运行时 <c>VNScriptParser.ParseCamWaypoint</c> 同构：
    /// <c>&gt; 目标点 [zoom] [时长] [ease:名] [xfade:秒] [hold:秒]</c>，改那边这里必须跟着改。
    /// </summary>
    public class VNCamWaypoint
    {
        public const float DefaultZoom = 1f;        // 与 VNCamWaypointDef.zoom 一致
        public const float DefaultDuration = 0.8f;  // 与 VNCamWaypointDef.duration 一致

        /// <summary>九宫格锚点（与运行时 VNStage 的锚点词表一致）</summary>
        public static readonly string[] Anchors = VNScenarioSchema.CamAnchors;

        /// <summary>角色部位（与 VNStage.ResolvePoint 的 case 分支一致；空 = 角色中心）</summary>
        public static readonly string[] CharacterParts =
            { "head", "chest", "waist", "feet", "up", "mid", "down" };

        public string point = "middle";
        public float zoom = DefaultZoom;
        public float duration = DefaultDuration;
        public string ease = "";
        public float fade;
        public float hold;      // >0 = 到达本点后停留的秒数

        // 原文没写 zoom / 时长时置位：只要值还是默认就继续省略，避免打开编辑器就把
        // 手写的 "> middle" 撑成 "> middle 1 0.8" 这种无意义的 diff
        public bool omitZoom;
        public bool omitDuration;

        public VNCamPointKind Kind => KindOf(point);

        public static VNCamPointKind KindOf(string point)
        {
            if (string.IsNullOrEmpty(point)) return VNCamPointKind.Anchor;
            if (point.Contains(",")) return VNCamPointKind.Coords;
            return System.Array.IndexOf(Anchors, point.ToLower()) >= 0
                ? VNCamPointKind.Anchor : VNCamPointKind.Character;
        }

        /// <summary>拆角色点位：角色id[:部位]</summary>
        public static void SplitCharacter(string point, out string id, out string part)
        {
            int colon = point == null ? -1 : point.IndexOf(':');
            id = colon > 0 ? point.Substring(0, colon) : (point ?? "");
            part = colon > 0 && colon + 1 < point.Length ? point.Substring(colon + 1) : "";
        }

        public static string JoinCharacter(string id, string part) =>
            string.IsNullOrEmpty(part) ? (id ?? "") : $"{id}:{part}";

        /// <summary>拆坐标点位；解析失败按 (0,0) 处理</summary>
        public static void SplitCoords(string point, out float x, out float y)
        {
            x = y = 0f;
            if (string.IsNullOrEmpty(point)) return;
            var parts = point.Split(',');
            TryFloat(parts[0], out x);
            if (parts.Length > 1) TryFloat(parts[1], out y);
        }

        public static string JoinCoords(float x, float y) =>
            string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", x, y);

        /// <summary>
        /// 解析一行 "&gt; ..."。**严格**：任何认不出的 token 都直接失败，
        /// 让调用方把原文当纯文本保留——宁可少一个结构化控件，也不能吞掉内容。
        /// </summary>
        public static bool TryParse(string line, out VNCamWaypoint wp)
        {
            wp = null;
            if (string.IsNullOrEmpty(line)) return false;
            string body = line.TrimStart();
            if (!body.StartsWith(">")) return false;

            var tokens = body.Substring(1).Trim()
                .Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return false;

            // 点位本身也要干净：Lint 查的「冒号带空格/方括号」这类写法留在纯文本里更好定位
            string point = tokens[0];
            if (point.StartsWith("[") || point.EndsWith(":") || point == ":") return false;

            var result = new VNCamWaypoint
            {
                point = point, omitZoom = true, omitDuration = true,
            };

            int numIndex = 0;
            for (int t = 1; t < tokens.Length; t++)
            {
                string tok = tokens[t];
                if (tok.StartsWith("ease:"))
                {
                    // 认不出的缓动名交给纯文本行，免得下拉悄悄把它改掉；
                    // 大小写不敏感匹配后统一成正名（运行时 Enum.TryParse 也是不敏感的）
                    string name = CanonicalEase(tok.Substring(5));
                    if (name == null) return false;
                    if (!string.IsNullOrEmpty(result.ease)) return false;   // 重复
                    result.ease = name;
                }
                else if (tok.StartsWith("xfade:"))
                {
                    if (!TryFloat(tok.Substring(6), out float f) || f <= 0f) return false;
                    if (result.fade > 0f) return false;                     // 重复
                    result.fade = f;
                }
                else if (tok.StartsWith("hold:"))
                {
                    if (!TryFloat(tok.Substring(5), out float h) || h <= 0f) return false;
                    if (result.hold > 0f) return false;                     // 重复
                    result.hold = h;
                }
                else if (TryFloat(tok, out float v))
                {
                    if (numIndex == 0) { result.zoom = v; result.omitZoom = false; }
                    else if (numIndex == 1) { result.duration = v; result.omitDuration = false; }
                    else return false;                                      // 多余的数字
                    numIndex++;
                }
                else
                {
                    return false;                                           // 不认识的 token
                }
            }

            wp = result;
            return true;
        }

        /// <summary>格式化回一行剧本文本（不含换行）</summary>
        public string Format()
        {
            // 时长是「第二个数字」，要写它就必须先把 zoom 写出来占位
            bool writeDuration = !omitDuration ||
                                 System.Math.Abs(duration - DefaultDuration) > 0.0001f;
            bool writeZoom = writeDuration || !omitZoom ||
                             System.Math.Abs(zoom - DefaultZoom) > 0.0001f;

            var sb = new StringBuilder("> ").Append(
                string.IsNullOrEmpty(point) ? "middle" : point);
            if (writeZoom) sb.Append(' ').Append(Num(zoom));
            if (writeDuration) sb.Append(' ').Append(Num(duration));
            if (!string.IsNullOrEmpty(ease)) sb.Append(" ease:").Append(ease);
            if (fade > 0.0001f) sb.Append(" xfade:").Append(Num(fade));
            if (hold > 0.0001f) sb.Append(" hold:").Append(Num(hold));
            return sb.ToString();
        }

        static string Num(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>缓动名规范化：认得就返回正名，认不得返回 null</summary>
        static string CanonicalEase(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var e in VNScenarioSchema.EaseNames)
                if (string.Equals(e, name, System.StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        static bool TryFloat(string s, out float v) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }

    /// <summary>
    /// 一整段 camseq 文本（camseq 行 + 若干 "&gt;" 行）的拆分工具。
    /// 镜头编排窗口回写、预设套用都走它，不经 VNScriptParser——
    /// 那边解析失败会往 Console 打 warning，编辑期不该有这种噪音。
    /// </summary>
    public static class VNCamseqText
    {
        /// <summary>camseq 行上允许出现的 kwarg（顺序 = 生成顺序）</summary>
        public static readonly string[] HeaderKeys = { "start", "startfade", "end", "endfade" };

        /// <summary>
        /// 拆成 header kwargs + 路径点行列表。找不到 camseq 行时返回 false。
        /// </summary>
        public static bool TrySplit(string text, out Dictionary<string, string> header,
            out List<string> waypointLines)
        {
            header = new Dictionary<string, string>();
            waypointLines = new List<string>();
            if (string.IsNullOrEmpty(text)) return false;

            bool found = false;
            foreach (var rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;

                if (line.StartsWith(">"))
                {
                    if (found) waypointLines.Add(line);
                    continue;
                }
                if (!found && line.StartsWith("camseq"))
                {
                    found = true;
                    foreach (var tok in line.Split(new[] { ' ', '\t' },
                                 System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        int colon = tok.IndexOf(':');
                        if (colon <= 0 || colon >= tok.Length - 1) continue;
                        string key = tok.Substring(0, colon);
                        if (System.Array.IndexOf(HeaderKeys, key) >= 0)
                            header[key] = tok.Substring(colon + 1);
                    }
                    continue;
                }
                if (found) break;   // camseq 块到此为止
            }
            return found;
        }

        /// <summary>把 header + 路径点行拼回一段 camseq 文本</summary>
        public static string Join(Dictionary<string, string> header, List<string> waypointLines)
        {
            var sb = new StringBuilder("camseq");
            if (header != null)
                foreach (var key in HeaderKeys)
                    if (header.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v))
                        sb.Append(' ').Append(key).Append(':').Append(v);
            sb.Append('\n');
            if (waypointLines != null)
                foreach (var l in waypointLines) sb.Append(l).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// 内置常用运镜模板（camseq 文本形式，与手写剧本 100% 一致）。
    /// <c>{char}</c> 是角色占位，套用时替换成当前可用的第一个角色 id；
    /// 一个角色都没有时整段退化成 middle，保证套完就能跑。
    /// </summary>
    public static class VNCamseqTemplates
    {
        public struct Entry
        {
            public string name;
            public string text;
        }

        public const string CharToken = "{char}";

        public static readonly Entry[] All =
        {
            new Entry { name = "推近特写", text =
                "camseq\n> middle 1.8 1.2 ease:InOutSine\n" },
            new Entry { name = "缓慢拉远", text =
                "camseq\n> middle 1 1.6 ease:OutSine\n" },
            new Entry { name = "推近后回位", text =
                "camseq\n> middle 1.7 0.9 ease:InSine\n> middle 1 0.9 ease:OutSine\n" },
            new Entry { name = "左右横摇", text =
                "camseq\n> left 1.3 0\n> right 1.3 2 ease:InOutSine\n> middle 1 0.8 ease:OutSine\n" },
            new Entry { name = "环视全景", text =
                "camseq\n> topleft 1.25 0\n> topright 1.25 1.4 ease:InOutSine\n" +
                "> bottomright 1.25 1.4 ease:InOutSine\n> middle 1 1 ease:OutSine\n" },
            new Entry { name = "三连甩镜（震撼）", text =
                "camseq\n> topleft 1.9 0\n> bottomright 1.9 0.18\n> middle 1.4 0.35 ease:OutSine\n" },
            new Entry { name = "告白推镜", text =
                "camseq\n> {char}:head 1.9 1.4 ease:InOutSine\n> {char}:head 2.1 2 ease:Linear\n" },
            new Entry { name = "惊讶弹镜", text =
                "camseq\n> {char}:head 2 0.25 ease:OutBack\n> middle 1 0.5 ease:OutSine\n" },
            new Entry { name = "叠化转特写", text =
                "camseq\n> {char}:head 1.9 0 xfade:0.5\n" },
            new Entry { name = "叠化开场特写", text =
                "camseq start:fade startfade:0.7\n> {char}:head 1.8 0\n" +
                "> {char}:head 1.9 2.5 ease:Linear\n" },
            new Entry { name = "转场瞬切起手", text =
                "camseq start:cut\n> topright 1.8 0\n> middle 1 1.4 ease:OutSine\n" },
        };

        /// <summary>把 {char} 换成实际角色 id；没有角色时整个点位退回 middle</summary>
        public static string Resolve(string text, string characterId)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!string.IsNullOrEmpty(characterId))
                return text.Replace(CharToken, characterId);
            // 没角色可用：连带部位后缀一起换掉，免得留下 "middle:head" 这种废点位
            foreach (var part in VNCamWaypoint.CharacterParts)
                text = text.Replace(CharToken + ":" + part, "middle");
            return text.Replace(CharToken, "middle");
        }
    }
}
