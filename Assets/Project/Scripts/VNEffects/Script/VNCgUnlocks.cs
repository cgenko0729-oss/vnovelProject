using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// CG 鉴赏解锁记录 —— 全局永久存储，与 20 槽存档系统完全分离。
    /// 玩家看过一次的 CG 永久解锁：读旧档、开新周目都不会丢
    /// （所以不能存进 VNFlags —— 那是跟随存档快照走的）。
    /// 文件：persistentDataPath/vn_cg_unlocks.json，解锁时立即落盘。
    /// P2 的鉴赏画廊将从这里读取解锁列表。
    /// </summary>
    public static class VNCgUnlocks
    {
        [System.Serializable]
        class SaveShape
        {
            public List<string> ids = new List<string>();
        }

        static HashSet<string> _unlocked;

        static string FilePath =>
            Path.Combine(Application.persistentDataPath, "vn_cg_unlocks.json");

        static void EnsureLoaded()
        {
            if (_unlocked != null) return;
            _unlocked = new HashSet<string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonUtility.FromJson<SaveShape>(
                        File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                    if (data?.ids != null)
                        foreach (var id in data.ids) _unlocked.Add(id);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNCg] 解锁记录读取失败（按全未解锁处理）：{e.Message}");
            }
        }

        /// <summary>标记一张 CG 已解锁（重复调用无害；有新增才写盘）</summary>
        public static void Unlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureLoaded();
            if (!_unlocked.Add(id)) return;
            try
            {
                var data = new SaveShape { ids = new List<string>(_unlocked) };
                data.ids.Sort(); // 文件内容稳定，便于人工查看/对比
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true),
                    System.Text.Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNCg] 解锁记录写入失败：{e.Message}");
            }
        }

        public static bool IsUnlocked(string id)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(id) && _unlocked.Contains(id);
        }

        /// <summary>已解锁的全部 CG id（鉴赏画廊用）</summary>
        public static IReadOnlyCollection<string> All
        {
            get { EnsureLoaded(); return _unlocked; }
        }
    }
}
