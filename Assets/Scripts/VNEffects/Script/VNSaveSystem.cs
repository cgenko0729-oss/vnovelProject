using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>存档数据（JSON 序列化）</summary>
    [System.Serializable]
    public class VNSaveData
    {
        public int commandIndex;          // 恢复点（正在显示的那句台词的命令索引）
        public string savedAt;            // 保存时间
        public string lastLine;           // 最后一句台词（存档预览用）

        public List<string> flagNames = new List<string>();
        public List<int> flagValues = new List<int>();

        public string backgroundId;
        public string weather;
        public string mood;
        public List<string> fxOn = new List<string>(); // 处于开启状态的 fx 名

        [System.Serializable]
        public class CharSave
        {
            public string id;
            public float x;
            public string expr;
        }
        public List<CharSave> characters = new List<CharSave>();
    }

    /// <summary>
    /// 存档读写：JSON 文件存到 persistentDataPath，多槽位。
    /// 快照内容 = 脚本指针 + 全部 flag + 舞台状态（背景/天气/色调/fx 开关/在场角色）。
    /// </summary>
    public static class VNSaveSystem
    {
        static string PathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"vn_save_{slot}.json");

        public static bool HasSave(int slot) => File.Exists(PathFor(slot));

        public static void Save(int slot, VNSaveData data)
        {
            data.savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            data.flagNames.Clear();
            data.flagValues.Clear();
            foreach (var kv in VNFlags.All)
            {
                data.flagNames.Add(kv.Key);
                data.flagValues.Add(kv.Value);
            }

            File.WriteAllText(PathFor(slot), JsonUtility.ToJson(data, true),
                System.Text.Encoding.UTF8);
            Debug.Log($"[VNSave] 已保存到槽位 {slot}：{PathFor(slot)}");
        }

        public static VNSaveData Load(int slot)
        {
            if (!HasSave(slot)) return null;
            try
            {
                var data = JsonUtility.FromJson<VNSaveData>(
                    File.ReadAllText(PathFor(slot), System.Text.Encoding.UTF8));

                VNFlags.Clear();
                for (int i = 0; i < data.flagNames.Count && i < data.flagValues.Count; i++)
                    VNFlags.Set(data.flagNames[i], data.flagValues[i]);

                Debug.Log($"[VNSave] 已读取槽位 {slot}（{data.savedAt}）");
                return data;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNSave] 槽位 {slot} 读取失败：{e.Message}");
                return null;
            }
        }
    }
}
