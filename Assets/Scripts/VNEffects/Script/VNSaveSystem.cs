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
        public string chapter;            // 当前章节文件名（旧存档为空时沿用场景默认章节）
        public string savedAt;            // 保存时间
        public string lastLine;           // 最后一句台词（存档预览用）

        public List<string> flagNames = new List<string>();
        public List<int> flagValues = new List<int>();

        public string backgroundId;
        public string weather;
        public string mood;
        public string bgm;
        public float bgmVol = 1f;         // bgm 命令的 vol: 参数（旧存档缺省 = 1）
        public bool portraitOff;                       // 对话头像被 portrait off 关闭
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
        public const int SlotCount = 20;

        static string PathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"vn_save_{slot}.json");

        static string ThumbnailPathFor(int slot) =>
            Path.Combine(Application.persistentDataPath, $"vn_save_{slot}.png");

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

        /// <summary>保存 JSON 与界面缩略图；旧的无图存档仍可正常读取。</summary>
        public static void Save(int slot, VNSaveData data, Texture2D thumbnail)
        {
            Save(slot, data);
            if (thumbnail == null) return;
            try
            {
                File.WriteAllBytes(ThumbnailPathFor(slot), thumbnail.EncodeToPNG());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNSave] 槽位 {slot} 缩略图保存失败：{e.Message}");
            }
        }

        /// <summary>只读槽位信息，不修改 VNFlags；用于存读档界面的 20 槽预览。</summary>
        public static VNSaveData Peek(int slot)
        {
            if (!HasSave(slot)) return null;
            try
            {
                return JsonUtility.FromJson<VNSaveData>(
                    File.ReadAllText(PathFor(slot), System.Text.Encoding.UTF8));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNSave] 槽位 {slot} 元数据读取失败：{e.Message}");
                return null;
            }
        }

        /// <summary>读取槽位 PNG 缩略图；调用方负责 Destroy 返回的 Texture2D。</summary>
        public static Texture2D LoadThumbnail(int slot)
        {
            string path = ThumbnailPathFor(slot);
            if (!File.Exists(path)) return null;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGB24, false)
                {
                    name = $"SaveThumbnail_{slot}",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                if (texture.LoadImage(File.ReadAllBytes(path), true)) return texture;
                Object.Destroy(texture);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNSave] 槽位 {slot} 缩略图读取失败：{e.Message}");
            }
            return null;
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
