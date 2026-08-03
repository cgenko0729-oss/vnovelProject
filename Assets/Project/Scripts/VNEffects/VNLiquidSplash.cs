using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 液体喷溅（舞台层）：从画面上某一点喷出的水柱与飞散的水珠。
    /// 和 <see cref="VNWetScreen"/> 是同一个效果的两层——这里管"空中飞的"，
    /// 那里管"溅到镜头玻璃上挂着的"，两层缺一个都不成立：
    /// 只有这层是"有水在飞"，只有那层是"水凭空出现在屏幕上"。
    ///
    /// 【三个发射器，各司其职】
    ///   Body     拉伸公告板的主水珠——水感的一大半来自 StretchedBillboard，
    ///            球形粒子无论怎么调都像泡泡不像水。走 VN/ParticleAlpha（实体，遮挡背景）。
    ///   Glow     叠在主体上的高光点，走 VN/Additive + HDR 吃 Bloom。
    ///            水既要遮挡又要反光，单一混合模式表达不了，所以拆两层同时发射。
    ///   Splinter 低速碎珠，主喷射的"渣"。没有它整发喷溅会干净得很假。
    ///
    /// 【HDR 怎么给】
    /// 粒子 startColor 走顶点色会被钳到 1。所以 Glow 的材质固定一个白色 HDR 增益，
    /// 由粒子 startColor 只负责色相与相对亮度（0~1）——这样四种液体共用一份材质
    /// 也不会串色，切换液体时也不会让还在飞的粒子突然变颜色。
    ///
    /// 【命中屏幕是"配额"不是"物理"】
    /// 不做真的飞行碰撞判定：VN 是演出驱动，剧本需要"这一发一定要溅到镜头上"的确定性。
    /// 每发喷射按 screen: 概率掷出几个命中名额，各自延迟一段飞行时间后通知 VNWetScreen。
    ///
    /// 挂在场外空物体上（和其它粒子一样靠 sortingOrder 排序，不进 Canvas 层级）。
    /// </summary>
    public class VNLiquidSplash : MonoBehaviour
    {
        [Header("渲染排序（粒子区 10~31，这里压在最上层但仍低于对话框 40）")]
        public int sortingOrder = 28;

        [Header("溅到镜头上的水渍层（留空则只有空中水珠，没有屏幕水渍）")]
        public VNWetScreen wetScreen;

        [Header("可选：预制的 VN/ParticleAlpha 材质资产；留空则运行时创建")]
        [SerializeField] Material alphaSourceMaterial;

        [Header("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material additiveSourceMaterial;

        [Header("Glow 材质的固定 HDR 增益（预设的 glowBoost 会按它归一化）")]
        public float glowHdrCeiling = 2.4f;

        // ---- 点击喷水模式（剧本 liquid click on 开启）----
        [Header("点击喷水模式：开启时左键点哪喷哪")]
        public bool clickMode;

        ParticleSystem _body, _glow, _splinter;
        ParticleSystemRenderer _bodyRenderer;
        Material _bodyMat, _glowMat, _splinterMat;
        Camera _cam;

        VNLiquidPreset _clickPreset;
        float _clickPower = 1f;
        float _clickScreen = 1f;

        // ---- 间歇喷射（噗——噗——）----
        bool _spraying;
        VNLiquidPreset _sprayPreset;
        Vector2 _sprayPos01;
        float _sprayPower = 1f, _sprayDir = float.NaN, _spraySpread = 26f;
        float _sprayRate = 1f, _sprayScreen = 1f;
        float _sprayTimer;

        VNLiquidPreset _lastEmitPreset;

        /// <summary>等待"飞到镜头"的命中名额</summary>
        struct PendingHit
        {
            public float at;             // Time.time 到点后落到屏幕上
            public Vector2 pos01;
            public VNLiquidPreset preset;
            public int cluster;          // 这一下溅开几颗
        }
        readonly List<PendingHit> _pending = new List<PendingHit>();

        /// <summary>是否正在间歇喷射（存档用）</summary>
        public bool IsSpraying => _spraying;
        public VNLiquidType SprayType => _sprayPreset != null ? _sprayPreset.type : VNLiquidType.Water;
        public Vector2 SprayPos => _sprayPos01;
        public float SprayPower => _sprayPower;
        public float SprayDir => _sprayDir;
        public float SpraySpread => _spraySpread;
        public float SprayRate => _sprayRate;
        public float SprayScreen => _sprayScreen;

        /// <summary>点击喷水模式是否开启（存档用）</summary>
        public bool IsClickMode => clickMode;
        public VNLiquidType ClickType => _clickPreset != null ? _clickPreset.type : VNLiquidType.Water;
        public float ClickPower => _clickPower;
        public float ClickScreen => _clickScreen;

        void Awake()
        {
            _cam = Camera.main;
            Build();
        }

        void OnDestroy()
        {
            if (_bodyMat != null) Destroy(_bodyMat);
            if (_glowMat != null) Destroy(_glowMat);
            if (_splinterMat != null) Destroy(_splinterMat);
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        void Build()
        {
            if (_body != null) return;

            _body = CreateSystem("Body", out _bodyRenderer);
            _glow = CreateSystem("Glow", out var glowRenderer);
            _splinter = CreateSystem("Splinter", out var splinterRenderer);

            // 主体：拉伸公告板 + 实体混合
            _bodyRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            _bodyRenderer.lengthScale = 1f;
            _bodyMat = CreateMaterial("VN/ParticleAlpha", alphaSourceMaterial,
                VNProceduralTextures.LiquidBlob);
            _bodyRenderer.material = _bodyMat;

            // 高光：加法混合 + 固定 HDR 增益（见类注释）
            glowRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _glowMat = CreateMaterial("VN/Additive", additiveSourceMaterial,
                VNProceduralTextures.SoftCircle);
            if (_glowMat != null)
                _glowMat.SetColor("_TintColor",
                    new Color(glowHdrCeiling, glowHdrCeiling, glowHdrCeiling, 1f));
            glowRenderer.material = _glowMat;

            // 碎珠：小圆点，实体混合
            splinterRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _splinterMat = CreateMaterial("VN/ParticleAlpha", alphaSourceMaterial,
                VNProceduralTextures.LiquidSplinter);
            splinterRenderer.material = _splinterMat;
        }

        ParticleSystem CreateSystem(string name, out ParticleSystemRenderer renderer)
        {
            var go = new GameObject($"VN_Liquid_{name}", typeof(ParticleSystem));
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 粒子模块是 struct，必须先取出局部变量再改（直接 ps.main.xxx = 编译不过）
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;      // 速度由 EmitParams 逐颗给
            main.startSize = 0.2f;
            main.startLifetime = 1f;
            main.maxParticles = 900;
            main.gravityModifier = 0.55f;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;      // 全部手动 Emit

            var shape = ps.shape;
            shape.enabled = false;

            // 空气阻力：黏液靠它"挂"在空中，清水几乎无感
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = 999f;          // 不限速，只要阻尼
            lim.drag = 0f;
            lim.multiplyDragByParticleSize = true;
            lim.multiplyDragByParticleVelocity = true;

            // 收尾淡出：水珠是越飞越碎越淡，不该"啪"地消失
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            // sizeOverLifetime / velocityOverLifetime 由 ApplyMode 按喷射模式设置：
            // 侧喷要越飞越小（蒸发），朝镜头要越飞越大（逼近）——曲线正好相反。
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = vel.y = vel.z = new ParticleSystem.MinMaxCurve(0f);

            renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sortingOrder = sortingOrder;

            ps.Play();
            return ps;
        }

        // ------------------------------------------------------------------
        // 喷射模式：侧喷 vs 朝镜头
        // ------------------------------------------------------------------

        /// <summary>
        /// 相机是**正交**的（`orthographic = true`），所以真给粒子一个 z 速度不会有
        /// 任何近大远小——正交投影下远近一样大。「朝镜头扑面而来」只能做伪透视：
        ///   · 从喷射点向四周放射（朝你飞来的东西在屏幕上就是从一点向外扩散）
        ///   · 径向速度取平方分布 → 多数粒子几乎不动只变大（正对着你飞），
        ///     少数快速向外掠过（擦着镜头过去）
        ///   · 边飞边加速、边飞边放大（越近越快越大）
        /// 拉伸公告板沿速度方向拉伸这件事在这里刚好白送：中心那些慢粒子几乎是圆点，
        /// 外围快的被拉成放射状短线——正是正对镜头的雨该有的样子。
        /// </summary>
        bool _modeTowardCamera = true;
        bool _modeApplied;

        void ApplyMode(bool towardCamera)
        {
            if (_modeApplied && _modeTowardCamera == towardCamera) return;
            _modeApplied = true;
            _modeTowardCamera = towardCamera;

            // 逼近曲线：先慢后快，模拟透视下越近位移越大
            var speedCurve = towardCamera
                ? new AnimationCurve(
                    new Keyframe(0f, 0.55f, 0f, 0.6f),
                    new Keyframe(0.6f, 1.5f, 2.4f, 2.4f),
                    new Keyframe(1f, 3.2f, 4f, 0f))
                : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));

            // 尺寸曲线：朝镜头是越来越大（逼近），侧喷是越飞越小（蒸发/拉断）
            var sizeCurve = towardCamera
                ? new AnimationCurve(
                    new Keyframe(0f, 0.42f, 0.6f, 0.6f),
                    new Keyframe(0.65f, 1f, 1.4f, 1.4f),
                    new Keyframe(1f, 2.1f, 3.2f, 0f))
                : new AnimationCurve(
                    new Keyframe(0f, 1f, 0f, 0f),
                    new Keyframe(0.7f, 0.85f, -0.3f, -0.3f),
                    new Keyframe(1f, 0.45f, -1.2f, 0f));

            foreach (var ps in new[] { _body, _glow, _splinter })
            {
                if (ps == null) continue;
                var sol = ps.sizeOverLifetime;
                sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
                var vel = ps.velocityOverLifetime;
                vel.speedModifier = new ParticleSystem.MinMaxCurve(1f, speedCurve);
            }
        }

        Material CreateMaterial(string shaderName, Material source, Texture2D texture)
        {
            Material mat;
            if (source != null && source.shader != null && source.shader.name == shaderName)
            {
                mat = new Material(source);
            }
            else
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError($"[VNEffects] 找不到 Shader \"{shaderName}\"。", this);
                    return null;
                }
                mat = new Material(shader);
            }
            mat.hideFlags = HideFlags.DontSave;
            mat.mainTexture = texture;
            return mat;
        }

        // ------------------------------------------------------------------
        // 对外接口
        // ------------------------------------------------------------------

        /// <summary>
        /// 一次性大爆溅。
        /// </summary>
        /// <param name="pos01">喷射点，屏幕比例坐标 0~1</param>
        /// <param name="preset">液体预设</param>
        /// <param name="power">力度倍率：1 = 标准，2 = 爆开</param>
        /// <param name="dirDeg">喷射方向角（0 = 向右，90 = 向上）</param>
        /// <param name="spreadDeg">扇形张角的半角</param>
        /// <param name="screenScale">命中镜头概率的倍率，0 = 绝不溅到屏幕上</param>
        public void Burst(Vector2 pos01, VNLiquidPreset preset, float power = 1f,
            float dirDeg = float.NaN, float spreadDeg = 40f, float screenScale = 1f)
        {
            Build();
            if (preset == null) preset = VNLiquidPreset.Get(VNLiquidType.Water);
            power = Mathf.Clamp(power, 0.1f, 5f);

            ApplyMode(float.IsNaN(dirDeg));
            ApplyPreset(preset, float.IsNaN(dirDeg));

            int count = Mathf.RoundToInt(preset.burstCount * Mathf.Lerp(0.7f, 1.8f, Mathf.Min(power, 2f) / 2f));
            EmitCluster(pos01, preset, count, power, dirDeg, spreadDeg, 1f);

            int splinters = Mathf.RoundToInt(preset.splinterCount * Mathf.Clamp(power, 0.5f, 2f));
            EmitSplinters(pos01, preset, splinters, power, dirDeg, spreadDeg);

            ScheduleScreenHits(pos01, preset, count, screenScale, dirDeg, spreadDeg, power);
        }

        /// <summary>
        /// 开始间歇喷射（噗——噗——）。参数含义同 <see cref="Burst"/>，
        /// rate 是脉冲频率倍率：1 = 大约每 0.45 秒一下。
        /// </summary>
        public void StartSpray(Vector2 pos01, VNLiquidPreset preset, float power = 1f,
            float dirDeg = float.NaN, float spreadDeg = 26f, float rate = 1f, float screenScale = 1f)
        {
            Build();
            _sprayPreset = preset ?? VNLiquidPreset.Get(VNLiquidType.Water);
            _sprayPos01 = pos01;
            _sprayPower = Mathf.Clamp(power, 0.1f, 5f);
            _sprayDir = dirDeg;
            _spraySpread = spreadDeg;
            _sprayRate = Mathf.Clamp(rate <= 0f ? 1f : rate, 0.1f, 6f);
            _sprayScreen = Mathf.Max(0f, screenScale);
            _spraying = true;
            _sprayTimer = 0f; // 立刻来第一下，别让剧本等
        }

        /// <summary>停止间歇喷射（已经飞出去的水珠照常走完）</summary>
        public void StopSpray() => _spraying = false;

        /// <summary>点击喷水模式（剧本 liquid click on|off）</summary>
        public void SetClickMode(bool on, VNLiquidPreset preset = null,
            float power = 1f, float screenScale = 1f)
        {
            Build();
            clickMode = on;

            // 喷水模式接管左键，顺手让点击涟漪让位——
            // 一发水花上再叠一圈柔光星环，两种反馈会互相打架。
            if (_clickRipple == null) _clickRipple = FindFirstObjectByType<VNClickRipple>();
            if (_clickRipple != null) _clickRipple.enabled = !on;

            if (!on) return;
            _clickPreset = preset ?? VNLiquidPreset.Get(VNLiquidType.Water);
            _clickPower = Mathf.Clamp(power, 0.1f, 5f);
            _clickScreen = Mathf.Max(0f, screenScale);
        }

        VNClickRipple _clickRipple;

        /// <summary>全部停下并清干净（读档 / 清场 / 调试重建用）</summary>
        public void ClearInstant()
        {
            _spraying = false;
            SetClickMode(false); // 走 API 而非直接改字段，点击涟漪才会被放回去
            _pending.Clear();
            if (_body != null) _body.Clear(true);
            if (_glow != null) _glow.Clear(true);
            if (_splinter != null) _splinter.Clear(true);
        }

        // ------------------------------------------------------------------
        // 发射
        // ------------------------------------------------------------------

        /// <summary>
        /// 预设里那些"只能整组生效"的参数（重力/阻力/拉伸）在发射前同步到粒子系统。
        /// 拉伸走 <c>lengthScale</c>（固定倍率）而不是 <c>velocityScale</c>（随速度）：
        /// 后者会把初速高的粒子拉成面条，而现有的雨也是用 lengthScale 做的，
        /// 想"像下雨那样"就得用同一套。velocityScale 只留一点点，让快的略长一些。
        /// </summary>
        void ApplyPreset(VNLiquidPreset preset, bool towardCamera)
        {
            if (_lastEmitPreset == preset && _lastEmitToward == towardCamera) return;
            _lastEmitPreset = preset;
            _lastEmitToward = towardCamera;

            foreach (var ps in new[] { _body, _glow, _splinter })
            {
                if (ps == null) continue;
                var main = ps.main;
                // 朝镜头时几乎看不出重力：位移主要发生在观众看不见的深度方向上
                main.gravityModifier = preset.gravityScale * (towardCamera ? 0.16f : 0.55f);
                var lim = ps.limitVelocityOverLifetime;
                lim.drag = preset.drag;
            }

            if (_bodyRenderer != null)
            {
                // 朝镜头的水珠是正对着你来的，投影到屏幕上更短
                _bodyRenderer.lengthScale = preset.stretch * (towardCamera ? 0.62f : 1f);
                _bodyRenderer.velocityScale = 0.035f;
            }
        }

        bool _lastEmitToward = true;

        /// <summary>发射一簇主水珠 + 配套高光。dirDeg 为 NaN = 朝镜头扑面而来。</summary>
        void EmitCluster(Vector2 pos01, VNLiquidPreset p, int count, float power,
            float dirDeg, float spreadDeg, float speedMul)
        {
            bool toward = float.IsNaN(dirDeg);
            Vector3 origin = WorldPoint(pos01);
            Color bodyColor = p.tint;
            bodyColor.a = p.bodyAlpha;

            // 高光色：材质已经承担 HDR 增益，这里只给色相和相对亮度（0~1）
            Color glowColor = p.glowTint * Mathf.Clamp01(p.glowBoost / glowHdrCeiling);
            glowColor.a = 1f;

            for (int i = 0; i < count; i++)
            {
                Vector3 vel;
                if (toward)
                {
                    // 朝镜头：360° 放射 + 径向速度平方分布。
                    // 平方分布是关键——多数粒子几乎不动只变大（正对着你飞过来），
                    // 少数快速向外掠过（擦着镜头过去）。均匀分布会变成一个平面上的烟花。
                    float ang = Random.value * Mathf.PI * 2f;
                    float radial = Mathf.Pow(Random.value, 2.2f) *
                                   (spreadDeg / 40f) * 4.2f * p.speedScale * power * speedMul;
                    // 给个下限：速度为 0 时拉伸公告板的朝向未定义，会抖
                    radial = Mathf.Max(radial, 0.35f);
                    vel = new Vector3(Mathf.Cos(ang) * radial, Mathf.Sin(ang) * radial, 0f);
                }
                else
                {
                    float ang = (dirDeg + Random.Range(-spreadDeg, spreadDeg)) * Mathf.Deg2Rad;
                    // 速度平方分布：多数中速、少数飞得特别远，均匀分布会像喷头而不像爆开
                    float speed = p.speedScale * power * speedMul *
                                  Mathf.Lerp(2.6f, 7.4f, Mathf.Pow(Random.value, 1.6f));
                    vel = new Vector3(Mathf.Cos(ang) * speed, Mathf.Sin(ang) * speed, 0f);
                }

                float size = Random.Range(p.sizeMin, p.sizeMax) * Mathf.Lerp(1f, 1.3f, power / 3f);
                // 朝镜头的整个过程很短——水扑到脸上就那么一下
                float life = Random.Range(p.lifeMin, p.lifeMax) * (toward ? 0.62f : 1f);

                // 喷口附近抖一点，否则所有水珠从同一个点出发像烟花
                var jitter = (Vector3)(Random.insideUnitCircle * (toward ? 0.05f : 0.09f));

                _body.Emit(new ParticleSystem.EmitParams
                {
                    position = origin + jitter,
                    velocity = vel,
                    startSize = size,
                    startLifetime = life,
                    startColor = bodyColor,
                }, 1);

                if (Random.value < p.glowRatio)
                {
                    _glow.Emit(new ParticleSystem.EmitParams
                    {
                        position = origin + jitter,
                        velocity = vel,
                        startSize = size * 0.75f,
                        startLifetime = life * 0.85f,
                        startColor = glowColor,
                    }, 1);
                }
            }
        }

        /// <summary>发射低速碎珠：喷射根部炸开的"渣"，方向更散、速度更低、寿命更短</summary>
        void EmitSplinters(Vector2 pos01, VNLiquidPreset p, int count, float power,
            float dirDeg, float spreadDeg)
        {
            bool toward = float.IsNaN(dirDeg);
            Vector3 origin = WorldPoint(pos01);
            Color c = p.tint;
            c.a = p.bodyAlpha * 0.9f;

            for (int i = 0; i < count; i++)
            {
                float ang, speed;
                if (toward)
                {
                    // 朝镜头的碎珠散得更开：它们是被主水柱撞碎后甩到边上的
                    ang = Random.value * Mathf.PI * 2f;
                    speed = Mathf.Pow(Random.value, 1.5f) *
                            (spreadDeg / 40f) * 5.5f * p.speedScale * power;
                    speed = Mathf.Max(speed, 0.5f);
                }
                else
                {
                    // 碎珠散得比主喷射宽得多（1.9 倍张角），这是"炸开"的观感来源
                    ang = (dirDeg + Random.Range(-spreadDeg, spreadDeg) * 1.9f) * Mathf.Deg2Rad;
                    speed = p.speedScale * power * Random.Range(0.8f, 3.2f);
                }

                _splinter.Emit(new ParticleSystem.EmitParams
                {
                    position = origin + (Vector3)(Random.insideUnitCircle * 0.12f),
                    velocity = new Vector3(Mathf.Cos(ang) * speed, Mathf.Sin(ang) * speed, 0f),
                    startSize = Random.Range(p.sizeMin, p.sizeMax) * 0.55f,
                    startLifetime = Random.Range(p.lifeMin, p.lifeMax) * (toward ? 0.45f : 0.7f),
                    startColor = c,
                }, 1);
            }
        }

        /// <summary>
        /// 掷出这一发的"命中镜头"名额，各自排一个到点时间。
        /// 命中点从喷射点朝喷射方向外扩：水是朝那个方向飞过来的，
        /// 溅在镜头上的位置理应偏向那一侧，落在正中心反而奇怪。
        /// </summary>
        void ScheduleScreenHits(Vector2 pos01, VNLiquidPreset p, int emitted,
            float screenScale, float dirDeg, float spreadDeg, float power)
        {
            if (wetScreen == null || screenScale <= 0f) return;

            float chance = Mathf.Clamp01(p.screenChance * screenScale);
            // 名额从"这发有多少颗水珠"里抽，但压一下上限，免得一发糊满整屏
            int trials = Mathf.Clamp(Mathf.RoundToInt(emitted * 0.22f), 1, 9);
            for (int i = 0; i < trials; i++)
            {
                if (Random.value >= chance) continue;

                float ang, dist;
                if (float.IsNaN(dirDeg))
                {
                    // 朝镜头：命中点绕着喷射点铺开一圈，越靠近喷射点越密
                    // （正对着飞来的那些就落在原地附近）
                    ang = Random.value * Mathf.PI * 2f;
                    dist = Mathf.Pow(Random.value, 1.8f) * 0.26f * Mathf.Clamp(power, 0.6f, 2f);
                }
                else
                {
                    ang = (dirDeg + Random.Range(-spreadDeg, spreadDeg) * 1.3f) * Mathf.Deg2Rad;
                    dist = Random.Range(0.04f, 0.30f) * Mathf.Clamp(power, 0.6f, 2f);
                }
                var hit = pos01 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.75f) * dist;
                hit += Random.insideUnitCircle * 0.07f;

                _pending.Add(new PendingHit
                {
                    // 飞行时间：越远的落点等得越久，一发喷溅的水渍就会前后错开着"啪、啪、啪"
                    at = Time.time + Mathf.Lerp(0.08f, 0.42f, dist / 0.34f) * Random.Range(0.8f, 1.25f),
                    pos01 = hit,
                    preset = p,
                    // 水珠小了以后成簇比单颗更像"被溅到"，提高成簇概率与颗数
                    cluster = Random.value < 0.62f ? Random.Range(2, 7) : 1,
                });
            }
        }

        Vector3 WorldPoint(Vector2 pos01)
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return new Vector3((pos01.x - 0.5f) * 12f, (pos01.y - 0.5f) * 7f, -2f);

            float depth = _cam.orthographic ? Mathf.Abs(_cam.transform.position.z) - 2f : 8f;
            var world = _cam.ViewportToWorldPoint(new Vector3(pos01.x, pos01.y, depth));
            world.z = -2f; // 压到立绘之前，水要能挡住角色
            return world;
        }

        // ------------------------------------------------------------------
        // 每帧
        // ------------------------------------------------------------------

        void Update()
        {
            TickSpray();
            TickPendingHits();
            TickClick();
        }

        /// <summary>
        /// 间歇喷射的节奏：脉冲之间的间隔随机化，并且每隔几下来一次"大的"。
        /// 均匀喷射听起来省事，但看起来像洒水器——真实的水压是一顿一顿的。
        /// </summary>
        void TickSpray()
        {
            if (!_spraying) return;
            _sprayTimer -= Time.deltaTime;
            if (_sprayTimer > 0f) return;

            var p = _sprayPreset;
            ApplyMode(float.IsNaN(_sprayDir));
            ApplyPreset(p, float.IsNaN(_sprayDir));

            bool bigOne = Random.value < 0.22f;      // 偶尔一记大的，节奏才不呆板
            float pulsePower = _sprayPower * (bigOne ? Random.Range(1.35f, 1.8f)
                                                     : Random.Range(0.65f, 1.05f));

            int count = Mathf.RoundToInt(p.burstCount * 0.34f * (bigOne ? 1.7f : 1f));
            EmitCluster(_sprayPos01, p, count, pulsePower, _sprayDir, _spraySpread, 1f);

            if (bigOne)
                EmitSplinters(_sprayPos01, p, Mathf.RoundToInt(p.splinterCount * 0.5f),
                    pulsePower, _sprayDir, _spraySpread);

            // 大的那下之后停顿更久 —— 泄压
            float baseGap = (bigOne ? 0.72f : 0.42f) / _sprayRate;
            _sprayTimer = baseGap * Random.Range(0.65f, 1.45f);

            if (_sprayScreen > 0f)
                ScheduleScreenHits(_sprayPos01, p, count, _sprayScreen * (bigOne ? 1.3f : 0.7f),
                    _sprayDir, _spraySpread, pulsePower);
        }

        void TickPendingHits()
        {
            if (_pending.Count == 0 || wetScreen == null) return;
            float now = Time.time;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var h = _pending[i];
                if (now < h.at) continue;
                _pending.RemoveAt(i);
                if (h.cluster > 1) wetScreen.SplatBurst(h.pos01, h.preset, h.cluster);
                else wetScreen.Splat(h.pos01, h.preset);
            }
        }

        /// <summary>
        /// 点击喷水模式：点哪，水就从哪一点朝镜头冲过来。
        /// </summary>
        void TickClick()
        {
            if (!clickMode) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 screen = mouse.position.ReadValue();
            var pos01 = new Vector2(
                Mathf.Clamp01(screen.x / Mathf.Max(1f, Screen.width)),
                Mathf.Clamp01(screen.y / Mathf.Max(1f, Screen.height)));

            // 朝镜头喷：点哪，水就从哪儿冲着你来。
            // （想要旧的"从画面中心朝点击点侧喷"，把 float.NaN 换成算出来的角度即可。）
            Burst(pos01, _clickPreset ?? VNLiquidPreset.Get(VNLiquidType.Water),
                _clickPower, float.NaN, 42f, _clickScreen);
        }
    }
}
