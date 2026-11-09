using UnityEngine;

namespace CargoClash
{
    /// <summary>Tuning for one cargo type. Starting values come from the design doc's level 1 table.</summary>
    [CreateAssetMenu(menuName = "Cargo Clash/Cargo Definition")]
    public sealed class CargoDefinition : ScriptableObject
    {
        public CargoSize size;
        public GameObject prefab;

        [Tooltip("Score value when rescued or destroyed.")]
        public int value = 10;

        [Tooltip("Seconds of smashing needed to destroy it.")]
        public float smashSeconds = 1f;

        [Tooltip("Run speed multiplier while carrying it.")]
        public float carrySpeedMultiplier = 0.9f;

        [Tooltip("People needed to move it.")]
        public int requiredCarriers = 1;

        [Tooltip("How many spawn in a 3v3 match. Fewer players scale this down.")]
        public int fullCount = 12;

        [Header("Show")]
        [Tooltip("What's left when it breaks. Comes from Tools/blender/make_cargo.py.")]
        public GameObject piecesPrefab;

        [Tooltip("The colour of the bits that fly off when the hammer lands.")]
        public Color chipColor = new Color(0.75f, 0.55f, 0.3f);

        [Tooltip("Played when it breaks, on top of the usual sound.")]
        public string breakSound = "crack";
    }
}
