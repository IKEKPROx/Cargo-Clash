using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>One card on the level select. It shows whatever GameConfig says about its level.</summary>
    public sealed class LevelCard : MonoBehaviour
    {
        private const float ShakeSeconds = 0.35f;
        private const float ShakeSize = 12f;
        private const float ShakeSpeed = 50f;

        public Button button;
        public UiButtonFeel feel;
        public Image picture;
        public Text number;
        public Text title;
        public GameObject lockedCover;

        private RectTransform rect;
        private Vector2 rest;
        private float shaking;

        private void Awake()
        {
            rect = (RectTransform)transform;
            rest = rect.anchoredPosition;
        }

        public void Show(int index, LevelInfo level)
        {
            number.text = (index + 1).ToString();
            title.text = level.title;
            picture.sprite = level.picture;
            lockedCover.SetActive(!level.IsReady);
            feel.clickSound = level.IsReady ? "click" : "locked";
        }

        /// <summary>Says no when a locked card is clicked.</summary>
        public void Shake()
        {
            shaking = ShakeSeconds;
        }

        private void Update()
        {
            if (shaking <= 0f) return;
            shaking = Mathf.Max(shaking - Time.unscaledDeltaTime, 0f);
            float size = ShakeSize * shaking / ShakeSeconds;
            rect.anchoredPosition = rest + Vector2.right * (Mathf.Sin(shaking * ShakeSpeed) * size);
        }
    }
}
