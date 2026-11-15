using System;
using UnityEngine;

namespace CargoClash
{
    /// <summary>Tuning for one cargo type. Starting values come from the design doc's level 1 table.</summary>
    [CreateAssetMenu(menuName = "Cargo Clash/Cargo Definition")]
    public sealed class CargoDefinition : ScriptableObject
    {
        public CargoSize size;
        [Tooltip("Which of Astra's cases it is: what's inside, and so what pressing F does with it.")]
        public CargoKind kind;
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

        [Header("Two carriers")]
        [Tooltip("How high its middle is while two people carry it by its side handles. 0 leaves it standing on the ground.")]
        public float coCarryHeight;
        [Tooltip("How far each carrier stands from its middle.")]
        public float coCarryReach;

        [Header("Inspect (F)")]
        [Tooltip("The crew's Animator state that plays it, made by Tools/blender/cargo_actions.py.")]
        public string inspectState;
        [Tooltip("Looked at where it stands on the ground (the goose crate), rather than out of your hands.")]
        public bool inspectOnGround;
        [Tooltip("How far in front of the crate you stand to open it.")]
        public float inspectDistance;
        public InspectTimes inspect;

        [Header("Show")]
        [Tooltip("What's left when it breaks. Comes from Tools/blender/make_astra_cargo.py.")]
        public GameObject piecesPrefab;

        [Tooltip("The colour of the bits that fly off when the hammer lands.")]
        public Color chipColor = new Color(0.75f, 0.55f, 0.3f);

        [Tooltip("Played when it breaks, on top of the usual sound.")]
        public string breakSound = "crack";

        public bool Inspectable => !string.IsNullOrEmpty(inspectState) && inspect.seconds > 0f;
    }

    /// <summary>
    /// When things happen in an inspect clip, in seconds from its start (Assets/Game/Models/CargoInspect.json, written
    /// with the clips). A moment the clip doesn't have is -1.
    /// </summary>
    [Serializable]
    public struct InspectTimes
    {
        public float seconds;
        [Tooltip("The case leaves the hands, and lands.")]
        public float drop;
        public float land;
        [Tooltip("The thing inside leaves its place in the case, and starts being Astra's clip.")]
        public float @out;
        public float start;
        [Tooltip("It's back in its place, the lid has shut, and the case comes up into the hands again.")]
        public float back;
        public float shut;
        public float lift;
        public float caught;
        [Tooltip("The goose crate's lid lifts.")]
        public float lidOpen;
        [Tooltip("Notes thrown, smoke breathed out, the cigarette flicked away.")]
        public float notes;
        public float puff;
        public float flick;

        /// <summary>While the thing is out of its place in the case.</summary>
        public bool ItemOut(float time) => time >= @out && (back < 0f || time < back);
    }
}
