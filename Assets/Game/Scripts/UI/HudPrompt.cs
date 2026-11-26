using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>A plate with a key cap and what the key does, sized to fit its words.</summary>
    public sealed class HudPrompt : MonoBehaviour
    {
        private const float Height = 56f;
        private const float Padding = 20f;
        // The key cap sits inside the plate's navy panel, with a little room above and below (step 30).
        private const float KeyHeight = Height - 18f;

        public RectTransform back;
        public RectTransform keyBack;
        public Text keyText;
        public Text label;

        /// <summary>Leave `key` empty for a line with no key to press.</summary>
        public void Show(string key, string words)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            bool hasKey = !string.IsNullOrEmpty(key);
            if (keyBack.gameObject.activeSelf != hasKey) keyBack.gameObject.SetActive(hasKey);

            float keyWidth = 0f;
            if (hasKey)
            {
                keyText.text = key;
                keyWidth = Mathf.Max(KeyHeight, keyText.preferredWidth + 22f);
                keyBack.sizeDelta = new Vector2(keyWidth, KeyHeight);
            }

            label.text = words;
            float textWidth = label.preferredWidth;
            float gap = hasKey ? 12f : 0f;
            float total = Padding * 2f + keyWidth + gap + textWidth;
            back.sizeDelta = new Vector2(total, Height);
            keyBack.anchoredPosition = new Vector2(-total * 0.5f + Padding * 0.6f + keyWidth * 0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(total * 0.5f - Padding - textWidth * 0.5f, 0f);
            label.rectTransform.sizeDelta = new Vector2(textWidth + 4f, Height);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
