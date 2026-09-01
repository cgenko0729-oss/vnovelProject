using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 擦雾玩法的四个音效。**代码合成**（决策 10）：没有任何音频素材也立刻有反馈，
    /// Def 资产里填了真音效就自动覆盖对应那一条。
    ///
    /// 【擦拭循环音为什么重要】
    /// 本作刻意不做速度惩罚（擦到就掉），于是「擦得快不快」在画面上是没有反馈的。
    /// 这条循环音的音量与音调跟着鼠标速度走，就是玩家唯一能听见的速度反馈——
    /// 少了它，擦拭会变成一件安静而无感的体力活。
    ///
    /// 循环音是一段 1 秒的滤波白噪（湿抹布擦玻璃的「唰」），靠改 pitch 与 volume
    /// 来表现速度，而不是切多段素材：切段会在切换点爆音，连续调参不会。
    /// </summary>
    public class VNFogSfx
    {
        public enum Kind { Blob, Clear, Tick }

        const int SampleRate = 44100;

        AudioSource _loopSource;
        AudioSource _oneShotSource;
        AudioClip _loopClip;
        readonly AudioClip[] _clips = new AudioClip[3];
        readonly float[] _gains = { 0.5f, 0.9f, 0.6f };
        VNAudio _vnAudio;

        /// <summary>循环音的当前音量（平滑用，避免速度抖动导致音量突变）</summary>
        float _loopVolume;

        public void Build(GameObject host, VNFogWipeDef def, VNAudio vnAudio)
        {
            _vnAudio = vnAudio;

            _loopClip = MakeWipeLoop("fog_wipe_loop");
            if (def != null && def.wipeLoopSe != null) _loopClip = def.wipeLoopSe;

            _loopSource = host.AddComponent<AudioSource>();
            _loopSource.playOnAwake = false;
            _loopSource.spatialBlend = 0f;
            _loopSource.loop = true;
            _loopSource.clip = _loopClip;
            _loopSource.volume = 0f;
            _loopSource.Play();

            _oneShotSource = host.AddComponent<AudioSource>();
            _oneShotSource.playOnAwake = false;
            _oneShotSource.spatialBlend = 0f;

            //                          名字          音高  时长  噪声比 衰减
            _clips[(int)Kind.Blob] = Make("fog_blob", 130f, 0.30f, 0.80f, 11f);
            _clips[(int)Kind.Clear] = Make("fog_clear", 780f, 0.42f, 0.18f, 7f);
            _clips[(int)Kind.Tick] = Make("fog_tick", 900f, 0.09f, 0.15f, 45f);

            if (def == null) return;
            if (def.blobSe != null) _clips[(int)Kind.Blob] = def.blobSe;
            if (def.clearSe != null) _clips[(int)Kind.Clear] = def.clearSe;
            if (def.tickSe != null) _clips[(int)Kind.Tick] = def.tickSe;
        }

        /// <summary>
        /// 每帧告知擦拭速度（屏幕像素/秒）。0 = 没在擦。
        /// 音量与音调都做了平滑，否则鼠标速度的逐帧抖动会听成一串爆音。
        /// </summary>
        public void SetWipeSpeed(float speed, float dt)
        {
            if (_loopSource == null) return;

            float channel = _vnAudio != null ? _vnAudio.seVolume : 1f;
            float target = Mathf.Clamp01(speed / 1200f);
            _loopVolume = Mathf.MoveTowards(_loopVolume, target, dt * 6f);

            _loopSource.volume = _loopVolume * 0.45f * channel;
            _loopSource.pitch = Mathf.Lerp(0.75f, 1.45f, Mathf.Clamp01(speed / 1600f));
        }

        public void Play(Kind kind)
        {
            if (_oneShotSource == null) return;
            var clip = _clips[(int)kind];
            if (clip == null) return;
            float channel = _vnAudio != null ? _vnAudio.seVolume : 1f;
            _oneShotSource.PlayOneShot(clip, _gains[(int)kind] * channel);
        }

        /// <summary>模块结束时把循环音停掉（AudioSource 随模块销毁，但停一下更干净）</summary>
        public void Stop()
        {
            if (_loopSource != null)
            {
                _loopSource.Stop();
                _loopSource.volume = 0f;
            }
        }

        // ==================================================================

        /// <summary>
        /// 一秒的可循环「唰」声：低通白噪 + 几条缓慢起伏的包络（模拟抹布的纹理）。
        /// 首尾各 20ms 交叉淡入淡出，循环点才不会「哒」一下。
        /// </summary>
        static AudioClip MakeWipeLoop(string name)
        {
            int count = SampleRate;
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());

            float lp1 = 0f, lp2 = 0f;
            for (int i = 0; i < count; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                // 两级低通串联：一级还是太"沙"，两级才接近湿布摩擦的闷响
                lp1 = Mathf.Lerp(lp1, white, 0.22f);
                lp2 = Mathf.Lerp(lp2, lp1, 0.30f);

                float t = i / (float)SampleRate;
                // 两个不成整数倍的低频起伏，听起来才不像一段死噪声
                float texture = 0.7f + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 3.1f)
                                     * Mathf.Sin(t * Mathf.PI * 2f * 7.7f);
                data[i] = lp2 * texture * 2.2f;
            }

            // 循环点交叉淡化
            int fade = SampleRate / 50;              // 20ms
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[count - 1 - i] *= k;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <param name="noise">0 = 纯乐音，1 = 纯噪声</param>
        /// <param name="decay">越大衰减越快（越"短促"）</param>
        static AudioClip Make(string name, float freq, float duration, float noise, float decay)
        {
            int count = Mathf.CeilToInt(SampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());

            float phase = 0f;
            float lowpass = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * decay);
                phase += 2f * Mathf.PI * freq / SampleRate;

                float tone = Mathf.Sin(phase);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lowpass = Mathf.Lerp(lowpass, white, 0.35f);

                data[i] = env * (tone * (1f - noise) + lowpass * noise) * 0.55f;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
