using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>
    /// AI 花费累计报表：扫 `AiTalkLogs/`（含 `Editor/` 子目录）的全部 json，
    /// 按月 / 日 / 人格 / 模型 / 来源聚合。菜单 Tools → VN Effects → AI → Cost Report。
    ///
    /// 【为什么要有它】
    ///   单场成本在每份日志里都有，但「这个月到底花了多少」得手工把几十份加起来，
    ///   加错是必然的。而且历史日志里的金额是**按当时写死的 Flash Lite 单价**算的——
    ///   那阵子若用的是别的模型，存下来的数字本身就是错的。
    ///
    /// 【按当前单价重算】
    ///   日志里同时存了 token 数与模型名，所以可以拿当前单价表重算一遍，
    ///   把历史上的错误估算修正过来。默认开启；关掉则显示日志里存的原值
    ///   （想复现「当时以为花了多少」时才需要）。
    /// </summary>
    public class VNAiCostReport : EditorWindow
    {
        [MenuItem("Tools/VN Effects/AI/Cost Report", false, 431)]
        public static void Open()
        {
            var w = GetWindow<VNAiCostReport>("AI 花费报表");
            w.minSize = new Vector2(720, 420);
            w.Rescan();
            w.Show();
        }

        enum GroupBy { Month, Day, Persona, Model, Source, Session }

        [SerializeField] GroupBy _groupBy = GroupBy.Month;
        [SerializeField] bool _recalc = true;
        [SerializeField] bool _includeEditor = true;
        [SerializeField] bool _includeGame = true;

        readonly List<Entry> _entries = new List<Entry>();
        Vector2 _scroll;
        string _scanError;

        /// <summary>一份日志 = 一场对话。</summary>
        class Entry
        {
            public string path;
            public bool fromEditor;          // 试聊台产生的
            public string startedAt;         // "yyyy-MM-dd HH:mm:ss"
            public string personaId;
            public string model;
            public int turns;
            public int summaryRequests;
            public int promptTokens;
            public int outputTokens;         // 含思考
            public float seconds;
            public double storedCost;        // 日志里存的
            public double recalcCost;        // 按当前单价重算的
            public bool unknownModel;

            public double Cost(bool recalc) => recalc ? recalcCost : storedCost;
            public int Requests => turns + summaryRequests;
        }

        class Group
        {
            public string key;
            public int sessions, requests, promptTokens, outputTokens;
            public double cost;
            public float seconds;
        }

        // ──────────────── 扫描 ────────────────

        void Rescan()
        {
            _entries.Clear();
            _scanError = null;
            try
            {
                string dir = VNAiTalkLog.ResolveDirectory();
                if (!Directory.Exists(dir))
                {
                    _scanError = $"还没有日志目录：{dir}";
                    return;
                }

                foreach (string p in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    VNAiTalkLog.Session s;
                    try
                    {
                        s = JsonUtility.FromJson<VNAiTalkLog.Session>(
                            File.ReadAllText(p, Encoding.UTF8));
                    }
                    catch { continue; }              // 单份坏了不影响整体
                    if (s == null) continue;

                    var e = new Entry
                    {
                        path = p,
                        fromEditor = p.Replace('\\', '/')
                                      .Contains("/" + VNAiStudioLog.SubFolder + "/"),
                        startedAt = s.startedAt ?? "",
                        personaId = string.IsNullOrEmpty(s.personaId) ? "(无人格)" : s.personaId,
                        model = string.IsNullOrEmpty(s.model) ? "(未记录)" : s.model,
                        turns = s.turns != null ? s.turns.Count : 0,
                        summaryRequests = s.summaryRequests,
                        promptTokens = s.totalPromptTokens,
                        outputTokens = s.totalOutputTokens,
                        seconds = s.totalSeconds,
                        storedCost = s.totalCostUsd,
                    };

                    // 重算：totalOutputTokens 已经含思考，所以直接按输出价乘，
                    // 不能再走 VNAiPricing.Cost(…, thoughtsTokens) 那条（会重复计一次思考）
                    var price = VNAiPricing.For(s.model, out bool found);
                    e.unknownModel = !found;
                    e.recalcCost = e.promptTokens * price.inputPerMillion / 1e6
                                 + e.outputTokens * price.outputPerMillion / 1e6;

                    _entries.Add(e);
                }

                _entries.Sort((a, b) => string.CompareOrdinal(b.startedAt, a.startedAt));
            }
            catch (Exception ex)
            {
                _scanError = ex.Message;
            }
        }

        // ──────────────── 聚合 ────────────────

        List<Entry> Filtered()
        {
            var list = new List<Entry>();
            foreach (var e in _entries)
            {
                if (e.fromEditor && !_includeEditor) continue;
                if (!e.fromEditor && !_includeGame) continue;
                list.Add(e);
            }
            return list;
        }

        List<Group> Aggregate(List<Entry> src)
        {
            var map = new Dictionary<string, Group>();
            var order = new List<string>();

            foreach (var e in src)
            {
                string key = KeyOf(e);
                if (!map.TryGetValue(key, out var g))
                {
                    g = new Group { key = key };
                    map[key] = g;
                    order.Add(key);
                }
                g.sessions++;
                g.requests += e.Requests;
                g.promptTokens += e.promptTokens;
                g.outputTokens += e.outputTokens;
                g.cost += e.Cost(_recalc);
                g.seconds += e.seconds;
            }

            var list = new List<Group>();
            foreach (string k in order) list.Add(map[k]);

            // 时间维度按时间倒序（新的在上），其余按花费降序
            if (_groupBy == GroupBy.Month || _groupBy == GroupBy.Day || _groupBy == GroupBy.Session)
                list.Sort((a, b) => string.CompareOrdinal(b.key, a.key));
            else
                list.Sort((a, b) => b.cost.CompareTo(a.cost));
            return list;
        }

        string KeyOf(Entry e)
        {
            switch (_groupBy)
            {
                case GroupBy.Month:
                    return e.startedAt.Length >= 7 ? e.startedAt.Substring(0, 7) : "(无日期)";
                case GroupBy.Day:
                    return e.startedAt.Length >= 10 ? e.startedAt.Substring(0, 10) : "(无日期)";
                case GroupBy.Persona: return e.personaId;
                case GroupBy.Model:   return e.model + (e.unknownModel ? " ⚠" : "");
                case GroupBy.Source:  return e.fromEditor ? "试聊台（调试）" : "游戏内";
                default:
                    return $"{e.startedAt}　{e.personaId}" + (e.fromEditor ? "　[试聊]" : "");
            }
        }

        // ──────────────── 绘制 ────────────────

        void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(_scanError))
            {
                EditorGUILayout.HelpBox(_scanError, MessageType.Info);
                return;
            }

            var src = Filtered();
            DrawSummary(src);

            var groups = Aggregate(src);
            double total = 0;
            foreach (var g in groups) total += g.cost;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            {
                DrawRow(GroupHeader(), "会话", "请求", "输入 tok", "输出 tok", "耗时", "花费", "占比",
                        EditorStyles.boldLabel);
                foreach (var g in groups)
                {
                    DrawRow(g.key,
                            g.sessions.ToString(),
                            g.requests.ToString(),
                            g.promptTokens.ToString("N0"),
                            g.outputTokens.ToString("N0"),
                            $"{g.seconds:0}s",
                            $"${g.cost:0.0000}",
                            total > 0 ? $"{g.cost / total * 100:0.0}%" : "-",
                            EditorStyles.label);
                }
                if (groups.Count == 0)
                    EditorGUILayout.LabelField("（没有符合条件的日志）", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(
                _recalc
                    ? "金额＝按当前单价表重算。历史日志里存的数字是当时的估算（旧版本一律按 Flash Lite 算），可能不准。"
                    : "金额＝日志里存的原始估算值（旧版本一律按 Flash Lite 算，换过模型的话偏低）。",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(44)))
                    Rescan();

                GUILayout.Label("分组", EditorStyles.miniLabel, GUILayout.Width(28));
                _groupBy = (GroupBy)EditorGUILayout.EnumPopup(
                    _groupBy, EditorStyles.toolbarPopup, GUILayout.Width(90));

                _includeGame = GUILayout.Toggle(_includeGame, "游戏内",
                                                EditorStyles.toolbarButton, GUILayout.Width(50));
                _includeEditor = GUILayout.Toggle(_includeEditor, "试聊台",
                                                  EditorStyles.toolbarButton, GUILayout.Width(50));

                _recalc = GUILayout.Toggle(_recalc, new GUIContent("按当前单价重算",
                        "历史日志里的金额是当时算的（旧版本一律按 Flash Lite），可能不准。\n" +
                        "日志里存了 token 数与模型名，所以能拿当前单价表重算一遍"),
                    EditorStyles.toolbarButton, GUILayout.Width(100));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("导出 CSV", EditorStyles.toolbarButton, GUILayout.Width(64)))
                    ExportCsv();
            }
            EditorGUILayout.EndHorizontal();
        }

        void DrawSummary(List<Entry> src)
        {
            double cost = 0;
            int requests = 0, sessions = src.Count, pt = 0, ot = 0;
            float seconds = 0;
            string first = null, last = null;
            foreach (var e in src)
            {
                cost += e.Cost(_recalc);
                requests += e.Requests;
                pt += e.promptTokens;
                ot += e.outputTokens;
                seconds += e.seconds;
                if (!string.IsNullOrEmpty(e.startedAt))
                {
                    if (first == null || string.CompareOrdinal(e.startedAt, first) < 0) first = e.startedAt;
                    if (last == null || string.CompareOrdinal(e.startedAt, last) > 0) last = e.startedAt;
                }
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.LabelField(
                    $"总花费 ${cost:0.0000}　·　{sessions} 场　{requests} 次请求　·　" +
                    $"{(pt + ot) / 1000f:0.0}k token　·　{seconds / 60f:0.0} 分钟",
                    EditorStyles.boldLabel);
                if (first != null)
                    EditorGUILayout.LabelField($"时间范围：{first} → {last}", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        string GroupHeader()
        {
            switch (_groupBy)
            {
                case GroupBy.Month:   return "月份";
                case GroupBy.Day:     return "日期";
                case GroupBy.Persona: return "人格";
                case GroupBy.Model:   return "模型";
                case GroupBy.Source:  return "来源";
                default:              return "会话";
            }
        }

        static void DrawRow(string a, string b, string c, string d, string e, string f,
                            string g, string h, GUIStyle style)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(a, style, GUILayout.MinWidth(160));
            EditorGUILayout.LabelField(b, style, GUILayout.Width(50));
            EditorGUILayout.LabelField(c, style, GUILayout.Width(50));
            EditorGUILayout.LabelField(d, style, GUILayout.Width(80));
            EditorGUILayout.LabelField(e, style, GUILayout.Width(80));
            EditorGUILayout.LabelField(f, style, GUILayout.Width(60));
            EditorGUILayout.LabelField(g, style, GUILayout.Width(80));
            EditorGUILayout.LabelField(h, style, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
        }

        // ──────────────── 导出 ────────────────

        void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel(
                "导出花费报表", VNAiTalkLog.ResolveDirectory(),
                $"ai_cost_{DateTime.Now:yyyyMMdd}", "csv");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            // 逐场导出（比聚合结果信息量大，进 Excel 想怎么透视都行）
            sb.AppendLine("开始时间,来源,人格,模型,轮数,总结请求,输入token,输出token含思考," +
                          "耗时秒,存储金额USD,重算金额USD,模型单价已知");
            foreach (var e in Filtered())
            {
                sb.AppendLine(string.Join(",",
                    Csv(e.startedAt),
                    Csv(e.fromEditor ? "试聊台" : "游戏内"),
                    Csv(e.personaId),
                    Csv(e.model),
                    e.turns.ToString(),
                    e.summaryRequests.ToString(),
                    e.promptTokens.ToString(),
                    e.outputTokens.ToString(),
                    e.seconds.ToString("0.0", CultureInfo.InvariantCulture),
                    e.storedCost.ToString("0.000000", CultureInfo.InvariantCulture),
                    e.recalcCost.ToString("0.000000", CultureInfo.InvariantCulture),
                    e.unknownModel ? "否" : "是"));
            }

            try
            {
                // UTF-8 BOM：没有它 Excel 打开中文会乱码
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Debug.Log($"[VNAiCost] 已导出：{path}");
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[VNAiCost] 导出失败：{e.Message}");
            }
        }

        static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Contains(",") || s.Contains("\"")
                ? "\"" + s.Replace("\"", "\"\"") + "\""
                : s;
        }
    }
}
