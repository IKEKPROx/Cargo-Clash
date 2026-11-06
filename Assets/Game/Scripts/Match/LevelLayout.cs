using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Where things go in a level. Move the child transforms in the scene to change a layout.
    /// </summary>
    public sealed class LevelLayout : MonoBehaviour
    {
        public static LevelLayout Instance { get; private set; }

        public Transform[] collectorSpawns;
        public Transform[] interrupterSpawns;
        public Transform[] smallSpots;
        public Transform[] mediumSpots;
        public Transform[] largeSpots;

        [Header("Truck")]
        [Tooltip("Carrying cargo into this area rescues it. Half size is in the transform's local X and Z.")]
        public Transform truckZone;
        public Vector2 truckZoneHalfSize = new Vector2(5f, 7f);
        [Tooltip("Rescued cargo lands in a grid on this transform's local X and Z.")]
        public Transform bedOrigin;
        public Vector2 bedHalfSize = new Vector2(1.4f, 2f);

        private int bedCount;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool InTruckZone(Vector3 worldPosition)
        {
            Vector3 local = truckZone.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) <= truckZoneHalfSize.x && Mathf.Abs(local.z) <= truckZoneHalfSize.y;
        }

        public Vector3 ClosestTruckZonePoint(Vector3 worldPosition)
        {
            Vector3 local = truckZone.InverseTransformPoint(worldPosition);
            local.x = Mathf.Clamp(local.x, -truckZoneHalfSize.x * 0.8f, truckZoneHalfSize.x * 0.8f);
            local.z = Mathf.Clamp(local.z, -truckZoneHalfSize.y * 0.8f, truckZoneHalfSize.y * 0.8f);
            local.y = 0f;
            return truckZone.TransformPoint(local);
        }

        public Transform[] SpotsFor(CargoSize size)
        {
            switch (size)
            {
                case CargoSize.Small: return smallSpots;
                case CargoSize.Medium: return mediumSpots;
                default: return largeSpots;
            }
        }

        public void ResetBed()
        {
            bedCount = 0;
        }

        /// <summary>The next landing point in the truck bed; rows stack upward as the bed fills.</summary>
        public Vector3 NextBedPoint()
        {
            const int columns = 3;
            const int rows = 4;
            int index = bedCount++;
            int layer = index / (columns * rows);
            int cell = index % (columns * rows);
            float x = Mathf.Lerp(-bedHalfSize.x, bedHalfSize.x, (cell % columns) / (float)(columns - 1));
            float z = Mathf.Lerp(-bedHalfSize.y, bedHalfSize.y, (cell / columns) / (float)(rows - 1));
            return bedOrigin.TransformPoint(new Vector3(x, 0.5f + layer * 0.7f, z));
        }
    }
}
