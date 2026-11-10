using UnityEngine;

namespace CargoClash
{
    /// <summary>Gently bobs, tilts and pulses a logo or button, so the screen never looks frozen.</summary>
    public sealed class UiFloat : MonoBehaviour
    {
        public float bobHeight = 8f;
        public float tiltDegrees = 1f;
        public float pulse = 0.015f;
        public float seconds = 3f;

        private RectTransform rect;
        private Vector2 rest;

        private void Awake()
        {
            rect = (RectTransform)transform;
            rest = rect.anchoredPosition;
        }

        private void Update()
        {
            float angle = Time.unscaledTime * Mathf.PI * 2f / seconds;
            rect.anchoredPosition = rest + Vector2.up * (Mathf.Sin(angle) * bobHeight);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(angle * 0.5f) * tiltDegrees);
            rect.localScale = Vector3.one * (1f + Mathf.Sin(angle * 2f) * pulse);
        }
    }
}
