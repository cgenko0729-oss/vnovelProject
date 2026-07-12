using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 鼠标轨迹星尘：鼠标移动时身后拖出细小的四芒星星尘，缓缓飘散消隐。
    /// 按移动距离手动发射（emission rate = 0，Emit() 逐颗生成），
    /// 世界空间模拟 → 星尘留在原地形成拖尾。梦幻系加分项。
    /// 开关：直接 enabled = false / Toggle()。
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class VNMouseStardust : MonoBehaviour
    {
        [Tooltip("星尘颜色")]
        public Color tint = new Color(1f, 0.9f, 0.55f);

        [Tooltip("HDR 增益，>1 时被 Bloom 泛光")]
        public float hdrBoost = 2f;

        [Tooltip("每移动 1 世界单位发射的星尘数")]
        public float emitPerUnit = 7f;

        [Tooltip("渲染排序（要高于 UI 与粒子，低于全屏转场）")]
        public int sortingOrder = 30;

        [Tooltip("可选：预制的 VN/Additive 材质资产；留空则运行时创建")]
        [SerializeField] Material sourceMaterial;

        ParticleSystem _ps;
        Material _mat;
        Camera _cam;
        Vector3 _lastPos;
        bool _hasLast;
        float _emitCarry; // 距离累加余数，保证低速移动也能均匀出粒子

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
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 星尘留在原地
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.12f; // 轻微下坠
            main.maxParticles = 400;

            var em = _ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f; // 全部手动 Emit

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sol = _ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

            var rol = _ps.rotationOverLifetime;
            rol.enabled = true;
            rol.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

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
            _mat.mainTexture = VNProceduralTextures.Sparkle;
            renderer.material = _mat;

            _ps.Play();
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (_cam == null)
            {
                _cam = Camera.main;
                if (_cam == null) return;
            }

            Vector2 screen = mouse.position.ReadValue();
            if (screen.x < 0f || screen.y < 0f ||
                screen.x > Screen.width || screen.y > Screen.height)
                return;

            Vector3 world = _cam.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, Mathf.Abs(_cam.transform.position.z) - 2f));
            world.z = -2f; // 在 Canvas 平面（z=0）之前

            if (!_hasLast)
            {
                _lastPos = world;
                _hasLast = true;
                return;
            }

            float dist = Vector3.Distance(world, _lastPos);
            _emitCarry += dist * emitPerUnit;
            int count = Mathf.FloorToInt(_emitCarry);
            if (count > 0)
            {
                _emitCarry -= count;
                count = Mathf.Min(count, 30); // 单帧上限，防瞬移狂喷
                Color c = new Color(tint.r * hdrBoost, tint.g * hdrBoost, tint.b * hdrBoost, 1f);
                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = Vector3.Lerp(_lastPos, world, (i + 1f) / count)
                                  + (Vector3)(Random.insideUnitCircle * 0.06f);
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = pos,
                        velocity = new Vector3(
                            Random.Range(-0.15f, 0.15f),
                            Random.Range(-0.05f, 0.2f), 0f),
                        startColor = c,
                    };
                    _ps.Emit(ep, 1);
                }
            }
            _lastPos = world;
        }

        void OnDisable()
        {
            _hasLast = false;
            _emitCarry = 0f;
        }

        public void Toggle() => enabled = !enabled;

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }
    }
}
