using System.Collections.Generic;

namespace VNEffects
{
    /// <summary>
    /// 一次拍照的装扮快照：谁摆了什么表情、用了哪个边框、贴了哪些贴纸。
    /// 只有玩家自己贴上去的贴纸计入 stickerIds——边框自带的装饰属于边框的一部分，
    /// 已经由边框加分项算过，再算一遍等于同一件事给两次分。
    /// </summary>
    public class VNPhotoDressing
    {
        public string meExpression;
        public string herExpression;
        public string frameId;
        public string backdropId;
        public List<string> stickerIds = new List<string>();
    }

    /// <summary>
    /// 拍照评分（纯静态数学，不碰 MonoBehaviour / UI，可直接写 EditMode 单测）。
    ///
    /// 规则是「清单制」：主题资产列出它想要的表情/边框/贴纸各值多少分，
    /// 命中就加，最后按完美线/及格线分三档。故意保持无状态、无随机——
    /// 同样的装扮永远得同样的分，玩家才能学会「什么样算好照片」。
    /// </summary>
    public static class VNPhotoScore
    {
        public const string OutcomePerfect = "完美";
        public const string OutcomeNormal = "普通";
        public const string OutcomeFail = "失败";
        public const string OutcomeFree = "完成";

        /// <summary>得分明细里的一条（结算时逐条弹出）</summary>
        public struct Hit
        {
            public string label;    // 「害羞的表情」
            public int score;       // +20
            public string comment;  // 命中细评（可空）
        }

        public class Result
        {
            public int total;
            public int grade;                       // 2 完美 / 1 普通 / 0 失败
            public List<Hit> hits = new List<Hit>();
            public string gradeComment = "";        // 分档总评
            public string bestComment = "";         // 命中项里分最高的那条细评（结算主打这句）

            public string Outcome =>
                grade >= 2 ? OutcomePerfect : grade == 1 ? OutcomeNormal : OutcomeFail;
        }

        /// <summary>
        /// 按主题给装扮打分。theme 为 null（自由拍照）时返回 total=0、grade=1 的空结果，
        /// 调用方自行判断是否展示分数。
        /// </summary>
        public static Result Evaluate(VNPhotoDressing dressing, VNPhotoThemeDef theme)
        {
            var result = new Result();
            if (theme == null) { result.grade = 1; return result; }
            if (dressing == null) dressing = new VNPhotoDressing();

            int total = theme.baseScore;
            if (theme.baseScore != 0)
                result.hits.Add(new Hit { label = "基础分", score = theme.baseScore });

            // ---- 表情 ----
            if (theme.expressionRules != null)
                foreach (var rule in theme.expressionRules)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.expression)) continue;

                    if (rule.slot != VNPhotoSlot.Her && IdEquals(dressing.meExpression, rule.expression))
                        total += AddHit(result, $"我的「{rule.expression}」", rule.score, rule.comment);

                    if (rule.slot != VNPhotoSlot.Me && IdEquals(dressing.herExpression, rule.expression))
                        total += AddHit(result, $"她的「{rule.expression}」", rule.score, rule.comment);
                }

            // ---- 边框 ----
            if (theme.frameRules != null && !string.IsNullOrEmpty(dressing.frameId))
                foreach (var rule in theme.frameRules)
                {
                    if (rule == null || !IdEquals(dressing.frameId, rule.frameId)) continue;
                    total += AddHit(result, $"边框「{rule.frameId}」", rule.score, rule.comment);
                    break;  // 一张照片只有一个边框，命中即止
                }

            // ---- 背景 ----
            if (theme.backdropRules != null && !string.IsNullOrEmpty(dressing.backdropId))
                foreach (var rule in theme.backdropRules)
                {
                    if (rule == null || !IdEquals(dressing.backdropId, rule.backdropId)) continue;
                    total += AddHit(result, $"背景「{rule.backdropId}」", rule.score, rule.comment);
                    break;  // 背景同样只有一个，命中即止
                }

            // ---- 贴纸 ----
            if (theme.stickerRules != null && dressing.stickerIds != null &&
                dressing.stickerIds.Count > 0)
            {
                var counts = new Dictionary<string, int>();
                foreach (var id in dressing.stickerIds)
                {
                    if (string.IsNullOrEmpty(id)) continue;
                    string key = Normalize(id);
                    counts.TryGetValue(key, out int n);
                    counts[key] = n + 1;
                }

                // 总额度：所有贴纸加起来最多按这么多个计分（0 = 不限）
                int budget = theme.stickerScoreCap > 0 ? theme.stickerScoreCap : int.MaxValue;

                foreach (var rule in theme.stickerRules)
                {
                    if (rule == null || string.IsNullOrEmpty(rule.stickerId)) continue;
                    if (budget <= 0) break;
                    if (!counts.TryGetValue(Normalize(rule.stickerId), out int have) || have <= 0)
                        continue;

                    int counted = have;
                    if (counted > rule.maxCount) counted = rule.maxCount;
                    if (counted > budget) counted = budget;
                    budget -= counted;

                    string label = counted > 1
                        ? $"贴纸「{rule.stickerId}」×{counted}"
                        : $"贴纸「{rule.stickerId}」";
                    total += AddHit(result, label, rule.score * counted, rule.comment);
                }
            }

            result.total = total;
            result.grade = total >= theme.perfectLine ? 2 : total >= theme.passLine ? 1 : 0;

            var gradeLine = theme.CommentForGrade(result.grade);
            result.gradeComment = gradeLine != null && !gradeLine.Empty ? gradeLine.Display : "";

            // 细评只挑一条：命中项里加分最多的那个，避免结算刷屏
            int best = 0;
            foreach (var hit in result.hits)
                if (!string.IsNullOrEmpty(hit.comment) && hit.score > best)
                {
                    best = hit.score;
                    result.bestComment = hit.comment;
                }

            return result;
        }

        static int AddHit(Result result, string label, int score, VNPhotoLine comment)
        {
            result.hits.Add(new Hit
            {
                label = label,
                score = score,
                comment = comment != null && !comment.Empty ? comment.Display : "",
            });
            return score;
        }

        /// <summary>id 比较：忽略首尾空白与英文大小写（中文 id 不受影响）</summary>
        static bool IdEquals(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return Normalize(a) == Normalize(b);
        }

        static string Normalize(string s) => s.Trim().ToLowerInvariant();
    }
}
