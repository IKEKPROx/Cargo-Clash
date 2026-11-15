using CargoClash.RiverV10;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Astra's rainy river (独立建模/雨天河流关卡_v16): the scene's half of the level. Where the water runs, the two fords
    /// people can wade across, the catcher downstream that planks plug, and where the planks wait in the huts.
    /// The rules that run during a match (the current, the planks, what the river carries off) are in
    /// <see cref="RiverMatch"/>, on the host.
    /// </summary>
    public sealed class RiverLevel : MonoBehaviour
    {
        public static RiverLevel Instance { get; private set; }

        [Header("Water")]
        [Tooltip("Astra's samples down the middle of the river with its width, relative to flowRoot.")]
        public RiverFlowPath flowPath;
        public Transform flowRoot;
        [Tooltip("Where people can wade across, in world space. Everywhere else the water is too deep (the PlayerBlock walls).")]
        public Bounds[] fords;
        [Tooltip("Cargo that leaves this part of the world, in X and Z, is gone. The river leaves it at the downstream end.")]
        public Rect field;

        [Header("Catcher")]
        [Tooltip("Her catcher across the river downstream: a board per slot, each with its own blocker.")]
        public CatcherVisualController catcher;

        [Header("Planks")]
        [Tooltip("Where a plank waits in the huts at the start of each half.")]
        public Transform[] plankSpots;

        public int SlotCount => catcher != null && catcher.slots != null ? catcher.slots.Length : 0;

        public Vector3 SlotPosition(int slot) => catcher.slots[slot].anchor.position;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Whether a point is over the river; if so, the water level, which way it flows, and the middle of the river there.</summary>
        public bool OverWater(Vector3 point, out float surface, out Vector3 flow, out Vector3 middle)
        {
            bool over = flowPath.TrySample(flowRoot, point, out middle, out flow);
            surface = middle.y;
            return over;
        }

        public bool InFord(Vector3 point)
        {
            if (fords == null) return false;
            foreach (Bounds ford in fords)
            {
                if (point.x >= ford.min.x && point.x <= ford.max.x && point.z >= ford.min.z && point.z <= ford.max.z) return true;
            }
            return false;
        }

        /// <summary>People slow down where their feet are under the water, which is only ever in a ford.</summary>
        public float WadeMultiplier(Vector3 feet)
        {
            if (!OverWater(feet, out float surface, out _, out _)) return 1f;
            return feet.y < surface + 0.05f ? GameConfig.Get().wadeSpeedMultiplier : 1f;
        }

        public bool OffField(Vector3 point) => point.y < -4f || !field.Contains(new Vector2(point.x, point.z));
    }
}
