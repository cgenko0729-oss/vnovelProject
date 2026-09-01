using System.Collections.Generic;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 擦雾玩法的判定数学：阶段推进与三档结果。
    ///
    /// **纯逻辑，不继承 MonoBehaviour**，可脱离场景单测（同 VNTouchScore / VNPhotoScore）。
    ///
    /// 【两条设计约束，改之前先读】
    ///
    /// 1. **阶段只升不降**。清晰度在回雾对抗下会反复穿越阈值，允许回退的话
    ///    玩家一停手就在阈值边界反复横跳，同一句台词播个没完。同 VNTouchScore 的教训。
    ///
    /// 2. **结算用历史峰值，不用结束瞬间的值**。时限到的那一帧刚好被雾吞掉一点，
    ///    就把玩家从「完美」打到「普通」，纯属运气而非操作。为了不让玩家困惑，
    ///    HUD 上会同时画出峰值刻度线——显示什么、结算什么，两者必须对得上。
    /// </summary>
    public class VNFogScore
    {
        /// <summary>一次更新之后发生了什么（模块据此决定演出）</summary>
        public struct Tick
        {
            /// <summary>阶段推进了</summary>
            public bool stageUp;
            /// <summary>推进后的阶段下标（stageUp 为 true 时有效）</summary>
            public int newStage;
        }

        /// <summary>已推进到第几个阶段（-1 = 还没到第一个阈值）</summary>
        public int Stage { get; private set; } = -1;

        /// <summary>当前清晰度 0~1</summary>
        public float Clarity { get; private set; }

        /// <summary>历史最高清晰度 0~1（结算用这个）</summary>
        public float Peak { get; private set; }

        readonly List<float> _thresholds = new List<float>();

        /// <param name="thresholds">阶段触发清晰度（0~1，会自动排序去重）</param>
        public void Init(IList<float> thresholds)
        {
            _thresholds.Clear();
            if (thresholds != null)
                foreach (float t in thresholds)
                    _thresholds.Add(Mathf.Clamp01(t));
            _thresholds.Sort();

            Stage = -1;
            Clarity = 0f;
            Peak = 0f;
        }

        public int StageCount => _thresholds.Count;

        /// <summary>喂入当前清晰度，返回这一帧是否跨过了新的阶段阈值</summary>
        public Tick Update(float clarity)
        {
            Clarity = Mathf.Clamp01(clarity);
            if (Clarity > Peak) Peak = Clarity;

            var tick = new Tick();
            // 一帧可能跨过多个阈值（雾团被一口气擦掉时），但只报最高的那个：
            // 连播两句台词反而乱，且低阶段的台词此刻已经不合时宜
            while (Stage + 1 < _thresholds.Count && Clarity >= _thresholds[Stage + 1])
            {
                Stage++;
                tick.stageUp = true;
                tick.newStage = Stage;
            }
            return tick;
        }

        /// <summary>三档结果判定（阈值为 0~1）。峰值优先——见类注释第 2 条。</summary>
        public static string Grade(float peak, float perfectAt, float normalAt,
            string perfect, string normal, string fail)
        {
            if (peak >= perfectAt) return perfect;
            if (peak >= normalAt) return normal;
            return fail;
        }

        /// <summary>达标（可以提前结束）</summary>
        public bool Reached(float perfectAt) => Peak >= perfectAt;
    }
}
