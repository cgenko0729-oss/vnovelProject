using System;
using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>擦拭道具的外观（图留空时由 VNProceduralTextures 程序化生成）</summary>
    public enum VNWiperKind
    {
        [InspectorName("雨刷")] Wiper = 0,
        [InspectorName("手掌")] Palm = 1,
        [InspectorName("抹布")] Cloth = 2,
        [InspectorName("手指")] Finger = 3,
    }

    /// <summary>
    /// 擦雾小游戏的定义资产：一场「擦起雾的玻璃看清 CG」要用到的全部参数与内容。
    /// 登记进 VNGameConfig 的「擦雾库」，剧本用 <c>event wipefog id:&lt;这个 id&gt;</c> 引用。
    ///
    /// 【调参提示：难度是算得出来的，不要瞎试】
    /// 每秒擦除面积 ≈ 笔刷直径 × 鼠标速度。1920×1080 下，笔刷 180px + 正常拖速 800px/s
    /// ≈ 每秒擦掉全屏的 6.9%，扣掉 1.5 倍的重叠浪费约 4.6%/秒。
    /// 再减去回雾速率就是净推进：回雾 3%/秒时净 +1.6%/秒，擦到「完美」90% 约需 55 秒，
    /// 刚好卡在 60 秒时限内——紧张但拿得到。这组数就是下面的出厂默认值。
    /// 回雾调到 3.5%/秒 完美就要 82 秒（拿不到），调到 2%/秒 只要 35 秒（毫无对抗）。
    ///
    /// 这个估算是**保守**的：边缘侵蚀只能吃掉已经擦净的像素，玩家还没擦到的地方
    /// 没得吃，所以实际回雾比设定值少、真实进度比算出来的快一些。
    /// 用估算定个起点，最终手感一定要自己试玩微调。
    ///
    /// 想直观地试，用 Tools → VN Effects → 预览 Preview → **擦雾调参 Fog Wipe Tuning**，
    /// 那个窗口会按当前参数算出预计通关秒数，不用进 Play Mode。
    /// </summary>
    [CreateAssetMenu(menuName = "VN/Fog Wipe Definition", fileName = "NewFogWipe")]
    public class VNFogWipeDef : ScriptableObject
    {
        /// <summary>一句台词（三语 + 可选语音）</summary>
        [Serializable]
        public class Line
        {
            [TextArea(1, 3)] public string text;
            [Header("英文/日文（留空回退中文）")]
            [TextArea(1, 3)] public string textEn;
            [TextArea(1, 3)] public string textJa;
            [Header("语音 id（VNAudio 的 voice 库；可留空）")]
            public string voice;

            public string Display
            {
                get
                {
                    string localized = VNLocale.Language == VNLanguage.English ? textEn
                        : VNLocale.Language == VNLanguage.Japanese ? textJa : null;
                    return string.IsNullOrEmpty(localized) ? text : localized;
                }
            }

            public bool IsValid => !string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// 一个清晰度阶段：擦到这个百分比时说一句话。
        /// 阶段**只升不降**（清晰度会因回雾反复穿越阈值，允许回退就会反复播同一句）。
        /// </summary>
        [Serializable]
        public class Stage
        {
            [Header("备注（只给自己看，方便在长列表里认出这条）")]
            public string note;

            [Header("触发清晰度（%）")]
            [Range(0f, 100f)]
            public float threshold = 30f;

            [Header("台词池（随机抽一条；可留空 = 这一阶段只加属性不说话）")]
            public List<Line> lines = new List<Line>();

            [Header("到达本阶段的属性奖励（走 VNStatsHud 钳制 + 飘字；可留空）")]
            public List<VNShopDef.StatOp> reward = new List<VNShopDef.StatOp>();

            public Line PickLine()
            {
                if (lines == null) return null;
                var pool = new List<Line>();
                foreach (var l in lines)
                    if (l != null && l.IsValid) pool.Add(l);
                return pool.Count == 0 ? null : pool[UnityEngine.Random.Range(0, pool.Count)];
            }
        }

        // ------------------------------------------------------------------

        [Header("剧本 event wipefog id:<这个 id> 引用（可中文，如 浴室镜面）")]
        public string fogWipeId;

        [Header("说话者显示名（留空 = 用剧本 vs: 的角色名）")]
        public string speakerName;

        // ---------------- 雾的外观 ----------------

        [Header("──────── 雾的外观 ────────")]
        [Header("雾色")]
        public Color fogColor = new Color(0.93f, 0.95f, 0.98f, 1f);

        [Header("★ 雾色混入比例：0 = 纯粹是模糊的底图，1 = 纯雾色（看不见剪影）。\n" +
                "这个数与下面的模糊半径合起来决定「不擦能看到多少」——\n" +
                "实测 0.55 时不擦也能看清七八成，玩家就没有擦的动机了；0.76 才是\n" +
                "「看得见有个人影、看不清五官」的分寸")]
        [Range(0f, 1f)]
        public float fogMix = 0.76f;

        [Header("雾的不透明度上限")]
        [Range(0f, 1f)]
        public float fogDensity = 0.92f;

        [Header("底图模糊半径（uv 单位）——雾是底图的模糊提亮版，剪影才透得出来")]
        [Range(0f, 0.05f)]
        public float blurAmount = 0.013f;

        [Header("模糊后提亮倍率")]
        [Range(0.5f, 3f)]
        public float brightness = 1.3f;

        [Header("★ 边界破碎程度。调到 0 会变成假的光滑圆形边缘，" +
                "这一条对真实感的贡献超过其他任何单项")]
        [Range(0f, 1f)]
        public float edgeNoise = 0.5f;

        [Header("噪声尺度（大 = 细碎的水汽纹理，小 = 大块）")]
        [Min(1f)]
        public float noiseScale = 14f;

        [Header("水汽颗粒强度")]
        [Range(0f, 0.5f)]
        public float grain = 0.075f;

        [Header("雾浓度曲线（>1 = 擦一点就变透，<1 = 要擦很干净才透）")]
        [Range(0.3f, 3f)]
        public float falloff = 1f;

        // ---------------- 笔刷 ----------------

        [Header("──────── 笔刷 ────────")]
        [Header("笔刷直径（屏幕像素 @1920 宽；难度的主要旋钮之一）")]
        [Range(40f, 500f)]
        public float brushDiameter = 180f;

        [Header("羽化带占半径的比例")]
        [Range(0f, 1f)]
        public float brushFeather = 0.35f;

        [Header("一笔能推进多少（1 = 一次划过就全清；0.5 = 要来回擦两遍才透）。\n" +
                "默认 1 对应「擦到就掉」的轻松取向；想要更费力的手感就往下调")]
        [Range(0.1f, 1f)]
        public float wipeStrength = 1f;

        [Header("道具外观（cursor.icon 留空时用这个程序化生成）")]
        public VNWiperKind cursorKind = VNWiperKind.Wiper;

        [Header("光标参数（icon 留空 = 按上面的 cursorKind 程序化生成）")]
        public VNInteractionItem cursor = new VNInteractionItem
        {
            id = "wiper",
            cursorHeight = 190f,
            idleAnim = VNCursorIdleAnim.Rock,
            idleFrequency = 0.9f,
            idleAmplitude = 4f,
            pressAnim = VNCursorPressAnim.Press,
            pressFrequency = 12f,
            pressAmplitude = 4f,
            tiltWithMotion = true,
            tiltMax = 18f,
        };

        // ---------------- 回雾 ----------------

        [Header("──────── 回雾（★ 难度的主要旋钮）────────")]
        [Header("边缘侵蚀：雾从画面四周往中间吞，每秒吞掉的整体清晰度（%）")]
        [Range(0f, 10f)]
        public float edgeRate = 2f;

        [Header("随机雾团：中心区随机冒雾，每秒总量（%）")]
        [Range(0f, 10f)]
        public float blobRate = 1f;

        [Header("雾团生成间隔（秒，随机区间）")]
        [Min(0.1f)]
        public float blobIntervalMin = 0.8f;
        [Min(0.1f)]
        public float blobIntervalMax = 1.5f;

        [Header("雾团半径（屏幕像素 @1920 宽，随机区间）")]
        [Min(10f)]
        public float blobRadiusMin = 60f;
        [Min(10f)]
        public float blobRadiusMax = 110f;

        // ---------------- 规则 ----------------

        [Header("──────── 规则 ────────")]
        [Header("时限（秒）；剧本 time: 可覆盖")]
        [Min(5f)]
        public float timeLimit = 60f;

        [Header("「完美」档清晰度门槛（%）；剧本 perfect: 可覆盖。达到即提前结束")]
        [Range(0f, 100f)]
        public float targetPerfect = 90f;

        [Header("「普通」档清晰度门槛（%）；剧本 target: 可覆盖")]
        [Range(0f, 100f)]
        public float targetNormal = 65f;

        [Header("结果名（剧本 * 结果行 精确匹配，永不翻译）")]
        public string outcomePerfect = "完美";
        public string outcomeNormal = "普通";
        public string outcomeFail = "失败";

        [Header("成绩 flag 前缀：<前缀>_清晰度 / _用时 / _档位（剧本 flag: 可覆盖）")]
        public string flagPrefix = "擦雾";

        // ---------------- 内容 ----------------

        [Header("──────── 分阶段台词 ────────")]
        [Header("按清晰度触发；顺序随意，运行时自动按 threshold 排序")]
        public List<Stage> stages = new List<Stage>();

        // ---------------- 音效覆盖 ----------------

        [Header("──────── 音效（留空 = 用代码合成的）────────")]
        public AudioClip wipeLoopSe;
        public AudioClip blobSe;
        public AudioClip clearSe;
        public AudioClip tickSe;

        // ==================================================================

        /// <summary>按 threshold 升序排好的有效阶段</summary>
        public List<Stage> SortedStages()
        {
            var list = new List<Stage>();
            if (stages != null)
                foreach (var s in stages)
                    if (s != null) list.Add(s);
            list.Sort((a, b) => a.threshold.CompareTo(b.threshold));
            return list;
        }

        void OnValidate()
        {
            if (blobIntervalMax < blobIntervalMin) blobIntervalMax = blobIntervalMin;
            if (blobRadiusMax < blobRadiusMin) blobRadiusMax = blobRadiusMin;
            // 普通档不该比完美档还高，否则「完美」永远拿不到
            if (targetNormal > targetPerfect) targetNormal = targetPerfect;
        }
    }
}
