using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 音频管理器：
    ///   BGM   —— 双 AudioSource 交叉淡入淡出（切曲无缝）
    ///   SE    —— 一次性音效（PlayOneShot）+ 循环环境音（每个循环音独立 AudioSource）
    ///   Voice —— 语音通道（同时只放一条，新语音顶掉旧的）
    ///   打字音 —— 指定 typingTick 后打字机自动"哒哒哒"（带节流与随机音高）
    /// 音频库：id → AudioClip，在 Inspector 的 library 列表登记后剧本即可引用。
    /// 当前 BGM 随存档保存。
    /// </summary>
    public class VNAudio : MonoBehaviour
    {
        [System.Serializable]
        public class AudioEntry
        {
            [Tooltip("剧本中引用的 id（可中文，如 黄昏之歌 / 雨声）")]
            public string id;
            public AudioClip clip;
        }

        [Tooltip("音频库：剧本里 bgm/se/voice 命令用 id 引用这里的条目")]
        public List<AudioEntry> library = new List<AudioEntry>();

        [Header("音量")]
        [Range(0f, 1f)] public float bgmVolume = 0.75f;
        [Range(0f, 1f)] public float seVolume = 1f;
        [Range(0f, 1f)] public float voiceVolume = 1f;

        [Header("打字音（可选）")]
        [Tooltip("打字机逐字音效；留空 = 无打字音")]
        public AudioClip typingTick;
        [Tooltip("打字音最小间隔（秒），防止连音刺耳")]
        public float typingTickInterval = 0.055f;

        static VNAudio _instance;

        AudioSource _bgmA, _bgmB;
        bool _usingA;
        string _currentBgm;

        AudioSource _seOneShot;
        AudioSource _voice;
        AudioSource _tick;
        readonly Dictionary<string, AudioSource> _loopingSe =
            new Dictionary<string, AudioSource>();
        float _lastTickTime;
        float _initialBgmVolume, _initialSeVolume, _initialVoiceVolume;

        /// <summary>当前 BGM 的 id（存档用；null = 无）</summary>
        public string CurrentBgm => _currentBgm;

        void Awake()
        {
            _instance = this;
            _initialBgmVolume = bgmVolume;
            _initialSeVolume = seVolume;
            _initialVoiceVolume = voiceVolume;
            _bgmA = CreateSource("BGM_A", true);
            _bgmB = CreateSource("BGM_B", true);
            _seOneShot = CreateSource("SE", false);
            _voice = CreateSource("Voice", false);
            _tick = CreateSource("TypeTick", false);
        }

        /// <summary>编辑器从中间行调试前，立即清除之前自动播放留下的音频状态。</summary>
        public void ResetForDebug()
        {
            foreach (var source in new[] { _bgmA, _bgmB, _seOneShot, _voice, _tick })
            {
                if (source == null) continue;
                source.DOKill();
                source.Stop();
                source.clip = null;
            }
            foreach (var source in _loopingSe.Values)
            {
                if (source == null) continue;
                source.DOKill();
                Destroy(source.gameObject);
            }
            _loopingSe.Clear();
            _currentBgm = null;
            _usingA = false;
            bgmVolume = _initialBgmVolume;
            seVolume = _initialSeVolume;
            voiceVolume = _initialVoiceVolume;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            return src;
        }

        AudioClip Find(string id, int line = 0)
        {
            foreach (var e in library)
                if (e.id == id) return e.clip;
            Debug.LogWarning($"[VNAudio] 第 {line} 行：音频库里没有「{id}」（在 VNAudio.library 登记）");
            return null;
        }

        // ------------------------------------------------------------------
        // BGM
        // ------------------------------------------------------------------

        /// <summary>播放/切换 BGM（交叉淡入淡出）</summary>
        public void PlayBgm(string id, float fade = 1.5f, int line = 0)
        {
            if (id == _currentBgm) return;
            var clip = Find(id, line);
            if (clip == null) return;

            var fadeIn = _usingA ? _bgmB : _bgmA;   // 换到闲置的那个源
            var fadeOut = _usingA ? _bgmA : _bgmB;
            _usingA = !_usingA;
            _currentBgm = id;

            fadeIn.DOKill();
            fadeOut.DOKill();

            fadeIn.clip = clip;
            fadeIn.volume = 0f;
            fadeIn.Play();
            fadeIn.DOFade(bgmVolume, Mathf.Max(0.01f, fade)).SetLink(gameObject);

            if (fadeOut.isPlaying)
                fadeOut.DOFade(0f, Mathf.Max(0.01f, fade)).SetLink(gameObject)
                       .OnComplete(fadeOut.Stop);
        }

        /// <summary>停止 BGM（淡出）</summary>
        public void StopBgm(float fade = 1.5f)
        {
            _currentBgm = null;
            foreach (var src in new[] { _bgmA, _bgmB })
            {
                if (!src.isPlaying) continue;
                src.DOKill();
                src.DOFade(0f, Mathf.Max(0.01f, fade)).SetLink(gameObject)
                   .OnComplete(src.Stop);
            }
        }

        // ------------------------------------------------------------------
        // SE / Voice
        // ------------------------------------------------------------------

        /// <summary>播放音效（loop = 循环环境音，如雨声）</summary>
        public void PlaySe(string id, bool loop = false, int line = 0)
        {
            var clip = Find(id, line);
            if (clip == null) return;

            if (!loop)
            {
                _seOneShot.PlayOneShot(clip, seVolume);
                return;
            }

            if (_loopingSe.TryGetValue(id, out var existing) && existing != null)
                return; // 已在循环播放

            var src = CreateSource($"SE_Loop_{id}", true);
            src.clip = clip;
            src.volume = 0f;
            src.Play();
            src.DOFade(seVolume, 0.8f).SetLink(gameObject);
            _loopingSe[id] = src;
        }

        /// <summary>停止某个循环音效（淡出后销毁）</summary>
        public void StopSe(string id, float fade = 0.8f)
        {
            if (!_loopingSe.TryGetValue(id, out var src) || src == null)
            {
                _loopingSe.Remove(id);
                return;
            }
            _loopingSe.Remove(id);
            src.DOKill();
            src.DOFade(0f, Mathf.Max(0.01f, fade)).SetLink(gameObject)
               .OnComplete(() => Destroy(src.gameObject));
        }

        /// <summary>播放语音（同时只有一条，新的顶掉旧的）</summary>
        public void PlayVoice(string id, int line = 0)
        {
            var clip = Find(id, line);
            if (clip == null) return;
            _voice.Stop();
            _voice.clip = clip;
            _voice.volume = voiceVolume;
            _voice.Play();
        }

        // ------------------------------------------------------------------
        // 音量
        // ------------------------------------------------------------------

        /// <summary>设置通道音量（bgm / se / voice），立即作用于正在播放的声音</summary>
        public void SetVolume(string channel, float volume, int line = 0)
        {
            volume = Mathf.Clamp01(volume);
            switch (channel)
            {
                case "bgm":
                    bgmVolume = volume;
                    var active = _usingA ? _bgmA : _bgmB;
                    if (active.isPlaying)
                    {
                        active.DOKill();
                        active.DOFade(volume, 0.3f).SetLink(gameObject);
                    }
                    break;
                case "se":
                    seVolume = volume;
                    foreach (var kv in _loopingSe)
                        if (kv.Value != null) kv.Value.volume = volume;
                    break;
                case "voice":
                    voiceVolume = volume;
                    _voice.volume = volume;
                    break;
                default:
                    Debug.LogWarning($"[VNAudio] 第 {line} 行：未知音量通道「{channel}」（bgm/se/voice）");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // 打字音（VNTypewriterText 每显示一个字调用一次）
        // ------------------------------------------------------------------

        public static void TypeTick()
        {
            var a = _instance;
            if (a == null || a.typingTick == null) return;
            if (Time.unscaledTime - a._lastTickTime < a.typingTickInterval) return;
            a._lastTickTime = Time.unscaledTime;
            a._tick.pitch = Random.Range(0.94f, 1.06f); // 轻微随机音高，不机械
            a._tick.PlayOneShot(a.typingTick, a.seVolume * 0.7f);
        }
    }
}
