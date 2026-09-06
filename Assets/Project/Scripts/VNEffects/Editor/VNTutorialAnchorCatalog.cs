using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VNEffects.EditorTools
{
    /// <summary>
    /// 教程锚点目录：Edit Mode 下也能知道「有哪些锚点可以填」。
    ///
    /// 锚点是运行时由模块 <c>VNTutorialAnchors.Register</c> 登记的，Edit Mode 下注册表是空的。
    /// 但每个模块都把 id 放在 <c>public const string Anchor*</c> 常量上（VNBadmintonModule /
    /// VNSecretPhotoUi / VNUiAnchors），反射扫一遍就是一份静态目录；带参数的 id
    /// （每个快捷条按钮 / 每条属性 / 每个装备格）按枚举与 VNGameConfig 现场展开，
    /// 用的是运行时同一套格式化方法，两边拼出来的字符串必然一致。
    ///
    /// Play Mode 下再叠上 <see cref="VNTutorialAnchors.Ids"/> 的实时清单（打 ● 标记）。
    /// </summary>
    public static class VNTutorialAnchorCatalog
    {
        public class Entry
        {
            public string id;
            public string owner;     // 显示用分组名（中文）
            public bool dynamic;     // 由枚举 / 配置展开而来
        }

        static readonly Dictionary<string, string> OwnerNames = new Dictionary<string, string>
        {
            { nameof(VNUiAnchors), "主界面" },
            { nameof(VNBadmintonModule), "羽毛球对战" },
            { nameof(VNSecretPhotoUi), "秘密偷拍" },
        };

        static List<Entry> _cache;

        public static IReadOnlyList<Entry> Entries
        {
            get
            {
                if (_cache == null) Build();
                return _cache;
            }
        }

        /// <summary>加了新模块 / 改了配置后重扫</summary>
        public static void Invalidate() => _cache = null;

        /// <summary>目录里有没有这个 id（判断「填的锚点认不认识」）</summary>
        public static bool Contains(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var e in Entries)
                if (e.id == id) return true;
            return false;
        }

        /// <summary>Play Mode 下当前真的登记着的 id（Edit Mode 返回空）</summary>
        public static HashSet<string> LiveIds()
        {
            var set = new HashSet<string>();
            if (!Application.isPlaying) return set;
            foreach (var id in VNTutorialAnchors.Ids) set.Add(id);
            return set;
        }

        static void Build()
        {
            _cache = new List<Entry>();

            // ---- 静态：反射扫 public const string Anchor* ----
            Assembly asm = typeof(VNTutorialDef).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var type in types)
            {
                if (type == null) continue;
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Static |
                                                 BindingFlags.DeclaredOnly))
                {
                    if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                    if (!f.Name.StartsWith("Anchor", StringComparison.Ordinal)) continue;
                    string id = f.GetRawConstantValue() as string;
                    if (string.IsNullOrEmpty(id)) continue;
                    _cache.Add(new Entry { id = id, owner = OwnerLabel(type.Name) });
                }
            }

            // ---- 动态：按枚举 / 配置展开（格式化方法与运行时共用） ----
            foreach (VNToolbarAction a in Enum.GetValues(typeof(VNToolbarAction)))
                _cache.Add(new Entry { id = VNUiAnchors.Toolbar(a), owner = "主界面 · 快捷条按钮", dynamic = true });

            foreach (VNEquipSlot s in Enum.GetValues(typeof(VNEquipSlot)))
            {
                if (s == VNEquipSlot.None) continue;
                _cache.Add(new Entry { id = VNUiAnchors.InventorySlot(s), owner = "主界面 · 装备格", dynamic = true });
            }

            var config = VNGameConfig.Active;
            if (config != null && config.stats != null)
                foreach (var stat in config.stats)
                    if (stat != null && !string.IsNullOrEmpty(stat.id) && stat.showInHud)
                        _cache.Add(new Entry { id = VNUiAnchors.Stat(stat.id), owner = "主界面 · 属性 HUD", dynamic = true });

            _cache.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.owner, b.owner);
                return c != 0 ? c : string.CompareOrdinal(a.id, b.id);
            });
        }

        static string OwnerLabel(string typeName) =>
            OwnerNames.TryGetValue(typeName, out var label) ? label : typeName;
    }
}
