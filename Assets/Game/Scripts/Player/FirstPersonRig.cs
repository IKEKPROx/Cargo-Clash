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
            [Tooltip("Closed round a handle. A punching fist is tighter.")]
            public GameObject grip;
        }

        private enum HandShape { Relaxed, Open, Fist, Grip }

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
            /// <summary>Put exactly where the pose says, with no spring: the inspect has already worked out every joint.</summary>
            Exact,
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
                new ArmPose { Wrist = grip, Aim = HandAim.OnHandle, Handle = handle, Direction = Quaternion.identity, Shape = HandShape.Grip };

            public static ArmPose FlatAgainst(Vector3 wrist, Vector3 backOfHand, HandShape shape) =>
                new ArmPose { Wrist = wrist, Aim = HandAim.FlatOn, Handle = backOfHand, Direction = Quaternion.identity, Shape = shape };

            public static ArmPose Exact(Vector3 wrist, Quaternion direction, HandShape shape) =>
                new ArmPose { Wrist = wrist, Aim = HandAim.Exact, Direction = direction, Shape = shape };

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
        /// How an arm's joints sit, in degrees. Twist turns the forearm from its middle position, where the thumb points
        /// toward the shoulder's side of the arm (+ turns the palm down); Ulnar bends the hand toward the little finger,
        /// and Flex toward the palm. Keeping these inside what a real arm does is what keeps the wrist from looking broken.
        /// </summary>
        private struct Joints
        {
            public Vector3 Wrist;
            public float Twist;
            public float Ulnar;
            public float Flex;

            public Joints(Vector3 wrist, float twist, float ulnar, float flex = 0f)
            {
                Wrist = wrist;
                Twist = twist;
                Ulnar = ulnar;
                Flex = flex;
            }

            public static Joints Lerp(Joints a, Joints b, float t) => new Joints(Vector3.Lerp(a.Wrist, b.Wrist, t),
                Mathf.Lerp(a.Twist, b.Twist, t), Mathf.Lerp(a.Ulnar, b.Ulnar, t), Mathf.Lerp(a.Flex, b.Flex, t));
        }

        /// <summary>The left hand during an inspect: Hold 1 is closed on its usual grip of the tool, 0 is wherever its joints put it.</summary>
        private struct LeftHand
        {
            public float Hold;
            public Joints Arm;
            public HandShape Shape;
        }

        /// <summary>
        /// One moment of the flourish you get by pressing F. It poses the right arm by its joints and the tool goes wherever
        /// that hand puts it: Spin turns the tool round its handle in the fingers, and Slide lets it ride up through a loose
        /// fist. Toss throws it clear of the hand, in the rig's space, and Roll turns it round its own length while it's up.
        /// Hold 1 is the fist closed, 0 is the hand open. Spin and Roll can go past a whole turn.
        /// </summary>
        private struct InspectKey
        {
            public float Time;
            public Joints Arm;
            public float Spin;
            public float Slide;
            public Vector3 Toss;
            public float Roll;
            public float Hold;
            public LeftHand Left;
            public Vector3 Torso;
            public Motion Into;
            public string Sound;

            public InspectKey(float time, Joints arm, LeftHand left, float spin, float slide = 0f, Vector3 toss = default, float roll = 0f,
                float hold = 1f, Vector3 torso = default, Motion into = Motion.Smooth, string sound = null)
            {
                Time = time;
                Arm = arm;
                Spin = spin;
                Slide = slide;
                Toss = toss;
                Roll = roll;
                Hold = hold;
                Left = left;
                Torso = torso;
                Into = into;
                Sound = sound;
            }
        }

        /// <summary>Everything an inspect puts somewhere, for one moment of it.</summary>
        private struct InspectPose
        {
            public Vector3 ToolPosition;
            public Quaternion ToolRotation;
            public Vector3 RightWrist;
            public Quaternion RightHand;
            public HandShape RightShape;
            public Vector3 LeftWrist;
            public Quaternion LeftHand;
            public HandShape LeftShape;
            public Vector3 Torso;
        }

        // These lengths and the tools' grips have to match Tools/blender/make_hands.py.
        private const float ForearmLength = 0.62f;
        private const float UpperArmLength = 0.54f;
        /// <summary>Where a handle runs through the right hand's grip, from the wrist: in front, and over toward the palm. The left is its mirror.</summary>
        private static readonly Vector3 GripToHandle = new Vector3(-0.061f, 0f, 0.13f);
        private const float LeftGripAlongHandle = -0.27f;
        private const float VacuumForeGrip = 0.185f;
        private static readonly Vector3 VacuumMouth = new Vector3(0f, 0.112f, 0.436f);

        /// <summary>How far out from the side of held cargo the wrists are: the thickness of the hand, so its palm is flat against it.</summary>
        private const float HoldSideGap = 0.05f;

        /// <summary>How far back from the near edge of held cargo the wrists are, so you can see the hands holding it.</summary>
        private const float HoldWristBack = 0.13f;

        // In step with the Smash clip in make_crew.py: the same moments, with the hit at 0.55.
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

        // ---- The inspect (F) ----
        // Both are one-handed, the way CS2 shows off a knife: the left hand slides off its grip at the start and back on at
        // the end, below the bottom of the view. They were worked out in Blender with this same maths, checking every frame
        // for a strained wrist and for the tool going into a hand or an arm. To move a pose, change its joints.

        /// <summary>The hammer held up in front of you: the hand low in front of the chest, the forearm turned out a little.</summary>
        private static readonly Vector3 HammerShowWrist = new Vector3(0.2f, -0.4f, 0.6f);

        /// <summary>The spin in the fingers that shows the hammer side on, from that pose.</summary>
        private const float HammerSideOn = 124.4f;

        private static readonly Vector3 VacuumShowWrist = new Vector3(0.2f, -0.38f, 0.57f);

        /// <summary>The middle of the vacuum, which it turns round when it's thrown up.</summary>
        private static readonly Vector3 VacuumCentre = new Vector3(0f, 0.11f, 0.13f);

        /// <summary>How much higher and farther off the thrown vacuum goes at the top, beyond sliding out of the fist.</summary>
        private static readonly Vector3 Rise = new Vector3(0f, 0.07f, 0.07f);

        private static readonly LeftHand LeftOnGrip = new LeftHand { Hold = 1f, Shape = HandShape.Grip };

        /// <summary>Down by your side, out of the view.</summary>
        private static readonly LeftHand LeftAway = new LeftHand { Arm = new Joints(new Vector3(-0.36f, -0.9f, 0.3f), 10f, 0f), Shape = HandShape.Relaxed };

        public static FirstPersonRig Instance { get; private set; }

        [Header("Parts")]
        [Tooltip("Everything that shows. The script stays on the object above it, so it keeps running while this is hidden.")]
        public GameObject content;
        public ArmParts rightArm;
        public ArmParts leftArm;
        public Transform hammer;
        public Transform vacuum;
        [Tooltip("The upper arms and forearms, which take the material of the clothes you have on.")]
        public Renderer[] sleeves;

        [Header("Looking inside cargo (F)")]
        [Tooltip("The medium body with only the shirt's arms, the gloves and the props showing (cargo_actions.py). It plays the same clip as your body while you look at what's in a case.")]
        public CrewModel inspectArms;
        [Tooltip("The middle between its eyes, in its own space. It goes on the camera.")]
        public Vector3 inspectEyes = new Vector3(0f, 1.36f, 0.17f);
        [Tooltip("How far the arms are tipped up into view while you hold what was in a case: the clip has your hands at your chest.")]
        public float inspectPitch = 38f;
        [Tooltip("And pushed this far away, so the hands aren't against the lens.")]
        public float inspectBack = 0.04f;

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
        /// <summary>The moment of the inspect being shown. Once it ends or is cut short, it stays there while the inspect blends out.</summary>
        private float inspectShownTime;
        private float inspectWeight;
        private int inspectSoundsPlayed;
        private InspectKey[] hammerInspect;
        private InspectKey[] vacuumInspect;
        private CargoItem lastCarried;
        private float tossUntil;
        private float pushUntil;
        private float knockedAt = -100f;
        private bool wasKnocked;
        private bool colorsApplied;
        private bool placed;
        private Material sleeveShown;
        private bool cargoInspecting;
        private bool inspectInWorld;
        private Renderer[] proceduralParts;
        private Renderer[] inspectParts;
        private int inspectSleeveSlot = -1;

        public float PickUpSeconds => pickUpSeconds;

        /// <summary>Where the vacuum's air comes out, in the world.</summary>
        public Vector3 VacuumMouthWorld => vacuum.TransformPoint(VacuumMouth);

        public bool ShowsTool => content.activeSelf && toolInHand != Tool.None;

        public bool Inspecting => inspectTime >= 0f;

        private Quaternion HoldTilt => Quaternion.Euler(holdTiltDegrees, 0f, 0f);

        /// <summary>True if this character's cargo is in your hands in first person, rather than on their chest.</summary>
        public bool HoldsCargoOf(PlayerAvatar carrier) => carrier == PlayerAvatar.Local && content.activeSelf && !cargoInspecting;

        /// <summary>While you look inside cargo, the arms you see are the inspect clip's.</summary>
        public bool InspectsCargo => cargoInspecting;

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
            // Built here rather than up front because they start and end in the ready poses, which depend on the shoulders.
            hammerInspect = BuildHammerInspect();
            vacuumInspect = BuildVacuumInspect();
            proceduralParts = content.GetComponentsInChildren<Renderer>(true);
            if (inspectArms != null)
            {
                inspectParts = inspectArms.GetComponentsInChildren<Renderer>(true);
                Material[] slots = inspectArms.firstPersonArms.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != null && slots[i].name.StartsWith("Porter_Shirt")) inspectSleeveSlot = i;
                }
                inspectArms.animator.speed = 0f;
                inspectArms.gameObject.SetActive(false);
            }
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
                if (cargoInspecting) SetCargoInspecting(false);
                return;
            }

            float deltaTime = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.033f);
            ApplyTeamColor(local);
            UpdateSleeves(local);
            NoticeChanges(local);
            MoveBody(local, deltaTime);
            UpdateCargoInspect(local);

            CargoItem carried = local.CarriedCargo;
            bool knocked = local.IsKnockedDown;
            Tool tool = ToolFor(local, carried, knocked);
            if (tool != toolInHand)
            {
                toolInHand = tool;
                toolPlaced = false;
                inspectTime = -1f;
                inspectWeight = 0f;
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

        // ---- Looking inside cargo (F) ----

        /// <summary>
        /// While you look at what's in a case, the arms are the medium body's real ones playing the same clip as your body,
        /// with their fingers and what they hold. They hang from the camera, tipped up so your hands are in sight. At the
        /// goose crate on the ground they stay where your body is instead, so they meet its lid.
        /// </summary>
        private void UpdateCargoInspect(PlayerAvatar local)
        {
            CargoItem target = local.InspectedCargo;
            float time = local.InspectTime;
            bool on = inspectArms != null && target != null && target.Definition.Inspectable && time >= 0f &&
                time < target.Definition.inspect.seconds && !local.IsKnockedDown && local.Visual != null && local.Visual.ShowsInspect;
            if (on != cargoInspecting) SetCargoInspecting(on);
            if (!on) return;

            CargoDefinition definition = target.Definition;
            Animator animator = inspectArms.animator;
            animator.Play(definition.inspectState, 0, Mathf.Clamp01(time / definition.inspect.seconds));
            animator.Update(0f);
            inspectArms.ShowProp((int)definition.kind);

            bool inWorld = definition.inspectOnGround;
            if (inWorld != inspectInWorld)
            {
                inspectInWorld = inWorld;
                int layer = inWorld ? GameLayers.Player : GameLayers.ViewModel;
                foreach (Renderer part in inspectParts) part.gameObject.layer = layer;
            }
            if (inWorld)
            {
                Transform body = local.Visual.pivot;
                inspectArms.transform.SetPositionAndRotation(body.position, body.rotation);
            }
            else
            {
                Quaternion tip = Quaternion.Euler(-inspectPitch, 0f, 0f);
                inspectArms.transform.localRotation = tip;
                inspectArms.transform.localPosition = -(tip * inspectEyes) + Vector3.forward * inspectBack;
            }
        }

        private void SetCargoInspecting(bool on)
        {
            cargoInspecting = on;
            foreach (Renderer part in proceduralParts) part.enabled = !on;
            if (inspectArms != null) inspectArms.gameObject.SetActive(on);
            // Coming back, your hands come up into view with the case, as when you pick something up.
            if (!on) placed = false;
            inspectInWorld = false;
            if (on && inspectParts != null)
            {
                foreach (Renderer part in inspectParts) part.gameObject.layer = GameLayers.ViewModel;
            }
        }

        /// <summary>What's in your hands: nothing while they're busy or you're on the ground, and otherwise what your side carries.</summary>
        private Tool ToolFor(PlayerAvatar local, CargoItem carried, bool knocked)
        {
            if (carried != null || knocked || Time.time < tossUntil || Time.time < pushUntil || local.IsInspecting) return Tool.None;
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

            InspectPose show = default;
            if (inspectWeight > 0f)
            {
                show = ShowInspect(hammerInspect, new Vector3(0f, LeftGripAlongHandle, 0f), Vector3.zero, ref grip, ref rotation);
                sharp = true;
            }
            PlaceTool(hammer, grip, rotation, sharp, deltaTime);
            Vector3 handle = toolRotation * Vector3.up;
            rightPose = ArmPose.OnHandleAt(toolPosition, handle);
            leftPose = ArmPose.OnHandleAt(toolPosition + handle * LeftGripAlongHandle, handle);
            if (inspectWeight > 0f) InspectHands(show, ref rightPose, ref leftPose);
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

            InspectPose show = default;
            if (inspectWeight > 0f)
            {
                show = ShowInspect(vacuumInspect, new Vector3(0f, 0f, VacuumForeGrip), VacuumCentre, ref grip, ref rotation);
                sharp = true;
            }
            PlaceTool(vacuum, grip, rotation, sharp, deltaTime);
            Vector3 handle = toolRotation * Vector3.up;
            rightPose = ArmPose.OnHandleAt(toolPosition, handle);
            leftPose = ArmPose.OnHandleAt(toolPosition + toolRotation * Vector3.forward * VacuumForeGrip, handle);
            if (inspectWeight > 0f) InspectHands(show, ref rightPose, ref leftPose);
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

        private InspectKey[] BuildHammerInspect()
        {
            Joints ready = ReadyJoints(HammerReadyGrip, HammerReadyRotation, out float readySpin);
            LeftHand off = LeftOffHandle(HammerReadyGrip, HammerReadyRotation, new Vector3(0f, LeftGripAlongHandle, 0f), 0.14f);
            Joints lifting = new Joints(new Vector3(0.33f, -0.45f, 0.62f), -105f, 40f, 6f);
            Joints shown = new Joints(HammerShowWrist, -40f, 22f);
            return new[]
            {
                new InspectKey(0f, ready, LeftOnGrip, readySpin),
                // The left hand slides off the end of the handle as the right hand starts to lift.
                new InspectKey(0.14f, lifting, off, 80f, torso: new Vector3(-1f, 1f, 0f)),
                // Up in front of you, side on. The left hand drops away.
                new InspectKey(0.44f, shown, LeftAway, HammerSideOn, torso: new Vector3(-3f, 3f, 0f)),
                // Rolled in the fingers to look at one face, the forearm tipping it a little.
                new InspectKey(0.88f, new Joints(new Vector3(0.18f, -0.39f, 0.61f), -30f, 20f), LeftAway, HammerSideOn + 70f, torso: new Vector3(-2f, -1f, 0f)),
                // And round past side on, to the other face.
                new InspectKey(1.28f, new Joints(new Vector3(0.21f, -0.41f, 0.6f), -47f, 23f), LeftAway, HammerSideOn - 55f, torso: new Vector3(-2f, 4f, 0f)),
                // Cocked...
                new InspectKey(1.44f, new Joints(new Vector3(0.2f, -0.43f, 0.6f), -40f, 27f), LeftAway, HammerSideOn - 40f,
                    torso: new Vector3(-1f, 3f, 0f), sound: "swing"),
                // ...and flicked, so it spins in a loose fist, riding up a little as it goes round and dropping back into the grip.
                new InspectKey(1.7f, new Joints(new Vector3(0.2f, -0.41f, 0.62f), -38f, 18f), LeftAway, HammerSideOn + 230f, slide: 0.04f,
                    torso: new Vector3(-3f, 1f, 0f), into: Motion.Linear),
                new InspectKey(2f, shown, LeftAway, HammerSideOn + 540f, torso: new Vector3(1f, 2f, 0f), into: Motion.Decelerate, sound: "clunk"),
                // Hefted once, the weight pulling the hand down.
                new InspectKey(2.24f, new Joints(new Vector3(0.2f, -0.37f, 0.6f), -40f, 16f), LeftAway, HammerSideOn + 540f, torso: new Vector3(-2f, 2f, 0f)),
                new InspectKey(2.44f, new Joints(new Vector3(0.2f, -0.45f, 0.6f), -40f, 27f), LeftAway, HammerSideOn + 540f,
                    torso: new Vector3(3f, 2f, 0f), into: Motion.Accelerate, sound: "clunk"),
                new InspectKey(2.7f, new Joints(new Vector3(0.21f, -0.41f, 0.6f), -42f, 21f), LeftAway, HammerSideOn + 540f, torso: new Vector3(0f, 2f, 0f)),
                // Back down to the ready pose; the left hand comes up the handle from below and takes hold.
                new InspectKey(3.08f, lifting, off, readySpin + 737f, torso: new Vector3(0f, 1f, 0f)),
                new InspectKey(3.4f, ready, LeftOnGrip, readySpin + 720f),
            };
        }

        private InspectKey[] BuildVacuumInspect()
        {
            Joints ready = ReadyJoints(VacuumReadyGrip, VacuumReadyRotation, out float readySpin);
            LeftHand off = LeftOffHandle(VacuumReadyGrip, VacuumReadyRotation, new Vector3(0f, 0f, VacuumForeGrip), 0.14f);
            Joints lifting = new Joints(new Vector3(0.33f, -0.42f, 0.5f), -50f, 36f, 5f);
            Joints flicked = new Joints(new Vector3(0.2f, -0.41f, 0.57f), -15f, 19f);
            Joints catching = new Joints(new Vector3(0.2f, -0.47f, 0.56f), -45f, 0f, -15f);
            // Thrown straight up the pistol grip, so it slides out of the fist and back in without touching the fingers.
            // Once it's clear of the fist, it carries on up and away from you rather than further up the grip, which leans back.
            Vector3 up = JointHand(1f, flicked, false) * Vector3.up;
            return new[]
            {
                new InspectKey(0f, ready, LeftOnGrip, readySpin),
                new InspectKey(0.14f, lifting, off, 42f, torso: new Vector3(-1f, 1f, 0f)),
                // Held up on end in front of you, turned to show its left side...
                new InspectKey(0.42f, new Joints(VacuumShowWrist, 22f, 18f), LeftAway, 40f, torso: new Vector3(-3f, 2f, 0f)),
                new InspectKey(0.84f, new Joints(new Vector3(0.15f, -0.36f, 0.58f), 48f, 15f), LeftAway, 40f, torso: new Vector3(-3f, -3f, 0f)),
                // ...then rolled over the top.
                new InspectKey(1.28f, new Joints(new Vector3(0.22f, -0.39f, 0.56f), -46f, 24f), LeftAway, 40f, torso: new Vector3(-2f, 5f, 0f)),
                // Dipped, then thrown up: it slides out of the top of the fist, which only opens once it's clear, spins
                // round its long axis, and drops back down into the fist the way it came out.
                new InspectKey(1.42f, new Joints(new Vector3(0.2f, -0.42f, 0.57f), -18f, 22f), LeftAway, 40f, torso: new Vector3(0f, 2f, 0f)),
                new InspectKey(1.52f, new Joints(new Vector3(0.2f, -0.39f, 0.57f), -15f, 18f), LeftAway, 40f, toss: up * 0.1f, roll: 10f,
                    torso: new Vector3(-2f, 1f, 0f), into: Motion.Accelerate, sound: "swing"),
                new InspectKey(1.58f, new Joints(new Vector3(0.2f, -0.4f, 0.57f), -15f, 18f), LeftAway, 40f, toss: up * 0.18f, roll: 60f,
                    torso: new Vector3(-2f, 1f, 0f), into: Motion.Linear),
                new InspectKey(1.62f, new Joints(new Vector3(0.2f, -0.43f, 0.57f), -22f, 14f, -4f), LeftAway, 40f, toss: up * 0.18f + Rise * 0.5f, roll: 120f, hold: 0f,
                    torso: new Vector3(-2f, 1f, 0f), into: Motion.Linear),
                new InspectKey(1.7f, catching, LeftAway, 40f, toss: up * 0.18f + Rise, roll: 220f, hold: 0f, torso: new Vector3(-3f, 0f, 0f), into: Motion.Decelerate),
                new InspectKey(1.8f, catching, LeftAway, 40f, toss: up * 0.18f + Rise * 0.5f, roll: 345f, hold: 0f, torso: new Vector3(-2f, 0f, 0f), into: Motion.Linear),
                new InspectKey(1.86f, new Joints(new Vector3(0.2f, -0.41f, 0.57f), -15f, 20f), LeftAway, 40f, toss: up * 0.17f, roll: 360f,
                    torso: new Vector3(-1f, 1f, 0f), into: Motion.Decelerate),
                new InspectKey(1.96f, new Joints(new Vector3(0.2f, -0.42f, 0.57f), -15f, 20f), LeftAway, 40f, roll: 360f,
                    torso: new Vector3(1f, 2f, 0f), into: Motion.Accelerate, sound: "clunk"),
                // Levelled at the sky ahead for a test puff, and the kick of it.
                new InspectKey(2.26f, new Joints(new Vector3(0.22f, -0.38f, 0.58f), -25f, 32f), LeftAway, 40f, roll: 360f,
                    torso: new Vector3(-1f, 1f, 0f), sound: "blast"),
                new InspectKey(2.36f, new Joints(new Vector3(0.22f, -0.355f, 0.57f), -27f, 21f), LeftAway, 40f, roll: 360f,
                    torso: new Vector3(-5f, 1f, 0f), into: Motion.Decelerate),
                new InspectKey(2.64f, new Joints(VacuumShowWrist, -18f, 22f), LeftAway, 40f, roll: 360f, torso: new Vector3(-1f, 2f, 0f)),
                // Back to the ready pose, the left hand coming up onto the front grip.
                new InspectKey(3f, lifting, off, 42f, roll: 360f, torso: new Vector3(0f, 1f, 0f)),
                new InspectKey(3.3f, ready, LeftOnGrip, readySpin, roll: 360f),
            };
        }

        private void UpdateInspect(PlayerAvatar local, bool swinging, float deltaTime)
        {
            // The click itself ends it. Waiting for the host would leave a client's flourish running into the swing.
            bool stop = swinging || local.WantsToUseTool || local.IsCharging || local.IsSlamming || local.Sucking.Value || local.IsDashing || local.IsStaggered;
            if (stop) inspectTime = -1f;
            if (inspectTime >= 0f)
            {
                inspectTime += deltaTime;
                InspectKey[] keys = toolInHand == Tool.Hammer ? hammerInspect : vacuumInspect;
                if (inspectTime >= keys[keys.Length - 1].Time) inspectTime = -1f;
            }
            inspectWeight = Mathf.MoveTowards(inspectWeight, inspectTime >= 0f ? 1f : 0f, deltaTime * (inspectTime >= 0f ? 7f : stop ? 10f : 4f));
        }

        /// <summary>Moves the tool toward where the inspect has it, by how far the inspect has blended in, and plays its sounds.</summary>
        private InspectPose ShowInspect(InspectKey[] keys, Vector3 leftGrip, Vector3 centre, ref Vector3 grip, ref Quaternion rotation)
        {
            // Blending out, after the end or cut short by a click, it holds where it got to rather than jumping to the end.
            if (inspectTime >= 0f) inspectShownTime = inspectTime;
            InspectPose show = SampleInspect(keys, inspectShownTime, leftGrip, centre);
            if (inspectTime >= 0f) PlayInspectSounds(keys);
            grip = Vector3.Lerp(grip, show.ToolPosition, inspectWeight);
            rotation = Quaternion.Slerp(rotation, show.ToolRotation, inspectWeight);
            torsoTarget = Vector3.Lerp(torsoTarget, show.Torso, inspectWeight);
            return show;
        }

        /// <summary>One moment of an inspect: the right arm from its joints, the tool from that hand, and the left hand.</summary>
        private InspectPose SampleInspect(InspectKey[] keys, float time, Vector3 leftGrip, Vector3 centre)
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
            Joints arm = Joints.Lerp(from.Arm, to.Arm, t);
            float spin = Mathf.Lerp(from.Spin, to.Spin, t);
            float slide = Mathf.Lerp(from.Slide, to.Slide, t);
            Vector3 toss = Vector3.Lerp(from.Toss, to.Toss, t);
            float roll = Mathf.Lerp(from.Roll, to.Roll, t);
            bool closed = Mathf.Lerp(from.Hold, to.Hold, t) > 0.5f;

            InspectPose show = default;
            show.Torso = Vector3.Lerp(from.Torso, to.Torso, t);
            Quaternion hand = JointHand(1f, arm, false);
            Quaternion rotation = hand * Quaternion.AngleAxis(spin, Vector3.up);
            Vector3 origin = arm.Wrist + hand * GripOffset(1f) + rotation * Vector3.up * slide;
            if (roll != 0f || toss != Vector3.zero)
            {
                // Thrown: it turns round its own length, through its middle.
                Vector3 middle = origin + rotation * centre;
                Quaternion turn = Quaternion.AngleAxis(roll, rotation * Vector3.forward);
                origin = middle + turn * (origin - middle) + toss;
                rotation = turn * rotation;
            }
            show.ToolPosition = origin;
            show.ToolRotation = rotation;
            show.RightWrist = arm.Wrist;
            show.RightShape = closed ? HandShape.Grip : HandShape.Open;
            show.RightHand = closed ? hand : JointHand(1f, arm, true);

            // The left hand on its grip, wherever its joints put it, or on the way between the two. On the way, it heads
            // for the free end's joints the whole time, so it goes straight there rather than swinging round.
            Joints free = from.Left.Hold >= 1f ? to.Left.Arm : to.Left.Hold >= 1f ? from.Left.Arm : Joints.Lerp(from.Left.Arm, to.Left.Arm, t);
            HandShape freeShape = from.Left.Hold < 1f ? from.Left.Shape : to.Left.Shape;
            float hold = Mathf.Clamp01(Mathf.Lerp(from.Left.Hold, to.Left.Hold, t));
            HandOnHandle(-1f, origin + rotation * leftGrip, rotation * Vector3.up, out Vector3 onWrist, out Quaternion onHand);
            Quaternion freeHand = JointHand(-1f, free, freeShape != HandShape.Grip && freeShape != HandShape.Fist);
            show.LeftWrist = Vector3.Lerp(free.Wrist, onWrist, hold);
            show.LeftHand = Quaternion.Slerp(freeHand, onHand, hold);
            show.LeftShape = hold > 0.6f ? HandShape.Grip : freeShape;
            return show;
        }

        private void PlayInspectSounds(InspectKey[] keys)
        {
            while (inspectSoundsPlayed < keys.Length && keys[inspectSoundsPlayed].Time <= inspectTime)
            {
                string sound = keys[inspectSoundsPlayed].Sound;
                inspectSoundsPlayed++;
                if (string.IsNullOrEmpty(sound)) continue;
                GameAudio.Play(sound, sound == "blast" ? 0.35f : 0.3f);
                // A test puff into the air ahead, away from you.
                if (sound == "blast") FxBurst.PlayBlast(VacuumMouthWorld, vacuum.forward);
            }
        }

        /// <summary>
        /// The inspect's hands, blended in from wherever the hands would be otherwise. They're put exactly where the
        /// inspect says, with no spring, because the joints are already worked out and a spring would let them lag off the tool.
        /// </summary>
        private void InspectHands(in InspectPose show, ref ArmPose rightPose, ref ArmPose leftPose)
        {
            float w = inspectWeight;
            HandOnHandle(1f, rightPose.Wrist, rightPose.Handle, out Vector3 rightWrist, out Quaternion rightHand);
            HandOnHandle(-1f, leftPose.Wrist, leftPose.Handle, out Vector3 leftWrist, out Quaternion leftHand);
            rightPose = ArmPose.Exact(Vector3.Lerp(rightWrist, show.RightWrist, w), Quaternion.Slerp(rightHand, show.RightHand, w),
                w > 0.5f ? show.RightShape : HandShape.Grip);
            leftPose = ArmPose.Exact(Vector3.Lerp(leftWrist, show.LeftWrist, w), Quaternion.Slerp(leftHand, show.LeftHand, w),
                w > 0.5f ? show.LeftShape : HandShape.Grip);
        }

        /// <summary>The right arm in a ready pose, as joints, worked out the way PlaceArm puts a hand on a handle.</summary>
        private Joints ReadyJoints(Vector3 grip, Quaternion rotation, out float spin)
        {
            HandOnHandle(1f, grip, rotation * Vector3.up, out Vector3 wrist, out Quaternion hand);
            Vector3 forward = Quaternion.Inverse(hand) * rotation * Vector3.forward;
            spin = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return JointsOf(1f, wrist, hand);
        }

        /// <summary>The left hand as it holds a tool in its ready pose, slid `distance` down the handle and off the end of it.</summary>
        private LeftHand LeftOffHandle(Vector3 grip, Quaternion rotation, Vector3 leftGrip, float distance)
        {
            Vector3 handle = rotation * Vector3.up;
            HandOnHandle(-1f, grip + rotation * leftGrip, handle, out Vector3 wrist, out Quaternion hand);
            Joints joints = JointsOf(-1f, wrist, hand);
            joints.Wrist -= handle * distance;
            return new LeftHand { Arm = joints, Shape = HandShape.Grip };
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

        private Vector3 Shoulder(float side) => new Vector3(shoulderRight.x * side, shoulderRight.y, shoulderRight.z);

        private Vector3 Bend(float side) => new Vector3(elbowDirection.x * side, elbowDirection.y, elbowDirection.z);

        private static Vector3 GripOffset(float side) => new Vector3(GripToHandle.x * side, GripToHandle.y, GripToHandle.z);

        /// <summary>
        /// A hand closed round a handle at `grip`. The handle is round, so the hand turns on it to line up with the
        /// forearm, and the wrist sits behind the grip.
        /// </summary>
        private void HandOnHandle(float side, Vector3 grip, Vector3 handle, out Vector3 wrist, out Quaternion direction)
        {
            Vector3 along = (grip - FindElbow(Shoulder(side), grip, Bend(side))).normalized;
            Vector3 across = Vector3.ProjectOnPlane(along, handle);
            if (across.sqrMagnitude < 0.001f) across = Vector3.ProjectOnPlane(Vector3.forward, handle);
            direction = Quaternion.LookRotation(across.normalized, handle);
            wrist = grip - direction * GripOffset(side);
        }

        /// <summary>
        /// The hand at the end of an arm whose joints sit like this, with the elbow hanging down and out as everywhere
        /// else. Twist turns the thumb away from the shoulder's side of the forearm (+ is palm down, on either hand),
        /// Ulnar bends the hand toward the little finger and Flex toward the palm. A closed hand comes out with the thumb
        /// on +Y; a flat one is turned a quarter so its thumb lines up the same way.
        /// </summary>
        private Quaternion JointHand(float side, in Joints joints, bool flat)
        {
            Vector3 shoulder = Shoulder(side);
            Vector3 elbow = FindElbow(shoulder, joints.Wrist, Bend(side));
            Vector3 along = (joints.Wrist - elbow).normalized;
            Vector3 thumb = Quaternion.AngleAxis(joints.Twist * side, along) * Vector3.ProjectOnPlane(shoulder - elbow, along);
            Quaternion hand = Quaternion.LookRotation(along, thumb) * Quaternion.Euler(joints.Ulnar, -joints.Flex * side, 0f);
            return flat ? hand * Quaternion.Euler(0f, 0f, -90f * side) : hand;
        }

        /// <summary>The joints that JointHand needs to give back exactly this closed hand at this wrist.</summary>
        private Joints JointsOf(float side, Vector3 wrist, Quaternion hand)
        {
            Vector3 shoulder = Shoulder(side);
            Vector3 elbow = FindElbow(shoulder, wrist, Bend(side));
            Vector3 along = (wrist - elbow).normalized;
            Quaternion middle = Quaternion.LookRotation(along, Vector3.ProjectOnPlane(shoulder - elbow, along));
            // What's left is a turn round the forearm, then flexion, then deviation: pull the three angles back out.
            Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Inverse(middle) * hand);
            float twist = Mathf.Atan2(m[1, 0], m[0, 0]) * Mathf.Rad2Deg * side;
            float flex = -Mathf.Asin(Mathf.Clamp(-m[2, 0], -1f, 1f)) * Mathf.Rad2Deg * side;
            float ulnar = Mathf.Atan2(m[2, 1], m[2, 2]) * Mathf.Rad2Deg;
            return new Joints(wrist, twist, ulnar, flex);
        }

        private void PlaceArm(Arm arm, in ArmPose pose, float stiffness, float deltaTime)
        {
            Vector3 shoulder = Shoulder(arm.Side);
            Vector3 bend = Bend(arm.Side);

            Vector3 wrist = pose.Wrist;
            Vector3 elbow = FindElbow(shoulder, wrist, bend);
            Vector3 along = (wrist - elbow).normalized;
            Quaternion direction;
            switch (pose.Aim)
            {
                case HandAim.OnHandle:
                    HandOnHandle(arm.Side, pose.Wrist, pose.Handle, out wrist, out direction);
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

            // An exact pose counts as already holding on, so a hand that ends an inspect on its handle stays locked to it.
            if (pose.Aim == HandAim.Exact) arm.Attached = 1f;
            else arm.Attached = pose.Aim == HandAim.OnHandle ? Mathf.MoveTowards(arm.Attached, 1f, deltaTime / 0.14f) : 0f;
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
            arm.Parts.grip.SetActive(shape == HandShape.Grip);
        }

        /// <summary>Your sleeves are whatever you have on: the porters' shirt or the wreckers' jacket, which swap at half-time.</summary>
        private void UpdateSleeves(PlayerAvatar local)
        {
            Material sleeve = local.Visual != null ? local.Visual.SleeveMaterial : null;
            if (sleeve == null || sleeve == sleeveShown) return;
            sleeveShown = sleeve;
            foreach (Renderer part in sleeves) part.sharedMaterial = sleeve;
            if (inspectArms != null && inspectSleeveSlot >= 0)
            {
                Material[] slots = inspectArms.firstPersonArms.sharedMaterials;
                slots[inspectSleeveSlot] = sleeve;
                inspectArms.firstPersonArms.sharedMaterials = slots;
            }
        }

        private void ApplyTeamColor(PlayerAvatar local)
        {
            if (colorsApplied) return;
            colorsApplied = true;
            if (local.Team == Team.Orange) return;
            Renderer[] parts = content.GetComponentsInChildren<Renderer>(true);
            if (inspectParts != null) parts = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(parts, inspectParts));
            foreach (Renderer part in parts)
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
