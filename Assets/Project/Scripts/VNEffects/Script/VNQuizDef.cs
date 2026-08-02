using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 限时问答题库资产：一套题目 + 每题的选项/答案/奖励。
    /// 成绩不存在资产里——答对数写 VNFlags（flag 名 = 前缀+"正确数"/"总数"），
    /// 随存档走、if 分支可直接判断（如 if 答题正确数>=3 jump 满分）。
    /// 剧本用法：event quiz id:&lt;题库id&gt; count:3 time:15 pass:2
    ///
    /// 属性奖励按「每题单独配」：难题可以给得多，答错也可以扣。
    /// 奖励条目复用 VNShopDef.StatOp（statId + amount），走 VNStatsHud 钳制 + 飘字。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Quiz Definition", fileName = "NewQuiz")]
    public class VNQuizDef : ScriptableObject
    {
        /// <summary>一个选项：文本 + 三语</summary>
        [System.Serializable]
        public class Option
        {
            [Header("选项文本")]
            [TextArea(1, 3)] public string text;
            [Header("英文/日文（留空回退中文）")]
            public string textEn;
            public string textJa;

            public string Display
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? textEn
                        : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                    return string.IsNullOrEmpty(localized) ? text : localized;
                }
            }
        }

        /// <summary>一道题：题干 + 2~4 个选项 + 正确答案下标 + 本题奖励</summary>
        [System.Serializable]
        public class Question
        {
            [Header("题干")]
            [TextArea(2, 4)] public string text;
            [Header("英文/日文题干（留空回退中文）")]
            [TextArea(2, 4)] public string textEn;
            [TextArea(2, 4)] public string textJa;

            [Header("选项（2~4 个；超过 4 个只取前 4 个）")]
            public List<Option> options = new List<Option>();

            [Header("正确答案的选项序号（0 = 第一个选项）")]
            public int answerIndex = 0;

            [Header("答对后的解析文字（可留空 = 不显示解析）")]
            [TextArea(1, 3)] public string explain;
            [TextArea(1, 3)] public string explainEn;
            [TextArea(1, 3)] public string explainJa;

            [Header("本题限时（秒）；0 = 用题库默认限时")]
            public float timeLimit = 0f;

            [Header("答对奖励（属性 id + 增量，走 VNStatDef 钳制 + 飘字；可留空）")]
            public List<VNShopDef.StatOp> rewardOnCorrect = new List<VNShopDef.StatOp>();

            [Header("答错（含超时）惩罚（同上，amount 一般填负数；可留空）")]
            public List<VNShopDef.StatOp> penaltyOnWrong = new List<VNShopDef.StatOp>();

            public string Display
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? textEn
                        : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                    return string.IsNullOrEmpty(localized) ? text : localized;
                }
            }

            public string DisplayExplain
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? explainEn
                        : VNLocale.Language == VNLanguage.Japanese ? explainJa : null;
                    return string.IsNullOrEmpty(localized) ? explain : localized;
                }
            }

            /// <summary>有题干、至少 2 个选项、答案下标在范围内才算可用</summary>
            public bool IsValid =>
                !string.IsNullOrEmpty(text) && options != null && options.Count >= 2 &&
                answerIndex >= 0 && answerIndex < options.Count;
        }

        [Header("剧本 event quiz id:<题库id> 引用的 id（可中文，如 社团常识）")]
        public string quizId;

        [Header("面板标题；留空 = 直接用 quizId（剧本 title: 可覆盖）")]
        public string title;
        [Header("英文/日文标题（留空回退中文）")]
        public string titleEn;
        public string titleJa;

        [Header("每题默认限时（秒）；剧本 time: 可覆盖，单题 timeLimit 优先级最高")]
        public float defaultTimeLimit = 15f;

        [Header("成绩写入 flag 的前缀：<前缀>正确数 / <前缀>总数（剧本 flag: 可覆盖）")]
        public string flagPrefix = "答题";

        [Header("题目清单")]
        public List<Question> questions = new List<Question>();

        /// <summary>一道题最多显示几个选项（UI 布局上限）</summary>
        public const int MaxOptions = 4;

        public string DisplayTitle
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? titleEn
                    : VNLocale.Language == VNLanguage.Japanese ? titleJa : null;
                if (!string.IsNullOrEmpty(localized)) return localized;
                return string.IsNullOrEmpty(title) ? quizId : title;
            }
        }

        /// <summary>题干/选项填全了的题（出题只从这里取，坏题不会让事件卡住）</summary>
        public List<Question> ValidQuestions()
        {
            var list = new List<Question>();
            if (questions == null) return list;
            foreach (var q in questions)
                if (q != null && q.IsValid) list.Add(q);
            return list;
        }
    }
}
