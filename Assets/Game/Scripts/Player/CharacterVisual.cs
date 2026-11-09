using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Plays the character's animations. It runs on every computer from replicated state, so nothing
    /// here affects the rules. The clips come from Tools/blender/make_character.py and the state
    /// machine is Assets/Game/Animation/Character.controller.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class CharacterVisual : MonoBehaviour
    {
        private const int UpperBodyLayer = 1;
        private const int UpperCarry = 1;
        private const int UpperSmash = 2;
        private const int UpperToss = 3;
        private const int UpperCarryBig = 4;
        private const int UpperPunch = 5;
        private const int UpperFlinch = 6;
        private const int MoodCheer = 1;
        private const int MoodGrumble = 2;

        /// <summary>Where the blow lands in the Smash and Punch clips, as a fraction of their length.</summary>
        private const float SwingHitTime = 0.55f;
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
        private static readonly int PunchState = Animator.StringToHash("Punch");

        [Header("Model")]
        public Transform pivot;
        public Animator animator;
        public Renderer[] allRenderers;
        [Tooltip("How many times faster than the clip the Run animation plays at full speed.")]
        public float runRate = 2.6f;

        [Header("Gear")]
        public GameObject collectorHat;
        public GameObject interrupterHat;
        public GameObject hammer;
        public Transform handLeft;
        public Transform handRight;

        [Header("Face")]
        public Transform eyeLeft;
        public Transform eyeRight;
        public GameObject dizzyStars;
        public Transform starsBone;

        private PlayerAvatar avatar;
        private GameConfig config;
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
        private float nextBlink;
        private float blinkUntil;
        private float lastSwingCycle;
        private bool colorsApplied;
        private bool wasKnockedDown;
        private bool bodyHidden;
        private CargoItem lastCarried;

        /// <summary>How far through a swing the character is, from 0 to 1. The first-person hands follow this.</summary>
        public float SwingCycle { get; private set; }

        /// <summary>How many swings since it started swinging. In first person, the hands take turns punching.</summary>
        public int SwingIndex { get; private set; }

        /// <summary>Where held cargo goes for anyone watching: between the hands, against the belly.</summary>
        public Vector3 CarryPoint(float cargoHalfDepth)
        {
            Vector3 hands = (handLeft.position + handRight.position) * 0.5f;
            return hands + transform.forward * (cargoHalfDepth - 0.05f);
        }

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            config = GameConfig.Get();
            lastPosition = transform.position;
            lastYaw = transform.eulerAngles.y;
            nextBlink = Time.time + Random.Range(1f, 4f);
            animator.SetFloat(KnockedRateParam, KnockedClipSeconds / config.knockdownSeconds);
        }

        private void Update()
        {
            if (!avatar.IsSpawned) return;
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

            float topSpeed = config.runSpeed * (carried != null ? carried.Definition.carrySpeedMultiplier : 1f);
            float speed = Mathf.Clamp01(smoothedSpeed / topSpeed);
            float pace = Mathf.Max(0.55f, smoothedSpeed / config.runSpeed);
            animator.SetFloat(SpeedParam, speed, 0.05f, deltaTime);
            animator.SetFloat(RateParam, Mathf.Lerp(1f, runRate * pace, speed));
            animator.SetInteger(CarryParam, coCarrying ? 2 : carried != null ? 1 : 0);
            animator.SetBool(DashParam, avatar.IsDashing);
            animator.SetBool(KnockedParam, knockedDown);
            animator.SetInteger(MoodParam, CurrentMood());

            UpdateUpperBody(carried, coCarrying, knockedDown, deltaTime);
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

            int pose = 0;
            if (!knockedDown)
            {
                if (avatar.IsStaggered) pose = UpperFlinch;
                else if (avatar.Swinging.Value) pose = avatar.Side == Side.Interrupter ? UpperSmash : UpperPunch;
                else if (carried != null && !coCarrying) pose = carried.Definition.size == CargoSize.Small ? UpperCarry : UpperCarryBig;
                else if (Time.time < tossUntil) pose = UpperToss;
            }
            if (pose != 0) animator.SetInteger(UpperPoseParam, pose);

            float target = pose != 0 ? 1f : 0f;
            float speed = knockedDown ? 30f : pose != 0 ? 12f : 6f;
            upperWeight = Mathf.MoveTowards(upperWeight, target, speed * deltaTime);
            animator.SetLayerWeight(UpperBodyLayer, upperWeight);

            if (pose == UpperSmash || pose == UpperPunch)
            {
                CheckSwingLanding();
            }
            else
            {
                lastSwingCycle = 0f;
                SwingCycle = 0f;
                SwingIndex = 0;
            }
        }

        /// <summary>Sound and debris happen when the blow lands, not whenever durability ticks down.</summary>
        private void CheckSwingLanding()
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(UpperBodyLayer);
            if (state.shortNameHash != SmashState && state.shortNameHash != PunchState) return;
            float cycle = Mathf.Repeat(state.normalizedTime, 1f);
            SwingCycle = cycle;
            SwingIndex = Mathf.FloorToInt(state.normalizedTime);
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
            UpdateBlink();
        }

        /// <summary>In first person your own body would be in the way, but it still casts its shadow.</summary>
        private void UpdateBodyHidden()
        {
            bool hide = avatar == PlayerAvatar.Local && FollowCamera.HidesLocalBody;
            if (hide == bodyHidden) return;
            bodyHidden = hide;
            UnityEngine.Rendering.ShadowCastingMode mode = hide
                ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly
                : UnityEngine.Rendering.ShadowCastingMode.On;
            foreach (Renderer part in allRenderers) part.shadowCastingMode = mode;
        }

        /// <summary>A little extra life on top of the clips: lean into turns, stretch in a ram, squash on impacts.</summary>
        private void UpdatePivot()
        {
            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            float yaw = transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(lastYaw, yaw) / deltaTime;
            lastYaw = yaw;
            float run = Mathf.Clamp01(smoothedSpeed / config.runSpeed);
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
            foreach (Renderer part in allRenderers)
            {
                Material[] slots = part.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i] == config.orangeMaterial) slots[i] = config.blueMaterial;
                }
                part.sharedMaterials = slots;
            }
        }

        private void UpdateGear()
        {
            MatchDirector director = MatchDirector.Instance;
            if (director != null)
            {
                bool collector = avatar.Side == Side.Collector;
                if (collectorHat.activeSelf != collector) collectorHat.SetActive(collector);
                if (interrupterHat.activeSelf == collector) interrupterHat.SetActive(!collector);
            }

            bool hammerOut = avatar.Swinging.Value && !avatar.IsKnockedDown && avatar.Side == Side.Interrupter;
            if (hammer.activeSelf != hammerOut) hammer.SetActive(hammerOut);

            // Flicker during the short immunity after getting up, so attackers can see it.
            bool visible = avatar.IsKnockedDown || !avatar.IsImmune || Mathf.Repeat(Time.time * 9f, 1f) > 0.35f;
            foreach (Renderer part in allRenderers)
            {
                if (part.enabled != visible) part.enabled = visible;
            }
        }

        private void UpdateFace()
        {
            bool dizzy = avatar.IsKnockedDown && Time.time - knockedAt > 0.3f;
            if (dizzyStars.activeSelf != dizzy) dizzyStars.SetActive(dizzy);
            if (!dizzy) return;
            starsAngle += 300f * Time.deltaTime;
            starsBone.localRotation *= Quaternion.AngleAxis(starsAngle, Vector3.up);
        }

        private void UpdateBlink()
        {
            if (Time.time >= nextBlink)
            {
                blinkUntil = Time.time + 0.11f;
                nextBlink = Time.time + Random.Range(1.8f, 5f);
            }
            if (Time.time >= blinkUntil || avatar.IsKnockedDown) return;

            // The animator has already set the eyes this frame, so this only applies for now.
            Vector3 lids = new Vector3(1f, 1f, 0.1f);
            eyeLeft.localScale = Vector3.Scale(eyeLeft.localScale, lids);
            eyeRight.localScale = Vector3.Scale(eyeRight.localScale, lids);
        }
    }
}
