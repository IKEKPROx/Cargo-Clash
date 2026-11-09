using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>Plays 2D sound effects and music by name from Resources/Audio.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const float MusicVolume = 0.45f;
        private const float DuckedMusicVolume = 0.16f;

        private static GameAudio instance;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        private AudioSource effects;
        private AudioSource music;
        private AudioSource loop;

        private void Awake()
        {
            instance = this;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false;
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.loop = true;
            music.volume = MusicVolume;
            loop = gameObject.AddComponent<AudioSource>();
            loop.playOnAwake = false;
            loop.loop = true;
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

        /// <summary>Turns the music down so something else can be heard over it.</summary>
        public static void SetMusicDucked(bool ducked)
        {
            if (instance != null) instance.music.volume = ducked ? DuckedMusicVolume : MusicVolume;
        }

        /// <summary>A looping sound whose volume a script sets every frame. No clip name stops it.</summary>
        public static void SetLoop(string clipName, float volume)
        {
            if (instance == null) return;
            AudioSource source = instance.loop;
            if (string.IsNullOrEmpty(clipName))
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            if (!instance.clips.TryGetValue(clipName, out AudioClip clip)) return;
            source.volume = volume;
            if (source.clip == clip && source.isPlaying) return;
            source.clip = clip;
            source.Play();
        }
    }
}
