using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The arms, gloves and hammer you see in first person, posed in code because where the hands go
    /// depends on the cargo. It only runs on the local player's computer and affects no rules: everyone
    /// else sees the Animator clips on the character model. Positions are in the camera's space:
    /// x right, y up, z forward, in metres.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public sealed class FirstPersonRig : MonoBehaviour
    {
        private struct HandPose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool Fist;

            public HandPose(Vector3 position, Vector3 angles, bool fist = false)
            {
                Position = position;
                Rotation = Quaternion.Euler(angles);
                Fist = fist;
            }

            public static HandPose Lerp(HandPose a, HandPose b, float t) => new HandPose
            {
                Position = Vector3.Lerp(a.Position, b.Position, t),
                Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t),
                Fist = t < 0.5f ? a.Fist : b.Fist,
            };
        }

        private struct HandState
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public Quaternion Rotation;
        }

        // The hammer swing, in step with the Smash clip in make_character.py. The hammer is fixed to the
        // fist with its handle along the fist's +Y and its striking face along +Z.
        private static readonly HandPose HammerReady = new HandPose(new Vector3(0.36f, -0.41f, 0.62f), new Vector3(16f, -8f, -8f), true);
        private static readonly HandPose[] SwingPoses =
        {
            new HandPose(new Vector3(0.37f, -0.27f, 0.56f), new Vector3(-16f, -6f, -6f), true),
            new HandPose(new Vector3(0.36f, -0.17f, 0.5f), new Vector3(-44f, -8f, -6f), true),
            new HandPose(new Vector3(0.15f, -0.27f, 0.64f), new Vector3(92f, -5f, 0f), true),
            new HandPose(new Vector3(0.18f, -0.25f, 0.62f), new Vector3(68f, -5f, 0f), true),
            new HandPose(new Vector3(0.37f, -0.27f, 0.56f), new Vector3(-16f, -6f, -6f), true),
        };
        private static readonly float[] SwingTimes = { 0f, 0.38f, 0.55f, 0.68f, 1f };

        /// <summary>How far out from the side of held cargo the wrists are, which leaves the fingers wrapped round its corners.</summary>
        private const float GripReach = 0.15f;

        public static FirstPersonRig Instance { get; private set; }

        [Header("Parts")]
        [Tooltip("Everything that shows. The script stays on the object above it, so it keeps running while this is hidden.")]
        public GameObject content;
        public Transform armLeft;
        public Transform armRight;
        public Transform handLeft;
        public Transform handRight;
        public GameObject openLeft;
        public GameObject fistLeft;
        public GameObject openRight;
        public GameObject fistRight;
        public GameObject hammer;

        [Header("Holding cargo")]
        [Tooltip("How far in front of your eyes the near side of held cargo is. Wider cargo goes farther out, as well as this much for each metre of half width.")]
        public float holdDistance = 0.5f;
        public float holdDistancePerWidth = 0.95f;
        [Tooltip("How far below the middle of the view the top of held cargo stays, so you can see where you're going.")]
        public float holdBelowDegrees = 7f;
        [Tooltip("How far held cargo is tipped forward, so its face looks back at you.")]
        public float holdTiltDegrees = 30f;
        public float pickUpSeconds = 0.2f;

        [Header("Feel")]
        public Vector3 elbowRight = new Vector3(0.5f, -0.88f, -0.02f);
        public float swayPerDegree = 0.0028f;
        public float swayLimit = 0.05f;
        public float swaySpring = 90f;

        private GameConfig config;
        private HandState right;
        private HandState left;
        private Vector2 swayNow;
        private Vector2 swayVelocity;
        private CargoItem lastCarried;
        private float tossUntil;
        private float pushUntil;
        private float knockedAt = -100f;
        private bool wasKnocked;
        private bool colorsApplied;
        private bool placed;
        private float swingWeight;

        public float PickUpSeconds => pickUpSeconds;

        /// <summary>True if this character's cargo is in your hands in first person, rather than on their chest.</summary>
        public bool HoldsCargoOf(PlayerAvatar carrier) => carrier == PlayerAvatar.Local && content.activeSelf;

        /// <summary>Where held cargo goes this frame. CargoItem runs after this script, so it follows without any lag.</summary>
        public void HoldPose(CargoItem cargo, out Vector3 position, out Quaternion rotation)
        {
            Vector3 anchor = HoldPoint(cargo);
            float walk = FollowCamera.WalkAmount;
            float phase = FollowCamera.WalkPhase;
            // Light things jiggle more than heavy ones.
            float jiggle = walk * (cargo.Definition.size == CargoSize.Small ? 1f : 0.45f);
            Quaternion tilt = Quaternion.Euler(Mathf.Sin(phase * 2f) * 2f * jiggle, Mathf.Cos(phase) * 1.5f * jiggle, Mathf.Cos(phase) * 3.5f * jiggle);
            position = transform.TransformPoint(anchor);
            rotation = transform.rotation * HoldTilt * tilt;
        }

        private Quaternion HoldTilt => Quaternion.Euler(holdTiltDegrees, 0f, 0f);

        private Vector3 HoldPoint(CargoItem cargo)
        {
            Vector3 half = cargo.HalfExtents;
            float z = holdDistance + half.x * holdDistancePerWidth + half.z;
            float sin = Mathf.Sin(holdTiltDegrees * Mathf.Deg2Rad);
            float cos = Mathf.Cos(holdTiltDegrees * Mathf.Deg2Rad);
            // Tipped forward, its highest point is the top edge nearest you.
            float cornerUp = half.y * cos + half.z * sin;
            float cornerForward = z + half.y * sin - half.z * cos;
            float y = -cornerForward * Mathf.Tan(holdBelowDegrees * Mathf.Deg2Rad) - cornerUp;
            return new Vector3(0f, y, z);
        }

        private void Awake()
        {
            Instance = this;
            config = GameConfig.Get();
            right.Rotation = Quaternion.identity;
            left.Rotation = Quaternion.identity;
            content.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            bool show = local != null && local.IsSpawned && FollowCamera.ShowsHands;
            if (content.activeSelf != show) content.SetActive(show);
            if (!show)
            {
                placed = false;
                return;
            }

            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.033f);
            ApplyTeamColor(local);
            UpdateSway(local, deltaTime);
            UpdateTransitions(local);

            CargoItem carried = local.CarriedCargo;
            bool swinging = local.Smashing.Value && !local.IsKnockedDown;
            bool holdingHammer = local.Side == Side.Interrupter && carried == null;
            ChoosePoses(local, carried, holdingHammer, swinging, deltaTime, out HandPose rightPose, out HandPose leftPose, out float stiffness);

            if (!placed)
            {
                placed = true;
                right.Position = rightPose.Position + Vector3.down * 0.3f;
                left.Position = leftPose.Position + Vector3.down * 0.3f;
                right.Rotation = rightPose.Rotation;
                left.Rotation = leftPose.Rotation;
            }

            swingWeight = Mathf.MoveTowards(swingWeight, swinging ? 1f : 0f, deltaTime * (swinging ? 14f : 8f));
            if (swinging && swingWeight >= 1f)
            {
                // The keyframes are sharp on purpose, so they aren't softened by the spring.
                right.Position = rightPose.Position;
                right.Rotation = rightPose.Rotation;
                right.Velocity = Vector3.zero;
            }
            else
            {
                Step(ref right, rightPose, stiffness, deltaTime);
            }
            Step(ref left, leftPose, stiffness, deltaTime);
            Show(handRight, armRight, right, 1f);
            Show(handLeft, armLeft, left, -1f);

            SetActiveIfChanged(openRight, !rightPose.Fist);
            SetActiveIfChanged(fistRight, rightPose.Fist);
            SetActiveIfChanged(openLeft, !leftPose.Fist);
            SetActiveIfChanged(fistLeft, leftPose.Fist);
            SetActiveIfChanged(hammer, holdingHammer && !local.IsKnockedDown && !local.IsDashing);
        }

        private void UpdateSway(PlayerAvatar local, float deltaTime)
        {
            // The hands trail behind when the view turns, and bounce a little with each step.
            Vector2 look = local.LookThisFrame;
            Vector2 target = Vector2.ClampMagnitude(new Vector2(-look.x, -look.y) * swayPerDegree / Mathf.Max(deltaTime, 0.008f) * 0.016f, swayLimit);
            swayVelocity += (target - swayNow) * (swaySpring * deltaTime);
            swayVelocity *= Mathf.Exp(-14f * deltaTime);
            swayNow += swayVelocity * deltaTime;

            float walk = FollowCamera.WalkAmount;
            float phase = FollowCamera.WalkPhase;
            Vector3 bounce = new Vector3(Mathf.Cos(phase) * 0.011f, -Mathf.Abs(Mathf.Sin(phase)) * 0.016f, 0f) * walk;
            transform.localPosition = new Vector3(swayNow.x, swayNow.y, 0f) + bounce;
            transform.localRotation = Quaternion.Euler(swayNow.y * 60f, -swayNow.x * 60f, swayNow.x * 40f);
        }

        private void UpdateTransitions(PlayerAvatar local)
        {
            bool knocked = local.IsKnockedDown;
            if (knocked && !wasKnocked) knockedAt = Time.time;
            wasKnocked = knocked;

            CargoItem carried = local.CarriedCargo;
            if (carried == lastCarried) return;
            if (lastCarried != null && carried == null && !knocked)
            {
                if (lastCarried.State.Value == CargoState.Rescued) tossUntil = Time.time + 0.32f;
                else pushUntil = Time.time + 0.2f;
            }
            lastCarried = carried;
        }

        private void ChoosePoses(PlayerAvatar local, CargoItem carried, bool holdingHammer, bool swinging, float deltaTime,
            out HandPose rightPose, out HandPose leftPose, out float stiffness)
        {
            float phase = FollowCamera.WalkPhase;
            float walk = FollowCamera.WalkAmount;
            float stride = Mathf.Sin(phase);
            stiffness = 170f;

            leftPose = RestPose(-1f, -stride, walk);
            rightPose = RestPose(1f, stride, walk);
            if (holdingHammer)
            {
                rightPose = HammerReady;
                rightPose.Position += new Vector3(0f, 0.012f * walk * stride, 0.03f * walk * stride);
            }

            if (local.IsKnockedDown)
            {
                KnockedPoses(out rightPose, out leftPose);
                stiffness = 120f;
                return;
            }

            if (local.IsDashing)
            {
                // Both fists up, leaning into the charge.
                rightPose = new HandPose(new Vector3(0.15f, -0.2f, 0.4f), new Vector3(-28f, -30f, 10f), true);
                leftPose = new HandPose(new Vector3(-0.17f, -0.25f, 0.37f), new Vector3(-28f, 30f, -10f), true);
                stiffness = 420f;
                return;
            }

            if (carried != null)
            {
                if (carried.Definition.requiredCarriers > 1) CoCarryPoses(carried, walk, stride, ref rightPose, ref leftPose);
                else HoldPoses(carried, out rightPose, out leftPose);
                stiffness = 900f;
                return;
            }

            if (Time.time < tossUntil)
            {
                // Pushing it up and away, toward the truck.
                rightPose = new HandPose(new Vector3(0.22f, -0.08f, 0.68f), new Vector3(-72f, -8f, -12f));
                leftPose = new HandPose(new Vector3(-0.22f, -0.08f, 0.68f), new Vector3(-72f, 8f, 12f));
                stiffness = 340f;
                return;
            }

            if (Time.time < pushUntil)
            {
                rightPose = new HandPose(new Vector3(0.26f, -0.27f, 0.68f), new Vector3(-30f, -8f, -12f));
                leftPose = new HandPose(new Vector3(-0.26f, -0.27f, 0.68f), new Vector3(-30f, 8f, 12f));
                stiffness = 340f;
                return;
            }

            if (swinging)
            {
                HandPose swing = SwingAt(local.Visual != null ? local.Visual.SmashCycle : 0f);
                rightPose = HandPose.Lerp(rightPose, swing, swingWeight);
                // The other arm swings the opposite way for balance.
                float balance = Mathf.Sin(swingWeight * Mathf.PI * 0.5f) * Mathf.Sin((local.Visual != null ? local.Visual.SmashCycle : 0f) * Mathf.PI * 2f);
                leftPose.Position += new Vector3(-0.03f, -0.03f, 0.05f) * balance;
            }
        }

        private static HandPose RestPose(float sign, float stride, float walk)
        {
            Vector3 position = new Vector3(sign * 0.31f, -0.345f + 0.03f * walk * (stride * 0.5f + 0.5f), 0.53f + 0.085f * walk * stride);
            return new HandPose(position, new Vector3(-14f, -sign * 12f, -sign * 18f));
        }

        private static HandPose SwingAt(float cycle)
        {
            for (int i = 0; i < SwingTimes.Length - 1; i++)
            {
                if (cycle > SwingTimes[i + 1]) continue;
                float t = Mathf.InverseLerp(SwingTimes[i], SwingTimes[i + 1], cycle);
                return HandPose.Lerp(SwingPoses[i], SwingPoses[i + 1], Mathf.SmoothStep(0f, 1f, t));
            }
            return SwingPoses[0];
        }

        private void KnockedPoses(out HandPose rightPose, out HandPose leftPose)
        {
            float since = Time.time - knockedAt;
            float flail = Mathf.Sin(since * 26f) * 0.03f;
            // Thrown up by the hit, then gone from view while you lie there, then back as you get up.
            float up = since < 0.32f ? 1f : Mathf.Clamp01(1f - (since - 0.32f) / 0.25f);
            float height = Mathf.Lerp(-0.72f, 0.03f, up);
            rightPose = new HandPose(new Vector3(0.36f, height + flail, 0.46f), new Vector3(-64f, -24f, -30f));
            leftPose = new HandPose(new Vector3(-0.36f, height - flail, 0.46f), new Vector3(-64f, 24f, 30f));
        }

        private void HoldPoses(CargoItem carried, out HandPose rightPose, out HandPose leftPose)
        {
            // Holds on to wherever the cargo really is, so the hands bring it up from the ground.
            Vector3 half = carried.HalfExtents;
            float blend = carried.CarryBlend;
            Vector3 anchor = Vector3.Lerp(transform.InverseTransformPoint(carried.CarryFrom), HoldPoint(carried), blend);
            Quaternion tilt = HoldTilt;
            float walk = FollowCamera.WalkAmount;
            float shift = Mathf.Cos(FollowCamera.WalkPhase) * 0.008f * walk;
            // Hands round the near corners with the fingers curling onto the side facing you, because
            // anything farther back along the sides would be hidden behind the cargo.
            float outward = half.x + GripReach;
            float up = half.y * 0.02f;
            float near = -half.z - 0.035f;
            rightPose = new HandPose
            {
                Position = anchor + tilt * new Vector3(outward, up + shift, near),
                Rotation = tilt * Quaternion.LookRotation(new Vector3(-1f, 0.2f, 0f), Vector3.back),
            };
            leftPose = new HandPose
            {
                Position = anchor + tilt * new Vector3(-outward, up - shift, near),
                Rotation = tilt * Quaternion.LookRotation(new Vector3(1f, 0.2f, 0f), Vector3.back),
            };
        }

        private void CoCarryPoses(CargoItem carried, float walk, float stride, ref HandPose rightPose, ref HandPose leftPose)
        {
            // The handles are on the ends of the chest. These hands stay on them in the world, so they're
            // only in view when you look at the chest.
            Vector3 half = carried.HalfExtents;
            Vector3 toYou = transform.position - carried.transform.position;
            float side = toYou.x >= 0f ? 1f : -1f;
            Vector3 handle = carried.transform.position + new Vector3(side * (half.x + 0.03f), 0.08f, 0f);
            Vector3 gripA = transform.InverseTransformPoint(handle + Vector3.forward * 0.17f);
            Vector3 gripB = transform.InverseTransformPoint(handle + Vector3.back * 0.17f);
            Vector3 gripRight = gripA.x > gripB.x ? gripA : gripB;
            Vector3 gripLeft = gripA.x > gripB.x ? gripB : gripA;

            float inView = gripRight.z < 0.1f ? 0f : Mathf.Clamp01(1.6f - Mathf.Abs(gripRight.y / gripRight.z)) * Mathf.Clamp01(1.8f - Mathf.Abs(gripRight.x / gripRight.z));
            Quaternion aimRight = Quaternion.LookRotation(gripRight - new Vector3(0.28f, -0.5f, 0f), Vector3.up);
            Quaternion aimLeft = Quaternion.LookRotation(gripLeft - new Vector3(-0.28f, -0.5f, 0f), Vector3.up);
            HandPose holdRight = new HandPose { Position = gripRight, Rotation = aimRight };
            HandPose holdLeft = new HandPose { Position = gripLeft, Rotation = aimLeft };

            // Looking away from the chest, you can see your arms straining low in front of you.
            HandPose strainRight = new HandPose(new Vector3(0.22f, -0.37f + 0.012f * walk * stride, 0.5f), new Vector3(30f, -10f, -14f));
            HandPose strainLeft = new HandPose(new Vector3(-0.22f, -0.37f - 0.012f * walk * stride, 0.5f), new Vector3(30f, 10f, 14f));
            rightPose = HandPose.Lerp(strainRight, holdRight, inView);
            leftPose = HandPose.Lerp(strainLeft, holdLeft, inView);
        }

        private static void Step(ref HandState state, in HandPose target, float stiffness, float deltaTime)
        {
            // Slightly under-damped, so a change of pose overshoots a touch and settles.
            float damping = 2f * Mathf.Sqrt(stiffness) * 0.8f;
            state.Velocity += (target.Position - state.Position) * (stiffness * deltaTime);
            state.Velocity *= Mathf.Exp(-damping * deltaTime);
            state.Position += state.Velocity * deltaTime;
            state.Rotation = Quaternion.Slerp(state.Rotation, target.Rotation, 1f - Mathf.Exp(-Mathf.Sqrt(stiffness) * 1.1f * deltaTime));
        }

        private void Show(Transform hand, Transform arm, in HandState state, float sign)
        {
            hand.SetLocalPositionAndRotation(state.Position, state.Rotation);
            // The sleeve runs from the wrist back to an elbow below the bottom of the screen.
            Vector3 elbow = new Vector3(elbowRight.x * sign, elbowRight.y, elbowRight.z);
            arm.SetLocalPositionAndRotation(state.Position, Quaternion.LookRotation(state.Position - elbow, Vector3.up));
        }

        private void ApplyTeamColor(PlayerAvatar local)
        {
            if (colorsApplied) return;
            colorsApplied = true;
            if (local.Team == Team.Orange) return;
            foreach (Renderer part in content.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = part.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == config.orangeMaterial) slots[i] = config.blueMaterial;
                }
                part.sharedMaterials = slots;
            }
        }

        private static void SetActiveIfChanged(GameObject item, bool active)
        {
            if (item.activeSelf != active) item.SetActive(active);
        }
    }
}
