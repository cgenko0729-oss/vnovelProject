using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>无限滚动的平铺方式</summary>
    public enum VNScrollMode
    {
        Repeat, // 直接平铺：要求背景图左右（或上下）无缝，否则接缝处会穿帮
        Mirror, // 镜像平铺：每隔一张左右翻转，任何图都不会有接缝（代价是看得出对称）
    }

    /// <summary>
    /// 背景无限滚动：让一张背景图永远往一个方向流，营造"走路 / 坐车 / 云在飘"的持续动感。
    ///
    /// 【为什么滚 UV 而不是拼两张图】
    /// 拼图方案要复制第二个 Image + 材质实例 + mood 注册，还要改 bg 转场逻辑，
    /// 而且它和 VNKenBurns 抢同一个 RectTransform。滚 UV 只写材质属性，于是：
    /// 背景仍是一个 Image、bg 转场/camseq 运镜/视差全都不用动，
    /// **还能和 Ken Burns 叠加**（那个动 transform、这个动 UV，各走各的）。
    ///
    /// 【接缝】平铺在 shader 里自己折（见 VNImageEffect 的 vnWrapUV），不依赖纹理导入设置：
    /// Repeat 直接 frac，Mirror 走 ping-pong。**背景图不要开 Generate Mip Maps**——
    /// Sprite 默认就是关的，开了的话 frac 的跳变会让接缝糊掉一行。
    ///
    /// 挂在背景 Image 上（生成器自动挂；旧场景由 VNStage 自愈补挂）。
    /// 剧本 bgscroll on|off [speed:] [dir:] [mode:] [time:]，状态进存档。
    /// </summary>
    [RequireComponent(typeof(VNImageEffectController))]
    public class VNBackgroundScroll : MonoBehaviour
    {
        /// <summary>速度单位换算用的基准宽度（画布 1920）：speed 是"每秒滚过多少画布像素"</summary>
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        public const float DefaultSpeed = 80f;
        public const float DefaultDirection = 180f;   // 180 = 画面往左流（人物在往右走）
        public const float DefaultFade = 0.6f;

        [Header("速度（画布像素/秒；走路≈120，云飘≈6）")]
        public float speed = DefaultSpeed;

        [Header("方向（角度：0=往右流 90=往上 180=往左 270=往下）")]
        public float directionDeg = DefaultDirection;

        [Header("平铺方式")]
        public VNScrollMode mode = VNScrollMode.Mirror;

        [Header("开关时速度缓入缓出的秒数（0 = 立刻）")]
        public float fadeSeconds = DefaultFade;

        VNImageEffectController _fx;
        Vector2 _offset;      // 累计 UV 偏移
        float _current;       // 当前实际速度（缓入缓出用）
        float _target;        // 目标速度（关闭时为 0）
        bool _on;

        static readonly int IdScrollMode = Shader.PropertyToID("_ScrollMode");
        static readonly int IdScrollOffset = Shader.PropertyToID("_ScrollOffset");

        public bool IsScrolling => _on;
        public float Speed => speed;
        public float DirectionDeg => directionDeg;
        public VNScrollMode Mode => mode;

        void Awake()
        {
            _fx = GetComponent<VNImageEffectController>();
        }

        /// <summary>开始滚动。参数留 null 就沿用当前值（bgscroll on 不带参数 = 用上次的设定）</summary>
        public void StartScroll(float? newSpeed = null, float? newDirectionDeg = null,
            VNScrollMode? newMode = null, float? fade = null)
        {
            if (newSpeed.HasValue) speed = Mathf.Max(0f, newSpeed.Value);
            if (newDirectionDeg.HasValue) directionDeg = newDirectionDeg.Value;
            if (newMode.HasValue) mode = newMode.Value;
            if (fade.HasValue) fadeSeconds = Mathf.Max(0f, fade.Value);

            _on = true;
            _target = speed;
            if (fadeSeconds <= 0.001f) _current = _target;
            Apply();
        }

        /// <summary>停止滚动。缓到 0 之后才真正关掉 shader 分支（急停会看出画面一顿）</summary>
        public void StopScroll(float? fade = null)
        {
            if (fade.HasValue) fadeSeconds = Mathf.Max(0f, fade.Value);
            _on = false;
            _target = 0f;
            if (fadeSeconds <= 0.001f)
            {
                _current = 0f;
                Apply();
            }
        }

        /// <summary>读档/调试重建用：不缓入，直接就位</summary>
        public void RestoreState(bool on, float newSpeed, float newDirectionDeg, VNScrollMode newMode)
        {
            speed = Mathf.Max(0f, newSpeed);
            directionDeg = newDirectionDeg;
            mode = newMode;
            _on = on;
            _target = on ? speed : 0f;
            _current = _target;
            _offset = Vector2.zero;   // 偏移不进存档：从哪一帧接着滚，玩家看不出来
            Apply();
        }

        void Update()
        {
            // 速度缓入缓出：用线性逼近而不是 DOTween，因为目标速度可能中途被改
            if (!Mathf.Approximately(_current, _target))
            {
                if (fadeSeconds <= 0.001f)
                {
                    _current = _target;
                }
                else
                {
                    float step = speed <= 0.001f ? 1f : speed / fadeSeconds;
                    _current = Mathf.MoveTowards(_current, _target,
                        Mathf.Max(step, 1f) * Time.deltaTime);
                }
                if (!_on && Mathf.Approximately(_current, 0f)) Apply();  // 缓停到底才关分支
            }

            if (Mathf.Abs(_current) < 0.0001f) return;

            // 画布像素/秒 → UV/秒：一整张图横向铺满一屏，所以 1920px ≈ 1 个 uv
            float rad = directionDeg * Mathf.Deg2Rad;
            // 方向说的是「画面内容往哪边流」，采样坐标要反着走，所以取负
            _offset.x -= Mathf.Cos(rad) * _current / ReferenceWidth * Time.deltaTime;
            _offset.y -= Mathf.Sin(rad) * _current / ReferenceHeight * Time.deltaTime;

            // 折回 [0,2)：mirror 的周期是 2，repeat 是 1，取 2 两边都安全。
            // 不折的话跑上几十分钟 float 精度会掉光，画面开始抖
            _offset.x = Mathf.Repeat(_offset.x, 2f);
            _offset.y = Mathf.Repeat(_offset.y, 2f);
            Push();
        }

        void Apply()
        {
            if (_fx == null) _fx = GetComponent<VNImageEffectController>();
            if (_fx == null) return;
            bool active = _on || Mathf.Abs(_current) > 0.0001f;
            _fx.Mat.SetFloat(IdScrollMode, !active ? 0f : mode == VNScrollMode.Repeat ? 1f : 2f);
            Push();
        }

        void Push()
        {
            if (_fx == null) return;
            _fx.Mat.SetVector(IdScrollOffset, new Vector4(_offset.x, _offset.y, 0f, 0f));
        }

        /// <summary>换背景图时调用：偏移归零，滚动状态本身保持（还在车上就该继续滚）</summary>
        public void ResetOffset()
        {
            _offset = Vector2.zero;
            Push();
        }

        /// <summary>方向词 → 角度；认不出返回 null</summary>
        public static float? ParseDirection(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            switch (token.Trim().ToLower())
            {
                case "right": case "右": return 0f;
                case "up": case "上": return 90f;
                case "left": case "左": return 180f;
                case "down": case "下": return 270f;
            }
            return float.TryParse(token, out float deg) ? deg : (float?)null;
        }

        /// <summary>平铺方式词 → 枚举；认不出返回 null</summary>
        public static VNScrollMode? ParseMode(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            switch (token.Trim().ToLower())
            {
                case "repeat": case "无缝": return VNScrollMode.Repeat;
                case "mirror": case "镜像": return VNScrollMode.Mirror;
            }
            return null;
        }

        void OnDisable()
        {
            // 组件被关掉时别把滚动状态留在材质上——那会让静止的背景停在半张图的位置
            if (_fx != null && _fx.HasMaterial) _fx.Mat.SetFloat(IdScrollMode, 0f);
        }
    }
}
