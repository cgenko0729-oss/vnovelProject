using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>属性值的展示样式（HUD/属性面板用；flag 里永远只存整数）</summary>
    public enum VNStatStyle
    {
        Number,   // 纯数值 + 可选单位后缀，如 500G
        Percent,  // 百分比，如 8%
        OutOfMax, // 分数形式，如 9/10（分母 = maxValue）
        Grade,    // 数值 + 等级评价（E~S，阈值表见 gradeSteps）
    }

    /// <summary>
    /// 养成属性定义资产：只管钳制范围与显示规则，数值本体全部存 VNFlags
    /// （flag 名 = id），因此存档、if 分支、choice 加减、调试重建零改动复用。
    /// 没有定义资产的属性也能用 stat 命令照常读写，只是不钳制、无 HUD 条目。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Stat Definition", fileName = "NewStat")]
    public class VNStatDef : ScriptableObject
    {
        [System.Serializable]
        public class GradeStep
        {
            [Tooltip("达到该数值（含）即评为此等级")]
            public int threshold;
            public string label = "E";
        }

        [Header("剧本 stat 命令引用的 id（= flag 名，可中文，如 金钱/压力/智力）")]
        public string id;

        [Header("HUD/面板显示名；留空 = 直接用 id")]
        public string displayName;

        [Header("—— 本地化显示名（留空回退中文）——")]
        public string displayNameEn;
        public string displayNameJa;

        [Header("图标（可留空，HUD 用色块代替）")]
        public Sprite icon;

        [Header("HUD/面板中的主题色（数值条与图标底色）")]
        public Color color = new Color(1f, 0.85f, 0.4f);

        [Header("钳制范围（useClamp 开启后 stat 命令写入时截断到 [min,max]）")]
        public bool useClamp = true;
        public int minValue = 0;
        public int maxValue = 100;

        [Header("初始值：进入游戏 / 读档后该 flag 尚不存在时自动写入")]
        public int initialValue = 0;

        [Header("展示样式")]
        public VNStatStyle style = VNStatStyle.Number;

        [Header("数值后缀（style=Number 时拼在数字后，如 G）")]
        public string unit = "";

        [Header("等级阈值表（style=Grade 用；按 threshold 从小到大填）")]
        public List<GradeStep> gradeSteps = new List<GradeStep>();

        [Header("显示在顶栏 HUD（不勾则只出现在 C 键属性面板）")]
        public bool showInHud = true;

        /// <summary>当前语言的显示名（译名留空回退中文 displayName，再回退 id）</summary>
        public string DisplayName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? displayNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? displayNameJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(displayName) ? id : displayName;
            }
        }

        /// <summary>把原始值截断到定义范围（未启用钳制则原样返回）</summary>
        public int Clamp(int value) =>
            useClamp ? Mathf.Clamp(value, minValue, maxValue) : value;

        /// <summary>数值 → 等级文本（无阈值表返回空串）</summary>
        public string GradeOf(int value)
        {
            string grade = "";
            foreach (var step in gradeSteps)
            {
                if (step == null) continue;
                if (value >= step.threshold) grade = step.label;
            }
            return grade;
        }

        /// <summary>按展示样式格式化数值（Grade 样式只给数字，等级另取 GradeOf）</summary>
        public string Format(int value)
        {
            switch (style)
            {
                case VNStatStyle.Percent: return $"{value}%";
                case VNStatStyle.OutOfMax: return $"{value}/{maxValue}";
                default: return string.IsNullOrEmpty(unit) ? value.ToString() : $"{value}{unit}";
            }
        }

        /// <summary>0..1 的条形进度；Number 样式（金钱类）或未钳制时返回 -1 = 不画条</summary>
        public float Normalized(int value)
        {
            if (style == VNStatStyle.Number) return -1f;
            if (!useClamp || maxValue <= minValue) return -1f;
            return Mathf.InverseLerp(minValue, maxValue, value);
        }
    }
}
