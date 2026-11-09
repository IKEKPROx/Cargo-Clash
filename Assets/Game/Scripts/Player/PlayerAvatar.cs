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
        public readonly NetworkVariable<bool> Smashing = new NetworkVariable<bool>();

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
        public bool RamReady => Now >= RamReadyAt.Value;
        public bool IsDashing => Time.time < dashVisualUntil;
        public CargoItem CarriedCargo => CargoItem.Find(CarriedCargoId.Value);

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
        private bool lastSmash;
        private Vector2 lastCoCarry;
        private float nextCoCarrySend;

        // Host state
        private double serverRamUntil;
        private Vector3 serverRamDirection;

        private void Awake()
        {
            config = GameConfig.Get();
            controller = GetComponent<CharacterController>();
            netTransform = GetComponent<NetworkTransform>();
        }

        /// <summary>Host only, before spawning.</summary>
        public void ServerInit(RosterEntry entry)
        {
            TeamVar.Value = entry.Team;
            NameVar.Value = entry.Name;
            IsBotVar.Value = entry.IsBot;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            controller.enabled = IsOwner;
            if (!IsOwner) return;

            if (IsBotVar.Value)
            {
                input = new BotBrain(this);
                return;
            }

            if (IsLocalPlayer)
            {
                Local = this;
                input = AutoPilot.LocalIsBot ? new BotBrain(this) : (ICharacterInput)new HumanInput(config.inputActions);
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
            bool canAct = director != null && director.Phase.Value == MatchPhase.Playing && !IsKnockedDown;
            Vector2 rawMove = canAct ? Vector2.ClampMagnitude(input.Move, 1f) : Vector2.zero;
            Vector3 wish = new Vector3(rawMove.x, 0f, rawMove.y);
            CargoItem carried = CarriedCargo;
            bool coCarrying = carried != null && carried.Definition.requiredCarriers > 1;

            if (coCarrying) TickCoCarry(carried, rawMove);
            else TickRunning(carried, wish, deltaTime);

            if (canAct && input.RamPressed) TryRam(carried, wish, director);
            if (canAct && input.InteractPressed) InteractRpc();

            bool smash = canAct && carried == null && input.SmashHeld && Side == Side.Interrupter;
            if (smash != lastSmash)
            {
                lastSmash = smash;
                SetSmashingRpc(smash);
            }
            if (smash && wish.sqrMagnitude <= 0.01f) FaceSmashTarget(deltaTime);
        }

        /// <summary>Standing still and smashing: turn so the hammer lands on the cargo.</summary>
        private void FaceSmashTarget(float deltaTime)
        {
            CargoItem target = CargoItem.FindSmashable(transform.position, config.smashRange);
            if (target == null) return;
            Vector3 toTarget = target.transform.position - transform.position;
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
            Vector3 velocity = wish * speed;
            if (Time.time < dashUntil) velocity = dashDirection * config.ramSpeed;
            if (slide.sqrMagnitude > 0.01f)
            {
                velocity += slide;
                slide = Vector3.MoveTowards(slide, Vector3.zero, 18f * deltaTime);
            }

            verticalSpeed = controller.isGrounded ? -2f : verticalSpeed - 20f * deltaTime;
            controller.Move((velocity + Vector3.up * verticalSpeed) * deltaTime);

            if (wish.sqrMagnitude > 0.01f && Time.time >= dashUntil)
            {
                Quaternion target = Quaternion.LookRotation(wish);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
                    config.turnDegreesPerSecond * deltaTime);
            }
        }

        /// <summary>
        /// While two people carry a large item the host moves the item, so each carrier sends
        /// their input to the host and stays on their side of the item.
        /// </summary>
        private void TickCoCarry(CargoItem carried, Vector2 rawMove)
        {
            if (controller.enabled) controller.enabled = false;
            dashUntil = 0f;

            if ((rawMove - lastCoCarry).sqrMagnitude > 0.01f || Time.time >= nextCoCarrySend)
            {
                lastCoCarry = rawMove;
                nextCoCarrySend = Time.time + 0.1f;
                CoCarryInputRpc(rawMove);
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
            Vector3 direction = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
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
            dashUntil = 0f;
            localRamReadyTime = 0f;
            verticalSpeed = 0f;
            slide = Vector3.zero;
        }

        [Rpc(SendTo.Owner)]
        private void KnockbackRpc(Vector3 direction)
        {
            dashUntil = 0f;
            slide = direction * config.knockbackSpeed;
            // Face whoever hit us, so the fall goes the way the hit pushed.
            if (direction.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(-direction);
        }

        // ---- Host: rules ----

        private bool ServerCanAct()
        {
            MatchDirector director = MatchDirector.Instance;
            return director != null && director.Phase.Value == MatchPhase.Playing && !IsKnockedDown;
        }

        [Rpc(SendTo.Server)]
        private void InteractRpc()
        {
            if (!ServerCanAct()) return;
            CargoItem carried = CarriedCargo;
            if (carried != null)
            {
                carried.ServerRelease(this);
                return;
            }
            CargoItem target = CargoItem.FindGrabbable(transform.position, config.pickupRange);
            if (target != null) target.ServerTryGrab(this);
        }

        [Rpc(SendTo.Server)]
        private void SetSmashingRpc(bool smashing)
        {
            Smashing.Value = smashing && ServerCanAct() && Side == Side.Interrupter && CarriedCargo == null;
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
            Smashing.Value = false;
            director.ServerOnRam(this);
            RamVisualRpc();
        }

        private void TickServer(float deltaTime)
        {
            if (Now < serverRamUntil) ServerCheckRamHit();

            if (!Smashing.Value) return;
            if (!ServerCanAct() || CarriedCargo != null || Side != Side.Interrupter)
            {
                Smashing.Value = false;
                return;
            }
            CargoItem target = CargoItem.FindSmashable(transform.position, config.smashRange);
            if (target != null) target.ServerApplySmash(this, deltaTime);
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

                Vector3 direction = offset.sqrMagnitude > 0.01f ? offset.normalized : serverRamDirection;
                serverRamUntil = 0;
                other.ServerKnockDown(direction);
                BonkRpc(other.transform.position + Vector3.up);
                return;
            }
        }

        public void ServerKnockDown(Vector3 direction)
        {
            KnockedUntil.Value = Now + config.knockdownSeconds;
            ImmuneUntil.Value = Now + config.knockdownSeconds + config.immuneSeconds;
            Smashing.Value = false;

            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(direction);
            MatchDirector.Instance.ServerOnKnockedDown(this, carried != null);
            KnockbackRpc(direction);
        }

        public void ServerSetCarry(CargoItem cargo)
        {
            CarriedCargoId.Value = cargo.NetworkObjectId;
            Smashing.Value = false;
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
            Smashing.Value = false;
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
            RamReadyAt.Value = 0;
            TeleportRpc(position, rotation);
        }

        // ---- Everyone: feedback ----

        [Rpc(SendTo.Everyone)]
        private void RamVisualRpc()
        {
            dashVisualUntil = Time.time + config.ramDuration;
            GameAudio.Play("whoosh");
        }

        [Rpc(SendTo.Everyone)]
        private void BonkRpc(Vector3 position)
        {
            if (IsOwner) dashUntil = 0f;
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(position, "BONK!", new Color(1f, 0.9f, 0.2f), 1.5f);
            GameAudio.Play("bonk");
            FxBurst.Play(position, Color.white, 14);
            FollowCamera.Shake(position);
        }
    }
}
