using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 羽毛球对手 / 难度定义资产。一条 = 一个可以约战的对手。
    ///
    /// 对应参考实现的两张表：
    ///   BadmintonLevelCfg（六项难度参数 + 对手 npc + 目标分）
    ///   LoveBadmintonCfg（六类随机台词）
    /// 这里合并成一个资产，因为「跟谁打」和「多难」在本项目里本来就是一回事。
    ///
    /// 剧本用法：event badminton vs:小雪 id:校队
    ///   id: 指这个资产的 badmintonId；省略时退回用 vs: 的角色 id 去找同名资产。
    ///
    /// 战绩不存在资产里——比分/精准数/最长回合写 VNFlags，随存档走、if 分支可直接判断。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Badminton Definition", fileName = "NewBadminton")]
    public class VNBadmintonDef : ScriptableObject
    {
        /// <summary>一句台词（三语）</summary>
        [System.Serializable]
        public class Talk
        {
            [TextArea(1, 2)] public string text;
            [Header("英文/日文（留空回退中文）")]
            [TextArea(1, 2)] public string textEn;
            [TextArea(1, 2)] public string textJa;

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

        /// <summary>一方的六类台词。每类留空 = 该情境不说话。</summary>
        [System.Serializable]
        public class TalkSet
        {
            [Header("发球时")] public List<Talk> serve = new List<Talk>();
            [Header("普通击球")] public List<Talk> hit = new List<Talk>();
            [Header("扣杀 / 起跳")] public List<Talk> smash = new List<Talk>();
            [Header("对方打出好球（夸对手）")] public List<Talk> praise = new List<Talk>();
            [Header("自己得分")] public List<Talk> score = new List<Talk>();
            [Header("自己失分")] public List<Talk> loseScore = new List<Talk>();

            public bool Any =>
                Count(serve) + Count(hit) + Count(smash) +
                Count(praise) + Count(score) + Count(loseScore) > 0;

            static int Count(List<Talk> l) => l?.Count ?? 0;

            /// <summary>随机取一条；该类为空返回 null</summary>
            public string Pick(List<Talk> lines, System.Random rng)
            {
                if (lines == null || lines.Count == 0) return null;
                var t = lines[rng.Next(lines.Count)];
                return t == null ? null : t.Display;
            }
        }

        [Header("剧本 event badminton id:<这里> 引用的 id（可中文，如 校队 / 王牌）")]
        public string badmintonId;

        [Header("记分板标题（留空 = 不显示；剧本 title: 可覆盖）")]
        public string title;
        public string titleEn;
        public string titleJa;

        [Header("──────── 对手 ────────")]
        [Header("对手显示名（留空 = 用 vs: 指定的角色名）")]
        public string opponentName;
        public string opponentNameEn;
        public string opponentNameJa;

        [Header("对手的羽球专用侧身立绘（留空 = 用下面的角色 id 回退，再留空 = 剪影占位）")]
        public Sprite opponentBody;
        [Header("回退取立绘的角色 id（VNCharacterDef.id）")]
        public string opponentCharacterId;

        [Header("──────── 玩家一侧 ────────")]
        [Header("玩家的羽球专用侧身立绘（留空 = 剪影占位）")]
        public Sprite playerBody;
        [Header("球拍图（留空 = 程序化占位球拍）")]
        public Sprite racket;
        [Header("挥拍手臂图（可选；留空 = 立绘模式不画手臂，占位模式画方块手臂）")]
        public Sprite arm;

        [Header("──────── 场景 ────────")]
        [Header("远景底图（留空 = 程序化渐变天空）")]
        public Sprite backdrop;

        [Header("──────── 赛制与手感 ────────")]
        [Header("目标分数（净胜 2 分制不变；剧本 target: 可覆盖）")]
        public int targetScore = 5;

        [Header("全部手感与难度参数（换算依据见《羽毛球小游戏实施计划.md》第四节）")]
        public VNBadmintonTuning tuning = new VNBadmintonTuning();

        [Header("──────── 台词 ────────")]
        [Header("台词触发概率（0 = 全程不说话）")]
        [Range(0f, 1f)] public float talkRate = 0.5f;
        [Header("对手台词")] public TalkSet opponentTalk = new TalkSet();
        [Header("玩家台词")] public TalkSet playerTalk = new TalkSet();

        public string DisplayTitle
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? titleEn
                    : VNLocale.Language == VNLanguage.Japanese ? titleJa : null;
                return string.IsNullOrEmpty(localized) ? title : localized;
            }
        }

        public string DisplayOpponentName
        {
            get
            {
                string localized = VNLocale.Language == VNLanguage.English ? opponentNameEn
                    : VNLocale.Language == VNLanguage.Japanese ? opponentNameJa : null;
                return string.IsNullOrEmpty(localized) ? opponentName : localized;
            }
        }

        /// <summary>台词总开关：概率 > 0 且至少一方配了台词</summary>
        public bool TalkEnabled => talkRate > 0f && (opponentTalk.Any || playerTalk.Any);
    }
}
