using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 任务定义资产：只管显示文案，进行状态全部存 VNFlags（flag 名 = 任务_&lt;id&gt;，
    /// 随存档免费保存，剧本可直接 if 任务_xx>=2 判断）。
    /// 阶段号约定：0=未接取，1..n=进行中（stages[阶段-1] 为当前目标文案），
    /// 100=完成，-1=失败（见 VNQuestLog 常量）。
    /// 没有定义资产的任务也能正常运作，只是日志/Toast 用 id 当标题、无阶段文案。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Quest Definition", fileName = "NewQuest")]
    public class VNQuestDef : ScriptableObject
    {
        [Header("剧本 quest 命令引用的 id（可中文，如 告白大作战）")]
        public string id;

        [Header("任务日志显示的标题；留空 = 直接用 id")]
        public string title;

        [TextArea]
        [Header("任务总描述（日志里显示在标题下，可留空）")]
        public string description;

        [Header("各阶段目标文案：第 1 项对应阶段 1（quest start 后的初始阶段）")]
        public List<string> stages = new List<string>();

        [Header("—— English 文案（本地化；留空的项回退中文）——")]
        public string titleEn;
        [TextArea]
        public string descriptionEn;
        [Header("英文阶段文案：与中文 stages 一一对应，缺项回退中文")]
        public List<string> stagesEn = new List<string>();

        [Header("—— 日本語 文案（本地化；留空的项回退中文）——")]
        public string titleJa;
        [TextArea]
        public string descriptionJa;
        [Header("日文阶段文案：与中文 stages 一一对应，缺项回退中文")]
        public List<string> stagesJa = new List<string>();

        /// <summary>当前语言的任务标题（译名留空回退中文 title，再回退 id）</summary>
        public string Title
        {
            get
            {
                string localized = Pick(titleEn, titleJa);
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(title) ? id : title;
            }
        }

        /// <summary>当前语言的任务总描述（留空回退中文）</summary>
        public string LocalizedDescription
        {
            get
            {
                string localized = Pick(descriptionEn, descriptionJa);
                return string.IsNullOrEmpty(localized) ? description : localized;
            }
        }

        /// <summary>当前语言的阶段目标文案（阶段号从 1 起；缺译/越界回退中文，再回退空串）</summary>
        public string StageText(int stage)
        {
            if (stage < 1) return "";
            var localizedList = VNLocale.Language == VNLanguage.English ? stagesEn
                : VNLocale.Language == VNLanguage.Japanese ? stagesJa : null;
            if (localizedList != null && stage <= localizedList.Count &&
                !string.IsNullOrEmpty(localizedList[stage - 1]))
                return localizedList[stage - 1];
            return stage <= stages.Count ? stages[stage - 1] : "";
        }

        static string Pick(string en, string ja)
        {
            switch (VNLocale.Language)
            {
                case VNLanguage.English: return en;
                case VNLanguage.Japanese: return ja;
                default: return null;
            }
        }
    }
}
