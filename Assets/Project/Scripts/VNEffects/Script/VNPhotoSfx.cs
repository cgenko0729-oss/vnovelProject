using UnityEngine;

namespace VNEffects
{
    /// <summary>
    /// 大头贴的五个音效。和 VNBadmintonSfx 同样是**代码合成**：没有任何音频素材
    /// 也立刻有反馈，资产里填了真音效就覆盖对应那一条。
    ///
    /// 合成配方分两类：
    ///   打击类（倒数滴 / 快门 / 贴纸落位 / 计分嗒）= 指数衰减包络 ×（正弦 + 低通白噪）
    ///     —— 正弦给音高，白噪给"机械感"，noise 决定这一下有多"咔"。
    ///   结算类（Fanfare）= 三音琶音，每个音一段衰减正弦，靠时间错开。
    ///
    /// 快门声故意做成 noise 很高 + 衰减很快的两下：单簧快门就是"咔"+"嗒"。
    /// 音量跟随 VNAudio 的 SE 通道，玩家在系统设置里调音效音量对这里同样生效。
    /// </summary>
    public class VNPhotoSfx
    {
        public enum Kind { Tick, Shutter, Place, Count, Fanfare }

        const int SampleRate = 44100;

        AudioSource _source;
        readonly AudioClip[] _clips = new AudioClip[5];
        readonly float[] _gains = { 0.55f, 0.9f, 0.4f, 0.25f, 0.8f };
        VNAudio _vnAudio;

        public void Build(GameObject host, VNAudio vnAudio)
        {
            _vnAudio = vnAudio;
            _source = host.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            //                              名字         音高   时长   噪声比 衰减
            _clips[(int)Kind.Tick] = Make("photo_tick", 880f, 0.10f, 0.20f, 40f);
            _clips[(int)Kind.Shutter] = Shutter();
            _clips[(int)Kind.Place] = Make("photo_place", 1400f, 0.07f, 0.75f, 70f);
            _clips[(int)Kind.Count] = Make("photo_count", 1650f, 0.035f, 0.15f, 120f);
            _clips[(int)Kind.Fanfare] = Fanfare();
        }

        /// <summary>资产里配了真音效就覆盖（留空保持合成的）</summary>
        public void Override(Kind kind, AudioClip clip)
        {
            if (clip != null) _clips[(int)kind] = clip;
        }

        public void Play(Kind kind, float pitch = 1f)
        {
            if (_source == null) return;
            var clip = _clips[(int)kind];
            if (clip == null) return;
            float channel = _vnAudio != null ? _vnAudio.seVolume : 1f;
            _source.pitch = pitch;
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
                lowpass = Mathf.Lerp(lowpass, white, 0.35f);

                data[i] = env * (tone * (1f - noise) + lowpass * noise) * 0.55f;
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>快门：机械的两下（帘幕开 + 合），全程噪声为主</summary>
        static AudioClip Shutter()
        {
            const float duration = 0.22f;
            int count = Mathf.CeilToInt(SampleRate * duration);
            var data = new float[count];
            var rng = new System.Random(20260812);

            float lowpass = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;

                // 两记短促的"咔"：第二下更闷更轻（快门帘合上）
                float env = Mathf.Exp(-t * 70f);
                if (t > 0.075f) env += Mathf.Exp(-(t - 0.075f) * 55f) * 0.7f;

                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lowpass = Mathf.Lerp(lowpass, white, 0.55f);

                // 一点低频"咚"给它重量感
                float body = Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.25f;

                data[i] = Mathf.Clamp(env * (lowpass * 0.85f + body), -1f, 1f) * 0.6f;
            }

            var clip = AudioClip.Create("photo_shutter", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>结算：三音上行琶音（大三和弦），一听就是"出成绩了"</summary>
        static AudioClip Fanfare()
        {
            const float duration = 0.75f;
            int count = Mathf.CeilToInt(SampleRate * duration);
            var data = new float[count];

            float[] freqs = { 523.25f, 659.25f, 783.99f };  // C5 E5 G5
            float[] starts = { 0f, 0.09f, 0.18f };

            for (int n = 0; n < freqs.Length; n++)
                for (int i = 0; i < count; i++)
                {
                    float t = i / (float)SampleRate - starts[n];
                    if (t < 0f) continue;
                    float env = Mathf.Exp(-t * 5.5f);
                    // 加一个八度泛音让音色亮一点，不然像电话忙音
                    float tone = Mathf.Sin(2f * Mathf.PI * freqs[n] * t) +
                                 Mathf.Sin(2f * Mathf.PI * freqs[n] * 2f * t) * 0.3f;
                    data[i] += env * tone * 0.22f;
                }

            for (int i = 0; i < count; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);

            var clip = AudioClip.Create("photo_fanfare", count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
