using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 调色来源通道。每个来源占一条，互不覆盖，由
    /// <see cref="VNImageEffectController.SetGrade"/> 合并后统一写进 shader。
    /// 加新通道时在 Count 之前追加即可，无需改合并逻辑。
    /// </summary>
    public enum VNGradeLayer
    {
        /// <summary>情绪色调 mood（VNMoodGrading 分层调色）</summary>
        Mood = 0,
        /// <summary>天气联动（VNWeatherController）</summary>
        Weather = 1,
        /// <summary>焦点压暗：说话者高亮、伪景深</summary>
        Focus = 2,
        /// <summary>临时演出：情绪动作沮丧、退场动画压暗</summary>
        Emote = 3,
        /// <summary>兜底通道：SetHSV / DOBrightness / DOSaturation 等直接调用</summary>
        Manual = 4,
        Count = 5,
    }

    /// <summary>
    /// 一份调色贡献。identity（无影响）= 白滤镜 + 色相 0 + 饱和/亮度/对比度 1。
    /// 与 VN/ImageEffect shader 的 Color Grading 区段一一对应。
    /// </summary>
    [System.Serializable]
    public struct VNGrade
    {
        public Color filter;      // RGB 乘法色滤镜
        public float hueShift;    // 色相偏移（加法，-0.5~0.5）
        public float saturation;  // 饱和度倍率
        public float brightness;  // 亮度倍率
        public float contrast;    // 对比度倍率（绕 0.5 中灰）

        public VNGrade(Color filter, float hueShift, float saturation,
            float brightness, float contrast)
        {
            this.filter = filter;
            this.hueShift = hueShift;
            this.saturation = saturation;
            this.brightness = brightness;
            this.contrast = contrast;
        }

        public static VNGrade Identity =>
            new VNGrade(Color.white, 0f, 1f, 1f, 1f);

        /// <summary>只压暗/降饱和的快捷构造（焦点压暗、天气联动用）</summary>
        public static VNGrade Dim(float brightness, float saturation) =>
            new VNGrade(Color.white, 0f, saturation, brightness, 1f);

        public static VNGrade[] NewLayerSet()
        {
            var set = new VNGrade[(int)VNGradeLayer.Count];
            for (int i = 0; i < set.Length; i++) set[i] = Identity;
            return set;
        }

        /// <summary>合并两份贡献：滤镜相乘、色相相加、其余相乘</summary>
        public static VNGrade Combine(VNGrade a, VNGrade b) => new VNGrade(
            new Color(a.filter.r * b.filter.r,
                      a.filter.g * b.filter.g,
                      a.filter.b * b.filter.b, 1f),
            a.hueShift + b.hueShift,
            a.saturation * b.saturation,
            a.brightness * b.brightness,
            a.contrast * b.contrast);

        public static VNGrade Lerp(VNGrade a, VNGrade b, float t) => new VNGrade(
            Color.Lerp(a.filter, b.filter, t),
            Mathf.Lerp(a.hueShift, b.hueShift, t),
            Mathf.Lerp(a.saturation, b.saturation, t),
            Mathf.Lerp(a.brightness, b.brightness, t),
            Mathf.Lerp(a.contrast, b.contrast, t));

        /// <summary>
        /// 按强度系数缩放这份贡献：0 = 完全无影响，1 = 原样。
        /// mood 的「背景全染、立绘轻染、UI 不染」就是靠它实现的。
        /// </summary>
        public VNGrade Scaled(float strength)
        {
            if (strength >= 0.999f) return this;
            return Lerp(Identity, this, Mathf.Clamp01(strength));
        }
    }
}
