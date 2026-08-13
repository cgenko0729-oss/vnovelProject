using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VNEffects
{
    /// <summary>一场 AI 聊天结束后沉淀下来的记忆。</summary>
    [Serializable]
    public class VNAiMemoryEntry
    {
        public string personaId;
        public string characterId;
        public string place;            // 当时的场景（剧本 place:）
        public string savedAt;          // 真实时间，仅用于排序与显示
        public string summary;          // 一句话概括，第三人称，给模型自己看
        public List<string> topics = new List<string>();  // 聊过的话题标签 ★去重的关键
        public List<string> facts = new List<string>();   // 她透露的具体信息
        public int affectionDelta;
        public int turns;
    }

    /// <summary>
    /// 跨场记忆 —— **存档态**。她记得的事跟着存档走：
    /// 读回第 3 章的档，她就不会提起第 5 章聊过的东西。
    ///
    /// 【为什么不像 CG 解锁那样做成全局】
    /// CG 解锁是「玩家看过就是看过」的元数据，回退没有意义。
    /// 但对话记忆是**剧情状态**——读旧档时她还记得「未来」发生的事就穿帮了。
    /// 所以这里跟 VNFlags 一样，是会被存档快照捕获与还原的静态状态。
    /// （日记本是另一回事：那是玩家的收藏品，走全局，见 VNAiDiary。）
    ///
    /// 【怎么帮 AI「少重复」】
    /// 注入 prompt 时分成两段：
    ///   1. 往事摘要——给上下文，让她能自然地接着上次的关系往下走
    ///   2. **已聊过的话题清单**——这才是去重的主力。实测让模型避开一个
    ///      明确列出的话题标签，比让它读一段散文摘要然后「别重复」有效得多。
    /// </summary>
    public static class VNAiMemory
    {
        /// <summary>默认保留最近多少场（人格资产可覆盖）</summary>
        public const int DefaultCapacity = 15;

        static readonly List<VNAiMemoryEntry> _entries = new List<VNAiMemoryEntry>();

        /// <summary>全部记忆（按加入顺序，越靠后越新）。只读用途。</summary>
        public static IReadOnlyList<VNAiMemoryEntry> All => _entries;

        public static int Count => _entries.Count;

        /// <summary>记一场。超出容量时丢掉最早的。</summary>
        public static void Add(VNAiMemoryEntry entry, int capacity = DefaultCapacity)
        {
            if (entry == null) return;
            _entries.Add(entry);

            int cap = Mathf.Max(1, capacity);
            while (_entries.Count > cap) _entries.RemoveAt(0);
        }

        /// <summary>某个角色的记忆，最新的在最后。</summary>
        public static List<VNAiMemoryEntry> For(string characterId)
        {
            var list = new List<VNAiMemoryEntry>();
            if (string.IsNullOrEmpty(characterId)) return list;
            foreach (var e in _entries)
                if (e != null && e.characterId == characterId) list.Add(e);
            return list;
        }

        /// <summary>
        /// 组装注入 system prompt 的「往事」段。返回空串 = 没有记忆，调用方跳过该段。
        /// </summary>
        /// <param name="maxEntries">最多带几场（0 = 全部）</param>
        public static string BuildContext(string characterId, int maxEntries = 0)
        {
            var list = For(characterId);
            if (list.Count == 0) return "";

            int from = maxEntries > 0 ? Mathf.Max(0, list.Count - maxEntries) : 0;
            var sb = new StringBuilder();

            sb.Append("你和「我」之前已经聊过 ").Append(list.Count - from)
              .AppendLine(" 次，按时间从早到晚：");
            for (int i = from; i < list.Count; i++)
            {
                var e = list[i];
                sb.Append("  ").Append(i - from + 1).Append(". ");
                if (!string.IsNullOrWhiteSpace(e.place)) sb.Append('（').Append(e.place).Append("）");
                sb.AppendLine(e.summary);
            }

            // 关键事实单独列——这些是她「应该记得」的具体信息
            var facts = new List<string>();
            for (int i = from; i < list.Count; i++)
                if (list[i].facts != null)
                    foreach (string f in list[i].facts)
                        if (!string.IsNullOrWhiteSpace(f) && !facts.Contains(f)) facts.Add(f);
            if (facts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("你记得的具体事情：");
                foreach (string f in facts) sb.Append("  - ").AppendLine(f);
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 已聊过的话题清单（去重、保序）。这是「少重复」的主力，
        /// 调用方应把它作为**硬性回避要求**写进提示词，而不是混在摘要里。
        /// </summary>
        public static List<string> TopicsOf(string characterId, int maxEntries = 0)
        {
            var list = For(characterId);
            var topics = new List<string>();
            int from = maxEntries > 0 ? Mathf.Max(0, list.Count - maxEntries) : 0;
            for (int i = from; i < list.Count; i++)
            {
                if (list[i].topics == null) continue;
                foreach (string t in list[i].topics)
                {
                    string s = t?.Trim();
                    if (!string.IsNullOrEmpty(s) && !topics.Contains(s)) topics.Add(s);
                }
            }
            return topics;
        }

        public static void Clear() => _entries.Clear();

        // ──────────────── 存档（三处同步之一）────────────────

        /// <summary>存档时写入快照。</summary>
        public static void CaptureSnapshot(VNSaveData data)
        {
            if (data == null) return;
            data.aiMemories = new List<VNAiMemoryEntry>(_entries);
        }

        /// <summary>
        /// 读档时还原。旧存档没有这个字段 → JsonUtility 给出空列表 → 记忆清空，
        /// 等价于「那时候还没聊过」，语义正确。
        /// </summary>
        public static void RestoreSnapshot(VNSaveData data)
        {
            _entries.Clear();
            if (data?.aiMemories == null) return;
            foreach (var e in data.aiMemories)
                if (e != null) _entries.Add(e);
        }
    }
}
