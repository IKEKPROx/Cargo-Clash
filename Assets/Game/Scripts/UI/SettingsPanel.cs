using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>One setting with a value and the arrows either side of it.</summary>
    [Serializable]
    public sealed class SettingChoice
    {
        public Button back;
        public Button next;
        public Text value;
        public CanvasGroup group;

        public void Wire(Action<int> step)
        {
            back.onClick.AddListener(() => step(-1));
            next.onClick.AddListener(() => step(1));
        }

        public void Show(string text, bool usable = true, bool pending = false)
        {
            value.text = text;
            value.color = pending ? SettingsPanel.PendingColour : SettingsPanel.ValueColour;
            group.alpha = usable ? 1f : 0.45f;
            group.interactable = usable;
            group.blocksRaycasts = usable;
        }
    }

    /// <summary>
    /// The settings page, on the start screen (from the main menu) and in a match (from the pause menu). Everything takes
    /// effect at once and is saved on this computer (GameSettings), except the window and resolution, which wait for
    /// Apply so the window doesn't jump around while someone looks through the sizes. Unity remembers the window itself.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        public static readonly Color ValueColour = new Color(0.09f, 0.12f, 0.24f);
        public static readonly Color PendingColour = new Color(0.86f, 0.42f, 0.05f);

        public GameObject panel;
        public Button backButton;

        [Header("Game")]
        public SettingChoice language;
        public SettingChoice view;
        public Slider lookSpeed;
        public Text lookSpeedValue;
        public SettingChoice invertY;

        [Header("Display")]
        public SettingChoice window;
        public SettingChoice resolution;
        public Button applyButton;
        public CanvasGroup applyGroup;
        public SettingChoice vSync;
        public SettingChoice maxFps;
        public SettingChoice graphics;

        [Header("Sound")]
        public Slider masterVolume;
        public Text masterValue;
        public Slider musicVolume;
        public Text musicValue;
        public Slider effectsVolume;
        public Text effectsValue;

        public Text note;

        private readonly List<Vector2Int> sizes = new List<Vector2Int>();
        private int pendingSize;
        private bool pendingFullScreen;
        private Vector2Int readSize;
        private bool readFullScreen;
        private bool wired;

        public bool IsOpen => panel.activeSelf;

        /// <summary>The page was closed with Back (or Esc, which its owner passes on).</summary>
        public event Action Closed;

        private void Awake()
        {
            Wire();
            panel.SetActive(false);
        }

        private void OnEnable()
        {
            GameSettings.Changed += Refresh;
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Refresh;
        }

        private void Wire()
        {
            if (wired) return;
            wired = true;
            backButton.onClick.AddListener(Close);
            language.Wire(_ => GameSettings.Language = GameSettings.Language == Language.English ? Language.Chinese : Language.English);
            view.Wire(_ => GameSettings.FirstPerson = !GameSettings.FirstPerson);
            invertY.Wire(_ => GameSettings.InvertLookY = !GameSettings.InvertLookY);
            window.Wire(_ =>
            {
                pendingFullScreen = !pendingFullScreen;
                Refresh();
            });
            resolution.Wire(step =>
            {
                if (sizes.Count == 0) return;
                pendingSize = (pendingSize + step + sizes.Count) % sizes.Count;
                Refresh();
            });
            applyButton.onClick.AddListener(ApplyDisplay);
            vSync.Wire(_ => GameSettings.VSync = !GameSettings.VSync);
            maxFps.Wire(step =>
            {
                int[] caps = GameSettings.FrameCaps;
                int index = Mathf.Max(Array.IndexOf(caps, GameSettings.MaxFps), 0);
                GameSettings.MaxFps = caps[(index + step + caps.Length) % caps.Length];
            });
            // The arrows go down and up the list High, Medium, Low.
            graphics.Wire(step => GameSettings.Graphics = (GraphicsLevel)(((int)GameSettings.Graphics + step + 3) % 3));

            lookSpeed.minValue = GameSettings.MinLookSpeed;
            lookSpeed.maxValue = GameSettings.MaxLookSpeed;
            lookSpeed.onValueChanged.AddListener(value => GameSettings.LookSpeed = value);
            masterVolume.onValueChanged.AddListener(value => GameSettings.MasterVolume = value);
            musicVolume.onValueChanged.AddListener(value => GameSettings.MusicVolume = value);
            effectsVolume.onValueChanged.AddListener(value => GameSettings.EffectsVolume = value);
        }

        public void Open()
        {
            Wire();
            ReadDisplay();
            panel.SetActive(true);
            Refresh();
        }

        public void Close()
        {
            if (!panel.activeSelf) return;
            panel.SetActive(false);
            GameSettings.Save();
            Closed?.Invoke();
        }

        /// <summary>The sizes this screen offers, smallest first, and which of them the window is now.</summary>
        private void ReadDisplay()
        {
            sizes.Clear();
            foreach (Resolution mode in Screen.resolutions)
            {
                Vector2Int size = new Vector2Int(mode.width, mode.height);
                if (size.x >= 800 && size.y >= 600 && !sizes.Contains(size)) sizes.Add(size);
            }
            Vector2Int now = new Vector2Int(Screen.width, Screen.height);
            if (!sizes.Contains(now)) sizes.Add(now);
            sizes.Sort((a, b) => a.x * a.y != b.x * b.y ? (a.x * a.y).CompareTo(b.x * b.y) : a.x.CompareTo(b.x));
            pendingSize = sizes.IndexOf(now);
            pendingFullScreen = Screen.fullScreenMode != FullScreenMode.Windowed;
            readSize = now;
            readFullScreen = pendingFullScreen;
        }

        private bool DisplayPending
        {
            get
            {
                if (sizes.Count == 0) return false;
                bool fullScreen = Screen.fullScreenMode != FullScreenMode.Windowed;
                return pendingFullScreen != fullScreen || sizes[pendingSize] != new Vector2Int(Screen.width, Screen.height);
            }
        }

        private void ApplyDisplay()
        {
            if (sizes.Count == 0) return;
            Vector2Int size = sizes[pendingSize];
            Screen.SetResolution(size.x, size.y, pendingFullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            AutoPilot.Log($"SETTINGS display {size.x}x{size.y} fullScreen={pendingFullScreen}");
            Refresh();
        }

        private void Update()
        {
            // When the window changes, whether from Apply, Alt+Enter or dragging its edge, the page starts again from it.
            if (!IsOpen || Time.frameCount % 15 != 0) return;
            bool fullScreen = Screen.fullScreenMode != FullScreenMode.Windowed;
            if (new Vector2Int(Screen.width, Screen.height) == readSize && fullScreen == readFullScreen) return;
            ReadDisplay();
            Refresh();
        }

        private void Refresh()
        {
            if (!wired || !panel.activeSelf) return;
            language.Show(GameText.LanguageName(GameSettings.Language), UiFonts.HasChinese);
            view.Show(GameSettings.FirstPerson ? GameText.FirstPersonView : GameText.ShoulderView);
            invertY.Show(GameSettings.InvertLookY ? GameText.On : GameText.Off);
            lookSpeed.SetValueWithoutNotify(GameSettings.LookSpeed);
            lookSpeedValue.text = GameSettings.LookSpeed.ToString("0.00") + "×";

            vSync.Show(GameSettings.VSync ? GameText.On : GameText.Off);
            maxFps.Show(GameSettings.VSync ? GameText.FollowsVSync : GameSettings.MaxFps <= 0 ? GameText.NoLimit : GameText.Fps(GameSettings.MaxFps), !GameSettings.VSync);
            graphics.Show(GameText.GraphicsName(GameSettings.Graphics));

            ShowVolume(masterVolume, masterValue, GameSettings.MasterVolume);
            ShowVolume(musicVolume, musicValue, GameSettings.MusicVolume);
            ShowVolume(effectsVolume, effectsValue, GameSettings.EffectsVolume);
            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            if (sizes.Count == 0) return;
            bool fullScreen = Screen.fullScreenMode != FullScreenMode.Windowed;
            Vector2Int size = sizes[pendingSize];
            window.Show(pendingFullScreen ? GameText.FullScreen : GameText.Windowed, true, pendingFullScreen != fullScreen);
            resolution.Show(GameText.Resolution(size.x, size.y), true, size != new Vector2Int(Screen.width, Screen.height));
            bool pending = DisplayPending;
            applyButton.interactable = pending;
            applyGroup.alpha = pending ? 1f : 0.45f;
            string message = pending ? GameText.DisplayPending : GameText.SettingsSaved;
            if (note.text != message) note.text = message;
        }

        private static void ShowVolume(Slider slider, Text value, float volume)
        {
            slider.SetValueWithoutNotify(volume);
            value.text = Mathf.RoundToInt(volume * 100f) + "%";
        }
    }
}
