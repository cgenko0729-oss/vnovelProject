using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 偷拍模式的**纯逻辑层**（无 MonoBehaviour，可单测）：
    /// 取景框的钳制、「她在不在框里 / 有多居中」的权重、察觉度涨速。
    ///
    /// 坐标约定与 VNCamera 一致：画布中心为原点、1920×1080 坐标系；
    /// lookPoint = 镜头看向的画布点，zoom = 缩放倍率。
    /// 取景框 = 以 lookPoint 为中心、半宽高 = canvasHalf / zoom 的矩形
    /// （zoom 越大框越小，这正是「推近」）。
    ///
    /// ★ 钳制公式必须与 <see cref="VNCamera.ComputeOffset"/> 同构：那边把容器偏移
    ///   钳在 ±((half+overscan)·zoom − half) 内，换算回 lookPoint 就是 ±(max/zoom)。
    ///   两边不一致的症状是「UI 上的取景框到了边，画面却没到」。
    /// </summary>
    public static class VNSecretPhotoRig
    {
        /// <summary>lookPoint 允许的最大绝对值（防露边）</summary>
        public static Vector2 LookLimit(float zoom, Vector2 canvasHalf, Vector2 overscan)
        {
            zoom = Mathf.Max(0.1f, zoom);
            var max = (canvasHalf + overscan) * zoom - canvasHalf;
            max = Vector2.Max(max, Vector2.zero);
            return max / zoom;
        }

        public static Vector2 ClampLook(Vector2 look, float zoom, Vector2 canvasHalf, Vector2 overscan)
        {
            var lim = LookLimit(zoom, canvasHalf, overscan);
            return new Vector2(Mathf.Clamp(look.x, -lim.x, lim.x),
                               Mathf.Clamp(look.y, -lim.y, lim.y));
        }

        /// <summary>取景框（画布坐标）</summary>
        public static Rect Frame(Vector2 look, float zoom, Vector2 canvasHalf)
        {
            var half = canvasHalf / Mathf.Max(0.1f, zoom);
            return new Rect(look - half, half * 2f);
        }

        /// <summary>
        /// 她在取景框里的权重 0~1：
        ///   = 可见比例（她的矩形与取景框的交集 / 她的矩形面积）
        ///   × 居中度（她的中心离框中心多近；edgeWeight ~ 1 线性）
        /// 完全不在框里 → 0（察觉度不涨，这是「只看累计量」版本唯一的躲避手段）。
        /// </summary>
        public static float TargetWeight(Rect frame, Rect character, float edgeWeight)
        {
            if (character.width <= 0.001f || character.height <= 0.001f) return 0f;

            float ix = Mathf.Min(frame.xMax, character.xMax) - Mathf.Max(frame.xMin, character.xMin);
            float iy = Mathf.Min(frame.yMax, character.yMax) - Mathf.Max(frame.yMin, character.yMin);
            if (ix <= 0f || iy <= 0f) return 0f;
            float visible = Mathf.Clamp01(ix * iy / (character.width * character.height));

            // 居中度：中心偏移量按取景框半尺寸归一化，0 = 正中，1 = 贴边
            Vector2 d = character.center - frame.center;
            float nx = Mathf.Abs(d.x) / Mathf.Max(1f, frame.width * 0.5f);
            float ny = Mathf.Abs(d.y) / Mathf.Max(1f, frame.height * 0.5f);
            float off = Mathf.Clamp01(Mathf.Max(nx, ny));
            float centering = Mathf.Lerp(1f, Mathf.Clamp01(edgeWeight), off);

            return visible * centering;
        }

        /// <summary>缩放带来的涨速倍率：zoomMin → 1，zoomMax → factorAtMax，线性</summary>
        public static float ZoomFactor(float zoom, float zoomMin, float zoomMax, float factorAtMax)
        {
            if (zoomMax - zoomMin <= 0.0001f) return 1f;
            float t = Mathf.Clamp01((zoom - zoomMin) / (zoomMax - zoomMin));
            return Mathf.Lerp(1f, Mathf.Max(1f, factorAtMax), t);
        }

        /// <summary>
        /// 察觉度每秒涨幅（百分比）。
        /// = 基础 × 框内权重 × 缩放倍率 × (1 + 警惕/100)
        /// 宽松档默认：基础 5、最大缩放 ×3 → 居中 1.0x 约 20 秒、贴脸 1.6x 约 7 秒。
        /// </summary>
        public static float DetectionRate(float baseRate, float weight, float zoomFactor, int alertPercent)
        {
            float alert = 1f + Mathf.Max(0, alertPercent) / 100f;
            return Mathf.Max(0f, baseRate) * Mathf.Clamp01(weight) * zoomFactor * alert;
        }

        /// <summary>取景框中心最近的那个角色（多人同场时的锁定规则）</summary>
        public static int PickNearest(Vector2 look, System.Collections.Generic.IList<Vector2> centers)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < centers.Count; i++)
            {
                float d = (centers[i] - look).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
