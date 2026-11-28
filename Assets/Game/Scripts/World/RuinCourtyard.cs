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
        // Two bits per breakable wall (room for 16), then one per wall for the side its stones were pushed to,
        // then three vine pickups and the ladder.
        private const int MaxWalls = 16;
        private const int PushSideBit = 32;
        private const int PickupBit = 48;
        private const ulong LadderBit = 1UL << 51;
        private const int MaxPatches = 12;
        private const int NoAction = 0;
        private const int Climb = 10;
        private const int Boost = 11;
        private const int PullTeammate = 12;
        private const int FirstPickup = 20;
        private const int ThrowVine = 30;

        [Serializable] private sealed class Spot { public string name; public Vector3 position; public float yaw; }
        [Serializable] private sealed class Block { public string name; public Vector4 rect; public Vector3 circle; public float minY, maxY; public bool round; }
        [Serializable] private sealed class WallSpec { public string id; public Vector4 footprintXZ; public Vector3 position; public float height; }
        [Serializable] private sealed class MapData
        {
            public Spot[] collectorSpawns, interrupterSpawns, smallSpots, mediumSpots, largeSpots;
            public Vector2 size, truckZoneHalfSize, bedHalfSize;
            public Vector3 truckZone;
            public Vector4[] pits;
            public Vector3[] pitReturns, vinePickups;
            public Block[] blockers;
            public WallSpec[] walls;
            // Where players can go (x min, z south, x max, z north): drawn in from the full courtyard.
            public Vector4 playArea;
            // Shipped breakables kept standing for good, no longer breakable (R7, the cracked floor on its steps).
            public WallSpec[] fixedWalls;
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
            // The separate stones of the wall, when it is built of them (RuinCourtyard.WallStones.cs).
            public Stone[] Stones;
            public float Ground;
            public int ShownStage = -1;
        }

        private MapData data;
        private GameConfig config;
        private Wall[] walls;
        private GameObject[] coils;
        private GameObject ladder, patchTemplate;
        private readonly List<GameObject> patchViews = new List<GameObject>();
        private double[] pickupReadyAt;
        private double ladderExpiresAt;
        private readonly RaycastHit[] rayHits = new RaycastHit[16];
        private Traverse ladderTraverse, boostTraverse;
        private PlayerAvatar boostHelper;
        private double boostReadyUntil;
        private ulong shownBits = ulong.MaxValue;
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
        private ulong Bits => Director != null && Director.IsSpawned ? Director.RuinBits.Value : 0UL;
        private double Now => Director.Now;
        private bool Playing => Director != null && Director.IsSpawned && Director.Phase.Value == MatchPhase.Playing;
        // A wall's two bits: 0 standing, 1 blown once, 3 blown twice, 2 down.
        private int WallState(int index) => (int)(Bits >> (index * 2) & 3);
        private static int BlastsIn(int state) => state == 1 ? 1 : state == 3 ? 2 : 0;
        private static int StateAfterBlasts(int blasts) => blasts <= 0 ? 0 : blasts == 1 ? 1 : 3;
        private bool PickupAvailable(int index) => (Bits & (1UL << (PickupBit + index))) == 0;
        private bool LadderInstalled => (Bits & LadderBit) != 0;

        private void Awake()
        {
            config = GameConfig.Get();
            data = JsonUtility.FromJson<MapData>(Load<TextAsset>("RuinLayout").text);
            if (data.vinePickups.Length > 3)
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
                shownBits = ulong.MaxValue;
            }
            if (Director.IsServer) ServerTick();
            if (shownBits != Bits)
            {
                ulong previous = shownBits;
                ShowState();
                if (previous != ulong.MaxValue && Playing)
                {
                    for (int i = 0; i < walls.Length; i++)
                        if ((int)(previous >> (i*2) & 3) != WallState(i))
                            PlayWallFeedback(i, WallState(i));
                }
            }
            if (Director.RuinPatches.Count != shownPatchCount || shownPatchCount > 0) ShowPatches();
            ShowStones();
        }

        private void SetBit(ulong bit, bool active)
        {
            Director.RuinBits.Value = active ? Bits | bit : Bits & ~bit;
            ShowState();
        }

        private void SetWall(int index, int state)
        {
            int shift = index * 2;
            Director.RuinBits.Value = (Bits & ~(3UL << shift)) | ((ulong)state << shift);
            ShowState();
            PlayWallFeedback(index, state);
        }

        private void PlayWallFeedback(int index, int state)
        {
            Vector3 centre = walls[index].Bounds.center;
            GameAudio.PlayAt(state == 0 ? "pickup" : "crack", centre);
            if (state != 0) FxBurst.PlayPuff(centre + Vector3.up * walls[index].Bounds.extents.y * .5f,
                                             new Color(.68f, .61f, .46f), state == 2 ? 14 : 6);
        }

        private void ShowState()
        {
            shownBits = Bits;
            for (int i = 0; i < walls.Length; i++)
            {
                Wall wall = walls[i];
                int state = WallState(i);
                // A wall of separate stones shows them instead of its shipped model (ShowStones).
                wall.Intact.gameObject.SetActive(wall.Stones == null && state != 2);
                if (wall.Cracked != null) wall.Cracked.gameObject.SetActive(false);
                // Stone walls leave their own stones lying about instead of the shipped rubble.
                wall.Broken.gameObject.SetActive(state == 2 && wall.Stones == null);
                wall.Collider.enabled = state != 2;
                if (wall.Obstacle != null) wall.Obstacle.enabled = state != 2;
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
            Director.RuinBits.Value = 0UL;
            Director.RuinPatches.Clear();
            Array.Clear(pickupReadyAt, 0, pickupReadyAt.Length);
            foreach (Wall wall in walls)
            {
                wall.LockedUntil = 0;
                wall.LastPullAt = 0;
                wall.LastPullFrame = -1;
                wall.Restore = 0;
            }
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
                SetBit(1UL << (PickupBit+i), false);
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
            if (!Occupied(Landing(player, current.End), player)) FinishTraverse(ref current, true);
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

        private static Bounds Landing(PlayerAvatar player, Vector3 point)
        {
            Bounds bounds = player.BodyBoundsAt(point);
            bounds.Expand(.1f);
            return bounds;
        }

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
            if (Occupied(Landing(player, data.boostExit), player)) return;
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
            if (teammate == null || Occupied(Landing(teammate, landing), teammate)) return;
            boostTraverse = StartTraverse(teammate, helper, data.boostStart, landing, config.ruinBoostSeconds);
        }

        private void UseLadder(PlayerAvatar player)
        {
            if (ladderTraverse != null || Occupied(Landing(player, data.ladderExit), player)) return;
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
                if (area.Intersects(player.BodyBoundsAt(player.transform.position))) return true;
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

        internal static bool BotCanReachCargo(PlayerAvatar player, CargoItem cargo)
        {
            if (Instance == null || cargo.transform.position.y < 1.7f) return true;
            return Mathf.Abs(cargo.transform.position.y - cargo.HalfExtents.y - player.transform.position.y) < 1.2f;
        }

        internal int FindUse(PlayerAvatar player, Vector3 aim, bool allowThrow = true)
        {
            if (!Playing || player.RuinClimbing.Value || player.CarriedCargo != null) return 0;
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
            if (action == Climb) UseLadder(player);
            else if (action == Boost) UseBoost(player);
            else if (action == PullTeammate) PullUp(player);
            else if (action >= FirstPickup && action < FirstPickup + coils.Length)
            {
                int index = action - FirstPickup;
                pickupReadyAt[index] = Now + config.ruinPickupSeconds;
                player.HasRuinVine.Value = true;
                SetBit(1UL << (PickupBit+index), true);
            }
            else if (action == ThrowVine) DeployVine(player, aim);
        }

        internal string UsePrompt(PlayerAvatar player, int action)
        {
            if (action == Climb) return LadderInstalled ? GameText.RuinClimb : GameText.RuinAttachVine;
            if (action == Boost)
                return player.RuinBoostReadyUntil.Value >= Now ? GameText.RuinBoostReady : GameText.RuinBoost;
            if (action == PullTeammate) return GameText.RuinPullTeammate;
            if (action >= FirstPickup && action < FirstPickup + coils.Length) return GameText.RuinTakeVine;
            return action == ThrowVine ? GameText.RuinThrowVine : null;
        }

        internal int WallCount => walls.Length;
        internal int VinePickupCount => coils.Length;
        internal int WallIndex(string id) => Array.FindIndex(data.walls, wall => wall.id == id);
        /// <summary>Blasts of a collector's blower a standing wall has taken: it carries them in its state.</summary>
        internal int WallBlasts(int index) => BlastsIn(WallState(index));
        /// <summary>The replicated state has room for two partly blown stages, so a wall takes at most three.</summary>
        internal int BlastsToBreak => Mathf.Clamp(config.ruinBlastsToBreak, 1, 3);
        internal Vector3 WallCentre(int index) => walls[index].Bounds.center;
        internal Bounds WallBounds(int index) => walls[index].Bounds;

        internal int FindWall(PlayerAvatar player, Vector3 aim, bool restore)
        {
            int best = -1;
            float bestDistance = float.PositiveInfinity;
            bool bestRayHit = false;
            float range = restore ? config.suckRange
                : (player.Side == Side.Collector ? config.blastRange : config.smashRange) + config.reachTolerance;
            Ray ray = new Ray(player.EyePosition, aim);
            for (int i = 0; i < walls.Length; i++)
            {
                int state = WallState(i);
                if (restore ? state == 0 : state == 2) continue;
                Wall wall = walls[i];
                // Standing in a broken wall's gap, sucking it back: that is the wall, wherever you look. Most
                // walls are lower than your eyes, so a level look from the gap passes over them.
                Vector3 feet = player.transform.position;
                if (restore && feet.x >= wall.Bounds.min.x && feet.x <= wall.Bounds.max.x &&
                    feet.z >= wall.Bounds.min.z && feet.z <= wall.Bounds.max.z && Mathf.Abs(feet.y - wall.Bounds.min.y) < 1f)
                    return i;
                bool rayHit = wall.Bounds.IntersectRay(ray, out float t) && t <= range;
                if (bestRayHit && !rayHit) continue;
                Vector3 point = rayHit ? ray.GetPoint(t+.015f) : wall.Bounds.ClosestPoint(player.EyePosition);
                Vector3 offset = point - player.EyePosition;
                float aimCone = restore ? config.suckAimDegrees : config.hitAimDegrees;
                if (!rayHit && offset.sqrMagnitude > .01f && Vector3.Angle(aim, offset) > aimCone) continue;
                if (!IsAimed(player, aim, point, range, wall.Collider)) continue;
                float distance = Vector3.Distance(point, player.EyePosition);
                if ((rayHit && !bestRayHit) || distance < bestDistance)
                {
                    bestDistance = distance;
                    bestRayHit = rayHit;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// One hammer blow knocks a wall down. A collector blows one down with BlastsToBreak blasts of the blower;
        /// each blast before the last knocks out a batch of its stones and stays in its state until it is
        /// repaired or knocked down.
        /// </summary>
        internal void ServerHitWall(PlayerAvatar player, int index, Vector3 aim)
        {
            if (!Playing || index < 0 || index >= walls.Length) return;
            Wall wall = walls[index];
            int state = WallState(index);
            if (state == 2 || Now < wall.LockedUntil ||
                !ValidAim(player, aim) || FindWall(player, aim, false) != index) return;
            wall.Restore = 0;
            // The side the stones go: away from whoever hit it. Every machine lays them down the same way.
            Vector3 across = wall.Bounds.size.x < wall.Bounds.size.z ? Vector3.right : Vector3.forward;
            SetBit(1UL << (PushSideBit + index), Vector3.Dot(wall.Bounds.center - player.transform.position, across) >= 0f);
            int blasts = BlastsIn(state) + 1;
            if (player.Side == Side.Collector && blasts < BlastsToBreak)
            {
                SetWall(index, StateAfterBlasts(blasts));
                return;
            }
            SetWall(index, 2);
            wall.LockedUntil = Now + config.ruinWallSwitchCooldown;
        }

        internal bool ServerRestoreWall(PlayerAvatar player, float delta, Vector3 aim)
        {
            if (!Playing || player.Side != Side.Collector || !ValidAim(player, aim)) return false;
            int index = FindWall(player, aim, true);
            if (index < 0) return false;
            Wall wall = walls[index];
            if (Now < wall.LockedUntil) return true;
            if (Now - wall.LastPullAt > .18) wall.Restore = 0;
            // Apply at most one frame of work, even if several collectors aim at the wall.
            if (wall.LastPullFrame == Time.frameCount) return true;
            wall.LastPullFrame = Time.frameCount;
            wall.LastPullAt = Now;
            wall.Restore = Mathf.Min(config.ruinRestoreSeconds, wall.Restore + delta);
            if (wall.Restore < config.ruinRestoreSeconds) return true;
            // Anyone or anything standing in the gap steps out to their own side: the restorer usually stands
            // right at it, and waiting for an empty gap meant the wall never came back.
            ClearWallGap(wall);
            wall.Restore = 0;
            wall.LockedUntil = Now + config.ruinWallSwitchCooldown;
            SetWall(index, 0);
            return true;
        }

        private static void ClearWallGap(Wall wall)
        {
            Bounds gap = wall.Bounds;
            Vector3 across = gap.size.x < gap.size.z ? Vector3.right : Vector3.forward;
            float half = Vector3.Dot(gap.extents, across);
            foreach (PlayerAvatar player in PlayerAvatar.All)
            {
                if (player == null || !player.IsSpawned) continue;
                Vector3 feet = player.transform.position;
                Bounds body = player.BodyBoundsAt(feet);
                if (!gap.Intersects(body)) continue;
                float offset = Vector3.Dot(feet - gap.center, across);
                float side = offset >= 0f ? 1f : -1f;
                float clear = half + Vector3.Dot(body.extents, across) + .1f;
                player.TeleportRpc(feet + across * (side * clear - offset), player.transform.rotation);
            }
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value != CargoState.Ground || cargo.Body == null) continue;
                Vector3 extents = Abs(cargo.transform.right * cargo.HalfExtents.x) + Abs(cargo.transform.up * cargo.HalfExtents.y) +
                                  Abs(cargo.transform.forward * cargo.HalfExtents.z);
                if (!gap.Intersects(new Bounds(cargo.transform.position, extents * 2f))) continue;
                float offset = Vector3.Dot(cargo.transform.position - gap.center, across);
                float side = offset >= 0f ? 1f : -1f;
                cargo.Body.position = cargo.transform.position + across * (side * (half + Vector3.Dot(extents, across) + .1f) - offset);
            }
        }

        internal static bool CargoReach(PlayerAvatar player, CargoItem cargo)
        {
            if (Instance == null) return true;
            return Mathf.Abs(player.transform.position.y - (cargo.transform.position.y - cargo.HalfExtents.y)) < 1.2f &&
                   !Physics.Linecast(player.EyePosition, cargo.transform.position, GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
        }
    }
}
