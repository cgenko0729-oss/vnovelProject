using System;
using System.Collections.Generic;

namespace VNEffects
{
    /// <summary>
    /// 「隐藏界面」能分别关掉的 UI 部件（剧本 hideHUD 命令的目标）。
    ///
    /// Dialogue 刻意把对话框本体与右下快捷功能条绑成一项——玩家心里它们是同一块
    /// 「对话 UI」，分开关会出现「台词没了但存档按钮还浮在半空」这种半吊子画面。
    /// </summary>
    [Flags]
    public enum VNUiParts
    {
        None = 0,
        Dialogue = 1 << 0,   // 对话框 + 右下快捷功能条
        Stats = 1 << 1,      // 顶部属性 HUD（金钱/行动力/…）
        Calendar = 1 << 2,   // 右下日历 HUD
        All = Dialogue | Stats | Calendar,
    }

    /// <summary>
    /// 部件名的解析与序列化。剧本 token 与存档字符串共用同一张表，
    /// 所以「剧本里能写的名字」和「存档里存的名字」永远不会分家。
    /// </summary>
    public static class VNUiPartsUtil
    {
        // 中文别名一并认：剧本作者写中文的概率不低，且这里不涉及翻译表
        static readonly Dictionary<string, VNUiParts> Names =
            new Dictionary<string, VNUiParts>(StringComparer.OrdinalIgnoreCase)
            {
                { "dialogue", VNUiParts.Dialogue }, { "对话框", VNUiParts.Dialogue },
                { "对话", VNUiParts.Dialogue },     { "台词", VNUiParts.Dialogue },
                { "stats", VNUiParts.Stats },       { "属性", VNUiParts.Stats },
                { "属性栏", VNUiParts.Stats },      { "hud", VNUiParts.Stats },
                { "calendar", VNUiParts.Calendar }, { "日历", VNUiParts.Calendar },
                { "all", VNUiParts.All },           { "全部", VNUiParts.All },
            };

        /// <summary>剧本 token → 部件；认不出返回 None（调用方负责告警）</summary>
        public static VNUiParts Parse(string token)
        {
            if (string.IsNullOrEmpty(token)) return VNUiParts.None;
            return Names.TryGetValue(token.Trim(), out var parts) ? parts : VNUiParts.None;
        }

        /// <summary>部件 → 存档字符串（"dialogue,stats"）。存名字不存位，方便肉眼查存档</summary>
        public static string ToToken(VNUiParts parts)
        {
            if (parts == VNUiParts.None) return "";
            var list = new List<string>();
            if ((parts & VNUiParts.Dialogue) != 0) list.Add("dialogue");
            if ((parts & VNUiParts.Stats) != 0) list.Add("stats");
            if ((parts & VNUiParts.Calendar) != 0) list.Add("calendar");
            return string.Join(",", list);
        }

        /// <summary>存档字符串 → 部件（空串/旧存档缺省 = None）</summary>
        public static VNUiParts FromToken(string text)
        {
            if (string.IsNullOrEmpty(text)) return VNUiParts.None;
            var result = VNUiParts.None;
            foreach (var piece in text.Split(','))
                result |= Parse(piece);
            return result;
        }
    }
}
