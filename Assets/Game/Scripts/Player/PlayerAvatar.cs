using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// One character. Movement runs on the owner's computer so it responds immediately. Rams,
    /// knockdowns, carrying and smashing are decided by the host and replicated to everyone.
    /// Bots are owned by the host and use the same code through <see cref="BotBrain"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(NetworkTransform))]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        public static readonly List<PlayerAvatar> All = new List<PlayerAvatar>();

        /// <summary>The character this computer's camera and HUD follow.</summary>
        public static PlayerAvatar Local { get; private set; }

        public readonly NetworkVariable<Team> TeamVar = new NetworkVariable<Team>();
        public readonly NetworkVariable<FixedString32Bytes> NameVar = new NetworkVariable<FixedString32Bytes>();
        public readonly NetworkVariable<bool> IsBotVar = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> KnockedUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> ImmuneUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<double> RamReadyAt = new NetworkVariable<double>();
        public readonly NetworkVariable<ulong> CarriedCargoId = new NetworkVariable<ulong>(NetIds.None);
        public readonly NetworkVariable<double> StaggeredUntil = new NetworkVariable<double>();
        public readonly NetworkVariable<bool> Swinging = new NetworkVariable<bool>();
        public readonly NetworkVariable<ulong> SmashTargetId = new NetworkVariable<ulong>(NetIds.None);
        public readonly NetworkVariable<ulong> HitTargetId = new NetworkVariable<ulong>(NetIds.None);
        public readonly NetworkVariable<int> Health = new NetworkVariable<int>(100);
        /// <summary>Out of health: lying there until they come back at their own side's start.</summary>
        public readonly NetworkVariable<bool> KnockedOut = new NetworkVariable<bool>();
        public readonly NetworkVariable<bool> Sucking = new NetworkVariable<bool>();
        /// <summary>When the hammer started charging, or 0 if it isn't.</summary>
        public readonly NetworkVariable<double> ChargingSince = new NetworkVariable<double>();
        public readonly NetworkVariable<double> SlammingUntil = new NetworkVariable<double>();

        public Team Team => TeamVar.Value;
        public string DisplayName => NameVar.Value.ToString();

        public Side Side
        {
            get
            {
                MatchDirector director = MatchDirector.Instance;
                return director != null && director.CollectorTeam.Value == Team ? Side.Collector : Side.Interrupter;
            }
        }

        public double Now => NetworkManager.ServerTime.Time;
        public bool IsKnockedDown => Now < KnockedUntil.Value;
        public bool IsImmune => Now < ImmuneUntil.Value;
        public bool IsStaggered => Now < StaggeredUntil.Value;
        public bool IsCharging => ChargingSince.Value > 0.0;
        public bool IsSlamming => Now < SlammingUntil.Value;

        /// <summary>
        /// Only meaningful on the owner's computer: the tool has been asked for and the host hasn't answered yet,
        /// or it has. On a client the answer takes a round trip, so anything that should react to the click itself uses this.
        /// </summary>
        public bool WantsToUseTool => lastSwing || lastSuck || lastCharge;

        public float Health01 => Mathf.Clamp01((float)Health.Value / config.maxHealth);

        /// <summary>From 0 to 1 once the hammer has charged enough to slam.</summary>
        public float Charge01 => IsCharging ? Mathf.Clamp01((float)((Now - ChargingSince.Value) / config.chargeSeconds)) : 0f;
        public bool RamReady => Now >= RamReadyAt.Value;
        public bool IsDashing => Time.time < dashVisualUntil;
        public CargoItem CarriedCargo => CargoItem.Find(CarriedCargoId.Value);
        public CargoItem SmashTarget => CargoItem.Find(SmashTargetId.Value);
        public PlayerAvatar HitTarget => Find(HitTargetId.Value);
        public CharacterVisual Visual { get; private set; }

        /// <summary>Where the owner is looking. Only meaningful on the owner's computer.</summary>
        public float LookYaw { get; private set; }

        /// <summary>Degrees above (negative) or below (positive) level. Only meaningful on the owner's computer.</summary>
        public float LookPitch { get; private set; }

        /// <summary>How far the view turned this frame, so the hands can lag behind it.</summary>
        public Vector2 LookThisFrame { get; private set; }

        /// <summary>The cargo E would act on right now. Only set on the owner's computer.</summary>
        public CargoItem AimedGrab { get; private set; }

        /// <summary>The cargo the hammer would land on right now. Only set on the owner's computer.</summary>
        public CargoItem AimedSmash { get; private set; }

        /// <summary>The enemy a swing would land on right now. They come before cargo. Only set on the owner's computer.</summary>
        public PlayerAvatar AimedEnemy { get; private set; }

        /// <summary>Cargo the vacuum would pull in right now. Only set on the owner's computer.</summary>
        public CargoItem AimedSuck { get; private set; }

        public Vector3 EyePosition => transform.position + Vector3.up * config.eyeHeight;
        public Vector3 LookDirection => Quaternion.Euler(LookPitch, LookYaw, 0f) * Vector3.forward;

        /// <summary>Remaining ram cooldown from 1 (just used) to 0 (ready), for the local HUD.</summary>
        public float RamCooldown01
        {
            get
            {
                float cooldown = MatchDirector.Instance != null ? MatchDirector.Instance.RamCooldownFor(Team) : config.ramCooldown;
                return Mathf.Clamp01((localRamReadyTime - Time.time) / cooldown);
            }
        }

        /// <summary>Latest movement a co-carrier asked for. The host averages both carriers.</summary>
        public Vector2 ServerCoCarryInput { get; private set; }

        private GameConfig config;
        private CharacterController controller;
        private NetworkTransform netTransform;
        private ICharacterInput input;

        // Owner state
        private Vector3 slide;
        private Vector3 dashDirection;
        private float dashUntil;
        private float dashVisualUntil;
        private float localRamReadyTime;
        private float verticalSpeed;
        private bool lastSwing;
        private bool lastAttackHeld;
        private float swingCommittedUntil;
        private float nextSwingAskAt;
        private bool lastSuck;
        private float nextSuckAskAt;
        private bool lastCharge;
        private float nextChargeAskAt;
        private Vector3 pull;
        private float pullUntil;
        private ulong lastSmashTarget = NetIds.None;
        private ulong lastHitTarget = NetIds.None;
        private Vector2 lastCoCarry;
        private float nextCoCarrySend;

        // Host state
        private double serverRamUntil;
        private Vector3 serverRamDirection;
        private bool serverSwinging;
        private double serverNextHitAt;
        private double serverLastHitAt = double.NegativeInfinity;
        private int hitsInARow;
        private double lastHitTakenAt = double.NegativeInfinity;
        private double serverRespawnAt;
        private Vector3 serverLastPosition;
        private bool serverPositionKnown;
        private double serverSlamAt;
        private float nextPullSendAt;

        /// <summary>How fast this person is moving as the host sees them. Only kept on the host.</summary>
        public float ServerSpeed { get; private set; }

        public static PlayerAvatar Find(ulong networkObjectId)
        {
            if (networkObjectId == NetIds.None) return null;
            foreach (PlayerAvatar avatar in All)
                if (avatar.NetworkObjectId == networkObjectId) return avatar;
            return null;
        }

        private void Awake()
        {
            config = GameConfig.Get();
            controller = GetComponent<CharacterController>();
            netTransform = GetComponent<NetworkTransform>();
            Visual = GetComponent<CharacterVisual>();
        }

        /// <summary>Host only, before spawning.</summary>
        public void ServerInit(RosterEntry entry)
        {
            Health.Value = config.maxHealth;
            TeamVar.Value = entry.Team;
            NameVar.Value = entry.Name;
            IsBotVar.Value = entry.IsBot;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            controller.enabled = IsOwner;
            if (!IsOwner) return;

            LookYaw = transform.eulerAngles.y;
            if (IsBotVar.Value)
            {
                input = new BotBrain(this);
                return;
            }

            if (IsLocalPlayer)
            {
                Local = this;
                input = AutoPilot.LocalIsBot ? new BotBrain(this) : (ICharacterInput)new HumanInput(config);
            }
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
            if (input is BotBrain brain) brain.Dispose();
            input = null;
            if (!IsServer) return;
            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(transform.forward);
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsOwner) TickOwner(Time.deltaTime);
            if (IsServer) TickServer(Time.deltaTime);
        }

        // ---- Owner: movement and input ----

        private void TickOwner(float deltaTime)
        {
            if (input == null) return;
            input.Tick();

            MatchDirector director = MatchDirector.Instance;
            bool canAct = director != null && director.Phase.Value == MatchPhase.Playing && !IsKnockedDown && !IsStaggered;
            if (input.SwitchViewPressed && IsLocalPlayer) FollowCamera.ToggleView();
            if (input.InspectPressed && IsLocalPlayer && FirstPersonRig.Instance != null) FirstPersonRig.Instance.StartInspecting();
            UpdateLook(director);

            Vector2 rawMove = canAct ? Vector2.ClampMagnitude(input.Move, 1f) : Vector2.zero;
            Vector3 wish = input.LooksAround
                ? Quaternion.Euler(0f, LookYaw, 0f) * new Vector3(rawMove.x, 0f, rawMove.y)
                : new Vector3(rawMove.x, 0f, rawMove.y);
            CargoItem carried = CarriedCargo;
            bool coCarrying = carried != null && carried.Definition.requiredCarriers > 1;

            if (coCarrying) TickCoCarry(carried, new Vector2(wish.x, wish.z));
            else TickRunning(carried, wish, deltaTime);

            UpdateAim();
            if (canAct && input.RamPressed) TryRam(carried, wish, director);
            if (canAct && input.InteractPressed)
            {
                // The host decides. With nothing in hand, this says what the person was aiming at.
                if (carried != null) InteractRpc(NetIds.None);
                else if (AimedGrab != null) InteractRpc(AimedGrab.NetworkObjectId);
            }

            UpdateTools(canAct, carried, wish, deltaTime);
        }

        /// <summary>
        /// The tool in your hands. Interrupters hit people and smash cargo with the hammer on the left button,
        /// and charge a slam on the right. Collectors blast people with the vacuum on the left, and suck on the
        /// right. Your hands have to be empty. If someone's in front of you, they get it rather than the cargo.
        /// </summary>
        private void UpdateTools(bool canAct, CargoItem carried, Vector3 wish, float deltaTime)
        {
            bool free = canAct && carried == null;
            bool collector = Side == Side.Collector;
            bool secondary = input.SecondaryHeld;
            UpdateSuck(free && collector && secondary);
            UpdateCharge(free && !collector && secondary && !IsSlamming);

            // A quick click still finishes its swing, even though the button has already come back up.
            bool held = input.AttackHeld;
            // It stays up a little past the moment the blow lands, in case this message reaches the host faster than the first.
            if (held && !lastAttackHeld) swingCommittedUntil = Time.time + FirstHitSeconds + 0.12f;
            lastAttackHeld = held;

            bool busy = secondary || IsSlamming;
            bool swing = free && !busy && (held || Time.time < swingCommittedUntil);
            ulong hitTarget = swing && AimedEnemy != null ? AimedEnemy.NetworkObjectId : NetIds.None;
            ulong smashTarget = swing && hitTarget == NetIds.None && !collector && AimedSmash != null
                ? AimedSmash.NetworkObjectId
                : NetIds.None;

            // The host can turn a swing down or end it, for a ram or because a client's clock runs ahead and
            // it thinks you're still getting up. Keep asking, or holding the button would do nothing.
            bool hostDisagrees = swing != Swinging.Value && Time.time >= nextSwingAskAt;
            if (swing != lastSwing || hitTarget != lastHitTarget || smashTarget != lastSmashTarget || hostDisagrees)
            {
                lastSwing = swing;
                lastHitTarget = hitTarget;
                lastSmashTarget = smashTarget;
                nextSwingAskAt = Time.time + 0.2f;
                SetSwingingRpc(swing, smashTarget, hitTarget);
            }
            if (swing && !input.LooksAround && wish.sqrMagnitude <= 0.01f) FaceSwingTarget(deltaTime);
        }

        private void UpdateSuck(bool suck)
        {
            bool hostDisagrees = suck != Sucking.Value && Time.time >= nextSuckAskAt;
            if (suck == lastSuck && !hostDisagrees) return;
            lastSuck = suck;
            nextSuckAskAt = Time.time + 0.2f;
            SetSuckingRpc(suck);
        }

        private void UpdateCharge(bool charge)
        {
            if (charge != lastCharge)
            {
                lastCharge = charge;
                nextChargeAskAt = Time.time + 0.2f;
                if (charge) StartChargingRpc();
                else ReleaseChargeRpc();
            }
            else if (charge && !IsCharging && Time.time >= nextChargeAskAt)
            {
                nextChargeAskAt = Time.time + 0.2f;
                StartChargingRpc();
            }
        }

        /// <summary>For the automatic tests, when a button seems to do nothing.</summary>
        public string ToolState => input == null ? "no input" :
            $"side={Side} attack={input.AttackHeld} secondary={input.SecondaryHeld} lastSwing={lastSwing} swinging={Swinging.Value} " +
            $"slamming={IsSlamming} charging={IsCharging} lastCharge={lastCharge} sucking={Sucking.Value} lastSuck={lastSuck} carrying={CarriedCargoId.Value != NetIds.None} " +
            $"knocked={IsKnockedDown} out={KnockedOut.Value} staggered={IsStaggered} immune={IsImmune} dashing={IsDashing} askIn={nextSwingAskAt - Time.time:0.00} committed={swingCommittedUntil - Time.time:0.00}";

        private float FirstHitSeconds => Side == Side.Collector ? config.blastFirstSeconds : config.smashFirstHitSeconds;
        private float SwingSeconds => Side == Side.Collector ? config.blastCycleSeconds : config.smashSwingSeconds;

        private void UpdateLook(MatchDirector director)
        {
            bool outro = director != null && director.Phase.Value >= MatchPhase.HalfOutro;
            if (input.LooksAround)
            {
                // You can look around during the countdown, but not while flat on your back or during a cutscene.
                bool canLook = !IsKnockedDown && !outro;
                LookThisFrame = canLook ? input.Look : Vector2.zero;
                LookYaw = Mathf.Repeat(LookYaw + LookThisFrame.x, 360f);
                LookPitch = Mathf.Clamp(LookPitch - LookThisFrame.y, -config.maxLookUp, config.maxLookDown);
                return;
            }

            // A bot playing as the local player, in the automatic tests: look where it's going, a little downward.
            float before = LookYaw;
            LookYaw = Mathf.MoveTowardsAngle(LookYaw, transform.eulerAngles.y, 260f * Time.deltaTime);
            LookPitch = Mathf.MoveTowards(LookPitch, 14f, 40f * Time.deltaTime);
            LookThisFrame = new Vector2(Mathf.DeltaAngle(before, LookYaw), 0f);
        }

        private void UpdateAim()
        {
            if (input.LooksAround)
            {
                AimedGrab = CargoItem.FindAimed(EyePosition, LookYaw, LookPitch, transform.position, config.pickupRange, false);
                AimedSmash = CargoItem.FindAimed(EyePosition, LookYaw, LookPitch, transform.position, config.smashRange, true);
            }
            else
            {
                AimedGrab = CargoItem.FindGrabbable(transform.position, config.pickupRange);
                AimedSmash = CargoItem.FindSmashable(transform.position, config.smashRange);
            }
            AimedEnemy = FindEnemyInReach();
            AimedSuck = input.LooksAround && Side == Side.Collector
                ? CargoItem.FindSuckable(transform.position, Quaternion.Euler(0f, LookYaw, 0f) * Vector3.forward)
                : null;
        }

        private PlayerAvatar FindEnemyInReach()
        {
            bool collector = Side == Side.Collector;
            float range = collector ? config.blastRange : config.hitRange;
            float aim = collector ? config.blastAimDegrees : config.hitAimDegrees;
            PlayerAvatar best = null;
            float bestScore = float.MaxValue;
            foreach (PlayerAvatar other in All)
            {
                if (other.Team == Team || other.IsKnockedDown || other.IsImmune) continue;
                Vector3 to = other.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > range) continue;

                // People have to be looking at who they hit. A bot turns to face them when it swings.
                float sideways = input.LooksAround ? Mathf.Abs(Mathf.DeltaAngle(LookYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg)) : 0f;
                if (sideways > aim) continue;
                float score = sideways + distance * 8f;
                if (score < bestScore)
                {
                    best = other;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>Standing still and swinging: turn so the blow lands on what it's meant for.</summary>
        private void FaceSwingTarget(float deltaTime)
        {
            Transform target = AimedEnemy != null ? AimedEnemy.transform : AimedSmash != null ? AimedSmash.transform : null;
            if (target == null) return;
            Vector3 toTarget = target.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget),
                config.turnDegreesPerSecond * deltaTime);
        }

        private void TickRunning(CargoItem carried, Vector3 wish, float deltaTime)
        {
            if (!controller.enabled) controller.enabled = true;

            float speed = config.runSpeed;
            if (carried != null) speed *= carried.Definition.carrySpeedMultiplier;
            if (Sucking.Value) speed *= config.suckMoveMultiplier;
            else if (IsCharging) speed *= config.chargeMoveMultiplier;
            Vector3 velocity = wish * speed;
            if (Time.time < pullUntil && !IsKnockedDown) velocity += pull;
            if (Time.time < dashUntil) velocity = dashDirection * config.ramSpeed;
            if (slide.sqrMagnitude > 0.01f)
            {
                velocity += slide;
                slide = Vector3.MoveTowards(slide, Vector3.zero, 18f * deltaTime);
            }

            velocity += SeparationFromOthers();

            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed - 20f * deltaTime;
            controller.Move((velocity + Vector3.up * verticalSpeed) * deltaTime);

            if (input.LooksAround)
            {
                // Knocked flat, the body stays turned toward whoever did it.
                if (!IsKnockedDown) transform.rotation = Quaternion.Euler(0f, LookYaw, 0f);
            }
            else if (wish.sqrMagnitude > 0.01f && Time.time >= dashUntil)
            {
                Quaternion target = Quaternion.LookRotation(wish);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
                    config.turnDegreesPerSecond * deltaTime);
            }
        }

        /// <summary>
        /// People don't block each other, but they do ease apart. Otherwise you could end up looking
        /// out through the inside of someone's head.
        /// </summary>
        private Vector3 SeparationFromOthers()
        {
            Vector3 push = Vector3.zero;
            Vector3 position = transform.position;
            float range = config.personalSpace;
            foreach (PlayerAvatar other in All)
            {
                if (other == this) continue;
                Vector3 away = position - other.transform.position;
                away.y = 0f;
                float distance = away.magnitude;
                if (distance >= range) continue;
                Vector3 direction = distance > 0.01f ? away / distance : transform.right;
                push += direction * ((range - distance) / range * config.personalSpacePush);
            }
            return push;
        }

        /// <summary>
        /// While two people carry a large item the host moves the item, so each carrier sends
        /// their input to the host and stays on their side of the item.
        /// </summary>
        private void TickCoCarry(CargoItem carried, Vector2 worldMove)
        {
            if (controller.enabled) controller.enabled = false;
            dashUntil = 0f;
            // A hit can't push you away from the chest, and shouldn't push you long afterward when you let go.
            slide = Vector3.zero;

            if ((worldMove - lastCoCarry).sqrMagnitude > 0.01f || Time.time >= nextCoCarrySend)
            {
                lastCoCarry = worldMove;
                nextCoCarrySend = Time.time + 0.1f;
                CoCarryInputRpc(worldMove);
            }

            Vector3 grab = carried.GrabPointWorld(this);
            transform.position = new Vector3(grab.x, transform.position.y, grab.z);
            Vector3 toCargo = carried.transform.position - transform.position;
            toCargo.y = 0f;
            if (toCargo.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toCargo);
        }

        private void TryRam(CargoItem carried, Vector3 wish, MatchDirector director)
        {
            if (carried != null || Time.time < localRamReadyTime || Time.time < dashUntil) return;
            // People charge the way they're looking. A bot charges the way it's heading.
            Vector3 direction = input.LooksAround
                ? Quaternion.Euler(0f, LookYaw, 0f) * Vector3.forward
                : wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
            dashDirection = direction;
            dashUntil = Time.time + config.ramDuration;
            localRamReadyTime = Time.time + director.RamCooldownFor(Team);
            transform.rotation = Quaternion.LookRotation(direction);
            RamRpc(direction);
        }

        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 position, Quaternion rotation)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            netTransform.Teleport(position, rotation, transform.localScale);
            controller.enabled = wasEnabled;
            LookYaw = rotation.eulerAngles.y;
            LookPitch = 0f;
            dashUntil = 0f;
            localRamReadyTime = 0f;
            verticalSpeed = 0f;
            slide = Vector3.zero;
        }

        [Rpc(SendTo.Owner)]
        private void StaggerRpc(Vector3 push)
        {
            dashUntil = 0f;
            slide = push;
            if (!IsLocalPlayer) return;
            Vector3 direction = push.normalized;
            if (MatchHud.Instance != null) MatchHud.Instance.ShowHitFrom(-direction);
            if (FirstPersonRig.Instance != null) FirstPersonRig.Instance.Flinch(direction);
            FollowCamera.Flinch(direction);
        }

        /// <summary>Someone's vacuum has got you. Your own computer moves you, so this keeps coming for as long as it does.</summary>
        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Unreliable)]
        private void PullRpc(Vector3 velocity)
        {
            pull = velocity;
            pullUntil = Time.time + 0.25f;
        }

        public bool IsBeingPulled => Time.time < pullUntil;

        [Rpc(SendTo.Owner)]
        private void KnockbackRpc(Vector3 direction)
        {
            dashUntil = 0f;
            slide = direction * config.knockbackSpeed;
            // Face whoever hit us, so the fall goes the way the hit pushed.
            if (direction.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(-direction);
            if (IsLocalPlayer && MatchHud.Instance != null) MatchHud.Instance.ShowHitFrom(-direction);
        }

        // ---- Host: rules ----

        private bool ServerCanAct()
        {
            MatchDirector director = MatchDirector.Instance;
            return director != null && director.Phase.Value == MatchPhase.Playing && !IsKnockedDown && !IsStaggered;
        }

        [Rpc(SendTo.Server)]
        private void InteractRpc(ulong aimedCargoId)
        {
            if (!ServerCanAct()) return;
            CargoItem carried = CarriedCargo;
            if (aimedCargoId == NetIds.None)
            {
                if (carried != null) carried.ServerRelease(this);
                return;
            }

            // Pressed again before hearing that the grab worked. That isn't asking to let go.
            if (carried != null) return;
            CargoItem target = CargoItem.Find(aimedCargoId);
            if (target != null && target.IsWithin(transform.position, config.pickupRange + config.reachTolerance)) target.ServerTryGrab(this);
        }

        [Rpc(SendTo.Server)]
        private void SetSwingingRpc(bool swinging, ulong smashTargetId, ulong hitTargetId)
        {
            Swinging.Value = swinging && ServerCanAct() && CarriedCargo == null;
            SmashTargetId.Value = Swinging.Value && Side == Side.Interrupter ? smashTargetId : NetIds.None;
            HitTargetId.Value = Swinging.Value ? hitTargetId : NetIds.None;
        }

        [Rpc(SendTo.Server)]
        private void SetSuckingRpc(bool sucking)
        {
            Sucking.Value = sucking && ServerCanAct() && CarriedCargo == null && Side == Side.Collector;
            if (Sucking.Value) ServerStopSwinging();
        }

        [Rpc(SendTo.Server)]
        private void StartChargingRpc()
        {
            if (IsCharging || IsSlamming || !ServerCanAct() || CarriedCargo != null || Side != Side.Interrupter) return;
            ChargingSince.Value = Now;
            ServerStopSwinging();
        }

        /// <summary>Let go too early and nothing happens, which makes it possible to bluff.</summary>
        [Rpc(SendTo.Server)]
        private void ReleaseChargeRpc()
        {
            if (!IsCharging) return;
            bool ready = Now - ChargingSince.Value >= config.chargeSeconds - 0.05;
            ChargingSince.Value = 0.0;
            if (!ready || !ServerCanAct()) return;
            SlammingUntil.Value = Now + config.slamSeconds;
            serverSlamAt = Now + config.slamLandSeconds;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
        private void CoCarryInputRpc(Vector2 move)
        {
            ServerCoCarryInput = Vector2.ClampMagnitude(move, 1f);
        }

        [Rpc(SendTo.Server)]
        private void RamRpc(Vector3 direction)
        {
            if (!ServerCanAct() || CarriedCargo != null) return;
            // A little tolerance, because the owner's clock runs slightly ahead of what the host saw.
            if (Now + 0.25 < RamReadyAt.Value) return;

            MatchDirector director = MatchDirector.Instance;
            RamReadyAt.Value = Now + director.RamCooldownFor(Team);
            serverRamUntil = Now + config.ramDuration + 0.1;
            serverRamDirection = direction;
            ServerStopTools();
            director.ServerOnRam(this);
            RamVisualRpc();
        }

        private void TickServer(float deltaTime)
        {
            TrackSpeed(deltaTime);
            if (Now < serverRamUntil) ServerCheckRamHit();
            if (serverRespawnAt > 0.0 && Now >= serverRespawnAt) ServerRespawn();
            if (serverSlamAt > 0.0 && Now >= serverSlamAt) ServerSlam();
            if (IsCharging && (!ServerCanAct() || CarriedCargo != null)) ChargingSince.Value = 0.0;

            if (Sucking.Value)
            {
                if (ServerCanAct() && CarriedCargo == null && Side == Side.Collector) ServerSuck(deltaTime);
                else Sucking.Value = false;
            }

            if (!Swinging.Value)
            {
                serverSwinging = false;
                return;
            }
            if (!ServerCanAct() || CarriedCargo != null)
            {
                ServerStopSwinging();
                return;
            }
            ServerSwing();
        }

        /// <summary>
        /// A swing lands once, when the hammer comes down or the air comes out. Letting go and pressing again
        /// doesn't swing any faster, because the next hit can't come sooner than a full swing after the last.
        /// </summary>
        private void ServerSwing()
        {
            if (!serverSwinging)
            {
                serverSwinging = true;
                serverNextHitAt = System.Math.Max(Now + FirstHitSeconds, serverLastHitAt + SwingSeconds);
            }
            if (Now < serverNextHitAt) return;
            serverLastHitAt = Now;
            serverNextHitAt = Now + SwingSeconds;

            bool collector = Side == Side.Collector;
            PlayerAvatar person = HitTarget;
            if (person != null && person.Team != Team)
            {
                float reach = (collector ? config.blastRange : config.hitRange) + config.reachTolerance + LagAllowance(person);
                float distance = HorizontalDistance(person);
                if (distance <= reach && person.ServerTakeHit(this, collector ? Weapon.Blast : Weapon.Hammer)) return;
                if (distance > reach) AutoPilot.Log($"MISS {DisplayName} at {person.DisplayName} distance={distance:0.00} reach={reach:0.00}");
            }

            CargoItem target = SmashTarget;
            if (target != null && target.IsWithin(transform.position, config.smashRange + config.reachTolerance))
                target.ServerApplySmash(this, config.smashSwingSeconds);
        }

        /// <summary>Everything in front of you gets pulled in, and the first cargo to reach your hands stays there.</summary>
        private void ServerSuck(float deltaTime)
        {
            Vector3 forward = transform.forward;
            Vector3 mouth = transform.position + forward * 0.7f + Vector3.up * 0.9f;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (!cargo.CanBeSuckedFrom(transform.position, forward)) continue;
                cargo.ServerPullToward(mouth, cargo.Definition.size == CargoSize.Small ? config.suckSpeedSmall : config.suckSpeedMedium, deltaTime);
                if (cargo.EdgeDistance(transform.position) > config.suckGrabRange) continue;
                if (cargo.ServerTryGrab(this)) return;
            }

            if (Time.time < nextPullSendAt) return;
            nextPullSendAt = Time.time + 0.1f;
            foreach (PlayerAvatar other in All)
            {
                if (other.Team == Team || other.IsKnockedDown) continue;
                Vector3 to = other.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > config.suckPersonRange || distance < config.suckPersonStop) continue;
                if (Vector3.Angle(forward, to) > config.suckAimDegrees) continue;
                other.PullRpc(-to / distance * config.suckPersonSpeed);
            }
        }

        /// <summary>It comes down in front of you and gets everything there at once, cargo included.</summary>
        private void ServerSlam()
        {
            serverSlamAt = 0.0;
            if (!ServerCanAct()) return;
            Vector3 impact = transform.position + transform.forward * config.slamReach;
            float radius = config.slamRadius;
            foreach (PlayerAvatar other in All)
            {
                if (other.Team == Team) continue;
                Vector3 offset = other.transform.position - impact;
                offset.y = 0f;
                float reach = radius + LagAllowance(other);
                if (offset.sqrMagnitude <= reach * reach) other.ServerTakeHit(this, Weapon.Slam);
            }
            for (int i = CargoItem.All.Count - 1; i >= 0; i--)
            {
                CargoItem cargo = CargoItem.All[i];
                if (cargo.IsWithin(impact, radius)) cargo.ServerApplySmash(this, config.smashSwingSeconds * config.slamCargoSwings);
            }
            SlamRpc(impact);
        }

        /// <summary>
        /// Someone playing on another computer sees everyone where they were a moment ago, and the host sees
        /// them a moment late as well. Without allowing for that, their swings at anyone running away never land.
        /// The host and the bots see what's really there, so they get nothing extra.
        /// </summary>
        private float LagAllowance(PlayerAvatar target)
        {
            if (IsOwnedByServer) return 0f;
            return Mathf.Min((ServerSpeed + target.ServerSpeed) * config.lagCompensationSeconds, config.maxLagCompensation);
        }

        private float HorizontalDistance(PlayerAvatar other)
        {
            Vector3 offset = other.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private void TrackSpeed(float deltaTime)
        {
            Vector3 position = transform.position;
            if (serverPositionKnown && deltaTime > 0f)
            {
                Vector3 moved = position - serverLastPosition;
                moved.y = 0f;
                // A teleport isn't running, and a ram is the fastest anyone really goes.
                float speed = Mathf.Min(moved.magnitude / deltaTime, config.ramSpeed);
                ServerSpeed = Mathf.Lerp(ServerSpeed, speed, 1f - Mathf.Exp(-deltaTime / 0.12f));
            }
            serverLastPosition = position;
            serverPositionKnown = true;
        }

        private void ServerStopSwinging()
        {
            Swinging.Value = false;
            SmashTargetId.Value = NetIds.None;
            HitTargetId.Value = NetIds.None;
        }

        private void ServerStopTools()
        {
            ServerStopSwinging();
            Sucking.Value = false;
            ChargingSince.Value = 0.0;
            SlammingUntil.Value = 0.0;
            serverSlamAt = 0.0;
        }

        /// <summary>
        /// Every hit takes health. One also pushes you back and stops what you're doing, and another straight
        /// after knocks you down. A ram or a slam knocks you down by itself, and running out of health knocks
        /// you out. Returns false if it had no effect.
        /// </summary>
        public bool ServerTakeHit(PlayerAvatar attacker, Weapon weapon)
        {
            if (IsKnockedDown || IsImmune) return false;

            Vector3 direction = transform.position - attacker.transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : attacker.transform.forward;
            Vector3 where = transform.position + Vector3.up * 1.1f;

            int damage = config.DamageOf(weapon);
            Health.Value = Mathf.Max(0, Health.Value - damage);
            hitsInARow = Now - lastHitTakenAt <= config.hitComboSeconds ? hitsInARow + 1 : 1;
            lastHitTakenAt = Now;

            bool knockedOut = Health.Value == 0;
            bool knockedDown = knockedOut || weapon == Weapon.Ram || weapon == Weapon.Slam || hitsInARow >= config.hitsToKnockDown;
            MatchDirector.Instance.ServerOnHit(attacker, this, weapon, knockedDown, knockedOut);
            if (knockedOut)
            {
                ServerKnockOut(attacker, weapon, direction);
                attacker.BonkRpc(where, damage);
                return true;
            }
            if (knockedDown)
            {
                ServerKnockDown(direction);
                attacker.BonkRpc(where, damage);
                return true;
            }

            StaggeredUntil.Value = Now + config.hitStaggerSeconds;
            ServerStopTools();
            StaggerRpc(direction * (weapon == Weapon.Blast ? config.blastPushSpeed : config.hitPushSpeed));
            attacker.PowRpc(where, damage);
            return true;
        }

        private void ServerKnockOut(PlayerAvatar attacker, Weapon weapon, Vector3 direction)
        {
            KnockedOut.Value = true;
            KnockedUntil.Value = Now + config.respawnSeconds;
            ImmuneUntil.Value = KnockedUntil.Value + config.immuneSeconds;
            serverRespawnAt = KnockedUntil.Value;
            StaggeredUntil.Value = 0.0;
            hitsInARow = 0;
            ServerStopTools();

            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(direction);
            MatchDirector.Instance.ServerOnKnockedOut(attacker, this, weapon, carried != null);
            KnockbackRpc(direction * 1.7f);
        }

        private void ServerRespawn()
        {
            serverRespawnAt = 0.0;
            KnockedOut.Value = false;
            Health.Value = config.maxHealth;
            Transform spot = MatchDirector.Instance.RespawnSpot(this);
            PoofRpc(transform.position, spot.position);
            TeleportRpc(spot.position, spot.rotation);
        }

        private void ServerCheckRamHit()
        {
            Vector3 position = transform.position;
            foreach (PlayerAvatar other in All)
            {
                if (other == this || other.Team == Team || other.IsKnockedDown || other.IsImmune) continue;
                Vector3 offset = other.transform.position - position;
                offset.y = 0f;
                if (offset.sqrMagnitude > config.ramHitRadius * config.ramHitRadius) continue;
                // You charge what's in front of you, not someone you just ran past.
                if (Vector3.Dot(offset, serverRamDirection) < 0f) continue;

                serverRamUntil = 0;
                other.ServerTakeHit(this, Weapon.Ram);
                return;
            }
        }

        public void ServerKnockDown(Vector3 direction)
        {
            KnockedUntil.Value = Now + config.knockdownSeconds;
            ImmuneUntil.Value = Now + config.knockdownSeconds + config.immuneSeconds;
            StaggeredUntil.Value = 0;
            hitsInARow = 0;
            ServerStopTools();

            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(direction);
            MatchDirector.Instance.ServerOnKnockedDown(this, carried != null);
            KnockbackRpc(direction);
        }

        public void ServerSetCarry(CargoItem cargo)
        {
            CarriedCargoId.Value = cargo.NetworkObjectId;
            ServerStopTools();
            ServerCoCarryInput = Vector2.zero;
        }

        public void ServerClearCarry()
        {
            CarriedCargoId.Value = NetIds.None;
            ServerCoCarryInput = Vector2.zero;
        }

        /// <summary>The whistle blew: let go of everything.</summary>
        public void ServerStopActions()
        {
            ServerStopTools();
            serverRamUntil = 0;
            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(transform.forward);
        }

        public void ServerResetForHalf(Vector3 position, Quaternion rotation)
        {
            ServerStopActions();
            ServerClearCarry();
            KnockedUntil.Value = 0;
            ImmuneUntil.Value = 0;
            StaggeredUntil.Value = 0;
            hitsInARow = 0;
            KnockedOut.Value = false;
            serverRespawnAt = 0.0;
            Health.Value = config.maxHealth;
            RamReadyAt.Value = 0;
            TeleportRpc(position, rotation);
        }

        // ---- Everyone: feedback ----

        [Rpc(SendTo.Everyone)]
        private void RamVisualRpc()
        {
            dashVisualUntil = Time.time + config.ramDuration;
            GameAudio.PlayAt("whoosh", transform.position);
        }

        [Rpc(SendTo.Everyone)]
        private void PowRpc(Vector3 position, int damage)
        {
            MatchHud hud = MatchHud.Instance;
            if (hud != null)
            {
                hud.ShowPopup(position, GameText.Pow, new Color(1f, 0.95f, 0.6f), 0.9f);
                hud.ShowPopup(position + Vector3.right * 0.5f, GameText.Damage(damage), new Color(1f, 0.4f, 0.32f), 0.75f);
                if (IsLocalPlayer) hud.ShowHitMarker(false);
            }
            GameAudio.PlayAt("punch", position);
            FxBurst.Play(position, Color.white, 6);
            FxBurst.PlayFlash(position, new Color(1f, 0.95f, 0.7f), 0.9f);
            FollowCamera.Shake(position, 0.35f);
        }

        [Rpc(SendTo.Everyone)]
        private void BonkRpc(Vector3 position, int damage)
        {
            if (IsOwner) dashUntil = 0f;
            MatchHud hud = MatchHud.Instance;
            if (hud != null)
            {
                hud.ShowPopup(position, GameText.Bonk, new Color(1f, 0.9f, 0.2f), 1.5f);
                hud.ShowPopup(position + Vector3.right * 0.7f, GameText.Damage(damage), new Color(1f, 0.4f, 0.32f), 0.9f);
                if (IsLocalPlayer) hud.ShowHitMarker(true);
            }
            GameAudio.PlayAt("bonk", position);
            FxBurst.Play(position, Color.white, 14);
            FxBurst.PlayFlash(position, new Color(1f, 0.92f, 0.45f), 1.5f);
            FollowCamera.Shake(position, 0.9f);
        }

        [Rpc(SendTo.Everyone)]
        private void SlamRpc(Vector3 impact)
        {
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(impact, GameText.Slam, new Color(1f, 0.55f, 0.2f), 1.6f);
            GameAudio.PlayAt("slam", impact);
            FxBurst.PlaySlam(impact);
            FollowCamera.Shake(impact, 1.2f);
            if (IsLocalPlayer) FollowCamera.Kick(2.2f);
        }

        /// <summary>Where someone vanished from and where they came back.</summary>
        [Rpc(SendTo.Everyone)]
        private void PoofRpc(Vector3 from, Vector3 to)
        {
            Color smoke = new Color(0.92f, 0.92f, 0.95f);
            FxBurst.PlayPuff(from + Vector3.up * 0.7f, smoke, 10);
            FxBurst.PlayPuff(to + Vector3.up * 0.7f, smoke, 10);
            GameAudio.PlayAt("respawn", to, 0.8f);
        }
    }
}
