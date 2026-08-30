using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 亲密互动的判定数学：兴奋度累计、阶段推进、每部位统计、冷却与拒绝计数。
    ///
    /// **纯逻辑，不继承 MonoBehaviour** —— 同 VNPhotoScore / VNBadmintonBallistics 的
    /// 分层习惯：手感全靠调这里的数，而调数需要能脱离场景反复跑。
    ///
    /// 时间一律传入 unscaledTime（模块不受快进 timeScale 影响）。
    /// </summary>
    public class VNTouchScore
    {
        /// <summary>一次输入之后发生了什么（模块据此决定演出）</summary>
        public struct Tick
        {
            /// <summary>该触发一次这个部位的反馈了</summary>
            public bool feedback;
            /// <summary>阶段推进了</summary>
            public bool stageUp;
            /// <summary>推进后的阶段（stageUp 为 true 时有效）</summary>
            public int newStage;
            /// <summary>本次被拒绝（摸了未解禁部位）</summary>
            public bool rejected;
            /// <summary>拒绝次数到上限 → 该判失败了</summary>
            public bool rejectOverflow;
        }

        public float Excite { get; private set; }
        public int Stage { get; private set; }
        public int RejectCount { get; private set; }

        /// <summary>本场累计抚摸量（所有部位之和，统计用）</summary>
        public float TotalUnits { get; private set; }

        List<float> _thresholds = new List<float>();
        readonly Dictionary<string, float> _zoneAmount = new Dictionary<string, float>();
        readonly Dictionary<string, int> _zoneTouches = new Dictionary<string, int>();
        readonly Dictionary<string, float> _pending = new Dictionary<string, float>();
        readonly Dictionary<string, float> _cooldownUntil = new Dictionary<string, float>();
        float _rejectReadyAt;

        public void Init(IList<float> thresholds)
        {
            _thresholds = thresholds != null
                ? new List<float>(thresholds) : new List<float> { 0f };
            if (_thresholds.Count == 0) _thresholds.Add(0f);

            Excite = 0f;
            Stage = 0;
            RejectCount = 0;
            TotalUnits = 0f;
            _zoneAmount.Clear();
            _zoneTouches.Clear();
            _pending.Clear();
            _cooldownUntil.Clear();
            _rejectReadyAt = 0f;
        }

        /// <summary>阶段数（至少 1）</summary>
        public int StageCount => _thresholds.Count;

        /// <summary>当前阶段到下一阶段之间的进度 0~1（进度条用；已在最高阶段则恒为 1）</summary>
        public float StageProgress
        {
            get
            {
                if (Stage >= _thresholds.Count - 1) return 1f;
                float lo = _thresholds[Stage], hi = _thresholds[Stage + 1];
                if (hi - lo <= 0.0001f) return 1f;
                return Mathf.Clamp01((Excite - lo) / (hi - lo));
            }
        }

        /// <summary>整场的总进度 0~1（以目标阶段的阈值为满格）</summary>
        public float ProgressTo(int targetStage)
        {
            targetStage = Mathf.Clamp(targetStage, 0, _thresholds.Count - 1);
            float goal = _thresholds[targetStage];
            if (goal <= 0.0001f) return Stage >= targetStage ? 1f : 0f;
            return Mathf.Clamp01(Excite / goal);
        }

        /// <summary>
        /// 加一笔抚摸量。units = 原始量（拖动距离换算或单击固定值），
        /// gain = 该部位 × 该道具的收益系数；feedbackEvery = 攒够多少 units 触发一次反馈。
        /// </summary>
        public Tick AddUnits(string zoneId, float units, float gain, float feedbackEvery)
        {
            var tick = new Tick { newStage = Stage };
            if (units <= 0f || string.IsNullOrEmpty(zoneId)) return tick;

            TotalUnits += units;
            _zoneAmount[zoneId] = ZoneAmount(zoneId) + units;

            Excite += units * gain;
            if (Excite < 0f) Excite = 0f;

            // 攒够一份就触发一次反馈（余量保留，不清零 —— 否则慢速抚摸永远攒不满）
            if (feedbackEvery > 0.0001f)
            {
                float pending = (_pending.TryGetValue(zoneId, out var p) ? p : 0f) + units;
                if (pending >= feedbackEvery)
                {
                    pending -= feedbackEvery;
                    tick.feedback = true;
                    _zoneTouches[zoneId] = ZoneTouches(zoneId) + 1;
                }
                _pending[zoneId] = pending;
            }

            int next = StageFor(_thresholds, Excite, Stage);
            if (next > Stage)
            {
                Stage = next;
                tick.stageUp = true;
                tick.newStage = next;
            }
            return tick;
        }

        /// <summary>
        /// 摸了未解禁部位。带冷却，避免按住不放的一瞬间就把拒绝次数扣满。
        /// limit = 0 表示永远不会因拒绝而失败。
        /// </summary>
        public Tick Reject(float now, float cooldown, int limit)
        {
            var tick = new Tick { newStage = Stage };
            if (now < _rejectReadyAt) return tick;

            _rejectReadyAt = now + Mathf.Max(0.1f, cooldown);
            RejectCount++;
            tick.rejected = true;
            tick.rejectOverflow = limit > 0 && RejectCount >= limit;
            return tick;
        }

        /// <summary>兴奋度自然衰减（手停下来就凉）。**不会让阶段回退**。</summary>
        public void Decay(float perSecond, float deltaTime)
        {
            if (perSecond <= 0f || deltaTime <= 0f) return;
            float floor = _thresholds[Mathf.Clamp(Stage, 0, _thresholds.Count - 1)];
            Excite = Mathf.Max(floor, Excite - perSecond * deltaTime);
        }

        /// <summary>直接加减兴奋度（反馈里的 excite 字段用）</summary>
        public void AddExcite(float delta)
        {
            Excite = Mathf.Max(0f, Excite + delta);
            int next = StageFor(_thresholds, Excite, Stage);
            if (next > Stage) Stage = next;
        }

        // ---- 冷却表（反馈条目共用一张，key 自己拼） ----

        public bool CoolDownReady(string key, float now) =>
            !_cooldownUntil.TryGetValue(key, out float until) || now >= until;

        public void MarkCoolDown(string key, float now, float cooldown)
        {
            if (cooldown > 0f) _cooldownUntil[key] = now + cooldown;
        }

        // ---- 统计查询 ----

        public float ZoneAmount(string zoneId) =>
            zoneId != null && _zoneAmount.TryGetValue(zoneId, out var v) ? v : 0f;

        public int ZoneTouches(string zoneId) =>
            zoneId != null && _zoneTouches.TryGetValue(zoneId, out var v) ? v : 0;

        public IEnumerable<KeyValuePair<string, int>> AllZoneTouches => _zoneTouches;

        /// <summary>
        /// 阈值表 + 当前兴奋度 → 阶段。**只升不降**：传入 current，返回值不会比它小。
        /// 不做回退是刻意的 —— 允许回退的话，玩家停手时表情会在阈值边界反复横跳。
        /// </summary>
        public static int StageFor(IList<float> thresholds, float excite, int current)
        {
            if (thresholds == null || thresholds.Count == 0) return current;
            int found = 0;
            for (int i = 0; i < thresholds.Count; i++)
                if (excite >= thresholds[i]) found = i;
            return Mathf.Max(current, found);
        }
    }
}
