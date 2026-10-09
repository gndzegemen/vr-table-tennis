using UnityEngine;

namespace TableTennis
{
    /// <summary>Procedurally generated ball sounds, so the build needs no audio assets.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class GameAudio : MonoBehaviour
    {
        AudioSource m_Source;
        AudioClip m_Racket;
        AudioClip m_Table;
        AudioClip m_Net;
        AudioClip m_Floor;
        AudioClip m_Point;

        void Awake()
        {
            m_Source = GetComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f;
            m_Racket = Click("racket", 1150f, 0.05f, 0.35f);
            m_Table = Click("table", 1900f, 0.035f, 0.2f);
            m_Net = Click("net", 420f, 0.08f, 0.7f);
            m_Floor = Click("floor", 900f, 0.03f, 0.25f);
            m_Point = Chime("point");
        }

        public void PlayRacket(float volume = 1f) => Play(m_Racket, volume);
        public void PlayTable() => Play(m_Table, 0.8f);
        public void PlayNet() => Play(m_Net, 0.6f);
        public void PlayFloor() => Play(m_Floor, 0.5f);
        public void PlayPoint() => Play(m_Point, 0.35f);

        void Play(AudioClip clip, float volume)
        {
            if (m_Source != null && clip != null)
                m_Source.PlayOneShot(clip, volume);
        }

        static AudioClip Click(string name, float frequency, float duration, float noise)
        {
            const int rate = 44100;
            int samples = Mathf.CeilToInt(rate * duration);
            var data = new float[samples];
            var rng = new System.Random(name.GetHashCode());
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t / (duration * 0.22f));
                float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * frequency * 2.3f * t) * 0.3f;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = (tone * (1f - noise) + n * noise) * envelope * 0.9f;
            }
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Chime(string name)
        {
            const int rate = 44100;
            const float duration = 0.35f;
            int samples = Mathf.CeilToInt(rate * duration);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float f = t < duration * 0.4f ? 880f : 1320f;
                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 6f) * 0.6f;
            }
            var clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
