using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 樱吹雪爆发：告白名场面组合技。
    ///   一阵强风把花瓣卷过整个画面（约 3 秒）+ 心跳演出（画面脉动 + 粉色边缘泛光）。
    ///
    /// 走 VNFoliageSystem，所以自动继承了实体 Alpha 混合、图集翻转、每片独立摆动、
    /// 三层景深这些底层改进；在此之上再叠三件专属强化：
    ///   1. 起手一记阵风冲击（Gust）+ 瞬间 Burst 一批花瓣 —— 「风来了」要有明确的起点
    ///   2. 近景层权重大幅拉高 —— 几片大而虚焦的花瓣贴着镜头横掠，是电影感的来源
    ///   3. 尾声阶段风力衰减 —— 花瓣由急掠转为缓飘，情绪才收得住
    /// 一行调用：sakura.Play();
    /// </summary>
    public class VNSakuraBurst : MonoBehaviour
    {
        [Header("可选：预制的 VN/ParticleAlpha 材质资产（花瓣用）")]
        public Material particleAlphaMaterial;

        [Header("可选：预制的 VN/Additive 材质资产（旧字段，保留兼容）")]
        public Material additiveMaterial;

        [Header("联动的心跳演出（可空）")]
        public VNHeartbeat heartbeat;

        [Header("花瓣主色（爆发用的色带以它为中心生成）")]
        public Color petalColor = new Color(1f, 0.68f, 0.8f);

        VNFoliageSystem _petals;
        VNWeatherDef _def;
        Sequence _seq;

        /// <summary>触发樱吹雪（burstSeconds 为强风时长，之后花瓣自然飘落殆尽）</summary>
        public void Play(float burstSeconds = 3f, bool withHeartbeat = true)
        {
            EnsurePetals();
            _seq?.Kill();

            _petals.SetPlaying(true);
            _petals.Gust(4.2f);      // 起手一记强阵风：风来了要有明确的起点
            _petals.Burst(90);       // 同时涌入一批，避免开头两秒画面还是空的
            if (withHeartbeat && heartbeat != null) heartbeat.StartBeat();

            _seq = DOTween.Sequence()
                // 强风期：中途再补两记阵风，风力才有起伏而不是一条直线
                .AppendInterval(burstSeconds * 0.35f)
                .AppendCallback(() => _petals.Gust(3.4f))
                .AppendInterval(burstSeconds * 0.35f)
                .AppendCallback(() => _petals.Gust(2.6f))
                .AppendInterval(burstSeconds * 0.30f)
                // 收势：风力衰减 → 花瓣由急掠转为缓飘，情绪收得住
                .AppendCallback(() =>
                {
                    if (_petals != null)
                        _petals.ApplyOverrides(0f, _def.windBase * 0.35f, 0f, 0f);
                })
                .AppendInterval(1.6f)
                .AppendCallback(() => _petals.SetPlaying(false))
                .AppendInterval(2f)
                .AppendCallback(() =>
                {
                    if (withHeartbeat && heartbeat != null) heartbeat.StopBeat();
                    // 参数复位，下次 Play 从满风力重新开始
                    if (_petals != null) _petals.ApplyOverrides(0f, float.NaN, 0f, 0f);
                })
                .SetLink(gameObject);
        }

        void EnsurePetals()
        {
            if (_petals != null) return;

            // 在内置落樱预设的基础上改成「暴风」参数
            _def = VNWeatherDef.CreateBuiltin(VNLeafShape.Sakura);
            _def.id = "sakuraburst";
            _def.colors = BuildPetalGradient(petalColor);
            _def.density = 70f;                              // 平时的 10 倍
            _def.fallSpeed = new Vector2(0.9f, 1.7f);
            _def.size = new Vector2(0.10f, 0.20f);
            _def.windBase = -2.6f;                           // 强风向左横扫
            _def.gustStrength = 2.4f;
            _def.gustFrequency = 0.35f;                      // 阵风更密
            _def.swayAmplitude = new Vector2(0.14f, 0.34f);
            _def.flipSpeed = new Vector2(0.35f, 0.85f);      // 被风卷着翻得更快
            _def.spinSpeed = new Vector2(-1.8f, 1.8f);

            // 近景层权重大幅拉高：几片大而虚焦的花瓣贴着镜头横掠 = 电影感
            _def.near.rateMul = 0.055f;
            _def.near.sizeMul = 3.2f;
            _def.near.speedMul = 2.1f;
            _def.near.alpha = 0.5f;
            _def.near.blur = 2.8f;
            _def.mid.alpha = 0.85f;
            _def.far.rateMul = 1.3f;

            _petals = VNFoliageSystem.Create(
                _def, particleAlphaMaterial, transform);
            _petals.SetPlaying(false);
        }

        /// <summary>由主色生成一条有微差的色带 —— 单色花瓣永远比有色差的一群假</summary>
        static Gradient BuildPetalGradient(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(h - 0.022f, 1f),
                        Mathf.Clamp01(s * 1.15f), Mathf.Clamp01(v * 0.94f)), 0f),
                    new GradientColorKey(c, 0.45f),
                    new GradientColorKey(Color.HSVToRGB(Mathf.Repeat(h + 0.018f, 1f),
                        Mathf.Clamp01(s * 0.55f), Mathf.Clamp01(v * 1.04f)), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        void OnDestroy()
        {
            _seq?.Kill();
            if (_def != null) Destroy(_def);
        }
    }
}
