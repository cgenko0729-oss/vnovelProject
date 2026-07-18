using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>支持的界面/剧本语言。枚举顺序即 PlayerPrefs 存储值，勿改动已有项顺序。</summary>
    public enum VNLanguage
    {
        Chinese = 0,
        English = 1,
        Japanese = 2,
    }

    /// <summary>
    /// VNLocale —— 全项目本地化统一入口。
    ///
    ///   VNLocale.Language        当前语言（写入 PlayerPrefs，切换时触发 LanguageChanged）
    ///   VNLocale.T("save.empty") UI 字符串查表（表在 Resources/VNLocale/ui.&lt;code&gt;.txt）
    ///   VNLocale.T(key, args)    带 string.Format 参数的查表
    ///
    /// 表文件格式：每行 "key = value"，# 开头为注释，value 支持 \n 转义。
    /// 查表回退链：当前语言 → 中文（源语言）→ key 本身，保证任何情况下不显示空白。
    /// 剧本台词的翻译不走本表（见 VNScriptLocale，按剧本文件独立成表）。
    /// </summary>
    public static class VNLocale
    {
        const string PrefKey = "VN.Config.Language";
        static readonly string[] Codes = { "zh", "en", "ja" };

        static VNLanguage _language;
        static bool _loaded;
        static readonly Dictionary<string, Dictionary<string, string>> _tables =
            new Dictionary<string, Dictionary<string, string>>();

        /// <summary>语言切换后触发（先于事件已完成字体切换）。UI 组件订阅后自行重建文案。</summary>
        public static event Action LanguageChanged;

        public static VNLanguage Language
        {
            get
            {
                EnsureLoaded();
                return _language;
            }
            set
            {
                EnsureLoaded();
                if (_language == value) return;
                _language = value;
                PlayerPrefs.SetInt(PrefKey, (int)value);
                PlayerPrefs.Save();
                VNFont.HandleLanguageChanged(); // 先换字体，订阅者重建文案时才能拿到正确字体
                LanguageChanged?.Invoke();
            }
        }

        /// <summary>当前语言代码：zh / en / ja（剧本翻译表文件名后缀也用它）</summary>
        public static string Code => Codes[(int)Language];

        /// <summary>某语言的语言代码</summary>
        public static string CodeOf(VNLanguage lang) => Codes[(int)lang];

        /// <summary>语言的自称显示名（用于语言切换按钮，永远以该语言本身书写）</summary>
        public static string DisplayName(VNLanguage lang)
        {
            switch (lang)
            {
                case VNLanguage.English: return "English";
                case VNLanguage.Japanese: return "日本語";
                default: return "中文";
            }
        }

        /// <summary>UI 字符串查表；找不到时回退中文表，再回退 key 本身</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var table = TableFor(Code);
            if (table != null && table.TryGetValue(key, out var value)) return value;
            if (Code != "zh")
            {
                var zh = TableFor("zh");
                if (zh != null && zh.TryGetValue(key, out var fallback)) return fallback;
            }
            Debug.LogWarning($"[VNLocale] UI 字符串表缺少 key「{key}」（语言 {Code}）");
            return key;
        }

        /// <summary>带 string.Format 参数的查表（表内可用 {0} {1} 占位）</summary>
        public static string T(string key, params object[] args)
        {
            string format = T(key);
            try { return string.Format(format, args); }
            catch (FormatException)
            {
                Debug.LogWarning($"[VNLocale] key「{key}」的译文占位符与参数不匹配：{format}");
                return format;
            }
        }

        // ------------------------------------------------------------------

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            int stored = PlayerPrefs.GetInt(PrefKey, (int)VNLanguage.Chinese);
            _language = (VNLanguage)Mathf.Clamp(stored, 0, Codes.Length - 1);
        }

        static Dictionary<string, string> TableFor(string code)
        {
            if (_tables.TryGetValue(code, out var cached)) return cached;
            var asset = Resources.Load<TextAsset>("VNLocale/ui." + code);
            Dictionary<string, string> table = null;
            if (asset != null) table = ParseTable(asset.text);
            else Debug.LogWarning($"[VNLocale] 未找到 UI 字符串表 Resources/VNLocale/ui.{code}.txt");
            _tables[code] = table;
            return table;
        }

        /// <summary>解析 "key = value" 表；value 中的 \n 转成换行（剧本翻译表也用本格式）</summary>
        public static Dictionary<string, string> ParseTable(string text)
        {
            var table = new Dictionary<string, string>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                table[key] = value;
            }
            return table;
        }
    }
}
