using System.IO;
using CargoClash;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The settings page (SettingsPanel), built the same on the start screen and in every level's match interface, and the
/// game's app icon.
/// </summary>
public static partial class CargoClashBuilder
{
    private static readonly Color UiSection = new Color(0.86f, 0.42f, 0.05f);
    private static readonly Color UiHint = new Color(0.34f, 0.37f, 0.48f);

    /// <summary>
    /// Two cream columns under a heading: the game and sound on the left, the display on the right. Laid out within
    /// 1600 wide, so it fits the match interface in a 4:3 window too. Starts hidden; SettingsPanel.Open shows it. In a
    /// match the background is darker, so the scoreboard and the map don't show through around the heading.
    /// </summary>
    private static SettingsPanel BuildSettingsPanel(Transform canvas, float dimAlpha = 0.72f)
    {
        RectTransform holder = UiPanelRoot(canvas, "Settings");
        SettingsPanel settings = holder.gameObject.AddComponent<SettingsPanel>();
        RectTransform page = UiPanelRoot(holder, "Page");
        settings.panel = page.gameObject;

        Image dim = UiImage(page, "Dim", null, new Color(UiNavy.r, UiNavy.g, UiNavy.b, dimAlpha));
        Fill(dim.rectTransform);
        dim.raycastTarget = true;
        AddTape(page, "TapeTop", true);
        AddTape(page, "TapeBottom", false);

        RectTransform headingSlot = PopSlot(page, "Heading", AnchorTop, new Vector2(0f, -116f), new Vector2(1100f, 110f), 0f);
        Text heading = UiText(headingSlot, "Text", GameText.Settings, 88, UiYellow, 4f);
        Fill(heading.rectTransform);
        settings.backButton = UiButton(page, "Back", GameText.Back, new Vector2(0f, 1f), new Vector2(170f, -116f), new Vector2(250f, 96f), UiBlue, 38, 0f,
            out _, UiSprite("UI_Back"));

        Vector2 columnSize = new Vector2(760f, 700f);
        RectTransform left = UiPanel(page, "Left", AnchorCentre, new Vector2(-400f, -26f), columnSize, UiCream, 0.05f, 1.6f);
        SettingsHeading(left, "Game", GameText.SettingsGame, 300f);
        settings.language = SettingsChoiceRow(left, "Language", GameText.SettingLanguage, 238f, 0.08f);
        settings.view = SettingsChoiceRow(left, "View", GameText.SettingCamera, 168f, 0.1f);
        settings.lookSpeed = SettingsSliderRow(left, "LookSpeed", GameText.LookSpeed, 98f, out settings.lookSpeedValue);
        settings.invertY = SettingsChoiceRow(left, "InvertY", GameText.SettingInvertY, 28f, 0.12f);
        SettingsHeading(left, "Sound", GameText.SettingsSound, -50f);
        settings.masterVolume = SettingsSliderRow(left, "Volume", GameText.SettingMaster, -112f, out settings.masterValue);
        settings.musicVolume = SettingsSliderRow(left, "Music", GameText.SettingMusic, -182f, out settings.musicValue);
        settings.effectsVolume = SettingsSliderRow(left, "Effects", GameText.SettingEffects, -252f, out settings.effectsValue);

        RectTransform right = UiPanel(page, "Right", AnchorCentre, new Vector2(400f, -26f), columnSize, UiCream, 0.1f, 1.6f);
        SettingsHeading(right, "Display", GameText.SettingsDisplay, 300f);
        settings.window = SettingsChoiceRow(right, "Window", GameText.SettingWindow, 238f, 0.13f);
        settings.resolution = SettingsChoiceRow(right, "Resolution", GameText.SettingResolution, 168f, 0.15f);
        settings.applyButton = UiButton(right, "Apply", GameText.Apply, AnchorCentre, new Vector2(150f, 98f), new Vector2(248f, 60f), UiOrange, 32, 0.17f, out _);
        settings.applyGroup = settings.applyButton.transform.parent.gameObject.AddComponent<CanvasGroup>();
        settings.vSync = SettingsChoiceRow(right, "VSync", GameText.SettingVSync, 28f, 0.19f);
        settings.maxFps = SettingsChoiceRow(right, "MaxFps", GameText.SettingMaxFps, -42f, 0.21f);
        settings.graphics = SettingsChoiceRow(right, "Graphics", GameText.SettingGraphics, -112f, 0.23f);

        settings.note = UiText(right, "Note", GameText.SettingsSaved, 26, UiHint);
        Rect(settings.note.gameObject, right, AnchorCentre, new Vector2(0f, -252f), new Vector2(700f, 44f));
        settings.note.horizontalOverflow = HorizontalWrapMode.Wrap;

        page.gameObject.SetActive(false);
        EditorUtility.SetDirty(settings);
        return settings;
    }

