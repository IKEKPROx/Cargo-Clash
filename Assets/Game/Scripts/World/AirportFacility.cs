using System;
using UnityEngine;

namespace CargoClash
{
    public enum AirportUse : byte { None, Door, LadderUp, LadderDown, DuctDown }

    /// <summary>
    /// The airport's freight area (the team's Map3 de_airport, refined by Astra): the scene's half of the level. B03 is the
    /// belt that carries what the warehouse sends out; the iron door shuts the ladder room off from the upper deck; people
    /// climb the ladder and drop down the D1 duct just by walking into them; and a fall from the upper deck onto anything
    /// but the belt knocks you out.
    ///
    /// Every computer moves the scenery here by itself: the door from AirportMatch.DoorState, the belt from the match
    /// clock, the waiting cargo on the warehouse benches from AirportMatch.Dispatched. The rules that change the match
    /// (the door, the warehouse queue) are in <see cref="AirportMatch"/>, on the host. People's own computers move them
    /// along the ladder and the duct (PlayerAvatar), like all their movement.
    /// </summary>
    public sealed class AirportFacility : MonoBehaviour
    {
        public static AirportFacility Instance { get; private set; }

        /// <summary>The bots' map area of the ladder and duct links: nobody takes a crate for two up or down them.</summary>
        public const int TraversalArea = 3;

        [Header("B03 belt")]
        public Transform beltIn;
        public Transform beltOut;
        [Tooltip("The collider people and cargo stand on over the belt, flush with the floor.")]
        public Collider beltFloor;
        [Tooltip("The treads, which slide along and wrap round while a half is on.")]
        public Transform[] beltTreads;
        [Tooltip("Half the belt's width either side of its middle line.")]
        public float beltHalfWidth = 2.63f;

        [Header("Iron door")]
        public Transform doorLeaf;
        public Transform doorHinge;
        public Transform doorCenter;
        [Tooltip("A point inside the ladder room, straight out from the hinge.")]
        public Transform doorSwing;
        [Tooltip("How close to the door, or to where its leaf has swung, someone can stand and press E.")]
        public float useRange = 2.15f;

        [Header("Ladder and duct")]
        public Transform ladderLow;
        public Transform ladderShaft;
        public Transform ladderHigh;
        [Tooltip("On the belt under the shaft in the upper deck: where a fall is safe.")]
        public Transform shaftLanding;
        [Tooltip("On the hard floor beside the belt: where a fall isn't.")]
        public Transform unsafeDrop;
        public Transform ductUpper;
        public Transform ductLower;
        [Tooltip("The duct's route from the upper deck down to the floor, in order.")]
        public Transform[] ductWaypoints;
        public float ladderEntryRadius = 1.2f;
        public float ductEntryRadius = 0.85f;

        [Header("Warehouse queue")]
        [Tooltip("What the warehouse sends out onto B03 each half, in order (Astra's AirportCargoLayout.json). With fewer " +
                 "players it sends the first part of it.")]
        public CargoKind[] dispatchOrder = new CargoKind[0];
        [Tooltip("The same queue waiting on the infeed benches inside the warehouse, only for show. Each goes when its piece leaves.")]
        public GameObject[] queueDisplays = new GameObject[0];

        private GameConfig config;
        private Vector3 direction;
        private float beltLength;
        private Vector3[] treadOrigins = new Vector3[0];
        private float[] treadOffsets = new float[0];
        private Vector3[] upRoute;
        private Vector3[] downRoute;
        private Vector3[] ductRoute;
        private Vector3 doorLeafCenterLocal;
        private Vector3 intoRoom;
        private Quaternion closed;
        private float openAngle;
        private float angle;
        private int motionHalf;
        private double beltStartedAt;
        private double ductBusyUntil;
        private int shownQueue = -1;

        /// <summary>The way the belt runs, flat.</summary>
        public Vector3 BeltDirection => direction;

        /// <summary>How many physics steps have pushed a case along the belt so far (host only), for the tests.</summary>
        public int Conveyed { get; private set; }

        /// <summary>The upper deck's floor height (the top of the ladder).</summary>
        public float UpperDeckHeight => ladderHigh.position.y;

        /// <summary>The belt only runs, and a fall only counts, while a half is being played.</summary>
        public static bool BeltRunning
        {
            get
            {
                MatchDirector match = MatchDirector.Instance;
                return Instance != null && match != null && match.IsSpawned && match.Phase.Value == MatchPhase.Playing;
            }
        }

