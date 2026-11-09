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

        [Header("Ram")]
        public float ramCooldown = 3f;
        [Tooltip("Cooldown for the smaller team when teams are uneven. Level 1 has no items to compensate with.")]
        public float ramCooldownSmallTeam = 2f;
        public float ramDuration = 0.28f;
        public float ramSpeed = 15f;
        public float ramHitRadius = 1.5f;

        [Header("Knockdown")]
        public float knockdownSeconds = 1.5f;
        public float immuneSeconds = 2f;
        public float knockbackSpeed = 8f;

        [Header("Interaction")]
        public float pickupRange = 1.6f;
        public float smashRange = 1.8f;
        public float headHeight = 1.46f;
        [Tooltip("Scales every cargo type's smash time. Playtesting tunes this before individual values.")]
        public float smashTimeMultiplier = 1f;

        [Header("Cargo")]
        public CargoDefinition[] cargo;

        [Header("Prefabs")]
        public GameObject avatarPrefab;
        public GameObject directorPrefab;
        public GameObject rosterPrefab;

        [Header("Art")]
        public Material orangeMaterial;
        public Material blueMaterial;
        public Material particleMaterial;

        [Header("Input")]
        public InputActionAsset inputActions;

        /// <summary>Half length for this run. AutoPilot shortens it for automated tests.</summary>
        public float EffectiveHalfSeconds => AutoPilot.HalfSecondsOverride > 0f ? AutoPilot.HalfSecondsOverride : halfSeconds;

        public float EffectiveSmashMultiplier => AutoPilot.SmashScaleOverride > 0f ? AutoPilot.SmashScaleOverride : smashTimeMultiplier;

        /// <summary>From the helicopter's arrival until it has flown off with the box.</summary>
        public float HelicopterWorkSeconds => helicopterLowerSeconds + helicopterClipSeconds + helicopterLiftSeconds + helicopterLeaveSeconds;
    }
}
