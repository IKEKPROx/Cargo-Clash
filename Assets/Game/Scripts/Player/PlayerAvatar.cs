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

            UpdateSwing(canAct, carried, wish, deltaTime);
        }

        /// <summary>
        /// Left click: both sides can hit people, and Interrupters can smash cargo too. Your hands have to
        /// be empty. If someone's in front of you, they get it rather than the cargo.
        /// </summary>
        private void UpdateSwing(bool canAct, CargoItem carried, Vector3 wish, float deltaTime)
        {
            // A quick click still finishes its swing, even though the button has already come back up.
            bool held = input.AttackHeld;
            // It stays up a little past the moment the blow lands, in case this message reaches the host faster than the first.
            if (held && !lastAttackHeld) swingCommittedUntil = Time.time + config.smashFirstHitSeconds + 0.12f;
            lastAttackHeld = held;

            bool swing = canAct && carried == null && (held || Time.time < swingCommittedUntil);
            ulong hitTarget = swing && AimedEnemy != null ? AimedEnemy.NetworkObjectId : NetIds.None;
            ulong smashTarget = swing && hitTarget == NetIds.None && Side == Side.Interrupter && AimedSmash != null
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
        }

        private PlayerAvatar FindEnemyInReach()
        {
            PlayerAvatar best = null;
            float bestScore = float.MaxValue;
            foreach (PlayerAvatar other in All)
            {
                if (other.Team == Team || other.IsKnockedDown || other.IsImmune) continue;
                Vector3 to = other.transform.position - transform.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > config.hitRange) continue;

                // People have to be looking at who they hit. A bot turns to face them when it swings.
                float sideways = input.LooksAround ? Mathf.Abs(Mathf.DeltaAngle(LookYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg)) : 0f;
                if (sideways > config.hitAimDegrees) continue;
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
            Vector3 velocity = wish * speed;
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
        private void StaggerRpc(Vector3 direction)
        {
            dashUntil = 0f;
            slide = direction * config.hitPushSpeed;
            if (!IsLocalPlayer) return;
            if (MatchHud.Instance != null) MatchHud.Instance.ShowHitFrom(-direction);
            if (FirstPersonRig.Instance != null) FirstPersonRig.Instance.Flinch(direction);
            FollowCamera.Flinch(direction);
        }

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
            ServerStopSwinging();
            director.ServerOnRam(this);
            RamVisualRpc();
        }

        private void TickServer(float deltaTime)
        {
            if (Now < serverRamUntil) ServerCheckRamHit();

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
        /// A swing lands once, when the hammer comes down or the fist goes out. Letting go and pressing again
        /// doesn't swing any faster, because the next hit can't come sooner than a full swing after the last.
        /// </summary>
        private void ServerSwing()
        {
            if (!serverSwinging)
            {
                serverSwinging = true;
                serverNextHitAt = System.Math.Max(Now + config.smashFirstHitSeconds, serverLastHitAt + config.smashSwingSeconds);
            }
            if (Now < serverNextHitAt) return;
            serverLastHitAt = Now;
            serverNextHitAt = Now + config.smashSwingSeconds;

            float reach = config.hitRange + config.reachTolerance;
            PlayerAvatar person = HitTarget;
            if (person != null && person.Team != Team && (person.transform.position - transform.position).sqrMagnitude <= reach * reach && person.ServerTakeHit(this))
                return;

            CargoItem target = SmashTarget;
            if (target != null && target.IsWithin(transform.position, config.smashRange + config.reachTolerance))
                target.ServerApplySmash(this, config.smashSwingSeconds);
        }

        private void ServerStopSwinging()
        {
            Swinging.Value = false;
            SmashTargetId.Value = NetIds.None;
            HitTargetId.Value = NetIds.None;
        }

        /// <summary>
        /// A punch or a hammer. One pushes you back and stops what you're doing, and another straight after
        /// knocks you down the same as a ram. Returns false if it had no effect.
        /// </summary>
        public bool ServerTakeHit(PlayerAvatar attacker)
        {
            if (IsKnockedDown || IsImmune) return false;

            Vector3 direction = transform.position - attacker.transform.position;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.01f ? direction.normalized : attacker.transform.forward;
            Vector3 where = transform.position + Vector3.up * 1.1f;

            hitsInARow = Now - lastHitTakenAt <= config.hitComboSeconds ? hitsInARow + 1 : 1;
            lastHitTakenAt = Now;
            bool knockedDown = hitsInARow >= config.hitsToKnockDown;
            MatchDirector.Instance.ServerOnHit(attacker, this, knockedDown);
            if (knockedDown)
            {
                ServerKnockDown(direction);
                attacker.BonkRpc(where);
                return true;
            }

            StaggeredUntil.Value = Now + config.hitStaggerSeconds;
            ServerStopSwinging();
            StaggerRpc(direction);
            attacker.PowRpc(where);
            return true;
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
            StaggeredUntil.Value = 0;
            hitsInARow = 0;
            ServerStopSwinging();

            CargoItem carried = CarriedCargo;
            if (carried != null) carried.ServerDropAll(direction);
            MatchDirector.Instance.ServerOnKnockedDown(this, carried != null);
            KnockbackRpc(direction);
        }

        public void ServerSetCarry(CargoItem cargo)
        {
            CarriedCargoId.Value = cargo.NetworkObjectId;
            ServerStopSwinging();
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
            ServerStopSwinging();
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
        private void PowRpc(Vector3 position)
        {
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(position, GameText.Pow, new Color(1f, 0.95f, 0.6f), 0.9f);
            GameAudio.PlayAt("punch", position);
            FxBurst.Play(position, Color.white, 6);
            FxBurst.PlayFlash(position, new Color(1f, 0.95f, 0.7f), 0.9f);
            FollowCamera.Shake(position, 0.35f);
        }

        [Rpc(SendTo.Everyone)]
        private void BonkRpc(Vector3 position)
        {
            if (IsOwner) dashUntil = 0f;
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(position, GameText.Bonk, new Color(1f, 0.9f, 0.2f), 1.5f);
            GameAudio.PlayAt("bonk", position);
            FxBurst.Play(position, Color.white, 14);
            FxBurst.PlayFlash(position, new Color(1f, 0.92f, 0.45f), 1.5f);
            FollowCamera.Shake(position, 0.9f);
        }
    }
}