        private void Awake()
        {
            Instance = this;
            config = GameConfig.Get();
            Vector3 belt = beltOut.position - beltIn.position;
            belt.y = 0f;
            beltLength = belt.magnitude;
            direction = belt.normalized;
            treadOrigins = new Vector3[beltTreads.Length];
            treadOffsets = new float[beltTreads.Length];
            for (int i = 0; i < beltTreads.Length; i++)
            {
                Transform tread = beltTreads[i];
                treadOrigins[i] = tread.position;
                Renderer renderer = tread.GetComponent<Renderer>();
                Vector3 centre = renderer != null ? renderer.bounds.center : tread.position;
                treadOffsets[i] = Vector3.Dot(centre - beltIn.position, direction);
            }

            closed = doorHinge.rotation;
            doorLeafCenterLocal = doorHinge.InverseTransformPoint(doorCenter.position);
            intoRoom = Flat(doorSwing.position - doorHinge.position).normalized;
            openAngle = Vector3.SignedAngle(Flat(doorCenter.position - doorHinge.position), intoRoom, Vector3.up);

            Vector3 shaftHigh = new Vector3(ladderShaft.position.x, ladderHigh.position.y + 0.08f, ladderShaft.position.z);
            upRoute = new[] { ladderLow.position, ladderShaft.position, shaftHigh, ladderHigh.position + Vector3.up * 0.03f };
            downRoute = (Vector3[])upRoute.Clone();
            Array.Reverse(downRoute);
            ductRoute = Array.ConvertAll(ductWaypoints, point => point.position);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>The host's belt pushes loose cargo along, like it does the people on it.</summary>
        private void FixedUpdate()
        {
            MatchDirector match = MatchDirector.Instance;
            if (match == null || !match.IsSpawned || !match.IsServer || match.Phase.Value != MatchPhase.Playing) return;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (!OnBeltFootprint(cargo.transform.position, 0.62f)) continue;
                float bottom = cargo.BottomHeight - beltIn.position.y;
                if (bottom < -0.10f || bottom > 0.16f) continue;
                cargo.ServerConvey(direction, config.airportBeltSpeed, config.airportBeltPull * Time.fixedDeltaTime);
                Conveyed++;
            }
        }

        private void Update()
        {
            MatchDirector match = MatchDirector.Instance;
            AirportMatch airport = AirportMatch.Instance;
            int state = airport != null ? airport.DoorState.Value : 0;
            angle = Mathf.MoveTowards(angle, state * openAngle, config.airportDoorDegreesPerSecond * Time.deltaTime);
            doorHinge.rotation = Quaternion.AngleAxis(angle, Vector3.up) * closed;
            ShowQueue(airport);

            if (match == null || !match.IsSpawned || match.Phase.Value != MatchPhase.Playing) return;
            if (motionHalf != match.Half.Value)
            {
                motionHalf = match.Half.Value;
                beltStartedAt = match.PhaseEndsAt.Value - config.EffectiveHalfSeconds;
            }
            float travel = (float)((match.Now - beltStartedAt) * config.airportBeltSpeed);
            for (int i = 0; i < beltTreads.Length; i++)
                beltTreads[i].position = treadOrigins[i] + direction * (Mathf.Repeat(treadOffsets[i] + travel, beltLength) - treadOffsets[i]);
        }

        /// <summary>The cases still waiting in the warehouse: this half's queue, less what has gone out.</summary>
        private void ShowQueue(AirportMatch airport)
        {
            int planned = airport != null ? airport.Queued.Value : queueDisplays.Length;
            int gone = airport != null ? airport.Dispatched.Value : 0;
            int key = planned * 1000 + gone;
            if (key == shownQueue) return;
            shownQueue = key;
            for (int i = 0; i < queueDisplays.Length; i++)
            {
                if (queueDisplays[i] == null) continue;
                bool waiting = i >= gone && i < planned;
                if (queueDisplays[i].activeSelf != waiting) queueDisplays[i].SetActive(waiting);
            }
        }

        // ---- The ladder, the duct and the door ----

