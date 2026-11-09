using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>
    /// The screens of the start scene: title, main menu, level select, and a room's lobby.
    /// Practice and creating a room both go through the level select. Joining doesn't, because the host picks.
    /// </summary>
    public sealed class TitleFlow : MonoBehaviour
    {
        private enum Page { Title, Menu, Levels, Lobby }
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

        private GameConfig config;
        private Page page = Page.Title;
        private Plan plan;
        private bool busy;

        private void Start()
        {
            config = GameConfig.Get();

            playButton.onClick.AddListener(HandlePlay);
            practiceButton.onClick.AddListener(() => ChooseLevelFor(Plan.Practice));
            createButton.onClick.AddListener(() => ChooseLevelFor(Plan.CreateRoom));
            joinButton.onClick.AddListener(HandleJoin);
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

            if (inRoom) page = Page.Lobby;
            else if (page == Page.Lobby) page = Page.Menu;

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
                (entry.Team == Team.Orange ? orange : blue).AppendLine(GameText.RosterLine(entry.Name.ToString(), isLocal, entry.Ready));
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
            if (plan == Plan.Practice) HandlePractice();
            else HandleCreate();
        }

        private void HandlePractice()
        {
            levelMessage.text = GameText.Loading;
            if (RoomService.Instance.StartPractice()) return;
            page = Page.Menu;
            statusText.text = GameText.PracticeFailed;
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
