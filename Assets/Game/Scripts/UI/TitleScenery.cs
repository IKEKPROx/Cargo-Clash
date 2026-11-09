using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// A group of things the start screen's truck drives past. Each is a child of this object, and
    /// whatever falls behind the truck jumps ahead by one period, so the road never runs out.
    /// </summary>
    public sealed class TitleScenery : MonoBehaviour
    {
        // Half the length of one fence span, for working out which way the ground slopes under it.
        private const float FenceHalfSpan = 1.2f;

        public Transform truck;
        [Tooltip("How far along the road the group repeats. Long enough that nothing jumps while in view.")]
        public float period = 96f;
        public float behind = 40f;
        [Tooltip("Stand each child on the bumpy ground. Without this, it keeps its height.")]
        public bool onGround = true;
        [Tooltip("Lean each child along the slope, so a line of fence stays joined up.")]
        public bool followSlope;

        private Transform[] items;

        private void Awake()
        {
            items = new Transform[transform.childCount];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = transform.GetChild(i);
                Settle(items[i], items[i].position.x);
            }
        }

        private void LateUpdate()
        {
            float limit = truck.position.x - behind;
            foreach (Transform item in items)
            {
                float x = item.position.x;
                if (x < limit) Settle(item, x + period * Mathf.Ceil((limit - x) / period));
            }
        }

        private void Settle(Transform item, float x)
        {
            Vector3 position = item.position;
            position.x = x;
            if (onGround) position.y = CargoClashEndlessTerrain.HeightAt(x, position.z);
            item.position = position;
            if (!followSlope) return;

            float rise = CargoClashEndlessTerrain.HeightAt(x + FenceHalfSpan, position.z) -
                CargoClashEndlessTerrain.HeightAt(x - FenceHalfSpan, position.z);
            item.rotation = Quaternion.LookRotation(new Vector3(FenceHalfSpan * 2f, rise, 0f));
        }
    }
}
