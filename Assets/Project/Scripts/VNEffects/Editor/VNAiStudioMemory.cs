using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VNEffects;

namespace VNEffectsEditor
{
    /// <summary>一套具名的记忆场景，如「初次见面（空）」「聊过 3 次」「好感 80 的老朋友」。</summary>
    [Serializable]
    public class VNAiStudioMemoryPreset
    {
        public string name = "新预设";
        public List<VNAiMemoryEntry> entries = new List<VNAiMemoryEntry>();
    }

    /// <summary>
    /// 试聊台的记忆预设存储 + 三个导入来源。
    ///
    /// 【为什么不直接读写运行时 VNAiMemory】
    ///   那一份是**存档态**——跟着存档走、被读档覆盖、域重载就清空。
    ///   编辑器往里写等于凭空制造「读旧档她却记得未来」的幽灵状态。
    ///   所以这里完全独立：自己的文件、自己的生命周期，
    ///   只在组装 prompt 那一刻把条目喂进 VNAiContext。
    ///
    /// 【落盘位置】
    ///   &lt;项目根&gt;/AiTalkStudio/Memories/*.json ——
    ///   与 AiTalkLogs/ 对称（都是本地调试产物，不进 git）。
    /// </summary>
    public static class VNAiStudioMemory
    {
        public const string RootFolder = "AiTalkStudio";
        public const string MemoryFolder = "Memories";

        public static string Directory
        {
            get
            {
                string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName
                              ?? Application.persistentDataPath;
                return Path.Combine(root, RootFolder, MemoryFolder);
            }
        }

        // ──────────────── 预设文件 ────────────────

        /// <summary>目录里全部预设名（不含扩展名），按名字排序。</summary>
        public static List<string> ListPresets()
        {
            var list = new List<string>();
            try
            {
                if (!System.IO.Directory.Exists(Directory)) return list;
                foreach (string f in System.IO.Directory.GetFiles(Directory, "*.json"))
                    list.Add(Path.GetFileNameWithoutExtension(f));
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 读取记忆预设目录失败：{e.Message}");
            }
            return list;
        }

        public static VNAiStudioMemoryPreset Load(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            try
            {
                string path = PathFor(name);
                if (!File.Exists(path)) return null;
                var p = JsonUtility.FromJson<VNAiStudioMemoryPreset>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (p != null && string.IsNullOrEmpty(p.name)) p.name = name;
                return p;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 记忆预设「{name}」读取失败：{e.Message}");
                return null;
            }
        }

        public static bool Save(VNAiStudioMemoryPreset preset)
        {
            if (preset == null || string.IsNullOrWhiteSpace(preset.name)) return false;
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(PathFor(preset.name),
                                  JsonUtility.ToJson(preset, true), new UTF8Encoding(true));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 记忆预设「{preset.name}」写入失败：{e.Message}");
                return false;
            }
        }

        public static void Delete(string name)
        {
            try
            {
                string path = PathFor(name);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 记忆预设「{name}」删除失败：{e.Message}");
            }
        }

        static string PathFor(string name) => Path.Combine(Directory, Sanitize(name) + ".json");

        static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "unnamed";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // ──────────────── 注入：组装成 prompt 用的两段 ────────────────
        //
        // 运行时走 VNAiMemory.BuildContext / TopicsOf（读静态列表）。
        // 这里对着预设条目做同样的事，但**刻意重写一遍而不是复用**——
        // 复用就得先把条目灌进 VNAiMemory 静态列表，那正是上面说的不能碰的东西。
        // 代价是这两段格式要和 VNAiMemory 保持同步（改那边记得改这边）。

        /// <summary>把某角色的记忆条目组装成「你还记得的事」那一段。</summary>
        public static string BuildContext(VNAiStudioMemoryPreset preset, string characterId,
                                          int maxEntries)
        {
            var list = For(preset, characterId, maxEntries);
            if (list.Count == 0) return null;

            var sb = new StringBuilder(256);
            sb.Append("你和「我」之前已经聊过 ").Append(list.Count).AppendLine(" 次，按时间从早到晚：");
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append("  ").Append(i + 1).Append('.');
                if (!string.IsNullOrWhiteSpace(list[i].place))
                    sb.Append('（').Append(list[i].place).Append('）');
                sb.AppendLine(list[i].summary);
            }

