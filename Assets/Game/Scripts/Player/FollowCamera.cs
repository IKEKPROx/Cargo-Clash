using UnityEngine;

namespace CargoClash
{
    /// <summary>Angled top-down camera that follows the local character.</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        private static FollowCamera instance;

        public Vector3 offset = new Vector3(0f, 21f, -15.5f);
        public float smoothTime = 0.14f;
        public float shakeRange = 16f;
        public Vector3 fallbackFocus = Vector3.zero;

        private Vector3 focus;
        private Vector3 velocity;
        private float shakeUntil;
        private bool placed;

        /// <summary>A short shake when a hit lands close enough to feel.</summary>
        public static void Shake(Vector3 worldPosition)
        {
            if (instance == null) return;
            if ((instance.focus - worldPosition).sqrMagnitude > instance.shakeRange * instance.shakeRange) return;
            instance.shakeUntil = Time.time + 0.18f;
        }

        private void Awake()
        {
            instance = this;
            focus = fallbackFocus;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void LateUpdate()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            Vector3 target = local != null ? local.transform.position : fallbackFocus;
            target.y = 0f;

            if (!placed && local != null)
            {
                focus = target;
                placed = true;
            }
            else
            {
                focus = Vector3.SmoothDamp(focus, target, ref velocity, smoothTime);
            }

            Vector3 shake = Time.time < shakeUntil ? Random.insideUnitSphere * 0.18f : Vector3.zero;
            transform.position = focus + offset + shake;
            transform.rotation = Quaternion.LookRotation(-offset);
        }
    }
}
