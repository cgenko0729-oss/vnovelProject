using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 日程排程定义资产：候选行动清单与文案（火山的女儿式"排一周日程"）。
    /// 玩家排好的日程全部存 VNFlags（flag 名 = 日程_&lt;格序号&gt;，值 = 行动编号），
    /// 随存档走、if 分支可直接判断。剧本用法（模板 Inspector 里可登记多套方案）：
    ///   event plan id:&lt;方案id&gt; [slots:7] [pool:打工,学习] [title:安排这一周]
    /// 没有资产也能运作：pool: 列出的行动名按出现顺序编号 1..n（无图标/收益文案）。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Plan Definition", fileName = "NewPlan")]
    public class VNPlanDef : ScriptableObject
    {
        [System.Serializable]
        public class ActionDef
        {
            [Header("行动 id（剧本 pool: 参数引用，可中文，永远不翻译）")]
            public string id;

            [Header("行动编号（写入 flag「日程_<格>」的值，剧本按它派发；≥1 且不重复）")]
            public int number = 1;

            [Header("显示名；留空 = 直接用 id")]
            public string displayName;
            [Header("英文/日文显示名（留空回退中文）")]
            public string displayNameEn;
            public string displayNameJa;

            [Header("预期收益/消耗文案（面板上显示，如「金钱+150 压力+10」，可留空）")]
            public string gainText;
            public string gainTextEn;
            public string gainTextJa;

            [Header("图标（可留空，用色块代替）")]
            public Sprite icon;

            [Header("上架条件（VNFlags 表达式，如 智力>=50；留空 = 总是可选）")]
            public string condition;

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

            public string LocalizedGainText
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? gainTextEn
                        : VNLocale.Language == VNLanguage.Japanese ? gainTextJa : null;
                    return string.IsNullOrEmpty(localized) ? gainText : localized;
                }
            }
        }

        [Header("剧本 event plan id:<方案id> 引用的 id（可中文，如 周日程）")]
        public string planId;

        [Header("面板标题；留空 = 用 UI 字符串表默认值（剧本 title: 参数优先）")]
        public string title;
        [Header("英文/日文标题（留空回退中文）")]
        public string titleEn;
        public string titleJa;

        [Header("候选行动清单")]
        public List<ActionDef> actions = new List<ActionDef>();

        public string Title
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? titleEn
                    : VNLocale.Language == VNLanguage.Japanese ? titleJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return title;
            }
        }

        public ActionDef FindByNumber(int number)
        {
            foreach (var action in actions)
                if (action != null && action.number == number) return action;
            return null;
        }
    }
}
