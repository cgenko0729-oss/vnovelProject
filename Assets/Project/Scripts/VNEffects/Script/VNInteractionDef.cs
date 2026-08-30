using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>光标待机动画（每个道具在资产里各自选一种）</summary>
    public enum VNCursorIdleAnim
    {
        [InspectorName("不动")] None = 0,
        [InspectorName("左右摆动")] SwingX = 1,
        [InspectorName("上下摆动")] SwingY = 2,
        [InspectorName("左右摇（旋转）")] Rock = 3,
        [InspectorName("呼吸缩放")] Breathe = 4,
    }

    /// <summary>按住左键时的光标动画</summary>
    public enum VNCursorPressAnim
    {
        [InspectorName("同待机")] Same = 0,
        [InspectorName("高频震动")] Vibrate = 1,
        [InspectorName("快速摆动")] FastSwing = 2,
        [InspectorName("按压缩小")] Press = 3,
    }

    /// <summary>
    /// 一次反馈：摸到某处 / 推进到某阶段 / 被拒绝时发生的事。
    /// 三处共用同一个结构，所以「阶段推进大台词」和「碎语音」写法一致。
    ///
    /// 字段部分由模块直接执行；<see cref="scriptLines"/> 走
    /// VNScriptRunner.RunInlineCo()，能写任意演出命令（fx / camseq / liquid …），
    /// 但控制流命令（jump / choice / call / event …）被白名单挡掉 ——
    /// 它们会破坏正挂起等待本模块的 Runner 状态。
    /// </summary>
    [Serializable]
    public class VNInteractionFeedback
    {
        [Header("备注（只给自己看，方便在长列表里认出这条）")]
        public string note;

        [Header("触发条件：阶段下限 / 上限（-1 = 不限）")]
        public int minStage = -1;
        public int maxStage = -1;

        [Header("随机池里的权重（同时满足条件的多条按权重抽一条）")]
        [Min(0f)]
        public float weight = 1f;

        [Header("冷却（秒）：这条触发后多久之内不再触发")]
        [Min(0f)]
        public float cooldown = 1.5f;

        [Header("表现")]
        [Header("换表情（留空 = 不改）")]
        public string expression;

        [Header("漫符：汗 / 怒 / 心 / 音符 / 红晕 …（留空 = 不出）")]
        public string mark;

        [Header("情绪动作：惊讶 / 生气 / 害羞 / 沮丧 / 点头 / 摇头（留空 = 不做）")]
        public string emote;

        [Header("叠加层 id（潮红 / 汗 / 泪，见 VNCharacterDef.overlays；留空 = 不改）")]
        public string overlay;

        [Header("叠加层目标强度 0~1")]
        [Range(0f, 1f)]
        public float overlayStrength = 1f;

        [Header("台词（中文；留空 = 不说话）")]
        [TextArea(1, 3)]
        public string line;
        [Header("台词译文 · 英文（留空回退中文）")]
        [TextArea(1, 3)]
        public string lineEn;
        [Header("台词译文 · 日文（留空回退中文）")]
        [TextArea(1, 3)]
        public string lineJa;

        [Header("勾上 = 说完这句要等玩家推进，期间暂停抚摸判定。" +
                "阶段推进的大台词该勾；过程中的碎反应别勾，否则一直被打断")]
        public bool blocking;

        [Header("语音 id 池（走 VNAudio 音频库，随机抽一条且不重复上一条）")]
        public List<string> voicePool = new List<string>();

        [Header("音效 id（留空 = 不放）")]
        public string se;

        [Header("数值")]
        [Header("兴奋度增减（拒绝反馈可以给负值）")]
        public float excite;

        [Header("属性变动，写法同剧本 stat 命令，如「好感 +2」（留空 = 不动）")]
        public string statOp;

        [Header("内嵌剧本行：想要 fx / camseq / liquid 之类的复杂演出就写在这里，一行一条")]
        [TextArea(2, 6)]
        public string scriptLines;

        /// <summary>当前语言的台词（译文留空回退中文）</summary>
        public string LocalizedLine
        {
            get
            {
                switch (VNLocale.Language)
                {
                    case VNLanguage.English: return string.IsNullOrEmpty(lineEn) ? line : lineEn;
                    case VNLanguage.Japanese: return string.IsNullOrEmpty(lineJa) ? line : lineJa;
                    default: return line;
                }
            }
        }

        public bool StageOk(int stage) =>
            (minStage < 0 || stage >= minStage) && (maxStage < 0 || stage <= maxStage);

        /// <summary>完全没内容的空条目（配置漏填时跳过，不要白白占掉一次冷却）</summary>
        public bool IsEmpty =>
            string.IsNullOrEmpty(expression) && string.IsNullOrEmpty(mark) &&
            string.IsNullOrEmpty(emote) && string.IsNullOrEmpty(overlay) &&
            string.IsNullOrEmpty(line) && string.IsNullOrEmpty(se) &&
            string.IsNullOrEmpty(statOp) && string.IsNullOrEmpty(scriptLines) &&
            (voicePool == null || voicePool.Count == 0) && Mathf.Approximately(excite, 0f);
    }

    /// <summary>玩家可用的道具（= 鼠标光标图标）</summary>
    [Serializable]
    public class VNInteractionItem
    {
        [Header("道具 id（剧本 items: 里引用）")]
        public string id;

        [Header("道具栏显示名（留空 = 用 id）")]
        public string displayName;

        [Header("光标图标")]
        public Sprite icon;

        [Header("光标显示尺寸（像素，按图片长宽比取其中的高度）")]
        [Min(8f)]
        public float cursorHeight = 160f;

        [Header("图标热点（归一化，(0,0) = 图中心，(0,0.5) = 图的上边缘）。" +
                "手掌类填掌心、长条道具填顶端")]
        public Vector2 hotspot = Vector2.zero;

        [Header("图标朝向修正（度）")]
        public float iconRotation;

        [Header("待机动画")]
        public VNCursorIdleAnim idleAnim = VNCursorIdleAnim.SwingX;
        [Header("待机动画频率（次/秒）")]
        [Min(0f)]
        public float idleFrequency = 1.4f;
        [Header("待机动画幅度（像素；旋转类则是度）")]
        [Min(0f)]
        public float idleAmplitude = 10f;

        [Header("按住左键时的动画")]
        public VNCursorPressAnim pressAnim = VNCursorPressAnim.FastSwing;
        [Header("按住时频率（次/秒）")]
        [Min(0f)]
        public float pressFrequency = 14f;
        [Header("按住时幅度")]
        [Min(0f)]
        public float pressAmplitude = 6f;

        [Header("跟随拖动方向倾斜（拖得越快倾得越多）")]
        public bool tiltWithMotion = true;
        [Header("倾斜上限（度）")]
        [Range(0f, 90f)]
        public float tiltMax = 22f;

        [Header("这个道具的整体增益倍率")]
        [Min(0f)]
        public float gainScale = 1f;

        [Header("解锁条件（flag 表达式，写法同剧本 if；留空 = 一直可用）")]
        public string unlockCondition;

        public string Label => string.IsNullOrEmpty(displayName) ? id : displayName;
    }

    /// <summary>互动的一个阶段。跨过阈值即进入，**只升不降**（不做回退，
    /// 否则在阈值边界表情会反复横跳，也不符合体验）。</summary>
    [Serializable]
    public class VNInteractionStage
    {
        [Header("阶段名（UI 显示与调试用，如 平静 / 心动 / 情动）")]
        public string name;

        [Header("进入本阶段所需的兴奋度")]
        [Min(0f)]
        public float threshold;

        [Header("本阶段的常态表情（留空 = 不强制）")]
        public string idleExpression;

        [Header("进入本阶段时触发一次的反馈")]
        public VNInteractionFeedback enterFeedback = new VNInteractionFeedback();
    }

    /// <summary>某个部位（× 某个道具）的反馈规则</summary>
    [Serializable]
    public class VNInteractionZoneRule
    {
        [Header("部位 id（对应 VNTouchZoneDef 里的部位）")]
        public string zoneId;

        [Header("限定道具 id（留空 = 所有道具都适用）")]
        public string itemId;

        [Header("每单位抚摸量的兴奋度收益")]
        public float gainPerUnit = 1f;

        [Header("反馈池：满足阶段条件的按权重随机抽一条")]
        public List<VNInteractionFeedback> feedbacks = new List<VNInteractionFeedback>();

        [Header("累计到多少抚摸量才触发一次反馈")]
        [Min(0.01f)]
        public float feedbackEvery = 8f;

        public bool Matches(string zone, string item) =>
            zoneId == zone && (string.IsNullOrEmpty(itemId) || itemId == item);
    }

    /// <summary>
    /// 一场互动的完整规则（Create → VN → Interaction Definition）。
    /// 剧本 <c>event interact id:&lt;这个 id&gt;</c> 引用。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Interaction Definition", fileName = "NewInteraction")]
    public class VNInteractionDef : ScriptableObject
    {
        [Header("剧本 id: 引用的 id")]
        public string id;

        [Header("标题（互动界面顶部显示；留空 = 不显示）")]
        public string title;

        [Header("道具（剧本 items: 可再缩小范围；留空 items: = 全部可用）")]
        public List<VNInteractionItem> items = new List<VNInteractionItem>();

        [Header("阶段（按 threshold 从小到大；第一个应为 0 = 起始阶段）")]
        public List<VNInteractionStage> stages = new List<VNInteractionStage>();

        [Header("部位规则")]
        public List<VNInteractionZoneRule> rules = new List<VNInteractionZoneRule>();

        [Header("判定手感")]
        [Header("拖动多少像素算 1 单位抚摸量")]
        [Min(1f)]
        public float dragPixelsPerUnit = 60f;

        [Header("单击一次算多少单位抚摸量")]
        [Min(0f)]
        public float clickUnits = 0.6f;

        [Header("兴奋度自然衰减（每秒；0 = 不衰减）。手停下来就凉，逼玩家持续操作")]
        [Min(0f)]
        public float exciteDecayPerSecond = 0f;

        [Header("禁忌与拒绝")]
        [Header("摸未解禁部位时的反馈池")]
        public List<VNInteractionFeedback> rejectFeedbacks = new List<VNInteractionFeedback>();

        [Header("被拒绝几次算失败（0 = 不会因此失败）")]
        [Min(0)]
        public int rejectLimit = 3;

        [Header("两次拒绝判定之间的最短间隔（秒），防止按住不放瞬间扣满")]
        [Min(0.1f)]
        public float rejectCooldown = 1.2f;

        [Header("结束条件")]
        [Header("推进到第几阶段就算达成（0 = 要走完最后一个阶段）")]
        [Min(0)]
        public int targetStage;

        [Header("限时（秒，0 = 不限时）")]
        [Min(0f)]
        public float timeLimit;

        [Header("允许玩家点右下角「结束」主动收手")]
        public bool allowManualEnd = true;

        [Header("达成目标阶段后是否立即结束（关 = 让玩家自己收手）")]
        public bool autoEndOnTarget = true;

        [Header("结果名（对应剧本的 * 结果行）")]
        public string outcomeSatisfied = "满足";
        public string outcomeNormal = "普通";
        public string outcomeRejected = "拒绝";

        [Header("结束时的反馈（三种结果各一）")]
        public VNInteractionFeedback endSatisfied = new VNInteractionFeedback();
        public VNInteractionFeedback endNormal = new VNInteractionFeedback();
        public VNInteractionFeedback endRejected = new VNInteractionFeedback();

        [Header("成绩 flag 前缀（剧本 flag: 可覆盖）：写 <前缀>_兴奋度 / _阶段 / _拒绝数 / _<部位>次数")]
        public string flagPrefix;

        // ------------------------------------------------------------------

        /// <summary>目标阶段索引（0 = 取最后一个阶段）</summary>
        public int ResolvedTargetStage =>
            targetStage > 0 ? Mathf.Min(targetStage, stages.Count - 1)
                            : Mathf.Max(0, stages.Count - 1);

        public VNInteractionItem FindItem(string itemId)
        {
            foreach (var it in items)
                if (it != null && it.id == itemId) return it;
            return null;
        }

        /// <summary>该部位 × 该道具适用的规则（优先返回指定了道具的那条）</summary>
        public VNInteractionZoneRule FindRule(string zoneId, string itemId)
        {
            VNInteractionZoneRule fallback = null;
            foreach (var r in rules)
            {
                if (r == null || r.zoneId != zoneId) continue;
                if (!string.IsNullOrEmpty(r.itemId))
                {
                    if (r.itemId == itemId) return r;   // 精确匹配优先
                }
                else if (fallback == null) fallback = r;
            }
            return fallback;
        }

        /// <summary>阶段阈值表（给 VNTouchScore 用）</summary>
        public List<float> Thresholds()
        {
            var list = new List<float>(stages.Count);
            foreach (var s in stages) list.Add(s != null ? s.threshold : 0f);
            return list;
        }
    }
}
