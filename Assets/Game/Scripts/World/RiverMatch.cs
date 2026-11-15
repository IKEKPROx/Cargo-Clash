using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The river level's rules. It sits on the MatchDirector's network object and does nothing on a level without a
    /// <see cref="RiverLevel"/>.
    ///
    /// The host floats cargo that lands in the water and lets the current carry it downstream, slowly, so there is
    /// time to chase it along the bank or suck it out. Whatever leaves the field is lost and nobody scores it. Each
    /// half it puts a plank on every spot in the two huts. A plank pushed into an empty slot of the catcher downstream
    /// blocks that slot for GameConfig.plankSeconds, each slot on its own clock: plugging one gap doesn't renew the
    /// others, so keeping the catcher shut means going back to the huts again and again.
    ///
    /// Everyone shows the catcher from the slot times this replicates. The boards age as they go (Astra's three looks).
    /// </summary>
    public sealed class RiverMatch : NetworkBehaviour
    {
        public static RiverMatch Instance { get; private set; }

        /// <summary>When the plank in each catcher slot went in, in server time, or 0 for an empty slot.</summary>
        private NetworkList<double> slots;

        private GameConfig config;
        private RiverLevel level;
        private bool[] filled = new bool[0];
        private double[] since = new double[0];
        private readonly List<CargoItem> sweep = new List<CargoItem>();

        private double Now => NetworkManager.ServerTime.Time;

        private void Awake()
        {
            slots = new NetworkList<double>();
        }

        public override void OnNetworkSpawn()
        {
            config = GameConfig.Get();
            level = RiverLevel.Instance;
            if (level == null) return;
            Instance = this;
            filled = new bool[level.SlotCount];
            since = new double[level.SlotCount];
            if (!IsServer) return;
            slots.Clear();
            for (int i = 0; i < level.SlotCount; i++) slots.Add(0.0);
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public int SlotCount => level != null ? Mathf.Min(slots.Count, level.SlotCount) : 0;

        public bool SlotFilled(int slot) => slots[slot] > 0.0;

        public int FilledCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++)
                    if (SlotFilled(i)) count++;
                return count;
            }
        }

        /// <summary>The empty slot in the catcher nearest someone standing here, if one is within reach; otherwise -1.</summary>
        public int ReachableGap(Vector3 feet, float extraReach = 0f)
        {
            int best = -1;
            float bestDistance = config.plankInsertRange + extraReach;
            for (int i = 0; i < SlotCount; i++)
            {
                if (SlotFilled(i)) continue;
                Vector3 to = level.SlotPosition(i) - feet;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance <= bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Someone here could reach the catcher, whether or not it has a gap.</summary>
        public bool CatcherInReach(Vector3 feet)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                Vector3 to = level.SlotPosition(i) - feet;
                to.y = 0f;
                if (to.magnitude <= config.plankInsertRange) return true;
            }
            return false;
        }

        // ---- Host only ----

        /// <summary>A new half: the catcher is empty and the huts are stocked again.</summary>
        public void ServerResetHalf()
        {
            if (level == null) return;
            for (int i = 0; i < slots.Count; i++) slots[i] = 0.0;
            CargoDefinition plank = config.plank;
            if (plank == null || plank.prefab == null) return;
            foreach (Transform spot in level.plankSpots)
            {
                GameObject instance = Instantiate(plank.prefab, spot.position, spot.rotation);
                instance.GetComponent<NetworkObject>().Spawn(true);
            }
        }

        /// <summary>Pressing E with a plank near the catcher pushes it into the nearest gap. False if there's none in reach.</summary>
        public bool ServerTryInsert(PlayerAvatar avatar, CargoItem plank)
        {
            if (level == null || plank == null || !plank.IsPlank) return false;
            // The host sees people a moment late, so it allows a little more reach than the prompt does.
            int slot = ReachableGap(avatar.transform.position, config.reachTolerance);
            if (slot < 0) return false;
            slots[slot] = Now;
            plank.ServerUseUp();
            AutoPilot.Log($"PLANK {avatar.DisplayName} side={avatar.Side} slot={slot} filled={FilledCount}");
            PlankInRpc(slot);
            return true;
        }

        private void Update()
        {
            if (level == null || !IsSpawned) return;
            if (IsServer) WashOutOldPlanks();
            ShowCatcher();
        }

        private void WashOutOldPlanks()
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] <= 0.0 || Now - slots[i] < config.plankSeconds) continue;
                slots[i] = 0.0;
                AutoPilot.Log($"PLANK slot={i} washed away filled={FilledCount}");
            }
        }

        private void ShowCatcher()
        {
            int count = SlotCount;
            if (count == 0 || level.catcher == null) return;
            for (int i = 0; i < count; i++)
            {
                filled[i] = slots[i] > 0.0;
                since[i] = slots[i];
            }
            level.catcher.ApplySnapshot(filled, since, Now, config.plankSeconds);
        }

        private void FixedUpdate()
        {
            if (!IsServer || level == null || !IsSpawned) return;
            MatchDirector director = MatchDirector.Instance;
            bool flowing = director != null && director.Phase.Value == MatchPhase.Playing;
            float deltaTime = Time.fixedDeltaTime;

            sweep.Clear();
            sweep.AddRange(CargoItem.All);
            foreach (CargoItem cargo in sweep)
            {
                if (cargo == null || !cargo.IsSpawned || cargo.State.Value != CargoState.Ground) continue;
                Rigidbody body = cargo.Body;
                if (body == null || body.isKinematic) continue;
                Vector3 position = body.position;
                if (level.OffField(position))
                {
                    if (cargo.IsPlank) cargo.ServerUseUp();
                    else cargo.ServerLose();
                    continue;
                }
                // A vacuum pulling it out of the water wins.
                if (Time.time - cargo.LastPulledTime < 0.25f) continue;
                Float(cargo, body, position, flowing, deltaTime);
            }
        }

        /// <summary>The water holds it up with its bottom a little under the surface, and while the half is on, carries it along.</summary>
        private void Float(CargoItem cargo, Rigidbody body, Vector3 position, bool flowing, float deltaTime)
        {
            if (!level.OverWater(position, out float surface, out Vector3 flow, out Vector3 middle)) return;
            Vector3 half = cargo.HalfExtents;
            float depth = surface - (position.y - half.y);
            if (depth <= 0.02f) return;
            float submerged = Mathf.Clamp01(depth / (half.y * 2f));

            float draft = Mathf.Min(half.y * 0.8f, 0.12f);
            float restHeight = surface - draft + half.y;
            Vector3 velocity = body.linearVelocity;
            velocity.y -= Physics.gravity.y * deltaTime * Mathf.Clamp01(submerged * 1.5f);
            velocity.y = Mathf.MoveTowards(velocity.y, Mathf.Clamp((restHeight - position.y) * 3f, -1.5f, 1.2f), 6f * deltaTime);

            // The current, easing it off the banks toward the middle so it doesn't stick.
            Vector3 want = Vector3.zero;
            if (flowing)
            {
                Vector3 toMiddle = middle - position;
                toMiddle.y = 0f;
                want = flow * (level.flowPath.suggestedCargoSpeed * config.riverFlowScale) + Vector3.ClampMagnitude(toMiddle, 1f) * 0.12f;
            }
            Vector3 flat = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), want,
                config.riverFlowAcceleration * Mathf.Max(0.35f, submerged) * deltaTime);
            body.linearVelocity = new Vector3(flat.x, velocity.y, flat.z);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.zero, 4f * deltaTime);

            // Floating boxes right themselves.
            Quaternion upright = Quaternion.FromToRotation(body.rotation * Vector3.up, Vector3.up) * body.rotation;
            body.MoveRotation(Quaternion.Slerp(body.rotation, upright, 2f * deltaTime));
        }

        // ---- Everyone ----

        [Rpc(SendTo.Everyone)]
        private void PlankInRpc(int slot)
        {
            if (level == null || slot < 0 || slot >= level.SlotCount) return;
            Vector3 at = level.SlotPosition(slot);
            GameAudio.PlayAt("clunk", at, 0.9f);
            FxBurst.PlayPuff(at + Vector3.up * 0.4f, new Color(0.78f, 0.86f, 0.9f), 4);
        }
    }
}
