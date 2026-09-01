using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 擦雾玩法的掩码层：一张低分辨率的「哪里被擦干净了」缓冲。
    ///
    /// **纯逻辑，不继承 MonoBehaviour** —— 同 VNPhotoScore / VNTouchScore /
    /// VNBadmintonBallistics 的分层习惯：手感全靠调这里的数，而调数需要能脱离场景反复跑
    /// （编辑器调参窗口 VNFogTuneWindow 用的就是这个类，与运行时同一份代码）。
    ///
    /// 【为什么掩码可以这么小】
    /// 雾本身就是模糊的，掩码不需要高分辨率。384×216 双线性放大 5 倍后看不出来，
    /// 换来的是：每帧全图遍历（回雾）只有 8 万次运算、上传只有 81KB、
    /// 清晰度统计顺手在同一次遍历里求和。用全屏 1920×1080 的话这三项都要差 25 倍。
    ///
    /// 一开始定的是 256×144，实测擦痕边缘能看出方块阶梯（一个掩码像素 = 7.5 屏幕像素，
    /// 刚好落在肉眼分辨得出的尺度上）。384 把它压到 5 像素，配合 Stamp 里保住的羽化带
    /// 与 shader 的边缘噪声就看不出来了。再往上加是浪费。
    ///
    /// 【为什么内部是 float[] 而不是直接操作 byte[]】
    /// 回雾是每帧减一个极小的量（3%/秒 ÷ 60fps ≈ 0.0005），byte 精度下会被整数截断
    /// 完全吃掉——症状是「雾根本不回来」。多一份 147KB 内存换数值正确。
    ///
    /// 【坐标约定】
    /// - uv：局部归一化坐标，(0,0) 左下、(1,1) 右上，与 shader 里的 localUV 同一套
    /// - 半径一律传**掩码像素**。屏幕像素 → 掩码像素的换算由调用方做
    ///   （因为 384/1920 == 216/1080，两个方向系数相同，屏幕上的正圆在掩码里还是正圆。
    ///   改分辨率时务必保持 16:9，否则笔刷会被拉成椭圆）
    /// </summary>
    public class VNFogMask
    {
        public const int Width = 384;
        public const int Height = 216;
        const int Count = Width * Height;

        /// <summary>0 = 全是雾，1 = 完全擦净</summary>
        float[] _mask;
        byte[] _bytes;

        /// <summary>
        /// 每像素上次被哪一笔碰过 / 那一笔开始前它的值。
        /// 用来把「一笔之内的重复覆盖」与「笔与笔之间的累加」分开——见 Stamp 的注释。
        /// </summary>
        int[] _strokeMark;
        float[] _strokeBase;
        int _strokeId;

        /// <summary>边缘侵蚀权重：越靠画面边缘越接近 1，中心接近 0。只算一次。</summary>
        float[] _edgeWeight;
        /// <summary>权重图的平均值——把资产里的「%/秒」换算成实际衰减系数用</summary>
        float _edgeWeightAvg = 1f;

        Texture2D _tex;
        bool _dirty;

        Vector2 _lastPixel;
        bool _hasLast;

        /// <summary>当前整体清晰度 0~1（Flush 时更新）</summary>
        public float Clarity { get; private set; }

        /// <summary>雾层 shader 的 _MaskTex</summary>
        public Texture2D Texture => _tex;

        // ==================================================================
        // 生命周期
        // ==================================================================

        public void Build()
        {
            _mask = new float[Count];
            _bytes = new byte[Count];
            _edgeWeight = new float[Count];
            _strokeMark = new int[Count];
            _strokeBase = new float[Count];

            BuildEdgeWeight();

            _tex = new Texture2D(Width, Height, TextureFormat.R8, false)
            {
                name = "VNFogMask",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            Reset();
        }

        /// <summary>回到「整屏全是雾」</summary>
        public void Reset()
        {
            if (_mask == null) return;
            for (int i = 0; i < Count; i++) _mask[i] = 0f;
            Clarity = 0f;
            _hasLast = false;
            // 不用清 _strokeMark：递增笔号就让所有旧标记自然失效了
            _strokeId++;
            _dirty = true;
            Flush();
        }

        /// <summary>
        /// 释放掩码贴图。
        ///
        /// 必须分播放/编辑两条路：这个类**编辑器也在用**（调参窗口 VNFogTuneWindow
        /// 跑的就是同一份代码），而 <c>Object.Destroy</c> 在编辑模式下只会打一条
        /// 「may not be called from edit mode」的 error 并且**什么都不销毁**——
        /// 症状是每关一次调参窗口就泄漏一张贴图。
        /// </summary>
        public void Destroy()
        {
            if (_tex == null) return;
            if (Application.isPlaying) Object.Destroy(_tex);
            else Object.DestroyImmediate(_tex);
            _tex = null;
        }

        /// <summary>
        /// 每个像素到最近边缘的归一化距离 d（边缘 0、中心 1），权重 = 1 - smoothstep(0, 0.35, d)。
        /// 只在 Build 时算一次，之后每帧的边缘侵蚀就是一次乘法，零成本。
        /// </summary>
        void BuildEdgeWeight()
        {
            double sum = 0.0;
            for (int y = 0; y < Height; y++)
            {
                float ny = Mathf.Min(y, Height - 1 - y) / (Height * 0.5f);
                for (int x = 0; x < Width; x++)
                {
                    float nx = Mathf.Min(x, Width - 1 - x) / (Width * 0.5f);
                    float d = Mathf.Min(nx, ny);
                    float w = 1f - Mathf.SmoothStep(0f, 0.35f, d);
                    _edgeWeight[y * Width + x] = w;
                    sum += w;
                }
            }
            _edgeWeightAvg = Mathf.Max(0.0001f, (float)(sum / Count));
        }

        // ==================================================================
        // 擦
        // ==================================================================

        /// <summary>开一笔。笔与笔之间才会累加，见 Stamp。</summary>
        public void BeginStroke()
        {
            _hasLast = false;
            _strokeId++;
        }

        public void EndStroke() => _hasLast = false;

        /// <summary>
        /// 擦到 uv 处。鼠标一帧能跑很远，必须沿线段补点，
        /// 否则画出来是一串断掉的圆（VNPhotoDoodle.StrokeTo 的同款处理）。
        /// </summary>
        /// <param name="radius">笔刷半径（掩码像素）</param>
        /// <param name="feather">羽化带占半径的比例 0~1</param>
        /// <param name="strength">单次擦除强度 0~1</param>
        public void StrokeTo(Vector2 uv, float radius, float feather, float strength)
        {
            if (_mask == null) return;
            var p = new Vector2(uv.x * Width, uv.y * Height);

            if (_hasLast)
            {
                float dist = Vector2.Distance(_lastPixel, p);
                int steps = Mathf.CeilToInt(dist / Mathf.Max(0.75f, radius * 0.3f));
                steps = Mathf.Min(steps, 256);          // 极端瞬移时的保险丝
                for (int i = 1; i <= steps; i++)
                    Stamp(Vector2.Lerp(_lastPixel, p, i / (float)steps), radius, feather, strength, true);
            }
            else Stamp(p, radius, feather, strength, true);

            _lastPixel = p;
            _hasLast = true;
            _dirty = true;
        }

        /// <summary>反向：在 uv 处盖一团雾（「她哈了口气」/ 随机重新起雾）</summary>
        public void FogBlob(Vector2 uv, float radius, float amount)
        {
            if (_mask == null) return;
            Stamp(new Vector2(uv.x * Width, uv.y * Height), radius, 0.6f, amount, false);
            _dirty = true;
        }

        /// <param name="wipe">true = 擦净（掩码升），false = 起雾（掩码降）</param>
        void Stamp(Vector2 center, float radius, float feather, float strength, bool wipe)
        {
            radius = Mathf.Max(0.6f, radius);
            float band = Mathf.Max(0.5f, radius * Mathf.Clamp01(feather));

            int cx = Mathf.RoundToInt(center.x);
            int cy = Mathf.RoundToInt(center.y);
            int r = Mathf.CeilToInt(radius);

            int y0 = Mathf.Max(0, cy - r), y1 = Mathf.Min(Height - 1, cy + r);
            int x0 = Mathf.Max(0, cx - r), x1 = Mathf.Min(Width - 1, cx + r);

            for (int y = y0; y <= y1; y++)
            {
                float dy = y - center.y;
                int row = y * Width;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - center.x;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((radius - d) / band);
                    if (a <= 0f) continue;

                    int idx = row + x;

                    if (!wipe)
                    {
                        _mask[idx] = Mathf.Max(0f, _mask[idx] - a * strength);
                        continue;
                    }

                    // ★ 擦：一笔之内取 max，笔与笔之间才累加。
                    //
                    // 不能直接 `mask += a*strength` —— 沿线段补点时同一个像素会被
                    // 七八个 stamp 连着盖到，羽化带一路被累加填满，边缘退化成硬边，
                    // 放大后就是肉眼可见的方块阶梯，shader 的边缘噪声也没了发挥空间
                    // （没有过渡带可打碎）。
                    //
                    // 但也不能单纯取 max —— 那样 wipeStrength 就成了永久天花板，
                    // 「反复擦同一处会越来越透」这件事再也不会发生。
                    //
                    // 正解是记住「这一笔碰到该像素之前的值」，本笔的上限 = 那个值 + a*strength：
                    // 一笔内重复覆盖只取最大的 a（羽化保留），下一笔才在新的基线上继续加。
                    // 于是 wipeStrength 的语义变成诚实的「一笔能推进多少」——
                    // 填 1 就是一次划过全清（默认，对应「轻松」取向），
                    // 填 0.5 就是要来回擦两遍才透。
                    if (_strokeMark[idx] != _strokeId)
                    {
                        _strokeMark[idx] = _strokeId;
                        _strokeBase[idx] = _mask[idx];
                    }
                    float target = Mathf.Min(1f, _strokeBase[idx] + a * strength);
                    if (target > _mask[idx]) _mask[idx] = target;
                }
            }
        }

        // ==================================================================
        // 回雾
        // ==================================================================

        /// <summary>
        /// 边缘侵蚀：雾从画面四周往中间吞。
        /// </summary>
        /// <param name="ratePerSec">
        /// 每秒吞掉的**整体清晰度百分比**（资产里填 2 就是 2%/秒）。
        /// 内部按权重图的平均值反算实际系数，所以填多少就真的掉多少，
        /// 不会因为「权重图大部分地方接近 0」而实际只掉了零头。
        ///
        /// 注意这个「填多少掉多少」是**整屏已擦净时**的速率：侵蚀只能吃掉已经有值的像素，
        /// 玩家只擦了中间一条时，边缘没得吃，实测掉的会明显少于设定值（这正是
        /// 「边缘侵蚀」该有的行为）。调参窗口的预估按满速减，所以它给的秒数偏保守。
        /// </param>
        public void ErodeFromEdges(float ratePerSec, float dt)
        {
            if (_mask == null || ratePerSec <= 0f || dt <= 0f) return;
            float k = ratePerSec * 0.01f / _edgeWeightAvg * dt;
            if (k <= 0f) return;

            for (int i = 0; i < Count; i++)
            {
                float v = _mask[i];
                if (v <= 0f) continue;
                _mask[i] = Mathf.Max(0f, v - k * _edgeWeight[i]);
            }
            _dirty = true;
        }

        /// <summary>
        /// 一个随机雾团要多强，才能让「每秒 ratePerSec% 的清晰度」这句话成立。
        /// 团太小或间隔太长时会算出 &gt;1，钳到 1（此时实际速率达不到设定值，
        /// 属于参数本身的问题，不该让代码假装做到）。
        /// </summary>
        public static float BlobStrengthFor(float ratePerSec, float interval, float radius)
        {
            float area = Mathf.PI * radius * radius;
            if (area < 1f) return 1f;
            float need = ratePerSec * 0.01f * interval * Count / area;
            return Mathf.Clamp(need, 0.15f, 1f);
        }

        // ==================================================================
        // 上传
        // ==================================================================

        /// <summary>把改动上传到贴图并重算清晰度。不脏就什么都不做。</summary>
        public void Flush()
        {
            if (!_dirty || _tex == null) return;
            _dirty = false;

            double sum = 0.0;
            for (int i = 0; i < Count; i++)
            {
                float v = _mask[i];
                sum += v;
                _bytes[i] = (byte)(v * 255f + 0.5f);
            }
            Clarity = (float)(sum / Count);

            _tex.SetPixelData(_bytes, 0);
            _tex.Apply(false);
        }

        /// <summary>调参窗口用：不经贴图直接读一个点的值</summary>
        public float ValueAt(int x, int y)
        {
            if (_mask == null) return 0f;
            x = Mathf.Clamp(x, 0, Width - 1);
            y = Mathf.Clamp(y, 0, Height - 1);
            return _mask[y * Width + x];
        }
    }
}
