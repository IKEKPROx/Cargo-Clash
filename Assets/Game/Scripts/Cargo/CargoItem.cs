using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// A piece of cargo. The host owns its state and physics. Single-carrier cargo follows its
    /// carrier locally on every computer, so it never trails behind the person holding it.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CargoItem : NetworkBehaviour
    {
        public static readonly List<CargoItem> All = new List<CargoItem>();

        [SerializeField] private CargoDefinition definition;
        [SerializeField] private Transform visual;
        [SerializeField] private Vector3 halfExtents = new Vector3(0.3f, 0.3f, 0.3f);

        public readonly NetworkVariable<CargoState> State = new NetworkVariable<CargoState>(CargoState.Ground);
        public readonly NetworkVariable<float> Durability = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<ulong> CarrierA = new NetworkVariable<ulong>(NetIds.None);
        public readonly NetworkVariable<ulong> CarrierB = new NetworkVariable<ulong>(NetIds.None);

        public CargoDefinition Definition => definition;
        public float Radius => Mathf.Max(halfExtents.x, halfExtents.z);
        public float Height => halfExtents.y * 2f;
        public bool HasFreeSlot => definition.requiredCarriers > 1 && State.Value != CargoState.Rescued &&
            State.Value != CargoState.Destroyed && (CarrierA.Value == NetIds.None || CarrierB.Value == NetIds.None);

        private readonly Dictionary<PlayerAvatar, float> recentSmashers = new Dictionary<PlayerAvatar, float>();
        private GameConfig config;
        private Rigidbody body;
        private Collider[] colliders;
        private float shakeUntil;
        private Vector3 visualRest;

        public static CargoItem Find(ulong networkObjectId)
        {
            if (networkObjectId == NetIds.None) return null;
            foreach (CargoItem cargo in All)
                if (cargo.NetworkObjectId == networkObjectId) return cargo;
            return null;
        }

        /// <summary>Nearest cargo that a person standing here could take hold of.</summary>
        public static CargoItem FindGrabbable(Vector3 position, float range)
        {
            CargoItem best = null;
            float bestDistance = float.MaxValue;
            foreach (CargoItem cargo in All)
            {
                bool grabbable = cargo.State.Value == CargoState.Ground ||
                    (cargo.State.Value == CargoState.Carried && cargo.HasFreeSlot);
                if (!grabbable) continue;
                float distance = cargo.EdgeDistance(position);
                if (distance <= range && distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Nearest cargo on the ground. Held cargo can't be smashed.</summary>
        public static CargoItem FindSmashable(Vector3 position, float range)
        {
            CargoItem best = null;
            float bestDistance = float.MaxValue;
            foreach (CargoItem cargo in All)
            {
                if (cargo.State.Value != CargoState.Ground) continue;
                float distance = cargo.EdgeDistance(position);
                if (distance <= range && distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public float EdgeDistance(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return Mathf.Max(0f, offset.magnitude - Radius);
        }

        private void Awake()
        {
            config = GameConfig.Get();
            body = GetComponent<Rigidbody>();
            colliders = GetComponentsInChildren<Collider>();
            if (visual != null) visualRest = visual.localPosition;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            State.OnValueChanged += HandleStateChanged;
            Durability.OnValueChanged += HandleDurabilityChanged;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            State.OnValueChanged -= HandleStateChanged;
            Durability.OnValueChanged -= HandleDurabilityChanged;
        }

        private void HandleStateChanged(CargoState previous, CargoState current)
        {
            if (current == CargoState.Destroyed && visual != null) visual.gameObject.SetActive(false);
            if (current == CargoState.Carried && previous == CargoState.Ground) GameAudio.Play("pickup");
            if (current == CargoState.Ground && previous == CargoState.Carried) GameAudio.Play("drop");
        }

        private void HandleDurabilityChanged(float previous, float current)
        {
            if (current < previous)
            {
                shakeUntil = Time.time + 0.12f;
                GameAudio.PlayThrottled("smash", 0.16f);
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) TickServer(Time.deltaTime);

            if (visual != null)
            {
                visual.localPosition = Time.time < shakeUntil
                    ? visualRest + Random.insideUnitSphere * 0.06f
                    : visualRest;
            }
        }

        private void LateUpdate()
        {
            if (!IsSpawned || State.Value != CargoState.Carried || definition.requiredCarriers > 1) return;
            PlayerAvatar carrier = FindAvatar(CarrierA.Value);
            if (carrier == null) return;

            float wobble = Mathf.Sin(Time.time * 7f) * 6f;
            transform.SetPositionAndRotation(
                carrier.transform.position + Vector3.up * (config.headHeight + halfExtents.y),
                carrier.transform.rotation * Quaternion.Euler(wobble * 0.5f, 0f, wobble));
        }

        /// <summary>Where a co-carrier stands: one on each side of the item.</summary>
        public Vector3 GrabPointWorld(PlayerAvatar avatar)
        {
            float sign = CarrierB.Value == avatar.NetworkObjectId ? 1f : -1f;
            return transform.position + Vector3.right * (sign * (halfExtents.x + 0.55f));
        }

        // ---- Host only ----

        private void TickServer(float deltaTime)
        {
            if (State.Value != CargoState.Carried) return;

            PlayerAvatar a = FindAvatar(CarrierA.Value);
            PlayerAvatar b = FindAvatar(CarrierB.Value);
            if (a == null) CarrierA.Value = NetIds.None;
            if (b == null) CarrierB.Value = NetIds.None;
            if (a == null && b == null)
            {
                ServerBecomeGround(Vector3.zero);
                return;
            }

            if (definition.requiredCarriers > 1 && a != null && b != null) MoveWithCarriers(a, b, deltaTime);

            MatchDirector director = MatchDirector.Instance;
            if (director != null && director.Phase.Value == MatchPhase.Playing) CheckDeposit(a, b);
        }

        private void MoveWithCarriers(PlayerAvatar a, PlayerAvatar b, float deltaTime)
        {
            Vector2 average = (a.ServerCoCarryInput + b.ServerCoCarryInput) * 0.5f;
            if (average.sqrMagnitude < 0.0025f) return;

            Vector3 step = new Vector3(average.x, 0f, average.y) * (config.runSpeed * definition.carrySpeedMultiplier * deltaTime);
            float distance = step.magnitude;
            bool blocked = Physics.BoxCast(transform.position, halfExtents * 0.95f, step / distance, out _,
                Quaternion.identity, distance + 0.05f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            if (!blocked) transform.position += step;
        }

        private void CheckDeposit(PlayerAvatar a, PlayerAvatar b)
        {
            LevelLayout layout = LevelLayout.Instance;
            bool aDelivers = a != null && a.Side == Side.Collector && layout.InTruckZone(a.transform.position);
            bool bDelivers = b != null && b.Side == Side.Collector && layout.InTruckZone(b.transform.position);
            if (!aDelivers && !bDelivers) return;

            List<PlayerAvatar> credit = new List<PlayerAvatar>();
            if (a != null && a.Side == Side.Collector) credit.Add(a);
            if (b != null && b.Side == Side.Collector) credit.Add(b);

            ServerClearCarriers();
            State.Value = CargoState.Rescued;
            MatchDirector.Instance.ServerOnRescued(this, credit);
            StartCoroutine(FlyToBed(layout.NextBedPoint()));
        }

        private IEnumerator FlyToBed(Vector3 target)
        {
            SetPhysicsActive(false);
            Vector3 start = transform.position;
            Quaternion startRotation = transform.rotation;
            const float duration = 0.55f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                Vector3 position = Vector3.Lerp(start, target, t);
                position.y += Mathf.Sin(t * Mathf.PI) * 3f;
                transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, Quaternion.identity, t));
                yield return null;
            }
            transform.SetPositionAndRotation(target, Quaternion.identity);
        }

        public bool ServerTryGrab(PlayerAvatar avatar)
        {
            ulong id = avatar.NetworkObjectId;
            if (definition.requiredCarriers > 1)
            {
                if (!HasFreeSlot) return false;
                if (CarrierA.Value == NetIds.None) CarrierA.Value = id;
                else CarrierB.Value = id;
            }
            else
            {
                if (State.Value != CargoState.Ground) return false;
                CarrierA.Value = id;
            }

            if (State.Value == CargoState.Ground)
            {
                State.Value = CargoState.Carried;
                SetPhysicsActive(false);
                if (definition.requiredCarriers > 1)
                {
                    // Stand it upright so both carriers have a clear side to hold.
                    Vector3 position = transform.position;
                    position.y = halfExtents.y + 0.02f;
                    transform.SetPositionAndRotation(position, Quaternion.identity);
                }
            }
            avatar.ServerSetCarry(this);
            return true;
        }

        /// <summary>A carrier chose to let go. The other carrier of a large item keeps holding it.</summary>
        public void ServerRelease(PlayerAvatar avatar)
        {
            ulong id = avatar.NetworkObjectId;
            if (CarrierA.Value == id) CarrierA.Value = NetIds.None;
            if (CarrierB.Value == id) CarrierB.Value = NetIds.None;
            avatar.ServerClearCarry();
            if (CarrierA.Value == NetIds.None && CarrierB.Value == NetIds.None)
                ServerBecomeGround(avatar.transform.forward * 2.5f + Vector3.up * 2f);
        }

        /// <summary>A carrier was knocked down: everyone loses their grip and the cargo flies out.</summary>
        public void ServerDropAll(Vector3 direction)
        {
            if (State.Value != CargoState.Carried) return;
            ServerClearCarriers();
            ServerBecomeGround(direction * 4.5f + Vector3.up * 4.5f);
        }

        private void ServerClearCarriers()
        {
            PlayerAvatar a = FindAvatar(CarrierA.Value);
            PlayerAvatar b = FindAvatar(CarrierB.Value);
            if (a != null) a.ServerClearCarry();
            if (b != null) b.ServerClearCarry();
            CarrierA.Value = NetIds.None;
            CarrierB.Value = NetIds.None;
        }

        private void ServerBecomeGround(Vector3 velocity)
        {
            State.Value = CargoState.Ground;
            SetPhysicsActive(true);
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }

        private void SetPhysicsActive(bool active)
        {
            body.isKinematic = !active;
            foreach (Collider part in colliders) part.enabled = active;
        }

        public void ServerApplySmash(PlayerAvatar smasher, float deltaTime)
        {
            if (State.Value != CargoState.Ground) return;
            recentSmashers[smasher] = Time.time;
            float next = Durability.Value - deltaTime / (definition.smashSeconds * config.EffectiveSmashMultiplier);
            Durability.Value = Mathf.Max(0f, next);
            if (next > 0f) return;

            List<PlayerAvatar> credit = new List<PlayerAvatar>();
            foreach (KeyValuePair<PlayerAvatar, float> pair in recentSmashers)
            {
                if (pair.Key != null && Time.time - pair.Value < 0.25f) credit.Add(pair.Key);
            }
            State.Value = CargoState.Destroyed;
            SetPhysicsActive(false);
            MatchDirector.Instance.ServerOnDestroyed(this, credit);
            StartCoroutine(DespawnSoon());
        }

        private IEnumerator DespawnSoon()
        {
            yield return new WaitForSeconds(0.6f);
            if (IsSpawned) NetworkObject.Despawn(true);
        }

        private static PlayerAvatar FindAvatar(ulong id)
        {
            if (id == NetIds.None) return null;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
                if (avatar.NetworkObjectId == id) return avatar;
            return null;
        }
    }
}
