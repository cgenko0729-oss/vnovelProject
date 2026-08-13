using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>日记本里的一条。正文是 AI 用主角口吻写的。</summary>
    [Serializable]
    public class VNAiDiaryEntry
    {
        public string id;               // 唯一 id（时间戳），删除与去重用
        public string savedAt;          // "2026-08-14 01:32"
        public string characterId;
        public string displayName;      // 写这条时她的显示名（改名后旧条目不受影响）
        public string place;
        public string body;             // ★ 主角口吻的日记正文
        public List<string> topics = new List<string>();
        public int affectionDelta;
        public int turns;
        public string logFile;          // 对应的 AiTalkLogs 文件名（开发期对照用）
    }

    /// <summary>
    /// 日记本 —— **全局永久存储**，与 20 槽存档完全分离。
    /// 文件：persistentDataPath/vn_ai_diary.json，每写一条立即落盘。
    ///
    /// 【为什么和记忆的存储语义相反】
    /// `VNAiMemory` 是剧情状态，必须跟着存档回退（读旧档她不该记得未来）。
    /// 日记本是**玩家的收藏品**，和 CG 画廊、大头贴相册同类——
    /// 玩家真实经历过的东西不该因为读档而消失。两者语义不同，所以分两套存储。
    ///
    /// 代价是可能出现「日记里有第 5 章的条目，但读回第 3 章的档时她不记得」——
    /// 这是刻意的：日记是玩家的记录，记忆是角色的记忆，本来就不是一回事。
    /// </summary>
    public static class VNAiDiary
    {
        /// <summary>最多留多少条，超出丢最早的（一条约 200 字，200 条约 40KB）</summary>
        public const int Capacity = 200;

        const string FileName = "vn_ai_diary.json";

        [Serializable]
        class SaveShape
        {
            public List<VNAiDiaryEntry> entries = new List<VNAiDiaryEntry>();
        }

        static List<VNAiDiaryEntry> _entries;

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        static void EnsureLoaded()
        {
            if (_entries != null) return;
            _entries = new List<VNAiDiaryEntry>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var data = JsonUtility.FromJson<SaveShape>(
                    File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                if (data?.entries != null)
                    foreach (var e in data.entries)
                        if (e != null) _entries.Add(e);
            }
            catch (Exception e)
            {
                // 读不出来按空日记处理，绝不让它把游戏拦住
                Debug.LogError($"[VNAiDiary] 日记读取失败（按空日记处理）：{e.Message}");
            }
        }

        static void Save()
        {
            try
            {
                var shape = new SaveShape { entries = _entries };
                File.WriteAllText(FilePath, JsonUtility.ToJson(shape, true),
                    new System.Text.UTF8Encoding(true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VNAiDiary] 日记写入失败：{e.Message}");
            }
        }

        /// <summary>全部条目，**最新的在最前**（UI 直接按这个顺序列）。</summary>
        public static IReadOnlyList<VNAiDiaryEntry> All
        {
            get { EnsureLoaded(); return _entries; }
        }

        public static int Count { get { EnsureLoaded(); return _entries.Count; } }

        /// <summary>写一条并立即落盘。</summary>
        public static void Add(VNAiDiaryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.body)) return;
            EnsureLoaded();

            if (string.IsNullOrEmpty(entry.id))
                entry.id = DateTime.Now.ToString("yyyyMMddHHmmssfff");

            _entries.Insert(0, entry);                       // 最新在最前
            while (_entries.Count > Capacity) _entries.RemoveAt(_entries.Count - 1);
            Save();
        }

        /// <summary>按角色筛（日记本的角色分页用）。</summary>
        public static List<VNAiDiaryEntry> For(string characterId)
        {
            EnsureLoaded();
            var list = new List<VNAiDiaryEntry>();
            foreach (var e in _entries)
                if (e != null && (string.IsNullOrEmpty(characterId) || e.characterId == characterId))
                    list.Add(e);
            return list;
        }

        /// <summary>日记里出现过的角色 id（做分页标签用），按最近写过的顺序。</summary>
        public static List<string> Characters()
        {
            EnsureLoaded();
            var ids = new List<string>();
            foreach (var e in _entries)
                if (e != null && !string.IsNullOrEmpty(e.characterId) &&
                    !ids.Contains(e.characterId)) ids.Add(e.characterId);
            return ids;
        }

        public static bool Remove(string id)
        {
            EnsureLoaded();
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i] != null && _entries[i].id == id)
                {
                    _entries.RemoveAt(i);
                    Save();
                    return true;
                }
            return false;
        }

        /// <summary>清空（调试用；玩家侧不给这个入口）。</summary>
        public static void ClearAll()
        {
            EnsureLoaded();
            _entries.Clear();
            Save();
        }

        /// <summary>下次访问重新读盘（编辑器里手改过 json 后用）。</summary>
        public static void Invalidate() => _entries = null;
    }
}
