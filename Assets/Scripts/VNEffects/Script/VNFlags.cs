using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 剧本全局变量（flag）：整型字典，bool 用 0/1 表示。
    ///   flag 勇气          → 设为 1
    ///   flag 好感度 3      → 设为 3
    ///   flag 好感度 +1     → 增减
    /// 条件求值（if 命令 / 将来选项条件）：
    ///   好感度 / !勇气 / 好感度>=2 / 结局==1 …（条件内不能有空格）
    /// P2 存档时整个字典随进度序列化。
    /// </summary>
    public static class VNFlags
    {
        static readonly Dictionary<string, int> _values = new Dictionary<string, int>();

        public static IReadOnlyDictionary<string, int> All => _values;

        public static int Get(string key) =>
            _values.TryGetValue(key, out var v) ? v : 0;

        public static void Set(string key, int value) => _values[key] = value;

        public static void Add(string key, int delta) => Set(key, Get(key) + delta);

        public static void Clear() => _values.Clear();

        /// <summary>应用一个 flag 操作串："名字"=置1、"名字+2"/"名字-1"=增减</summary>
        public static void Apply(string op)
        {
            if (string.IsNullOrEmpty(op)) return;
            op = op.Trim();

            // 从第 2 个字符起找 +/-（避免把负号开头误判）
            for (int i = 1; i < op.Length; i++)
            {
                if (op[i] == '+' || op[i] == '-')
                {
                    string name = op.Substring(0, i).Trim();
                    if (int.TryParse(op.Substring(i), out int delta))
                    {
                        Add(name, delta);
                        return;
                    }
                    break;
                }
            }
            Set(op, 1);
        }

        /// <summary>求值条件串（不含空格）：flag名 / !flag名 / flag名&lt;op&gt;数值</summary>
        public static bool Evaluate(string cond, int line = 0)
        {
            if (string.IsNullOrEmpty(cond)) return false;
            cond = cond.Trim();

            if (cond.StartsWith("!"))
                return Get(cond.Substring(1)) == 0;

            // 按长度优先匹配比较符
            string[] ops = { ">=", "<=", "==", "!=", ">", "<" };
            foreach (var op in ops)
            {
                int idx = cond.IndexOf(op);
                if (idx <= 0) continue;

                string name = cond.Substring(0, idx).Trim();
                string rhs = cond.Substring(idx + op.Length).Trim();
                if (!int.TryParse(rhs, out int value))
                {
                    Debug.LogWarning($"[VNScript] 第 {line} 行：条件「{cond}」右侧不是数字");
                    return false;
                }
                int lhs = Get(name);
                switch (op)
                {
                    case ">=": return lhs >= value;
                    case "<=": return lhs <= value;
                    case "==": return lhs == value;
                    case "!=": return lhs != value;
                    case ">": return lhs > value;
                    case "<": return lhs < value;
                }
            }

            return Get(cond) != 0; // 裸 flag 名：非 0 即真
        }
    }
}
