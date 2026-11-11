using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The end of every half: the helicopter arrives, hooks the truck's cargo box and flies it away
    /// with the rescued cargo inside. It is only for show, and every computer plays it locally from
    /// the arrival time the host replicates, so no positions go over the network.
    ///
    /// The lift is Astra's (独立建模/运输载具_v07, 脚本/motion_fleet.py). Every pose comes straight from the
    /// time since arrival, with no state carried from frame to frame, so it looks the same at any frame
    /// rate and on every computer. The hook comes down and the four slings spread to the box's lifting
    /// eyes; once they're tight the box rises straight up off the truck, still at the truck's tilt, and
    /// only levels off when it's clear; then the helicopter winds in the cable and flies off with it.
    /// Each stage eases in and out (6u⁵ - 15u⁴ + 10u³), so nothing jerks where one stage hands over to the
    /// next. The slings keep their length once they're on: the box hangs from the hook by them.
    /// </summary>
    public sealed class HelicopterExtraction : MonoBehaviour
    {
        private enum Stage { Waiting, FlyingIn, Lowering, Clipping, Lifting, Leaving, Gone }

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
        [Tooltip("A clip at the end of each sling, where it hooks onto a lifting eye.")]
        public Transform[] slingClips;
        [Tooltip("Where the slings tie on, in the hook's local space.")]
        public Vector3 hookEye = new Vector3(0f, -1.3f, 0f);
        public float cableThickness = 0.09f;
        public float slingThickness = 0.06f;

        [Header("Cargo box on the truck")]
        [Tooltip("The box's pivot, in the middle of its floor.")]
        public Transform bed;
        [Tooltip("The four lifting eyes the slings clip onto, in the box's local space.")]
        public Vector3[] bedClips =
        {
            new Vector3(-1.82f, 1.2f, -2.36f), new Vector3(1.82f, 1.2f, -2.36f),
            new Vector3(-1.82f, 1.2f, 2.36f), new Vector3(1.82f, 1.2f, 2.36f),
        };
        [Tooltip("Height of the hook above the box while the slings are tight.")]
        public float hookHeight = 5.1f;

        [Header("Lift")]
        [Tooltip("Cable from the winch to the hook while the hook is down on the box.")]
        public float cableDown = 4.3f;
        [Tooltip("Cable once it's wound in, for the flight away.")]
        public float cableUp = 2.4f;
        [Tooltip("How far the box rises off the truck, still tilted, before it levels.")]
        public float clearance = 0.48f;
        [Tooltip("How much higher it goes while it levels off.")]
        public float liftHeight = 2.72f;
        [Tooltip("Share of the lift spent clearing the truck.")]
        public float clearShare = 0.3f;

        [Header("Flight")]
        [Tooltip("Where it has got to when the shot ends, in its own frame: right, up, ahead. It speeds up all the way.")]
        public Vector3 leaveOffset = new Vector3(6f, 16f, 60f);
        [Tooltip("The cable swings back this far once it's under way, and the box tips less than half as much.")]
        public float trailDegrees = 5.5f;
        public float boxFollowsCable = 0.45f;
        public Vector3 leaveDirection = new Vector3(-0.22f, 0f, -1f);
        [Tooltip("The flight path in, as offsets from the hover point.")]
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
        private Quaternion level;
        private Vector3 hookRest;
        private Vector3 hoverAnchor;
        private Vector3 liftedAnchor;
        private Vector3 clipScale;
        private float nextDustAt;

        private float LowerEnd => config.helicopterLowerSeconds;
        private float ClipEnd => LowerEnd + config.helicopterClipSeconds;
        private float LiftEnd => ClipEnd + config.helicopterLiftSeconds;
        private float LeaveEnd => LiftEnd + config.helicopterLeaveSeconds;

        /// <summary>Eases 0 to 1 with no speed or acceleration at either end.</summary>
        public static float Smoother(float u)
        {
            u = Mathf.Clamp01(u);
            return u * u * u * (u * (u * 6f - 15f) + 10f);
        }

        private void Awake()
        {
            config = GameConfig.Get();
            leaveDirection = leaveDirection.normalized;
            level = Quaternion.LookRotation(leaveDirection);

            bedRestPosition = bed.position;
            bedRestRotation = bed.rotation;
            Vector3 boxForward = Vector3.ProjectOnPlane(bedRestRotation * Vector3.forward, Vector3.up);
            bedLevelRotation = Quaternion.LookRotation(boxForward, Vector3.up);
            hookRest = bedRestPosition + bedRestRotation * Vector3.up * hookHeight;
            // It hovers with the cable hanging straight down to where the hook will be.
            hoverAnchor = hookRest + Vector3.up * cableDown;
            liftedAnchor = bedRestPosition + Vector3.up * (clearance + liftHeight + hookHeight + cableUp);

            if (slingClips != null && slingClips.Length > 0 && slingClips[0] != null) clipScale = slingClips[0].localScale;
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
                PoseScene(time);
                KickUpDust(time);
                GameAudio.SetLoop("rotor", RotorVolume(time));
            }

            if (phase == MatchPhase.HalfOutro) AimCamera();
        }

        // ---- Where everything is at a given time. Time 0 is the arrival over the truck ----

        private float Lowered(float time) => Smoother(time / config.helicopterLowerSeconds);

        private float Cleared(float time) => Smoother((time - ClipEnd) / (config.helicopterLiftSeconds * clearShare));

        private float Levelled(float time) =>
            Smoother((time - ClipEnd - config.helicopterLiftSeconds * clearShare) / (config.helicopterLiftSeconds * (1f - clearShare)));

        private float Departed(float time) => Mathf.Clamp01((time - LiftEnd) / config.helicopterLeaveSeconds);

        /// <summary>The box while it hangs from the hook, before the flight away.</summary>
        private void BoxOnLift(float time, out Vector3 position, out Quaternion rotation)
        {
            position = bedRestPosition + Vector3.up * (clearance * Cleared(time) + liftHeight * Levelled(time));
            rotation = Quaternion.Slerp(bedRestRotation, bedLevelRotation, Levelled(time));
        }

        /// <summary>Where the cable leaves the helicopter.</summary>
        private Vector3 Anchor(float time)
        {
            if (time < 0f)
            {
                float progress = Mathf.Clamp01(1f + time / config.helicopterApproachSeconds);
                float eased = 1f - Mathf.Pow(1f - progress, 1.6f);
                return Bezier(hoverAnchor + pathStart, hoverAnchor + pathControlA, hoverAnchor + pathControlB, hoverAnchor, eased);
            }
            if (time < ClipEnd)
            {
                // Holding station, with a little life that settles as the hook goes on.
                float settle = Mathf.Clamp01(time) * (1f - Smoother(time / ClipEnd));
                return hoverAnchor + new Vector3(Mathf.Sin(time * 1.4f) * 0.1f, Mathf.Sin(time * 2.3f) * 0.12f, 0f) * settle;
            }
            if (time < LiftEnd)
            {
                BoxOnLift(time, out Vector3 box, out Quaternion boxTurn);
                Vector3 hookAt = box + boxTurn * Vector3.up * hookHeight;
                return hookAt + Vector3.up * Mathf.Lerp(cableDown, cableUp, Levelled(time));
            }
            // It speeds up all the way out (u³ starts with no speed and no push), so the box doesn't jerk.
            float u = Departed(time);
            Vector3 right = level * Vector3.right;
            return liftedAnchor + (right * leaveOffset.x + Vector3.up * leaveOffset.y + leaveDirection * leaveOffset.z) * (u * u * u);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        private void PoseScene(float time)
        {
            PoseHelicopter(time, out Quaternion bodyTurn);
            Vector3 anchor = Anchor(time);
            // The winch sits exactly on the path, whatever way the body tips.
            helicopter.position = anchor - bodyTurn * Vector3.Scale(helicopter.lossyScale, winch.localPosition);

            if (time < 0f)
            {
                EnterStage(Stage.FlyingIn);
                hook.SetPositionAndRotation(anchor, helicopter.rotation);
                HideCables();
                return;
            }

            Vector3 boxAt;
            Quaternion boxTurn;
            Vector3 hookAt;
            Quaternion hookTurn;
            if (time < ClipEnd)
            {
                EnterStage(time < LowerEnd ? Stage.Lowering : Stage.Clipping);
                boxAt = bedRestPosition;
                boxTurn = bedRestRotation;
                float down = Lowered(time);
                hookAt = Vector3.Lerp(anchor, hookRest, down);
                hookTurn = Quaternion.Slerp(helicopter.rotation, bedRestRotation, down);
            }
            else if (time < LiftEnd)
            {
                EnterStage(Stage.Lifting);
                BoxOnLift(time, out boxAt, out boxTurn);
                hookAt = boxAt + boxTurn * Vector3.up * hookHeight;
                hookTurn = boxTurn;
            }
            else
            {
                EnterStage(Stage.Leaving);
                // Under way the cable trails a little behind, and sways a touch to the side.
                float u = Departed(time);
                float trail = trailDegrees * Smoother(u / 0.35f) * Mathf.Deg2Rad;
                float wave = Mathf.Sin(Mathf.PI * u);
                float sway = Mathf.Deg2Rad * wave * wave * wave * Mathf.Sin(2f * Mathf.PI * u);
                Vector3 right = level * Vector3.right;
                Vector3 hang = (-Vector3.up - leaveDirection * Mathf.Sin(trail) + right * Mathf.Sin(sway)).normalized;
                hookAt = anchor + hang * cableUp;
                boxTurn = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, -hang, boxFollowsCable)) * bedLevelRotation;
                boxAt = hookAt - boxTurn * Vector3.up * hookHeight;
                hookTurn = boxTurn;
            }

            bed.SetPositionAndRotation(boxAt, boxTurn);
            hook.SetPositionAndRotation(hookAt, hookTurn);
            PlaceBetween(cable, anchor, hookAt, cableThickness);
            PoseSlings(time, hookAt, hookTurn, boxAt, boxTurn);

            Quaternion turn = boxTurn * Quaternion.Inverse(bedRestRotation);
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value == CargoState.Rescued) cargo.ShowCarriedAway(bedRestPosition, boxAt, turn);
            }
        }

        /// <summary>
        /// On the way down they hang from the hook and spread out toward the four eyes, growing to full
        /// length early; from then on each runs from the hook's eye to its lifting eye on the box.
        /// </summary>
        private void PoseSlings(float time, Vector3 hookAt, Quaternion hookTurn, Vector3 boxAt, Quaternion boxTurn)
        {
            Vector3 eye = hookAt + hookTurn * hookEye;
            float eyeHeight = hookHeight + hookEye.y;
            float down = Lowered(time);
            for (int i = 0; i < slings.Length; i++)
            {
                Vector3 end;
                if (time < LowerEnd)
                {
                    Vector3 reach = bedClips[i] - Vector3.up * eyeHeight;
                    Vector3 direction = Vector3.Slerp(Vector3.down, reach.normalized, down);
                    end = eye + hookTurn * direction * (reach.magnitude * Smoother(down * 2.3f));
                }
                else
                {
                    end = boxAt + boxTurn * bedClips[i];
                }
                PlaceBetween(slings[i], eye, end, slingThickness);

                if (slingClips == null || i >= slingClips.Length || slingClips[i] == null) continue;
                Transform clip = slingClips[i];
                float size = Smoother(time * 3f);
                bool visible = size > 0.01f;
                if (clip.gameObject.activeSelf != visible) clip.gameObject.SetActive(visible);
                if (!visible) continue;
                clip.SetPositionAndRotation(end, hookTurn);
                clip.localScale = clipScale * size;
            }
        }

        private void PoseHelicopter(float time, out Quaternion bodyTurn)
        {
            // Speed and acceleration come straight from the path, so the tilt can't drift or depend on frame rate.
            const float step = 0.2f;
            Vector3 position = Anchor(time);
            Vector3 before = Anchor(time - step);
            Vector3 after = Anchor(time + step);
            Vector3 velocity = (after - before) / (2f * step);
            Vector3 acceleration = (after - 2f * position + before) / (step * step);

            Vector3 flat = velocity;
            flat.y = 0f;
            float speed = flat.magnitude;
            Vector3 heading = speed > 0.01f
                ? Vector3.Slerp(leaveDirection, flat / speed, Mathf.Clamp01(speed / 7f))
                : leaveDirection;
            Vector3 right = Vector3.Cross(Vector3.up, heading);

            float pitch = Mathf.Clamp(Vector3.Dot(velocity, heading) * 0.4f + Vector3.Dot(acceleration, heading) * 1.5f, -16f, 24f);
            float roll = Mathf.Clamp(-Vector3.Dot(acceleration, right) * 3f, -18f, 18f);
            bodyTurn = Quaternion.LookRotation(heading) * Quaternion.Euler(pitch, 0f, roll);
            helicopter.rotation = bodyTurn;

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
            return Mathf.Lerp(0.8f, 0f, Departed(time));
        }

        private void KickUpDust(float time)
        {
            if (time < -1f || time > LiftEnd || Time.time < nextDustAt) return;
            nextDustAt = Time.time + 0.18f;
            Vector2 spot = Random.insideUnitCircle.normalized * Random.Range(3.5f, 8f);
            FxBurst.Play(new Vector3(bedRestPosition.x + spot.x, 0.2f, bedRestPosition.z + spot.y), new Color(0.78f, 0.7f, 0.55f), 5);
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
            if (slingClips == null) return;
            foreach (Transform clip in slingClips)
            {
                if (clip != null && clip.gameObject.activeSelf) clip.gameObject.SetActive(false);
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
