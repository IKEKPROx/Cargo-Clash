using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Plays the character's animations. It runs on every computer from replicated state, so nothing
    /// here affects the rules. The models are Astra's (CrewModel, from Tools/blender/make_crew.py): one
    /// per body type, each with the porters' and the wreckers' clothes on the same skeleton and its own
    /// copy of the clips, dressed in the player's CrewLook. The state machines are
    /// Assets/Game/Animation/Crew_*.controller.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class CharacterVisual : MonoBehaviour
    {
        private const int UpperBodyLayer = 1;
        private const int UpperCarry = 1;
        private const int UpperSmash = 2;
        private const int UpperToss = 3;
        private const int UpperCarryBig = 4;
        private const int UpperFlinch = 6;
        private const int UpperReadyHammer = 7;
        private const int UpperReadyVacuum = 8;
        private const int UpperBlast = 9;
        private const int UpperCharge = 10;
        private const int UpperSlam = 11;
        private const int MoodCheer = 1;
        private const int MoodGrumble = 2;

        /// <summary>Where the blow lands in the Smash clip, as a fraction of its length.</summary>
        private const float SwingHitTime = 0.55f;
        /// <summary>Where the air comes out in the Blast clip, as a fraction of its length.</summary>
        private const float BlastTime = 0.18f;
        /// <summary>Where it starts coming down, for the sound of it going through the air.</summary>
        private const float SwingWhooshTime = 0.36f;
        private const float TossSeconds = 0.36f;
        private const float KnockedClipSeconds = 1.5f;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int RateParam = Animator.StringToHash("Rate");
        private static readonly int CarryParam = Animator.StringToHash("Carry");
        private static readonly int DashParam = Animator.StringToHash("Dash");
        private static readonly int KnockedParam = Animator.StringToHash("Knocked");
        private static readonly int KnockedRateParam = Animator.StringToHash("KnockedRate");
        private static readonly int MoodParam = Animator.StringToHash("Mood");
        private static readonly int UpperPoseParam = Animator.StringToHash("UpperPose");
        private static readonly int SmashState = Animator.StringToHash("Smash");
        private static readonly int BlastState = Animator.StringToHash("Blast");
        private static readonly int OutParam = Animator.StringToHash("Out");

        [Header("Model")]
        public Transform pivot;
        [Tooltip("One per body type. Only the one this player picked is switched on.")]
        public CrewModel[] crews;

        private PlayerAvatar avatar;
        private GameConfig config;
        private CrewModel look;
        private Animator animator;
        private CrewModel dressedModel;
        private CrewLook dressedLook;
        private Vector3 lastPosition;
        private float lastYaw;
        private float smoothedSpeed;
        private float bank;
        private float stretch;
        private float squash;
        private float upperWeight;
        private float starsAngle;
        private float knockedAt;
        private float tossUntil;
        private float lastSwingCycle;
        private bool colorsApplied;
        private bool wasKnockedDown;
        private bool bodyHidden;
        private CargoItem lastCarried;
        private float nextWispAt;
        private bool wasSucking;

        /// <summary>How far through a swing the character is, from 0 to 1. The first-person hands follow this.</summary>
        public float SwingCycle { get; private set; }

        /// <summary>Collectors wear the porters' clothes. Outside a match everyone does.</summary>
        private bool WearsPorter => MatchDirector.Instance == null || avatar.Side == Side.Collector;

        /// <summary>What this player's sleeves look like now, for the first-person arms.</summary>
        public Material SleeveMaterial => look == null ? null : WearsPorter ? look.porterSleeve : look.wreckerSleeve;

        /// <summary>Where the vacuum's air goes in and out, for the effects. On your own computer, that's the one in your hands.</summary>
        private Vector3 VacuumMouth
        {
            get
            {
                FirstPersonRig rig = FirstPersonRig.Instance;
                if (avatar == PlayerAvatar.Local && rig != null && rig.ShowsTool) return rig.VacuumMouthWorld;
                return look.handRight.position + transform.forward * 0.5f + Vector3.up * 0.1f;
            }
        }

        private Vector3 VacuumAim => avatar == PlayerAvatar.Local ? avatar.LookDirection : transform.forward;

        /// <summary>Where held cargo goes for anyone watching: between the hands, against the belly.</summary>
        public Vector3 CarryPoint(float cargoHalfDepth)
        {
            Vector3 hands = (look.handLeft.position + look.handRight.position) * 0.5f;
            return hands + transform.forward * (cargoHalfDepth - 0.05f);
        }

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            config = GameConfig.Get();
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            UseLook(BodyType.Medium);
        }

        /// <summary>Switches on this player's body type. The host sets it before spawning, so it's known from the start.</summary>
        private void UseLook(BodyType body)
        {
            CrewModel next = crews[0];
            foreach (CrewModel candidate in crews)
            {
                if (candidate.body == body) next = candidate;
            }
            foreach (CrewModel other in crews)
            {
                if (other.gameObject.activeSelf != (other == next)) other.gameObject.SetActive(other == next);
            }
            look = next;
            animator = next.animator;
            animator.SetFloat(KnockedRateParam, KnockedClipSeconds / config.knockdownSeconds);
            bodyHidden = false;
            ApplyShadows(false);
        }

        private void Update()
        {
            if (!avatar.IsSpawned) return;
            if (look.body != avatar.Body) UseLook(avatar.Body);
            if (dressedModel != look || !dressedLook.Equals(avatar.Look))
            {
                dressedModel = look;
                dressedLook = avatar.Look;
                look.Dress(dressedLook);
            }
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);

            Vector3 moved = transform.position - lastPosition;
            moved.y = 0f;
            lastPosition = transform.position;
            smoothedSpeed = Mathf.MoveTowards(smoothedSpeed, moved.magnitude / deltaTime, 40f * deltaTime);

            CargoItem carried = avatar.CarriedCargo;
            bool coCarrying = carried != null && carried.Definition.requiredCarriers > 1;
            bool knockedDown = avatar.IsKnockedDown;
            if (knockedDown && !wasKnockedDown) knockedAt = Time.time;
            if (wasKnockedDown && !knockedDown) squash = 1f;
            wasKnockedDown = knockedDown;

            float topSpeed = avatar.RunSpeed * (carried != null ? carried.Definition.carrySpeedMultiplier : 1f);
            float speed = Mathf.Clamp01(smoothedSpeed / topSpeed);
            float pace = Mathf.Max(0.55f, smoothedSpeed / avatar.RunSpeed);
            animator.SetFloat(SpeedParam, speed, 0.05f, deltaTime);
            animator.SetFloat(RateParam, Mathf.Lerp(1f, look.runRate * pace, speed));
            animator.SetInteger(CarryParam, coCarrying ? 2 : carried != null ? 1 : 0);
            animator.SetBool(DashParam, avatar.IsDashing);
            animator.SetBool(KnockedParam, knockedDown);
            animator.SetBool(OutParam, avatar.KnockedOut.Value);
            animator.SetInteger(MoodParam, CurrentMood());

            UpdateUpperBody(carried, coCarrying, knockedDown, deltaTime);
            UpdateSuction();
        }

        private void UpdateSuction()
        {
            bool sucking = avatar.Sucking.Value;
            if (avatar == PlayerAvatar.Local && sucking != wasSucking) GameAudio.SetLoop(sucking ? "suck" : null, 0.32f);
            wasSucking = sucking;
            if (!sucking || Time.time < nextWispAt) return;
            nextWispAt = Time.time + 0.025f;
            FxBurst.PlaySuction(VacuumMouth, VacuumAim);
        }

        /// <summary>Holding a tool the way you would in this half of the match, between the whistles and with nothing else in your hands.</summary>
        private bool ToolOut(CargoItem carried, bool knockedDown)
        {
            MatchDirector director = MatchDirector.Instance;
            if (director == null || carried != null || knockedDown || Time.time < tossUntil) return false;
            MatchPhase phase = director.Phase.Value;
            return phase == MatchPhase.Playing || phase == MatchPhase.HalfIntro;
        }

        /// <summary>The upper-body layer covers carrying and smashing, so the legs can keep running below it.</summary>
        private void UpdateUpperBody(CargoItem carried, bool coCarrying, bool knockedDown, float deltaTime)
        {
            if (carried != lastCarried)
            {
                squash = Mathf.Max(squash, 0.5f);
                bool rescued = lastCarried != null && lastCarried.State.Value == CargoState.Rescued;
                if (carried == null && rescued && lastCarried.Definition.requiredCarriers == 1)
                    tossUntil = Time.time + TossSeconds;
                lastCarried = carried;
            }

            // A ram is all in the base layer, so it shows through.
            int pose = 0;
            if (!knockedDown && !avatar.IsDashing)
            {
                if (avatar.IsStaggered) pose = UpperFlinch;
                else if (carried != null && !coCarrying) pose = carried.Definition.size == CargoSize.Small ? UpperCarry : UpperCarryBig;
                else if (Time.time < tossUntil) pose = UpperToss;
                else if (ToolOut(carried, knockedDown))
                {
                    if (avatar.Side == Side.Collector) pose = avatar.Swinging.Value ? UpperBlast : UpperReadyVacuum;
                    else if (avatar.IsSlamming) pose = UpperSlam;
                    else if (avatar.IsCharging) pose = UpperCharge;
                    else pose = avatar.Swinging.Value ? UpperSmash : UpperReadyHammer;
                }
            }
            if (pose != 0) animator.SetInteger(UpperPoseParam, pose);

            float target = pose != 0 ? 1f : 0f;
            float speed = knockedDown ? 30f : pose != 0 ? 12f : 6f;
            upperWeight = Mathf.MoveTowards(upperWeight, target, speed * deltaTime);
            animator.SetLayerWeight(UpperBodyLayer, upperWeight);

            if (pose == UpperSmash) CheckSwingLanding();
            else if (pose == UpperBlast) CheckBlast();
            else
            {
                lastSwingCycle = 0f;
                SwingCycle = 0f;
            }
        }

        /// <summary>The air comes out, whether or not it gets anyone. That part is up to the host.</summary>
        private void CheckBlast()
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);
            if (state.shortNameHash != BlastState) return;
            float cycle = Mathf.Repeat(state.normalizedTime, 1f);
            SwingCycle = cycle;
            bool fired = lastSwingCycle < BlastTime && cycle >= BlastTime;
            lastSwingCycle = cycle;
            if (!fired) return;

            Vector3 mouth = VacuumMouth;
            FxBurst.PlayBlast(mouth, VacuumAim);
            GameAudio.PlayAt("blast", mouth, 0.8f);
            squash = Mathf.Max(squash, 0.2f);
            if (avatar == PlayerAvatar.Local) FollowCamera.Kick(-1.3f);
        }

        /// <summary>Sound and debris happen when the blow lands, not whenever durability ticks down.</summary>
        private void CheckSwingLanding()
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);
            if (state.shortNameHash != SmashState) return;
            float cycle = Mathf.Repeat(state.normalizedTime, 1f);
            SwingCycle = cycle;
            if (lastSwingCycle < SwingWhooshTime && cycle >= SwingWhooshTime) GameAudio.PlayAt("swing", transform.position, 0.5f);
            bool landed = lastSwingCycle < SwingHitTime && cycle >= SwingHitTime;
            lastSwingCycle = cycle;
            if (!landed) return;

            squash = Mathf.Max(squash, 0.35f);
            if (avatar == PlayerAvatar.Local) FollowCamera.Kick(1.1f);

            // Whether it hurt someone is up to the host, and the effects come when it says so.
            if (avatar.HitTarget != null) return;

            CargoItem target = avatar.SmashTarget;
            if (target == null || target.State.Value != CargoState.Ground) return;

            // On the side of the cargo facing the hammer, so the flash isn't hidden inside it.
            Vector3 toHammer = transform.position - target.transform.position;
            toHammer.y = 0f;
            Vector3 away = toHammer.sqrMagnitude > 0.001f ? toHammer.normalized : transform.forward;
            Vector3 hit = target.transform.position + Vector3.up * (target.Height * 0.3f) + away * target.Radius;
            target.Bump(-away);
            GameAudio.PlayAt("smash", hit);
            FxBurst.Play(hit, target.Definition.chipColor, 8);
            FxBurst.PlayFlash(hit + Vector3.up * 0.1f, Color.white, 0.75f);
            if (avatar == PlayerAvatar.Local) FollowCamera.Kick(0.6f);
            FollowCamera.Shake(hit, avatar == PlayerAvatar.Local ? 0.42f : 0.18f);
        }

        private int CurrentMood()
        {
            MatchDirector director = MatchDirector.Instance;
            if (director == null) return 0;
            MatchPhase phase = director.Phase.Value;
            if (phase == MatchPhase.HalfOutro) return avatar.Side == Side.Collector ? MoodCheer : MoodGrumble;
            if (phase != MatchPhase.Results) return 0;

            int mine = avatar.Team == Team.Orange ? director.OrangeScore.Value : director.BlueScore.Value;
            int theirs = avatar.Team == Team.Orange ? director.BlueScore.Value : director.OrangeScore.Value;
            return mine >= theirs ? MoodCheer : MoodGrumble;
        }

        private void LateUpdate()
        {
            if (!avatar.IsSpawned) return;
            ApplyTeamColor();
            UpdateBodyHidden();
            UpdatePivot();
            UpdateGear();
            UpdateFace();
        }

        /// <summary>In first person your own body would be in the way, but it still casts its shadow.</summary>
        private void UpdateBodyHidden()
        {
            bool hide = avatar == PlayerAvatar.Local && FollowCamera.HidesLocalBody;
            if (hide == bodyHidden) return;
            bodyHidden = hide;
            ApplyShadows(hide);
        }

        private void ApplyShadows(bool shadowOnly)
        {
            UnityEngine.Rendering.ShadowCastingMode mode = shadowOnly
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (Renderer part in look.renderers) part.shadowCastingMode = mode;
        }

        /// <summary>A little extra life on top of the clips: lean into turns, stretch in a ram, squash on impacts.</summary>
        private void UpdatePivot()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            float yaw = transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(lastYaw, yaw) / deltaTime;
            lastYaw = yaw;
            float run = Mathf.Clamp01(smoothedSpeed / avatar.RunSpeed);
            bank = Mathf.MoveTowards(bank, Mathf.Clamp(-turnRate * 0.035f, -16f, 16f) * run, 90f * deltaTime);
            pivot.localRotation = Quaternion.Euler(0f, 0f, avatar.IsKnockedDown ? 0f : bank);

            stretch = Mathf.MoveTowards(stretch, avatar.IsDashing ? 0.14f : 0f, 2f * deltaTime);
            squash = Mathf.MoveTowards(squash, 0f, 3.5f * deltaTime);
            float wobble = Mathf.Sin(squash * 9f) * squash * 0.2f;
            pivot.localScale = new Vector3(1f + wobble - stretch, 1f - wobble + stretch, 1f + wobble);
        }

        private void ApplyTeamColor()
        {
            if (colorsApplied) return;
            colorsApplied = true;
            if (avatar.Team == Team.Orange) return;
            foreach (CrewModel each in crews)
            {
                foreach (Renderer part in each.renderers)
                {
                    Material[] slots = part.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] == config.orangeMaterial) slots[i] = config.blueMaterial;
                    }
                    part.sharedMaterials = slots;
                }
            }
        }

        private void UpdateGear()
        {
            // The sides swap at half-time, and everyone changes into the other company's clothes.
            look.Wear(WearsPorter);

            bool toolOut = ToolOut(avatar.CarriedCargo, avatar.IsKnockedDown);
            bool interrupter = avatar.Side == Side.Interrupter;
            if (look.hammer.activeSelf != (toolOut && interrupter)) look.hammer.SetActive(toolOut && interrupter);
            if (look.vacuum.activeSelf != (toolOut && !interrupter)) look.vacuum.SetActive(toolOut && !interrupter);

            // Flicker during the short immunity after getting up, so attackers can see it.
            bool visible = avatar.IsKnockedDown || !avatar.IsImmune || Mathf.Repeat(Time.time * 9f, 1f) > 0.35f;
            foreach (Renderer part in look.renderers)
            {
                if (part.enabled != visible) part.enabled = visible;
            }
        }

        private void UpdateFace()
        {
            bool dizzy = avatar.IsKnockedDown && Time.time - knockedAt > 0.3f;
            if (look.dizzyStars.activeSelf != dizzy) look.dizzyStars.SetActive(dizzy);
            if (!dizzy) return;
            starsAngle += 300f * Time.deltaTime;
            look.starsBone.localRotation *= Quaternion.AngleAxis(starsAngle, Vector3.up);
        }
    }
}
