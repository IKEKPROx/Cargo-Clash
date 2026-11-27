using System;
using UnityEngine;

namespace CargoClash
{
    /// <summary>Where one kind of cargo starts, on a level that places each kind itself.</summary>
    [Serializable]
    public sealed class KindSpots
    {
        public CargoKind kind;
        public Transform[] spots;
    }

    /// <summary>
    /// Where things go in a level. Move the child transforms in the scene to change a layout.
    /// </summary>
    public sealed class LevelLayout : MonoBehaviour
    {
        public static LevelLayout Instance { get; private set; }

        [Tooltip("This level's half length in seconds. 0 uses GameConfig.halfSeconds.")]
        public float halfSeconds;

        public Transform[] collectorSpawns;
        public Transform[] interrupterSpawns;
        public Transform[] smallSpots;
        public Transform[] mediumSpots;
        public Transform[] largeSpots;
        [Tooltip("A level can place each kind of cargo itself (the river does). Then a kind spawns once on each of its spots, fewer with fewer players, and the size spots above are not used.")]
        public KindSpots[] kindSpots;

        [Header("Truck")]
        [Tooltip("Carrying cargo into this area rescues it. Half size is in the transform's local X and Z.")]
        public Transform truckZone;
        public Vector2 truckZoneHalfSize = new Vector2(5f, 7f);
        [Tooltip("How far above and below the zone's height it counts, in local metres. 0 counts any height, which is right " +
                 "on one floor. On the airport the truck is on the upper deck, so standing under it doesn't count.")]
        [Min(0f)] public float truckZoneHalfHeight;
        [Tooltip("Rescued cargo lands in a grid on this transform's local X and Z.")]
        public Transform bedOrigin;
        public Vector2 bedHalfSize = new Vector2(1.4f, 2f);

        [Header("Map")]
        [Tooltip("The part of the world the map shows, in world X and Z.")]
        public Rect mapArea = new Rect(-23f, -49f, 46f, 98f);
        public Texture mapPicture;
        [Tooltip("The map shows world -Z at the top instead of +Z, the way the level was drawn.")]
        public bool mapUpsideDown;

        [Header("Music")]
        [Tooltip("A level's own match music, timed to the half: it plays from its start each half and switches to its " +
                 "fast part for the final stretch. None plays the usual music.")]
        public AudioClip matchMusic;
        [Tooltip("Plays while loading, between the halves and over the results, with the level's match music.")]
        public AudioClip matchInterlude;
        [Tooltip("Seconds from the start of matchMusic to its first fast beat.")]
        [Min(0f)] public float matchMusicSprintStart;

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
            return Mathf.Abs(local.x) <= truckZoneHalfSize.x && Mathf.Abs(local.z) <= truckZoneHalfSize.y
                && (truckZoneHalfHeight <= 0f || Mathf.Abs(local.y) <= truckZoneHalfHeight);
        }

        /// <summary>The level has its own timed match music.</summary>
        public bool HasTimedMusic => matchMusic != null && matchMusicSprintStart > 0f && matchMusicSprintStart < matchMusic.length;

        public Vector3 ClosestTruckZonePoint(Vector3 worldPosition)
        {
            Vector3 local = truckZone.InverseTransformPoint(worldPosition);
            local.x = Mathf.Clamp(local.x, -truckZoneHalfSize.x * 0.8f, truckZoneHalfSize.x * 0.8f);
            local.z = Mathf.Clamp(local.z, -truckZoneHalfSize.y * 0.8f, truckZoneHalfSize.y * 0.8f);
            local.y = 0f;
            return truckZone.TransformPoint(local);
        }

        public bool PlacesEachKind => kindSpots != null && kindSpots.Length > 0;

        public Transform[] SpotsFor(CargoKind kind)
        {
            if (kindSpots == null) return null;
            foreach (KindSpots entry in kindSpots)
                if (entry.kind == kind) return entry.spots;
            return null;
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

        /// <summary>Cargo lands square with the bed, turned however the truck came to rest.</summary>
        public Quaternion BedRotation => bedOrigin != null ? bedOrigin.rotation : Quaternion.identity;

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
