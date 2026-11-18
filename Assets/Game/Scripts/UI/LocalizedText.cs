using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// The fonts for the player's language. The game's font, Lilita One, only has Latin letters, so Chinese is drawn with
    /// a font that comes with the computer: PingFang on a Mac, Microsoft YaHei on Windows. Nothing is shipped for it.
    /// </summary>
    public static class UiFonts
    {
        /// <summary>The first one installed is used. macOS first, then Windows, then Linux.</summary>
        private static readonly string[] ChineseFonts =
        {
            "PingFang SC", "Hiragino Sans GB", "Heiti SC", "STHeiti",
            "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "Microsoft JhengHei", "SimSun",
            "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "WenQuanYi Micro Hei", "Arial Unicode MS",
        };

        private static bool searched;
        private static Font chinese;

        /// <summary>Whether this computer can show Chinese. The settings page only offers Chinese when it can.</summary>
        public static bool HasChinese => Chinese != null;

        public static Font Chinese
        {
            get
            {
                if (!searched && Application.isPlaying)
                {
                    searched = true;
                    chinese = FindChinese();
                }
                return chinese;
            }
        }

        private static Font FindChinese()
        {
            HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            foreach (string name in ChineseFonts)
            {
                if (installed.Contains(name)) names.Add(name);
            }
            if (names.Count == 0)
            {
                Debug.Log("[UiFonts] No font with Chinese on this computer, so the game stays in English.");
                return null;
            }
            Font font = Font.CreateDynamicFontFromOSFont(names.ToArray(), 48);
            Debug.Log($"[UiFonts] Chinese is drawn with {names[0]}.");
            return font;
        }

        /// <summary>True if the text has anything Lilita One can't draw: Chinese characters and full-width punctuation.</summary>
        public static bool NeedsChinese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char letter in text)
            {
                if (letter >= '⺀') return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Keeps one label in the player's language. A label built into a scene remembers the English it was made with and
    /// shows it in the player's language, for as long as it still says that (code that writes its own text wins).
    /// Whatever it says, it's drawn with a font that has its letters: Lilita One for English, the computer's Chinese font
    /// for Chinese, in bold to sit with Lilita's weight.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [Tooltip("The English this label was made with, or empty when only code writes it.")]
        public string english;

        private Text label;
        private Font latin;
        private FontStyle latinStyle;
        private string seen;
        private string translated;

        private void Awake()
        {
            label = GetComponent<Text>();
            latin = label.font;
            latinStyle = label.fontStyle;
        }

        private void OnEnable()
        {
            GameSettings.LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            GameSettings.LanguageChanged -= Refresh;
        }

        private void Refresh()
        {
            if (!string.IsNullOrEmpty(english) && (label.text == english || label.text == translated))
            {
                translated = GameText.T(english);
                label.text = translated;
            }
            FitFont();
        }

        private void LateUpdate()
        {
            if (!ReferenceEquals(label.text, seen)) FitFont();
        }

        private void FitFont()
        {
            seen = label.text;
            Font chinese = UiFonts.NeedsChinese(seen) ? UiFonts.Chinese : null;
            Font font = chinese != null ? chinese : latin;
            FontStyle style = chinese != null ? FontStyle.Bold : latinStyle;
            if (label.font != font) label.font = font;
            if (label.fontStyle != style) label.fontStyle = style;
        }
    }
}
