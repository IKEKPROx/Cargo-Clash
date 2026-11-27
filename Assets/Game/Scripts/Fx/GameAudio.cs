using System.Collections.Generic;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Plays 2D sound effects and music by name from Resources/Audio. The settings page's music and effects volumes
    /// scale everything here; its overall volume is the AudioListener's.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        private const float MusicVolume = 0.45f;
        private const float DuckedMusicVolume = 0.16f;
        private const float HearFullyWithin = 5f;
        private const float HearNothingBeyond = 55f;
        private const float MusicFadeSeconds = 0.25f;

        private static GameAudio instance;

        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();
        private AudioSource effects;
        private AudioSource music;
        private AudioSource loop;
        private readonly AudioSource[] placed = new AudioSource[8];
        private int nextPlaced;
        private bool ducked;
        // A level's timed match music: which half it started for, whether it's in its fast part, and its fade-in after a jump.
        private int timedHalf;
        private bool timedSprint;
        private float musicFade = 1f;

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
            for (int i = 0; i < placed.Length; i++)
            {
                placed[i] = gameObject.AddComponent<AudioSource>();
                placed[i].playOnAwake = false;
            }
            foreach (AudioClip clip in Resources.LoadAll<AudioClip>("Audio")) clips[clip.name] = clip;
            ApplyVolumes();
        }

        /// <summary>Called when the player moves a volume slider.</summary>
        public static void ApplyVolumes()
        {
            if (instance == null) return;
            instance.music.volume = (instance.ducked ? DuckedMusicVolume : MusicVolume) * GameSettings.MusicVolume * instance.musicFade;
            instance.effects.volume = GameSettings.EffectsVolume;
            foreach (AudioSource source in instance.placed) source.volume = GameSettings.EffectsVolume;
        }

        public static void Play(string clipName, float volume = 1f)
        {
            if (instance == null || !instance.clips.TryGetValue(clipName, out AudioClip clip)) return;
            instance.effects.PlayOneShot(clip, volume);
        }

        /// <summary>
        /// A sound from somewhere in the world: quieter the farther away it is, and from the side it came
        /// from. Good for hearing someone coming up behind you in first person.
        /// </summary>
        public static void PlayAt(string clipName, Vector3 position, float volume = 1f)
        {
            if (instance == null || !instance.clips.TryGetValue(clipName, out AudioClip clip)) return;
            Camera view = Camera.main;
            if (view == null)
            {
                instance.effects.PlayOneShot(clip, volume);
                return;
            }

            Vector3 offset = position - view.transform.position;
            float distance = offset.magnitude;
            float loudness = Mathf.Clamp01(1f - (distance - HearFullyWithin) / (HearNothingBeyond - HearFullyWithin));
            loudness *= loudness;
            if (loudness < 0.02f) return;

            AudioSource source = instance.placed[instance.nextPlaced];
            instance.nextPlaced = (instance.nextPlaced + 1) % instance.placed.Length;
            source.panStereo = distance > 0.5f ? Vector3.Dot(offset / distance, view.transform.right) * 0.8f : 0f;
            source.PlayOneShot(clip, volume * loudness);
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
            instance.timedHalf = 0;
            if (instance.musicFade < 1f)
            {
                instance.musicFade = 1f;
                ApplyVolumes();
            }
            source.loop = true;
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

        /// <summary>
        /// A level's own match music (LevelLayout.matchMusic), timed to the half: it starts from the top each half, and the
        /// final stretch jumps to its fast part if a short half didn't get there by itself. A jump fades in.
        /// </summary>
        public static void SetTimedMusic(AudioClip clip, int half, float position, float sprintStart, bool sprint)
        {
            if (instance == null || clip == null) return;
            GameAudio audio = instance;
            AudioSource source = audio.music;
            bool newHalf = audio.timedHalf != half || source.clip != clip;
            bool sectionChanged = audio.timedSprint != sprint;
            bool wrapped = !sprint && source.time >= sprintStart && position < sprintStart - 0.25f;
            bool seek = (sectionChanged || wrapped) && Mathf.Abs(source.time - position) > 0.30f;
            if (audio.ducked)
            {
                audio.ducked = false;
                ApplyVolumes();
            }
            if (newHalf)
            {
                source.clip = clip;
                source.loop = false;
                source.pitch = 1f;
                source.time = position;
                source.Play();
                audio.musicFade = 0f;
            }
            else if (seek)
            {
                source.time = position;
                if (!source.isPlaying) source.Play();
                audio.musicFade = 0f;
            }
            if (newHalf || sectionChanged)
                AutoPilot.Log($"TIMED_MUSIC {clip.name} half={half} sprint={sprint} position={position:0.00} seek={!newHalf && seek}");
            audio.timedHalf = half;
            audio.timedSprint = sprint;
        }

        /// <summary>A level's music for loading, between the halves and over the results (LevelLayout.matchInterlude).</summary>
        public static void SetInterludeMusic(AudioClip clip, bool ducked)
        {
            if (instance == null || clip == null) return;
            GameAudio audio = instance;
            AudioSource source = audio.music;
            audio.timedHalf = 0;
            audio.musicFade = 1f;
            audio.ducked = ducked;
            ApplyVolumes();
            source.loop = true;
            source.pitch = 1f;
            if (source.clip == clip && source.isPlaying) return;
            source.clip = clip;
            source.Play();
            AutoPilot.Log($"LEVEL_INTERLUDE {clip.name}");
        }

        private void Update()
        {
            if (musicFade >= 1f) return;
            musicFade = Mathf.MoveTowards(musicFade, 1f, Time.unscaledDeltaTime / MusicFadeSeconds);
            ApplyVolumes();
        }

        /// <summary>Turns the music down so something else can be heard over it.</summary>
        public static void SetMusicDucked(bool ducked)
        {
            if (instance == null) return;
            instance.ducked = ducked;
            ApplyVolumes();
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
            source.volume = volume * GameSettings.EffectsVolume;
            if (source.clip == clip && source.isPlaying) return;
            source.clip = clip;
            source.Play();
        }
    }
}
