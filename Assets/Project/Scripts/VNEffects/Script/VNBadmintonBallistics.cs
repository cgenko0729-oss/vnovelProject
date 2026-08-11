using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 羽毛球的手感参数集合。全部长度单位是 1920×1080 画布下、锚点(0.5, 0) 的像素。
    ///
    /// 数值来源：参考实现（Student Age 的 BadmintonMiniGameView）设计分辨率约 2560×1440，
    /// 本专案是 1920×1080，所有长度量一律 ×0.75。完整换算表见
    /// 《羽毛球小游戏实施计划.md》第四节。
    ///
    /// ★ flySpeedScale 是必须的额外修正：坐标缩放 k 后飞行速度只会变成 √k 倍，
    ///   但要保持飞行时间不变需要 k 倍，因此还要再乘 √k = 0.866。少了这一下，
    ///   球相对屏幕会快约 15%，回合明显变短、来不及跑位（而且很难归因）。
    ///
    /// 这个类将来会被 VNBadmintonDef 资产整块持有（P3），现在先当默认值用。
    /// </summary>
    [System.Serializable]
    public class VNBadmintonTuning
    {
        [Header("──── 场地几何（px，锚点底边中心）────")]
        [Tooltip("地面线：球落到这个高度即判定落地，角色也站在这条线上")]
        public float groundY = 195f;
        [Tooltip("发球时球的初始高度")]
        public float ballStartY = 375f;
        [Tooltip("发球的目标落点 x（距中线）")]
        public float serveTargetX = 300f;
        [Tooltip("双方开局站位 x（距中线）")]
        public float startStandX = 375f;
        [Tooltip("球网上沿高度 = 过网最低高度")]
        public float netTopY = 450f;
        [Tooltip("过网最高高度（高吊球上限）")]
        public float netMaxY = 750f;
        [Tooltip("低手/高手挥拍的球高分界")]
        public float lowSwingY = 488f;
        [Tooltip("来球最高点高于这个值才值得起跳扣杀")]
        public float smashNeedY = 600f;

        [Header("──── 活动范围 ────")]
        [Tooltip("离中线最近能站到哪")]
        public float moveMinX = 150f;
        [Tooltip("离中线最远能站到哪 = 边线（球落在这之外算出界）")]
        public float moveMaxX = 750f;
        [Tooltip("出界落点抽样：边线外 min~max")]
        public float outMarginMin = 38f;
        public float outMarginMax = 225f;
        [Tooltip("抛物线顶点钳制上限（别让球飞出画面太多）")]
        public float apexClamp = 900f;

        [Header("──── 判定 ────")]
        [Tooltip("精准判定窗口：球在身前这个距离内击中 = 必定界内")]
        public float perfectDistance = 90f;
        [Tooltip("球在身后多少距离内还算够得着（超过就是勉强够到）")]
        public float behindTolerance = 15f;
        [Tooltip("扣杀时的宽容窗口：身前这个距离内扣杀也算必定界内")]
        public float heavyTolerance = 120f;
        [Tooltip("非精准击球的界内概率（难度参数，AI 与玩家共用一套算法）")]
        [Range(0f, 1f)] public float perfectRate = 0.75f;

        [Header("──── 速度 ────")]
        [Tooltip("速度常数：参考实现是 |重力|×200")]
        public float speedConstant = 1962f;
        [Tooltip("★ 坐标缩放的时间不变修正，见类注释")]
        public float flySpeedScale = 0.866f;
        [Tooltip("超过这个速度的扣杀才会再乘力度系数")]
        public float heavySpeedThreshold = 1350f;
        [Tooltip("球速上限（防止极端曲率解出天文数字）")]
        public float maxFlySpeed = 4000f;

        [Header("──── 跳跃与移动 ────")]
        [Tooltip("重力倍数（作用于跳跃，不作用于球）")]
        public float gravityScale = 3f;
        [Tooltip("米 → 像素。参考实现是 ×100（2560 宽），我们 ×75")]
        public float pixelsPerUnit = 75f;
        [Tooltip("落地下坠速度 px/s")]
        public float fallSpeed = 750f;

        [Header("──── 玩家能力（可被养成属性覆盖）────")]
        [Tooltip("扣杀力度系数")]
        public float playerPower = 1f;
        [Tooltip("移动速度（单位/秒，再 × pixelsPerUnit）")]
        public float playerMoveSpeed = 8f;
        [Tooltip("跳跃高度（单位，再 × pixelsPerUnit）")]
        public float playerJumpHeight = 3f;

        [Header("──── 对手能力（难度）────")]
        public float opponentPower = 0.6f;
        public float opponentMoveSpeed = 8f;
        public float opponentJumpHeight = 3f;
        [Tooltip("对手到位了也漏球的反面：接到球的概率")]
        [Range(0f, 1f)] public float opponentHitRate = 0.9f;
        [Tooltip("对手的扣杀倾向")]
        [Range(0f, 1f)] public float opponentHeavyRate = 0.2f;
        [Tooltip("给玩家看多长的轨迹预告（1 = 全程，越小越难）")]
        [Range(0f, 1f)] public float trackDisplayRate = 1f;

        [Header("──── 轨迹预告 ────")]
        [Tooltip("轨迹虚点的间距")]
        public float trackSpacing = 38f;

        /// <summary>跳跃初速度：v0 = √(2·g·h)，g 已含 gravityScale</summary>
        public float JumpSpeed(float heightUnits)
        {
            float g = Mathf.Abs(Physics2D.gravity.y) * gravityScale;
            return Mathf.Sqrt(Mathf.Max(0f, 2f * g * heightUnits));
        }

        /// <summary>跳跃用的重力（负值，单位/秒²）</summary>
        public float JumpGravity => -Mathf.Abs(Physics2D.gravity.y) * gravityScale;

        public VNBadmintonTuning Clone() => (VNBadmintonTuning)MemberwiseClone();
    }

    /// <summary>一条已解出的球路：y = a·x² + b·x + c，外加飞行速度</summary>
    public struct VNBadmintonArc
    {
        public float a, b, c;
        /// <summary>带符号的水平速度 px/s（正 = 向右飞）</summary>
        public float speed;
        public float startX, endX;
        /// <summary>本次是否扣杀（表现层用）</summary>
        public bool heavy;

        public float Y(float x) => a * x * x + b * x + c;
        /// <summary>顶点 x（a &lt; 0 时是最高点）</summary>
        public float ApexX => Mathf.Approximately(a, 0f) ? 0f : -b / (2f * a);
        public float ApexY => Y(ApexX);
        public bool Valid => a < 0f && !float.IsNaN(a) && !float.IsInfinity(a) && speed != 0f;
    }

    /// <summary>
    /// 羽毛球弹道数学：三点定抛物线、飞行速度、落点抽样、精准判定、AI 击球点求解。
    ///
    /// **纯静态、无 MonoBehaviour / 无 UI 依赖**——所有玩法数学集中在这里，
    /// 表现层（VNBadmintonModule / VNBadmintonActor）只负责把结果画出来。
    /// 逻辑 1:1 复刻参考实现的 CalcTrack / CalcAccurate / GetRandomEndPos。
    /// </summary>
    public static class VNBadmintonBallistics
    {
        /// <summary>
        /// 三点定二次曲线 y = ax²+bx+c。三点 x 两两不同才有解。
        /// （参考实现里那三坨行列式展开就是这个；这里保留通用形式。）
        /// </summary>
        public static bool SolveParabola(Vector2 p0, Vector2 p1, Vector2 p2,
            out float a, out float b, out float c)
        {
            a = b = c = 0f;
            float x0 = p0.x, x1 = p1.x, x2 = p2.x;
            float y0 = p0.y, y1 = p1.y, y2 = p2.y;

            float det = x0 * x0 * x1 + x1 * x1 * x2 + x2 * x2 * x0
                      - x2 * x2 * x1 - x1 * x1 * x0 - x0 * x0 * x2;
            if (Mathf.Abs(det) < 1e-4f) return false;

            a = (y0 * x1 + y1 * x2 + y2 * x0 - y2 * x1 - y1 * x0 - y0 * x2) / det;
            b = (x0 * x0 * y1 + x1 * x1 * y2 + x2 * x2 * y0
               - x2 * x2 * y1 - x1 * x1 * y0 - x0 * x0 * y2) / det;
            c = (x0 * x0 * x1 * y2 + x1 * x1 * x2 * y0 + x2 * x2 * x0 * y1
               - x2 * x2 * x1 * y0 - x1 * x1 * x0 * y2 - x0 * x0 * x2 * y1) / det;
            return !float.IsNaN(a) && !float.IsInfinity(a);
        }

        /// <summary>
        /// 造一条球路。核心步骤（照抄参考实现）：
        ///   ① 求起点→落点直线在球网处(x=0)的高度，作为过网高度的下限；
        ///   ② 扣杀取下限+10（贴网而过），否则在 [下限, netMaxY] 之间随机；
        ///   ③ 起点 / 过网点 / 落点三点定抛物线；
        ///   ④ 速度 = √(|speedConstant / (2a)|) × 方向 × flySpeedScale。
        /// </summary>
        public static bool BuildArc(Vector2 start, Vector2 end, VNBadmintonTuning t,
            float power, bool heavy, System.Random rng, out VNBadmintonArc arc)
        {
            arc = default;
            if (Mathf.Approximately(start.x, end.x)) return false;

            // ① 直线在 x=0 处的高度
            float lineAtNet = (-end.x) / (start.x - end.x) * (start.y - end.y) + end.y;

            // ② 过网高度
            float netY;
            if (heavy)
            {
                netY = Mathf.Max(t.netTopY, lineAtNet) + 10f;
            }
            else
            {
                float lo = Mathf.Max(t.netTopY + 10f, lineAtNet);
                float hi = Mathf.Max(lo + 1f, t.netMaxY);
                netY = Mathf.Lerp(lo, hi, (float)rng.NextDouble());
            }

            // ③ 三点定抛物线
            if (!SolveParabola(start, new Vector2(0f, netY), end,
                    out float a, out float b, out float c))
                return false;
            if (a >= 0f) return false; // 开口朝上 = 不是一条能落地的球路

            // ④ 速度
            float dir = Mathf.Sign(end.x - start.x);
            float speed = Mathf.Sqrt(Mathf.Abs(t.speedConstant / (2f * a)))
                          * dir * t.flySpeedScale;
            if (heavy && Mathf.Abs(speed) > t.heavySpeedThreshold) speed *= Mathf.Max(0.1f, power);
            speed = Mathf.Clamp(speed, -t.maxFlySpeed, t.maxFlySpeed);

            arc = new VNBadmintonArc
            {
                a = a, b = b, c = c,
                speed = speed,
                startX = start.x,
                endX = end.x,
                heavy = heavy,
            };
            return arc.Valid;
        }

        /// <summary>
        /// 精准度：击球点与球拍的水平距离 → 界内概率。
        /// 距离定义为「球在身前多远」（正 = 球在挥拍方向那一侧，负 = 球在身后）。
        ///   身前 0~perfectDistance（含身后一点点容差）→ 1.0 必定界内，冒「精准」
        ///   更远 → perfectRate；扣杀另有 heavyTolerance 的宽容窗口
        ///   身后太多 → 0.5
        /// </summary>
        public static float Accuracy(float distance, VNBadmintonTuning t, bool heavy)
        {
            if (distance >= -t.behindTolerance && distance <= t.perfectDistance) return 1f;
            if (distance > t.perfectDistance)
            {
                if (heavy && distance <= t.heavyTolerance) return 1f;
                return t.perfectRate;
            }
            return 0.5f;
        }

        /// <summary>距离是否落在「精准」窗口内（表现层冒字用，与 Accuracy 的第一档同义）</summary>
        public static bool IsPerfect(float distance, VNBadmintonTuning t) =>
            distance >= -t.behindTolerance && distance <= t.perfectDistance;

        /// <summary>
        /// 按精准度抽一个落点：命中概率内落界内，否则落到边线外 outMargin 区间。
        /// travelDir 是球的飞行方向（+1 向右 / -1 向左）。
        /// </summary>
        public static Vector2 SampleEndPos(int travelDir, float accuracy,
            VNBadmintonTuning t, System.Random rng)
        {
            float x = Mathf.Lerp(t.moveMinX + 50f, t.moveMaxX, (float)rng.NextDouble());
            if ((float)rng.NextDouble() > accuracy)
                x = t.moveMaxX + Mathf.Lerp(t.outMarginMin, t.outMarginMax, (float)rng.NextDouble());
            return new Vector2(x * travelDir, t.groundY);
        }

        /// <summary>
        /// 解 y = 目标高度 时的两个 x 根。无实根返回 false。
        /// AI 求击球点、玩家扣杀指示圈都用它。
        /// </summary>
        public static bool SolveXAtHeight(in VNBadmintonArc arc, float y,
            out float xLow, out float xHigh)
        {
            xLow = xHigh = 0f;
            float cc = arc.c - y;
            float disc = arc.b * arc.b - 4f * arc.a * cc;
            if (disc < 0f) return false;
            float sq = Mathf.Sqrt(disc);
            float r1 = (-arc.b + sq) / (2f * arc.a);
            float r2 = (-arc.b - sq) / (2f * arc.a);
            xLow = Mathf.Min(r1, r2);
            xHigh = Mathf.Max(r1, r2);
            return true;
        }

        /// <summary>
        /// 求接球方应该在哪个点击球（AI 跑位 + 玩家扣杀指示圈共用）。
        ///
        /// 做法照抄参考实现：先看这条球路在接球方活动范围内能达到的高度区间，
        /// 再按「是否想扣杀」在区间里挑一个高度，反解回 x。
        /// </summary>
        /// <param name="receiverDir">接球方在哪一侧：+1 右 / -1 左</param>
        /// <param name="wantSmash">是否想打高点扣杀</param>
        public static bool SolveReceivePoint(in VNBadmintonArc arc, int receiverDir,
            bool wantSmash, VNBadmintonTuning t, System.Random rng, out Vector2 point)
        {
            point = default;
            if (!arc.Valid) return false;

            // 接球方的活动范围（带符号）
            float nearX = t.moveMinX * receiverDir;   // 靠近球网那端
            float farX = t.moveMaxX * receiverDir;    // 靠近底线那端

            float nearY = arc.Y(nearX);
            float farY = arc.Y(Mathf.Abs(arc.endX) > t.moveMaxX ? farX : arc.endX);

            // 能够到的高度区间（顶点被活动范围截断时以边界为准）
            float apexX = arc.ApexX;
            float hiY = (apexX * receiverDir >= nearX * receiverDir &&
                         apexX * receiverDir <= farX * receiverDir)
                        ? arc.ApexY : Mathf.Max(nearY, farY);
            float loY = Mathf.Min(nearY, farY);

            hiY = Mathf.Min(hiY, t.apexClamp);
            loY = Mathf.Max(loY, t.groundY);
            if (hiY <= loY) hiY = loY + 1f;

            // 挑一个击球高度
            float targetY;
            if (wantSmash && hiY > t.smashNeedY)
                targetY = Mathf.Lerp(Mathf.Max(loY, t.smashNeedY), hiY, (float)rng.NextDouble());
            else
                targetY = Mathf.Lerp(loY, Mathf.Min(hiY, t.smashNeedY), (float)rng.NextDouble());

            if (!SolveXAtHeight(arc, targetY, out float xLow, out float xHigh)) return false;

            // 取接球方那一侧、且离球网较远的那个根（球先过网再落下）
            float x = receiverDir > 0 ? xHigh : xLow;
            x = Mathf.Clamp(x * receiverDir, t.moveMinX, t.moveMaxX) * receiverDir;

            point = new Vector2(x, arc.Y(x));
            return true;
        }

        /// <summary>球是否落在界内（|x| 未超过边线）</summary>
        public static bool InBounds(float x, VNBadmintonTuning t) =>
            Mathf.Abs(x) <= t.moveMaxX + t.outMarginMin;
    }
}
