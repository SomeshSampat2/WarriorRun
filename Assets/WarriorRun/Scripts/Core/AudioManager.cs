using UnityEngine;

namespace WarriorRun.Core
{
    public enum Sfx { Coin, Jump, Slide, Swipe, Crash, Click, Start, PowerUp, ShieldBreak, Car, Plane, Gem, Phase, Stomp, Spring, Ghost }

    /// <summary>
    /// Plays generated clips from Resources/Audio. Bootstraps itself so every
    /// scene gets audio without manual wiring.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        static AudioManager inst;
        public static AudioManager Instance =>
            inst != null ? inst : (inst = FindAnyObjectByType<AudioManager>(FindObjectsInactive.Include));

        AudioSource sfxSource;
        AudioSource musicSource;
        AudioClip[] clips;
        AudioClip music;
        AudioClip[] beats;   // beat_* clips — one random track per run
        int lastBeat = -1;   // remembers the previous pick so runs never repeat back-to-back

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            if (Instance == null)
                new GameObject("AudioManager").AddComponent<AudioManager>();
        }

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[WR] AudioManager.Awake begin");

            sfxSource = GetComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.loop = true;
            musicSource.volume = 0.5f;

            clips = Resources.LoadAll<AudioClip>("Audio");
            // gather every beat_N clip; music_loop stays as the fallback track
            int count = 0;
            foreach (var c in clips) if (c != null && c.name.StartsWith("beat_")) count++;
            if (count > 0)
            {
                beats = new AudioClip[count];
                int i = 0;
                foreach (var c in clips) if (c != null && c.name.StartsWith("beat_")) beats[i++] = c;
            }
            music = PickBeat();

            // constant BGM from the very first frame — the object survives
            // every scene load so the track never restarts mid-session
            PlayMusic();
            Debug.Log("[WR] AudioManager.Awake done clips=" + clips.Length);
        }

        AudioClip FindClip(string name)
        {
            foreach (var c in clips)
                if (c != null && c.name == name) return c;
            return null;
        }

        public void Play(Sfx sfx)
        {
            if (!SaveSystem.SoundOn) return;
            var clip = FindClip("sfx_" + sfx.ToString().ToLowerInvariant());
            if (clip != null) sfxSource.PlayOneShot(clip);
        }

        public void PlayMusic()
        {
            if (music == null || !SaveSystem.SoundOn) return;
            if (musicSource.clip != music) musicSource.clip = music;
            if (!musicSource.isPlaying) musicSource.Play();
        }

        /// <summary>Pick a random beat — never the same one twice in a row.</summary>
        AudioClip PickBeat()
        {
            if (beats == null || beats.Length == 0) return FindClip("music_loop");
            if (beats.Length == 1) return beats[0];
            int i;
            do { i = Random.Range(0, beats.Length); } while (i == lastBeat);
            lastBeat = i;
            return beats[i];
        }

        /// <summary>Switch to a fresh random beat — called when a new run begins.</summary>
        public void PlayRandomMusic()
        {
            var next = PickBeat();
            if (next == null) return;
            music = next;
            if (!SaveSystem.SoundOn) { musicSource.clip = next; return; }
            musicSource.clip = next;
            musicSource.Play();
        }

        public void StopMusic() => musicSource.Stop();
    }
}
