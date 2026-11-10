using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// Everything drawn over the game: the scoreboard, crosshair and prompts, markers on things worth
    /// finding, what happens on screen when you're hit, and the results and pause screens.
    /// </summary>
    public sealed class MatchHud : MonoBehaviour
    {
        private static readonly Vector2 BannerInPlay = new Vector2(0f, 330f);
        private static readonly Vector2 BannerUnderShot = new Vector2(0f, -455f);
        private static readonly Color CargoGold = new Color(1f, 0.82f, 0.2f);
        private static readonly Color AimGrab = new Color(0.38f, 0.95f, 0.5f);
        private static readonly Color AimSmash = new Color(1f, 0.32f, 0.22f);
        private static readonly Color TruckGreen = new Color(0.4f, 1f, 0.5f);

        private const float CargoLabelRange = 20f;
        private const float NameLabelRange = 38f;
        private const float ThreatRange = 10f;
        private const float MarkerHideWithin = 13f;
        private const float HealthBarRange = 26f;

        private const int FeedLines = 4;
        private const float FeedSeconds = 5f;
        private const float FeedLineHeight = 40f;
        private const float StreakWindowSeconds = 7f;
        private const float StreakShowSeconds = 1.8f;

        private sealed class FeedLine
        {
            public Text Label;
            public float Age;
        }

        private sealed class Popup
        {
            public Text Label;
            public Vector3 World;
            public float Age;
            public float Scale;
        }

        public static MatchHud Instance { get; private set; }

        [Header("Top bar")]
        public Text timerText;
        public Text halfText;
        public Text orangeScoreText;
        public Text blueScoreText;
        public Text orangeRoleText;
        public Text blueRoleText;
        public GameObject orangeYou;
        public GameObject blueYou;
        public Text roleChip;
        public Image roleChipBack;

        [Header("Centre")]
        public Text bannerText;
        public GameObject introCard;
        public Text introHeading;
        public Text introGoal;
        public Text introCount;
        public Text introSwap;

        [Header("Crosshair")]
        public Image crosshairDot;
        public Image crosshairRing;
        public HudPrompt promptA;
        public HudPrompt promptB;
        public GameObject targetBar;
        public Image targetFill;

        [Header("Bottom")]
        [Tooltip("The ram bar and everything else that's hidden while the helicopter sequence plays.")]
        public GameObject playHud;
        public Image ramFill;
        public Text ramLabel;
        public Image healthFill;
        public Text healthLabel;

        [Header("Knockouts")]
        [Tooltip("Who knocked out whom. Each line is added here.")]
        public RectTransform feedRoot;
        public Text streakText;

        [Header("Being hit")]
        public Image speedLines;
        public Image vignette;
        public Image hitArrow;
        public RectTransform dizzyStars;

        [Header("Transitions")]
        public Image screenFade;

        [Header("World overlays")]
        public RectTransform overlayRoot;
        public Font overlayFont;
        public Sprite roundSprite;
        public Sprite diamondSprite;
        public Sprite chevronSprite;

        [Header("Results")]
        public GameObject resultsPanel;
        public Text resultsHeading;
        public Text resultsScore;
        public Text resultsTitles;
        public Text resultsWaiting;
        public Button againButton;
        public Text againLabel;
        public Button leaveButton;

        [Header("Pause")]
        public GameObject pausePanel;
        public Button pauseResumeButton;
        public Button pauseLeaveButton;
        public Slider lookSpeedSlider;

        private readonly List<Text> labelPool = new List<Text>();
        private readonly List<Image> barPool = new List<Image>();
        private readonly List<Image> markerPool = new List<Image>();
        private readonly List<Popup> popups = new List<Popup>();
        private readonly List<FeedLine> feed = new List<FeedLine>();
        private int labelsUsed;
        private int barsUsed;
        private int markersUsed;
        private Camera worldCamera;
        private bool resultsShown;
        private string timedBanner = "";
        private float timedBannerUntil;
        private bool finalStretchAnnounced;
        private bool helicopterAnnounced;
        private MatchPhase lastPhase = MatchPhase.Loading;
        private Vector3 hitFrom;
        private float hitAt = -100f;
        private float dashAmount;
        private float knockedAmount;
        private float hitMarkerAt = -100f;
        private bool hitMarkerBig;
        private string knockedOutBy = "";
        private int streak;
        private float lastKnockOutAt = -100f;

        /// <summary>True while the pointer should be free for clicking, and looking around should stop.</summary>
        public bool MenuOpen => pausePanel.activeSelf || resultsShown;

        /// <summary>True while the mouse should be steering the view rather than moving a pointer.</summary>
        public bool WantsCursorLocked => !MenuOpen && Application.isFocused && PlayerAvatar.Local != null;

        private float CanvasScale => overlayRoot.lossyScale.x;

        private void Awake()
        {
            Instance = this;
            resultsPanel.SetActive(false);
            bannerText.text = "";
            againButton.onClick.AddListener(HandleAgain);
            leaveButton.onClick.AddListener(() => RoomService.Instance.Leave());
            pausePanel.SetActive(false);
            pauseResumeButton.onClick.AddListener(() => pausePanel.SetActive(false));
            pauseLeaveButton.onClick.AddListener(() => RoomService.Instance.Leave());
            lookSpeedSlider.minValue = GameSettings.MinLookSpeed;
            lookSpeedSlider.maxValue = GameSettings.MaxLookSpeed;
            lookSpeedSlider.SetValueWithoutNotify(GameSettings.LookSpeed);
            lookSpeedSlider.onValueChanged.AddListener(value => GameSettings.LookSpeed = value);
            introCard.SetActive(false);
            promptA.Hide();
            promptB.Hide();
            targetBar.SetActive(false);
            streakText.text = "";
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameAudio.SetMusic(null);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            UpdateCursor();
            MatchDirector director = MatchDirector.Instance;
            if (director == null || !director.IsSpawned) return;

            UpdateTopBar(director);
            UpdateBanner(director);
            UpdateIntroCard(director);
            UpdateRamBar();
            UpdateHealthBar();
            UpdateFeed();
            UpdateMusic(director);
            UpdateResults(director);
            UpdatePopups();
            UpdatePause();
            UpdateFade(director);
            UpdateHitEffects();

            bool inPlay = director.Phase.Value == MatchPhase.Playing || director.Phase.Value == MatchPhase.HalfIntro;
            if (playHud.activeSelf != inPlay) playHud.SetActive(inPlay);
            UpdateCrosshair(director);

            BeginOverlays();
            if (inPlay)
            {
                DrawCargoOverlays();
                DrawPlayerOverlays();
                DrawMarkers();
                DrawThreats();
            }
            EndOverlays();
        }

        private void UpdateCursor()
        {
            if (AutoPilot.Active) return;
            bool locked = WantsCursorLocked;
            CursorLockMode mode = locked ? CursorLockMode.Locked : CursorLockMode.None;
            if (Cursor.lockState != mode) Cursor.lockState = mode;
            if (Cursor.visible == locked) Cursor.visible = !locked;
        }

        private void UpdateTopBar(MatchDirector director)
        {
            MatchPhase phase = director.Phase.Value;
            float seconds = phase == MatchPhase.Playing
                ? director.SecondsLeft
                : phase == MatchPhase.HalfIntro || phase == MatchPhase.Loading ? GameConfig.Get().EffectiveHalfSeconds : 0f;
            int whole = Mathf.CeilToInt(seconds);
            timerText.text = $"{whole / 60}:{whole % 60:00}";
            bool hurry = seconds <= GameConfig.Get().finalStretchSeconds && phase == MatchPhase.Playing;
            timerText.color = hurry ? new Color(1f, 0.42f, 0.3f) : Color.white;

            halfText.text = GameText.Halftime + director.Half.Value;
            orangeScoreText.text = director.OrangeScore.Value.ToString();
            blueScoreText.text = director.BlueScore.Value.ToString();
            bool orangeCollects = director.CollectorTeam.Value == Team.Orange;
            orangeRoleText.text = orangeCollects ? GameText.Collectors : GameText.Interrupters;
            blueRoleText.text = orangeCollects ? GameText.Interrupters : GameText.Collectors;

            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            bool orange = local.Team == Team.Orange;
            if (orangeYou.activeSelf != orange) orangeYou.SetActive(orange);
            if (blueYou.activeSelf == orange) blueYou.SetActive(!orange);
            bool collector = local.Side == Side.Collector;
            roleChip.text = collector ? GameText.CollectorGoal : GameText.InterrupterGoal;
            roleChipBack.color = local.Team.ToColor();
        }

        private void UpdateBanner(MatchDirector director)
        {
            MatchPhase phase = director.Phase.Value;
            GameConfig config = GameConfig.Get();
            bannerText.rectTransform.anchoredPosition = phase == MatchPhase.HalfOutro ? BannerUnderShot : BannerInPlay;
            if (phase != lastPhase)
            {
                if (phase == MatchPhase.Playing || phase == MatchPhase.HalfOutro) GameAudio.Play("whistle");
                if (phase == MatchPhase.HalfIntro)
                {
                    finalStretchAnnounced = false;
                    helicopterAnnounced = false;
                    timedBannerUntil = 0f;
                }
                lastPhase = phase;
            }

            switch (phase)
            {
                case MatchPhase.HalfOutro:
                    float sinceArrival = (float)(director.Now - director.HelicopterArrivesAt.Value);
                    bool lifting = sinceArrival >= config.helicopterLowerSeconds + config.helicopterClipSeconds;
                    if (lifting) bannerText.text = GameText.HelicopterLeaves(director.HalfRescued.Value);
                    else bannerText.text = director.CargoRanOut.Value ? GameText.NoCargoLeft : GameText.TimeUp;
                    break;
                case MatchPhase.Playing:
                    PlayerAvatar me = PlayerAvatar.Local;
                    if (me != null && me.KnockedOut.Value)
                    {
                        int back = Mathf.Max(1, Mathf.CeilToInt((float)(me.KnockedUntil.Value - director.Now)));
                        bannerText.text = GameText.KnockedOutBy(knockedOutBy, back);
                        break;
                    }
                    float left = director.SecondsLeft;
                    if (!finalStretchAnnounced && left <= config.finalStretchSeconds)
                    {
                        finalStretchAnnounced = true;
                        ShowTimedBanner(GameText.SecondsLeft(Mathf.CeilToInt(left)));
                    }
                    if (!helicopterAnnounced && left <= config.helicopterWarningSeconds)
                    {
                        helicopterAnnounced = true;
                        ShowTimedBanner(GameText.HelicopterIncoming);
                    }
                    bannerText.text = Time.time < timedBannerUntil ? timedBanner : "";
                    break;
                default:
                    bannerText.text = "";
                    break;
            }
        }

        private void UpdateIntroCard(MatchDirector director)
        {
            bool show = director.Phase.Value == MatchPhase.HalfIntro && PlayerAvatar.Local != null;
            if (introCard.activeSelf != show) introCard.SetActive(show);
            if (!show) return;

            PlayerAvatar local = PlayerAvatar.Local;
            bool collector = local.Side == Side.Collector;
            introHeading.text = collector ? GameText.CollectorRole : GameText.InterrupterRole;
            introHeading.color = local.Team.ToColor();
            introGoal.text = collector ? GameText.CollectorGoal : GameText.InterrupterGoal;
            introCount.text = Mathf.CeilToInt(director.SecondsLeft).ToString();
            bool swapped = director.Half.Value > 1;
            if (introSwap.gameObject.activeSelf != swapped) introSwap.gameObject.SetActive(swapped);
        }

        private void ShowTimedBanner(string message)
        {
            timedBanner = message;
            timedBannerUntil = Time.time + 2.2f;
        }

        /// <summary>Hides the reset between halves: black at the end of the first half, clear shortly after the next begins.</summary>
        private void UpdateFade(MatchDirector director)
        {
            const float fadeSeconds = 0.35f;
            float alpha = 0f;
            MatchPhase phase = director.Phase.Value;
            if (phase == MatchPhase.HalfIntro)
                alpha = 1f - (GameConfig.Get().halfIntroSeconds - director.SecondsLeft) / fadeSeconds;
            else if (phase == MatchPhase.HalfOutro && director.Half.Value == 1)
                alpha = 1f - director.SecondsLeft / fadeSeconds;

            alpha = Mathf.Clamp01(alpha);
            screenFade.color = new Color(0f, 0f, 0f, alpha);
            if (screenFade.enabled != alpha > 0f) screenFade.enabled = alpha > 0f;
        }

        private void UpdateRamBar()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            float cooldown = local.RamCooldown01;
            // The sliced picture needs some width, or its rounded ends turn inside out.
            ramFill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(0.09f, 1f, 1f - cooldown), 1f);
            ramFill.color = cooldown <= 0f ? new Color(0.35f, 0.85f, 0.42f) : new Color(0.6f, 0.65f, 0.75f);
            ramLabel.text = cooldown <= 0f ? GameText.RamReady : GameText.RamCooling;
        }

        private void UpdateHealthBar()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            float health = local.Health01;
            healthFill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(0.09f, 1f, health), 1f);
            Color color = Color.Lerp(new Color(0.95f, 0.22f, 0.18f), new Color(0.35f, 0.85f, 0.42f), Mathf.Clamp01(health * 1.6f - 0.2f));
            // It throbs when you're nearly out.
            if (health <= 0.34f) color = Color.Lerp(color, Color.white, 0.25f + Mathf.Sin(Time.time * 9f) * 0.25f);
            healthFill.color = color;
            healthLabel.text = local.Health.Value.ToString();
        }

        /// <summary>Called on every computer when someone's knocked out.</summary>
        public void ShowKnockOut(PlayerAvatar attacker, PlayerAvatar victim, Weapon weapon)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            string first = ColoredName(attacker);
            string second = ColoredName(victim);
            FeedLine line = new FeedLine { Label = NewLabel("Feed") };
            line.Label.rectTransform.SetParent(feedRoot, false);
            line.Label.rectTransform.pivot = new Vector2(1f, 0.5f);
            line.Label.alignment = TextAnchor.MiddleRight;
            line.Label.fontSize = 28;
            line.Label.color = Color.white;
            line.Label.supportRichText = true;
            line.Label.text = $"{first}  {GameText.KnockOutVerb(weapon)}  {second}";
            feed.Add(line);
            if (feed.Count > FeedLines)
            {
                Destroy(feed[0].Label.gameObject);
                feed.RemoveAt(0);
            }

            if (victim == local) knockedOutBy = attacker.DisplayName;
            if (attacker != local) return;

            streak = Time.time - lastKnockOutAt < StreakWindowSeconds ? streak + 1 : 1;
            lastKnockOutAt = Time.time;
            streakText.text = GameText.Streak(streak);
            GameAudio.Play("knockout", 0.7f);
        }

        private static string ColoredName(PlayerAvatar avatar) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(avatar.Team.ToColor())}>{avatar.DisplayName}</color>";

        private void UpdateFeed()
        {
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                FeedLine line = feed[i];
                line.Age += Time.deltaTime;
                if (line.Age >= FeedSeconds)
                {
                    Destroy(line.Label.gameObject);
                    feed.RemoveAt(i);
                }
            }
            for (int i = 0; i < feed.Count; i++)
            {
                FeedLine line = feed[i];
                line.Label.rectTransform.anchoredPosition = new Vector2(0f, -i * FeedLineHeight);
                Color color = line.Label.color;
                color.a = Mathf.Clamp01((FeedSeconds - line.Age) * 2f);
                line.Label.color = color;
            }

            float t = (Time.time - lastKnockOutAt) / StreakShowSeconds;
            bool show = t < 1f;
            if (streakText.enabled != show) streakText.enabled = show;
            if (!show) return;
            float pop = 1f + 0.55f * Mathf.Max(0f, 1f - t * 7f);
            streakText.rectTransform.localScale = Vector3.one * pop;
            Color streakColor = streakText.color;
            streakColor.a = Mathf.Clamp01((1f - t) * 4f);
            streakText.color = streakColor;
        }

        /// <summary>A blow of yours landed.</summary>
        public void ShowHitMarker(bool big)
        {
            hitMarkerAt = Time.time;
            hitMarkerBig = big;
            GameAudio.Play("tick", 0.55f);
        }

        private void UpdateMusic(MatchDirector director)
        {
            MatchPhase phase = director.Phase.Value;
            GameAudio.SetMusicDucked(phase == MatchPhase.HalfOutro);
            if (phase == MatchPhase.Playing)
            {
                bool fast = director.SecondsLeft <= GameConfig.Get().finalStretchSeconds;
                GameAudio.SetMusic("music", fast ? 1.22f : 1f);
            }
            else if (phase == MatchPhase.Results)
            {
                GameAudio.SetMusic(null);
            }
        }

        private void UpdateResults(MatchDirector director)
        {
            if (resultsShown || director.Results == null) return;
            resultsShown = true;
            pausePanel.SetActive(false);
            resultsPanel.SetActive(true);
            GameAudio.Play("fanfare");

            int orange = director.OrangeScore.Value;
            int blue = director.BlueScore.Value;
            resultsHeading.text = orange == blue ? GameText.Draw : (orange > blue ? GameText.OrangeWins : GameText.BlueWins);
            resultsHeading.color = orange == blue ? new Color(0.09f, 0.12f, 0.24f) : (orange > blue ? Team.Orange.ToColor() : Team.Blue.ToColor());
            resultsScore.text = $"{orange}  :  {blue}";

            StringBuilder lines = new StringBuilder();
            foreach (ResultLine line in director.Results)
                lines.AppendLine($"{line.Title}:  {line.Player}  ({line.Value})");
            resultsTitles.text = lines.ToString();

            bool isHost = Unity.Netcode.NetworkManager.Singleton.IsServer;
            bool practice = RoomService.Instance.IsPractice;
            againButton.transform.parent.gameObject.SetActive(isHost);
            againLabel.text = practice ? GameText.PlayAgain : GameText.BackToLobby;
            resultsWaiting.gameObject.SetActive(!isHost);
        }

        private void UpdatePause()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !resultsShown)
                pausePanel.SetActive(!pausePanel.activeSelf);
        }

        private void HandleAgain()
        {
            SessionRoster roster = SessionRoster.Instance;
            if (roster == null) return;
            if (RoomService.Instance.IsPractice) roster.ServerRestartMatch();
            else roster.ServerBackToLobby();
        }

        // ---- Crosshair and prompts ----

        private void UpdateCrosshair(MatchDirector director)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            bool playing = director.Phase.Value == MatchPhase.Playing && local != null && !local.IsKnockedDown;
            if (crosshairDot.enabled != playing) crosshairDot.enabled = playing;
            if (!playing)
            {
                crosshairRing.enabled = false;
                promptA.Hide();
                promptB.Hide();
                if (targetBar.activeSelf) targetBar.SetActive(false);
                return;
            }

            CargoItem carried = local.CarriedCargo;
            CargoItem target = null;
            if (carried != null)
            {
                ShowCarryPrompts(local, carried);
            }
            else
            {
                bool interrupter = local.Side == Side.Interrupter;
                target = interrupter && local.AimedSmash != null ? local.AimedSmash : local.AimedGrab;
                ShowAimPrompts(local, interrupter);
            }

            // Someone to hit comes before any cargo, the same as in the swing itself.
            bool aimsAtPerson = carried == null && local.AimedEnemy != null;
            if (aimsAtPerson) target = null;
            crosshairRing.enabled = target != null || aimsAtPerson;
            if (aimsAtPerson)
            {
                crosshairRing.rectTransform.localScale = Vector3.one;
                crosshairRing.color = AimSmash;
            }
            else if (target != null)
            {
                float sinceHit = Time.time - target.LastHurtTime;
                float punch = sinceHit < 0.16f ? 1f - sinceHit / 0.16f : 0f;
                crosshairRing.rectTransform.localScale = Vector3.one * (1f + punch * 0.45f);
                crosshairRing.color = local.Side == Side.Interrupter && local.AimedSmash != null ? AimSmash : AimGrab;
            }

            // The ring closes in as the hammer charges, and swims about while the vacuum pulls.
            if (local.IsCharging)
            {
                float charge = local.Charge01;
                crosshairRing.enabled = true;
                crosshairRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, charge);
                crosshairRing.color = charge >= 1f ? AimSmash : Color.white;
            }
            else if (local.Sucking.Value)
            {
                crosshairRing.enabled = true;
                crosshairRing.rectTransform.localScale = Vector3.one * (1.25f + Mathf.Sin(Time.time * 13f) * 0.12f);
                crosshairRing.color = new Color(0.55f, 0.9f, 1f);
            }

            // A blow of yours landing outranks anything else it was showing.
            float sinceMarker = (Time.time - hitMarkerAt) / 0.2f;
            if (sinceMarker < 1f)
            {
                crosshairRing.enabled = true;
                crosshairRing.rectTransform.localScale = Vector3.one * (hitMarkerBig ? Mathf.Lerp(2f, 1.2f, sinceMarker) : Mathf.Lerp(1.6f, 1.05f, sinceMarker));
                crosshairRing.color = hitMarkerBig ? new Color(1f, 0.85f, 0.25f) : Color.white;
            }

            bool personBar = aimsAtPerson;
            bool cargoBar = target != null && target.State.Value == CargoState.Ground && (target.Durability.Value < 1f || local.Swinging.Value);
            bool showBar = personBar || cargoBar;
            if (targetBar.activeSelf != showBar) targetBar.SetActive(showBar);
            if (showBar)
            {
                float fraction = personBar ? local.AimedEnemy.Health01 : target.Durability.Value;
                targetFill.rectTransform.anchorMax = new Vector2(Mathf.Lerp(0.05f, 1f, fraction), 1f);
                targetFill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.42f), fraction);
            }
        }

        private void ShowCarryPrompts(PlayerAvatar local, CargoItem carried)
        {
            bool large = carried.Definition.requiredCarriers > 1;
            bool waiting = large && carried.HasFreeSlot;
            promptA.Show(GameText.KeyInteract, large ? GameText.PromptLetGo : GameText.PromptDrop);
            if (waiting) promptB.Show(null, GameText.PromptWaiting);
            else if (large) promptB.Show(null, GameText.PromptTogether);
            else if (local.Side == Side.Collector) promptB.Show(null, GameText.PromptToTruck);
            else promptB.Hide();
        }

        private void ShowAimPrompts(PlayerAvatar local, bool interrupter)
        {
            CargoItem grab = local.AimedGrab;
            CargoItem smash = local.AimedSmash;
            if (local.Sucking.Value)
            {
                promptA.Show(GameText.KeySecondary, GameText.PromptSucking);
                promptB.Hide();
                return;
            }
            if (local.IsCharging)
            {
                promptA.Show(GameText.KeySecondary, local.Charge01 >= 1f ? GameText.PromptSlamReady : GameText.PromptCharging);
                promptB.Hide();
                return;
            }
            if (local.AimedEnemy != null)
            {
                promptA.Show(GameText.KeyAttack, interrupter ? GameText.PromptHit : GameText.PromptBlast);
                if (grab != null) promptB.Show(GameText.KeyInteract, GameText.PromptCarry);
                else if (interrupter) promptB.Show(GameText.KeySecondary, GameText.PromptSlam);
                else promptB.Hide();
                return;
            }
            if (interrupter && smash != null)
            {
                promptA.Show(GameText.KeyAttack, GameText.PromptSmash);
                if (grab != null) promptB.Show(GameText.KeyInteract, GameText.PromptCarry);
                else promptB.Hide();
                return;
            }

            promptB.Hide();
            if (grab == null)
            {
                if (!interrupter && local.AimedSuck != null) promptA.Show(GameText.KeySecondary, GameText.PromptSuck);
                else promptA.Hide();
                return;
            }
            string words;
            if (grab.State.Value == CargoState.Carried) words = GameText.PromptHelp;
            else if (grab.Definition.requiredCarriers > 1) words = GameText.PromptLarge;
            else words = GameText.PromptPickUp;
            promptA.Show(GameText.KeyInteract, words);
        }

        // ---- Being hit ----

        /// <summary>Shows which way a hit came from, as a direction in the world.</summary>
        public void ShowHitFrom(Vector3 direction)
        {
            hitFrom = direction;
            hitAt = Time.time;
        }

        private void UpdateHitEffects()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            bool dashing = local != null && local.IsDashing && FollowCamera.ShowsHands;
            bool knocked = local != null && local.IsKnockedDown;
            dashAmount = Mathf.MoveTowards(dashAmount, dashing ? 1f : 0f, Time.deltaTime * (dashing ? 14f : 4f));
            knockedAmount = Mathf.MoveTowards(knockedAmount, knocked ? 1f : 0f, Time.deltaTime * (knocked ? 9f : 3f));

            speedLines.color = new Color(1f, 1f, 1f, dashAmount * 0.75f);
            if (speedLines.enabled != dashAmount > 0f) speedLines.enabled = dashAmount > 0f;
            vignette.color = new Color(0.09f, 0.06f, 0.14f, knockedAmount * 0.85f);
            if (vignette.enabled != knockedAmount > 0f) vignette.enabled = knockedAmount > 0f;

            float sinceHit = Time.time - hitAt;
            bool showArrow = sinceHit < 1f && WorldCamera() != null;
            if (hitArrow.enabled != showArrow) hitArrow.enabled = showArrow;
            if (showArrow)
            {
                Transform eye = WorldCamera().transform;
                float angle = Mathf.DeltaAngle(eye.eulerAngles.y, Mathf.Atan2(hitFrom.x, hitFrom.z) * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                hitArrow.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 230f;
                hitArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
                hitArrow.color = new Color(1f, 0.25f, 0.2f, Mathf.Clamp01((1f - sinceHit) * 2.5f));
            }

            bool starsShow = knocked && FollowCamera.ShowsHands;
            if (dizzyStars.gameObject.activeSelf != starsShow) dizzyStars.gameObject.SetActive(starsShow);
            if (starsShow) SpinStars();
        }

        private void SpinStars()
        {
            int count = dizzyStars.childCount;
            for (int i = 0; i < count; i++)
            {
                float angle = Time.time * 3.4f + i * Mathf.PI * 2f / count;
                RectTransform star = (RectTransform)dizzyStars.GetChild(i);
                star.anchoredPosition = new Vector2(Mathf.Cos(angle) * 240f, Mathf.Sin(angle) * 62f);
                star.localScale = Vector3.one * (0.8f + Mathf.Sin(angle) * 0.25f);
                star.localRotation = Quaternion.Euler(0f, 0f, angle * 70f);
            }
        }

        // ---- Popups ----

        public void ShowPopup(Vector3 worldPosition, string text, Color color, float scale = 1f)
        {
            Popup popup = new Popup { Label = NewLabel("Popup"), Scale = scale, World = worldPosition + Vector3.up * 1.2f };
            popup.Label.text = text;
            popup.Label.color = color;
            popup.Label.fontSize = 46;
            popups.Add(popup);
        }

        private void UpdatePopups()
        {
            Camera view = WorldCamera();
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup popup = popups[i];
                popup.Age += Time.deltaTime;
                float t = popup.Age / 0.9f;
                if (t >= 1f || view == null)
                {
                    Destroy(popup.Label.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }

                // It stays over the place it happened, so it can be read while the view turns.
                Vector3 screen = view.WorldToScreenPoint(popup.World + Vector3.up * (t * 0.9f));
                bool visible = screen.z > 0f;
                if (popup.Label.enabled != visible) popup.Label.enabled = visible;
                float distance = Mathf.Max(screen.z, 0.1f);
                float size = Mathf.Clamp(14f / distance, 0.45f, 1.5f);
                float pop = popup.Scale * size * (1f + 0.5f * Mathf.Sin(Mathf.Min(t * 6f, Mathf.PI)));
                RectTransform rect = popup.Label.rectTransform;
                rect.position = screen;
                rect.localScale = Vector3.one * pop;
                Color color = popup.Label.color;
                color.a = 1f - t * t;
                popup.Label.color = color;
            }
        }

        // ---- World overlays ----

        private Camera WorldCamera()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            return worldCamera;
        }

        private void BeginOverlays()
        {
            labelsUsed = 0;
            barsUsed = 0;
            markersUsed = 0;
        }

        private void EndOverlays()
        {
            for (int i = labelsUsed; i < labelPool.Count; i++)
                if (labelPool[i].gameObject.activeSelf) labelPool[i].gameObject.SetActive(false);
            for (int i = barsUsed; i < barPool.Count; i++)
                if (barPool[i].gameObject.activeSelf) barPool[i].gameObject.SetActive(false);
            for (int i = markersUsed; i < markerPool.Count; i++)
                if (markerPool[i].gameObject.activeSelf) markerPool[i].gameObject.SetActive(false);
        }

        /// <summary>Things shrink with distance on screen, and so do their labels, but not so far they can't be read.</summary>
        private static float LabelScale(float distance, float fullSizeWithin)
        {
            return Mathf.Clamp(fullSizeWithin / Mathf.Max(distance, 0.1f), 0.5f, 1.1f);
        }

        private void DrawCargoOverlays()
        {
            Camera view = WorldCamera();
            PlayerAvatar local = PlayerAvatar.Local;
            if (view == null || local == null) return;
            CargoItem aimed = local.Side == Side.Interrupter && local.AimedSmash != null ? local.AimedSmash : local.AimedGrab;
            CargoItem held = local.CarriedCargo;
            foreach (CargoItem cargo in CargoItem.All)
            {
                CargoState state = cargo.State.Value;
                if (state == CargoState.Destroyed || state == CargoState.Rescued || cargo == held) continue;

                Vector3 world = cargo.transform.position + Vector3.up * (cargo.Height * 0.5f + 0.3f);
                Vector3 screen = view.WorldToScreenPoint(world);
                if (screen.z <= 0f || screen.z > CargoLabelRange) continue;

                float scale = LabelScale(screen.z, 6f);
                Text label = PoolLabel();
                label.text = cargo.Definition.value.ToString();
                label.color = cargo == aimed ? Color.white : CargoGold;
                label.fontSize = 34;
                label.rectTransform.position = screen;
                label.rectTransform.localScale = Vector3.one * (cargo == aimed ? scale * 1.25f : scale);

                // What you're aiming at has a bigger bar under the crosshair.
                if (state == CargoState.Ground && cargo.Durability.Value < 1f && cargo != aimed)
                    DrawBar(screen + Vector3.up * (30f * scale * CanvasScale), cargo.Durability.Value, scale);
            }
        }

        private void DrawPlayerOverlays()
        {
            Camera view = WorldCamera();
            if (view == null) return;
            PlayerAvatar local = PlayerAvatar.Local;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                if (avatar == local) continue;
                Vector3 screen = view.WorldToScreenPoint(avatar.transform.position + Vector3.up * 1.82f);
                if (screen.z <= 0f || screen.z > NameLabelRange) continue;

                Text label = PoolLabel();
                label.text = avatar.DisplayName;
                label.color = avatar.Team.ToColor();
                label.fontSize = 30;
                label.rectTransform.position = screen;
                float scale = LabelScale(screen.z, 7f);
                label.rectTransform.localScale = Vector3.one * scale;

                if (!avatar.KnockedOut.Value && screen.z <= HealthBarRange)
                    DrawBar(screen - Vector3.up * (26f * scale * CanvasScale), avatar.Health01, scale);
            }
        }

        /// <summary>The truck and the big chests, on screen or pointing from the edge. These are what you navigate by.</summary>
        private void DrawMarkers()
        {
            Camera view = WorldCamera();
            PlayerAvatar local = PlayerAvatar.Local;
            if (view == null || local == null) return;

            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.Definition.size != CargoSize.Large) continue;
                CargoState state = cargo.State.Value;
                if (state == CargoState.Destroyed || state == CargoState.Rescued || local.CarriedCargo == cargo) continue;
                DrawMarker(view, cargo.transform.position + Vector3.up * 1.5f, CargoGold, cargo.Definition.value.ToString(), 0.7f);
            }

            LevelLayout layout = LevelLayout.Instance;
            if (layout == null) return;
            bool collector = local.Side == Side.Collector;
            bool delivering = collector && local.CarriedCargo != null;
            float pulse = delivering ? 1f + Mathf.Sin(Time.time * 6f) * 0.12f : 0.85f;
            Color color = TruckGreen;
            color.a = collector ? 1f : 0.55f;
            // Standing next to it, you don't need telling where it is.
            Vector3 truck = layout.truckZone.position + Vector3.up * 3.6f;
            if ((truck - local.transform.position).sqrMagnitude > MarkerHideWithin * MarkerHideWithin)
                DrawMarker(view, truck, color, GameText.MarkerTruck, pulse);
        }

        private void DrawMarker(Camera view, Vector3 world, Color color, string text, float scale)
        {
            Vector3 viewport = view.WorldToViewportPoint(world);
            bool onScreen = viewport.z > 0f && viewport.x > 0.04f && viewport.x < 0.96f && viewport.y > 0.07f && viewport.y < 0.93f;
            int metres = Mathf.RoundToInt(Vector3.Distance(view.transform.position, world));
            Vector3 place;
            if (onScreen)
            {
                // Under the scoreboard, not behind it.
                place = new Vector3(viewport.x * Screen.width, Mathf.Min(viewport.y, 0.82f) * Screen.height, 0f);
                Image diamond = PoolMarker(diamondSprite);
                diamond.color = color;
                diamond.rectTransform.position = place;
                diamond.rectTransform.localRotation = Quaternion.identity;
                diamond.rectTransform.localScale = Vector3.one * (0.62f * scale);
            }
            else
            {
                place = PlaceOnEdge(view, world, out float angle);
                Image arrow = PoolMarker(chevronSprite);
                arrow.color = color;
                arrow.rectTransform.position = place;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                arrow.rectTransform.localScale = Vector3.one * (0.8f * scale);
            }

            Text label = PoolLabel();
            label.text = GameText.Metres(text, metres);
            label.color = color;
            label.fontSize = 26;
            // Above an arrow at the bottom of the screen, and below anything else.
            float under = place.y < Screen.height * 0.15f ? 46f : -36f;
            label.rectTransform.position = place + Vector3.up * (under * CanvasScale);
            label.rectTransform.localScale = Vector3.one * Mathf.Clamp(scale, 0.75f, 1.1f);
        }

        /// <summary>Red arrows for enemies close enough to get you, when they're not in view.</summary>
        private void DrawThreats()
        {
            Camera view = WorldCamera();
            PlayerAvatar local = PlayerAvatar.Local;
            if (view == null || local == null) return;
            foreach (PlayerAvatar enemy in PlayerAvatar.All)
            {
                if (enemy.Team == local.Team || enemy.IsKnockedDown) continue;
                float distance = Vector3.Distance(enemy.transform.position, local.transform.position);
                if (distance > ThreatRange) continue;
                Vector3 viewport = view.WorldToViewportPoint(enemy.transform.position + Vector3.up);
                bool inView = viewport.z > 0f && viewport.x > 0.06f && viewport.x < 0.94f && viewport.y > 0.06f && viewport.y < 0.94f;
                if (inView) continue;

                Vector3 place = PlaceOnEdge(view, enemy.transform.position + Vector3.up, out float angle);
                float closeness = 1f - distance / ThreatRange;
                Image arrow = PoolMarker(chevronSprite);
                arrow.color = new Color(1f, 0.25f, 0.2f, Mathf.Lerp(0.45f, 1f, closeness));
                arrow.rectTransform.position = place;
                arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                arrow.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.05f, closeness);
            }
        }

        /// <summary>Where on the screen's edge something out of view lies, measured in the camera's own space so behind you works too.</summary>
        private static Vector3 PlaceOnEdge(Camera view, Vector3 world, out float angle)
        {
            Vector3 local = view.transform.InverseTransformPoint(world);
            Vector2 direction = new Vector2(local.x, local.y);
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
            // Directly behind has no sideways part to go on, so it points down.
            if (local.z < 0f && Mathf.Abs(local.x) < 0.5f && Mathf.Abs(local.y) < 2f) direction = new Vector2(local.x, -1f);
            direction.Normalize();
            angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;

            float aspect = (float)Screen.width / Screen.height;
            const float margin = 0.42f;
            float across = Mathf.Abs(direction.x) * (1f / aspect);
            float up = Mathf.Abs(direction.y);
            float reach = margin / Mathf.Max(across, up, 0.001f);
            Vector2 edge = new Vector2(0.5f, 0.5f) + new Vector2(direction.x * reach / aspect, direction.y * reach);
            return new Vector3(edge.x * Screen.width, edge.y * Screen.height, 0f);
        }

        private void DrawBar(Vector3 screenPosition, float fraction, float scale)
        {
            float width = 92f * scale;
            float height = 14f * scale;
            Image background = PoolBar();
            background.color = new Color(0.09f, 0.12f, 0.24f, 0.9f);
            background.rectTransform.position = screenPosition;
            background.rectTransform.sizeDelta = new Vector2(width, height);
            background.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            float inside = (width - 6f) * fraction;
            Image fill = PoolBar();
            fill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.45f), fraction);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.position = screenPosition + Vector3.left * ((width * 0.5f - 3f) * CanvasScale);
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(inside, 2f), height - 6f);
        }

        private Text PoolLabel()
        {
            if (labelsUsed == labelPool.Count) labelPool.Add(NewLabel("Label"));
            Text label = labelPool[labelsUsed++];
            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            return label;
        }

        private Text NewLabel(string objectName)
        {
            GameObject item = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            item.transform.SetParent(overlayRoot, false);
            Text label = item.GetComponent<Text>();
            label.font = overlayFont;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            Outline outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0.09f, 0.12f, 0.24f, 0.95f);
            outline.effectDistance = new Vector2(2f, -2f);
            return label;
        }

        private Image PoolBar()
        {
            if (barsUsed == barPool.Count)
            {
                GameObject item = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(overlayRoot, false);
                Image image = item.GetComponent<Image>();
                image.raycastTarget = false;
                image.sprite = roundSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 6f;
                barPool.Add(image);
            }
            Image bar = barPool[barsUsed++];
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
            return bar;
        }

        private Image PoolMarker(Sprite sprite)
        {
            if (markersUsed == markerPool.Count)
            {
                GameObject item = new GameObject("Marker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(overlayRoot, false);
                Image image = item.GetComponent<Image>();
                image.raycastTarget = false;
                image.rectTransform.sizeDelta = new Vector2(64f, 64f);
                markerPool.Add(image);
            }
            Image marker = markerPool[markersUsed++];
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            if (marker.sprite != sprite) marker.sprite = sprite;
            return marker;
        }
    }
}
