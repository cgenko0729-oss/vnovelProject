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
    /// 【C1 假折射】
    /// 不采样背景做真折射（Canvas 里的 shader 拿不到已渲染画面，要么 GrabPass 要么加相机）。
    /// 水滴的"玻璃感"全部烘进 VNProceduralTextures.WaterDrop 的 RGB 剖面里：
    /// 中心压暗 + 内亮环 + 外圈菲涅尔暗边，再叠一层 HDR 高光吃 Bloom。
    /// 代价是看不见水滴里倒立的背景，正常观看距离下几乎分辨不出。
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

        [Header("同时存在的水渍上限（超出时回收最老的）")]
        public int maxDrops = 48;

        [Header("水滴基准直径（1080p 画布下的像素，实际再乘液体的 dropScale 与随机尺寸）")]
        public float baseDropSize = 62f;

        [Header("常驻湿镜头（liquid wet on）的目标水滴数")]
        public int wetTargetDrops = 22;

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
            public float size;         // 直径（像素）
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
        public void Splat(Vector2 screen01, VNLiquidPreset preset, float sizeScale = 1f)
            => SplatInternal(screen01, preset, sizeScale);

        /// <summary>Splat 的内部版：返回刚溅上的那颗，供铺底时二次调整（Drop 是私有类型）</summary>
        Drop SplatInternal(Vector2 screen01, VNLiquidPreset preset, float sizeScale)
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

            // 尺寸分布：偏小的多、偏大的少（Pow 压向小端），大滴才是视觉重点
            float roll = Mathf.Pow(Random.value, 1.7f);
            d.size = baseDropSize * preset.dropScale * sizeScale *
                     Mathf.Lerp(0.42f, 1.35f, roll) * (canvasSize.y / 1080f);

            d.age = 0f;
            d.dry = preset.drySeconds * Random.Range(0.75f, 1.25f);
            d.cling = Random.Range(preset.clingMin, preset.clingMax);
            // 大滴撑不住自身重量，挂得更短——同一场雨里大滴先流下去是最容易被察觉的真实感
            d.cling *= Mathf.Lerp(1.35f, 0.45f, roll);
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
            int count = 3, float spread = 0.06f)
        {
            Splat(screen01, preset);
            for (int i = 1; i < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * spread;
                // 画布是横的，同样的比例偏移在纵向看起来更大，压一下
                offset.y *= 0.62f;
                Splat(screen01 + offset, preset, Random.Range(0.35f, 0.7f));
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
            int seed = Mathf.RoundToInt(wetTargetDrops * _wetAmount * 0.6f);
            for (int i = 0; i < seed; i++)
            {
                var d = SplatInternal(new Vector2(Random.value, Random.value), _wetPreset,
                    Random.Range(0.5f, 1f));
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
                    float sizeFactor = Mathf.Clamp(d.size / (baseDropSize * d.preset.dropScale),
                        0.35f, 1.5f);
                    float target = d.preset.dripSpeed * sizeFactor * (canvasSize.y / 1080f);
                    d.vel = Mathf.MoveTowards(d.vel, target, target * 2.2f * dt);

                    float dy = d.vel * dt;
                    d.pos.y -= dy;
                    d.streakLen += dy;

                    // 横向微飘：玻璃不是绝对垂直的，水路会歪。每滴独立相位，否则整屏同步摆。
                    d.pos.x += Mathf.Sin(d.age * 1.7f + d.wobblePhase) * dt * d.vel * 0.16f;

                    if (d.pos.y < -d.size)
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
            int target = Mathf.RoundToInt(wetTargetDrops * _wetAmount);
            int alive = 0;
            foreach (var d in _pool) if (d.active) alive++;
            if (alive >= target) return;

            _wetTimer -= dt;
            if (_wetTimer > 0f) return;
            // 缺得越多补得越快，但始终保持随机间隔，避免节拍器一样规律
            float deficit = Mathf.Clamp01((target - alive) / (float)Mathf.Max(1, target));
            _wetTimer = Mathf.Lerp(0.9f, 0.12f, deficit) * Random.Range(0.6f, 1.4f);
            Splat(new Vector2(Random.value, Random.Range(0.15f, 1f)), _wetPreset,
                Random.Range(0.5f, 1f));
        }

        /// <summary>把状态刷到 uGUI 上</summary>
        void Layout(Drop d, float dt)
        {
            float lifeT = Mathf.Clamp01(d.age / d.dry);

            // 撞击形变：前 0.16 秒从"横向拍扁"弹回圆形，这一下让水渍像是撞上来的
            float impact = Mathf.Clamp01(d.age / 0.16f);
            float squashX = Mathf.Lerp(1.55f, 1f, EaseOutBack(impact));
            float squashY = Mathf.Lerp(0.55f, 1f, EaseOutBack(impact));

            d.root.anchoredPosition = d.pos;
            d.root.sizeDelta = new Vector2(d.size * squashX, d.size * squashY);

            // 蒸发：后 45% 生命才开始变淡，前半程保持清晰
            float evap = 1f - Mathf.Pow(Mathf.Clamp01((lifeT - 0.55f) / 0.45f), 1.4f);
            float alpha = Mathf.Clamp01(impact) * Mathf.Clamp01(evap);

            var p = d.preset;
            var bodyColor = p.tint;
            bodyColor.a = p.bodyAlpha * alpha;
            d.body.color = bodyColor;

            // 颜色与 HDR 强度在材质上（见 ApplyVisual），顶点色这里只管淡入淡出
            d.spec.color = new Color(1f, 1f, 1f, alpha * 0.9f);

            // 水痕：长度封顶，太长会像挂面
            if (p.trailAlpha > 0f && d.streakLen > 1f)
            {
                float maxLen = d.size * 9f;
                float len = Mathf.Min(d.streakLen, maxLen);
                d.streakRt.sizeDelta = new Vector2(d.size * 0.42f, len);
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

            if (_pool.Count >= maxDrops)
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
            body.texture = VNProceduralTextures.WaterDrop;
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
            var type = d.preset.type;
            if (!_specMats.TryGetValue(type, out var mat) || mat == null)
            {
                mat = CreateSpecMaterial(d.preset);
                _specMats[type] = mat;
            }
            d.spec.material = mat;
            // 墨这类几乎不反光的液体直接关掉高光层，留着只会变成"发光的黑水"
            d.spec.enabled = d.preset.glowRatio > 0.08f;
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
