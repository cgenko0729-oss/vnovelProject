using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 一条统计声明：把某个「本次成绩」flag 派生成生涯数据。
    /// 小游戏写的是本次成绩（羽球_我方得分 = 21），下一场直接覆盖，
    /// 所以「单场 5000 分」「累计打 10 场」这类任务条件没有数据可读——本层就补这个。
    /// </summary>
    [System.Serializable]
    public class VNTrackerEntry
    {
        [Header("源 flag 名（模块写结果的那个，如 羽球_我方得分）")]
        public string sourceFlag;

        [Header("历史最高 → <源>@最高")]
        public bool trackMax = true;

        [Header("历史最低 → <源>@最低")]
        public bool trackMin;

        [Header("每次写入值的总和 → <源>@累计\n" +
                "注意累的是「写入的值本身」，适合成绩类；\n" +
                "金钱这种余额型 flag 累计没有意义（要总收入请另开一个只写增量的 flag）")]
        public bool trackSum;

        [Header("被写入过多少次 → <源>@次数\n" +
                "要求模块只在结束时写一次结果 flag（羽球 WriteResultFlags 就是这样）")]
        public bool trackCount;
    }

    /// <summary>
    /// 统计层：按 VNGameConfig 里的声明，把源 flag 派生成 @最高 / @最低 / @累计 / @次数。
    ///
    /// 三个设计要点：
    ///   ① 派生值本身也是普通 flag，所以随存档走、能直接写进任务条件表达式
    ///      （已验证 '@' 不在 VNExpression 的标识符分隔符黑名单里，求值器零改动）。
    ///   ② 靠 VNFlags.KeyChanged 精确知道「哪个 flag 被写了」，而不是 diff 字典——
    ///      diff 会把「两场都打 21 分」算成一场（值没变），@次数 与 @累计 就错了。
    ///   ③ 读档必须挂起：读档是把整张字典逐个 Set 回去，每一次都会触发事件，
    ///      不挂起的话读一次档 @累计 就被整份历史再加一遍，而派生值本来就存在档里。
    /// </summary>
    public static class VNTracker
    {
        public const string SuffixMax = "@最高";
        public const string SuffixMin = "@最低";
        public const string SuffixSum = "@累计";
        public const string SuffixCount = "@次数";

        /// <summary>引擎保留字：任务 / 属性 / 道具 id 都不得含它（Lint 卡死）</summary>
        public const char ReservedChar = '@';

        static readonly Dictionary<string, VNTrackerEntry> _map =
            new Dictionary<string, VNTrackerEntry>();

        static bool _hooked;
        static bool _writing;   // 写派生值会再次触发 KeyChanged，防重入
        static bool _suspended;

        /// <summary>
        /// 挂起统计（读档 / 调试重建 / VNFlags.Clear 期间）。
        /// 挂起时源 flag 的写入一律不派生，恢复后也不会补算——
        /// 这正是要的语义：读回来的那些值不是「新成绩」。
        /// </summary>
        public static bool Suspended
        {
            get => _suspended;
            set => _suspended = value;
        }

        /// <summary>已声明的统计源（编辑器 Lint 用：条件里写了 @最高 却没声明就报错）</summary>
        public static IEnumerable<string> Sources => _map.Keys;

        /// <summary>某个源 flag 的声明；没声明返回 null</summary>
        public static VNTrackerEntry Find(string sourceFlag) =>
            sourceFlag != null && _map.TryGetValue(sourceFlag, out var e) ? e : null;

        /// <summary>由 VNQuestLog 在 Awake 时用 VNGameConfig.trackers 调用</summary>
        public static void Configure(List<VNTrackerEntry> entries)
        {
            _map.Clear();
            if (entries != null)
            {
                foreach (var e in entries)
                {
                    if (e == null || string.IsNullOrEmpty(e.sourceFlag)) continue;
                    if (e.sourceFlag.IndexOf(ReservedChar) >= 0)
                    {
                        Debug.LogWarning($"[VNTracker] 统计源「{e.sourceFlag}」含保留字符 " +
                                         $"'{ReservedChar}'，已忽略");
                        continue;
                    }
                    _map[e.sourceFlag] = e;
                }
            }

            if (!_hooked)
            {
                _hooked = true;
                VNFlags.KeyChanged += OnKeyChanged;
            }
        }

        /// <summary>派生 flag 名（编辑器 Lint 与条件补全用）</summary>
        public static string DerivedName(string sourceFlag, string suffix) => sourceFlag + suffix;

        /// <summary>某个名字是不是「已声明的源 + 合法后缀」组合</summary>
        public static bool IsDerivedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int at = name.LastIndexOf(ReservedChar);
            if (at <= 0) return false;
            string src = name.Substring(0, at);
            string suffix = name.Substring(at);
            if (!_map.TryGetValue(src, out var e)) return false;
            switch (suffix)
            {
                case SuffixMax: return e.trackMax;
                case SuffixMin: return e.trackMin;
                case SuffixSum: return e.trackSum;
                case SuffixCount: return e.trackCount;
                default: return false;
            }
        }

        // ------------------------------------------------------------------

        static void OnKeyChanged(string key)
        {
            if (_suspended || _writing) return;
            if (!_map.TryGetValue(key, out var entry)) return;

            int value = VNFlags.Get(key);
            _writing = true;
            try
            {
                if (entry.trackMax) Extreme(key + SuffixMax, value, true);
                if (entry.trackMin) Extreme(key + SuffixMin, value, false);
                if (entry.trackSum) VNFlags.Set(key + SuffixSum, VNFlags.Get(key + SuffixSum) + value);
                if (entry.trackCount) VNFlags.Set(key + SuffixCount, VNFlags.Get(key + SuffixCount) + 1);
            }
            finally
            {
                _writing = false;
            }
        }

        /// <summary>极值：flag 还不存在时直接写入本次值（否则最低值永远是 0）</summary>
        static void Extreme(string flag, int value, bool takeMax)
        {
            if (!VNFlags.All.ContainsKey(flag))
            {
                VNFlags.Set(flag, value);
                return;
            }
            int cur = VNFlags.Get(flag);
            if (takeMax ? value > cur : value < cur) VNFlags.Set(flag, value);
        }
    }
}
