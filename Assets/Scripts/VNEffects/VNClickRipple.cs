using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 点击涟漪：每次鼠标左键点击，在点击处绽放一圈扩散的柔光圆环 + 几颗星光。
    /// 圆环用单颗粒子实现（尺寸随生命周期放大、透明度衰减），星光复用
    /// PlaySparkleBurst。玩家全程都看得到的高频反馈。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class VNClickRipple : MonoBehaviour
    {
        [Tooltip("涟漪/星光颜色")]
        public Color tint = new Color(1f, 0.92f, 0.65f);

        [Tooltip("HDR 增益（>1 配合 Bloom 泛光）")]
        public float hdrBoost = 1.8f;

        [Tooltip("涟漪最大直径（世界单位）")]
        public float rippleSize = 1.3f;

        [Tooltip("每次点击附带的星光数量")]
        public int sparkleCount = 3;

        [Tooltip("渲染排序（高于 UI 粒子层）")]
        public int sortingOrder = 31;

        [Tooltip("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        ParticleSystem _ps;
        Material _mat;
        Camera _cam;

        void Awake()
        {
            _cam = Camera.main;
            _ps = GetComponent<ParticleSystem>();
            Configure();
        }

        void Configure()
        {
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 0.55f;
            main.startSize = rippleSize;
            main.maxParticles = 30;

            var em = _ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f; // 手动 Emit

            // 圆环从小放大
            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.12f, 0f, 4f),
                new Keyframe(1f, 1f, 0.6f, 0f)));

            // 透明度快速衰减
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.9f, 0f),
                    new GradientAlphaKey(0.5f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = sortingOrder;

            if (sourceMaterial != null && sourceMaterial.shader != null &&
                sourceMaterial.shader.name == "VN/Additive")
            {
                _mat = new Material(sourceMaterial);
            }
            else
            {
                var shader = Shader.Find("VN/Additive");
                if (shader == null)
                {
                    Debug.LogError("[VNEffects] 找不到 Shader \"VN/Additive\"。", this);
                    return;
                }
                _mat = new Material(shader);
            }
            _mat.hideFlags = HideFlags.DontSave;
            _mat.mainTexture = VNProceduralTextures.Ring;
            renderer.material = _mat;

            _ps.Play();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            Vector2 screen = mouse.position.ReadValue();
            Vector3 world = _cam.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, Mathf.Abs(_cam.transform.position.z) - 2f));
            world.z = -2f;

            var ep = new ParticleSystem.EmitParams
            {
                position = world,
                startColor = new Color(tint.r * hdrBoost, tint.g * hdrBoost, tint.b * hdrBoost, 1f),
            };
            _ps.Emit(ep, 1);

            if (sparkleCount > 0)
                VNAmbientParticles.PlaySparkleBurst(world, tint, sparkleCount, 1.1f, sortingOrder);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
