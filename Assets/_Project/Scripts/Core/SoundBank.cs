using UnityEngine;

namespace AnimalEscape.Core
{
    /// <summary>
    /// 原型用的音效：專案裡還沒有任何音效檔，這裡用程式即時合成幾個簡單的聲音，只求聽得出差別。
    /// 之後有正式音效時，把這幾個屬性換成載入音效檔就好。
    /// </summary>
    public static class SoundBank
    {
        const int k_Rate = 44100;

        static AudioClip s_Footstep;
        static AudioClip s_Bell;
        static AudioClip s_Cry;
        static AudioClip s_Crash;

        /// <summary>人類的腳步聲：低沉的「咚」加一點沙沙的雜音。</summary>
        public static AudioClip Footstep
        {
            get { return s_Footstep != null ? s_Footstep : (s_Footstep = Make("Footstep", 0.16f, FootstepSample)); }
        }

        /// <summary>動物項圈的鈴鐺：清脆的「叮」，有兩個泛音，慢慢消失。</summary>
        public static AudioClip Bell
        {
            get { return s_Bell != null ? s_Bell : (s_Bell = Make("Bell", 0.45f, BellSample)); }
        }

        /// <summary>動物被抓時的叫聲：音高往上滑、帶一點顫音的「吱──」。</summary>
        public static AudioClip Cry
        {
            get { return s_Cry != null ? s_Cry : (s_Cry = Make("Cry", 0.5f, CrySample)); }
        }

        /// <summary>東西被弄倒：「哐啷」一聲，低沉的撞擊加上碎裂的雜音。</summary>
        public static AudioClip Crash
        {
            get { return s_Crash != null ? s_Crash : (s_Crash = Make("Crash", 0.6f, CrashSample)); }
        }

        static float CrashSample(float t, System.Random random)
        {
            float thud = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 14f);
            float noise = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 9f) * 0.6f;
            float clink = Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-t * 22f) * 0.25f
                + Mathf.Sin(2f * Mathf.PI * 2650f * (t - 0.08f)) * Mathf.Exp(-Mathf.Max(0f, t - 0.08f) * 25f) * (t > 0.08f ? 0.2f : 0f);
            float attack = Mathf.Clamp01(t * 500f);
            return (thud * 0.8f + noise + clink) * attack;
        }

        delegate float Sample(float t, System.Random random);

        static AudioClip Make(string name, float seconds, Sample sample)
        {
            int count = Mathf.CeilToInt(seconds * k_Rate);
            var data = new float[count];
            var random = new System.Random(name.GetHashCode());
            for (int i = 0; i < count; i++)
            {
                data[i] = Mathf.Clamp(sample((float)i / k_Rate, random), -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("Synth " + name, count, 1, k_Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float FootstepSample(float t, System.Random random)
        {
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 30f);
            float noise = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 55f) * 0.45f;
            float attack = Mathf.Clamp01(t * 400f);
            return (thump * 0.9f + noise) * attack;
        }

        static float BellSample(float t, System.Random random)
        {
            float decay = Mathf.Exp(-t * 9f);
            float tone = Mathf.Sin(2f * Mathf.PI * 2350f * t) * 0.6f
                + Mathf.Sin(2f * Mathf.PI * 3530f * t) * 0.3f
                + Mathf.Sin(2f * Mathf.PI * 5170f * t) * 0.15f * Mathf.Exp(-t * 20f);
            float attack = Mathf.Clamp01(t * 2000f);
            return tone * decay * attack * 0.7f;
        }

        static float CrySample(float t, System.Random random)
        {
            // 音高在 0.35 秒內從 700 滑到 1400 Hz，再加每秒 18 次、上下 60 Hz 的顫音。
            // 相位用頻率的積分直接算出來（每個取樣點各自算，不用累加），滑音才不會破音。
            const float from = 700f;
            const float to = 1400f;
            const float glide = 0.35f;
            float inGlide = Mathf.Min(t, glide);
            float cycles = from * t + (to - from) * 0.5f * inGlide * inGlide / glide + Mathf.Max(0f, t - glide) * (to - from)
                - (60f / (2f * Mathf.PI * 18f)) * Mathf.Cos(2f * Mathf.PI * 18f * t);
            float phase = 2f * Mathf.PI * cycles;
            float envelope = Mathf.Clamp01(t * 60f) * Mathf.Clamp01((0.5f - t) * 8f);
            return (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2f) * 0.2f) * envelope * 0.8f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Footstep = null;
            s_Bell = null;
            s_Cry = null;
        }
    }
}
