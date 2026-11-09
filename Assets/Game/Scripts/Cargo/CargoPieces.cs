using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// What's left of a piece of cargo when it breaks. Each computer throws its own copy apart, so it's
    /// only for show and nothing about it goes over the network.
    /// </summary>
    public sealed class CargoPieces : MonoBehaviour
    {
        public float lifetime = 3.4f;
        public float shrinkSeconds = 0.55f;
        public float blast = 3.6f;
        public float upward = 3.4f;
        public float spin = 10f;
        [Tooltip("How much of the hammer's direction goes into the pieces.")]
        public float follow = 1.6f;

        private float age;

        public static void Burst(GameObject prefab, Vector3 position, Quaternion rotation, Vector3 push)
        {
            if (prefab == null || Application.isBatchMode) return;
            GameObject copy = Instantiate(prefab, position, rotation);
            copy.GetComponent<CargoPieces>().Throw(position, push);
        }

        private void Throw(Vector3 centre, Vector3 push)
        {
            foreach (Rigidbody piece in GetComponentsInChildren<Rigidbody>())
            {
                Vector3 outward = piece.position - centre;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Random.onUnitSphere;
                outward += Random.insideUnitSphere * 0.5f;
                piece.linearVelocity = outward * (blast * Random.Range(0.6f, 1.25f)) +
                    Vector3.up * (upward * Random.Range(0.45f, 1.15f)) + push * follow;
                piece.angularVelocity = Random.insideUnitSphere * spin;
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            float left = lifetime - age;
            if (left <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (left < shrinkSeconds)
            {
                // Each piece shrinks where it lies, rather than the whole pile shrinking toward one spot.
                float scale = left / shrinkSeconds;
                foreach (Transform piece in transform) piece.localScale = Vector3.one * scale;
            }
        }
    }
}
