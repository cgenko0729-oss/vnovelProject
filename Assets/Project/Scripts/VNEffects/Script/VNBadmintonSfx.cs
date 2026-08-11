using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 羽球的五个音效。**代码合成**（决策 10）：没有任何音频素材也立刻有反馈，
    /// Def 资产里填了真音效就自动覆盖对应那一条。
    ///
    /// 合成配方：一记「拍击」= 指数衰减包络 ×（衰减正弦 + 低通白噪）。
    ///   正弦决定音高（球拍绷弦的"当"），白噪决定质感（击打的"啪"），
    ///   两者的配比 noise 就是「这一拍有多闷」。
    /// 参数沿参考实现的五个音效位分工：发球轻、击球中、精准亮、扣杀闷而重、落地最闷。
    ///
    /// 音量跟随 VNAudio 的 SE 通道，玩家在系统设置里调音效音量对这里同样生效。
    /// </summary>
    public class VNBadmintonSfx
    {
        public enum Kind { Serve, Hit, Perfect, Smash, Land }

        const int SampleRate = 44100;

        AudioSource _source;
        readonly AudioClip[] _clips = new AudioClip[5];
        readonly float[] _gains = { 0.75f, 0.85f, 1f, 1f, 0.55f };
        VNAudio _vnAudio;

        public void Build(GameObject host, VNBadmintonDef def, VNAudio vnAudio)
        {
            _vnAudio = vnAudio;
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            //                            名字        音高  时长   噪声比 衰减
            _clips[(int)Kind.Serve] = Make("bdm_serve", 420f, 0.13f, 0.55f, 42f);
            _clips[(int)Kind.Hit] = Make("bdm_hit", 520f, 0.12f, 0.60f, 48f);
            _clips[(int)Kind.Perfect] = Make("bdm_perfect", 880f, 0.22f, 0.30f, 26f);
            _clips[(int)Kind.Smash] = Make("bdm_smash", 240f, 0.20f, 0.75f, 30f);
            _clips[(int)Kind.Land] = Make("bdm_land", 150f, 0.16f, 0.85f, 34f);

            if (def == null) return;
            Override(Kind.Serve, def.serveSe);
            Override(Kind.Hit, def.hitSe);
            Override(Kind.Perfect, def.perfectSe);
            Override(Kind.Smash, def.smashSe);
            Override(Kind.Land, def.landSe);
        }

        void Override(Kind kind, AudioClip clip)
        {
            if (clip != null) _clips[(int)kind] = clip;
        }

        public void Play(Kind kind)
        {
            if (_source == null) return;
            var clip = _clips[(int)kind];
            if (clip == null) return;
            float channel = _vnAudio != null ? _vnAudio.seVolume : 1f;
            _source.PlayOneShot(clip, _gains[(int)kind] * channel);
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
                lowpass = Mathf.Lerp(lowpass, white, 0.35f);   // 去掉刺耳的高频

                data[i] = env * (tone * (1f - noise) + lowpass * noise) * 0.55f;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
