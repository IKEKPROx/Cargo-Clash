using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// The screens of the start scene: title, main menu, level select, crew page and a room's lobby.
    /// Practice and creating a room both go through the level select. Joining doesn't, because the host picks.
    /// The crew page is where players pick their body type, hat, glasses and face: from the menu, from the
    /// lobby, and on the way into practice, where its button starts the match.
    /// </summary>
    public sealed class TitleFlow : MonoBehaviour
    {
        private enum Page { Title, Menu, Levels, Lobby, Crew }
        private enum Plan { Practice, CreateRoom }

        // Once someone has pressed Play, coming back from a match goes straight to the menu.
        private static bool pastTitle;

        public CargoClashStartMenu stage;

        [Header("Title")]
        public GameObject titlePanel;
        public Button playButton;

        [Header("Main menu")]
        public GameObject menuPanel;
        public Button practiceButton;
        public Button createButton;
        public Button joinButton;
        public Button crewButton;
        public Button quitButton;
        public InputField codeInput;
        public Text statusText;

        [Header("Level select")]
        public GameObject levelPanel;
        public Button levelBackButton;
        public LevelCard[] levelCards;
        public Text levelMessage;

        [Header("Lobby")]
        public GameObject lobbyPanel;
        public Text roomCodeText;
        public Text lobbyLevelText;
        public Text orangeList;
        public Text blueList;
        public Button orangeButton;
        public Button blueButton;
        public Button readyButton;
        public Text readyLabel;
        public Button fillButton;
        public Text fillLabel;
        public Button startButton;
        public Button leaveButton;
        public Text lobbyHint;
        public Button lobbyCrewButton;

        [Header("Crew")]
        public GameObject crewPanel;
        public CrewPreview preview;
        public Button crewBackButton;
        public Button crewStartButton;
        public Text crewStartLabel;
        public Button crewRandomButton;
        public Button crewOutfitButton;
        public Text crewOutfitLabel;
        [Tooltip("Short, medium and tall, in that order.")]
        public Button[] bodyButtons;
        public GameObject[] bodyPicked;
        [Tooltip("Health, speed and damage, in that order.")]
        public Image[] statFills;
        public Text[] statValues;
        public Text bodyBlurb;
        public Button hatBack;
        public Button hatNext;
        public Text hatLabel;
        public Button glassesBack;
        public Button glassesNext;
        public Text glassesLabel;
        [Tooltip("In CrewLook.FaceSliders order.")]
        public Slider[] faceSliders;

        private GameConfig config;
        private Page page = Page.Title;
        private Plan plan;
        private bool busy;
        private Page crewReturn = Page.Menu;
        private bool crewStarts;
        private bool previewPorter = true;

        private void Start()
        {
            config = GameConfig.Get();

            playButton.onClick.AddListener(HandlePlay);
            practiceButton.onClick.AddListener(() => ChooseLevelFor(Plan.Practice));
            createButton.onClick.AddListener(() => ChooseLevelFor(Plan.CreateRoom));
            joinButton.onClick.AddListener(HandleJoin);
            crewButton.onClick.AddListener(() => OpenCrew(Page.Menu, false));
            quitButton.onClick.AddListener(Application.Quit);
            levelBackButton.onClick.AddListener(() => page = Page.Menu);
            for (int i = 0; i < levelCards.Length; i++)
            {
                int index = i;
                levelCards[i].Show(i, config.Level(i));
                levelCards[i].button.onClick.AddListener(() => HandleLevel(index));
            }

            orangeButton.onClick.AddListener(() => SessionRoster.Instance?.SetTeamRpc(Team.Orange));
            blueButton.onClick.AddListener(() => SessionRoster.Instance?.SetTeamRpc(Team.Blue));
            readyButton.onClick.AddListener(HandleReady);
            fillButton.onClick.AddListener(HandleFill);
            startButton.onClick.AddListener(() => SessionRoster.Instance?.ServerStartMatch());
            leaveButton.onClick.AddListener(() => RoomService.Instance.Leave());
            lobbyCrewButton.onClick.AddListener(() => OpenCrew(Page.Lobby, false));
            WireCrewPage();

            GameAudio.SetMusicDucked(true);
            GameAudio.SetMusic("music");

            NetworkManager net = NetworkManager.Singleton;
            if (pastTitle || (net != null && net.IsListening))
            {
                page = Page.Menu;
                stage.JumpToMenus();
            }
            ShowPage();
        }

        private void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            SessionRoster roster = SessionRoster.Instance;
            bool connected = net != null && net.IsListening;
            bool inRoom = connected && roster != null && roster.IsSpawned && !RoomService.Instance.IsPractice;

            if (inRoom && page != Page.Crew) page = Page.Lobby;
            else if (!inRoom && (page == Page.Lobby || (page == Page.Crew && crewReturn == Page.Lobby))) page = Page.Menu;

            if (!string.IsNullOrEmpty(RoomService.PendingMessage))
            {
                statusText.text = RoomService.PendingMessage;
                RoomService.PendingMessage = null;
                busy = false;
                if (page != Page.Lobby) page = Page.Menu;
            }
            ShowPage();

            bool waiting = busy || connected;
            practiceButton.interactable = !waiting;
            createButton.interactable = !waiting;
            crewButton.interactable = !waiting;
            joinButton.interactable = !waiting && !string.IsNullOrWhiteSpace(codeInput.text);
            levelBackButton.interactable = !waiting;
            foreach (LevelCard card in levelCards) card.button.interactable = !waiting;

            if (page == Page.Lobby) RefreshLobby(roster, net);
        }

        private void ShowPage()
        {
            SetActive(titlePanel, page == Page.Title);
            SetActive(menuPanel, page == Page.Menu);
            SetActive(levelPanel, page == Page.Levels);
            SetActive(lobbyPanel, page == Page.Lobby);
            SetActive(crewPanel, page == Page.Crew);
            SetActive(preview.gameObject, page == Page.Crew);
            if (page != Page.Title) stage.SwingToMenus();
        }

        private static void SetActive(GameObject panel, bool active)
        {
            if (panel.activeSelf != active) panel.SetActive(active);
        }

        private void RefreshLobby(SessionRoster roster, NetworkManager net)
        {
            RoomService rooms = RoomService.Instance;
            roomCodeText.text = rooms.IsLanRoom ? GameText.LanRoom(rooms.RoomCode) : GameText.RoomCode(rooms.RoomCode);
            lobbyLevelText.text = GameText.LevelLine(roster.Level.Value + 1, config.Level(roster.Level.Value).title);

            StringBuilder orange = new StringBuilder();
            StringBuilder blue = new StringBuilder();
            bool localReady = false;
            foreach (RosterEntry entry in roster.Entries)
            {
                bool isLocal = !entry.IsBot && entry.ClientId == net.LocalClientId;
                if (isLocal) localReady = entry.Ready;
                (entry.Team == Team.Orange ? orange : blue).AppendLine(GameText.RosterLine(entry.Name.ToString(), isLocal, entry.Ready, entry.Look.Body));
            }
            orangeList.text = orange.ToString();
            blueList.text = blue.ToString();

            readyLabel.text = localReady ? GameText.NotReady : GameText.Ready;
            bool isHost = net.IsServer;
            startButton.gameObject.SetActive(isHost);
            fillButton.gameObject.SetActive(isHost);
            startButton.interactable = roster.CanStart;
            fillLabel.text = GameText.BotsFill(roster.FillWithBots.Value);
            lobbyHint.text = !isHost ? GameText.LobbyWaitForHost : roster.CanStart ? GameText.LobbyAllReady : GameText.LobbyWaitForReady;
        }

        private void HandlePlay()
        {
            pastTitle = true;
            page = Page.Menu;
        }

        private void ChooseLevelFor(Plan chosenPlan)
        {
            plan = chosenPlan;
            page = Page.Levels;
            levelMessage.text = GameText.LevelHint;
            statusText.text = "";
        }

        private void HandleLevel(int index)
        {
            if (!config.Level(index).IsReady)
            {
                levelCards[index].Shake();
                levelMessage.text = GameText.LevelLocked;
                return;
            }

            RoomService.Instance.SelectedLevel = index;
            // Practice goes through the crew page first, and its button starts the match.
            if (plan == Plan.Practice) OpenCrew(Page.Levels, true);
            else HandleCreate();
        }

        private void HandlePractice()
        {
            crewStartButton.interactable = false;
            crewStartLabel.text = GameText.Loading;
            if (RoomService.Instance.StartPractice()) return;
            crewStartButton.interactable = true;
            page = Page.Menu;
            statusText.text = GameText.PracticeFailed;
        }

        // ---- The crew page ----

        private void WireCrewPage()
        {
            crewBackButton.onClick.AddListener(() => page = crewReturn);
            crewStartButton.onClick.AddListener(() =>
            {
                if (crewStarts) HandlePractice();
                else page = crewReturn;
            });
            crewRandomButton.onClick.AddListener(() => ChangeLook(CrewLook.Random(GameSettings.Look.Body)));
            crewOutfitButton.onClick.AddListener(() =>
            {
                previewPorter = !previewPorter;
                RefreshCrew();
            });
            for (int i = 0; i < bodyButtons.Length; i++)
            {
                BodyType body = (BodyType)i;
                bodyButtons[i].onClick.AddListener(() => ChangeLook(WithBody(GameSettings.Look, body)));
            }
            hatBack.onClick.AddListener(() => StepHat(-1));
            hatNext.onClick.AddListener(() => StepHat(1));
            glassesBack.onClick.AddListener(() => StepGlasses(-1));
            glassesNext.onClick.AddListener(() => StepGlasses(1));
            for (int i = 0; i < faceSliders.Length; i++)
            {
                int slider = i;
                faceSliders[i].onValueChanged.AddListener(value =>
                {
                    CrewLook look = GameSettings.Look;
                    look.SetFace(slider, value);
                    ChangeLook(look);
                });
            }
        }

        private static CrewLook WithBody(CrewLook look, BodyType body)
        {
            look.Body = body;
            return look;
        }

        private void StepHat(int step)
        {
            CrewLook look = GameSettings.Look;
            look.Hat = (byte)((look.Hat + step + CrewLook.Hats.Length + 1) % (CrewLook.Hats.Length + 1));
            ChangeLook(look);
        }

        private void StepGlasses(int step)
        {
            CrewLook look = GameSettings.Look;
            look.Glasses = (byte)((look.Glasses + step + CrewLook.GlassesStyles.Length + 1) % (CrewLook.GlassesStyles.Length + 1));
            ChangeLook(look);
        }

        private void OpenCrew(Page returnTo, bool startsPractice)
        {
            crewReturn = returnTo;
            crewStarts = startsPractice;
            previewPorter = true;
            crewStartButton.interactable = true;
            statusText.text = "";
            page = Page.Crew;
            RefreshCrew();
        }

        /// <summary>Saved straight away, and sent to the host when in a room.</summary>
        private void ChangeLook(CrewLook look)
        {
            GameSettings.Look = look;
            SessionRoster roster = SessionRoster.Instance;
            if (roster != null && roster.IsSpawned && !roster.InGame.Value) roster.SetLookRpc(GameSettings.Look);
            RefreshCrew();
        }

        private void RefreshCrew()
        {
            CrewLook look = GameSettings.Look;
            preview.Show(look, previewPorter);
            for (int i = 0; i < bodyPicked.Length; i++) bodyPicked[i].SetActive(i == (int)look.Body);

            BodyStats stats = config.Stats(look.Body);
            float strongestHealth = Mathf.Max(config.shortBody.maxHealth, Mathf.Max(config.mediumBody.maxHealth, config.tallBody.maxHealth));
            float fastest = Mathf.Max(config.shortBody.speed, Mathf.Max(config.mediumBody.speed, config.tallBody.speed));
            float hardest = Mathf.Max(config.shortBody.damage, Mathf.Max(config.mediumBody.damage, config.tallBody.damage));
            SetStat(0, stats.maxHealth / strongestHealth, stats.maxHealth.ToString());
            SetStat(1, stats.speed / fastest, GameText.Percent(stats.speed));
            SetStat(2, stats.damage / hardest, GameText.Percent(stats.damage));
            bodyBlurb.text = GameText.BodyBlurb(look.Body);

            hatLabel.text = GameText.HatName(look.Hat);
            glassesLabel.text = GameText.GlassesName(look.Glasses);
            for (int i = 0; i < faceSliders.Length; i++) faceSliders[i].SetValueWithoutNotify(look.FaceValue(i));
            crewOutfitLabel.text = previewPorter ? GameText.SeeWrecker : GameText.SeePorter;
            if (crewStartButton.interactable) crewStartLabel.text = crewStarts ? GameText.StartPractice : GameText.Done;
        }

        private void SetStat(int index, float share, string value)
        {
            // Never quite empty, so even the lowest still reads as a bar.
            statFills[index].rectTransform.anchorMax = new Vector2(Mathf.Lerp(0.08f, 1f, Mathf.Clamp01(share)), 1f);
            statValues[index].text = value;
        }

        private async void HandleCreate()
        {
            busy = true;
            levelMessage.text = GameText.CreatingRoom;
            bool created = await RoomService.Instance.CreateRoom();
            busy = false;
            statusText.text = created ? "" : GameText.CreateFailed;
            if (!created) page = Page.Menu;
        }

        private async void HandleJoin()
        {
            busy = true;
            statusText.text = GameText.Joining;
            bool joined = await RoomService.Instance.JoinRoom(codeInput.text);
            busy = false;
            if (joined) statusText.text = "";
        }

        private void HandleReady()
        {
            SessionRoster roster = SessionRoster.Instance;
            if (roster == null) return;
            bool ready = roster.TryGetEntry(NetworkManager.Singleton.LocalClientId, out RosterEntry entry) && entry.Ready;
            roster.SetReadyRpc(!ready);
        }

        private void HandleFill()
        {
            SessionRoster roster = SessionRoster.Instance;
            if (roster != null) roster.ServerSetFillWithBots(!roster.FillWithBots.Value);
        }
    }
}
