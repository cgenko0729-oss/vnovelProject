using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VNEffects
{
    /// <summary>
    /// 多层视差背景（2.5D Parallax）：各层随鼠标反向微移，越"近"的层移动越多，
    /// 画面立刻有纵深。作用于专门的层容器（LayerBack/Mid/Front），
    /// 不直接动背景/立绘本身，与悬浮、呼吸、情绪动作零冲突。
    /// </summary>
    public class VNParallax : MonoBehaviour
    {
        [System.Serializable]
        public class Layer
        {
            public RectTransform rect;
            [Tooltip("视差强度（像素）：越大越靠近观众")]
            public float strength = 10f;
            [HideInInspector] public Vector2 basePos;
        }

        [Tooltip("视差层（场景生成器自动填：远景/中景/近景）")]
        public List<Layer> layers = new List<Layer>();

        [Tooltip("跟随平滑度（越大跟得越紧）")]
        public float damping = 5f;

        bool _enabled = true;
        Vector2 _current;
        bool _cached;

        public bool IsEnabled => _enabled;

        void Start()
        {
            CacheBases();
        }

        void CacheBases()
        {
            if (_cached) return;
            foreach (var l in layers)
                if (l.rect != null) l.basePos = l.rect.anchoredPosition;
            _cached = true;
        }

        /// <summary>运行时添加视差层（如前景装饰）</summary>
        public void AddLayer(RectTransform rect, float strength)
        {
            if (rect == null) return;
            layers.Add(new Layer { rect = rect, strength = strength, basePos = rect.anchoredPosition });
        }

        public void SetEnabled(bool on) => _enabled = on;

        public void Toggle() => _enabled = !_enabled;

        void Update()
        {
            CacheBases();

            Vector2 target = Vector2.zero;
            var mouse = Mouse.current;
            if (_enabled && mouse != null)
            {
                Vector2 pos = mouse.position.ReadValue();
                // 归一化到 -1..1（屏幕中心为原点）
                target = new Vector2(
                    Mathf.Clamp(pos.x / Screen.width - 0.5f, -0.5f, 0.5f) * 2f,
                    Mathf.Clamp(pos.y / Screen.height - 0.5f, -0.5f, 0.5f) * 2f);
            }

            // 指数平滑，帧率无关
            _current = Vector2.Lerp(_current, target, 1f - Mathf.Exp(-damping * Time.deltaTime));

            foreach (var l in layers)
            {
                if (l.rect == null) continue;
                l.rect.anchoredPosition = l.basePos - _current * l.strength; // 反向移动
            }
        }
    }
}
