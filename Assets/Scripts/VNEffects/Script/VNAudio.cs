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
    /// 音频库：id → AudioClip，按通道分为 bgmLibrary / seLibrary / voiceLibrary 三个库，
    /// 每个条目带独立基准音量（素材响度不齐时在库里标定一次，所有引用处生效）。
    /// 旧的混合 library 保留兼容：三个通道都找得到里面的条目，建议逐步迁移。
    /// 最终音量 = 条目基准音量 × 剧本 vol 参数 × 通道音量。
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
            [Tooltip("该素材的基准音量。素材本身偏响就往下调；Unity 音量上限为 1，无法放大素材本身")]
            [Range(0f, 1f)] public float volume = 1f;
        }

        [Header("音频库（按通道分开管理）")]
        [Tooltip("BGM 库：剧本 bgm 命令用 id 引用")]
        public List<AudioEntry> bgmLibrary = new List<AudioEntry>();
        [Tooltip("SE 库：剧本 se 命令用 id 引用")]
        public List<AudioEntry> seLibrary = new List<AudioEntry>();
        [Tooltip("语音库：剧本 voice 命令用 id 引用")]
        public List<AudioEntry> voiceLibrary = new List<AudioEntry>();

        [Header("旧版混合库（兼容保留，建议迁移到上面对应库）")]
        [Tooltip("旧场景登记的条目仍能被 bgm/se/voice 三个通道找到，找不到 id 时才提示")]
        public List<AudioEntry> library = new List<AudioEntry>();

        [Header("音量")]
        [Range(0f, 1f)] public float bgmVolume = 0.75f;
        [Range(0f, 1f)] public float seVolume = 1f;
        [Range(0f, 1f)] public float voiceVolume = 1f;

        [Header("语音时压低 BGM")]
        [Tooltip("播放语音时 BGM 降低的比例。0.2 = 降低 20%，即保留原音量的 80%。")]
        [Range(0f, 1f)] public float voiceBgmReduction = 0.2f;
        [Tooltip("BGM 压低和恢复所需的淡入淡出时间（秒）")]
        [Min(0f)] public float voiceBgmFadeDuration = 0.25f;

        [Header("打字音（可选）")]
        [Tooltip("打字机逐字音效；留空 = 无打字音")]
        public AudioClip typingTick;
        [Tooltip("打字音最小间隔（秒），防止连音刺耳")]
        public float typingTickInterval = 0.055f;

        static VNAudio _instance;

        AudioSource _bgmA, _bgmB;
        bool _usingA;
        string _currentBgm;
        float _currentBgmGain = 1f;      // 条目基准 × 剧本 vol，PlayBgm 时确定
        float _currentBgmScriptVol = 1f; // 仅剧本 vol 参数（存档用，基准音量读档时从库里重新取）

        AudioSource _seOneShot;
        AudioSource _voice;
        AudioSource _tick;
        bool _isBgmDucked;
        float _currentVoiceGain = 1f;

        /// <summary>一个正在循环播放的 SE 及其增益（通道音量变化时按增益重算）</summary>
        class LoopingSe
        {
            public AudioSource source;
            public float gain;
        }
        readonly Dictionary<string, LoopingSe> _loopingSe =
            new Dictionary<string, LoopingSe>();
        float _lastTickTime;
        float _initialBgmVolume, _initialSeVolume, _initialVoiceVolume;

        /// <summary>当前 BGM 的 id（存档用；null = 无）</summary>
        public string CurrentBgm => _currentBgm;

        /// <summary>当前 BGM 的剧本 vol 参数（存档用；读档时传回 PlayBgm）</summary>
        public float CurrentBgmVol => _currentBgmScriptVol;

        /// <summary>当前是否仍有角色语音在播放（口型同步用）。</summary>
        public bool IsVoicePlaying => _voice != null && _voice.isPlaying;

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
            foreach (var looping in _loopingSe.Values)
            {
                if (looping?.source == null) continue;
                looping.source.DOKill();
                Destroy(looping.source.gameObject);
            }
            _loopingSe.Clear();
            _currentBgm = null;
            _currentBgmGain = 1f;
            _currentBgmScriptVol = 1f;
            _currentVoiceGain = 1f;
            _usingA = false;
            _isBgmDucked = false;
            bgmVolume = _initialBgmVolume;
            seVolume = _initialSeVolume;
            voiceVolume = _initialVoiceVolume;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            // AudioSource 没有“自然播放完毕”事件，因此在语音结束后的第一帧恢复 BGM。
            // 新语音会在 PlayVoice 中直接替换旧语音，不会在两条语音之间误恢复。
            if (_isBgmDucked && (_voice == null || !_voice.isPlaying))
                SetBgmDucked(false);
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

        static AudioEntry FindIn(List<AudioEntry> lib, string id)
        {
            if (lib == null) return null;
            foreach (var e in lib)
                if (e != null && e.id == id && e.clip != null) return e;
            return null;
        }

        /// <summary>先查通道专属库，再查旧混合库；都没有则告警。</summary>
        AudioEntry Find(List<AudioEntry> channelLib, string libName, string id, int line)
        {
            var entry = FindIn(channelLib, id) ?? FindIn(library, id);
            if (entry == null)
                Debug.LogWarning($"[VNAudio] 第 {line} 行：音频库里没有「{id}」" +
                                 $"（在 VNAudio.{libName} 登记）");
            return entry;
        }

        // ------------------------------------------------------------------
        // BGM
        // ------------------------------------------------------------------

        /// <summary>播放/切换 BGM（交叉淡入淡出）。vol = 剧本音量乘数（叠加条目基准音量）</summary>
        public void PlayBgm(string id, float fade = 1.5f, float vol = 1f, int line = 0)
        {
            if (id == _currentBgm) return;
            var entry = Find(bgmLibrary, "bgmLibrary", id, line);
            if (entry == null) return;

            var fadeIn = _usingA ? _bgmB : _bgmA;   // 换到闲置的那个源
            var fadeOut = _usingA ? _bgmA : _bgmB;
            _usingA = !_usingA;
            _currentBgm = id;
            _currentBgmScriptVol = Mathf.Clamp01(vol);
            _currentBgmGain = entry.volume * _currentBgmScriptVol;

            fadeIn.DOKill();
            fadeOut.DOKill();

            fadeIn.clip = entry.clip;
            fadeIn.volume = 0f;
            fadeIn.Play();
            fadeIn.DOFade(EffectiveBgmVolume, Mathf.Max(0.01f, fade)).SetLink(gameObject);

            if (fadeOut.isPlaying)
                fadeOut.DOFade(0f, Mathf.Max(0.01f, fade)).SetLink(gameObject)
                       .OnComplete(fadeOut.Stop);
        }

        /// <summary>停止 BGM（淡出）</summary>
        public void StopBgm(float fade = 1.5f)
        {
            _currentBgm = null;
            _currentBgmGain = 1f;
            _currentBgmScriptVol = 1f;
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

        /// <summary>播放音效（loop = 循环环境音，如雨声）。vol = 剧本音量乘数</summary>
        public void PlaySe(string id, bool loop = false, float vol = 1f, int line = 0)
        {
            var entry = Find(seLibrary, "seLibrary", id, line);
            if (entry == null) return;
            float gain = entry.volume * Mathf.Clamp01(vol);

            if (!loop)
            {
                _seOneShot.PlayOneShot(entry.clip, seVolume * gain);
                return;
            }

            if (_loopingSe.TryGetValue(id, out var existing) && existing?.source != null)
                return; // 已在循环播放

            var src = CreateSource($"SE_Loop_{id}", true);
            src.clip = entry.clip;
            src.volume = 0f;
            src.Play();
            src.DOFade(seVolume * gain, 0.8f).SetLink(gameObject);
            _loopingSe[id] = new LoopingSe { source = src, gain = gain };
        }

        /// <summary>停止某个循环音效（淡出后销毁）</summary>
        public void StopSe(string id, float fade = 0.8f)
        {
            if (!_loopingSe.TryGetValue(id, out var looping) || looping?.source == null)
            {
                _loopingSe.Remove(id);
                return;
            }
            _loopingSe.Remove(id);
            var src = looping.source;
            src.DOKill();
            src.DOFade(0f, Mathf.Max(0.01f, fade)).SetLink(gameObject)
               .OnComplete(() => Destroy(src.gameObject));
        }

        /// <summary>播放语音（同时只有一条，新的顶掉旧的）。vol = 剧本音量乘数</summary>
        public bool PlayVoice(string id, float vol = 1f, int line = 0)
        {
            var entry = Find(voiceLibrary, "voiceLibrary", id, line);
            if (entry == null) return false;
            _currentVoiceGain = entry.volume * Mathf.Clamp01(vol);
            _voice.Stop();
            _voice.clip = entry.clip;
            _voice.volume = voiceVolume * _currentVoiceGain;
            _voice.Play();
            SetBgmDucked(true);
            return true;
        }

        float EffectiveBgmVolume =>
            bgmVolume * _currentBgmGain * (_isBgmDucked ? 1f - voiceBgmReduction : 1f);

        void SetBgmDucked(bool ducked)
        {
            if (_isBgmDucked == ducked) return;
            _isBgmDucked = ducked;

            var active = _usingA ? _bgmA : _bgmB;
            if (active == null || !active.isPlaying) return;

            active.DOKill();
            active.DOFade(EffectiveBgmVolume, Mathf.Max(0.01f, voiceBgmFadeDuration))
                  .SetLink(gameObject);
        }

        // ------------------------------------------------------------------
        // 音量
        // ------------------------------------------------------------------

        /// <summary>设置通道音量（bgm / se / voice），立即作用于正在播放的声音（保留各自增益）</summary>
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
                        active.DOFade(EffectiveBgmVolume, 0.3f).SetLink(gameObject);
                    }
                    break;
                case "se":
                    seVolume = volume;
                    foreach (var kv in _loopingSe)
                        if (kv.Value?.source != null)
                            kv.Value.source.volume = volume * kv.Value.gain;
                    break;
                case "voice":
                    voiceVolume = volume;
                    _voice.volume = volume * _currentVoiceGain;
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
