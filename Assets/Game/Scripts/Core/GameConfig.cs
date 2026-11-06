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

        [Header("Scenes")]
        public string titleScene = "CargoRover";
        public string levelScene = "Level01";

        [Header("Match")]
        public int maxPlayers = 6;
        public int teamSize = 3;
        public float halfSeconds = 180f;
        public float halfIntroSeconds = 4f;
        public float halfOutroSeconds = 3f;

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
        public float headHeight = 1.55f;
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
    }
}
