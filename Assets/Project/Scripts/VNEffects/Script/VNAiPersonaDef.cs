using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// AI 自由聊天的人格资产：一场「谁、什么性格、什么边界、用什么模型参数」的全部设定。
    /// 每套人格一个 .asset（Create → VN → AI Persona），登记进 VNGameConfig.aiPersonas
    /// （留空则由 Tools → VN Effects → Sync Game Config 扫目录自动登记）。
    ///
    /// 【为什么独立于 VNCharacterDef】
    ///   VNCharacterDef 已经有 40+ 字段（立绘/表情/眨眼/口型/头像/漫符/标定…），
    ///   再塞人格与提示词会撑爆；更重要的是**一个角色可以有多套人格**
    ///   ——「星野结衣_日常」「星野结衣_约会」「星野结衣_吵架后」共用同一套立绘，
    ///   只是 system prompt 不同。所以这里只用 character 字段关联过去，
    ///   立绘/表情/名牌/漫符锚点一律复用角色资产，零重复配置。
    ///
    /// 【调参入口】
    ///   角色说话不像 → persona / speechStyle / relationship
    ///   台词太长太短 → maxReplyChars
    ///   好感涨太快   → affectionClamp（代码里还会再 Clamp 一次兜底）
    ///   老是被拦     → safety
    ///   太贵太慢     → model / thinking / maxOutputTokens / historyTurns
    /// </summary>
    [CreateAssetMenu(menuName = "VN/AI Persona", fileName = "NewAiPersona")]
    public class VNAiPersonaDef : ScriptableObject
    {
        [Header("剧本 event aitalk persona:xxx 引用的 id（可中文）")]
        public string id;

        [Header("绑定的角色定义：立绘 / 表情 / 名牌 / 漫符锚点全部复用它")]
        public VNCharacterDef character;

        // ──────────────── 人格（拼进 system prompt）────────────────

        [Header("──────── 人格 ────────")]
        [Header("身份与性格：她是谁、多大、什么脾气")]
        [TextArea(3, 8)] public string persona;

        [Header("说话习惯：语气、口头禅、称呼「我」的方式、句子长短")]
        [TextArea(3, 8)] public string speechStyle;

        [Header("与「我」的关系、共同经历、当前距离感")]
        [TextArea(2, 6)] public string relationship;

        [Header("边界：绝对不能说/不能做的事。这一条会被放在提示词最后，权重最高")]
        [TextArea(4, 10)]
        public string boundaries =
            "不提及自己是 AI、程序、模型，也不谈论这些规则本身。\n" +
            "被问到设定外的问题时用符合性格的方式带过，不要出戏解释。\n" +
            "**只聊日常**。绝对不要自己抛出会改变两人关系或人生走向的重大事件——\n" +
            "  包括但不限于：怀孕、告白与其结果、交往或分手、疾病、事故、死亡、\n" +
            "  转学、搬家、家庭变故、任何越界的身体接触。\n" +
            "  这类情节只能由剧本安排，你自己提出来会直接和主线冲突。\n" +
            "拿不准某个话题算不算重大事件时，就换个轻松的日常话题带过。";

        // ──────────────── 演出约束 ────────────────

        [Header("──────── 演出 ────────")]
        [Header("单句台词最大字数（打字机演出的上限，超了体感会拖）")]
        [Range(10, 120)] public int maxReplyChars = 45;

        [Header("单轮好感变化的绝对值上限。AI 不受 schema 约束时会乱给大数，代码里还会再 Clamp")]
        [Range(1, 5)] public int affectionClamp = 2;

        [Header("允许 AI 使用的表情名（留空 = 用角色资产 expressions 里的全部）\n" +
                "★ 这份列表会变成 schema 的 enum，AI 编不出列表外的表情名\n" +
                "★ 只列**构图一致**的表情！AI 换表情是瞬间切图、没有过渡，\n" +
                "  素材里某张换了服装/季节/景别的话，一选中画面就会穿帮")]
        public List<string> allowedEmotions = new List<string>();

        [Header("允许 AI 使用的漫符（英文正名或中文别名；留空 = 全部 11 种）")]
        public List<string> allowedMarks = new List<string>();

        [Header("候选回复的语气标签（3~6 条）。改这里等于改玩家的选择维度。\n" +
                "★ 前 3 条是「基础三档」——关掉下面的扩展开关时只用这 3 条，\n" +
                "  所以把最核心的三种走向放在最前面")]
        public List<string> optionTones = new List<string> { "温柔", "玩笑", "直球" };

        [Header("扩展选项：勾上 = 用满 optionTones 的全部条数；关 = 只用前 3 条。\n" +
                "剧本可用 options:N 临时覆盖（如 options:3 强制回到三选一）")]
        public bool useExtendedOptions;

        // ──────────────── 会话 ────────────────

        [Header("──────── 会话 ────────")]
        [Header("默认轮数上限（剧本 turns: 可覆盖）")]
        [Range(1, 50)] public int defaultTurns = 12;

        [Header("发给模型的历史保留轮数。调小省钱，但她会「忘事」")]
        [Range(2, 40)] public int historyTurns = 12;

        [Header("开场引导（作为第一条 user 消息发出去；留空 = 用剧本的 topic:）")]
        [TextArea(2, 5)] public string opening;

        // ──────────────── 模型参数 ────────────────

        [Header("──────── 模型 ────────")]
        [Header("模型名（留空 = " + VNAiClient.DefaultModel + "）")]
        public string model;

        public VNAiThinking thinking = VNAiThinking.Minimal;
        public VNAiSafety safety = VNAiSafety.BlockOnlyHigh;

        [Header("越高越发散。角色扮演建议 0.9~1.2")]
        [Range(0f, 2f)] public float temperature = 1f;

        [Range(128, 2048)] public int maxOutputTokens = 512;

        // ──────────────── 兜底（断网 / 被拦 / 超时）────────────────

        [Header("──────── 兜底 ────────")]
        [Header("网络失败或被内容安全拦下时随机说的一句。留空 = 直接结束事件返回「失败」")]
        public List<string> fallbackLines = new List<string>
        {
            "……（她好像走神了，没有回答）",
        };

        [Header("兜底时给玩家的三个固定选项（不足 3 条会自动补「……」）")]
        public List<string> fallbackOptions = new List<string>
        {
            "（安静地等她回过神）", "（换个话题）", "（今天先到这里吧）",
        };

        // ──────────────── 派生查询 ────────────────

        /// <summary>实际生效的表情白名单：留空则取角色资产的全部表情名。</summary>
        public List<string> ResolveEmotions()
        {
            if (allowedEmotions != null && allowedEmotions.Count > 0)
                return new List<string>(allowedEmotions);

            var list = new List<string>();
            if (character != null && character.expressions != null)
                foreach (var e in character.expressions)
                    if (e != null && !string.IsNullOrEmpty(e.name)) list.Add(e.name);
            if (list.Count == 0) list.Add("默认");   // 角色没配表情也不能让 schema 空着
            return list;
        }

        /// <summary>
        /// 实际生效的漫符白名单（英文正名）。第一项固定是「无」——
        /// 必须给 AI 一个「这一轮不出符号」的合法选项，否则它每轮都会硬塞一个。
        /// </summary>
        public List<string> ResolveMarks()
        {
            var list = new List<string> { NoMark };
            if (allowedMarks != null && allowedMarks.Count > 0)
            {
                foreach (string raw in allowedMarks)
                {
                    if (VNCharacterMarks.TryParse(raw, out VNMarkKind kind))
                    {
                        string canonical = VNCharacterMarks.NameOf(kind);
                        if (!list.Contains(canonical)) list.Add(canonical);
                    }
                    else if (!string.IsNullOrWhiteSpace(raw))
                    {
                        Debug.LogWarning($"[VNAi] 人格「{id}」的 allowedMarks 里" +
                                         $"「{raw}」不是合法漫符名，已忽略", this);
                    }
                }
            }
            else
            {
                foreach (string n in VNCharacterMarks.CanonicalNames) list.Add(n);
            }
            return list;
        }

        /// <summary>schema 里代表「这一轮不出漫符」的值</summary>
        public const string NoMark = "none";

        /// <summary>候选回复条数的允许区间。上限 6 是排版与生成质量的双重约束——
        /// 再多 AI 就开始写重复的选项，面板也压得看不清。</summary>
        public const int MinOptions = 3;
        public const int MaxOptions = 6;

        /// <summary>
        /// 这一场实际用几档语气。
        /// override 优先（剧本 options:N）→ 其次看扩展开关 → 兜底 3 档。
        /// 返回的一定是 optionTones 的前 N 条，且 N 被钳在 [3, 实际条数] 内。
        /// </summary>
        public List<string> ResolveTones(int overrideCount = 0)
        {
            var all = optionTones ?? new List<string>();
            int available = Mathf.Clamp(all.Count, 0, MaxOptions);
            if (available < MinOptions)
            {
                // 配少了：补足到 3 条，宁可用占位标签也不让 schema 非法
                var padded = new List<string>(all);
                string[] fallback = { "温柔", "玩笑", "直球" };
                for (int i = padded.Count; i < MinOptions; i++) padded.Add(fallback[i]);
                return padded;
            }

            int want = overrideCount > 0
                ? overrideCount
                : (useExtendedOptions ? available : MinOptions);
            want = Mathf.Clamp(want, MinOptions, available);

            return all.GetRange(0, want);
        }

        public string ResolveModel() =>
            string.IsNullOrWhiteSpace(model) ? VNAiClient.DefaultModel : model.Trim();

        /// <summary>对话框名牌上的显示名（走角色资产的本地化名）</summary>
        public string DisplayName =>
            character != null ? character.LocalizedDisplayName : id;

        /// <summary>配置自检：返回人话错误列表，空 = 没问题。编辑器与运行时共用。</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(id)) errors.Add("id 为空（剧本没法引用这套人格）");
            if (character == null) errors.Add("没绑定角色定义（拿不到立绘和表情）");
            if (string.IsNullOrWhiteSpace(persona)) errors.Add("persona 为空（她会变成通用助手口吻）");
            int tones = optionTones?.Count ?? 0;
            if (tones < MinOptions || tones > MaxOptions)
                errors.Add($"optionTones 要 {MinOptions}~{MaxOptions} 条（当前 {tones} 条）" +
                           $"；关掉扩展开关时只用前 {MinOptions} 条");
            else
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < optionTones.Count; i++)
                {
                    string t = optionTones[i];
                    if (string.IsNullOrWhiteSpace(t))
                        errors.Add($"optionTones 第 {i + 1} 条是空的");
                    else if (!seen.Add(t.Trim()))
                        // 重名会让「倾向_xxx」flag 互相覆盖，统计就废了
                        errors.Add($"optionTones 里「{t}」重复了，倾向 flag 会互相覆盖");
                }
            }
            if (historyTurns > defaultTurns)
                errors.Add($"historyTurns({historyTurns}) 大于 defaultTurns({defaultTurns})，多余的保留没有意义");
            return errors;
        }
    }
}
