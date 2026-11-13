using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CargoClash
{
    [Serializable]
    public struct RuinPatch : INetworkSerializable, IEquatable<RuinPatch>
    {
        public Vector3 Position;
        public double ExpiresAt;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref ExpiresAt);
        }

        public bool Equals(RuinPatch other) => Position == other.Position && ExpiresAt == other.ExpiresAt;
    }

    /// <summary>Scene geometry and host rules for the Ruin Courtyard. MatchDirector replicates its state.</summary>
    [DefaultExecutionOrder(-300)]
    [RequireComponent(typeof(LevelLayout))]
    public sealed partial class RuinCourtyard : MonoBehaviour
    {
        public static RuinCourtyard Instance { get; private set; }
        private const string ResourceRoot = "RuinCourtyard/";
        public const string SceneName = "LevelRuinCourtyard";
        // Twelve two-bit wall states, then two doors, three pickups and the ladder.
        private const int DoorBit = 24;
        private const int PickupBit = 26;
        private const uint LadderBit = 1u << 29;
        private const int MaxPatches = 12;
        private const int NoAction = 0;
        private const int FirstDoor = 1;
        private const int Climb = 10;
        private const int Boost = 11;
        private const int PullTeammate = 12;
        private const int FirstPickup = 20;
        private const int ThrowVine = 30;

        [Serializable] private sealed class Spot { public string name; public Vector3 position; public float yaw; }
        [Serializable] private sealed class Block { public string name; public Vector4 rect; public Vector3 circle; public float minY, maxY; public bool round; }
        [Serializable] private sealed class WallSpec { public string id; public Vector4 footprintXZ; public Vector3 position; public float height; }
        [Serializable] private sealed class DoorSpec { public string id; public Vector3 center; public float width, height; public string[] leaves, hinges; }
        [Serializable] private sealed class MapData
        {
            public Spot[] collectorSpawns, interrupterSpawns, smallSpots, mediumSpots, largeSpots;
            public Vector2 size, truckZoneHalfSize, bedHalfSize;
            public Vector3 truckZone;
            public Vector4[] pits;
            public Vector3[] pitReturns, vinePickups;
            public Block[] blockers;
            public WallSpec[] walls;
            public DoorSpec[] doors;
            public Vector3 boostStart, boostExit, ladderStart, ladderExit, ladderAnchor;
        }

        private sealed class Wall
        {
            public Transform Intact, Cracked, Broken;
            public Collider Collider;
            public NavMeshObstacle Obstacle;
            public Bounds Bounds;
            public double LockedUntil;
            public double LastPullAt;
            public int LastPullFrame = -1;
            public float Restore;
        }

        private sealed class Door
        {
            public Transform[] Hinges, Leaves;
            public BoxCollider[] Boxes;
            public Quaternion[] Open, Closed;
            public Bounds Sweep;
            public double LockedUntil;
        }

        private MapData data;
        private GameConfig config;
        private Wall[] walls;
        private Door[] doors;
        private GameObject[] coils;
        private GameObject ladder, patchTemplate;
        private readonly List<GameObject> patchViews = new List<GameObject>();
        private double[] pickupReadyAt;
        private double ladderExpiresAt;
        private readonly RaycastHit[] rayHits = new RaycastHit[16];
        private Traverse ladderTraverse, boostTraverse;
        private PlayerAvatar boostHelper;
        private double boostReadyUntil;
        private uint shownBits = uint.MaxValue;
        private MatchDirector lastDirector;
        private int shownPatchCount = -1;
        private bool built;

        private sealed class Traverse
        {
            public PlayerAvatar Player, Helper;
            public bool NeedsHelper;
            public Vector3 Start, End, HelperPosition;
            public double FinishesAt;
        }

        private MatchDirector Director => MatchDirector.Instance;
        private uint Bits => Director != null ? Director.RuinBits.Value : 0;
        private double Now => Director.Now;
        private bool Playing => Director != null && Director.IsSpawned && Director.Phase.Value == MatchPhase.Playing;
        private int WallState(int index) => (int)(Bits >> (index * 2) & 3);
        private bool DoorClosed(int index) => (Bits & (1u << (DoorBit + index))) != 0;
        private bool PickupAvailable(int index) => (Bits & (1u << (PickupBit + index))) == 0;
        private bool LadderInstalled => (Bits & LadderBit) != 0;

        private void Awake()
        {
            config = GameConfig.Get();
            data = JsonUtility.FromJson<MapData>(Load<TextAsset>("RuinLayout").text);
            if (data.walls.Length != 12 || data.doors.Length != 2 || data.vinePickups.Length != 3)
                throw new InvalidOperationException("Ruin Courtyard layout does not fit the replicated state.");
            BuildField();
            Instance = this;
            built = true;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            DisposeField();
        }

        private void Update()
        {
            if (!built || Director == null || !Director.IsSpawned) return;
            if (lastDirector != Director)
            {
                lastDirector = Director;
                shownPatchCount = -1;
                shownBits = uint.MaxValue;
            }
            if (Director.IsServer) ServerTick();
            if (shownBits != Bits)
            {
                uint previous = shownBits;
                ShowState();
                if (previous != uint.MaxValue && Playing)
                    for (int i = 0; i < walls.Length; i++)
                        if ((previous >> (i*2) & 3) != WallState(i))
                            PlayWallFeedback(i, WallState(i));
            }
            if (Director.RuinPatches.Count != shownPatchCount || shownPatchCount > 0) ShowPatches();
        }

        private void SetBit(uint bit, bool active)
        {
            Director.RuinBits.Value = active ? Bits | bit : Bits & ~bit;
            ShowState();
        }

        private void SetWall(int index, int state)
        {
            int shift = index * 2;
            Director.RuinBits.Value = (Bits & ~(3u << shift)) | ((uint)state << shift);
            ShowState();
            PlayWallFeedback(index, state);
        }

        private void PlayWallFeedback(int index, int state)
        {
            Vector3 centre = walls[index].Bounds.center;
            GameAudio.PlayAt(state == 0 ? "pickup" : "crack", centre);
            if (state == 2) FxBurst.PlayPuff(centre, new Color(.68f, .61f, .46f), 8);
        }

        private void ShowState()
        {
            shownBits = Bits;
            for (int i = 0; i < walls.Length; i++)
            {
                Wall wall = walls[i];
                int state = WallState(i);
                wall.Intact.gameObject.SetActive(state == 0 || state == 1 && wall.Cracked == null);
                if (wall.Cracked != null) wall.Cracked.gameObject.SetActive(state == 1);
                wall.Broken.gameObject.SetActive(state == 2);
                wall.Collider.enabled = state != 2;
                if (wall.Obstacle != null) wall.Obstacle.enabled = state != 2;
            }
            for (int i = 0; i < doors.Length; i++)
            {
                Door door = doors[i];
                Quaternion[] rotations = DoorClosed(i) ? door.Closed : door.Open;
                for (int j = 0; j < door.Hinges.Length; j++)
                {
                    door.Hinges[j].localRotation = rotations[j];
                    Vector3 along = door.Leaves[j].position - door.Hinges[j].position;
                    along.y = 0f;
                    door.Boxes[j].transform.SetPositionAndRotation(door.Leaves[j].position,
                        Quaternion.LookRotation(Vector3.Cross(along, Vector3.up)));
                    door.Boxes[j].enabled = true;
                }
            }
            for (int i = 0; i < coils.Length; i++) coils[i].SetActive(PickupAvailable(i));
            ladder.SetActive(LadderInstalled);
            Physics.SyncTransforms();
        }

        private void ShowPatches()
        {
            var patches = Director.RuinPatches;
            shownPatchCount = patches.Count;
            for (int i = 0; i < shownPatchCount; i++)
            {
                if (i == patchViews.Count) patchViews.Add(Instantiate(patchTemplate, transform));
                GameObject patch = patchViews[i];
                patch.transform.position = patches[i].Position;
                patch.SetActive(true);
            }
            for (int i = shownPatchCount; i < patchViews.Count; i++) patchViews[i].SetActive(false);
        }

        internal void ServerReset()
        {
            Director.RuinBits.Value = 0;
            Director.RuinPatches.Clear();
            Array.Clear(pickupReadyAt, 0, pickupReadyAt.Length);
            foreach (Wall wall in walls)
            {
                wall.LockedUntil = 0;
                wall.LastPullAt = 0;
                wall.LastPullFrame = -1;
                wall.Restore = 0;
            }
            foreach (Door door in doors) door.LockedUntil = 0;
            ladderTraverse = boostTraverse = null;
            boostHelper = null;
            ladderExpiresAt = 0;
            ShowState();
        }

        private void ServerTick()
        {
            if (!Playing)
            {
                FinishTraverse(ref ladderTraverse, false);
                FinishTraverse(ref boostTraverse, false);
                return;
            }
            for (int i = 0; i < pickupReadyAt.Length; i++)
            {
                if (pickupReadyAt[i] == 0 || pickupReadyAt[i] > Now) continue;
                pickupReadyAt[i] = 0;
                SetBit(1u << (PickupBit+i), false);
            }
            for (int i = Director.RuinPatches.Count-1; i >= 0; i--)
                if (Director.RuinPatches[i].ExpiresAt <= Now) Director.RuinPatches.RemoveAt(i);
            TickTraverse(ref ladderTraverse);
            TickTraverse(ref boostTraverse);
            if (LadderInstalled && Now >= ladderExpiresAt && ladderTraverse == null) SetBit(LadderBit, false);
        }

        private void TickTraverse(ref Traverse current)
        {
            if (current == null) return;
            PlayerAvatar player = current.Player;
            if (player == null || !player.IsSpawned || player.KnockedOut.Value || player.IsKnockedDown || player.IsStaggered ||
                current.NeedsHelper && (current.Helper == null || !StillSupporting(current) || current.Helper.Team != player.Team))
            {
                FinishTraverse(ref current, false);
                return;
            }
            if (Now < current.FinishesAt) return;
            if (!Occupied(Landing(current.End), player)) FinishTraverse(ref current, true);
            else if (Now >= current.FinishesAt + config.ruinLandingWaitSeconds) FinishTraverse(ref current, false);
        }

        private bool StillSupporting(Traverse current)
        {
            if (!current.Helper.IsSpawned || current.Helper.IsKnockedDown || current.Helper.IsStaggered ||
                current.Helper.CarriedCargo != null || current.Helper.RuinClimbing.Value) return false;
            return Vector3.Distance(current.Helper.transform.position, current.HelperPosition) <= config.ruinUseRange;
        }

        private static void FinishTraverse(ref Traverse current, bool success)
        {
            if (current == null) return;
            if (current.Player != null && current.Player.IsSpawned)
                current.Player.ServerFinishRuinTraverse(success ? current.End : current.Start);
            current = null;
        }

        private Traverse StartTraverse(PlayerAvatar player, PlayerAvatar helper, Vector3 start, Vector3 end, float seconds)
        {
            player.ServerStartRuinTraverse(start, end, seconds);
            return new Traverse { Player = player, Helper = helper, NeedsHelper = helper != null,
                HelperPosition = helper != null ? helper.transform.position : default,
                Start = start, End = end, FinishesAt = Now + seconds };
        }

        private static Bounds Landing(Vector3 point) =>
            new Bounds(point + Vector3.up * .8f, new Vector3(1.1f, 1.6f, 1.1f));

        private bool ReadyToBoost(PlayerAvatar player) => player.IsSpawned && !player.KnockedOut.Value && !player.IsKnockedDown && !player.IsStaggered
            && player.CarriedCargo == null && !player.RuinClimbing.Value && Vector3.Distance(player.transform.position, data.boostStart) <= config.ruinUseRange;

        private void UseBoost(PlayerAvatar player)
        {
            if (boostTraverse != null || !ReadyToBoost(player)) return;
            if (boostHelper == null || boostHelper == player || boostReadyUntil < Now || boostHelper.Team != player.Team || !ReadyToBoost(boostHelper))
            {
                if (boostHelper != null && boostHelper.IsSpawned) boostHelper.RuinBoostReadyUntil.Value = 0;
                boostHelper = player;
                boostReadyUntil = Now + config.ruinBoostReadySeconds;
                player.RuinBoostReadyUntil.Value = boostReadyUntil;
                return;
            }
            if (Occupied(Landing(data.boostExit), player)) return;
            boostTraverse = StartTraverse(player, boostHelper, data.boostStart, data.boostExit, config.ruinBoostSeconds);
            boostHelper.RuinBoostReadyUntil.Value = 0;
            boostHelper = null;
        }

        private PlayerAvatar TeammateBelow(PlayerAvatar helper)
        {
            foreach (PlayerAvatar player in PlayerAvatar.All)
                if (player != helper && player.Team == helper.Team && ReadyToBoost(player)) return player;
            return null;
        }

        private void PullUp(PlayerAvatar helper)
        {
            if (boostTraverse != null) return;
            PlayerAvatar teammate = TeammateBelow(helper);
            Vector3 landing = data.boostExit + Vector3.right * 1.15f;
            if (teammate == null || Occupied(Landing(landing), teammate)) return;
            boostTraverse = StartTraverse(teammate, helper, data.boostStart, landing, config.ruinBoostSeconds);
        }

        private void UseLadder(PlayerAvatar player)
        {
            if (ladderTraverse != null || Occupied(Landing(data.ladderExit), player)) return;
            if (!LadderInstalled)
            {
                if (!player.HasRuinVine.Value) return;
                player.HasRuinVine.Value = false;
                ladderExpiresAt = Now + config.ruinLadderSeconds;
                SetBit(LadderBit, true);
            }
            ladderTraverse = StartTraverse(player, null, data.ladderStart, data.ladderExit, config.ruinClimbSeconds);
        }

        private void DeployVine(PlayerAvatar player, Vector3 aim)
        {
            if (!player.HasRuinVine.Value || Director.RuinPatches.Count >= MaxPatches) return;
            if (!Physics.Raycast(player.EyePosition, aim, out RaycastHit hit, config.ruinThrowRange, GameLayers.LevelMask,
                                 QueryTriggerInteraction.Ignore) || hit.normal.y < .8f) return;
            Vector3 point = hit.point;
            if (Vector3.Distance(player.transform.position, point) > config.ruinThrowRange) return;
            if (!LevelLayout.Instance.mapArea.Contains(new Vector2(point.x, point.z)) ||
                Mathf.Abs(point.y - player.transform.position.y) > .55f || point.y < -.05f ||
                LevelLayout.Instance.InTruckZone(point) || ProtectedVineSite(point)) return;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector3 edge = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * config.ruinPatchRadius;
                if (!LevelLayout.Instance.mapArea.Contains(new Vector2(edge.x, edge.z)) ||
                    !Physics.Raycast(edge + Vector3.up * .3f, Vector3.down, out RaycastHit floor, .65f,
                                     GameLayers.LevelMask, QueryTriggerInteraction.Ignore) ||
                    floor.normal.y < .8f || Mathf.Abs(floor.point.y-point.y) > .25f)
                    return;
            }
            player.HasRuinVine.Value = false;
            Director.RuinPatches.Add(new RuinPatch { Position = point + Vector3.up * .025f, ExpiresAt = Now + config.ruinPatchSeconds });
        }

        private bool ProtectedVineSite(Vector3 p)
        {
            float radius = config.ruinPatchRadius + .55f;
            foreach (Transform spawn in LevelLayout.Instance.collectorSpawns)
                if (Vector3.Distance(p, spawn.position) < radius) return true;
            foreach (Transform spawn in LevelLayout.Instance.interrupterSpawns)
                if (Vector3.Distance(p, spawn.position) < radius) return true;
            foreach (Vector3 spot in data.pitReturns)
                if (Vector3.Distance(p, spot) < radius) return true;
            Vector3 local = LevelLayout.Instance.truckZone.InverseTransformPoint(p);
            Vector2 half = LevelLayout.Instance.truckZoneHalfSize;
            if (Mathf.Abs(local.x) < half.x + radius && Mathf.Abs(local.z) < half.y + radius) return true;
            return Vector3.Distance(p, data.boostStart) < radius || Vector3.Distance(p, data.boostExit) < radius ||
                   Vector3.Distance(p, data.ladderStart) < radius || Vector3.Distance(p, data.ladderExit) < radius;
        }

        internal float SpeedAt(Vector3 p)
        {
            if (!Playing) return 1f;
            float radiusSquared = config.ruinPatchRadius * config.ruinPatchRadius;
            foreach (RuinPatch patch in Director.RuinPatches)
            {
                Vector3 delta = p - patch.Position;
                if (Mathf.Abs(delta.y) <= .6f && delta.x * delta.x + delta.z * delta.z <= radiusSquared)
                    return config.ruinPatchSpeed;
            }
            return 1f;
        }

        internal bool TryPitReturn(Vector3 point, out Vector3 position)
        {
            position = default;
            if (point.y >= config.ruinPitReturnHeight) return false;
            int nearest = 0;
            float best = float.PositiveInfinity;
            for (int i = 0; i < data.pits.Length; i++)
            {
                Vector4 pit = data.pits[i];
                float distance = (new Vector2((pit.x+pit.z)*.5f, (pit.y+pit.w)*.5f) - new Vector2(point.x, point.z)).sqrMagnitude;
                if (distance < best) { best = distance; nearest = i; }
            }
            position = data.pitReturns[nearest];
            return true;
        }

        private static bool Occupied(Bounds area, PlayerAvatar ignored = null)
        {
            foreach (PlayerAvatar player in PlayerAvatar.All)
            {
                if (player == ignored) continue;
                if (area.Intersects(new Bounds(player.transform.position + Vector3.up * .8f, new Vector3(1f, 1.6f, 1f)))) return true;
            }
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value != CargoState.Ground && cargo.State.Value != CargoState.Carried) continue;
                Vector3 extents = cargo.HalfExtents;
                Vector3 right = cargo.transform.right * extents.x, up = cargo.transform.up * extents.y, forward = cargo.transform.forward * extents.z;
                Vector3 aabb = Abs(right) + Abs(up) + Abs(forward);
                if (area.Intersects(new Bounds(cargo.transform.position, aabb * 2f))) return true;
            }
            return false;
        }

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        private bool IsAimed(PlayerAvatar player, Vector3 direction, Vector3 point, float range, Collider target = null, Collider alternate = null)
        {
            Vector3 offset = point - player.EyePosition;
            float distance = offset.magnitude;
            if (distance > range || (distance > .12f && Vector3.Dot(offset / distance, direction) < .55f)) return false;
            if (distance < .02f) return true;
            int count = Physics.RaycastNonAlloc(player.EyePosition, offset / distance, rayHits, distance,
                                                GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            if (count == rayHits.Length) return false;
            for (int i = 0; i < count; i++)
                if (rayHits[i].collider != target && rayHits[i].collider != alternate) return false;
            return true;
        }

        internal static bool ValidAim(PlayerAvatar player, Vector3 aim)
        {
            if (!(aim.sqrMagnitude > .9f && aim.sqrMagnitude < 1.1f)) return false;
            Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up);
            return flat.sqrMagnitude < .03f || Vector3.Dot(flat.normalized, player.transform.forward) >= .4f;
        }

        internal bool TryBotOpenDoor(PlayerAvatar player, Vector3 goal)
        {
            if (!Playing || !Director.IsServer || player.CarriedCargo != null) return false;
            for (int i = 0; i < doors.Length; i++)
            {
                if (!DoorClosed(i)) continue;
                Door door = doors[i];
                Vector3 feet = player.transform.position;
                if (Mathf.Abs(goal.z - door.Sweep.center.z) < .3f ||
                    (goal.z - door.Sweep.center.z) * (feet.z - door.Sweep.center.z) >= 0f) continue;
                Vector3 target = DoorHandlePoint(i, player);
                Vector3 direction = (target - player.EyePosition).normalized;
                if (!IsAimed(player, direction, target, config.ruinUseRange + .5f, door.Boxes[0], door.Boxes[1])) continue;
                TrySetDoor(i, false);
                return true;
            }
            return false;
        }

        internal static bool BotCanReachCargo(PlayerAvatar player, CargoItem cargo)
        {
            if (Instance == null || cargo.transform.position.y < 1.7f) return true;
            return Mathf.Abs(cargo.transform.position.y - cargo.HalfExtents.y - player.transform.position.y) < 1.2f;
        }

        private Vector3 DoorHandlePoint(int index, PlayerAvatar player)
        {
            Vector3 point = data.doors[index].center + Vector3.up;
            bool outside = player.transform.position.z < point.z;
            if (DoorClosed(index)) point.z += outside ? -.19f : .19f;
            else if (outside) point.z -= .15f;
            return point;
        }

        internal int FindUse(PlayerAvatar player, Vector3 aim, bool allowThrow = true)
        {
            if (!Playing || player.RuinClimbing.Value || player.CarriedCargo != null) return 0;
            for (int i = 0; i < doors.Length; i++)
                if (IsAimed(player, aim, DoorHandlePoint(i, player), config.ruinUseRange + .5f, doors[i].Boxes[0], doors[i].Boxes[1])) return FirstDoor + i;
            if (IsAimed(player, aim, data.ladderStart + Vector3.up * .85f, config.ruinUseRange) &&
                (LadderInstalled || player.HasRuinVine.Value)) return Climb;
            if (ReadyToBoost(player) && IsAimed(player, aim, data.boostStart + Vector3.up, config.ruinUseRange)) return Boost;
            if (Vector3.Distance(player.transform.position, data.boostExit) < 2f && TeammateBelow(player) != null)
                return PullTeammate;
            if (!player.HasRuinVine.Value)
            {
                for (int i = 0; i < coils.Length; i++)
                    if (PickupAvailable(i) && IsAimed(player, aim, data.vinePickups[i] + Vector3.up * .25f, config.ruinUseRange))
                        return FirstPickup + i;
            }
            return allowThrow && player.HasRuinVine.Value && aim.y < -.2f ? ThrowVine : NoAction;
        }

        internal void ServerUse(PlayerAvatar player, int action, Vector3 aim)
        {
            if (!Playing || !ValidAim(player, aim) || action == 0 || FindUse(player, aim) != action) return;
            if (action >= FirstDoor && action < FirstDoor + doors.Length)
            {
                int index = action - FirstDoor;
                TrySetDoor(index, !DoorClosed(index));
            }
            else if (action == Climb) UseLadder(player);
            else if (action == Boost) UseBoost(player);
            else if (action == PullTeammate) PullUp(player);
            else if (action >= FirstPickup && action < FirstPickup + coils.Length)
            {
                int index = action - FirstPickup;
                pickupReadyAt[index] = Now + config.ruinPickupSeconds;
                player.HasRuinVine.Value = true;
                SetBit(1u << (PickupBit+index), true);
            }
            else if (action == ThrowVine) DeployVine(player, aim);
        }

        private void TrySetDoor(int index, bool closed)
        {
            Door door = doors[index];
            if (Now < door.LockedUntil || Occupied(door.Sweep)) return;
            door.LockedUntil = Now + config.ruinDoorCooldown;
            SetBit(1u << (DoorBit+index), closed);
        }

        internal string UsePrompt(PlayerAvatar player, int action)
        {
            if (action >= FirstDoor && action < FirstDoor + doors.Length)
                return Occupied(doors[action-FirstDoor].Sweep) ? GameText.RuinDoorBlocked :
                    DoorClosed(action-FirstDoor) ? GameText.RuinOpenDoor : GameText.RuinCloseDoor;
            if (action == Climb) return LadderInstalled ? GameText.RuinClimb : GameText.RuinAttachVine;
            if (action == Boost)
                return player.RuinBoostReadyUntil.Value >= Now ? GameText.RuinBoostReady : GameText.RuinBoost;
            if (action == PullTeammate) return GameText.RuinPullTeammate;
            if (action >= FirstPickup && action < FirstPickup + coils.Length) return GameText.RuinTakeVine;
            return action == ThrowVine ? GameText.RuinThrowVine : null;
        }

        internal int FindWall(PlayerAvatar player, Vector3 aim, bool restore)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            float range = restore ? config.suckRange : config.smashRange + config.reachTolerance;
            for (int i = 0; i < walls.Length; i++)
            {
                int state = WallState(i);
                if (restore ? state == 0 : state == 2) continue;
                Wall wall = walls[i];
                Ray ray = new Ray(player.EyePosition, aim);
                bool rayHit = wall.Bounds.IntersectRay(ray, out float t) && t <= range;
                Vector3 point = rayHit ? ray.GetPoint(t+.015f) : wall.Bounds.ClosestPoint(player.EyePosition);
                Vector3 offset = point - player.EyePosition;
                float aimCone = restore ? config.suckAimDegrees : config.hitAimDegrees;
                if (!rayHit && offset.sqrMagnitude > .01f && Vector3.Angle(aim, offset) > aimCone) continue;
                if (!IsAimed(player, aim, point, range, wall.Collider)) continue;
                float distance = Vector3.Distance(point, player.EyePosition);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        internal void ServerHitWall(PlayerAvatar player, int index, Vector3 aim)
        {
            if (!Playing || player.Side != Side.Interrupter || index < 0 || index >= walls.Length) return;
            Wall wall = walls[index];
            int state = WallState(index);
            if (state == 2 || Now < wall.LockedUntil ||
                !ValidAim(player, aim) || FindWall(player, aim, false) != index) return;
            wall.Restore = 0;
            if (wall.Cracked != null && state == 0) SetWall(index, 1);
            else
            {
                SetWall(index, 2);
                wall.LockedUntil = Now + config.ruinWallSwitchCooldown;
            }
        }

        internal void ServerRestoreWall(PlayerAvatar player, float delta, Vector3 aim)
        {
            if (!Playing || player.Side != Side.Collector) return;
            if (!ValidAim(player, aim)) return;
            int index = FindWall(player, aim, true);
            if (index < 0) return;
            Wall wall = walls[index];
            if (Now < wall.LockedUntil) return;
            if (Now - wall.LastPullAt > .18) wall.Restore = 0;
            // Apply at most one frame of work, even if several collectors aim at the wall.
            if (wall.LastPullFrame == Time.frameCount) return;
            wall.LastPullFrame = Time.frameCount;
            wall.LastPullAt = Now;
            wall.Restore = Mathf.Min(config.ruinRestoreSeconds, wall.Restore + delta);
            Bounds sweep = wall.Bounds;
            sweep.Expand(wall.Cracked != null ? new Vector3(.25f, 1.1f, .25f) : Vector3.one * .15f);
            if (wall.Restore < config.ruinRestoreSeconds || Occupied(sweep)) return;
            wall.Restore = 0;
            wall.LockedUntil = Now + config.ruinWallSwitchCooldown;
            SetWall(index, 0);
        }

        internal static bool CargoReach(PlayerAvatar player, CargoItem cargo)
        {
            if (Instance == null) return true;
            return Mathf.Abs(player.transform.position.y - (cargo.transform.position.y - cargo.HalfExtents.y)) < 1.2f &&
                   !Physics.Linecast(player.EyePosition, cargo.transform.position, GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
        }
    }
}
