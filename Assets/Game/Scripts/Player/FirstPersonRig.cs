using System;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The arms, gloves and tools you see in first person, posed in code because where the hands go
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

        private enum Tool { None, Hammer, Vacuum }

        private enum Motion { Smooth, Accelerate, Decelerate, Linear }

        private enum HandAim
        {
            /// <summary>The hand has its own fixed direction.</summary>
            Fixed,
            /// <summary>The hand carries straight on from the forearm, so the wrist never looks broken.</summary>
            AlongForearm,
            /// <summary>The hand is closed round a handle, turned so it's as straight as the handle allows.</summary>
            OnHandle,
            /// <summary>Flat against something, with `Handle` the way the back of the hand faces and the fingers pointing away from you.</summary>
            FlatOn,
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

            public static ArmPose FlatAgainst(Vector3 wrist, Vector3 backOfHand, HandShape shape) =>
                new ArmPose { Wrist = wrist, Aim = HandAim.FlatOn, Handle = backOfHand, Direction = Quaternion.identity, Shape = shape };

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
            /// <summary>From 0 when it has just taken hold of a handle to 1 once it's there. Until then it moves there on a spring.</summary>
            public float Attached;
        }

        /// <summary>One moment of a tool's swing, held in both hands. Handle is the way the grip runs, and Strike is the way its business end points.</summary>
        private struct ToolKey
        {
            public float Time;
            public Vector3 Grip;
            public Quaternion Rotation;
            public Vector3 Torso;
            public Motion Into;

            public ToolKey(float time, Vector3 grip, Vector3 handle, Vector3 strike, Vector3 torso, Motion into)
            {
                Time = time;
                Grip = grip;
                Rotation = ToolRotation(handle, strike);
                Torso = torso;
                Into = into;
            }
        }

        /// <summary>
        /// One moment of the flourish you get by pressing F. Angles are the tool's way up, with the grip straight
        /// up and the business end pointing away from you at 0, 0, 0. Flip then spins it around the hand and Twist
        /// turns it around the grip's own axis, which are separate so they can go past a whole turn.
        /// </summary>
        private struct InspectKey
        {
            public float Time;
            public Vector3 Grip;
            public Vector3 Angles;
            public float Flip;
            public float Twist;
            public Vector3 Torso;
            public bool RightLetGo;
            public bool LeftLetGo;
            public Vector3 LeftWrist;
            public HandShape LeftShape;
            public Motion Into;
            public string Sound;

            public InspectKey(float time, Vector3 grip, Vector3 angles, float flip = 0f, float twist = 0f, Vector3 torso = default,
                bool rightLetGo = false, bool leftLetGo = false, Vector3 leftWrist = default, HandShape leftShape = HandShape.Relaxed,
                Motion into = Motion.Smooth, string sound = null)
            {
                Time = time;
                Grip = grip;
                Angles = angles;
                Flip = flip;
                Twist = twist;
                Torso = torso;
                RightLetGo = rightLetGo;
                LeftLetGo = leftLetGo;
                LeftWrist = leftWrist;
                LeftShape = leftShape;
                Into = into;
                Sound = sound;
            }
        }

        // These lengths and the tools' grips have to match Tools/blender/make_hands.py.
        private const float ForearmLength = 0.62f;
        private const float UpperArmLength = 0.54f;
        private const float FistToHandle = 0.105f;
        private const float LeftGripAlongHandle = -0.27f;
        private const float VacuumForeGrip = 0.185f;
        private static readonly Vector3 VacuumMouth = new Vector3(0f, 0.112f, 0.436f);

        /// <summary>How far out from the side of held cargo the wrists are: the thickness of the hand, so its palm is flat against it.</summary>
        private const float HoldSideGap = 0.05f;

        /// <summary>How far back from the near edge of held cargo the wrists are, so you can see the hands holding it.</summary>
        private const float HoldWristBack = 0.13f;

        // In step with the Smash clip in make_character.py: the same moments, with the hit at 0.55.
        // Torso angles are lean forward, turn right and tilt left, in degrees.
        private static readonly ToolKey[] HammerKeys =
        {
            new ToolKey(0f, new Vector3(0.3f, -0.27f, 0.54f), new Vector3(0.3f, 0.85f, 0.42f), new Vector3(-0.3f, -0.4f, 0.87f), new Vector3(-2f, 3f, 0f), Motion.Smooth),
            new ToolKey(0.38f, new Vector3(0.27f, -0.07f, 0.38f), new Vector3(0.14f, 0.8f, -0.58f), new Vector3(-0.1f, 0.6f, 0.79f), new Vector3(-7f, 11f, 3f), Motion.Smooth),
            new ToolKey(0.55f, new Vector3(0.08f, -0.29f, 0.6f), new Vector3(-0.07f, 0.06f, 0.995f), new Vector3(0f, -0.99f, 0.06f), new Vector3(7f, -8f, -2f), Motion.Accelerate),
            new ToolKey(0.6f, new Vector3(0.08f, -0.3f, 0.6f), new Vector3(-0.07f, 0.04f, 0.995f), new Vector3(0f, -0.99f, 0.04f), new Vector3(8f, -8f, -2f), Motion.Linear),
            new ToolKey(0.74f, new Vector3(0.13f, -0.27f, 0.58f), new Vector3(-0.02f, 0.3f, 0.95f), new Vector3(0f, -0.95f, 0.3f), new Vector3(5f, -5f, -1f), Motion.Decelerate),
            new ToolKey(1f, new Vector3(0.3f, -0.27f, 0.54f), new Vector3(0.3f, 0.85f, 0.42f), new Vector3(-0.3f, -0.4f, 0.87f), new Vector3(-2f, 3f, 0f), Motion.Smooth),
        };

        private static readonly Vector3 HammerReadyGrip = new Vector3(0.28f, -0.37f, 0.6f);
        private static readonly Quaternion HammerReadyRotation = ToolRotation(new Vector3(0.4f, 0.62f, 0.68f), new Vector3(-0.55f, -0.35f, 0.76f));
        private static readonly Vector3 HammerChargeGrip = new Vector3(0.24f, -0.38f, 0.48f);
        private static readonly Quaternion HammerChargeRotation = ToolRotation(new Vector3(0.2f, 0.85f, 0.48f), new Vector3(-0.6f, -0.3f, 0.74f));

        // Hauled up where you can see it, ready to come down.
        private static readonly Vector3 HammerRaisedGrip = new Vector3(0.25f, -0.2f, 0.52f);
        private static readonly Vector3 HammerRaisedHandle = new Vector3(-0.3f, 0.82f, 0.1f);
        private static readonly Vector3 HammerRaisedStrike = new Vector3(-0.05f, 0.25f, 0.96f);
        private static readonly Quaternion HammerRaisedRotation = ToolRotation(HammerRaisedHandle, HammerRaisedStrike);
        private static readonly Vector3 HammerRaisedTorso = new Vector3(-9f, 9f, 2f);

        // As a fraction of GameConfig.slamSeconds, with the hit at 0.28 to match the Slam clip.
        private static readonly ToolKey[] SlamKeys =
        {
            new ToolKey(0f, HammerRaisedGrip, HammerRaisedHandle, HammerRaisedStrike, HammerRaisedTorso, Motion.Smooth),
            new ToolKey(0.28f, new Vector3(0.05f, -0.45f, 0.72f), new Vector3(-0.05f, -0.28f, 0.96f), new Vector3(0f, -0.96f, -0.28f), new Vector3(14f, -7f, -2f), Motion.Accelerate),
            new ToolKey(0.46f, new Vector3(0.06f, -0.42f, 0.7f), new Vector3(-0.05f, -0.12f, 0.99f), new Vector3(0f, -0.99f, -0.12f), new Vector3(12f, -6f, -2f), Motion.Linear),
            new ToolKey(1f, HammerReadyGrip, new Vector3(0.4f, 0.62f, 0.68f), new Vector3(-0.55f, -0.35f, 0.76f), new Vector3(-2f, 3f, 0f), Motion.Smooth),
        };

        // Levelled at whatever's in the middle of the view, low enough to see past.
        private static readonly Vector3 VacuumReadyGrip = new Vector3(0.24f, -0.42f, 0.5f);
        private static readonly Vector3 VacuumReadyHandle = new Vector3(0.05f, 1f, -0.03f);
        private static readonly Vector3 VacuumReadyStrike = new Vector3(-0.05f, 0.06f, 1f);
        private static readonly Quaternion VacuumReadyRotation = ToolRotation(VacuumReadyHandle, VacuumReadyStrike);
        private static readonly Vector3 VacuumChargeGrip = new Vector3(0.2f, -0.46f, 0.42f);
        private static readonly Quaternion VacuumChargeRotation = ToolRotation(new Vector3(0.2f, 0.95f, 0.2f), new Vector3(-0.25f, -0.2f, 0.95f));

        // In step with the Blast clip: the air comes out at 0.18 and throws it up and back.
        private static readonly ToolKey[] BlastKeys =
        {
            new ToolKey(0f, VacuumReadyGrip, VacuumReadyHandle, VacuumReadyStrike, Vector3.zero, Motion.Smooth),
            new ToolKey(0.1f, new Vector3(0.24f, -0.43f, 0.53f), VacuumReadyHandle, new Vector3(-0.05f, 0.02f, 1f), new Vector3(2f, 0f, 0f), Motion.Smooth),
            new ToolKey(0.18f, new Vector3(0.25f, -0.385f, 0.41f), new Vector3(0.05f, 0.96f, -0.28f), new Vector3(-0.05f, 0.27f, 0.96f), new Vector3(-5f, 3f, 1f), Motion.Accelerate),
            new ToolKey(0.42f, new Vector3(0.24f, -0.41f, 0.47f), new Vector3(0.05f, 0.99f, -0.1f), new Vector3(-0.05f, 0.12f, 0.99f), new Vector3(-1f, 1f, 0f), Motion.Decelerate),
            new ToolKey(1f, VacuumReadyGrip, VacuumReadyHandle, VacuumReadyStrike, Vector3.zero, Motion.Smooth),
        };

        // The hammer goes round in the plane of the view, because end over end would bring its head into your face.
        private static readonly InspectKey[] HammerInspect =
        {
            // Up in front of you, and a slow look at one side of the head.
            new InspectKey(0f, new Vector3(0.2f, -0.32f, 0.62f), new Vector3(6f, 0f, 20f), twist: 30f),
            new InspectKey(0.42f, new Vector3(0.14f, -0.25f, 0.62f), new Vector3(0f, 0f, 34f), twist: -45f, torso: new Vector3(-2f, 3f, 0f)),
            // Turned round in the fingers to look at the other.
            new InspectKey(0.98f, new Vector3(0.14f, -0.24f, 0.63f), new Vector3(-4f, 0f, 32f), twist: 135f, torso: new Vector3(-2f, -2f, 0f)),
            // Let go with the left, and tip it back to throw it round.
            new InspectKey(1.24f, new Vector3(0.22f, -0.3f, 0.62f), new Vector3(4f, 0f, 8f), flip: -42f, twist: 180f, torso: new Vector3(-3f, 5f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), rightLetGo: true, sound: "swing"),
            // Twice round the hand, the way M9 owners do.
            new InspectKey(1.62f, new Vector3(0.2f, -0.22f, 0.63f), new Vector3(4f, 0f, 8f), flip: 318f, twist: 180f, torso: new Vector3(1f, 0f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), rightLetGo: true, into: Motion.Linear, sound: "swing"),
            new InspectKey(1.96f, new Vector3(0.21f, -0.24f, 0.63f), new Vector3(4f, 0f, 8f), flip: 700f, twist: 180f, torso: new Vector3(3f, -3f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), into: Motion.Linear),
            // Caught, with the weight of it pulling the hand down.
            new InspectKey(2.1f, new Vector3(0.24f, -0.36f, 0.62f), new Vector3(10f, 0f, 10f), flip: 734f, twist: 180f, torso: new Vector3(5f, -3f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), into: Motion.Decelerate, sound: "clunk"),
            new InspectKey(2.34f, new Vector3(0.22f, -0.31f, 0.62f), new Vector3(6f, 0f, 14f), flip: 720f, twist: 180f,
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.5f, 0.38f)),
            // Weighed in the other palm, twice.
            new InspectKey(2.72f, new Vector3(0.2f, -0.28f, 0.62f), new Vector3(8f, 0f, 66f), flip: 720f, twist: 180f, torso: new Vector3(1f, 4f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.2f, -0.32f, 0.56f), leftShape: HandShape.Open, sound: "smash"),
            new InspectKey(2.92f, new Vector3(0.21f, -0.22f, 0.62f), new Vector3(6f, 0f, 52f), flip: 720f, twist: 180f, torso: new Vector3(-1f, 3f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.2f, -0.29f, 0.56f), leftShape: HandShape.Open),
            new InspectKey(3.08f, new Vector3(0.2f, -0.28f, 0.62f), new Vector3(8f, 0f, 66f), flip: 720f, twist: 180f, torso: new Vector3(1f, 4f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.2f, -0.32f, 0.56f), leftShape: HandShape.Open, into: Motion.Accelerate, sound: "smash"),
            new InspectKey(3.5f, new Vector3(0.26f, -0.36f, 0.6f), new Vector3(22f, 0f, -28f), flip: 720f, twist: 180f),
        };

        private static readonly InspectKey[] VacuumInspect =
        {
            // Brought up to look along one side.
            new InspectKey(0f, new Vector3(0.22f, -0.4f, 0.52f), new Vector3(-3f, -4f, 0f)),
            new InspectKey(0.46f, new Vector3(0.2f, -0.3f, 0.54f), new Vector3(-6f, -62f, 12f), torso: new Vector3(-2f, 4f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.52f, 0.38f)),
            // And the other.
            new InspectKey(1.02f, new Vector3(0.12f, -0.3f, 0.56f), new Vector3(-8f, 52f, -14f), torso: new Vector3(-2f, -4f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.52f, 0.38f)),
            // Twirled on the hand like a pistol.
            new InspectKey(1.26f, new Vector3(0.24f, -0.36f, 0.5f), new Vector3(-4f, 0f, 0f), flip: -34f, torso: new Vector3(-3f, 4f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), rightLetGo: true, sound: "swing"),
            new InspectKey(1.64f, new Vector3(0.23f, -0.32f, 0.52f), new Vector3(-4f, 0f, 0f), flip: 326f, torso: new Vector3(1f, 0f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), rightLetGo: true, into: Motion.Linear, sound: "swing"),
            new InspectKey(1.98f, new Vector3(0.23f, -0.33f, 0.52f), new Vector3(-4f, 0f, 0f), flip: 704f, torso: new Vector3(3f, -3f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), into: Motion.Linear),
            new InspectKey(2.12f, new Vector3(0.25f, -0.44f, 0.52f), new Vector3(4f, 0f, 2f), flip: 732f, torso: new Vector3(4f, -3f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.32f, -0.54f, 0.36f), into: Motion.Decelerate, sound: "clunk"),
            new InspectKey(2.34f, new Vector3(0.24f, -0.4f, 0.5f), new Vector3(-2f, 0f, 0f), flip: 720f,
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.5f, 0.38f)),
            // A look down the wrong end of it, and it blows in your face.
            new InspectKey(2.78f, new Vector3(0.1f, -0.42f, 0.88f), new Vector3(-14f, 176f, 0f), flip: 720f, torso: new Vector3(3f, 0f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.5f, 0.4f)),
            new InspectKey(3f, new Vector3(0.1f, -0.42f, 0.88f), new Vector3(-14f, 176f, 0f), flip: 720f, torso: new Vector3(3f, 0f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.5f, 0.4f), sound: "blast"),
            new InspectKey(3.16f, new Vector3(0.12f, -0.46f, 0.82f), new Vector3(-30f, 176f, 0f), flip: 720f, torso: new Vector3(-6f, 0f, 0f),
                leftLetGo: true, leftWrist: new Vector3(-0.3f, -0.5f, 0.4f), into: Motion.Decelerate),
            new InspectKey(3.6f, VacuumReadyGrip, new Vector3(-3f, -4f, 0f), flip: 720f),
        };

        public static FirstPersonRig Instance { get; private set; }

        [Header("Parts")]
        [Tooltip("Everything that shows. The script stays on the object above it, so it keeps running while this is hidden.")]
        public GameObject content;
        public ArmParts rightArm;
        public ArmParts leftArm;
        public Transform hammer;
        public Transform vacuum;

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
        private Vector3 toolPosition;
        private Vector3 toolVelocity;
        private Quaternion toolRotation = Quaternion.identity;
        private bool toolPlaced;
        private Tool toolInHand;
        private float swingWeight;
        private float suckWeight;
        private float inspectTime = -1f;
        private float inspectWeight;
        private int inspectSoundsPlayed;
        private InspectKey inspectNow;
        private float inspectRegrab;
        private CargoItem lastCarried;
        private float tossUntil;
        private float pushUntil;
        private float knockedAt = -100f;
        private bool wasKnocked;
        private bool colorsApplied;
        private bool placed;

        public float PickUpSeconds => pickUpSeconds;

        /// <summary>Where the vacuum's air comes out, in the world.</summary>
        public Vector3 VacuumMouthWorld => vacuum.TransformPoint(VacuumMouth);

        public bool ShowsTool => content.activeSelf && toolInHand != Tool.None;

        public bool Inspecting => inspectTime >= 0f;

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

        /// <summary>F: a look at whatever's in your hands. Anything you do with it stops the show.</summary>
        public void StartInspecting()
        {
            if (!ShowsTool || Inspecting || swingWeight > 0.1f) return;
            inspectTime = 0f;
            inspectSoundsPlayed = 0;
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
                toolPlaced = false;
                toolInHand = Tool.None;
                inspectTime = -1f;
                inspectWeight = 0f;
                return;
            }

            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.033f);
            ApplyTeamColor(local);
            NoticeChanges(local);
            MoveBody(local, deltaTime);

            CargoItem carried = local.CarriedCargo;
            bool knocked = local.IsKnockedDown;
            Tool tool = ToolFor(local, carried, knocked);
            if (tool != toolInHand)
            {
                toolInHand = tool;
                toolPlaced = false;
                inspectTime = -1f;
                if (hammer.gameObject.activeSelf != (tool == Tool.Hammer)) hammer.gameObject.SetActive(tool == Tool.Hammer);
                if (vacuum.gameObject.activeSelf != (tool == Tool.Vacuum)) vacuum.gameObject.SetActive(tool == Tool.Vacuum);
            }

            bool swinging = local.Swinging.Value && tool != Tool.None;
            swingWeight = Mathf.MoveTowards(swingWeight, swinging ? 1f : 0f, deltaTime * (swinging ? 16f : 8f));
            suckWeight = Mathf.MoveTowards(suckWeight, local.Sucking.Value ? 1f : 0f, deltaTime * 7f);
            UpdateInspect(local, swinging, deltaTime);

            float stride = FollowCamera.Stride;
            float walk = FollowCamera.WalkAmount;
            torsoTarget = new Vector3(0f, -stride * runTwist * walk, stride * runTwist * 0.4f * walk);

            ArmPose rightPose;
            ArmPose leftPose;
            float stiffness = 500f;
            if (tool == Tool.Hammer) HammerPoses(local, stride, walk, deltaTime, out rightPose, out leftPose);
            else if (tool == Tool.Vacuum) VacuumPoses(local, stride, walk, deltaTime, out rightPose, out leftPose);
            else ChoosePoses(carried, knocked, walk, stride, out rightPose, out leftPose, out stiffness);

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

        /// <summary>What's in your hands: nothing while they're busy or you're on the ground, and otherwise what your side carries.</summary>
        private Tool ToolFor(PlayerAvatar local, CargoItem carried, bool knocked)
        {
            if (carried != null || knocked || Time.time < tossUntil || Time.time < pushUntil) return Tool.None;
            return local.Side == Side.Interrupter ? Tool.Hammer : Tool.Vacuum;
        }

        // ---- The tools: held in both hands, so the hands simply go where they go ----

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

            bool sharp = swingWeight >= 1f;
            if (swingWeight > 0f)
            {
                SampleTool(HammerKeys, local.Visual != null ? local.Visual.SwingCycle : 0f, out Vector3 swingGrip, out Quaternion swingRotation, out Vector3 swingTorso);
                grip = Vector3.Lerp(grip, swingGrip, swingWeight);
                rotation = Quaternion.Slerp(rotation, swingRotation, swingWeight);
                torsoTarget = Vector3.Lerp(torsoTarget, swingTorso, swingWeight);
            }

            if (local.IsCharging)
            {
                // Shaking more the longer you hold it, until it's ready.
                float charge = local.Charge01;
                float up = Mathf.SmoothStep(0f, 1f, charge * 2.2f);
                float shake = charge * 0.006f;
                grip = Vector3.Lerp(grip, HammerRaisedGrip, up) + new Vector3(Mathf.Sin(Time.time * 41f), Mathf.Sin(Time.time * 53f), 0f) * shake;
                rotation = Quaternion.Slerp(rotation, HammerRaisedRotation, up);
                torsoTarget = Vector3.Lerp(torsoTarget, HammerRaisedTorso, up);
                sharp = false;
            }
            else if (local.IsSlamming)
            {
                float t = 1f - Mathf.Clamp01((float)((local.SlammingUntil.Value - local.Now) / config.slamSeconds));
                SampleTool(SlamKeys, t, out grip, out rotation, out Vector3 slamTorso);
                torsoTarget = slamTorso;
                sharp = true;
            }

            ApplyInspect(HammerInspect, ref grip, ref rotation, ref sharp);
            PlaceTool(hammer, grip, rotation, sharp, deltaTime);
            Vector3 handle = toolRotation * Vector3.up;
            rightPose = ArmPose.OnHandleAt(toolPosition, handle);
            leftPose = ArmPose.OnHandleAt(toolPosition + handle * LeftGripAlongHandle, handle);
            InspectHands(ref rightPose, ref leftPose);
        }

        private void VacuumPoses(PlayerAvatar local, float stride, float walk, float deltaTime, out ArmPose rightPose, out ArmPose leftPose)
        {
            Vector3 grip = VacuumReadyGrip + new Vector3(0f, Mathf.Abs(stride) * 0.008f * walk, stride * 0.018f * walk);
            Quaternion rotation = VacuumReadyRotation * Quaternion.Euler(stride * 1.5f * walk, 0f, -stride * 1.5f * walk);
            if (local.IsDashing)
            {
                grip = VacuumChargeGrip;
                rotation = VacuumChargeRotation;
                torsoTarget = new Vector3(5f, 18f, -3f);
            }

            if (suckWeight > 0f)
            {
                // Braced against it and shaking with the pull.
                Vector3 shake = new Vector3(Mathf.Sin(Time.time * 47f), Mathf.Sin(Time.time * 61f), Mathf.Sin(Time.time * 37f)) * 0.0055f;
                grip += (new Vector3(-0.03f, 0.02f, 0.05f) + shake) * suckWeight;
                rotation *= Quaternion.Euler(Mathf.Sin(Time.time * 53f) * 0.8f * suckWeight, Mathf.Sin(Time.time * 43f) * 0.8f * suckWeight, 0f);
                torsoTarget = Vector3.Lerp(torsoTarget, new Vector3(4f, 0f, 0f), suckWeight);
            }

            bool sharp = swingWeight >= 1f;
            if (swingWeight > 0f)
            {
                SampleTool(BlastKeys, local.Visual != null ? local.Visual.SwingCycle : 0f, out Vector3 blastGrip, out Quaternion blastRotation, out Vector3 blastTorso);
                grip = Vector3.Lerp(grip, blastGrip, swingWeight);
                rotation = Quaternion.Slerp(rotation, blastRotation, swingWeight);
                torsoTarget = Vector3.Lerp(torsoTarget, blastTorso, swingWeight);
            }

            ApplyInspect(VacuumInspect, ref grip, ref rotation, ref sharp);
            PlaceTool(vacuum, grip, rotation, sharp, deltaTime);
            Vector3 handle = toolRotation * Vector3.up;
            rightPose = ArmPose.OnHandleAt(toolPosition, handle);
            leftPose = ArmPose.OnHandleAt(toolPosition + toolRotation * Vector3.forward * VacuumForeGrip, handle);
            InspectHands(ref rightPose, ref leftPose);
        }

        private void PlaceTool(Transform tool, Vector3 grip, Quaternion rotation, bool sharp, float deltaTime)
        {
            if (!toolPlaced)
            {
                toolPlaced = true;
                toolPosition = grip + Vector3.down * 0.3f;
                toolRotation = rotation;
                toolVelocity = Vector3.zero;
            }

            if (sharp)
            {
                // The keyframes are sharp on purpose, so they aren't softened by a spring.
                toolPosition = grip;
                toolRotation = rotation;
                toolVelocity = Vector3.zero;
            }
            else
            {
                const float stiffness = 300f;
                float damping = 2f * Mathf.Sqrt(stiffness) * 0.85f;
                toolVelocity += (grip - toolPosition) * (stiffness * deltaTime);
                toolVelocity *= Mathf.Exp(-damping * deltaTime);
                toolPosition += toolVelocity * deltaTime;
                toolRotation = Quaternion.Slerp(toolRotation, rotation, 1f - Mathf.Exp(-20f * deltaTime));
            }
            tool.SetLocalPositionAndRotation(toolPosition, toolRotation);
        }

        private static void SampleTool(ToolKey[] keys, float cycle, out Vector3 grip, out Quaternion rotation, out Vector3 bodyTurn)
        {
            for (int i = 0; i < keys.Length - 1; i++)
            {
                ToolKey to = keys[i + 1];
                if (cycle > to.Time) continue;
                ToolKey from = keys[i];
                float t = Ease(Mathf.InverseLerp(from.Time, to.Time, cycle), to.Into);
                grip = Vector3.Lerp(from.Grip, to.Grip, t);
                rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
                bodyTurn = Vector3.Lerp(from.Torso, to.Torso, t);
                return;
            }
            ToolKey last = keys[keys.Length - 1];
            grip = last.Grip;
            rotation = last.Rotation;
            bodyTurn = last.Torso;
        }

        /// <summary>The handle comes out exactly as asked. The strike direction is only used to decide which way the head faces.</summary>
        private static Quaternion ToolRotation(Vector3 handle, Vector3 strike)
        {
            Vector3 up = handle.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(strike, up).normalized;
            return Quaternion.LookRotation(forward, up);
        }

        // ---- F: looking at what's in your hands. Only you see this, as in CS2 ----

        private void UpdateInspect(PlayerAvatar local, bool swinging, float deltaTime)
        {
            // The click itself ends it. Waiting for the host would leave a client's flourish running into the swing.
            bool stop = swinging || local.WantsToUseTool || local.IsCharging || local.IsSlamming || local.Sucking.Value || local.IsDashing || local.IsStaggered;
            if (stop) inspectTime = -1f;
            if (inspectTime >= 0f)
            {
                inspectTime += deltaTime;
                InspectKey[] keys = toolInHand == Tool.Hammer ? HammerInspect : VacuumInspect;
                if (inspectTime >= keys[keys.Length - 1].Time) inspectTime = -1f;
            }
            inspectWeight = Mathf.MoveTowards(inspectWeight, inspectTime >= 0f ? 1f : 0f, deltaTime * (inspectTime >= 0f ? 7f : stop ? 10f : 4f));
        }

        private void ApplyInspect(InspectKey[] keys, ref Vector3 grip, ref Quaternion rotation, ref bool sharp)
        {
            if (inspectWeight <= 0f) return;
            // Blending out after the end, it holds where it finished.
            float time = inspectTime >= 0f ? inspectTime : keys[keys.Length - 1].Time;
            inspectRegrab = SampleInspect(keys, time, toolInHand == Tool.Hammer ? Vector3.forward : Vector3.right, out inspectNow, out Quaternion shown);
            if (inspectTime >= 0f) PlayInspectSounds(keys);

            grip = Vector3.Lerp(grip, inspectNow.Grip, inspectWeight);
            rotation = Quaternion.Slerp(rotation, shown, inspectWeight);
            torsoTarget = Vector3.Lerp(torsoTarget, inspectNow.Torso, inspectWeight);
            sharp = inspectWeight >= 1f;
        }

        /// <summary>Returns how far the left hand has got going back to the handle, from 0 to 1, when the next key holds the tool again.</summary>
        private static float SampleInspect(InspectKey[] keys, float time, Vector3 flipAxis, out InspectKey now, out Quaternion rotation)
        {
            InspectKey from = keys[0];
            InspectKey to = keys[keys.Length - 1];
            for (int i = 0; i < keys.Length - 1; i++)
            {
                if (time > keys[i + 1].Time) continue;
                from = keys[i];
                to = keys[i + 1];
                break;
            }
            float t = Ease(Mathf.InverseLerp(from.Time, to.Time, time), to.Into);
            now = from;
            now.Grip = Vector3.Lerp(from.Grip, to.Grip, t);
            now.Torso = Vector3.Lerp(from.Torso, to.Torso, t);
            // A key that holds the tool again has no wrist of its own. Heading for its zero would bring the hand into your face.
            bool regrab = from.LeftLetGo && !to.LeftLetGo;
            now.LeftWrist = regrab ? from.LeftWrist : Vector3.Lerp(from.LeftWrist, to.LeftWrist, t);
            Vector3 angles = Vector3.Lerp(from.Angles, to.Angles, t);
            float flip = Mathf.Lerp(from.Flip, to.Flip, t);
            float twist = Mathf.Lerp(from.Twist, to.Twist, t);
            rotation = Quaternion.Euler(angles) * Quaternion.AngleAxis(flip, flipAxis) * Quaternion.AngleAxis(twist, Vector3.up);
            return regrab ? t : 0f;
        }

        private void PlayInspectSounds(InspectKey[] keys)
        {
            while (inspectSoundsPlayed < keys.Length && keys[inspectSoundsPlayed].Time <= inspectTime)
            {
                string sound = keys[inspectSoundsPlayed].Sound;
                inspectSoundsPlayed++;
                if (string.IsNullOrEmpty(sound)) continue;
                GameAudio.Play(sound, sound == "blast" ? 0.35f : 0.3f);
                if (sound == "blast")
                {
                    // Right in the face.
                    FollowCamera.Flinch(Vector3.zero);
                    FxBurst.PlayBlast(VacuumMouthWorld, -transform.forward);
                }
            }
        }

        /// <summary>The right hand lets go to throw the tool round, and the left hand lets go to get out of the way.</summary>
        private void InspectHands(ref ArmPose rightPose, ref ArmPose leftPose)
        {
            if (inspectWeight < 1f) return;
            if (inspectNow.RightLetGo)
                rightPose = ArmPose.Straight(toolPosition + new Vector3(0.02f, -0.03f, -0.12f), 0f, HandShape.Open);
            if (inspectNow.LeftLetGo)
            {
                // Reaching back for the handle near the end. Taking hold of it happens at the next key, where the grip springs on.
                Vector3 wrist = Vector3.Lerp(inspectNow.LeftWrist, leftPose.Wrist, inspectRegrab * 0.8f);
                leftPose = ArmPose.Straight(wrist, inspectNow.LeftShape == HandShape.Open ? -150f : 30f, inspectNow.LeftShape);
            }
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

        private void ChoosePoses(CargoItem carried, bool knocked, float walk, float stride,
            out ArmPose rightPose, out ArmPose leftPose, out float stiffness)
        {
            if (knocked)
            {
                KnockedPoses(out rightPose, out leftPose);
                torsoTarget = new Vector3(-10f, 0f, 0f);
                stiffness = 130f;
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

            stiffness = 340f;
            if (Time.time < tossUntil)
            {
                // Heaving it up and away, toward the truck.
                rightPose = ArmPose.Fixed(new Vector3(0.22f, -0.1f, 0.7f), Quaternion.Euler(-72f, -8f, -12f), HandShape.Open);
                leftPose = ArmPose.Fixed(new Vector3(-0.22f, -0.1f, 0.7f), Quaternion.Euler(-72f, 8f, 12f), HandShape.Open);
                torsoTarget = new Vector3(-8f, 0f, 0f);
                return;
            }

            // Just let go of something, pushing it away.
            rightPose = ArmPose.Fixed(new Vector3(0.26f, -0.28f, 0.7f), Quaternion.Euler(-30f, -8f, -12f), HandShape.Open);
            leftPose = ArmPose.Fixed(new Vector3(-0.26f, -0.28f, 0.7f), Quaternion.Euler(-30f, 8f, 12f), HandShape.Open);
            torsoTarget = new Vector3(5f, 0f, 0f);
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
            // Cargo flies up into the hands, and they come up to meet it. Reaching down to the ground for
            // it would swing a whole arm across the view.
            Vector3 half = carried.HalfExtents;
            float rising = 1f - Mathf.Clamp01(carried.CarryBlend);
            Vector3 anchor = HoldPoint(carried) + new Vector3(0f, -0.4f, -0.1f) * rising;
            Quaternion tilt = HoldTilt;
            float shift = Mathf.Cos(FollowCamera.WalkPhase) * 0.008f * FollowCamera.WalkAmount;
            // The way anyone carries a box: palms flat on its sides, fingers going away from you and thumbs on top.
            float outward = half.x + HoldSideGap;
            float low = -half.y * 0.3f;
            float near = -half.z - HoldWristBack;
            rightPose = ArmPose.FlatAgainst(anchor + tilt * new Vector3(outward, low + shift, near), tilt * Vector3.right, HandShape.Open);
            leftPose = ArmPose.FlatAgainst(anchor + tilt * new Vector3(-outward, low - shift, near), tilt * Vector3.left, HandShape.Open);
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
                case HandAim.FlatOn:
                    // Mostly straight ahead. Following the forearm, which comes up from below, stands the fingers on end.
                    Vector3 fingers = Vector3.ProjectOnPlane(Vector3.Lerp(Vector3.forward, along, 0.2f), pose.Handle);
                    direction = Quaternion.LookRotation(fingers.normalized, pose.Handle);
                    break;
                case HandAim.AlongForearm:
                    direction = LookAlong(along) * Quaternion.Euler(0f, 0f, pose.Roll);
                    break;
                default:
                    direction = pose.Direction;
                    break;
            }

            arm.Attached = pose.Aim == HandAim.OnHandle ? Mathf.MoveTowards(arm.Attached, 1f, deltaTime / 0.14f) : 0f;
            if (arm.Attached >= 1f)
            {
                // Locked to the tool, or the fists would slide along its handle in a fast swing.
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
