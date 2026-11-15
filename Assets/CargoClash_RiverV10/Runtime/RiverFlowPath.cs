using UnityEngine;

namespace CargoClash.RiverV10
{
    [CreateAssetMenu(menuName = "Cargo Clash/River V10/Flow Path")]
    public sealed class RiverFlowPath : ScriptableObject
    {
        public Vector3[] centres;
        public float[] halfWidths;
        [Tooltip("Reference speed only. This asset never applies forces or changes cargo state.")]
        public float suggestedCargoSpeed = 0.35f;

        public bool TrySample(Transform levelRoot, Vector3 worldPoint, out Vector3 surface, out Vector3 direction)
        {
            Vector3 p = levelRoot.InverseTransformPoint(worldPoint);
            surface = Vector3.zero; direction = Vector3.zero;
            float best = float.PositiveInfinity, width = 0f;
            for (int i = 0; i + 1 < centres.Length; ++i)
            {
                Vector3 a = centres[i], delta = centres[i + 1] - a;
                Vector3 flat = new Vector3(delta.x, 0, delta.z);
                float t = Mathf.Clamp01(Vector3.Dot(p - a, flat) / flat.sqrMagnitude);
                Vector3 nearest = a + delta * t;
                float distance = new Vector2(p.x - nearest.x, p.z - nearest.z).sqrMagnitude;
                if (distance >= best) continue;
                best = distance; surface = nearest; direction = flat.normalized;
                width = Mathf.Lerp(halfWidths[i], halfWidths[i + 1], t);
            }
            surface = levelRoot.TransformPoint(surface);
            direction = levelRoot.TransformDirection(direction).normalized;
            return best <= width * width;
        }
    }
}
