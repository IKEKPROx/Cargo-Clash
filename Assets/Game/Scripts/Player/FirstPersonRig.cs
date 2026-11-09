using System;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The arms, gloves and hammer you see in first person, posed in code because where the hands go
    /// depends on the cargo. It only runs on the local player's computer and affects no rules: everyone
    /// else sees the Animator clips on the character model.
    ///
    /// What makes it read as one body rather than two floating hands: this object is the upper body,
    /// and it turns around the chest, carrying both arms and any held cargo with it. Each arm is a
    /// shoulder, an elbow worked out from where the hand has to be, and a hand that carries on from the
    /// forearm. Running follows the footsteps, and each swing is keyframes shared by the whole body.
    /// Positions are in the camera's space: x right, y up, z forward, in metres.
    /// </summary>
    [DefaultExecutionOrder(400)]
    public sealed class FirstPersonRig : MonoBehaviour
    {
        [Serializable]
        public sealed class ArmParts
        {
            public Transform upperArm;
            public Transform forearm;
            public Transform hand;
            public GameObject relaxed;
            public GameObject open;
            public GameObject fist;
        }

        private enum HandShape { Relaxed, Open, Fist }

        private enum Motion { Smooth, Accelerate, Decelerate, Linear }

        private enum HandAim
        {
            /// <summary>The hand has its own fixed direction.</summary>
            Fixed,
            /// <summary>The hand carries straight on from the forearm, so the wrist never looks broken.</summary>
            AlongForearm,
            /// <summary>The hand is closed round the hammer's handle, turned so it's as straight as the handle allows.</summary>
            OnHandle,
        }

        private struct ArmPose
        {
            public Vector3 Wrist;
            public HandAim Aim;
            public Quaternion Direction;
            public float Roll;
            public Vector3 Handle;
            public HandShape Shape;

            public static ArmPose Fixed(Vector3 wrist, Quaternion direction, HandShape shape) =>
                new ArmPose { Wrist = wrist, Aim = HandAim.Fixed, Direction = direction, Shape = shape };

            /// <summary>Roll turns the hand around the forearm. Positive is anticlockwise, seen from behind the hand.</summary>
            public static ArmPose Straight(Vector3 wrist, float roll, HandShape shape) =>
                new ArmPose { Wrist = wrist, Aim = HandAim.AlongForearm, Roll = roll, Direction = Quaternion.identity, Shape = shape };

            public static ArmPose OnHandleAt(Vector3 grip, Vector3 handle) =>
                new ArmPose { Wrist = grip, Aim = HandAim.OnHandle, Handle = handle, Direction = Quaternion.identity, Shape = HandShape.Fist };

            public static ArmPose Lerp(ArmPose a, ArmPose b, float t)
            {
                ArmPose result = t < 0.5f ? a : b;
                result.Wrist = Vector3.Lerp(a.Wrist, b.Wrist, t);
                result.Roll = Mathf.Lerp(a.Roll, b.Roll, t);
                result.Direction = Quaternion.Slerp(a.Direction, b.Direction, t);
                return result;
            }
        }

        private sealed class Arm
        {
            public ArmParts Parts;
            public float Side;
            public Vector3 Wrist;
            public Vector3 Velocity;
            public Quaternion Hand = Quaternion.identity;
            public HandShape Shape;
            public bool ShapeSet;
        }

        /// <summary>One moment of the two-handed hammer swing. Handle is the way the head is from the hands, and Strike is the way its face points.</summary>
        private struct HammerKey
        {
            public float Time;
            public Vector3 Grip;
            public Quaternion Rotation;
            public Vector3 Torso;
            public Motion Into;

            public HammerKey(float time, Vector3 grip, Vector3 handle, Vector3 strike, Vector3 torso, Motion into)
            {
                Time = time;
                Grip = grip;
                Rotation = HammerRotation(handle, strike);
                Torso = torso;
                Into = into;
            }
        }

        /// <summary>One moment of a punch, written for the right hand. Left-hand punches are the mirror of it.</summary>
        private struct PunchKey
        {
            public float Time;
            public Vector3 Punching;
            public float PunchingRoll;
            public Vector3 Guarding;
            public float GuardingRoll;
            public Vector3 Torso;
            public Motion Into;

            public PunchKey(float time, Vector3 punching, float punchingRoll, Vector3 guarding, float guardingRoll, Vector3 torso, Motion into)
            {
                Time = time;
                Punching = punching;
                PunchingRoll = punchingRoll;
                Guarding = guarding;
                GuardingRoll = guardingRoll;
                Torso = torso;
                Into = into;
            }
        }

        // These lengths and the hammer's grips have to match Tools/blender/make_hands.py.
        private const float ForearmLength = 0.62f;
        private const float UpperArmLength = 0.54f;
        private const float FistToHandle = 0.105f;
        private const float LeftGripAlongHandle = -0.27f;

        /// <summary>How far out from the side of held cargo the wrists are, which leaves the fingers wrapped round its corners.</summary>
        private const float GripReach = 0.15f;

        // In step with the Smash clip in make_character.py: the same moments, with the hit at 0.55.
        // Torso angles are lean forward, turn right and tilt left, in degrees.
        private static readonly HammerKey[] HammerKeys =
        {
            new HammerKey(0f, new Vector3(0.3f, -0.27f, 0.54f), new Vector3(0.3f, 0.85f, 0.42f), new Vector3(-0.3f, -0.4f, 0.87f), new Vector3(-2f, 3f, 0f), Motion.Smooth),
            new HammerKey(0.38f, new Vector3(0.27f, -0.07f, 0.38f), new Vector3(0.14f, 0.8f, -0.58f), new Vector3(-0.1f, 0.6f, 0.79f), new Vector3(-7f, 11f, 3f), Motion.Smooth),
            new HammerKey(0.55f, new Vector3(0.08f, -0.29f, 0.6f), new Vector3(-0.07f, 0.06f, 0.995f), new Vector3(0f, -0.99f, 0.06f), new Vector3(7f, -8f, -2f), Motion.Accelerate),
            new HammerKey(0.6f, new Vector3(0.08f, -0.3f, 0.6f), new Vector3(-0.07f, 0.04f, 0.995f), new Vector3(0f, -0.99f, 0.04f), new Vector3(8f, -8f, -2f), Motion.Linear),
            new HammerKey(0.74f, new Vector3(0.13f, -0.27f, 0.58f), new Vector3(-0.02f, 0.3f, 0.95f), new Vector3(0f, -0.95f, 0.3f), new Vector3(5f, -5f, -1f), Motion.Decelerate),
            new HammerKey(1f, new Vector3(0.3f, -0.27f, 0.54f), new Vector3(0.3f, 0.85f, 0.42f), new Vector3(-0.3f, -0.4f, 0.87f), new Vector3(-2f, 3f, 0f), Motion.Smooth),
        };

        private static readonly Vector3 HammerReadyGrip = new Vector3(0.28f, -0.37f, 0.6f);
        private static readonly Quaternion HammerReadyRotation = HammerRotation(new Vector3(0.4f, 0.62f, 0.68f), new Vector3(-0.55f, -0.35f, 0.76f));
        private static readonly Vector3 HammerChargeGrip = new Vector3(0.24f, -0.38f, 0.48f);
        private static readonly Quaternion HammerChargeRotation = HammerRotation(new Vector3(0.2f, 0.85f, 0.48f), new Vector3(-0.6f, -0.3f, 0.74f));

        private static readonly Vector3 GuardWrist = new Vector3(0.21f, -0.32f, 0.4f);
        private static readonly PunchKey[] PunchKeys =
        {
            new PunchKey(0f, GuardWrist, 25f, new Vector3(-0.21f, -0.32f, 0.4f), -25f, Vector3.zero, Motion.Smooth),
            new PunchKey(0.3f, new Vector3(0.28f, -0.36f, 0.28f), 15f, new Vector3(-0.19f, -0.29f, 0.45f), -30f, new Vector3(-2f, 10f, 1f), Motion.Smooth),
            new PunchKey(0.55f, new Vector3(0.06f, -0.17f, 0.82f), 75f, new Vector3(-0.25f, -0.35f, 0.3f), -15f, new Vector3(4f, -14f, -2f), Motion.Accelerate),
            new PunchKey(0.64f, new Vector3(0.06f, -0.18f, 0.8f), 75f, new Vector3(-0.25f, -0.35f, 0.3f), -15f, new Vector3(4f, -13f, -2f), Motion.Linear),
            new PunchKey(1f, GuardWrist, 25f, new Vector3(-0.21f, -0.32f, 0.4f), -25f, Vector3.zero, Motion.Smooth),
        };

        public static FirstPersonRig Instance { get; private set; }

        [Header("Parts")]
        [Tooltip("Everything that shows. The script stays on the object above it, so it keeps running while this is hidden.")]
        public GameObject content;
        public ArmParts rightArm;
        public ArmParts leftArm;
        public Transform hammer;

        [Header("Holding cargo")]
        [Tooltip("How far in front of your eyes the near side of held cargo is. Wider cargo goes farther out, as well as this much for each metre of half width.")]
        public float holdDistance = 0.5f;
        public float holdDistancePerWidth = 0.95f;
        [Tooltip("How far below the middle of the view the top of held cargo stays, so you can see where you're going.")]
        public float holdBelowDegrees = 7f;
        [Tooltip("How far held cargo is tipped forward, so its face looks back at you.")]
        public float holdTiltDegrees = 30f;
        public float pickUpSeconds = 0.2f;

        [Header("Body")]
        [Tooltip("The upper body turns around this point.")]
        public Vector3 chest = new Vector3(0f, -0.55f, -0.05f);
        [Tooltip("Where the right shoulder is. The left is its mirror.")]
        public Vector3 shoulderRight = new Vector3(0.3f, -0.5f, -0.1f);
        [Tooltip("The way the right elbow bends. The left is its mirror.")]
        public Vector3 elbowDirection = new Vector3(0.65f, -1f, -0.3f);

        [Header("Running")]
        [Tooltip("How far the shoulders turn with each stride, in degrees.")]
        public float runTwist = 3.2f;
        public Vector3 restWrist = new Vector3(0.3f, -0.35f, 0.5f);
        public Vector3 runWristForward = new Vector3(0.2f, -0.2f, 0.5f);
        public Vector3 runWristMiddle = new Vector3(0.34f, -0.4f, 0.46f);
        public Vector3 runWristBack = new Vector3(0.42f, -0.62f, 0.22f);

        [Header("Feel")]
        public float swayPerDegree = 0.0028f;
        public float swayLimit = 0.05f;
        public float swaySpring = 90f;
        public float torsoSpring = 240f;
        public float coCarryReachDegrees = 35f;

        private readonly Arm right = new Arm { Side = 1f };
        private readonly Arm left = new Arm { Side = -1f };
        private GameConfig config;
        private Vector2 swayNow;
        private Vector2 swayVelocity;
        private Vector3 torso;
        private Vector3 torsoVelocity;
        private Vector3 torsoTarget;
        private Vector3 hammerPosition;
        private Vector3 hammerVelocity;
        private Quaternion hammerRotation = Quaternion.identity;
        private bool hammerPlaced;
        private float gripBlend;
        private float swingWeight;
        private CargoItem lastCarried;
        private float tossUntil;
        private float pushUntil;
        private float knockedAt = -100f;
        private bool wasKnocked;
        private bool colorsApplied;
        private bool placed;

        public float PickUpSeconds => pickUpSeconds;

        private Quaternion HoldTilt => Quaternion.Euler(holdTiltDegrees, 0f, 0f);

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

        /// <summary>A hit from this direction in the world throws the upper body back and round.</summary>
        public void Flinch(Vector3 push)
        {
            float side = Vector3.Dot(push, transform.right);
            torsoVelocity += new Vector3(-170f, side * 230f, -side * 150f);
        }

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
            right.Parts = rightArm;
            left.Parts = leftArm;
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
                hammerPlaced = false;
                return;
            }

            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.033f);
            ApplyTeamColor(local);
            NoticeChanges(local);
            MoveBody(local, deltaTime);

            CargoItem carried = local.CarriedCargo;
            bool knocked = local.IsKnockedDown;
            bool swinging = local.Swinging.Value && !knocked && carried == null;
            bool holdsHammer = local.Side == Side.Interrupter && carried == null && !knocked;
            swingWeight = Mathf.MoveTowards(swingWeight, swinging ? 1f : 0f, deltaTime * (swinging ? 16f : 8f));
            gripBlend = Mathf.MoveTowards(gripBlend, holdsHammer ? 1f : 0f, deltaTime / 0.14f);
            if (hammer.gameObject.activeSelf != holdsHammer)
            {
                hammer.gameObject.SetActive(holdsHammer);
                hammerPlaced = false;
            }

            float stride = FollowCamera.Stride;
            float walk = FollowCamera.WalkAmount;
            torsoTarget = new Vector3(0f, -stride * runTwist * walk, stride * runTwist * 0.4f * walk);

            ArmPose rightPose;
            ArmPose leftPose;
            float stiffness;
            if (holdsHammer)
            {
                HammerPoses(local, stride, walk, deltaTime, out rightPose, out leftPose);
                stiffness = 500f;
            }
            else
            {
                ChoosePoses(local, carried, knocked, stride, walk, out rightPose, out leftPose, out stiffness);
            }

            if (!placed)
            {
                placed = true;
                // Coming up from below the view, rather than appearing in place.
                right.Wrist = rightPose.Wrist + Vector3.down * 0.35f;
                left.Wrist = leftPose.Wrist + Vector3.down * 0.35f;
                right.Velocity = Vector3.zero;
                left.Velocity = Vector3.zero;
            }
            PlaceArm(right, rightPose, stiffness, deltaTime);
            PlaceArm(left, leftPose, stiffness, deltaTime);
        }

        /// <summary>The upper body: springs toward wherever the current action wants it, and trails behind the view when it turns.</summary>
        private void MoveBody(PlayerAvatar local, float deltaTime)
        {
            Vector2 look = local.LookThisFrame;
            Vector2 target = Vector2.ClampMagnitude(new Vector2(-look.x, -look.y) * swayPerDegree / Mathf.Max(deltaTime, 0.008f) * 0.016f, swayLimit);
            swayVelocity += (target - swayNow) * (swaySpring * deltaTime);
            swayVelocity *= Mathf.Exp(-14f * deltaTime);
            swayNow += swayVelocity * deltaTime;

            float damping = 2f * Mathf.Sqrt(torsoSpring) * 0.85f;
            torsoVelocity += (torsoTarget - torso) * (torsoSpring * deltaTime);
            torsoVelocity *= Mathf.Exp(-damping * deltaTime);
            torso += torsoVelocity * deltaTime;

            // Hands hang from the body, so they stay nearly level in the world while the head bobs over them.
            float walk = FollowCamera.WalkAmount;
            float phase = FollowCamera.WalkPhase;
            Vector3 bounce = new Vector3(-Mathf.Cos(phase) * 0.01f, -Mathf.Abs(Mathf.Sin(phase)) * 0.016f, 0f) * walk;
            float breath = Mathf.Sin(Time.time * 1.7f) * 0.004f * (1f - walk);

            Quaternion turn = Quaternion.Euler(torso.x + swayNow.y * 60f, torso.y - swayNow.x * 60f, torso.z + swayNow.x * 40f);
            transform.localRotation = turn;
            transform.localPosition = chest - turn * chest + new Vector3(swayNow.x, swayNow.y + breath, 0f) + bounce;
        }

        private void NoticeChanges(PlayerAvatar local)
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

        // ---- The hammer: held in both hands, so the hands simply go where it goes ----

        private void HammerPoses(PlayerAvatar local, float stride, float walk, float deltaTime, out ArmPose rightPose, out ArmPose leftPose)
        {
            Vector3 grip = HammerReadyGrip + new Vector3(0f, Mathf.Abs(stride) * 0.008f * walk, stride * 0.022f * walk);
            Quaternion rotation = HammerReadyRotation * Quaternion.Euler(stride * 2.5f * walk, 0f, -stride * 2f * walk);
            if (local.IsDashing)
            {
                grip = HammerChargeGrip;
                rotation = HammerChargeRotation;
                torsoTarget = new Vector3(5f, 18f, -3f);
            }

            if (swingWeight > 0f)
            {
                SampleHammer(local.Visual != null ? local.Visual.SwingCycle : 0f, out Vector3 swingGrip, out Quaternion swingRotation, out Vector3 swingTorso);
                grip = Vector3.Lerp(grip, swingGrip, swingWeight);
                rotation = Quaternion.Slerp(rotation, swingRotation, swingWeight);
                torsoTarget = Vector3.Lerp(torsoTarget, swingTorso, swingWeight);
            }

            if (!hammerPlaced)
            {
                hammerPlaced = true;
                hammerPosition = grip + Vector3.down * 0.3f;
                hammerRotation = rotation;
                hammerVelocity = Vector3.zero;
            }

            if (swingWeight >= 1f)
            {
                // The keyframes are sharp on purpose, so they aren't softened by a spring.
                hammerPosition = grip;
                hammerRotation = rotation;
                hammerVelocity = Vector3.zero;
            }
            else
            {
                const float stiffness = 300f;
                float damping = 2f * Mathf.Sqrt(stiffness) * 0.85f;
                hammerVelocity += (grip - hammerPosition) * (stiffness * deltaTime);
                hammerVelocity *= Mathf.Exp(-damping * deltaTime);
                hammerPosition += hammerVelocity * deltaTime;
                hammerRotation = Quaternion.Slerp(hammerRotation, rotation, 1f - Mathf.Exp(-20f * deltaTime));
            }

            hammer.SetLocalPositionAndRotation(hammerPosition, hammerRotation);
            Vector3 handle = hammerRotation * Vector3.up;
            rightPose = ArmPose.OnHandleAt(hammerPosition, handle);
            leftPose = ArmPose.OnHandleAt(hammerPosition + handle * LeftGripAlongHandle, handle);
        }

        private static void SampleHammer(float cycle, out Vector3 grip, out Quaternion rotation, out Vector3 bodyTurn)
        {
            for (int i = 0; i < HammerKeys.Length - 1; i++)
            {
                HammerKey to = HammerKeys[i + 1];
                if (cycle > to.Time) continue;
                HammerKey from = HammerKeys[i];
                float t = Ease(Mathf.InverseLerp(from.Time, to.Time, cycle), to.Into);
                grip = Vector3.Lerp(from.Grip, to.Grip, t);
                rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
                bodyTurn = Vector3.Lerp(from.Torso, to.Torso, t);
                return;
            }
            grip = HammerKeys[0].Grip;
            rotation = HammerKeys[0].Rotation;
            bodyTurn = HammerKeys[0].Torso;
        }

        /// <summary>The handle comes out exactly as asked. The strike direction is only used to decide which way the head faces.</summary>
        private static Quaternion HammerRotation(Vector3 handle, Vector3 strike)
        {
            Vector3 up = handle.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(strike, up).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        private static float Ease(float t, Motion motion)
        {
            switch (motion)
            {
                case Motion.Accelerate: return t * t * t;
                case Motion.Decelerate: return 1f - (1f - t) * (1f - t);
                case Motion.Linear: return t;
                default: return t * t * (3f - 2f * t);
            }
        }

        // ---- Everything else ----

        private void ChoosePoses(PlayerAvatar local, CargoItem carried, bool knocked, float stride, float walk,
            out ArmPose rightPose, out ArmPose leftPose, out float stiffness)
        {
            stiffness = 200f;
            rightPose = RunPose(1f, stride, walk);
            leftPose = RunPose(-1f, -stride, walk);

            if (knocked)
            {
                KnockedPoses(out rightPose, out leftPose);
                torsoTarget = new Vector3(-10f, 0f, 0f);
                stiffness = 130f;
                return;
            }

            if (local.IsDashing)
            {
                // Fists up and the shoulder leading into the charge.
                rightPose = ArmPose.Straight(new Vector3(0.25f, -0.35f, 0.34f), 35f, HandShape.Fist);
                leftPose = ArmPose.Straight(new Vector3(-0.03f, -0.27f, 0.52f), -55f, HandShape.Fist);
                torsoTarget = new Vector3(5f, 18f, -3f);
                stiffness = 500f;
                return;
            }

            if (carried != null)
            {
                if (carried.Definition.requiredCarriers > 1)
                {
                    CoCarryPoses(carried, walk, stride, out rightPose, out leftPose);
                }
                else
                {
                    HoldPoses(carried, out rightPose, out leftPose);
                    // Bending to lift it, and straightening up as it comes.
                    if (carried.CarryBlend < 0.98f) torsoTarget.x += 10f;
                }
                stiffness = 900f;
                return;
            }

            if (Time.time < tossUntil)
            {
                // Heaving it up and away, toward the truck.
                rightPose = ArmPose.Fixed(new Vector3(0.22f, -0.1f, 0.7f), Quaternion.Euler(-72f, -8f, -12f), HandShape.Open);
                leftPose = ArmPose.Fixed(new Vector3(-0.22f, -0.1f, 0.7f), Quaternion.Euler(-72f, 8f, 12f), HandShape.Open);
                torsoTarget = new Vector3(-8f, 0f, 0f);
                stiffness = 340f;
                return;
            }

            if (Time.time < pushUntil)
            {
                rightPose = ArmPose.Fixed(new Vector3(0.26f, -0.28f, 0.7f), Quaternion.Euler(-30f, -8f, -12f), HandShape.Open);
                leftPose = ArmPose.Fixed(new Vector3(-0.26f, -0.28f, 0.7f), Quaternion.Euler(-30f, 8f, 12f), HandShape.Open);
                torsoTarget = new Vector3(5f, 0f, 0f);
                stiffness = 340f;
                return;
            }

            if (swingWeight > 0f) PunchPoses(local, ref rightPose, ref leftPose, ref stiffness);
        }

        /// <summary>
        /// Arms pumping in time with the feet: `stride` is 1 with this hand as far forward as it goes, which
        /// is when the opposite foot lands. It comes up into view going forward and drops out of it going back.
        /// </summary>
        private ArmPose RunPose(float side, float stride, float walk)
        {
            float u = (stride + 1f) * 0.5f;
            Vector3 running = Vector3.Lerp(Vector3.Lerp(runWristBack, runWristMiddle, u), Vector3.Lerp(runWristMiddle, runWristForward, u), u);
            Vector3 wrist = Vector3.Lerp(restWrist, running, walk);
            wrist.x *= side;
            // Palms turned in toward each other, as hands are when they swing.
            return ArmPose.Straight(wrist, -side * 70f, HandShape.Relaxed);
        }

        private void PunchPoses(PlayerAvatar local, ref ArmPose rightPose, ref ArmPose leftPose, ref float stiffness)
        {
            float cycle = local.Visual != null ? local.Visual.SwingCycle : 0f;
            // Right, left, right, left.
            float side = local.Visual != null && local.Visual.SwingIndex % 2 != 0 ? -1f : 1f;

            PunchKey from = PunchKeys[0];
            PunchKey to = PunchKeys[1];
            for (int i = 0; i < PunchKeys.Length - 1; i++)
            {
                if (cycle > PunchKeys[i + 1].Time) continue;
                from = PunchKeys[i];
                to = PunchKeys[i + 1];
                break;
            }
            float t = Ease(Mathf.InverseLerp(from.Time, to.Time, cycle), to.Into);

            Vector3 punching = Vector3.Lerp(from.Punching, to.Punching, t);
            Vector3 guarding = Vector3.Lerp(from.Guarding, to.Guarding, t);
            float punchingRoll = Mathf.Lerp(from.PunchingRoll, to.PunchingRoll, t);
            float guardingRoll = Mathf.Lerp(from.GuardingRoll, to.GuardingRoll, t);
            Vector3 bodyTurn = Vector3.Lerp(from.Torso, to.Torso, t);

            ArmPose main = ArmPose.Straight(new Vector3(punching.x * side, punching.y, punching.z), punchingRoll * side, HandShape.Fist);
            ArmPose other = ArmPose.Straight(new Vector3(guarding.x * side, guarding.y, guarding.z), guardingRoll * side, HandShape.Fist);
            ArmPose rightTarget = side > 0f ? main : other;
            ArmPose leftTarget = side > 0f ? other : main;
            rightPose = ArmPose.Lerp(rightPose, rightTarget, swingWeight);
            leftPose = ArmPose.Lerp(leftPose, leftTarget, swingWeight);
            torsoTarget = Vector3.Lerp(torsoTarget, new Vector3(bodyTurn.x, bodyTurn.y * side, bodyTurn.z * side), swingWeight);
            stiffness = Mathf.Lerp(stiffness, 1000f, swingWeight);
        }

        private void KnockedPoses(out ArmPose rightPose, out ArmPose leftPose)
        {
            float since = Time.time - knockedAt;
            float flail = Mathf.Sin(since * 26f) * 0.03f;
            // Thrown up by the hit, then gone from view while you lie there, then back as you get up.
            float up = since < 0.32f ? 1f : Mathf.Clamp01(1f - (since - 0.32f) / 0.25f);
            float height = Mathf.Lerp(-0.72f, 0.03f, up);
            rightPose = ArmPose.Straight(new Vector3(0.38f, height + flail, 0.46f), -30f, HandShape.Open);
            leftPose = ArmPose.Straight(new Vector3(-0.38f, height - flail, 0.46f), 30f, HandShape.Open);
        }

        private void HoldPoses(CargoItem carried, out ArmPose rightPose, out ArmPose leftPose)
        {
            // Holds on to wherever the cargo really is, so the hands bring it up from the ground.
            Vector3 half = carried.HalfExtents;
            Vector3 anchor = Vector3.Lerp(transform.InverseTransformPoint(carried.CarryFrom), HoldPoint(carried), carried.CarryBlend);
            Quaternion tilt = HoldTilt;
            float shift = Mathf.Cos(FollowCamera.WalkPhase) * 0.008f * FollowCamera.WalkAmount;
            // Hands round the near corners with the fingers curling onto the side facing you, because
            // anything farther back along the sides would be hidden behind the cargo.
            float outward = half.x + GripReach;
            float up = half.y * 0.02f;
            float near = -half.z - 0.035f;
            rightPose = ArmPose.Fixed(anchor + tilt * new Vector3(outward, up + shift, near),
                tilt * Quaternion.LookRotation(new Vector3(-1f, 0.2f, 0f), Vector3.back), HandShape.Open);
            leftPose = ArmPose.Fixed(anchor + tilt * new Vector3(-outward, up - shift, near),
                tilt * Quaternion.LookRotation(new Vector3(1f, 0.2f, 0f), Vector3.back), HandShape.Open);
        }

        private void CoCarryPoses(CargoItem carried, float walk, float stride, out ArmPose rightPose, out ArmPose leftPose)
        {
            // The handles are on the ends of the chest. The hands stay on them in the world while you can see them.
            Vector3 half = carried.HalfExtents;
            Vector3 toYou = transform.position - carried.transform.position;
            float side = toYou.x >= 0f ? 1f : -1f;
            Vector3 handle = carried.transform.position + new Vector3(side * (half.x + 0.03f), 0.08f, 0f);
            Vector3 gripA = transform.InverseTransformPoint(handle + Vector3.forward * 0.17f);
            Vector3 gripB = transform.InverseTransformPoint(handle + Vector3.back * 0.17f);
            Vector3 gripRight = gripA.x > gripB.x ? gripA : gripB;
            Vector3 gripLeft = gripA.x > gripB.x ? gripB : gripA;

            float inView = gripRight.z < 0.1f ? 0f : Mathf.Clamp01(1.6f - Mathf.Abs(gripRight.y / gripRight.z)) * Mathf.Clamp01(1.8f - Mathf.Abs(gripRight.x / gripRight.z));
            ArmPose holdRight = ArmPose.Straight(gripRight, 0f, HandShape.Open);
            ArmPose holdLeft = ArmPose.Straight(gripLeft, 0f, HandShape.Open);

            // You usually walk with the chest beside you and look ahead. Both arms still reach out that way,
            // low on the screen, so you can see what you're holding and where it is. Behind you, they're out of sight.
            Vector3 toHandle = transform.InverseTransformPoint(handle) - chest;
            float toward = Mathf.Atan2(toHandle.x, toHandle.z) * Mathf.Rad2Deg;
            Quaternion reach = Quaternion.Euler(0f, Mathf.Clamp(toward, -coCarryReachDegrees, coCarryReachDegrees), 0f);
            float behind = Mathf.InverseLerp(115f, 150f, Mathf.Abs(toward));
            Vector3 middle = chest + reach * Vector3.forward * 0.7f + Vector3.up * (0.19f - behind * 0.4f);
            Vector3 across = reach * Vector3.right * 0.19f;
            Vector3 heave = Vector3.up * (0.012f * walk * stride);
            ArmPose strainRight = ArmPose.Straight(middle + across + heave, 0f, HandShape.Fist);
            ArmPose strainLeft = ArmPose.Straight(middle - across - heave, 0f, HandShape.Fist);
            rightPose = ArmPose.Lerp(strainRight, holdRight, inView);
            leftPose = ArmPose.Lerp(strainLeft, holdLeft, inView);
        }

        // ---- Turning a pose into an arm ----

        private void PlaceArm(Arm arm, in ArmPose pose, float stiffness, float deltaTime)
        {
            Vector3 shoulder = new Vector3(shoulderRight.x * arm.Side, shoulderRight.y, shoulderRight.z);
            Vector3 bend = new Vector3(elbowDirection.x * arm.Side, elbowDirection.y, elbowDirection.z);

            Vector3 wrist = pose.Wrist;
            Vector3 elbow = FindElbow(shoulder, wrist, bend);
            Vector3 along = (wrist - elbow).normalized;
            Quaternion direction;
            switch (pose.Aim)
            {
                case HandAim.OnHandle:
                    // The handle is round, so the hand can turn on it to line up with the forearm.
                    Vector3 across = Vector3.ProjectOnPlane(along, pose.Handle);
                    if (across.sqrMagnitude < 0.001f) across = Vector3.ProjectOnPlane(Vector3.forward, pose.Handle);
                    direction = Quaternion.LookRotation(across.normalized, pose.Handle);
                    wrist = pose.Wrist - direction * Vector3.forward * FistToHandle;
                    break;
                case HandAim.AlongForearm:
                    direction = LookAlong(along) * Quaternion.Euler(0f, 0f, pose.Roll);
                    break;
                default:
                    direction = pose.Direction;
                    break;
            }

            if (pose.Aim == HandAim.OnHandle && gripBlend >= 1f)
            {
                // Locked to the hammer, or the fists would slide along its handle in a fast swing.
                arm.Wrist = wrist;
                arm.Velocity = Vector3.zero;
                arm.Hand = direction;
            }
            else
            {
                float damping = 2f * Mathf.Sqrt(stiffness) * 0.85f;
                arm.Velocity += (wrist - arm.Wrist) * (stiffness * deltaTime);
                arm.Velocity *= Mathf.Exp(-damping * deltaTime);
                arm.Wrist += arm.Velocity * deltaTime;
                arm.Hand = Quaternion.Slerp(arm.Hand, direction, 1f - Mathf.Exp(-Mathf.Sqrt(stiffness) * 1.2f * deltaTime));
            }

            elbow = FindElbow(shoulder, arm.Wrist, bend);
            arm.Parts.hand.SetLocalPositionAndRotation(arm.Wrist, arm.Hand);
            arm.Parts.forearm.SetLocalPositionAndRotation(arm.Wrist, LookAlong(arm.Wrist - elbow));
            arm.Parts.upperArm.SetLocalPositionAndRotation(elbow, LookAlong(elbow - shoulder));
            SetShape(arm, pose.Shape);
        }

        /// <summary>
        /// Where the elbow has to be for the hand to reach the wrist. Past the arm's full reach, the forearm
        /// stays attached to the hand and it's the shoulder end that comes apart, which is off screen.
        /// </summary>
        private static Vector3 FindElbow(Vector3 shoulder, Vector3 wrist, Vector3 bend)
        {
            Vector3 toWrist = wrist - shoulder;
            float distance = toWrist.magnitude;
            Vector3 axis = distance > 0.0001f ? toWrist / distance : Vector3.forward;
            if (distance >= UpperArmLength + ForearmLength - 0.001f) return wrist - axis * ForearmLength;

            distance = Mathf.Max(distance, Mathf.Abs(UpperArmLength - ForearmLength) + 0.01f);
            float along = (UpperArmLength * UpperArmLength - ForearmLength * ForearmLength + distance * distance) / (2f * distance);
            float height = Mathf.Sqrt(Mathf.Max(UpperArmLength * UpperArmLength - along * along, 0f));
            Vector3 side = Vector3.ProjectOnPlane(bend, axis);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : Vector3.down;
            return shoulder + axis * along + side * height;
        }

        private static Quaternion LookAlong(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.97f ? Vector3.back : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

        private static void SetShape(Arm arm, HandShape shape)
        {
            if (arm.ShapeSet && arm.Shape == shape) return;
            arm.ShapeSet = true;
            arm.Shape = shape;
            arm.Parts.relaxed.SetActive(shape == HandShape.Relaxed);
            arm.Parts.open.SetActive(shape == HandShape.Open);
            arm.Parts.fist.SetActive(shape == HandShape.Fist);
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
    }
}
