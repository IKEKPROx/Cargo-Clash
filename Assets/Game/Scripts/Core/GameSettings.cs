using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CargoClash
{
    public enum Language : byte { English = 0, Chinese = 1 }

    public enum GraphicsLevel : byte { High = 0, Medium = 1, Low = 2 }

    /// <summary>
    /// What each player sets for themselves on the settings page. Kept on their own computer, and never affects the rules.
    /// Automatic tests start from the defaults (except the crew look, the look speed and the view, which they read as
    /// saved) and never save what they change. The window's size and full screen aren't here: Unity keeps those itself.
    /// </summary>
    public static class GameSettings
    {
        public const float MinLookSpeed = 0.25f;
        public const float MaxLookSpeed = 3f;
        /// <summary>The choices for the frame rate limit without VSync. 0 is no limit.</summary>
        public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144, 240 };

        private const string LookSpeedKey = "cc.lookSpeed";
        private const string FirstPersonKey = "cc.firstPerson";
        private const string CrewKey = "cc.crew";

        private static readonly IntSetting language = new IntSetting("cc.language", -1);
        private static readonly IntSetting invertLookY = new IntSetting("cc.invertLookY", 0);
        private static readonly IntSetting vSync = new IntSetting("cc.vsync", 1);
        private static readonly IntSetting maxFps = new IntSetting("cc.maxFps", 0);
        private static readonly IntSetting graphics = new IntSetting("cc.graphics", (int)GraphicsLevel.High);
        private static readonly FloatSetting masterVolume = new FloatSetting("cc.volume.master", 1f);
        private static readonly FloatSetting musicVolume = new FloatSetting("cc.volume.music", 1f);
        private static readonly FloatSetting effectsVolume = new FloatSetting("cc.volume.effects", 1f);

        private static float lookSpeed = -1f;
        private static int firstPerson = -1;
        private static CrewLook? look;
        private static PipelineDefaults pipelineDefaults;

        /// <summary>Any setting changed. The settings page and anything that shows a setting listen for this.</summary>
        public static event Action Changed;

        /// <summary>The language changed, so every label shows its words again.</summary>
        public static event Action LanguageChanged;

        /// <summary>A test run, which starts from the defaults and saves nothing.</summary>
        private static bool Testing => AutoPilot.Requested;

        /// <summary>The body type, hat, glasses and face picked on the crew page, for every match after.</summary>
        public static CrewLook Look
        {
            get
            {
                look ??= CrewLook.Load(PlayerPrefs.GetString(CrewKey, ""));
                return look.Value;
            }
            set
            {
                look = value.Sanitized();
                if (!Testing) PlayerPrefs.SetString(CrewKey, look.Value.Save());
            }
        }

        /// <summary>Scales how far the view turns for a given mouse movement. 1 is the default.</summary>
        public static float LookSpeed
        {
            get
            {
                if (lookSpeed < 0f) lookSpeed = Mathf.Clamp(PlayerPrefs.GetFloat(LookSpeedKey, 1f), MinLookSpeed, MaxLookSpeed);
                return lookSpeed;
            }
            set
            {
                lookSpeed = Mathf.Clamp(value, MinLookSpeed, MaxLookSpeed);
                if (!Testing) PlayerPrefs.SetFloat(LookSpeedKey, lookSpeed);
                Changed?.Invoke();
            }
        }

        public static bool FirstPerson
        {
            get
            {
                if (firstPerson < 0) firstPerson = PlayerPrefs.GetInt(FirstPersonKey, 1);
                return firstPerson == 1;
            }
            set
            {
                firstPerson = value ? 1 : 0;
                if (!Testing) PlayerPrefs.SetInt(FirstPersonKey, firstPerson);
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// The words on screen. Until someone picks one it follows the computer's language. Chinese needs a font with
        /// Chinese in it on the computer (UiFonts); without one the game stays in English. A test uses English unless
        /// it passes -ccLanguage zh.
        /// </summary>
        public static Language Language
        {
            get
            {
                int stored = language.Value;
                Language wanted = stored >= 0 ? (Language)stored : Testing ? TestLanguage() : SystemLanguage();
                return wanted == Language.Chinese && !UiFonts.HasChinese ? Language.English : wanted;
            }
            set
            {
                if (value == Language) return;
                language.Value = (int)value;
                LanguageChanged?.Invoke();
                Changed?.Invoke();
            }
        }

        /// <summary>Moving the mouse forward looks down instead of up.</summary>
        public static bool InvertLookY
        {
            get => invertLookY.Value == 1;
            set => Set(invertLookY, value ? 1 : 0);
        }

        public static bool VSync
        {
            get => vSync.Value == 1;
            set
            {
                Set(vSync, value ? 1 : 0);
                ApplyFrameRate();
            }
        }

        /// <summary>Frames per second when VSync is off. 0 is no limit.</summary>
        public static int MaxFps
        {
            get => Array.IndexOf(FrameCaps, maxFps.Value) >= 0 ? maxFps.Value : 0;
            set
            {
                Set(maxFps, value);
                ApplyFrameRate();
            }
        }

        public static GraphicsLevel Graphics
        {
            get => (GraphicsLevel)Mathf.Clamp(graphics.Value, 0, 2);
            set
            {
                Set(graphics, (int)value);
                ApplyGraphics();
            }
        }

        public static float MasterVolume
        {
            get => Mathf.Clamp01(masterVolume.Value);
            set
            {
                Set(masterVolume, Mathf.Clamp01(value));
                ApplyVolume();
            }
        }

        public static float MusicVolume
        {
            get => Mathf.Clamp01(musicVolume.Value);
            set
            {
                Set(musicVolume, Mathf.Clamp01(value));
                ApplyVolume();
            }
        }

        public static float EffectsVolume
        {
            get => Mathf.Clamp01(effectsVolume.Value);
            set
            {
                Set(effectsVolume, Mathf.Clamp01(value));
                ApplyVolume();
            }
        }

        /// <summary>Puts the saved frame rate, graphics and volume into effect when the game starts.</summary>
        public static void ApplyAtStart()
        {
            ApplyFrameRate();
            ApplyGraphics();
            ApplyVolume();
        }

        /// <summary>Writes the saved settings to disk now, rather than only when the game quits.</summary>
        public static void Save()
        {
            if (!Testing) PlayerPrefs.Save();
        }

        private static void ApplyFrameRate()
        {
            // Without a window, a test sets its own frame rate (AutoPilot.TryStart).
            if (Application.isBatchMode) return;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || MaxFps <= 0 ? -1 : MaxFps;
        }

        /// <summary>
        /// Medium and low draw fewer pixels and shorter, simpler shadows. Only in a built game: in the Editor this would
        /// change the project's own render pipeline asset.
        /// </summary>
        private static void ApplyGraphics()
        {
            if (Application.isEditor) return;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            pipelineDefaults ??= new PipelineDefaults
            {
                renderScale = pipeline.renderScale,
                shadowDistance = pipeline.shadowDistance,
                cascades = pipeline.shadowCascadeCount,
            };
            switch (Graphics)
            {
                case GraphicsLevel.Low:
                    pipeline.renderScale = 0.7f;
                    pipeline.shadowDistance = Mathf.Min(pipelineDefaults.shadowDistance, 25f);
                    pipeline.shadowCascadeCount = 1;
                    break;
                case GraphicsLevel.Medium:
                    pipeline.renderScale = 0.85f;
                    pipeline.shadowDistance = Mathf.Min(pipelineDefaults.shadowDistance, 40f);
                    pipeline.shadowCascadeCount = Mathf.Min(pipelineDefaults.cascades, 2);
                    break;
                default:
                    pipeline.renderScale = pipelineDefaults.renderScale;
                    pipeline.shadowDistance = pipelineDefaults.shadowDistance;
                    pipeline.shadowCascadeCount = pipelineDefaults.cascades;
                    break;
            }
        }

        /// <summary>How much of the screen's pixels the game draws now. For tests.</summary>
        public static float RenderScale =>
            GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline ? pipeline.renderScale : 1f;

        private static void ApplyVolume()
        {
            AudioListener.volume = MasterVolume;
            GameAudio.ApplyVolumes();
        }

        private static void Set(IntSetting setting, int value)
        {
            if (setting.Value == value) return;
            setting.Value = value;
            Changed?.Invoke();
        }

        private static void Set(FloatSetting setting, float value)
        {
            if (Mathf.Approximately(setting.Value, value)) return;
            setting.Value = value;
            Changed?.Invoke();
        }

        private static Language SystemLanguage()
        {
            SystemLanguage system = Application.systemLanguage;
            return system == UnityEngine.SystemLanguage.Chinese || system == UnityEngine.SystemLanguage.ChineseSimplified ||
                system == UnityEngine.SystemLanguage.ChineseTraditional ? Language.Chinese : Language.English;
        }

        private static Language TestLanguage() =>
            string.Equals(AutoPilot.Argument("-ccLanguage"), "zh", StringComparison.OrdinalIgnoreCase) ? Language.Chinese : Language.English;

        private sealed class PipelineDefaults
        {
            public float renderScale;
            public float shadowDistance;
            public int cascades;
        }

        /// <summary>A whole number kept in PlayerPrefs, read once. A test keeps its own copy at the default.</summary>
        private sealed class IntSetting
        {
            private readonly string key;
            private readonly int fallback;
            private bool loaded;
            private int value;

            public IntSetting(string key, int fallback)
            {
                this.key = key;
                this.fallback = fallback;
            }

            public int Value
            {
                get
                {
                    if (!loaded) value = Testing ? fallback : PlayerPrefs.GetInt(key, fallback);
                    loaded = true;
                    return value;
                }
                set
                {
                    this.value = value;
                    loaded = true;
                    if (!Testing) PlayerPrefs.SetInt(key, value);
                }
            }
        }

        private sealed class FloatSetting
        {
            private readonly string key;
            private readonly float fallback;
            private bool loaded;
            private float value;

            public FloatSetting(string key, float fallback)
            {
                this.key = key;
                this.fallback = fallback;
            }

            public float Value
            {
                get
                {
                    if (!loaded) value = Testing ? fallback : PlayerPrefs.GetFloat(key, fallback);
                    loaded = true;
                    return value;
                }
                set
                {
                    this.value = value;
                    loaded = true;
                    if (!Testing) PlayerPrefs.SetFloat(key, value);
                }
            }
        }
    }
}
