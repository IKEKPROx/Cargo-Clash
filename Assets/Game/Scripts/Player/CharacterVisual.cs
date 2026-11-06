using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Procedural animation for the round little characters: waddle, lean, fall over, swing the
    /// hammer. It runs on every computer from replicated state, so nothing here affects the rules.
    /// Replace it with an Animator once real animation clips exist.
    /// </summary>
    [RequireComponent(typeof(PlayerAvatar))]
    public sealed class CharacterVisual : MonoBehaviour
    {
        public Transform pivot;
        public Renderer[] teamRenderers;
        public Renderer[] allRenderers;
        public GameObject collectorHat;
        public GameObject interrupterHat;
        public Transform hammer;

        private PlayerAvatar avatar;
        private GameConfig config;
        private Vector3 lastPosition;
        private float smoothedSpeed;
        private float walkPhase;
        private float fall;
        private float squash;
        private bool colorsApplied;
        private bool wasKnockedDown;

        private void Awake()
        {
            avatar = GetComponent<PlayerAvatar>();
            config = GameConfig.Get();
            lastPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (!avatar.IsSpawned) return;
            ApplyTeamColor();

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            lastPosition = transform.position;
            smoothedSpeed = Mathf.MoveTowards(smoothedSpeed, delta.magnitude / deltaTime, 40f * deltaTime);
            float run = Mathf.Clamp01(smoothedSpeed / config.runSpeed);
            walkPhase += deltaTime * (5f + 11f * run);

            bool knockedDown = avatar.IsKnockedDown;
            if (wasKnockedDown && !knockedDown) squash = 1f;
            wasKnockedDown = knockedDown;
            fall = Mathf.MoveTowards(fall, knockedDown ? 1f : 0f, deltaTime * (knockedDown ? 9f : 5f));
            squash = Mathf.MoveTowards(squash, 0f, deltaTime * 3.5f);

            float upright = 1f - fall;
            float waddle = Mathf.Sin(walkPhase) * 11f * run * upright;
            float bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.16f * run * upright;
            float lean = (avatar.IsDashing ? 30f : run * 7f) * upright;
            pivot.localRotation = Quaternion.Euler(lean - 88f * fall, 0f, waddle);
            pivot.localPosition = new Vector3(0f, bob + 0.28f * fall, 0f);

            float stretch = avatar.IsDashing ? 0.12f : 0f;
            float wobbleScale = Mathf.Sin(squash * 9f) * squash * 0.22f;
            pivot.localScale = new Vector3(1f + wobbleScale - stretch, 1f - wobbleScale + stretch, 1f + wobbleScale);

            UpdateHats();
            UpdateHammer();
            UpdateBlink(knockedDown);
        }

        private void ApplyTeamColor()
        {
            if (colorsApplied) return;
            colorsApplied = true;
            Material material = avatar.Team == Team.Orange ? config.orangeMaterial : config.blueMaterial;
            foreach (Renderer part in teamRenderers) part.sharedMaterial = material;
        }

        private void UpdateHats()
        {
            MatchDirector director = MatchDirector.Instance;
            if (director == null) return;
            bool collector = avatar.Side == Side.Collector;
            if (collectorHat.activeSelf != collector) collectorHat.SetActive(collector);
            if (interrupterHat.activeSelf == collector) interrupterHat.SetActive(!collector);
        }

        private void UpdateHammer()
        {
            bool smashing = avatar.Smashing.Value;
            if (hammer.gameObject.activeSelf != smashing) hammer.gameObject.SetActive(smashing);
            if (smashing) hammer.localRotation = Quaternion.Euler(20f + Mathf.Sin(Time.time * 24f) * 60f, 0f, 0f);
        }

        private void UpdateBlink(bool knockedDown)
        {
            // Flicker during the short immunity after getting up, so attackers can see it.
            bool visible = knockedDown || !avatar.IsImmune || Mathf.Repeat(Time.time * 9f, 1f) > 0.35f;
            foreach (Renderer part in allRenderers)
            {
                if (part.enabled != visible) part.enabled = visible;
            }
        }
    }
}
