using System;
using System.Collections.Generic;
using System.Reflection;
using DG.Tweening;

namespace VNEffects
{
    /// <summary>
    /// 标在 <see cref="VNCharacterEmotes"/> 的动作方法上，声明「这是一个剧本可用的情绪动作」。
    /// 方法名本身就是剧本里的英文正名（emote 小雪 Tremble），这里只补中文显示名与别名。
    /// 加一个新动作 = 写一个 public Sequence 方法 + 打这个 attribute，
    /// 剧本 switch / 编辑器下拉 / Lint 白名单 / 互动模块别名全部由 <see cref="VNEmoteCatalog"/> 反射自动跟上。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class VNEmoteAttribute : Attribute
    {
        /// <summary>编辑器下拉里显示的中文名</summary>
        public readonly string label;
        /// <summary>额外别名（互动资产 / 秘密偷拍等处可写；方法名本身大小写不敏感地永远可用）</summary>
        public readonly string[] aliases;

        public VNEmoteAttribute(string label, params string[] aliases)
        {
            this.label = label;
            this.aliases = aliases ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// 情绪动作目录：**唯一真相**，其他地方（VNStage.Emote、VNScenarioSchema.EmoteNames、
    /// VNScenarioLinter、剧本编辑器中文名、VNInteractionModule）一律从这里取，谁也不再手写清单。
    /// 目录在首次访问时反射一次 <see cref="VNCharacterEmotes"/> 上带 <see cref="VNEmoteAttribute"/>
    /// 的方法（必须是 public 实例方法、无参、返回 Sequence），之后全部走缓存的开放实例委托，
    /// 不再有反射开销。方法按源码声明顺序排列，所以下拉顺序 = 文件里的顺序。
    /// 同名/同别名冲突只保留先声明的那个并打警告，不抛异常（别让一个拼错的别名炸掉整个编辑器）。
    /// </summary>
    public static class VNEmoteCatalog
    {
        public sealed class Entry
        {
            public string name;      // 英文正名 = 方法名，剧本与存档用
            public string label;     // 中文显示名
            public string[] aliases; // 别名（含中文）
            public Func<VNCharacterEmotes, Sequence> invoke;
        }

        static Entry[] _entries;
        static string[] _names;
        static Dictionary<string, Entry> _byKey;
        static Dictionary<string, string> _labels;

        /// <summary>全部动作（源码声明顺序）</summary>
        public static Entry[] Entries { get { Ensure(); return _entries; } }

        /// <summary>英文正名清单（编辑器下拉 / Lint 白名单用这份）</summary>
        public static string[] Names { get { Ensure(); return _names; } }

        /// <summary>正名 → 中文显示名（编辑器翻译用）</summary>
        public static Dictionary<string, string> Labels { get { Ensure(); return _labels; } }

        /// <summary>按正名或别名查（大小写不敏感）</summary>
        public static bool TryGet(string key, out Entry entry)
        {
            Ensure();
            entry = null;
            return !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key.Trim(), out entry);
        }

        public static bool Contains(string key) => TryGet(key, out _);

        public static string LabelOf(string name) =>
            TryGet(name, out var e) ? e.label : name;

        /// <summary>
        /// 在目标组件上播放动作；认不出名字返回 null（调用方自己决定要不要警告）。
        /// </summary>
        public static Sequence Invoke(VNCharacterEmotes target, string key)
        {
            if (target == null || !TryGet(key, out var e)) return null;
            return e.invoke(target);
        }

        /// <summary>给报错信息用的「可用：A/B/C」清单</summary>
        public static string NamesJoined(string sep = "/") => string.Join(sep, Names);

        static void Ensure()
        {
            if (_entries != null) return;

            var list = new List<Entry>();
            var byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            var labels = new Dictionary<string, string>();

            var methods = typeof(VNCharacterEmotes).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            // GetMethods 不保证顺序；MetadataToken 递增 = 源码声明顺序
            Array.Sort(methods, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

            foreach (var m in methods)
            {
                var attr = m.GetCustomAttribute<VNEmoteAttribute>();
                if (attr == null) continue;
                if (m.ReturnType != typeof(Sequence) || m.GetParameters().Length != 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[VNEmoteCatalog] {m.Name} 打了 [VNEmote] 但签名不是 public Sequence {m.Name}()，已跳过");
                    continue;
                }

                var entry = new Entry
                {
                    name = m.Name,
                    label = string.IsNullOrEmpty(attr.label) ? m.Name : attr.label,
                    aliases = attr.aliases,
                    invoke = (Func<VNCharacterEmotes, Sequence>)Delegate.CreateDelegate(
                        typeof(Func<VNCharacterEmotes, Sequence>), m),
                };

                if (!Register(byKey, entry.name, entry)) continue;
                foreach (var a in entry.aliases) Register(byKey, a, entry);
                list.Add(entry);
                labels[entry.name] = entry.label;
            }

            _entries = list.ToArray();
            _names = new string[_entries.Length];
            for (int i = 0; i < _entries.Length; i++) _names[i] = _entries[i].name;
            _byKey = byKey;
            _labels = labels;
        }

        static bool Register(Dictionary<string, Entry> byKey, string key, Entry entry)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            key = key.Trim();
            if (byKey.TryGetValue(key, out var existing))
            {
                if (existing != entry)
                    UnityEngine.Debug.LogWarning(
                        $"[VNEmoteCatalog] 「{key}」同时指向 {existing.name} 与 {entry.name}，保留先声明的 {existing.name}");
                return false;
            }
            byKey[key] = entry;
            return true;
        }
    }
}
