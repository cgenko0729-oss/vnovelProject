using UnityEngine;
using UnityEngine.UI;

namespace VNEffects
{
    /// <summary>
    /// 打字机文字（挂在 uGUI Text 上的顶点特效）：
    /// 逐字显现，每个字出现时从下方轻微上浮 + 淡入。
    /// 用 BaseMeshEffect 直接改 Text 网格顶点（每字一个四边形），
    /// 不依赖 TMP —— 走 LegacyRuntime 字体的系统回退，中文直接可用。
    /// </summary>
    [RequireComponent(typeof(Text))]
    public class VNTypewriterText : BaseMeshEffect
    {
        [Tooltip("每秒显示字数")]
        public float charsPerSecond = 18f;

        [Tooltip("每个字上浮的高度（像素）")]
        public float riseHeight = 10f;

        [Tooltip("每个字淡入动画跨越的字数（越大越绵柔）")]
        public float fadeSpanChars = 2.5f;

        /// <summary>整段文字显示完毕时回调</summary>
        public System.Action onComplete;

        Text _text;
        float _visible;      // 已显现的"字数进度"（小数）
        bool _playing;
        int _lastQuadCount;  // 上次网格里的四边形（字）数量

        public bool IsTyping => _playing;

        protected override void Awake()
        {
            base.Awake();
            _text = GetComponent<Text>();
        }

        /// <summary>开始打字机播放一段文字</summary>
        public void Play(string content)
        {
            if (_text == null) _text = GetComponent<Text>();
            _text.text = content;
            _visible = 0f;
            _playing = true;
            _lastQuadCount = 0;
            graphic.SetVerticesDirty();
        }

        /// <summary>立即显示全部（玩家催促时调用）</summary>
        public void Complete()
        {
            _visible = float.MaxValue;
            graphic.SetVerticesDirty();
            if (_playing)
            {
                _playing = false;
                onComplete?.Invoke();
            }
        }

        void Update()
        {
            if (!_playing) return;
            _visible += charsPerSecond * Time.deltaTime;
            graphic.SetVerticesDirty();
            if (_lastQuadCount > 0 && _visible >= _lastQuadCount + fadeSpanChars)
            {
                _playing = false;
                onComplete?.Invoke();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;

            int quadCount = vh.currentVertCount / 4;
            _lastQuadCount = quadCount;
            if (_visible >= quadCount + fadeSpanChars) return; // 全部显示完，无需修改

            var v = new UIVertex();
            for (int q = 0; q < quadCount; q++)
            {
                float t = Mathf.Clamp01((_visible - q) / fadeSpanChars);
                if (t >= 1f) continue;
                float ease = 1f - (1f - t) * (1f - t); // OutQuad
                float yOffset = -(1f - ease) * riseHeight; // 从下方上浮到位

                for (int i = 0; i < 4; i++)
                {
                    int idx = q * 4 + i;
                    vh.PopulateUIVertex(ref v, idx);
                    v.position += new Vector3(0f, yOffset, 0f);
                    var c = v.color;
                    c.a = (byte)(c.a * ease);
                    v.color = c;
                    vh.SetUIVertex(v, idx);
                }
            }
        }
    }
}
