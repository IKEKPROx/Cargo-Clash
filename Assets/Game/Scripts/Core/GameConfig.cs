using UnityEngine;
using UnityEngine.InputSystem;

namespace CargoClash
{
    /// <summary>All gameplay tuning in one asset, so numbers can change without touching code.</summary>
    [CreateAssetMenu(menuName = "Cargo Clash/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        private static GameConfig instance;

        public static GameConfig Get()
        {
            if (instance == null) instance = Resources.Load<GameConfig>("GameConfig");
            return instance;
        }

        public LevelInfo Level(int index) => levels[Mathf.Clamp(index, 0, levels.Length - 1)];

        [Header("Scenes")]
        public string titleScene = "CargoRover";

        [Header("Levels")]
        [Tooltip("In the order the level select shows them. The first one is the tutorial.")]
        public LevelInfo[] levels;

        [Header("Match")]
        public int maxPlayers = 6;
        public int teamSize = 3;
        public float halfSeconds = 60f;
        [Tooltip("The music speeds up and the timer turns red for this long at the end of a half.")]
        public float finalStretchSeconds = 15f;
        public float halfIntroSeconds = 4f;

        [Header("Helicopter")]
        [Tooltip("It starts flying in this long before it arrives over the truck.")]
        public float helicopterApproachSeconds = 12f;
        [Tooltip("The warning banner shows this long before the half ends.")]
        public float helicopterWarningSeconds = 10f;
        [Tooltip("It arrives this long after time runs out, so the camera has time to reach the truck.")]
        public float helicopterArrivalDelay = 1.2f;
        [Tooltip("If the cargo runs out early, it arrives this long after the half ends.")]
        public float helicopterHurrySeconds = 3f;
        public float helicopterLowerSeconds = 1f;
        public float helicopterClipSeconds = 0.4f;
        public float helicopterLiftSeconds = 1.7f;
        public float helicopterLeaveSeconds = 2.3f;

        [Header("Movement")]
        public float runSpeed = 6.5f;
        public float turnDegreesPerSecond = 900f;
        [Tooltip("People closer than this ease apart, so no one's view ends up inside someone else.")]
        public float personalSpace = 0.95f;
        public float personalSpacePush = 3.5f;

        [Header("Looking around")]
        public float eyeHeight = 1.28f;
        [Tooltip("Degrees the view turns for each pixel the mouse moves. Each player scales this in the pause menu.")]
        public float lookDegreesPerPixel = 0.1f;
        [Tooltip("How fast the arrow keys and the right stick turn the view.")]
        public float keyTurnDegreesPerSecond = 150f;
        public float maxLookUp = 70f;
        public float maxLookDown = 78f;
        [Tooltip("How far off the middle of the view cargo can be and still count as being aimed at, measured sideways. The size of the cargo adds to this.")]
        public float aimDegrees = 34f;

        [Header("Ram")]
        public float ramCooldown = 3f;
        [Tooltip("Cooldown for the smaller team when teams are uneven. Level 1 has no items to compensate with.")]
        public float ramCooldownSmallTeam = 2f;
        public float ramDuration = 0.28f;
        public float ramSpeed = 15f;
        public float ramHitRadius = 1.5f;

        [Header("Hitting people")]
        [Tooltip("How close someone has to be for the hammer to reach them, from the middle of one person to the middle of the other.")]
        public float hitRange = 2.1f;
        [Tooltip("How far off the middle of the view someone can be and still get hit by the hammer.")]
        public float hitAimDegrees = 38f;
        [Tooltip("How long a hit stops someone from moving or doing anything.")]
        public float hitStaggerSeconds = 0.4f;
        public float hitPushSpeed = 6f;
        [Tooltip("Hits in a row that knock someone down, each coming within hitComboSeconds of the last.")]
        public int hitsToKnockDown = 2;
        public float hitComboSeconds = 2.5f;

        [Header("Health")]
        public int maxHealth = 100;
        public int hammerDamage = 34;
        public int blastDamage = 25;
        public int slamDamage = 60;
        public int ramDamage = 20;
        [Tooltip("How long someone stays down after losing all their health, before they come back at their own side's start.")]
        public float respawnSeconds = 3f;

        [Header("Knockdown")]
        public float knockdownSeconds = 1.5f;
        public float immuneSeconds = 2f;
        public float knockbackSpeed = 8f;

        [Header("Collectors' vacuum: blast")]
        [Tooltip("How far a blast reaches, from the middle of one person to the middle of the other.")]
        public float blastRange = 3.2f;
        [Tooltip("A blast has to be aimed better than the hammer, because it reaches farther.")]
        public float blastAimDegrees = 24f;
        public float blastPushSpeed = 9f;
        [Tooltip("One blast. Should match the Blast clip's length in make_character.py.")]
        public float blastCycleSeconds = 0.55f;
        [Tooltip("From clicking until the air comes out. Should match the Blast clip.")]
        public float blastFirstSeconds = 0.1f;

        [Header("Collectors' vacuum: suction")]
        public float suckRange = 8f;
        [Tooltip("How far off the middle of the view it pulls, as well as the size of the cargo.")]
        public float suckAimDegrees = 22f;
        public float suckSpeedSmall = 7f;
        public float suckSpeedMedium = 3.5f;
        [Tooltip("Cargo this close to the person sucking it, edge to feet, goes into their hands.")]
        public float suckGrabRange = 0.9f;
        public float suckPersonRange = 6f;
        public float suckPersonSpeed = 3.5f;
        [Tooltip("How close it pulls someone before they stop coming.")]
        public float suckPersonStop = 1.6f;
        public float suckMoveMultiplier = 0.6f;

        [Header("Interrupters' hammer: charged slam")]
        public float chargeSeconds = 0.7f;
        public float chargeMoveMultiplier = 0.55f;
        [Tooltip("The whole slam, from letting go until the hammer is back up. Should match the Slam clip.")]
        public float slamSeconds = 0.5f;
        [Tooltip("From letting go until it lands. Should match the Slam clip.")]
        public float slamLandSeconds = 0.14f;
        [Tooltip("How far in front of the person the middle of the slam is.")]
        public float slamReach = 1.5f;
        public float slamRadius = 2f;
        [Tooltip("How many ordinary hammer swings a slam is worth against cargo.")]
        public float slamCargoSwings = 3f;

        [Header("Interaction")]
        public float pickupRange = 1.6f;
        public float smashRange = 1.8f;
        [Tooltip("Allowance when the host checks a reach, because it sees people slightly later than their owners do.")]
        public float reachTolerance = 0.6f;
        [Tooltip("Roughly how far behind someone on another computer sees everyone else. When they hit someone, the host allows for both having moved that long.")]
        public float lagCompensationSeconds = 0.3f;
        [Tooltip("The most that allowance can add to a reach, in metres.")]
        public float maxLagCompensation = 2.5f;
        [Tooltip("Scales every cargo type's smash time. Playtesting tunes this before individual values.")]
        public float smashTimeMultiplier = 1f;
        [Tooltip("One swing of the hammer. Should match the Smash clip's length in make_character.py.")]
        public float smashSwingSeconds = 0.5f;
        [Tooltip("From starting to swing until the first hit. Should match where the hit lands in the Smash clip.")]
        public float smashFirstHitSeconds = 0.275f;

        [Header("Cargo")]
        public CargoDefinition[] cargo;
        [Tooltip("Cargo looks damaged once its durability falls to this.")]
        public float damagedBelow = 0.67f;
        [Tooltip("Cargo looks nearly destroyed once its durability falls to this.")]
        public float wreckedBelow = 0.34f;

        [Header("Prefabs")]
        public GameObject avatarPrefab;
        public GameObject directorPrefab;
        public GameObject rosterPrefab;

        [Header("Art")]
        public Material orangeMaterial;
        public Material blueMaterial;
        public Material particleMaterial;
        public Material puffMaterial;
        public Material flashMaterial;

        [Header("Input")]
        public InputActionAsset inputActions;

        public int DamageOf(Weapon weapon)
        {
            switch (weapon)
            {
                case Weapon.Blast: return blastDamage;
                case Weapon.Slam: return slamDamage;
                case Weapon.Ram: return ramDamage;
                default: return hammerDamage;
            }
        }

        /// <summary>Half length for this run. AutoPilot shortens it for automated tests.</summary>
        public float EffectiveHalfSeconds => AutoPilot.HalfSecondsOverride > 0f ? AutoPilot.HalfSecondsOverride : halfSeconds;

        public float EffectiveSmashMultiplier => AutoPilot.SmashScaleOverride > 0f ? AutoPilot.SmashScaleOverride : smashTimeMultiplier;

        /// <summary>From the helicopter's arrival until it has flown off with the box.</summary>
        public float HelicopterWorkSeconds => helicopterLowerSeconds + helicopterClipSeconds + helicopterLiftSeconds + helicopterLeaveSeconds;
    }
}
