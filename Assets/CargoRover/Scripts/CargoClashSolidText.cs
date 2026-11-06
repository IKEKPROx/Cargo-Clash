using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Uses a complete, heavy system typeface on this Mac without bundling an OS font file.
/// Falls back to the Unity font if the preferred family is unavailable elsewhere.
/// </summary>
[RequireComponent(typeof(Text))]
public sealed class CargoClashSolidText : MonoBehaviour
{
    private static Font titleFont;

    private void Awake()
    {
        Text label = GetComponent<Text>();
        if (titleFont == null)
        {
            string[] installed = Font.GetOSInstalledFontNames();
            string[] choices = { "Impact", "Arial Black", "DIN Condensed", "DIN Alternate" };
            foreach (string candidate in choices)
            {
                string family = Array.Find(installed, name =>
                    name.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);
                if (family == null) continue;
                titleFont = Font.CreateDynamicFontFromOSFont(family, 144);
                break;
            }
        }

        if (titleFont != null) label.font = titleFont;
        label.fontStyle = FontStyle.Bold;
    }
}
