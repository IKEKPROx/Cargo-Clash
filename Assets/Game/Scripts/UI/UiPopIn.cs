using UnityEngine;

namespace CargoClash
{
    /// <summary>Springs in every time it is shown. A delay lets a group of these land one after another.</summary>
    public sealed class UiPopIn : MonoBehaviour
    {
        private const float Overshoot = 1.9f;

        public float delay;
        public float duration = 0.36f;

        private float age;
        private bool settled;

        private void OnEnable()
        {
            age = 0f;
            settled = false;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (settled) return;
            age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01((age - delay) / duration);
            float eased = 1f + (Overshoot + 1f) * Mathf.Pow(t - 1f, 3f) + Overshoot * Mathf.Pow(t - 1f, 2f);
            transform.localScale = Vector3.one * eased;
            settled = t >= 1f;
        }
    }
}
