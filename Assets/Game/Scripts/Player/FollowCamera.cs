using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Angled top-down camera that follows the local character. For a moment everyone should watch,
    /// such as the helicopter leaving, another script can take over with <see cref="SetShot"/>.
    /// </summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        private static FollowCamera instance;

        /// <summary>While set, the camera looks here instead of at the character. Only the automatic tests use it.</summary>
        public static Vector3? TestFocus;

        public Vector3 offset = new Vector3(0f, 21f, -15.5f);
        public float smoothTime = 0.14f;
        public float shakeRange = 16f;
        public float shotBlendSeconds = 1.1f;
        public Vector3 fallbackFocus = Vector3.zero;

        private Vector3 focus;
        private Vector3 velocity;
        private float shakeUntil;
        private bool placed;
        private bool shotActive;
        private float shotBlend;
        private Vector3 shotPosition;
        private Vector3 shotLook;
        private Vector3 shotLookTarget;
        private Vector3 shotLookVelocity;

        /// <summary>What the camera is looking at right now, for effects that should always shake it.</summary>
        public static Vector3 Focus => instance != null ? instance.focus : Vector3.zero;

        /// <summary>A short shake when a hit lands close enough to feel.</summary>
        public static void Shake(Vector3 worldPosition)
        {
            if (instance == null) return;
            if ((instance.focus - worldPosition).sqrMagnitude > instance.shakeRange * instance.shakeRange) return;
            instance.shakeUntil = Time.time + 0.18f;
        }

        /// <summary>Moves the camera to this view. Call every frame the view should hold.</summary>
        public static void SetShot(Vector3 position, Vector3 lookAt)
        {
            if (instance == null) return;
            if (!instance.shotActive) instance.shotLook = lookAt;
            instance.shotActive = true;
            instance.shotPosition = position;
            instance.shotLookTarget = lookAt;
        }

        /// <summary>Back to the character straight away. Use it while the screen is black.</summary>
        public static void EndShot()
        {
            if (instance == null) return;
            instance.shotActive = false;
            instance.shotBlend = 0f;
        }

        private void Awake()
        {
            instance = this;
            focus = fallbackFocus;
            if (AutoPilot.CameraZoom > 0f) offset *= AutoPilot.CameraZoom;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void LateUpdate()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            Vector3 target = local != null ? local.transform.position : fallbackFocus;
            if (TestFocus.HasValue) target = TestFocus.Value;
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
            Vector3 position = focus + offset;
            Quaternion rotation = Quaternion.LookRotation(-offset);

            shotBlend = Mathf.MoveTowards(shotBlend, shotActive ? 1f : 0f, Time.deltaTime / shotBlendSeconds);
            if (shotBlend > 0f)
            {
                shotLook = Vector3.SmoothDamp(shotLook, shotLookTarget, ref shotLookVelocity, 0.3f);
                float blend = Mathf.SmoothStep(0f, 1f, shotBlend);
                position = Vector3.Lerp(position, shotPosition, blend);
                rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(shotLook - shotPosition), blend);
            }

            transform.SetPositionAndRotation(position + shake, rotation);
        }
    }
}
