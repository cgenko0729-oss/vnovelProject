using TMPro;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 打字机文字（TextMeshPro 版）：
    /// 逐字显现，每个字出现时从下方轻微上浮 + 淡入。
    /// 通过 TMP 的 textInfo 逐字顶点信息改网格（每个可见字四个顶点），
    /// 富文本标签不占字数（characterInfo 已剔除控制符，优于旧版按 quad 计数）。
    /// 对外 API（Play/Complete/IsTyping/onComplete）与 legacy Text 版保持一致。
    /// </summary>
    [RequireComponent(typeof(TMP_Text))]
    public class VNTypewriterText : MonoBehaviour
    {
        [Header("每秒显示字数")]
        public float charsPerSecond = 18f;

        [Header("每个字上浮的高度（像素）")]
        public float riseHeight = 10f;

        [Header("每个字淡入动画跨越的字数（越大越绵柔）")]
        public float fadeSpanChars = 2.5f;

        /// <summary>整段文字显示完毕时回调</summary>
        public System.Action onComplete;

        TMP_Text _text;
        float _visible;      // 已显现的"字数进度"（小数，按可见字计）
        bool _playing;
        bool _animating;     // 是否仍需每帧刷新顶点（播放中，或催促后收尾一帧）

        public bool IsTyping => _playing;

        void Awake()
        {
            _text = GetComponent<TMP_Text>();
        }

        /// <summary>开始打字机播放一段文字</summary>
        public void Play(string content)
        {
            if (_text == null) _text = GetComponent<TMP_Text>();
            _text.text = content;
            _visible = 0f;
            _playing = true;
            _animating = true;
        }

        /// <summary>立即显示全部（玩家催促时调用）</summary>
        public void Complete()
        {
            _visible = float.MaxValue;
            _animating = true; // 收尾：把网格刷回完全显示状态
            if (_playing)
            {
                _playing = false;
                onComplete?.Invoke();
            }
        }

        void Update()
        {
            if (!_playing) return;
            int before = Mathf.FloorToInt(_visible);
            _visible += charsPerSecond * Time.deltaTime;
            if (Mathf.FloorToInt(_visible) > before)
                VNAudio.TypeTick(); // 每显现一个新字打一次字音（内部带节流）
        }

        void LateUpdate()
        {
            if (!_animating || _text == null) return;

            _text.ForceMeshUpdate();
            var info = _text.textInfo;

            int visibleIndex = 0;   // 可见字序号（空格/换行/富文本标签不计）
            bool anyPartial = false;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ci = info.characterInfo[i];
                if (!ci.isVisible) continue;

                float t = Mathf.Clamp01((_visible - visibleIndex) / fadeSpanChars);
                visibleIndex++;
                if (t >= 1f) continue;
                anyPartial = true;

                float ease = 1f - (1f - t) * (1f - t); // OutQuad
                float yOffset = -(1f - ease) * riseHeight; // 从下方上浮到位

                var mesh = info.meshInfo[ci.materialReferenceIndex];
                int vi = ci.vertexIndex;
                for (int j = 0; j < 4; j++)
                {
                    mesh.vertices[vi + j].y += yOffset;
                    var c = mesh.colors32[vi + j];
                    c.a = (byte)(c.a * ease);
                    mesh.colors32[vi + j] = c;
                }
            }
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.All);

            if (_playing)
            {
                if (visibleIndex == 0)
                {
                    // 整段没有可见字（空行/纯空白）：视作立即播完，避免卡住剧本推进
                    _playing = false;
                    onComplete?.Invoke();
                }
                else if (_visible >= visibleIndex + fadeSpanChars)
                {
                    _playing = false;
                    onComplete?.Invoke();
                }
            }

            // 全部字都已到位且不再播放：停止每帧重建网格
            if (!_playing && !anyPartial) _animating = false;
        }
    }
}
