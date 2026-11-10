using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The end of every half: the helicopter arrives, hooks the truck's cargo box and flies it away
    /// with the rescued cargo inside. It is only for show, and every computer plays it locally from
    /// the arrival time the host replicates, so no positions go over the network.
    /// </summary>
    public sealed class HelicopterExtraction : MonoBehaviour
    {
        private enum Stage { Waiting, FlyingIn, Lowering, Clipping, Lifting, Leaving, Gone }

        private const float RopeStep = 1f / 120f;

        [Header("Helicopter")]
        public Transform helicopter;
        public Transform mainRotor;
        public Transform tailRotor;
        [Tooltip("Where the cable leaves the belly.")]
        public Transform winch;
        public float mainRotorSpeed = 1000f;
        public float tailRotorSpeed = 1500f;

        [Header("Rigging")]
        public Transform cable;
        public Transform hook;
        public Transform[] slings;
        [Tooltip("Where the slings tie on, in the hook's local space.")]
        public Vector3 hookEye = new Vector3(0f, -1.3f, 0f);
        public float cableThickness = 0.09f;
        public float slingThickness = 0.06f;

        [Header("Cargo box on the truck")]
        [Tooltip("The box's pivot, in the middle of its floor.")]
        public Transform bed;
        [Tooltip("The four corners the slings clip onto, in the box's local space.")]
        public Vector3[] bedClips =
        {
            new Vector3(-1.82f, 1.2f, -2.36f), new Vector3(1.82f, 1.2f, -2.36f),
            new Vector3(-1.82f, 1.2f, 2.36f), new Vector3(1.82f, 1.2f, 2.36f),
        };
        [Tooltip("Height of the hook above the box while the slings are tight.")]
        public float hookHeight = 5.1f;

        [Header("Flight")]
        [Tooltip("Height of the winch above the box while hovering.")]
        public float hoverHeight = 9.4f;
        public float liftClimb = 2.2f;
        public float liftWindIn = 1.9f;
        [Tooltip("How far it has flown when the shot ends. It speeds up gradually, so the box doesn't jerk.")]
        public float leaveDistance = 50f;
        public float leaveClimb = 15f;
        [Tooltip("The box may swing this far from straight down.")]
        public float maxSwingDegrees = 24f;
        public Vector3 leaveDirection = new Vector3(-0.22f, 0f, -1f);
        [Tooltip("The flight path, as offsets from the hover point.")]
        public Vector3 pathStart = new Vector3(26f, 2f, 170f);
        public Vector3 pathControlA = new Vector3(20f, 0f, 95f);
        public Vector3 pathControlB = new Vector3(5f, 1f, 34f);

        [Header("Camera")]
        [Tooltip("Where the camera watches from, as an offset from the box on the truck.")]
        public Vector3 shotOffset = new Vector3(30f, 12f, -9f);
        public float shotLookHeight = 6.2f;

        private GameConfig config;
        private Stage stage = Stage.Waiting;
        private Vector3 bedRestPosition;
        private Quaternion bedRestRotation;
        private Quaternion bedLevelRotation;
        private Vector3 hoverPoint;
        private Vector3 hookRest;
        private Vector3 heading;
        private Vector3 load;
        private Vector3 loadPrevious;
        private Vector3 anchorPrevious;
        private float ropeLength;
        private float ropeTime;
        private float nextDustAt;

        private float LowerEnd => config.helicopterLowerSeconds;
        private float ClipEnd => LowerEnd + config.helicopterClipSeconds;
        private float LiftEnd => ClipEnd + config.helicopterLiftSeconds;
        private float LeaveEnd => LiftEnd + config.helicopterLeaveSeconds;

        private void Awake()
        {
            config = GameConfig.Get();
            leaveDirection = leaveDirection.normalized;
            heading = leaveDirection;

            bedRestPosition = bed.position;
            bedRestRotation = bed.rotation;
            Vector3 boxForward = Vector3.ProjectOnPlane(bedRestRotation * Vector3.forward, Vector3.up);
            bedLevelRotation = Quaternion.LookRotation(boxForward, Vector3.up);
            hookRest = bedRestPosition + bedRestRotation * Vector3.up * hookHeight;

            Vector3 winchOffset = Quaternion.LookRotation(leaveDirection) * winch.localPosition;
            hoverPoint = bedRestPosition + Vector3.up * hoverHeight - winchOffset;
            helicopter.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            GameAudio.SetLoop(null, 0f);
        }

        private void LateUpdate()
        {
            MatchDirector director = MatchDirector.Instance;
            MatchPhase phase = director != null && director.IsSpawned ? director.Phase.Value : MatchPhase.Loading;

            if (phase == MatchPhase.Loading || phase == MatchPhase.HalfIntro)
            {
                FollowCamera.EndShot();
                ResetScene();
                return;
            }

            if (phase == MatchPhase.Results)
            {
                FinishScene();
                FollowCamera.SetShot(bedRestPosition + shotOffset, bedRestPosition + Vector3.up * shotLookHeight);
                return;
            }

            float time = (float)(director.Now - director.HelicopterArrivesAt.Value);
            if (time < -config.helicopterApproachSeconds)
            {
                ResetScene();
                return;
            }

            if (time >= LeaveEnd)
            {
                FinishScene();
            }
            else
            {
                if (!helicopter.gameObject.activeSelf) helicopter.gameObject.SetActive(true);
                PoseHelicopter(time);
                PoseRigging(time);
                KickUpDust(time);
                GameAudio.SetLoop("rotor", RotorVolume(time));
            }

            if (phase == MatchPhase.HalfOutro) AimCamera();
        }

        // ---- Flight ----

        /// <summary>Where the helicopter is at a given time. Time 0 is its arrival over the truck.</summary>
        private Vector3 FlightPosition(float time)
        {
            if (time < 0f)
            {
                float progress = Mathf.Clamp01(1f + time / config.helicopterApproachSeconds);
                float eased = 1f - Mathf.Pow(1f - progress, 1.6f);
                return Bezier(hoverPoint + pathStart, hoverPoint + pathControlA, hoverPoint + pathControlB, hoverPoint, eased);
            }

            float settle = Mathf.Clamp01(time);
            Vector3 position = hoverPoint;
            position.y += Mathf.Sin(time * 2.3f) * 0.14f * settle;
            position.x += Mathf.Sin(time * 1.4f) * 0.1f * settle;
            position.y += liftClimb * Mathf.SmoothStep(0f, 1f, (time - ClipEnd) / config.helicopterLiftSeconds);

            float leaving = (time - LiftEnd) / config.helicopterLeaveSeconds;
            if (leaving > 0f)
            {
                float flown = Mathf.Pow(leaving, 2.4f);
                position += leaveDirection * (leaveDistance * flown) + Vector3.up * (leaveClimb * flown);
            }
            return position;
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        private void PoseHelicopter(float time)
        {
            // Speed and acceleration come straight from the path, so the tilt can't drift or depend on frame rate.
            const float step = 0.2f;
            Vector3 position = FlightPosition(time);
            Vector3 before = FlightPosition(time - step);
            Vector3 after = FlightPosition(time + step);
            Vector3 velocity = (after - before) / (2f * step);
            Vector3 acceleration = (after - 2f * position + before) / (step * step);

            Vector3 flat = velocity;
            flat.y = 0f;
            float speed = flat.magnitude;
            heading = speed > 0.01f
                ? Vector3.Slerp(leaveDirection, flat / speed, Mathf.Clamp01(speed / 7f))
                : leaveDirection;
            Vector3 right = Vector3.Cross(Vector3.up, heading);

            float pitch = Mathf.Clamp(Vector3.Dot(velocity, heading) * 0.4f + Vector3.Dot(acceleration, heading) * 1.5f, -16f, 24f);
            float roll = Mathf.Clamp(-Vector3.Dot(acceleration, right) * 3f, -18f, 18f);
            helicopter.SetPositionAndRotation(position, Quaternion.LookRotation(heading) * Quaternion.Euler(pitch, 0f, roll));

            mainRotor.Rotate(Vector3.up, mainRotorSpeed * Time.deltaTime, Space.Self);
            tailRotor.Rotate(Vector3.right, tailRotorSpeed * Time.deltaTime, Space.Self);
        }

        private float RotorVolume(float time)
        {
            if (time < 0f)
            {
                float progress = Mathf.Clamp01(1f + time / config.helicopterApproachSeconds);
                return Mathf.Lerp(0.12f, 0.8f, progress * progress);
            }
            if (time < LiftEnd) return 0.8f;
            return Mathf.Lerp(0.8f, 0f, (time - LiftEnd) / config.helicopterLeaveSeconds);
        }

        private void KickUpDust(float time)
        {
            if (time < -1f || time > LiftEnd || Time.time < nextDustAt) return;
            nextDustAt = Time.time + 0.18f;
            Vector2 spot = Random.insideUnitCircle.normalized * Random.Range(3.5f, 8f);
            FxBurst.Play(new Vector3(bedRestPosition.x + spot.x, 0.2f, bedRestPosition.z + spot.y), new Color(0.78f, 0.7f, 0.55f), 5);
        }

        // ---- Hook, slings and the box ----

        private void PoseRigging(float time)
        {
            if (time < 0f)
            {
                EnterStage(Stage.FlyingIn);
                hook.SetPositionAndRotation(winch.position, helicopter.rotation);
                HideCables();
                return;
            }

            if (time < ClipEnd)
            {
                PoseLowering(time);
                return;
            }

            PoseCarrying(time);
        }

        private void PoseLowering(float time)
        {
            EnterStage(time < LowerEnd ? Stage.Lowering : Stage.Clipping);

            Vector3 anchor = winch.position;
            Quaternion hookRotation = Quaternion.LookRotation(heading);
            Vector3 hookPosition = Vector3.Lerp(anchor, hookRest, Mathf.SmoothStep(0f, 1f, time / config.helicopterLowerSeconds));
            hook.SetPositionAndRotation(hookPosition, hookRotation);
            PlaceBetween(cable, anchor, hookPosition, cableThickness);

            // The slings hang loose on the way down, then spread out to the four corners.
            Vector3 eye = hook.TransformPoint(hookEye);
            float spread = Mathf.SmoothStep(0f, 1f, (time - LowerEnd) / config.helicopterClipSeconds);
            Vector3 sway = new Vector3(Mathf.Sin(time * 5f), 0f, Mathf.Cos(time * 4f)) * 0.08f;
            for (int i = 0; i < slings.Length; i++)
            {
                Vector3 clip = bedRestPosition + bedRestRotation * bedClips[i];
                Vector3 hanging = eye + new Vector3(bedClips[i].x * 0.12f, -2.3f, bedClips[i].z * 0.12f) + sway;
                PlaceBetween(slings[i], eye, Vector3.Lerp(hanging, clip, spread), slingThickness);
            }

            load = hookRest;
            loadPrevious = hookRest;
            anchorPrevious = anchor;
            ropeLength = Vector3.Distance(anchor, hookRest);
            ropeTime = 0f;
        }

        private void PoseCarrying(float time)
        {
            EnterStage(time < LiftEnd ? Stage.Lifting : Stage.Leaving);

            Vector3 anchor = winch.position;
            float wound = Mathf.SmoothStep(0f, 1f, (time - ClipEnd) / config.helicopterLiftSeconds);
            StepRope(anchor, ropeLength - liftWindIn * wound);
            Vector3 hang = (anchor - load).normalized;

            // The box leaves the tilted truck and settles level. It tips less than the cable swings.
            Quaternion hanging = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, hang), 0.7f) * bedLevelRotation;
            float settle = Mathf.SmoothStep(0f, 1f, (time - ClipEnd) / (config.helicopterLiftSeconds * 0.6f));
            Quaternion bedRotation = Quaternion.Slerp(bedRestRotation, hanging, settle);
            Vector3 bedPosition = load - bedRotation * Vector3.up * hookHeight;
            bed.SetPositionAndRotation(bedPosition, bedRotation);

            hook.SetPositionAndRotation(load, Quaternion.LookRotation(Vector3.ProjectOnPlane(heading, hang), hang));
            PlaceBetween(cable, anchor, load, cableThickness);
            Vector3 eye = hook.TransformPoint(hookEye);
            for (int i = 0; i < slings.Length; i++)
                PlaceBetween(slings[i], eye, bed.TransformPoint(bedClips[i]), slingThickness);

            Quaternion turn = bedRotation * Quaternion.Inverse(bedRestRotation);
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value == CargoState.Rescued) cargo.ShowCarriedAway(bedRestPosition, bedPosition, turn);
            }
        }

        /// <summary>The box swings on the cable by itself. A fixed step keeps it the same at any frame rate.</summary>
        private void StepRope(Vector3 anchor, float length)
        {
            ropeTime += Mathf.Min(Time.deltaTime, 0.1f);
            int steps = Mathf.FloorToInt(ropeTime / RopeStep);
            ropeTime -= steps * RopeStep;
            for (int i = 1; i <= steps; i++)
            {
                Vector3 stepAnchor = Vector3.Lerp(anchorPrevious, anchor, i / (float)steps);
                Vector3 velocity = (load - loadPrevious) * 0.998f;
                loadPrevious = load;
                load += velocity + Physics.gravity * (RopeStep * RopeStep);

                Vector3 toLoad = Vector3.ClampMagnitude(load - stepAnchor, length);
                if (Vector3.Angle(Vector3.down, toLoad) > maxSwingDegrees)
                    toLoad = Vector3.RotateTowards(Vector3.down, toLoad, maxSwingDegrees * Mathf.Deg2Rad, 0f).normalized * toLoad.magnitude;
                load = stepAnchor + toLoad;
            }
            if (steps > 0) anchorPrevious = anchor;
        }

        private static void PlaceBetween(Transform part, Vector3 from, Vector3 to, float thickness)
        {
            Vector3 offset = to - from;
            float length = offset.magnitude;
            bool visible = length > 0.02f;
            if (part.gameObject.activeSelf != visible) part.gameObject.SetActive(visible);
            if (!visible) return;
            part.SetPositionAndRotation((from + to) * 0.5f, Quaternion.FromToRotation(Vector3.up, offset / length));
            // Unity's cylinder is 2 units tall and 1 unit wide.
            part.localScale = new Vector3(thickness, length * 0.5f, thickness);
        }

        private void HideCables()
        {
            if (cable.gameObject.activeSelf) cable.gameObject.SetActive(false);
            foreach (Transform sling in slings)
            {
                if (sling.gameObject.activeSelf) sling.gameObject.SetActive(false);
            }
        }

        // ---- Stages ----

        private void EnterStage(Stage next)
        {
            if (stage == next) return;
            stage = next;
            AutoPilot.Log($"HELICOPTER {next}");
            if (next == Stage.Lifting)
            {
                GameAudio.Play("clunk");
                FxBurst.Play(bedRestPosition + Vector3.up, new Color(0.78f, 0.7f, 0.55f), 26);
                FollowCamera.Shake(FollowCamera.Focus);
            }
        }

        /// <summary>Everything back on the truck, ready for the next half.</summary>
        private void ResetScene()
        {
            if (stage == Stage.Waiting) return;
            stage = Stage.Waiting;
            helicopter.gameObject.SetActive(false);
            bed.gameObject.SetActive(true);
            bed.SetPositionAndRotation(bedRestPosition, bedRestRotation);
            GameAudio.SetLoop(null, 0f);
        }

        /// <summary>The helicopter has gone, taking the box and the rescued cargo with it.</summary>
        private void FinishScene()
        {
            if (stage == Stage.Gone) return;
            EnterStage(Stage.Gone);
            helicopter.gameObject.SetActive(false);
            bed.gameObject.SetActive(false);
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value == CargoState.Rescued) cargo.HideModel();
            }
            GameAudio.SetLoop(null, 0f);
        }

        private void AimCamera()
        {
            Vector3 look = bedRestPosition + Vector3.up * shotLookHeight;
            if (helicopter.gameObject.activeSelf) look = Vector3.Lerp(look, helicopter.position + Vector3.down * 3f, 0.22f);
            FollowCamera.SetShot(bedRestPosition + shotOffset, look);
        }
    }
}
