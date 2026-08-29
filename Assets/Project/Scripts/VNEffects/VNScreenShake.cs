using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>震动等级</summary>
    public enum VNShakeLevel
    {
        Light,  // 轻震：心跳、紧张（6px / 0.25s）
        Medium, // 中震：惊吓、撞击（16px / 0.4s）
        Heavy,  // 强震：爆炸、冲击（34px / 0.6s + 旋转抖动）
    }

    /// <summary>
    /// 一次震动的完整参数。camseq 路径点的 <c>shake:</c> 参数、以及三档等级都归一到这里，
    /// 这样「等级 → 数值」只有一张表，运行时和编辑器预览（要算停顿时长）不会各写一份。
    /// </summary>
    public struct VNShakeSpec
    {
        public float strength;    // 位移幅度（像素）
        public float duration;    // 时长（秒）
        public int vibrato;       // 抖动次数
        public float rotationDeg; // >0 时叠加旋转抖动

        public bool Valid => strength > 0f && duration > 0f;

        /// <summary>三档等级的数值表（唯一一份）</summary>
        public static VNShakeSpec Of(VNShakeLevel level)
        {
            switch (level)
            {
                case VNShakeLevel.Light:
                    return new VNShakeSpec { strength = 6f, duration = 0.25f, vibrato = 18 };
                case VNShakeLevel.Heavy:
                    return new VNShakeSpec { strength = 34f, duration = 0.6f, vibrato = 26,
                                             rotationDeg = 1.4f };
                default:
                    return new VNShakeSpec { strength = 16f, duration = 0.4f, vibrato = 22 };
            }
        }

        /// <summary>
        /// 解析 <c>shake:</c> 的值：<c>light|medium|heavy</c> 三档别名，
        /// 或 <c>强度,秒数</c> 自定义（如 <c>20,0.5</c>）。认不出返回 false。
        /// 编辑器的路径点行也调它，两边判定必须一致。
        /// </summary>
        public static bool TryParse(string value, out VNShakeSpec spec)
        {
            spec = default;
            if (string.IsNullOrEmpty(value)) return false;

            switch (value.Trim().ToLower())
            {
                case "light": spec = Of(VNShakeLevel.Light); return true;
                case "medium": spec = Of(VNShakeLevel.Medium); return true;
                case "heavy": spec = Of(VNShakeLevel.Heavy); return true;
            }

            // 自定义：强度,秒数（两个都必须为正，否则宁可报错也不静默按默认震）
            var parts = value.Split(',');
            if (parts.Length != 2) return false;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float st)) return false;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float du)) return false;
            if (st <= 0f || du <= 0f) return false;

            spec = new VNShakeSpec { strength = st, duration = du, vibrato = 20 };
            return true;
        }

        /// <summary>格式化回剧本 token 的值部分（三档命中就写别名，否则写数值对）</summary>
        public string Format()
        {
            foreach (VNShakeLevel lv in System.Enum.GetValues(typeof(VNShakeLevel)))
            {
                var p = Of(lv);
                if (Mathf.Approximately(p.strength, strength) &&
                    Mathf.Approximately(p.duration, duration))
                    return lv.ToString().ToLower();
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:0.##},{1:0.##}", strength, duration);
        }
    }

    /// <summary>
    /// 因为 Canvas 是 Screen Space - Camera（震相机 UI 不会动），
    /// 所以震的是"SceneRoot"容器（背景+光束+立绘都在里面），
    /// 提示文字/对话框等 UI 保持稳定——这正是想要的效果。
    /// 每次震动前重置回基准位，连续触发不会漂移。
    /// </summary>
    public class VNScreenShake : MonoBehaviour
    {
        [Header("被震动的容器（场景生成器自动指向 SceneRoot）")]
        public RectTransform target;

        Vector2 _basePos;
        bool _cached;

        /// <summary>按等级震动</summary>
        public Tween Shake(VNShakeLevel level) => Shake(VNShakeSpec.Of(level));

        /// <summary>按参数震动（camseq 路径点的 shake: 走这条）</summary>
        public Tween Shake(VNShakeSpec spec) =>
            spec.Valid ? Shake(spec.strength, spec.duration, spec.vibrato, spec.rotationDeg) : null;

        /// <summary>自定义震动（strength：像素；rotationDeg &gt; 0 时叠加旋转抖动）</summary>
        public Tween Shake(float strength, float duration, int vibrato = 20, float rotationDeg = 0f)
        {
            if (target == null) return null;

            if (!_cached)
            {
                _basePos = target.anchoredPosition;
                _cached = true;
            }

            // 打断上一次震动并复位，防止基准漂移
            DOTween.Kill(this);
            target.anchoredPosition = _basePos;
            target.localRotation = Quaternion.identity;

            var t = target.DOShakeAnchorPos(duration, new Vector2(strength, strength * 0.7f),
                                            vibrato, 90f, false, true)
                          .SetTarget(this).SetLink(gameObject);
            if (rotationDeg > 0f)
            {
                target.DOShakeRotation(duration, new Vector3(0f, 0f, rotationDeg), vibrato, 90f, true)
                      .SetTarget(this).SetLink(gameObject);
            }
            return t;
        }

        void OnDestroy()
        {
            DOTween.Kill(this);
        }
    }
}
