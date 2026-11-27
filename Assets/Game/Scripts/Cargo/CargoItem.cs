using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// A piece of cargo. The host owns its state and physics. Single-carrier cargo follows its
    /// carrier locally on every computer, so it never trails behind the person holding it: in the
    /// hands on the carrier's own computer, and on their chest on everyone else's.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CargoItem : NetworkBehaviour
    {
        public static readonly List<CargoItem> All = new List<CargoItem>();

        [SerializeField] private CargoDefinition definition;
        [SerializeField] private Transform visual;
        [SerializeField] private Vector3 halfExtents = new Vector3(0.3f, 0.3f, 0.3f);
        [Tooltip("The body when whole, then as it gets more damaged.")]
        [SerializeField] private GameObject[] damageBodies;
        [Tooltip("For each of those: its lid's hinge, which opens for the inspect.")]
        [SerializeField] private Transform[] lids;
        [Tooltip("For each of those: the thing the hands take out during the inspect, hidden while it's out.")]
        [SerializeField] private GameObject[] slots;

        public readonly NetworkVariable<CargoState> State = new NetworkVariable<CargoState>(CargoState.Ground);
        public readonly NetworkVariable<float> Durability = new NetworkVariable<float>(1f);
        public readonly NetworkVariable<ulong> CarrierA = new NetworkVariable<ulong>(NetIds.None);
        public readonly NetworkVariable<ulong> CarrierB = new NetworkVariable<ulong>(NetIds.None);

        public CargoDefinition Definition => definition;
        /// <summary>A plank from the river level's huts: carried like cargo, but it never scores.</summary>
        public bool IsPlank => definition.kind == CargoKind.Plank;
        /// <summary>Rescued, smashed or carried off by the river: it's out of play.</summary>
        public bool IsResolved => State.Value == CargoState.Rescued || State.Value == CargoState.Destroyed || State.Value == CargoState.Lost;
        /// <summary>Host only: what the river pushes along.</summary>
        public Rigidbody Body => body;
        public float Radius => Mathf.Max(halfExtents.x, halfExtents.z);
        public float Height => halfExtents.y * 2f;
        public Vector3 HalfExtents => halfExtents;

        /// <summary>The height of its lowest corner, however it lies.</summary>
        public float BottomHeight
        {
            get
            {
                Vector3 up = transform.InverseTransformDirection(Vector3.up);
                return transform.position.y - (Mathf.Abs(up.x) * halfExtents.x + Mathf.Abs(up.y) * halfExtents.y
                    + Mathf.Abs(up.z) * halfExtents.z);
            }
        }

        /// <summary>From 0 when just picked up to 1 once it has come up into the carrier's hands.</summary>
        public float CarryBlend { get; private set; }

        /// <summary>Where it was picked up from, in the world.</summary>
        public Vector3 CarryFrom { get; private set; }

        public float LastHurtTime { get; private set; } = -100f;
        /// <summary>Host only: when a vacuum last pulled it. The river leaves it to the vacuum for a moment after.</summary>
        public float LastPulledTime { get; private set; } = -100f;
        public bool HasFreeSlot => definition.requiredCarriers > 1 && !IsResolved &&
            (CarrierA.Value == NetIds.None || CarrierB.Value == NetIds.None);

        private readonly Dictionary<PlayerAvatar, float> recentSmashers = new Dictionary<PlayerAvatar, float>();
        private GameConfig config;
        private Rigidbody body;
        private Collider[] colliders;
        private float shakeUntil;
        private float squash;
        private float hop;
        private Vector3 visualRest;
        private Vector3 lastHitPush;
        private int damageStage;
        private Renderer[] modelRenderers;
        private bool onViewLayer;
        private bool wasHeld;
        private float heldSince;
        private Quaternion carryFromRotation;
        private float flashUntil;
        private bool flashing;
        private MaterialPropertyBlock flashBlock;
        private Quaternion[] lidRest;
        private bool inspectShown;
        private int inspectStage;
        private bool followingMarker;
        private float markerSince;
        private Vector3 markerFrom;
        private Quaternion markerFromRotation;
        private bool heldStill;
        /// <summary>Where the case's own origin sits in this object, for lining it up with a clip's marker.</summary>
        private Vector3 frameOffset;
        private Quaternion frameTurn = Quaternion.identity;

        private const float MarkerBlendSeconds = 0.12f;

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
                if (cargo.State.Value != CargoState.Ground || cargo.IsPlank) continue;
                float distance = cargo.EdgeDistance(position);
                if (distance <= range && distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>
        /// The cargo someone is aiming at from their eyes, among what their feet are close enough to reach.
        /// It's mostly about facing it: people don't look at their feet when they're running, so up and
        /// down only decide between two things that are both in front of you. `toSmash` looks only at cargo
        /// on the ground that a hammer can break, so not at planks.
        /// </summary>
        public static CargoItem FindAimed(Vector3 eye, float yaw, float pitch, Vector3 feet, float range, bool toSmash)
        {
            float allowance = GameConfig.Get().aimDegrees;
            CargoItem best = null;
            float bestScore = float.MaxValue;
            foreach (CargoItem cargo in All)
            {
                CargoState state = cargo.State.Value;
                bool usable = toSmash
                    ? state == CargoState.Ground && !cargo.IsPlank
                    : state == CargoState.Ground || (state == CargoState.Carried && cargo.HasFreeSlot);
                if (!usable || cargo.EdgeDistance(feet) > range) continue;

                Vector3 to = cargo.transform.position - eye;
                float flat = new Vector2(to.x, to.z).magnitude;
                float size = Mathf.Atan2(cargo.Radius, Mathf.Max(flat, 0.1f)) * Mathf.Rad2Deg;
                float sideways = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg));
                if (sideways - size > allowance) continue;

                float below = Mathf.Atan2(-to.y, flat) * Mathf.Rad2Deg;
                float score = sideways + Mathf.Abs(Mathf.DeltaAngle(pitch, below)) * 0.35f;
                if (score < bestScore)
                {
                    best = cargo;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>The cargo nearest the middle of the view that a vacuum here would pull in, for the prompt.</summary>
        public static CargoItem FindSuckable(Vector3 feet, Vector3 forward)
        {
            CargoItem best = null;
            float bestDistance = float.MaxValue;
            foreach (CargoItem cargo in All)
            {
                if (!cargo.CanBeSuckedFrom(feet, forward)) continue;
                float distance = cargo.EdgeDistance(feet);
                if (distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>A vacuum pulls what's on the ground in front of it. Large cargo is too heavy.</summary>
        public bool CanBeSuckedFrom(Vector3 feet, Vector3 forward)
        {
            if (State.Value != CargoState.Ground || definition.requiredCarriers > 1) return false;
            Vector3 to = transform.position - feet;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance - Radius > config.suckRange) return false;
            if (distance < 0.01f) return true;
            float size = Mathf.Atan2(Radius, distance) * Mathf.Rad2Deg;
            return Vector3.Angle(forward, to) - size <= config.suckAimDegrees;
        }

        public bool IsWithin(Vector3 position, float range) => EdgeDistance(position) <= range;

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
            modelRenderers = GetComponentsInChildren<Renderer>(true);
            flashBlock = new MaterialPropertyBlock();
            flashBlock.SetColor("_BaseColor", Color.white);
            if (visual != null) visualRest = visual.localPosition;
            if (lids != null)
            {
                lidRest = new Quaternion[lids.Length];
                for (int i = 0; i < lids.Length; i++) lidRest[i] = lids[i] != null ? lids[i].localRotation : Quaternion.identity;
            }
            if (damageBodies != null && damageBodies.Length > 0)
            {
                Transform frame = damageBodies[0].transform;
                frameOffset = transform.InverseTransformPoint(frame.position);
                frameTurn = Quaternion.Inverse(transform.rotation) * frame.rotation;
            }
        }

        // ---- Looking inside (F) ----

        /// <summary>The way the crate's front faces, flat on the ground: its lid hinges at the back.</summary>
        public Vector3 FrontDirection
        {
            get
            {
                if (lids == null || lids.Length == 0 || lids[damageStage] == null) return transform.forward;
                Transform frame = damageBodies[damageStage].transform;
                Vector3 back = lids[damageStage].position - frame.position;
                back.y = 0f;
                return back.sqrMagnitude > 0.0001f ? -back.normalized : transform.forward;
            }
        }

        public bool IsUpright => Vector3.Angle(transform.up, Vector3.up) <= config.inspectMaxTilt;

        /// <summary>Someone looking at this, with their clip under way (or, with `stepping`, still stepping up to it).</summary>
        public PlayerAvatar Inspector(bool stepping = false)
        {
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                if (!avatar.IsInspecting || avatar.InspectTarget.Value != NetworkObjectId) continue;
                float time = avatar.InspectTime;
                if (time < definition.inspect.seconds && (stepping || time >= 0f)) return avatar;
            }
            return null;
        }

        /// <summary>The goose crate can be opened where it stands: on its own, upright, and within reach.</summary>
        public bool CanBeOpenedHere(Vector3 feet)
        {
            return definition.inspectOnGround && definition.Inspectable && State.Value == CargoState.Ground && IsUpright &&
                Inspector(true) == null && IsWithin(feet, config.pickupRange + config.reachTolerance);
        }

        /// <summary>This object's pose that puts the case's own origin on a clip's marker.</summary>
        private void PoseOnMarker(Vector3 markerPosition, Quaternion markerRotation, out Vector3 position, out Quaternion rotation)
        {
            rotation = markerRotation * Quaternion.Inverse(frameTurn);
            position = markerPosition - rotation * frameOffset;
        }

        /// <summary>The lid follows the inspector's clip, and what they took out isn't in the case while they hold it.</summary>
        private void ShowInspect(PlayerAvatar inspector)
        {
            bool show = inspector != null && lids != null && inspector.Visual != null && inspector.Visual.HasCargoMarkers;
            if (!show)
            {
                if (!inspectShown) return;
                inspectShown = false;
                RestoreInspected(inspectStage);
                return;
            }
            if (inspectShown && inspectStage != damageStage) RestoreInspected(inspectStage);
            inspectShown = true;
            inspectStage = damageStage;
            Transform lid = lids[damageStage];
            if (lid != null) lid.rotation = lid.parent.rotation * inspector.Visual.LidTurn;
            GameObject slot = slots != null && damageStage < slots.Length ? slots[damageStage] : null;
            bool inHands = definition.inspect.ItemOut(inspector.InspectTime);
            if (slot != null && slot.activeSelf == inHands) slot.SetActive(!inHands);
        }

        private void RestoreInspected(int stage)
        {
            if (lids != null && stage < lids.Length && lids[stage] != null) lids[stage].localRotation = lidRest[stage];
            if (slots != null && stage < slots.Length && slots[stage] != null && !slots[stage].activeSelf) slots[stage].SetActive(true);
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
            if (current == CargoState.Destroyed) ShowBreaking();
            if (current == CargoState.Lost) ShowSwept();
            if (current == CargoState.Carried && previous == CargoState.Ground) GameAudio.PlayAt("pickup", transform.position);
            if (current == CargoState.Ground && previous == CargoState.Carried) GameAudio.PlayAt("drop", transform.position);
        }

        private void ShowBreaking()
        {
            if (visual != null) visual.gameObject.SetActive(false);
            Vector3 middle = transform.position;
            CargoPieces.Burst(definition.piecesPrefab, middle, transform.rotation, lastHitPush);
            FxBurst.Play(middle, definition.chipColor, 14);
            FxBurst.PlayPuff(middle, new Color(0.86f, 0.8f, 0.68f), 6 + (int)(Radius * 8f));
            FxBurst.PlayFlash(middle + Vector3.up * 0.2f, new Color(1f, 0.95f, 0.65f), 1.2f + Radius * 1.4f);
            GameAudio.PlayAt(definition.breakSound, middle);
            FollowCamera.Shake(middle, 0.65f);
        }

        /// <summary>The river takes it off the field: a splash, and it's gone.</summary>
        private void ShowSwept()
        {
            if (visual != null) visual.gameObject.SetActive(false);
            Vector3 middle = transform.position;
            FxBurst.PlayPuff(middle, new Color(0.78f, 0.86f, 0.9f), 8);
            GameAudio.PlayAt("whoosh", middle, 0.6f);
        }

        private void HandleDurabilityChanged(float previous, float current)
        {
            if (current < previous)
            {
                shakeUntil = Time.time + 0.12f;
                LastHurtTime = Time.time;
            }

            int stage = current > config.damagedBelow ? 0 : current > config.wreckedBelow ? 1 : 2;
            if (stage != damageStage && damageBodies != null && stage < damageBodies.Length)
            {
                damageBodies[damageStage].SetActive(false);
                damageBodies[stage].SetActive(true);
                damageStage = stage;
                // Only here, not on every hit: at arm's length, a flash twice a second is hard on the eyes.
                flashUntil = Time.time + 0.07f;
                FxBurst.Play(transform.position + Vector3.up * halfExtents.y, definition.chipColor, 12);
                FxBurst.PlayPuff(transform.position + Vector3.up * halfExtents.y, new Color(0.86f, 0.8f, 0.68f), 3);
                GameAudio.PlayAt("crack", transform.position, 0.75f);
            }
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) TickServer(Time.deltaTime);

            if (visual != null)
            {
                hop = Mathf.MoveTowards(hop, 0f, 4.5f * Time.deltaTime);
                Vector3 lifted = visualRest + Vector3.up * (Mathf.Sin(hop * Mathf.PI) * 0.1f);
                visual.localPosition = Time.time < shakeUntil
                    ? lifted + Random.insideUnitSphere * 0.06f
                    : lifted;

                squash = Mathf.MoveTowards(squash, 0f, 4f * Time.deltaTime);
                float wobble = Mathf.Sin(squash * 9f) * squash * 0.18f;
                visual.localScale = new Vector3(1f + wobble, 1f - wobble, 1f + wobble);
            }
            UpdateFlash();
        }

        private void UpdateFlash()
        {
            bool flash = Time.time < flashUntil;
            if (flash == flashing) return;
            flashing = flash;
            foreach (Renderer part in modelRenderers) part.SetPropertyBlock(flash ? flashBlock : null);
        }

        private void LateUpdate()
        {
            if (!IsSpawned) return;
            PlayerAvatar inspector = Inspector();
            ShowInspect(inspector);

            bool held = State.Value == CargoState.Carried && definition.requiredCarriers == 1;
            PlayerAvatar carrier = held ? FindAvatar(CarrierA.Value) : null;
            if (carrier == null)
            {
                wasHeld = false;
                followingMarker = false;
                SetOnViewLayer(false);
                return;
            }
            if (!wasHeld)
            {
                wasHeld = true;
                heldSince = Time.time;
                CarryFrom = transform.position;
                carryFromRotation = transform.rotation;
            }

            // Looking inside: the case goes where the carrier's clip puts it, down onto the ground and back up.
            if (inspector == carrier && carrier.Visual != null && carrier.Visual.CaseMarker(out Vector3 markerPosition, out Quaternion markerRotation))
            {
                if (!followingMarker)
                {
                    followingMarker = true;
                    markerSince = Time.time;
                    markerFrom = transform.position;
                    markerFromRotation = transform.rotation;
                }
                SetOnViewLayer(false);
                PoseOnMarker(markerPosition, markerRotation, out Vector3 onMarker, out Quaternion onMarkerRotation);
                float k = Mathf.SmoothStep(0f, 1f, (Time.time - markerSince) / MarkerBlendSeconds);
                transform.SetPositionAndRotation(Vector3.Lerp(markerFrom, onMarker, k), Quaternion.Slerp(markerFromRotation, onMarkerRotation, k));
                CarryBlend = 1f;
                return;
            }
            if (followingMarker)
            {
                // Back from the show: into the hands from wherever the clip left it.
                followingMarker = false;
                heldSince = Time.time;
                CarryFrom = transform.position;
                carryFromRotation = transform.rotation;
            }

            Vector3 position;
            Quaternion rotation;
            FirstPersonRig hands = FirstPersonRig.Instance;
            bool inHands = hands != null && hands.HoldsCargoOf(carrier);
            SetOnViewLayer(inHands);
            float pickUpSeconds = 0.2f;
            if (inHands)
            {
                hands.HoldPose(this, out position, out rotation);
                pickUpSeconds = hands.PickUpSeconds;
            }
            else if (IsPlank)
            {
                // Across the body on both palms, for anyone watching.
                Transform body = carrier.transform;
                position = carrier.Visual.HandsMiddle + body.up * (halfExtents.y + 0.04f) + body.forward * 0.08f;
                rotation = body.rotation * Quaternion.Euler(0f, 90f, 0f);
            }
            else if (carrier.Visual.CaseMarker(out Vector3 carryPosition, out Quaternion carryRotation))
            {
                // Astra's cases: the carry clip puts the case between the hands.
                PoseOnMarker(carryPosition, carryRotation, out position, out rotation);
            }
            else
            {
                float wobble = Mathf.Sin(Time.time * 7f) * 2.5f;
                position = carrier.Visual.CarryPoint(halfExtents.z);
                rotation = carrier.transform.rotation * Quaternion.Euler(wobble * 0.5f, 0f, wobble);
            }

            float t = Mathf.Clamp01((Time.time - heldSince) / pickUpSeconds);
            // Out of the ground fast, then settling in, with a slight overshoot.
            CarryBlend = 1f + (t - 1f) * (t - 1f) * (2.2f * (t - 1f) + 1.2f);
            transform.SetPositionAndRotation(
                Vector3.LerpUnclamped(CarryFrom, position, CarryBlend),
                Quaternion.SlerpUnclamped(carryFromRotation, rotation, Mathf.Clamp01(CarryBlend)));
        }

        /// <summary>Held cargo gets drawn over the world with the hands, so it never pokes into a wall.</summary>
        private void SetOnViewLayer(bool onView)
        {
            if (onView == onViewLayer) return;
            onViewLayer = onView;
            int layer = onView ? GameLayers.ViewModel : GameLayers.Cargo;
            foreach (Renderer part in modelRenderers) part.gameObject.layer = layer;
        }

        /// <summary>
        /// Moves only the model, so the replicated position stays where the host put it. The
        /// helicopter uses this to carry rescued cargo away inside the box.
        /// </summary>
        public void ShowCarriedAway(Vector3 pivotFrom, Vector3 pivotTo, Quaternion turn)
        {
            if (visual == null) return;
            Vector3 rest = transform.TransformPoint(visualRest);
            visual.SetPositionAndRotation(pivotTo + turn * (rest - pivotFrom), turn * transform.rotation);
        }

        /// <summary>A squash and hop when the hammer lands. Only for show.</summary>
        public void Bump(Vector3 push)
        {
            squash = 1f;
            hop = 1f;
            lastHitPush = push;
        }

        public void HideModel()
        {
            if (visual != null) visual.gameObject.SetActive(false);
        }

        /// <summary>Where a co-carrier stands: one on each side of the item.</summary>
        public Vector3 GrabPointWorld(PlayerAvatar avatar)
        {
            float sign = CarrierB.Value == avatar.NetworkObjectId ? 1f : -1f;
            float reach = definition.coCarryReach > 0f ? definition.coCarryReach : halfExtents.x + 0.55f;
            return transform.position + Vector3.right * (sign * reach);
        }

        // ---- Host only ----

        private void TickServer(float deltaTime)
        {
            // The crate stays where it is while someone opens it, so their hands meet its lid.
            bool still = State.Value == CargoState.Ground && definition.inspectOnGround && Inspector(true) != null;
            if (still != heldStill)
            {
                heldStill = still;
                if (State.Value == CargoState.Ground) body.isKinematic = still;
            }
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
            // A carrier stepped off the airport's upper deck, and both let go.
            if (State.Value != CargoState.Carried) return;

            MatchDirector director = MatchDirector.Instance;
            if (director != null && director.Phase.Value == MatchPhase.Playing) CheckDeposit(a, b);
        }

        /// <summary>
        /// The two carriers walk it along the ground: it keeps its carrying height over slopes and banks, stops at
        /// walls and rocks, and stops where either carrier couldn't walk, like deep water.
        /// </summary>
        private void MoveWithCarriers(PlayerAvatar a, PlayerAvatar b, float deltaTime)
        {
            AirportFacility airport = AirportFacility.Instance;
            if (airport != null)
            {
                MoveOnDecks(a, b, deltaTime, airport);
                return;
            }

            Vector2 average = (a.ServerCoCarryInput + b.ServerCoCarryInput) * 0.5f;
            if (average.sqrMagnitude < 0.0025f) return;

            float speed = config.runSpeed * definition.carrySpeedMultiplier;
            RiverLevel river = RiverLevel.Instance;
            if (river != null) speed *= river.WadeMultiplier(transform.position);
            Vector3 next = transform.position + new Vector3(average.x, 0f, average.y) * (speed * deltaTime);
            float lift = definition.coCarryHeight > 0f ? definition.coCarryHeight : halfExtents.y + 0.02f;
            if (GroundBelow(next, out float ground)) next.y = Mathf.MoveTowards(transform.position.y, ground + lift, 4f * deltaTime);

            if (Physics.CheckBox(next + Vector3.up * 0.06f, halfExtents * 0.9f, Quaternion.identity, GameLayers.LevelMask, QueryTriggerInteraction.Ignore)) return;
            Vector3 offset = next - transform.position;
            foreach (PlayerAvatar carrier in new[] { a, b })
            {
                Vector3 feet = GrabPointWorld(carrier) + offset;
                if (Physics.CheckSphere(feet + Vector3.up * 0.9f, 0.3f, 1 << GameLayers.PlayerBlock, QueryTriggerInteraction.Ignore)) return;
            }
            transform.position = next;
        }

        /// <summary>
        /// On the airport, floors stand over floors: the crate keeps its carrying height over the highest floor under its
        /// middle and both carriers, never goes where any of the three would step off into the air (the deck's edge, the
        /// shaft over B03), and rides the belt with its carriers. If a carrier ends up on the upper deck with nothing
        /// underfoot anyway, both let go.
        /// </summary>
        private void MoveOnDecks(PlayerAvatar a, PlayerAvatar b, float deltaTime, AirportFacility airport)
        {
            if (airport.LostFooting(a.transform.position) || airport.LostFooting(b.transform.position))
            {
                ServerDropAll(Vector3.zero);
                return;
            }

            Vector2 average = (a.ServerCoCarryInput + b.ServerCoCarryInput) * 0.5f;
            Vector3 velocity = average.sqrMagnitude < 0.0025f ? Vector3.zero
                : new Vector3(average.x, 0f, average.y) * (config.runSpeed * definition.carrySpeedMultiplier);
            if (AirportFacility.BeltRunning)
                velocity += (airport.BeltVelocityAt(a.transform.position) + airport.BeltVelocityAt(b.transform.position)) * 0.5f;
            if (velocity.sqrMagnitude < 0.0001f) return;

            Vector3 offset = velocity * deltaTime;
            float lift = definition.coCarryHeight > 0f ? definition.coCarryHeight : halfExtents.y + 0.02f;
            float floorNow = transform.position.y - lift;
            Vector3 middle = transform.position + offset;
            middle.y = floorNow;
            Vector3 aFeet = GrabPointWorld(a) + offset;
            Vector3 bFeet = GrabPointWorld(b) + offset;
            aFeet.y = bFeet.y = floorNow;
            if (!airport.TryCarryFloor(middle, out RaycastHit under) || !airport.TryCarryFloor(aFeet, out RaycastHit underA)
                || !airport.TryCarryFloor(bFeet, out RaycastHit underB)) return;

            float floor = Mathf.Max(under.point.y, underA.point.y, underB.point.y);
            Vector3 next = transform.position + offset;
            next.y = Mathf.MoveTowards(transform.position.y, floor + lift, 6f * deltaTime);
            if (Physics.CheckBox(next + Vector3.up * 0.06f, halfExtents * 0.9f, Quaternion.identity, GameLayers.LevelMask, QueryTriggerInteraction.Ignore)) return;
            transform.position = next;
        }

        /// <summary>B03 carries loose cargo along with it (host only).</summary>
        public void ServerConvey(Vector3 direction, float speed, float velocityChange)
        {
            if (!IsServer || State.Value != CargoState.Ground || body.isKinematic) return;
            Vector3 velocity = body.linearVelocity;
            float along = Vector3.Dot(velocity, direction);
            body.linearVelocity = velocity + direction * (Mathf.MoveTowards(along, speed, velocityChange) - along);
        }

        /// <summary>The level's surface under a point, looking down from a little above it.</summary>
        public static bool GroundBelow(Vector3 point, out float height)
        {
            bool found = Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 6f, GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            height = found ? hit.point.y : point.y;
            return found;
        }

        private void CheckDeposit(PlayerAvatar a, PlayerAvatar b)
        {
            if (IsPlank) return;
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
            StartCoroutine(FlyToBed(layout.NextBedPoint(), layout.BedRotation));
        }

        /// <summary>It lands square with the bed, so it sits inside it however the truck came to rest.</summary>
        private IEnumerator FlyToBed(Vector3 target, Quaternion landing)
        {
            SetPhysicsActive(false);
            Vector3 start = transform.position;
            Quaternion startRotation = transform.rotation;
            const float duration = 0.55f;
            for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
            {
                Vector3 position = Vector3.Lerp(start, target, t);
                position.y += Mathf.Sin(t * Mathf.PI) * 3f;
                transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, landing, t));
                yield return null;
            }
            transform.SetPositionAndRotation(target, landing);
        }

        /// <summary>The river carried it off the field (host only). Nobody scores it.</summary>
        public void ServerLose()
        {
            if (IsResolved) return;
            ServerClearCarriers();
            State.Value = CargoState.Lost;
            SetPhysicsActive(false);
            MatchDirector director = MatchDirector.Instance;
            if (director != null) director.ServerOnLost(this);
            StartCoroutine(DespawnSoon());
        }

        /// <summary>A plank pushed into the catcher (host only): the board in the slot takes its place.</summary>
        public void ServerUseUp()
        {
            ServerClearCarriers();
            SetPhysicsActive(false);
            if (IsSpawned) NetworkObject.Despawn(true);
        }

        public bool ServerTryGrab(PlayerAvatar avatar)
        {
            ulong id = avatar.NetworkObjectId;
            if (CarrierA.Value == id || CarrierB.Value == id) return false;
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
                    // Stand it upright so both carriers have a clear side to hold, lifted by its handles.
                    Vector3 position = transform.position;
                    float ground = Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit floor, 4f,
                        GameLayers.LevelMask, QueryTriggerInteraction.Ignore) ? floor.point.y : position.y - halfExtents.y;
                    position.y = ground + (definition.coCarryHeight > 0f ? definition.coCarryHeight : halfExtents.y + 0.02f);
                    transform.SetPositionAndRotation(position, Quaternion.identity);
                }
            }
            avatar.ServerSetCarry(this);
            if (AutoPilot.Active) AutoPilot.Log($"PICKUP {definition.kind} by={avatar.DisplayName} side={avatar.Side} at=({transform.position.x:0},{transform.position.y:0},{transform.position.z:0})");
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
            {
                ServerPlaceClearOfWalls(avatar);
                ServerBecomeGround(avatar.transform.forward * 2.5f + Vector3.up * 2f);
            }
        }

        /// <summary>
        /// Held cargo sits in front of its carrier and can overlap a wall they're standing at. Before
        /// physics takes over, it moves back along the line from the carrier until it fits.
        /// </summary>
        private void ServerPlaceClearOfWalls(PlayerAvatar holder)
        {
            if (holder == null || definition.requiredCarriers > 1) return;
            Vector3 from = holder.transform.position + Vector3.up * (halfExtents.y + 0.1f);
            Vector3 to = transform.position;
            to.y = Mathf.Max(to.y, from.y);
            Vector3 path = to - from;
            float distance = path.magnitude;
            if (distance > 0.001f && Physics.BoxCast(from, halfExtents * 0.9f, path / distance, out RaycastHit hit,
                Quaternion.identity, distance, GameLayers.LevelMask, QueryTriggerInteraction.Ignore))
            {
                to = from + path / distance * Mathf.Max(0f, hit.distance - 0.02f);
            }
            transform.position = to;
            body.position = to;
        }

        /// <summary>A carrier was knocked down: everyone loses their grip and the cargo flies out.</summary>
        public void ServerDropAll(Vector3 direction)
        {
            if (State.Value != CargoState.Carried) return;
            ServerPlaceClearOfWalls(FindAvatar(CarrierA.Value));
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

        /// <summary>Drags it through the air toward a vacuum. Called every frame someone's sucking on it.</summary>
        public void ServerPullToward(Vector3 point, float speed, float deltaTime)
        {
            if (State.Value != CargoState.Ground) return;
            LastPulledTime = Time.time;
            Vector3 wanted = (point - transform.position).normalized * speed;
            body.linearVelocity = Vector3.MoveTowards(body.linearVelocity, wanted, 45f * deltaTime);
            body.angularVelocity = Vector3.MoveTowards(body.angularVelocity, Vector3.up * 5f, 20f * deltaTime);
        }

        public void ServerApplySmash(PlayerAvatar smasher, float deltaTime)
        {
            if (State.Value != CargoState.Ground || IsPlank) return;
            recentSmashers[smasher] = Time.time;
            float next = Durability.Value - deltaTime / (definition.smashSeconds * config.EffectiveSmashMultiplier);
            Durability.Value = Mathf.Max(0f, next);
            if (next > 0f) return;

            List<PlayerAvatar> credit = new List<PlayerAvatar>();
            foreach (KeyValuePair<PlayerAvatar, float> pair in recentSmashers)
            {
                if (pair.Key != null && Time.time - pair.Value < config.smashSwingSeconds * 1.25f) credit.Add(pair.Key);
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
