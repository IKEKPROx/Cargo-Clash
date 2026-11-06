using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>Timer, scores, banners, results, and the labels drawn over the game world.</summary>
    public sealed class MatchHud : MonoBehaviour
    {
        private sealed class Popup
        {
            public Text Label;
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

        [Header("Centre")]
        public Text bannerText;

        [Header("Bottom")]
        public Image ramFill;
        public Text ramLabel;

        [Header("World overlays")]
        public RectTransform overlayRoot;
        public Font overlayFont;

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

        private readonly List<Text> labelPool = new List<Text>();
        private readonly List<Image> barPool = new List<Image>();
        private readonly List<Image> arrowPool = new List<Image>();
        private readonly List<Popup> popups = new List<Popup>();
        private int labelsUsed;
        private int barsUsed;
        private int arrowsUsed;
        private Camera worldCamera;
        private bool resultsShown;
        private float lastThirtyShownAt = -100f;
        private bool lastThirtyDone;
        private bool whistlePlayed;
        private MatchPhase lastPhase = MatchPhase.Loading;

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
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameAudio.SetMusic(null);
        }

        private void Update()
        {
            MatchDirector director = MatchDirector.Instance;
            if (director == null || !director.IsSpawned) return;

            UpdateTopBar(director);
            UpdateBanner(director);
            UpdateRamBar();
            UpdateMusic(director);
            UpdateResults(director);
            UpdatePopups();
            UpdatePause();

            BeginOverlays();
            DrawCargoOverlays();
            DrawPlayerOverlays();
            DrawPrompt(director);
            DrawArrows();
            EndOverlays();
        }

        private void UpdateTopBar(MatchDirector director)
        {
            float seconds = director.Phase.Value == MatchPhase.Playing
                ? director.SecondsLeft
                : director.Phase.Value == MatchPhase.HalfOutro ? 0f : GameConfig.Get().EffectiveHalfSeconds;
            int whole = Mathf.CeilToInt(seconds);
            timerText.text = $"{whole / 60}:{whole % 60:00}";
            timerText.color = seconds <= 30f && director.Phase.Value == MatchPhase.Playing
                ? new Color(1f, 0.35f, 0.25f)
                : Color.white;

            halfText.text = "HALF " + director.Half.Value;
            orangeScoreText.text = director.OrangeScore.Value.ToString();
            blueScoreText.text = director.BlueScore.Value.ToString();
            bool orangeCollects = director.CollectorTeam.Value == Team.Orange;
            orangeRoleText.text = orangeCollects ? "COLLECTORS" : "INTERRUPTERS";
            blueRoleText.text = orangeCollects ? "INTERRUPTERS" : "COLLECTORS";
        }

        private void UpdateBanner(MatchDirector director)
        {
            MatchPhase phase = director.Phase.Value;
            if (phase != lastPhase)
            {
                if (phase == MatchPhase.Playing) GameAudio.Play("whistle");
                if (phase == MatchPhase.HalfOutro) GameAudio.Play("whistle");
                lastPhase = phase;
            }

            switch (phase)
            {
                case MatchPhase.HalfIntro:
                    string goal = PlayerAvatar.Local != null && PlayerAvatar.Local.Side == Side.Interrupter
                        ? GameText.InterrupterGoal
                        : GameText.CollectorGoal;
                    string prefix = director.Half.Value > 1 ? GameText.SidesSwapped + "\n" : "";
                    bannerText.text = prefix + goal + "\n" + Mathf.CeilToInt(director.SecondsLeft);
                    break;
                case MatchPhase.HalfOutro:
                    bannerText.text = GameText.TimeUp;
                    break;
                case MatchPhase.Playing:
                    bool showLastThirty = !lastThirtyDone && director.SecondsLeft <= 30f;
                    if (showLastThirty)
                    {
                        lastThirtyDone = true;
                        lastThirtyShownAt = Time.time;
                    }
                    bannerText.text = Time.time - lastThirtyShownAt < 2f ? GameText.LastThirty : "";
                    break;
                default:
                    bannerText.text = "";
                    break;
            }
            if (phase == MatchPhase.HalfIntro) lastThirtyDone = false;
        }

        private void UpdateRamBar()
        {
            PlayerAvatar local = PlayerAvatar.Local;
            if (local == null) return;
            float cooldown = local.RamCooldown01;
            ramFill.fillAmount = 1f - cooldown;
            ramFill.color = cooldown <= 0f ? new Color(0.35f, 0.9f, 0.45f) : new Color(0.75f, 0.75f, 0.75f);
            ramLabel.text = cooldown <= 0f ? "RAM READY" : "RAM";
        }

        private void UpdateMusic(MatchDirector director)
        {
            if (director.Phase.Value == MatchPhase.Playing)
            {
                bool fast = director.SecondsLeft <= 30f;
                GameAudio.SetMusic("music", fast ? 1.22f : 1f);
                bool helicopterNear = director.SecondsLeft <= 12f;
                if (helicopterNear && !whistlePlayed)
                {
                    whistlePlayed = true;
                    GameAudio.Play("helicopter", 0.8f);
                }
            }
            else
            {
                whistlePlayed = false;
                if (director.Phase.Value == MatchPhase.Results) GameAudio.SetMusic(null);
            }
        }

        private void UpdateResults(MatchDirector director)
        {
            if (resultsShown || director.Results == null) return;
            resultsShown = true;
            resultsPanel.SetActive(true);
            GameAudio.Play("fanfare");

            int orange = director.OrangeScore.Value;
            int blue = director.BlueScore.Value;
            resultsHeading.text = orange == blue ? "DRAW!" : (orange > blue ? "ORANGE WINS!" : "BLUE WINS!");
            resultsHeading.color = orange == blue ? Color.white : (orange > blue ? Team.Orange.ToColor() : Team.Blue.ToColor());
            resultsScore.text = $"{orange}  :  {blue}";

            StringBuilder lines = new StringBuilder();
            foreach (ResultLine line in director.Results)
                lines.AppendLine($"{line.Title}:  {line.Player}  ({line.Value})");
            resultsTitles.text = lines.ToString();

            bool isHost = Unity.Netcode.NetworkManager.Singleton.IsServer;
            bool practice = RoomService.Instance.IsPractice;
            againButton.gameObject.SetActive(isHost);
            againLabel.text = practice ? "PLAY AGAIN" : "BACK TO LOBBY";
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

        // ---- Popups ----

        public void ShowPopup(Vector3 worldPosition, string text, Color color, float scale = 1f)
        {
            Camera view = WorldCamera();
            if (view == null) return;
            Vector3 screen = view.WorldToScreenPoint(worldPosition + Vector3.up * 1.2f);
            if (screen.z <= 0f) return;

            Popup popup = new Popup { Label = NewLabel("Popup"), Scale = scale };
            popup.Label.text = text;
            popup.Label.color = color;
            popup.Label.fontSize = 34;
            popup.Label.rectTransform.position = screen;
            popup.Label.gameObject.SetActive(true);
            popups.Add(popup);
        }

        private void UpdatePopups()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup popup = popups[i];
                popup.Age += Time.deltaTime;
                float t = popup.Age / 0.9f;
                if (t >= 1f)
                {
                    Destroy(popup.Label.gameObject);
                    popups.RemoveAt(i);
                    continue;
                }
                float pop = popup.Scale * (1f + 0.5f * Mathf.Sin(Mathf.Min(t * 6f, Mathf.PI)));
                RectTransform rect = popup.Label.rectTransform;
                rect.localScale = Vector3.one * pop;
                rect.position += Vector3.up * (70f * Time.deltaTime);
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
            arrowsUsed = 0;
        }

        private void EndOverlays()
        {
            for (int i = labelsUsed; i < labelPool.Count; i++)
                if (labelPool[i].gameObject.activeSelf) labelPool[i].gameObject.SetActive(false);
            for (int i = barsUsed; i < barPool.Count; i++)
                if (barPool[i].gameObject.activeSelf) barPool[i].gameObject.SetActive(false);
            for (int i = arrowsUsed; i < arrowPool.Count; i++)
                if (arrowPool[i].gameObject.activeSelf) arrowPool[i].gameObject.SetActive(false);
        }

        private void DrawCargoOverlays()
        {
            Camera view = WorldCamera();
            if (view == null) return;
            foreach (CargoItem cargo in CargoItem.All)
            {
                CargoState state = cargo.State.Value;
                if (state == CargoState.Destroyed || state == CargoState.Rescued) continue;

                Vector3 screen = view.WorldToScreenPoint(cargo.transform.position + Vector3.up * (cargo.Height * 0.5f + 0.35f));
                if (screen.z <= 0f) continue;

                Text label = PoolLabel();
                label.text = cargo.Definition.value.ToString();
                label.color = new Color(1f, 0.93f, 0.35f);
                label.fontSize = cargo.Definition.size == CargoSize.Large ? 30 : cargo.Definition.size == CargoSize.Medium ? 22 : 16;
                label.rectTransform.position = screen;

                if (state == CargoState.Ground && cargo.Durability.Value < 1f)
                    DrawBar(screen + Vector3.up * 24f, cargo.Durability.Value);
            }
        }

        private void DrawPlayerOverlays()
        {
            Camera view = WorldCamera();
            if (view == null) return;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                // Names sit at the feet so they never cover the value of carried cargo.
                Vector3 screen = view.WorldToScreenPoint(avatar.transform.position);
                if (screen.z <= 0f) continue;

                Text label = PoolLabel();
                bool isLocal = avatar == PlayerAvatar.Local;
                label.text = isLocal ? "YOU" : avatar.DisplayName;
                label.color = avatar.Team.ToColor();
                label.fontSize = isLocal ? 22 : 18;
                label.rectTransform.position = screen + Vector3.down * 18f;
            }
        }

        /// <summary>Tells new players what they can do with the cargo next to them.</summary>
        private void DrawPrompt(MatchDirector director)
        {
            PlayerAvatar local = PlayerAvatar.Local;
            Camera view = WorldCamera();
            if (local == null || view == null || director.Phase.Value != MatchPhase.Playing || local.IsKnockedDown) return;

            string message;
            Vector3 anchor;
            CargoItem carried = local.CarriedCargo;
            if (carried != null)
            {
                bool waiting = carried.Definition.requiredCarriers > 1 && carried.HasFreeSlot;
                if (!waiting && local.Side != Side.Collector) return;
                message = waiting ? GameText.PromptWaiting : GameText.PromptToTruck;
                anchor = local.transform.position;
            }
            else
            {
                GameConfig config = GameConfig.Get();
                CargoItem nearby = CargoItem.FindGrabbable(local.transform.position, config.pickupRange);
                if (nearby == null) return;
                if (local.Side == Side.Interrupter && nearby.State.Value == CargoState.Ground) message = GameText.PromptSmash;
                else message = nearby.Definition.requiredCarriers > 1 ? GameText.PromptLarge : GameText.PromptPickUp;
                anchor = nearby.transform.position;
            }

            Vector3 screen = view.WorldToScreenPoint(anchor);
            if (screen.z <= 0f) return;
            Text label = PoolLabel();
            label.text = message;
            label.color = Color.white;
            label.fontSize = 20;
            label.rectTransform.position = screen + Vector3.down * 44f;
        }

        private void DrawArrows()
        {
            Camera view = WorldCamera();
            if (view == null) return;

            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.Definition.size != CargoSize.Large) continue;
                CargoState state = cargo.State.Value;
                if (state == CargoState.Destroyed || state == CargoState.Rescued) continue;
                DrawEdgeArrow(view, cargo.transform.position, new Color(1f, 0.82f, 0.2f));
            }

            PlayerAvatar local = PlayerAvatar.Local;
            LevelLayout layout = LevelLayout.Instance;
            if (local != null && layout != null && local.Side == Side.Collector)
                DrawEdgeArrow(view, layout.truckZone.position, new Color(0.4f, 1f, 0.5f));
        }

        private void DrawEdgeArrow(Camera view, Vector3 worldPosition, Color color)
        {
            Vector3 viewport = view.WorldToViewportPoint(worldPosition);
            bool onScreen = viewport.z > 0f && viewport.x > 0.03f && viewport.x < 0.97f && viewport.y > 0.03f && viewport.y < 0.97f;
            if (onScreen) return;

            Vector2 centre = new Vector2(0.5f, 0.5f);
            Vector2 direction = new Vector2(viewport.x, viewport.y) - centre;
            if (viewport.z < 0f) direction = -direction;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();

            float scale = Mathf.Min(0.44f / Mathf.Max(0.001f, Mathf.Abs(direction.x)), 0.44f / Mathf.Max(0.001f, Mathf.Abs(direction.y)));
            Vector2 edge = centre + direction * scale;

            Image arrow = PoolArrow();
            arrow.color = color;
            RectTransform rect = arrow.rectTransform;
            rect.position = new Vector3(edge.x * Screen.width, edge.y * Screen.height, 0f);
            rect.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        private void DrawBar(Vector3 screenPosition, float fraction)
        {
            Image background = PoolBar();
            background.color = new Color(0f, 0f, 0f, 0.75f);
            background.rectTransform.position = screenPosition;
            background.rectTransform.sizeDelta = new Vector2(70f, 10f);
            background.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            Image fill = PoolBar();
            fill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.35f, 0.9f, 0.45f), fraction);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.position = screenPosition + Vector3.left * 33f * overlayRoot.lossyScale.x;
            fill.rectTransform.sizeDelta = new Vector2(66f * fraction, 6f);
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
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            Outline outline = item.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
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
                barPool.Add(image);
            }
            Image bar = barPool[barsUsed++];
            if (!bar.gameObject.activeSelf) bar.gameObject.SetActive(true);
            return bar;
        }

        private Image PoolArrow()
        {
            if (arrowsUsed == arrowPool.Count)
            {
                GameObject item = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                item.transform.SetParent(overlayRoot, false);
                Image image = item.GetComponent<Image>();
                image.sprite = UiKit.TriangleSprite();
                image.raycastTarget = false;
                image.rectTransform.sizeDelta = new Vector2(44f, 44f);
                arrowPool.Add(image);
            }
            Image arrow = arrowPool[arrowsUsed++];
            if (!arrow.gameObject.activeSelf) arrow.gameObject.SetActive(true);
            return arrow;
        }
    }
}
