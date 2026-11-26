using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// Makes a button feel chunky: it grows under the pointer, its face sinks when pressed, and it
    /// turns gray when it can't be used. Lives on the object with the Button. The main menu's plates
    /// (step 25) sink as a whole (`sink`) and dim part by part, each to its own colour (`dimmed`).
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class UiButtonFeel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private static readonly Color DisabledFace = new Color(0.66f, 0.69f, 0.76f);
        private static readonly Color DisabledLip = new Color(0.44f, 0.47f, 0.54f);
        private const float Response = 18f;

        [Tooltip("The top of the button, which sinks onto its base when pressed. Cards have none.")]
        public Image face;
        public Image lip;
        [Tooltip("What sinks when pressed, when it isn't the face: a main-menu plate sinks whole onto its shadow.")]
        public RectTransform sink;
        [Tooltip("Parts that take another colour while the button can't be used, with the colour each takes.")]
        public Graphic[] dimmed;
        public Color[] dimmedColors;
        public float hoverScale = 1.05f;
        public float pressDepth = 8f;
        [Tooltip("A slow pulse in size, for the button the player should press first.")]
        public float pulse;
        public string clickSound = "click";

        private Button button;
        private RectTransform moving;
        private Vector2 movingRest;
        private Color faceColor;
        private Color lipColor;
        private Color[] restColors;
        private bool hovered;
        private bool pressed;
        private bool looksEnabled = true;

        private void Awake()
        {
            button = GetComponent<Button>();
            moving = sink != null ? sink : face != null ? face.rectTransform : null;
            if (moving != null) movingRest = moving.anchoredPosition;
            if (face != null)
            {
                faceColor = face.color;
                lipColor = lip.color;
            }
            if (dimmed != null)
            {
                restColors = new Color[dimmed.Length];
                for (int i = 0; i < dimmed.Length; i++) restColors[i] = dimmed[i].color;
            }
            button.onClick.AddListener(() => GameAudio.Play(clickSound, 0.8f));
        }

        private void OnDisable()
        {
            hovered = false;
            pressed = false;
            transform.localScale = Vector3.one;
            if (moving != null) moving.anchoredPosition = movingRest;
        }

        public void OnPointerEnter(PointerEventData eventData) => hovered = true;

        public void OnPointerExit(PointerEventData eventData) => hovered = false;

        public void OnPointerDown(PointerEventData eventData) => pressed = true;

        public void OnPointerUp(PointerEventData eventData) => pressed = false;

        private void Update()
        {
            bool active = button.interactable;
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime * Response);
            float size = active && hovered ? hoverScale : 1f + Mathf.Sin(Time.unscaledTime * 3.4f) * pulse;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * size, blend);
            if (moving == null) return;

            Vector2 target = movingRest + (active && pressed ? Vector2.down * pressDepth : Vector2.zero);
            moving.anchoredPosition = Vector2.Lerp(moving.anchoredPosition, target, blend);
            if (active == looksEnabled) return;
            looksEnabled = active;
            if (face != null)
            {
                face.color = active ? faceColor : DisabledFace;
                lip.color = active ? lipColor : DisabledLip;
            }
            if (dimmed == null) return;
            for (int i = 0; i < dimmed.Length; i++) dimmed[i].color = active ? restColors[i] : dimmedColors[i];
        }
    }
}
