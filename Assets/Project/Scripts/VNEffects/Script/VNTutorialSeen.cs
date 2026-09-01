using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 「这篇教程看过了」的全局记录 —— 与 20 槽存档完全分离，
    /// 做法照抄 <see cref="VNCgUnlocks"/>。
    ///
    /// 【为什么不是 flag】
    /// flag 跟随存档快照走，读旧档就会「忘记」看过教程，于是新手引导又弹一遍；
    /// 开新周目更是每篇重看。看过教程是玩家的元知识，跟 CG 解锁同类，
    /// 属于全局永久记录。
    ///
    /// 文件：persistentDataPath/vn_tutorial_seen.json，有新增才写盘。
    /// 玩家想重看：设置面板「重置教程记录」，或剧本 tutorial &lt;id&gt; force:on。
    /// </summary>
    public static class VNTutorialSeen
    {
        const string EnabledKey = "VN.Tutorial.Hints";

        [System.Serializable]
        class SaveShape
        {
            public List<string> ids = new List<string>();
        }

        static HashSet<string> _seen;

        static string FilePath =>
            Path.Combine(Application.persistentDataPath, "vn_tutorial_seen.json");

        /// <summary>
        /// 「显示教程提示」总开关（设置面板里的那一项，存 PlayerPrefs）。
        /// 关掉后自动触发的教程一律不播；剧本写 force:on 的仍然会播
        /// ——那是作者点名要讲的，不是新手提示。
        /// </summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, 1) != 0;
            set => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
        }

        static void EnsureLoaded()
        {
            if (_seen != null) return;
            _seen = new HashSet<string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    var data = JsonUtility.FromJson<SaveShape>(
                        File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                    if (data?.ids != null)
                        foreach (var id in data.ids) _seen.Add(id);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNTutorial] 教程记录读取失败（按全未看处理）：{e.Message}");
            }
        }

        /// <summary>这篇教程看过没有</summary>
        public static bool Has(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            EnsureLoaded();
            return _seen.Contains(id);
        }

        /// <summary>标记为看过（重复调用无害；有新增才写盘）</summary>
        public static void Mark(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            EnsureLoaded();
            if (!_seen.Add(id)) return;
            Flush();
        }

        /// <summary>清空全部记录（设置面板的「重置教程记录」）</summary>
        public static void ResetAll()
        {
            EnsureLoaded();
            if (_seen.Count == 0) return;
            _seen.Clear();
            Flush();
        }

        static void Flush()
        {
            try
            {
                var data = new SaveShape { ids = new List<string>(_seen) };
                data.ids.Sort(); // 文件内容稳定，便于人工查看
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true),
                    System.Text.Encoding.UTF8);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VNTutorial] 教程记录写入失败：{e.Message}");
            }
        }
    }
}
