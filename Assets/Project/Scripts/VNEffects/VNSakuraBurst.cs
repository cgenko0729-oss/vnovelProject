using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 樱吹雪爆发：告白名场面组合技。
    ///   花瓣以平时 10 倍的速率、被风横扫般涌过画面（约 3 秒）
    ///   + 心跳演出（画面脉动 + 粉色边缘泛光）自动开启并延后关闭。
    /// 纯组合现有件：Petals 粒子预设（爆发参数化）+ VNHeartbeat。
    /// 一行调用：sakura.Play();
    /// </summary>
    public class VNSakuraBurst : MonoBehaviour
    {
        [Header("可选：预制的 VN/Additive 材质资产")]
        public Material additiveMaterial;

        [Header("联动的心跳演出（可空）")]
        public VNHeartbeat heartbeat;

        [Header("花瓣颜色")]
        public Color petalColor = new Color(1f, 0.68f, 0.8f);

        VNAmbientParticles _petals;
        Sequence _seq;

        /// <summary>触发樱吹雪（burstSeconds 为爆发时长，之后花瓣自然飘落殆尽）</summary>
        public void Play(float burstSeconds = 3f, bool withHeartbeat = true)
        {
            EnsurePetals();
            _seq?.Kill();

            _petals.SetPlaying(true);
            if (withHeartbeat && heartbeat != null) heartbeat.StartBeat();

            _seq = DOTween.Sequence()
                .AppendInterval(burstSeconds)
                .AppendCallback(() => _petals.SetPlaying(false))
                .AppendInterval(2f)
                .AppendCallback(() =>
                {
                    if (withHeartbeat && heartbeat != null) heartbeat.StopBeat();
                })
                .SetLink(gameObject);
        }

        void EnsurePetals()
        {
            if (_petals != null) return;

            // 10 倍速率的花瓣系统，再调成"被风横扫"的暴风参数
            _petals = VNAmbientParticles.Create(
                VNAmbientParticles.Preset.Petals,
                petalColor, 13, additiveMaterial, 10f, transform);

            var ps = _petals.GetComponent<ParticleSystem>();

            // 生命周期缩短、风力大增 → 花瓣快速斜扫过整个画面
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.maxParticles = 500;

            var vel = ps.velocityOverLifetime;
            vel.x = new ParticleSystem.MinMaxCurve(-3.2f, -1.6f); // 强风向左
            vel.y = new ParticleSystem.MinMaxCurve(-1.6f, -0.7f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            // 生成带右移半个屏宽 + 加宽，保证强风下仍覆盖全屏
            var shape = ps.shape;
            var scale = shape.scale;
            shape.scale = new Vector3(scale.x * 1.6f, scale.y, scale.z);
            var pos = shape.position;
            shape.position = new Vector3(pos.x + scale.x * 0.3f, pos.y, pos.z);

            _petals.SetPlaying(false);
        }

        void OnDestroy()
        {
            _seq?.Kill();
        }
    }
}
