using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace CargoClash
{
    /// <summary>The title screen after GO: practice, create or join a room, then the lobby.</summary>
    public sealed class TitleFlow : MonoBehaviour
    {
        public CargoClashStartMenu startMenu;

        [Header("Main menu")]
        public GameObject menuPanel;
        public Button practiceButton;
        public Button createButton;
        public Button joinButton;
        public InputField codeInput;
        public Text statusText;

        [Header("Lobby")]
        public GameObject lobbyPanel;
        public Text roomCodeText;
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

        private bool goPressed;
        private bool busy;

        private void Start()
        {
            menuPanel.SetActive(false);
            lobbyPanel.SetActive(false);

            practiceButton.onClick.AddListener(HandlePractice);
            createButton.onClick.AddListener(HandleCreate);
            joinButton.onClick.AddListener(HandleJoin);
            orangeButton.onClick.AddListener(() => SessionRoster.Instance?.SetTeamRpc(Team.Orange));
            blueButton.onClick.AddListener(() => SessionRoster.Instance?.SetTeamRpc(Team.Blue));
            readyButton.onClick.AddListener(HandleReady);
            fillButton.onClick.AddListener(HandleFill);
            startButton.onClick.AddListener(() => SessionRoster.Instance?.ServerStartMatch());
            leaveButton.onClick.AddListener(() => RoomService.Instance.Leave());
            startMenu.GoPressed.AddListener(() => goPressed = true);

            bool comingBack = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (comingBack || !string.IsNullOrEmpty(RoomService.PendingMessage)) startMenu.OnGoPressed();
        }

        private void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (!goPressed)
            {
                if (net != null && net.IsListening) startMenu.OnGoPressed();
                return;
            }

            SessionRoster roster = SessionRoster.Instance;
            bool inRoom = net != null && net.IsListening && roster != null && roster.IsSpawned;
            bool practice = RoomService.Instance != null && RoomService.Instance.IsPractice;
            bool showLobby = inRoom && !practice;

            if (lobbyPanel.activeSelf != showLobby) lobbyPanel.SetActive(showLobby);
            bool showMenu = !showLobby;
            if (menuPanel.activeSelf != showMenu) menuPanel.SetActive(showMenu);

            if (!string.IsNullOrEmpty(RoomService.PendingMessage))
            {
                statusText.text = RoomService.PendingMessage;
                RoomService.PendingMessage = null;
                busy = false;
            }

            bool waiting = busy || (net != null && net.IsListening);
            practiceButton.interactable = !waiting;
            createButton.interactable = !waiting;
            joinButton.interactable = !waiting && !string.IsNullOrWhiteSpace(codeInput.text);

            if (showLobby) RefreshLobby(roster, net);
        }

        private void RefreshLobby(SessionRoster roster, NetworkManager net)
        {
            RoomService rooms = RoomService.Instance;
            roomCodeText.text = rooms.IsLanRoom
                ? $"LAN ROOM: {rooms.RoomCode}"
                : $"ROOM CODE: {rooms.RoomCode}";

            StringBuilder orange = new StringBuilder();
            StringBuilder blue = new StringBuilder();
            bool localReady = false;
            foreach (RosterEntry entry in roster.Entries)
            {
                bool isLocal = !entry.IsBot && entry.ClientId == net.LocalClientId;
                if (isLocal) localReady = entry.Ready;
                string line = $"{entry.Name}{(isLocal ? " (you)" : "")}{(entry.Ready ? "  READY" : "")}";
                (entry.Team == Team.Orange ? orange : blue).AppendLine(line);
            }
            orangeList.text = orange.ToString();
            blueList.text = blue.ToString();

            readyLabel.text = localReady ? "NOT READY" : "READY";
            bool isHost = net.IsServer;
            startButton.gameObject.SetActive(isHost);
            fillButton.gameObject.SetActive(isHost);
            startButton.interactable = roster.CanStart;
            fillLabel.text = roster.FillWithBots.Value ? "BOTS FILL TEAMS: ON" : "BOTS FILL TEAMS: OFF";
            lobbyHint.text = isHost
                ? (roster.CanStart ? "Everyone is ready." : "Waiting for everyone to get ready, with even teams.")
                : "Waiting for the host to start.";
        }

        private void HandlePractice()
        {
            statusText.text = "Loading...";
            if (!RoomService.Instance.StartPractice()) statusText.text = "Could not start practice mode.";
        }

        private async void HandleCreate()
        {
            busy = true;
            statusText.text = "Creating room...";
            bool created = await RoomService.Instance.CreateRoom();
            busy = false;
            statusText.text = created ? "" : "Could not create a room.";
        }

        private async void HandleJoin()
        {
            busy = true;
            statusText.text = "Joining...";
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
