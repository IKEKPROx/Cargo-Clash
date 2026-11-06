using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>Plays 2D sound effects and music by name from Resources/Audio.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        private AudioSource effects;
        private AudioSource music;

        private void Awake()
        {
            instance = this;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.volume = 0.45f;
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio")) clips[clip.name] = clip;
        }

        public static void Play(string clipName, float volume = 1f)
        {
            if (instance == null || !instance.clips.TryGetValue(clipName, out AudioClip clip)) return;
            instance.effects.PlayOneShot(clip, volume);
        }

        /// <summary>For sounds that many things can trigger at once, such as several smashers.</summary>
        public static void PlayThrottled(string clipName, float minInterval, float volume = 1f)
        {
            if (instance == null) return;
            instance.lastPlayed.TryGetValue(clipName, out float last);
            if (Time.unscaledTime - last < minInterval) return;
            instance.lastPlayed[clipName] = Time.unscaledTime;
            Play(clipName, volume);
        }

        public static void SetMusic(string clipName, float pitch = 1f)
        {
            if (instance == null) return;
            AudioSource source = instance.music;
            source.pitch = pitch;
            if (string.IsNullOrEmpty(clipName))
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            if (!instance.clips.TryGetValue(clipName, out AudioClip clip)) return;
            if (source.clip == clip && source.isPlaying) return;
            source.clip = clip;
            source.Play();
        }
    }
}
