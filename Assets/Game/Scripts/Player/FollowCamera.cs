using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The player's view: through the character's eyes, or from over their shoulder after pressing V.
    /// For a moment everyone should watch, such as the helicopter leaving, another script can take over
    /// with <see cref="SetShot"/>. The hands and held cargo are drawn by a second camera on top of this
    /// one, so they never poke into a wall.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class FollowCamera : MonoBehaviour
    {
        private static FollowCamera instance;

        /// <summary>While set, the camera looks down at this spot. Only the automatic tests use it.</summary>
        public static Vector3? TestFocus;

        /// <summary>While set, the camera stands here at eye height and looks this way (x, z, yaw). Only the automatic tests use it.</summary>
        public static Vector3? TestEye;

        [Header("Parts")]
        public Camera view;
        [Tooltip("Draws the hands and held cargo over everything else.")]
        public Camera handsView;

        [Header("First person")]
        public float firstPersonFov = 64f;
        public float bobHeight = 0.035f;
        public float bobSide = 0.018f;
        [Tooltip("Metres walked for each footstep.")]
        public float stepLength = 1.55f;
        [Tooltip("Extra field of view at the height of a ram.")]
        public float ramFovKick = 11f;
        public float ramRoll = 2.5f;
        [Tooltip("Where your eyes end up when you're knocked flat.")]
        public float knockedEyeHeight = 0.38f;

        [Header("Over the shoulder")]
        public float shoulderDistance = 3.4f;
        public float shoulderHeight = 1.62f;
        public float shoulderSide = 0.42f;
        public float shoulderFov = 58f;
        public float viewBlendSeconds = 0.22f;

        [Header("Shake")]
        public float shakeRange = 18f;
        public float shakeDegrees = 3.2f;
        public float shakeFade = 2.6f;

        [Header("Test and overview camera")]
        public Vector3 offset = new Vector3(0f, 21f, -15.5f);
        public float smoothTime = 0.14f;
        public float overviewFov = 42f;
        public Vector3 fallbackFocus = Vector3.zero;

        [Header("Shots")]
        public float shotBlendSeconds = 1.1f;

        private GameConfig config;
        private Vector3 focus;
        private Vector3 velocity;
        private float trauma;
        private float noiseSeed;
        private float kickPitch;
        private float kickVelocity;
        private float rollNow;
        private float rollVelocity;
        private bool placed;
        private bool shotActive;
        private float shotBlend;
        private Vector3 shotPosition;
        private Vector3 shotLook;
        private Vector3 shotLookTarget;
        private Vector3 shotLookVelocity;

        private float viewBlend;
        private Vector3 lastPosition;
        private float walkPhase;
        private float walkAmount;
        private float dashAmount;
        private float knockedAt = -100f;
        private bool wasKnocked;
        private float hitRoll;

        /// <summary>What the camera is looking at right now, for effects that should always shake it.</summary>
        public static Vector3 Focus => instance != null ? instance.transform.position : Vector3.zero;

        /// <summary>True while the local character's own body would be in the way of the view.</summary>
        public static bool HidesLocalBody => instance != null && instance.InFirstPerson;

        /// <summary>True while the view is through the character's eyes, so the hands should show.</summary>
        public static bool ShowsHands => instance != null && instance.InFirstPerson;

        /// <summary>How far through a walking stride the view is, in radians. The hands swing in time with it.</summary>
        public static float WalkPhase => instance != null ? instance.walkPhase : 0f;

        /// <summary>
        /// Where the arms are in their swing: 1 with the right hand as far forward as it goes, -1 with the left.
        /// An arm is furthest forward when the opposite foot lands, and footsteps fall on whole turns of the phase.
        /// </summary>
        public static float Stride => -Mathf.Cos(WalkPhase);

        /// <summary>From 0 standing still to 1 at a full run.</summary>
        public static float WalkAmount => instance != null ? instance.walkAmount : 0f;

        private bool InFirstPerson => viewBlend < 0.5f && shotBlend < 0.02f && !TestFocus.HasValue && !TestEye.HasValue && PlayerAvatar.Local != null;

        /// <summary>A shake when a hit lands close enough to feel. 1 is the biggest there is.</summary>
        public static void Shake(Vector3 worldPosition, float amount = 0.55f)
        {
            if (instance == null) return;
            float distance = Vector3.Distance(instance.transform.position, worldPosition);
            if (distance > instance.shakeRange) return;
            float scaled = amount * (1f - distance / instance.shakeRange);
            instance.trauma = Mathf.Clamp01(Mathf.Max(instance.trauma, scaled));
        }

        /// <summary>A sharp nod of the view, for your own hammer landing.</summary>
        public static void Kick(float degrees)
        {
            if (instance == null) return;
            instance.kickVelocity += degrees * 14f;
        }

        /// <summary>A light hit: your head snaps back and round. `push` is the way it pushed you, in the world.</summary>
        public static void Flinch(Vector3 push)
        {
            if (instance == null) return;
            instance.kickVelocity -= 26f;
            instance.rollVelocity += Vector3.Dot(push, instance.transform.right) * 170f;
            instance.trauma = Mathf.Max(instance.trauma, 0.5f);
        }

        public static void ToggleView()
        {
            GameSettings.FirstPerson = !GameSettings.FirstPerson;
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
            config = GameConfig.Get();
            focus = fallbackFocus;
            noiseSeed = Random.value * 100f;
            viewBlend = GameSettings.FirstPerson ? 0f : 1f;
            if (AutoPilot.CameraZoom > 0f) offset *= AutoPilot.CameraZoom;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            TestFocus = null;
            TestEye = null;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            PlayerAvatar local = PlayerAvatar.Local;

            Vector3 position;
            Quaternion rotation;
            float fov;
            float handsFov = firstPersonFov;
            if (TestEye.HasValue)
            {
                Vector3 eye = TestEye.Value;
                position = new Vector3(eye.x, config.eyeHeight, eye.y);
                rotation = Quaternion.Euler(4f, eye.z, 0f);
                fov = firstPersonFov;
            }
            else if (local == null || TestFocus.HasValue)
            {
                OverviewPose(local, out position, out rotation);
                fov = overviewFov;
            }
            else
            {
                UpdateWalk(local, deltaTime);
                viewBlend = Mathf.MoveTowards(viewBlend, GameSettings.FirstPerson ? 0f : 1f, deltaTime / viewBlendSeconds);
                float blend = Mathf.SmoothStep(0f, 1f, viewBlend);

                FirstPersonPose(local, deltaTime, out Vector3 eyePosition, out Quaternion eyeRotation);
                ShoulderPose(local, out Vector3 shoulderPosition, out Quaternion shoulderRotation);
                position = Vector3.Lerp(eyePosition, shoulderPosition, blend);
                rotation = Quaternion.Slerp(eyeRotation, shoulderRotation, blend);

                dashAmount = Mathf.MoveTowards(dashAmount, local.IsDashing ? 1f : 0f, deltaTime * (local.IsDashing ? 12f : 5f));
                fov = Mathf.Lerp(firstPersonFov, shoulderFov, blend) + ramFovKick * Mathf.SmoothStep(0f, 1f, dashAmount);
            }

            shotBlend = Mathf.MoveTowards(shotBlend, shotActive ? 1f : 0f, deltaTime / shotBlendSeconds);
            if (shotBlend > 0f)
            {
                shotLook = Vector3.SmoothDamp(shotLook, shotLookTarget, ref shotLookVelocity, 0.3f);
                float blend = Mathf.SmoothStep(0f, 1f, shotBlend);
                position = Vector3.Lerp(position, shotPosition, blend);
                rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(shotLook - shotPosition), blend);
                fov = Mathf.Lerp(fov, overviewFov, blend);
            }

            transform.SetPositionAndRotation(position, rotation * ShakeRotation(deltaTime));
            view.fieldOfView = fov;
            if (handsView != null) handsView.fieldOfView = handsFov;
        }

        private void OverviewPose(PlayerAvatar local, out Vector3 position, out Quaternion rotation)
        {
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
            position = focus + offset;
            rotation = Quaternion.LookRotation(-offset);
        }

        private void UpdateWalk(PlayerAvatar local, float deltaTime)
        {
            Vector3 moved = local.transform.position - lastPosition;
            moved.y = 0f;
            lastPosition = local.transform.position;
            float speed = deltaTime > 0f ? Mathf.Min(moved.magnitude / deltaTime, config.runSpeed * 1.2f) : 0f;
            walkAmount = Mathf.MoveTowards(walkAmount, Mathf.Clamp01(speed / config.runSpeed), deltaTime * 6f);

            // Two footsteps to each full swing of the sine, so a step lands at each half turn.
            float before = walkPhase;
            walkPhase += speed * deltaTime * Mathf.PI / stepLength;
            bool stepped = Mathf.FloorToInt(before / Mathf.PI) != Mathf.FloorToInt(walkPhase / Mathf.PI);
            if (stepped && speed > 1f && !local.IsKnockedDown) GameAudio.Play("step", 0.16f);
        }

        private void FirstPersonPose(PlayerAvatar local, float deltaTime, out Vector3 position, out Quaternion rotation)
        {
            bool knocked = local.IsKnockedDown;
            if (knocked && !wasKnocked)
            {
                knockedAt = Time.time;
                hitRoll = Random.value < 0.5f ? -1f : 1f;
            }
            wasKnocked = knocked;

            // Falling over and getting up, timed to the length of a knockdown.
            float since = Time.time - knockedAt;
            float length = config.knockdownSeconds;
            float down = 0f;
            if (since < length)
            {
                float fall = Mathf.SmoothStep(0f, 1f, since / 0.2f);
                float rise = Mathf.SmoothStep(0f, 1f, (since - (length - 0.42f)) / 0.42f);
                down = fall * (1f - rise);
            }

            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) - 0.5f;
            float side = Mathf.Cos(walkPhase);
            float eye = Mathf.Lerp(config.eyeHeight, knockedEyeHeight, down);
            Quaternion yaw = Quaternion.Euler(0f, local.LookYaw, 0f);
            position = local.transform.position + Vector3.up * (eye + bob * bobHeight * 2f * walkAmount) +
                yaw * Vector3.right * (side * bobSide * walkAmount);

            float dazed = Mathf.Sin(Time.time * 5f) * 3f * down;
            float pitch = local.LookPitch - 48f * down + kickPitch;
            float roll = hitRoll * 22f * down + dazed - side * 0.35f * walkAmount + ramRoll * dashAmount * side + rollNow;
            rotation = Quaternion.Euler(pitch, local.LookYaw, roll);

            // A spring, so a kick snaps down and eases back.
            float spring = -kickPitch * 220f - kickVelocity * 24f;
            kickVelocity += spring * deltaTime;
            kickPitch += kickVelocity * deltaTime;
            rollVelocity += (-rollNow * 180f - rollVelocity * 20f) * deltaTime;
            rollNow += rollVelocity * deltaTime;
        }

        private void ShoulderPose(PlayerAvatar local, out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(local.LookPitch, local.LookYaw, 0f);
            Vector3 pivot = local.transform.position + Vector3.up * shoulderHeight + rotation * Vector3.right * shoulderSide;
            Vector3 back = rotation * Vector3.back;
            float distance = shoulderDistance;
            // The camera comes in close rather than going through a wall or the ground.
            if (Physics.SphereCast(pivot, 0.22f, back, out RaycastHit hit, shoulderDistance, GameLayers.LevelMask, QueryTriggerInteraction.Ignore))
                distance = Mathf.Max(0.5f, hit.distance);
            position = pivot + back * distance;
        }

        private Quaternion ShakeRotation(float deltaTime)
        {
            trauma = Mathf.MoveTowards(trauma, 0f, shakeFade * deltaTime);
            if (trauma <= 0f) return Quaternion.identity;
            float amount = trauma * trauma * shakeDegrees;
            float t = Time.time * 24f;
            return Quaternion.Euler(
                (Mathf.PerlinNoise(noiseSeed, t) - 0.5f) * 2f * amount,
                (Mathf.PerlinNoise(noiseSeed + 7f, t) - 0.5f) * 2f * amount,
                (Mathf.PerlinNoise(noiseSeed + 13f, t) - 0.5f) * 2f * amount);
        }
    }
}