            var facts = new List<string>();
            foreach (var e in list)
                if (e.facts != null)
                    foreach (string f in e.facts)
                        if (!string.IsNullOrWhiteSpace(f) && !facts.Contains(f)) facts.Add(f);
            if (facts.Count > 0)
            {
                sb.AppendLine("你记得的具体事情：");
                foreach (string f in facts) sb.Append("  - ").AppendLine(f);
            }
            return sb.ToString();
        }

        /// <summary>已聊过的话题清单（去重，作为硬性回避清单注入）。</summary>
        public static List<string> TopicsOf(VNAiStudioMemoryPreset preset, string characterId,
                                            int maxEntries)
        {
            var topics = new List<string>();
            foreach (var e in For(preset, characterId, maxEntries))
                if (e.topics != null)
                    foreach (string t in e.topics)
                        if (!string.IsNullOrWhiteSpace(t) && !topics.Contains(t)) topics.Add(t);
            return topics;
        }

        /// <summary>
        /// 该角色的记忆条目，取最近 maxEntries 条。
        /// characterId 为空 = 不过滤（试聊台上人格可能还没绑角色）。
        /// </summary>
        public static List<VNAiMemoryEntry> For(VNAiStudioMemoryPreset preset, string characterId,
                                                int maxEntries)
        {
            var list = new List<VNAiMemoryEntry>();
            if (preset?.entries == null) return list;

            foreach (var e in preset.entries)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.summary)) continue;
                if (!string.IsNullOrEmpty(characterId) &&
                    !string.IsNullOrEmpty(e.characterId) &&
                    e.characterId != characterId) continue;
                list.Add(e);
            }

            if (maxEntries > 0 && list.Count > maxEntries)
                list.RemoveRange(0, list.Count - maxEntries);
            return list;
        }

        // ──────────────── 导入①：从游戏存档槽 ────────────────

        /// <summary>有存档的槽位号。</summary>
        public static List<int> ListSaveSlots()
        {
            var slots = new List<int>();
            for (int i = 0; i < VNSaveSystem.SlotCount; i++)
                if (File.Exists(SavePathFor(i))) slots.Add(i);
            return slots;
        }

        /// <summary>
        /// 读某个存档槽里的 aiMemories。
        ///
        /// ★ 刻意不走 VNSaveSystem.Load()：那个方法会 VNFlags.Clear() 再灌入存档里的
        ///   全部 flag。在编辑器里点一下「导入」就把工程的 flag 状态冲掉，
        ///   下次进 Play Mode 行为莫名其妙。这里只做纯读，无任何副作用。
        /// </summary>
        public static List<VNAiMemoryEntry> ImportFromSave(int slot)
        {
            var list = new List<VNAiMemoryEntry>();
            try
            {
                string path = SavePathFor(slot);
                if (!File.Exists(path)) return list;
                var data = JsonUtility.FromJson<VNSaveData>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (data?.aiMemories != null)
                    foreach (var e in data.aiMemories)
                        if (e != null) list.Add(e);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 存档槽 {slot} 读取失败：{e.Message}");
            }
            return list;
        }

        static string SavePathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"vn_save_{slot}.json");

        // ──────────────── 导入②：从对话日志 ────────────────

        /// <summary>一份可导入的日志（列表显示用）。</summary>
        public class LogFile
        {
            public string path;
            public string display;      // 「2026-08-13 23:14 星野结衣_日常 · 8 轮」
            public VNAiTalkLog.Session session;
        }

        /// <summary>扫 AiTalkLogs/（含 Editor/ 子目录）里的 .json，最近的排前面。</summary>
        public static List<LogFile> ListLogs(int max = 30)
        {
            var files = new List<LogFile>();
            try
            {
                string dir = VNAiTalkLog.ResolveDirectory();
                if (!System.IO.Directory.Exists(dir)) return files;

                var paths = new List<string>(
                    System.IO.Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories));
                paths.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));

                foreach (string p in paths)
                {
                    if (files.Count >= max) break;
                    try
                    {
                        var s = JsonUtility.FromJson<VNAiTalkLog.Session>(
                            File.ReadAllText(p, Encoding.UTF8));
                        if (s == null || s.turns == null || s.turns.Count == 0) continue;
                        files.Add(new LogFile
                        {
                            path = p,
                            session = s,
                            display = $"{s.startedAt}  {s.personaId}  · {s.turns.Count} 轮" +
                                      (p.Contains(Path.DirectorySeparatorChar + "Editor" +
                                                  Path.DirectorySeparatorChar) ? "  [试聊]" : ""),
                        });
                    }
                    catch { /* 单个日志坏了不影响列出其他的 */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiStudio] 日志目录扫描失败：{e.Message}");
            }
            return files;
        }

        /// <summary>
        /// 把一份日志变成一条记忆——**要发一次总结请求**（约 $0.001）。
        ///
        /// 为什么不做「免费的骨架导入」：注入 prompt 时真正被用到的是
        /// summary / topics / facts 三样，日志里一样都没有（总结不写进日志）。
        /// 导入一条三样全空的条目等于什么都没导入，白白让人以为记忆生效了。
        ///
        /// 做法是拿日志里的 playerSaid / reply 重建一次会话历史（不发请求，
        /// BuildRequest 只是组装），再走现成的 BuildSummaryRequest。
        /// </summary>
        public static IEnumerator ImportFromLogCo(
            LogFile log, VNAiPersonaDef persona, string playerName,
            Action<VNAiMemoryEntry, string> onDone)
        {
            if (log?.session == null || persona == null)
            {
                onDone?.Invoke(null, "日志或人格为空");
                yield break;
            }

            var convo = new VNAiConversation(persona);
            var ctx = new VNAiContext { playerName = playerName, place = log.session.kwargs };

            foreach (var t in log.session.turns)
            {
                convo.BuildRequest(t.playerSaid, ctx);
                convo.RecordReply(new VNAiTurn { reply = t.reply });
            }

            var req = convo.BuildSummaryRequest(ctx, playerName);
            VNAiResult res = null;
            yield return VNAiClient.Send(req, r => res = r);
            while (res == null) yield return null;

            // 这一次请求不属于任何一场对话，进不了会话日志，所以至少让它在 Console 留痕——
            // 否则「导入记忆」就成了一笔查不到的开销
            Debug.Log($"[VNAiStudio] 从日志总结记忆：{res.elapsedSeconds:0.0}s　" +
                      $"{res.promptTokens}+{res.outputTokens + res.thoughtsTokens} tok　" +
                      $"≈${res.EstimatedCostUsd:0.000000}（{persona.ResolveModel()}）");

            if (!res.ok)
            {
                onDone?.Invoke(null, $"{res.failure}：{res.errorMessage}");
                yield break;
            }
            if (!VNAiConversation.TryParseSummary(res.text, out var summary, out string err))
            {
                onDone?.Invoke(null, err);
                yield break;
            }

            onDone?.Invoke(new VNAiMemoryEntry
            {
                personaId = log.session.personaId,
                characterId = log.session.characterId,
                place = ExtractPlace(log.session.kwargs),
                savedAt = log.session.startedAt,
                summary = summary.summary,
                topics = summary.topics,
                facts = summary.facts,
                affectionDelta = log.session.affectionTotal,
                turns = log.session.turns.Count,
            }, null);
        }

        /// <summary>从日志的 kwargs 串里尽力抠出 place:。抠不到就留空，让人手填。</summary>
        static string ExtractPlace(string kwargs)
        {
            if (string.IsNullOrEmpty(kwargs)) return "";
            foreach (string part in kwargs.Split(' ', ','))
            {
                string p = part.Trim();
                if (p.StartsWith("place:", StringComparison.OrdinalIgnoreCase))
                    return p.Substring("place:".Length).Trim();
            }
            return "";
        }
    }
}