        /// <summary>
        /// Walking into the foot or the top of the ladder, or into the duct's mouth on the upper deck, starts the climb or
        /// the slide. The feet have to be at the opening and heading into it.
        /// </summary>
        public AirportUse FindTraversal(Vector3 feet, Vector3 movement)
        {
            if (movement.sqrMagnitude < 0.05f) return AirportUse.None;
            Vector3 heading = movement.normalized;
            if (Entered(ladderLow.position, ladderShaft.position, ladderEntryRadius, 0.40f)) return AirportUse.LadderUp;
            if (Entered(ladderHigh.position, ladderShaft.position, ladderEntryRadius, 0.40f)) return AirportUse.LadderDown;
            return Entered(ductUpper.position, ductRoute[1], ductEntryRadius, 0.60f) ? AirportUse.DuctDown : AirportUse.None;

            bool Entered(Vector3 entry, Vector3 target, float radius, float angleThreshold)
            {
                Vector3 offset = feet - entry;
                Vector3 intoOpening = Flat(target - entry).normalized;
                return Mathf.Abs(offset.y) < 0.45f && Flat(offset).sqrMagnitude <= radius * radius
                    && Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, intoOpening))) < 0.90f
                    && Vector3.Dot(heading, (intoOpening - Flat(offset) / radius).normalized) > angleThreshold
                    && Vector3.Dot(heading, intoOpening) > 0.25f;
            }
        }

        /// <summary>Someone standing here could use it: press E at the door, or start the climb or the slide.</summary>
        public bool CanReach(AirportUse use, Vector3 feet)
        {
            Vector3 entry;
            switch (use)
            {
                case AirportUse.Door:
                    Vector3 fixedCenter = doorCenter.position;
                    Vector3 swungCenter = doorHinge.TransformPoint(doorLeafCenterLocal);
                    return Mathf.Abs(fixedCenter.y - 1.15f - feet.y) < 1.25f
                        && Mathf.Min(Flat(fixedCenter - feet).sqrMagnitude, Flat(swungCenter - feet).sqrMagnitude) <= useRange * useRange;
                case AirportUse.LadderUp: entry = ladderLow.position; break;
                case AirportUse.LadderDown: entry = ladderHigh.position; break;
                case AirportUse.DuctDown: entry = ductUpper.position; break;
                default: return false;
            }
            float reach = (use == AirportUse.DuctDown ? ductEntryRadius : ladderEntryRadius) + 0.55f;
            return Mathf.Abs(entry.y - feet.y) < 0.65f && Flat(entry - feet).sqrMagnitude <= reach * reach;
        }

        /// <summary>
        /// What E does to the door: shut, it swings away from whoever pressed it (1 into the ladder room, -1 out onto the
        /// deck); open, it shuts from either side.
        /// </summary>
        public int DoorStateAfterToggle(int state, Vector3 feet)
        {
            if (state != 0) return 0;
            return Vector3.Dot(Flat(feet - doorCenter.position), intoRoom) <= 0f ? 1 : -1;
        }

        /// <summary>
        /// For the bots: where to walk to start the climb or the slide from here, a metre into the ladder's shaft from its
        /// foot or its top, or into the duct's mouth. False if neither is close.
        /// </summary>
        public bool TryEntryTarget(Vector3 feet, out Vector3 into)
        {
            into = default;
            Vector3 found = default;
            float best = 2.2f;
            Check(ladderLow.position, ladderShaft.position);
            Check(ladderHigh.position, ladderShaft.position);
            Check(ductUpper.position, ductRoute[1]);
            into = found;
            return best < 2.2f;

            void Check(Vector3 entry, Vector3 target)
            {
                float distance = Flat(entry - feet).magnitude;
                if (Mathf.Abs(entry.y - feet.y) > 0.8f || distance >= best) return;
                best = distance;
                found = entry + Flat(target - entry).normalized;
            }
        }

        /// <summary>The iron door stands between these two points, both up at its level.</summary>
        public bool DoorBetween(Vector3 a, Vector3 b)
        {
            float level = doorCenter.position.y - 1.15f;
            if (Mathf.Abs(a.y - level) > 1.5f || Mathf.Abs(b.y - level) > 1.5f) return false;
            float sideA = Vector3.Dot(Flat(a - doorCenter.position), intoRoom);
            float sideB = Vector3.Dot(Flat(b - doorCenter.position), intoRoom);
            return (sideA > 0f) != (sideB > 0f);
        }

        public Vector3[] Route(AirportUse use) => use switch
        {
            AirportUse.LadderUp => upRoute,
            AirportUse.LadderDown => downRoute,
            _ => ductRoute,
        };

        public float TravelSpeed(AirportUse use) => use == AirportUse.DuctDown ? config.airportDuctSpeed : config.airportLadderSpeed;

        public float TravelSeconds(AirportUse use, Vector3 start)
        {
            float distance = 0f;
            foreach (Vector3 point in Route(use))
            {
                distance += Vector3.Distance(start, point);
                start = point;
            }
            return distance / TravelSpeed(use);
        }

        /// <summary>One person at a time in the duct (host only). The ladder takes anyone.</summary>
        public bool ServerReserveTraversal(AirportUse use, double now, float seconds)
        {
            if (use != AirportUse.DuctDown) return true;
            if (now < ductBusyUntil) return false;
            ductBusyUntil = now + seconds;
            return true;
        }

        public void ResetTraversal() => ductBusyUntil = 0.0;

        // ---- The belt, the floors and falling ----

        private bool OnBeltFootprint(Vector3 point, float inletExtension = 0f)
        {
            Vector3 offset = Flat(point - beltIn.position);
            float along = Vector3.Dot(offset, direction);
            return along >= -inletExtension && along < beltLength
                && (offset - direction * along).sqrMagnitude <= beltHalfWidth * beltHalfWidth;
        }

        /// <summary>How fast the belt carries someone standing here: along it on the belt, nothing anywhere else.</summary>
        public Vector3 BeltVelocityAt(Vector3 feet) => OnBeltFootprint(feet) && Mathf.Abs(feet.y - beltIn.position.y) < 0.18f
            && TryFindFloor(feet, out RaycastHit floor) && floor.collider == beltFloor ? direction * config.airportBeltSpeed : Vector3.zero;

        /// <summary>
        /// Where the next piece comes out of the warehouse: at the belt's start, in the left or the right lane, whichever
        /// is clear. False while both are taken, so the queue waits.
        /// </summary>
        public bool TryDispatchPosition(Vector3 halfExtents, out Vector3 position)
        {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 centre = beltIn.position - direction * 0.42f + Vector3.up * (halfExtents.y + 0.06f);
            for (int lane = 0; lane < 2; lane++)
            {
                position = centre + side * (lane == 0 ? -0.90f : 0.90f);
                bool occupied = false;
                foreach (CargoItem cargo in CargoItem.All)
                {
                    if (cargo.State.Value != CargoState.Ground) continue;
                    Vector3 offset = cargo.transform.position - position;
                    if (Mathf.Abs(offset.y) > 1.4f
                        || Mathf.Abs(Vector3.Dot(offset, direction)) >= halfExtents.z + cargo.HalfExtents.z + 0.16f
                        || Mathf.Abs(Vector3.Dot(offset, side)) >= halfExtents.x + cargo.HalfExtents.x + 0.16f) continue;
                    occupied = true;
                    break;
                }
                if (!occupied) return true;
            }
            position = default;
            return false;
        }

        /// <summary>A floor right under someone's feet.</summary>
        public bool TryFindFloor(Vector3 feet, out RaycastHit floor) => Physics.Raycast(feet + Vector3.up * 0.16f,
            Vector3.down, out floor, 0.34f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore);

        /// <summary>Walkable floor within a metre of this height: where two people carrying a crate can step.</summary>
        public bool TryCarryFloor(Vector3 feet, out RaycastHit floor) => Physics.Raycast(feet + Vector3.up,
            Vector3.down, out floor, 2f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore) && floor.normal.y > 0.70f;

        /// <summary>Up on the deck with nothing under the feet: off the edge or over the shaft.</summary>
        public bool LostFooting(Vector3 feet) => feet.y > UpperDeckHeight - 0.40f && !TryFindFloor(feet, out _);

        /// <summary>A fall from the upper deck that ends on hard ground. Landing on the belt is safe.</summary>
        public bool IsLethalLanding(float peak, Vector3 feet, RaycastHit floor) =>
            peak >= UpperDeckHeight - 0.40f && peak - floor.point.y >= config.airportLethalDrop
            && !(floor.collider == beltFloor && OnBeltFootprint(feet));

        private static Vector3 Flat(Vector3 value) => Vector3.ProjectOnPlane(value, Vector3.up);
    }
}