    private static void SettingsHeading(RectTransform column, string name, string text, float y)
    {
        Text heading = UiText(column, name + "Heading", text, 36, UiSection);
        heading.alignment = TextAnchor.MiddleLeft;
        Rect(heading.gameObject, column, AnchorCentre, new Vector2(0f, y), new Vector2(700f, 50f));
    }

    /// <summary>A label, then the value between a back and a next arrow.</summary>
    private static SettingChoice SettingsChoiceRow(RectTransform column, string name, string label, float y, float delay)
    {
        RectTransform row = UiGroup(column, name, AnchorCentre, new Vector2(0f, y), new Vector2(720f, 64f));
        SettingChoice choice = new SettingChoice { group = row.gameObject.AddComponent<CanvasGroup>() };
        SettingsLabel(row, label);
        choice.back = UiButton(row, "Back", "<", AnchorCentre, new Vector2(-20f, 0f), new Vector2(72f, 56f), UiBlue, 36, delay, out _);
        choice.value = UiText(row, "Value", "", 28, UiNavy);
        Rect(choice.value.gameObject, row, AnchorCentre, new Vector2(150f, 0f), new Vector2(252f, 56f));
        choice.next = UiButton(row, "Next", ">", AnchorCentre, new Vector2(320f, 0f), new Vector2(72f, 56f), UiBlue, 36, delay, out _);
        return choice;
    }

    /// <summary>A label, a slider, and its value as a number.</summary>
    private static Slider SettingsSliderRow(RectTransform column, string name, string label, float y, out Text value)
    {
        RectTransform row = UiGroup(column, name, AnchorCentre, new Vector2(0f, y), new Vector2(720f, 64f));
        SettingsLabel(row, label);
        Slider slider = UiSlider(row, "Slider", AnchorCentre, new Vector2(102f, 0f), new Vector2(300f, 44f));
        slider.minValue = 0f;
        slider.maxValue = 1f;
        value = UiText(row, "Value", "", 28, UiNavy);
        value.alignment = TextAnchor.MiddleRight;
        Rect(value.gameObject, row, AnchorCentre, new Vector2(312f, 0f), new Vector2(96f, 56f));
        return slider;
    }

    private static void SettingsLabel(RectTransform row, string label)
    {
        Text text = UiText(row, "Label", label, 30, UiNavy);
        text.alignment = TextAnchor.MiddleLeft;
        Rect(text.gameObject, row, AnchorCentre, new Vector2(-212f, 0f), new Vector2(276f, 56f));
    }

    // ---- The app icon ----

    private const string AppIconPath = UiDir + "/AppIcon.png";
    /// <summary>The same picture on Apple's icon grid, which CargoClashPlayerBuild.Mac puts into the app.</summary>
    public const string MacAppIconPath = UiDir + "/AppIcon_Mac.png";

    /// <summary>
    /// The icon Unity gives the Windows build (and any other). Made by Tools/blender/make_ui.py (-- --only appicon):
    /// Astra's goose crate in front of the hammer and the vacuum, in the two teams' colours.
    /// </summary>
    private static void SetAppIcon()
    {
        foreach (string path in new[] { AppIconPath, MacAppIconPath })
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) continue;
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 1024;
            importer.SaveAndReimport();
        }
        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
        if (icon == null)
        {
            Debug.LogError($"[Builder] {AppIconPath} is missing. Run make_ui.py with -- --only appicon.");
            return;
        }
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
    }

    public static string MacAppIconFullPath => Path.GetFullPath(MacAppIconPath);
}
