using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VNEffects
{
    /// <summary>存档数据（JSON 序列化）</summary>
    [System.Serializable]
    public class VNSaveData
    {
        public int saveVersion = 3;       // 0/缺省 = call 栈加入前；2 = 无参数 call 栈
        public int commandIndex;          // 恢复点（正在显示的那句台词的命令索引）
        public string chapter;            // 当前章节文件名（旧存档为空时沿用场景默认章节）
        public string savedAt;            // 保存时间
        public string lastLine;           // 最后一句台词（存档预览用）

        [System.Serializable]
        public class CallFrameSave
        {
            public string chapter;        // 返回文件
            public int returnIndex;       // call 后的下一条命令
            public int sourceLine;        // call 所在物理行（诊断用）
            public List<string> parameterNames = new List<string>();
            public List<string> parameterValues = new List<string>();
        }
        public List<CallFrameSave> callStack = new List<CallFrameSave>();
        public List<string> parameterNames = new List<string>();
        public List<string> parameterValues = new List<string>();

        public List<string> flagNames = new List<string>();
        public List<int> flagValues = new List<int>();

        public string backgroundId;
        // 天气 id：飘落类为 VNWeatherDef 的 id（petals/maple/… 或自定义中文 id），
        // 其余为 VNWeather 枚举名。旧存档存的 "Petals"/"Rain" 照常认得。
        public string weather;
        // 飘落天气的剧本覆盖参数。density/speed/size 用 0 表示「未覆盖」；
        // wind 可以是负数（向左吹），所以另用一个 bool 标记，旧存档缺省 false = 未覆盖。
        public float weatherDensity;
        public float weatherSpeed;
        public float weatherSize;
        public bool weatherWindSet;
        public float weatherWind;
        // 背景无限滚动（bgscroll）。scrollOn=false 时其余字段无意义；
        // 累计偏移刻意不存——从哪一帧接着滚玩家看不出来，存了反而多一个要维护的数
        public bool scrollOn;
        public float scrollSpeed;
        public float scrollDir;
        public string scrollMode;                      // "Repeat" / "Mirror"
        public string mood;
        public string bgm;
        public float bgmVol = 1f;         // bgm 命令的 vol: 参数（旧存档缺省 = 1）
        public bool portraitOff;                       // 对话头像被 portrait off 关闭
        public List<string> fxOn = new List<string>(); // 处于开启状态的 fx 名（含被 CG 暂停的）
        public string cgId;                            // 显示中的 CG（空 = 无，旧存档缺省兼容）
        public bool cgKeepChars;                       // cg 命令的 chars:keep
        public bool cgKeepFx;                          // cg 命令的 fx:keep
        public string dialogueSkin;                    // 对话框皮肤 id（空 = 默认，旧存档兼容）
        public string choiceSkin;                      // 选项面板皮肤 id（空 = 默认）
        public string nameplateStyle;                  // 名字样式名（空 = 出厂样式，旧存档兼容）

        /// <summary>
        /// AI 自由聊天的跨场记忆。**必须跟着存档走**——读回旧档时她不该记得
        /// 「未来」聊过的事。旧存档没有这个字段时 JsonUtility 给空列表，
        /// 等价于「那时候还没聊过」，语义正确。
        /// （日记本是玩家的收藏品，走全局 JSON，不在这里，见 VNAiDiary。）
        /// </summary>
        public List<VNAiMemoryEntry> aiMemories = new List<VNAiMemoryEntry>();

        /// <summary>
        /// 液体喷溅的持续状态（liquid 命令）。
        /// 空中飞的水珠和屏幕上已经溅好的水渍都是瞬态的，读档不还原也不违和——
        /// 存的只是"还在喷 / 镜头是湿的 / 点击喷水模式开着"这三个会一直持续下去的开关。
        /// 旧存档缺这一段时 JsonUtility 会给出一个全 false 的实例，等价于"什么都没开"。
        /// </summary>
        [System.Serializable]
        public class LiquidSave
        {
            public bool sprayOn;
            public string sprayType;
            public float sprayX, sprayY;
            public float sprayPower, spraySpread, sprayRate, sprayScreen;
            // 喷射方向：内部用 float.NaN 表示"朝镜头"，但 JsonUtility 会把 NaN 写成
            // 非法 JSON，读回来是垃圾值。所以照 weatherWindSet 的先例另用一个 bool 标记，
            // 旧存档缺省 false = 朝镜头（也正是新的默认行为）。
            public bool sprayDirSet;
            public float sprayDir;

            public bool clickOn;
            public string clickType;
            public float clickPower, clickScreen;

            public bool wetOn;
            public string wetType;
            public float wetAmount;

            public bool cover;   // 水渍层是否盖住对话框
        }
        public LiquidSave liquid = new LiquidSave();

        [System.Serializable]
        public class CharSave
        {
            public string id;
            public float x;
            public string expr;
            public string marks;   // 常驻漫符的英文正名，逗号分隔（空 = 无，旧存档兼容）
            // 登场用的是日常向预设（crossfade/slidein/stepin/walkin）→ 不开周期扫光。
            // 旧存档缺省 false = 保持原来"一律开扫光"的行为。
            public bool casualEntrance;
        }
        public List<CharSave> characters = new List<CharSave>();

        // ---- SNS 手机聊天（旧存档全部缺省 = 未打开）----
        public bool snsOpen;                   // 存档瞬间聊天界面是否开着
        public string snsPeerId;               // 对方角色 id
        public string snsSessionId;            // 会话 id（默认同 peerId）
        public string snsTitle;                // 顶栏标题
        public string snsPlayerAlias;          // 剧本里代表玩家的说话者名（sns open 的 me:）
        // 本次会话已发出的全部消息，顺序即显示顺序。
        // 存档点必然停在某条消息上，因此这份列表天然就是"截断到存档点"的历史。
        public List<VNSnsMessage> snsMessages = new List<VNSnsMessage>();
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
