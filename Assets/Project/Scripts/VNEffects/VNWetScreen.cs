using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 溅在"镜头玻璃"上的水渍层。液体喷溅的第三层，也是最出彩的一层：
    /// 空中的水珠只是"有水在飞"，玻璃上挂着往下淌的那几滴才是"水溅到我脸上了"。
    ///
    /// 【为什么不是粒子】
    /// 水渍要挂住、要按各自的节奏开始下滑、要留一条越来越淡的痕、要慢慢干——
    /// 全是逐个体的状态机，ParticleSystem 的曲线模型表达不了。所以这里是一池
    /// uGUI 元素 + 手动模拟，几十个的量级对 uGUI 完全不构成压力。
    ///
    /// 【尺度：和空中的水珠同一个量级】
    /// 镜头上的水点必须和空中飞的水珠差不多大（约 4~8px 宽），大一圈就立刻变成肥皂泡。
    /// 换算：相机 orthographicSize=5 → 1 世界单位 = 108px，粒子直径 0.038~0.075 → 4~8px。
    /// 尺寸分**两档**而不是一条连续曲线：真实的镜头水渍是"一大片细点 + 零星几颗大的"，
    /// 连续分布只会得到一堆不上不下的中等水珠，那是最假的尺寸。
    /// 只有大滴（默认 15%）会挂住→下滑→拖水痕；小水点现实中根本不会流，
    /// 表面张力足够撑住它自己。
    ///
    /// 【形状：一律 WaterSpeck，不用带折射剖面的 WaterDrop】
    /// WaterDrop 那套"中心压暗 + 内亮环 + 菲涅尔暗边"是给几十像素的大水滴设计的，
    /// 缩到十几像素就退化成一个白圈——屏幕上是一串肥皂泡。大滴也不例外。
    /// 这个尺度的水只需要两条信息：**是一条细的**、**边比中间亮**，仅此而已。
    /// （WaterDrop 本身保留，将来做特写大水滴时还用得上。）
    ///
    /// 【朝向】
    /// 刚溅上时沿撞击方向拉长、圆头朝外（和空中那层的放射感对上）；
    /// 一旦开始往下流就慢慢转回竖直——重力接管之后，水的长轴只会是竖的。
    ///
    /// 【坐标系】
    /// 对外一律用 0~1 的屏幕比例坐标，内部换算成 Canvas 像素——
    /// 这样喷溅点、剧本参数、鼠标位置三方共用一套数，不必关心 CanvasScaler 缩放。
    ///
    /// 挂到 Canvas 下的空 RectTransform 上，Awake 自建覆盖层，零美术资源。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class VNWetScreen : MonoBehaviour
    {
        [Header("不盖对话框时的排序（26 shockwave < 30 < 35 黑边 < 40 对话框）")]
        public int sortingOrder = 30;

        [Header("盖住对话框时的排序（高于对话框 40 / 选项 45）")]
        public int coverSortingOrder = 50;

        [Header("水渍是否盖住对话框（剧本 liquid cover on|off 可切）")]
        public bool coverDialogue;

        // ------------------------------------------------------------------
        // 手感参数（常量，不是 Inspector 字段）
        //
        // ⚠ 这些**故意**不做成 public 序列化字段。踩过一次：字段一旦被存进场景，
        //   改代码里的默认值对已存在的实例完全无效——场景里躺着 baseDropSize: 62，
        //   代码改成 6 也没用，而运行时才计算的"拉长比"却生效了，
        //   结果是 62px 的水渍被拉成三百多像素的烟雾状长条，比不改还糟。
        //   参数本来就决定跟着代码走（不做 ScriptableObject，见 VNLiquidPreset 注释），
        //   那就不该有第二个真相来源。要调手感，改这里，改完 Play 立刻生效。
        // ------------------------------------------------------------------

        /// <summary>同时存在的水渍上限（超出时回收最老的）</summary>
        const int MaxDrops = 240;

        /// <summary>
        /// 水滴基准**宽度**（1080p 画布下的像素）。长度 = 宽度 × 拉长比。
        /// 5px 是照着空中喷射粒子的屏幕尺寸定的：相机 orthographicSize=5 →
        /// 1 世界单位 = 108px，粒子直径 0.038~0.075 世界单位 ≈ 4~8px。
        /// 镜头上的水点和空中的水珠本来就该是同一个尺度。
        /// </summary>
        const float BaseDropSize = 5f;

        /// <summary>大水滴的比例（只有这些会挂住、往下流、拖水痕；其余是不动的小水点）</summary>
        const float BigDropRatio = 0.12f;

        /// <summary>常驻湿镜头（liquid wet on）的目标水滴数</summary>
        const int WetTargetDrops = 130;

        [Header("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        /// <summary>一颗水渍的全部运行时状态</summary>
        class Drop
        {
            public RectTransform root;
            public RawImage body;      // 主体（假折射剖面，默认 UI 混合）
            public RawImage spec;      // 高光（VN/Additive + HDR，吃 Bloom）
            public RawImage streak;    // 下滑水痕
            public RectTransform streakRt;

            public bool active;
            public VNLiquidPreset preset;
            public Vector2 pos;        // Canvas 像素坐标（左下为原点）
            public float size;         // 宽度（像素）；长度 = size × elongation
            public float elongation;   // 拉长比：小水点更细长，大水滴更接近圆（表面张力）
            public float angle;        // 当前朝向（度，0 = 长轴竖直、圆头朝下）
            public bool big;           // 只有大滴会挂住→下滑→拖水痕
            public float age;          // 已存在秒数
            public float dry;          // 干涸总时长
            public float cling;        // 还要挂住多久才开始下滑
            public float vel;          // 当前下滑速度（像素/秒）
            public float streakLen;    // 已经拖出来的水痕长度
            public float wobblePhase;  // 横向微飘的相位（每滴独立，否则整屏同步摆）
            public float fade;         // 主动淡出进度（Dry() 用），0 = 不淡出
        }

        readonly List<Drop> _pool = new List<Drop>();
        readonly Dictionary<VNLiquidType, Material> _specMats =
            new Dictionary<VNLiquidType, Material>();

        RectTransform _rect;
        RectTransform _container;
        Canvas _canvas;

        // 常驻湿镜头
        bool _wetOn;
        VNLiquidPreset _wetPreset;
        float _wetAmount = 1f;
        float _wetTimer;

        /// <summary>当前是否处于常驻湿镜头状态（存档用）</summary>
        public bool IsWet => _wetOn;
        /// <summary>常驻湿镜头的液体类型（存档用）</summary>
        public VNLiquidType WetType => _wetPreset != null ? _wetPreset.type : VNLiquidType.Water;
        /// <summary>常驻湿镜头的浓度（存档用）</summary>
        public float WetAmount => _wetAmount;

        void Awake()
        {
            Build();
        }

        void Build()
        {
            if (_container != null) return;

            _rect = (RectTransform)transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null) _canvas = gameObject.AddComponent<Canvas>();
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = coverDialogue ? coverSortingOrder : sortingOrder;

            var go = new GameObject("WetScreenDrops", typeof(RectTransform));
            _container = (RectTransform)go.transform;
            _container.SetParent(transform, false);
            _container.anchorMin = Vector2.zero;
            _container.anchorMax = Vector2.one;
            _container.offsetMin = Vector2.zero;
            _container.offsetMax = Vector2.zero;
        }

        void OnDestroy()
        {
            foreach (var kv in _specMats)
                if (kv.Value != null) Destroy(kv.Value);
            _specMats.Clear();
        }

        /// <summary>水渍层是否盖住对话框（剧本 liquid cover on|off）</summary>
        public void SetCover(bool cover)
        {
            coverDialogue = cover;
            Build();
            if (_canvas != null)
                _canvas.sortingOrder = cover ? coverSortingOrder : sortingOrder;
        }

        // ------------------------------------------------------------------
        // 对外接口
        // ------------------------------------------------------------------

        /// <summary>
        /// 在屏幕比例坐标 (0~1) 处溅上一颗水渍。
        /// sizeScale 用来做同一发喷溅里的大小分布，主溅 1.0、余溅 0.5 左右。
        /// </summary>
        /// <param name="splashAngleDeg">
        /// 撞击方向（数学角，0 = 右、90 = 上）。水点会沿这个方向拉长、圆头朝外；
        /// float.NaN = 随机。开始下滑之后会慢慢转回竖直。
        /// </param>
        public void Splat(Vector2 screen01, VNLiquidPreset preset, float sizeScale = 1f,
            float splashAngleDeg = float.NaN)
            => SplatInternal(screen01, preset, sizeScale, splashAngleDeg);

        /// <summary>Splat 的内部版：返回刚溅上的那颗，供铺底时二次调整（Drop 是私有类型）</summary>
        Drop SplatInternal(Vector2 screen01, VNLiquidPreset preset, float sizeScale,
            float splashAngleDeg)
        {
            Build();
            if (preset == null) preset = VNLiquidPreset.Get(VNLiquidType.Water);

            var d = Rent();
            if (d == null) return null;

            Vector2 canvasSize = CanvasSize();
            d.preset = preset;
            d.pos = new Vector2(
                Mathf.Clamp(screen01.x, -0.02f, 1.02f) * canvasSize.x,
                Mathf.Clamp(screen01.y, -0.02f, 1.02f) * canvasSize.y);

            // 尺寸分两档而不是一条连续曲线：真实的镜头水渍就是"一大片细点 + 零星几颗大的"，
            // 连续分布会得到一堆不上不下的中等水珠，那是最假的尺寸。
            d.big = Random.value < BigDropRatio;
            float widthMul = d.big ? Random.Range(1.7f, 2.7f)
                                   : Mathf.Lerp(0.7f, 1.4f, Random.value);
            d.size = BaseDropSize * preset.dropScale * sizeScale * widthMul *
                     (canvasSize.y / 1080f);
            // 小水点细长（被甩上去抹开的），大水滴接近圆（表面张力把它收住）。
            // 上限压到 2.8：再长就不像水点像划痕了。
            d.elongation = d.big ? Random.Range(1.15f, 1.6f) : Random.Range(1.6f, 2.8f);

            // 圆头（贴图 -Y 端）朝撞击方向：贴图长轴默认 +Y，所以要 +90°
            d.angle = float.IsNaN(splashAngleDeg)
                ? Random.Range(0f, 360f)
                : splashAngleDeg + 90f;

            d.age = 0f;
            d.dry = preset.drySeconds * Random.Range(0.75f, 1.25f);
            if (d.big)
            {
                d.cling = Random.Range(preset.clingMin, preset.clingMax) * 0.6f;
            }
            else
            {
                // 小水点现实中根本不会流——表面张力足够撑住它自己。
                // cling 设到干涸之后，等于永远不进入下滑状态。
                d.cling = d.dry * 2f;
            }
            d.vel = 0f;
            d.streakLen = 0f;
            d.wobblePhase = Random.value * Mathf.PI * 2f;
            d.fade = 0f;
            d.active = true;

            ApplyVisual(d);
            d.root.gameObject.SetActive(true);
            Layout(d, 0f);
            return d;
        }

        /// <summary>
        /// 一次命中溅开一小簇：一颗主滴 + 几颗卫星滴。
        /// 单独一颗孤零零的水滴永远不像"被溅到"，成簇才像。
        /// </summary>
        public void SplatBurst(Vector2 screen01, VNLiquidPreset preset,
            int count = 8, float spread = 0.045f, float splashAngleDeg = float.NaN)
        {
            Splat(screen01, preset, 1f, splashAngleDeg);
            for (int i = 1; i < count; i++)
            {
                // 平方根分布：卫星滴挤在主滴附近，而不是均匀铺满整个圆
                float ang = Random.value * Mathf.PI * 2f;
                float r = Mathf.Sqrt(Random.value) * spread;
                var offset = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r * 0.62f);
                // 卫星滴各自沿"从主滴甩出去"的方向拉长——一簇水点是炸开的，不是平行的
                Splat(screen01 + offset, preset, Random.Range(0.5f, 0.9f),
                    ang * Mathf.Rad2Deg);
            }
        }

        /// <summary>常驻湿镜头开关（隔着车窗看雨那种）。amount 是浓度倍率。</summary>
        public void SetWet(bool on, VNLiquidPreset preset = null, float amount = 1f)
        {
            Build();
            _wetOn = on;
            if (!on) return;
            _wetPreset = preset ?? VNLiquidPreset.Get(VNLiquidType.Water);
            _wetAmount = Mathf.Clamp(amount <= 0f ? 1f : amount, 0.1f, 3f);
            _wetTimer = 0f;
            // 开场先铺一批，否则要等好几秒才看得出"镜头是湿的"
            int seed = Mathf.RoundToInt(WetTargetDrops * _wetAmount * 0.6f);
            for (int i = 0; i < seed; i++)
            {
                // 湿镜头不是"被溅到"，是水本来就在玻璃上：朝向该竖着，不是放射状
                var d = SplatInternal(new Vector2(Random.value, Random.value), _wetPreset,
                    Random.Range(0.6f, 1f), Random.Range(-110f, -70f));
                // 铺底的这批错开年龄，不然会整屏同时开始下滑、同时干掉
                if (d != null) d.age = Random.Range(0f, d.dry * 0.5f);
            }
        }

        /// <summary>擦干：停止补水滴，现存水渍加速淡出（剧本 liquid dry）</summary>
        public void Dry(float seconds = 0.8f)
        {
            _wetOn = false;
            foreach (var d in _pool)
                if (d.active && d.fade <= 0f)
                    d.fade = 1f / Mathf.Max(0.05f, seconds);
        }

        /// <summary>瞬间清空（读档 / 清场 / 调试重建用，不做任何动画）</summary>
        public void ClearInstant()
        {
            _wetOn = false;
            foreach (var d in _pool)
            {
                d.active = false;
                if (d.root != null) d.root.gameObject.SetActive(false);
            }
        }

        // ------------------------------------------------------------------
        // 模拟
        // ------------------------------------------------------------------

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector2 canvasSize = CanvasSize();

            if (_wetOn) TickWet(dt);

            for (int i = 0; i < _pool.Count; i++)
            {
                var d = _pool[i];
                if (!d.active) continue;

                d.age += dt;

                // 主动淡出（Dry）优先于自然干涸
                if (d.fade > 0f)
                {
                    d.dry = Mathf.Min(d.dry, d.age + 1f / d.fade);
                }

                if (d.age >= d.dry)
                {
                    d.active = false;
                    d.root.gameObject.SetActive(false);
                    continue;
                }

                if (d.age >= d.cling)
                {
                    // 下滑：加速到该尺寸对应的终速。大滴 = 重 = 快，白送的物理直觉。
                    float sizeFactor = Mathf.Clamp(d.size / (BaseDropSize * d.preset.dropScale),
                        0.35f, 2.5f);
                    float target = d.preset.dripSpeed * sizeFactor * (canvasSize.y / 1080f);
                    d.vel = Mathf.MoveTowards(d.vel, target, target * 2.2f * dt);

                    float dy = d.vel * dt;
                    d.pos.y -= dy;
                    d.streakLen += dy;

                    // 一旦开始往下流，朝向就从撞击方向慢慢转回竖直——
                    // 重力接管之后，水的长轴只会是竖的
                    d.angle = Mathf.LerpAngle(d.angle, 0f, 1f - Mathf.Exp(-3.5f * dt));

                    // 横向微飘：玻璃不是绝对垂直的，水路会歪。每滴独立相位，否则整屏同步摆。
                    d.pos.x += Mathf.Sin(d.age * 1.7f + d.wobblePhase) * dt * d.vel * 0.16f;

                    if (d.pos.y < -d.size * d.elongation)
                    {
                        d.active = false;
                        d.root.gameObject.SetActive(false);
                        continue;
                    }
                }

                Layout(d, dt);
            }
        }

        /// <summary>常驻湿镜头：维持目标数量，随机补新水滴</summary>
        void TickWet(float dt)
        {
            int target = Mathf.RoundToInt(WetTargetDrops * _wetAmount);
            int alive = 0;
            foreach (var d in _pool) if (d.active) alive++;
            if (alive >= target) return;

            _wetTimer -= dt;
            if (_wetTimer > 0f) return;
            // 缺得越多补得越快，但始终保持随机间隔，避免节拍器一样规律
            float deficit = Mathf.Clamp01((target - alive) / (float)Mathf.Max(1, target));
            _wetTimer = Mathf.Lerp(0.9f, 0.12f, deficit) * Random.Range(0.6f, 1.4f);
            Splat(new Vector2(Random.value, Random.Range(0.15f, 1f)), _wetPreset,
                Random.Range(0.6f, 1f), Random.Range(-110f, -70f));
        }

        /// <summary>把状态刷到 uGUI 上</summary>
        void Layout(Drop d, float dt)
        {
            float lifeT = Mathf.Clamp01(d.age / d.dry);

            // 撞击形变：前 0.16 秒从"垂直于撞击方向被拍扁"弹回本来的形状。
            // squash 作用在**局部**坐标上，所以 X = 宽、Y = 长，和旋转无关。
            float impact = Mathf.Clamp01(d.age / 0.16f);
            float squashX = Mathf.Lerp(1.35f, 1f, EaseOutBack(impact));
            float squashY = Mathf.Lerp(0.65f, 1f, EaseOutBack(impact));

            d.root.anchoredPosition = d.pos;
            d.root.sizeDelta = new Vector2(d.size * squashX,
                                           d.size * d.elongation * squashY);
            d.root.localRotation = Quaternion.Euler(0f, 0f, d.angle);

            // 蒸发：后 45% 生命才开始变淡，前半程保持清晰
            float evap = 1f - Mathf.Pow(Mathf.Clamp01((lifeT - 0.55f) / 0.45f), 1.4f);
            float alpha = Mathf.Clamp01(impact) * Mathf.Clamp01(evap);

            var p = d.preset;
            var bodyColor = p.tint;
            bodyColor.a = p.bodyAlpha * alpha;
            d.body.color = bodyColor;

            // 颜色与 HDR 强度在材质上（见 ApplyVisual），顶点色这里只管淡入淡出
            d.spec.color = new Color(1f, 1f, 1f, alpha * 0.5f);

            // 水痕：只有大滴会流，所以也只有大滴有痕。长度封顶，太长会像挂面。
            if (d.big && p.trailAlpha > 0f && d.streakLen > 1f)
            {
                float maxLen = d.size * 16f;
                float len = Mathf.Min(d.streakLen, maxLen);
                d.streakRt.sizeDelta = new Vector2(d.size * 0.55f, len);
                var sc = p.tint;
                sc.a = p.trailAlpha * alpha * 0.55f;
                d.streak.color = sc;
                if (!d.streak.gameObject.activeSelf) d.streak.gameObject.SetActive(true);
            }
            else if (d.streak.gameObject.activeSelf)
            {
                d.streak.gameObject.SetActive(false);
            }
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        Vector2 CanvasSize()
        {
            Build();
            var size = _rect.rect.size;
            if (size.x < 1f || size.y < 1f) return new Vector2(1920f, 1080f);
            return size;
        }

        // ------------------------------------------------------------------
        // 对象池
        // ------------------------------------------------------------------

        Drop Rent()
        {
            foreach (var d in _pool)
                if (!d.active) return d;

            if (_pool.Count >= MaxDrops)
            {
                // 池满：回收最老的一颗（age/dry 比例最大 = 最接近消失，牺牲它最不显眼）
                Drop oldest = null;
                float worst = -1f;
                foreach (var d in _pool)
                {
                    float t = d.dry > 0f ? d.age / d.dry : 1f;
                    if (t > worst) { worst = t; oldest = d; }
                }
                return oldest;
            }

            return CreateDrop();
        }

        Drop CreateDrop()
        {
            var root = new GameObject($"Drop{_pool.Count}", typeof(RectTransform));
            var rt = (RectTransform)root.transform;
            rt.SetParent(_container, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero; // 左下为原点，pos 直接是像素
            rt.pivot = new Vector2(0.5f, 0.5f);

            // 水痕挂在水滴之下（先建 = 先渲染 = 在底层），pivot 在底端向上长
            var streakGo = new GameObject("Streak",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var streakRt = (RectTransform)streakGo.transform;
            streakRt.SetParent(rt, false);
            streakRt.anchorMin = streakRt.anchorMax = new Vector2(0.5f, 0.5f);
            streakRt.pivot = new Vector2(0.5f, 0f);
            streakRt.anchoredPosition = Vector2.zero;
            var streak = streakGo.GetComponent<RawImage>();
            streak.texture = VNProceduralTextures.LiquidStreak;
            streak.raycastTarget = false;
            streakGo.SetActive(false);

            var bodyGo = new GameObject("Body",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var bodyRt = (RectTransform)bodyGo.transform;
            bodyRt.SetParent(rt, false);
            Stretch(bodyRt);
            var body = bodyGo.GetComponent<RawImage>();
            body.texture = VNProceduralTextures.WaterSpeck; // 初值，ApplyVisual 按大/小滴覆盖
            body.raycastTarget = false;

            var specGo = new GameObject("Spec",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var specRt = (RectTransform)specGo.transform;
            specRt.SetParent(rt, false);
            Stretch(specRt);
            var spec = specGo.GetComponent<RawImage>();
            spec.texture = VNProceduralTextures.DropSpec;
            spec.raycastTarget = false;

            var drop = new Drop
            {
                root = rt,
                body = body,
                spec = spec,
                streak = streak,
                streakRt = streakRt,
            };
            root.SetActive(false);
            _pool.Add(drop);
            return drop;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 高光层材质：每种液体一份。
        /// 分液体建材质不只是为了避免同屏血/水串色，更是因为 HDR 增益只能挂在材质上——
        /// RawImage.color 是顶点色，>1 的分量会被钳掉，挂在那里等于没有 Bloom。
        /// 所以材质 _TintColor 承载"什么颜色、多亮"，顶点色只承载"淡入淡出到几成"。
        /// </summary>
        void ApplyVisual(Drop d)
        {
            // 一律用 WaterSpeck。曾经想让大滴用带折射剖面的 WaterDrop，
            // 但那套"中心暗 + 亮环 + 暗边"在**任何**只有十几像素的尺寸下都会退化成
            // 一个白圈——大滴也不例外，屏幕上就是一串肥皂泡。
            // 镜头上的水在这个尺度只需要：是一条细的、边比中间亮。
            d.body.texture = VNProceduralTextures.WaterSpeck;

            var type = d.preset.type;
            if (!_specMats.TryGetValue(type, out var mat) || mat == null)
            {
                mat = CreateSpecMaterial(d.preset);
                _specMats[type] = mat;
            }
            d.spec.material = mat;
            // 高光只给大滴：墨这类几乎不反光的液体本来就不该有（会变成"发光的黑水"），
            // 而 5px 的小水点上一个高光块会把它重新变回泡泡。
            d.spec.enabled = d.big && d.preset.glowRatio > 0.08f;
        }

        Material CreateSpecMaterial(VNLiquidPreset preset)
        {
            Material mat;
            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Additive")
            {
                mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Additive");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Additive\"。", this);
                    return null;
                }
                mat = new Material(shader);
            }
            mat.hideFlags = HideFlags.DontSave;
            var hdr = preset.glowTint * preset.glowBoost;
            hdr.a = 1f;
            mat.SetColor("_TintColor", hdr);
            return mat;
        }
    }
}
