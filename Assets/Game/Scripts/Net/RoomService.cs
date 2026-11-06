using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoClash
{
    /// <summary>
    /// Starts and leaves rooms. Online rooms use Sessions and Relay when the project is linked to
    /// Unity Cloud. Without that link they fall back to a LAN room whose code is the host's IP.
    /// Practice mode always stays on this computer, so it works offline.
    /// </summary>
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class RoomService : MonoBehaviour
    {
        public const ushort DefaultPort = 7777;

        private static ushort DirectPort => AutoPilot.PortOverride != 0 ? AutoPilot.PortOverride : DefaultPort;

        public static RoomService Instance { get; private set; }

        /// <summary>Shown by the title screen the next time it is visible.</summary>
        public static string PendingMessage;

        public string RoomCode { get; private set; } = "";
        public bool IsPractice { get; private set; }
        public bool IsLanRoom { get; private set; }

        private NetworkManager net;
        private UnityTransport transport;
        private ISession session;
        private bool leavingOnPurpose;
        private bool wasConnected;

        private void Awake()
        {
            Instance = this;
            net = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();

            GameConfig config = GameConfig.Get();
            net.AddNetworkPrefab(config.avatarPrefab);
            net.AddNetworkPrefab(config.directorPrefab);
            net.AddNetworkPrefab(config.rosterPrefab);
            foreach (CargoDefinition definition in config.cargo) net.AddNetworkPrefab(definition.prefab);

            net.OnServerStarted += HandleServerStarted;
            net.OnClientConnectedCallback += HandleClientConnected;
            net.OnClientStopped += HandleClientStopped;
        }

        public bool StartPractice()
        {
            ResetFlags();
            IsPractice = true;
            transport.SetConnectionData("127.0.0.1", DirectPort, "127.0.0.1");
            return net.StartHost();
        }

        public bool StartLanHost()
        {
            ResetFlags();
            IsLanRoom = true;
            RoomCode = LocalIPv4();
            transport.SetConnectionData(RoomCode, DirectPort, "0.0.0.0");
            return net.StartHost();
        }

        public async Task<bool> CreateRoom()
        {
            ResetFlags();
            if (HasCloudProject())
            {
                try
                {
                    await EnsureSignedIn();
                    SessionOptions options = new SessionOptions { MaxPlayers = GameConfig.Get().maxPlayers }
                        .WithRelayNetwork();
                    session = await MultiplayerService.Instance.CreateSessionAsync(options);
                    RoomCode = session.Code;
                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Online room failed, using a LAN room: {exception.Message}");
                }
            }
            return StartLanHost();
        }

        public async Task<bool> JoinRoom(string code)
        {
            ResetFlags();
            code = code.Trim();
            if (code.Contains("."))
            {
                IsLanRoom = true;
                RoomCode = code;
                transport.SetConnectionData(code, DirectPort);
                return net.StartClient();
            }

            if (!HasCloudProject())
            {
                PendingMessage = GameText.NeedCloudProject;
                return false;
            }

            try
            {
                await EnsureSignedIn();
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.ToUpperInvariant());
                RoomCode = session.Code;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Join failed: {exception.Message}");
                PendingMessage = GameText.JoinFailed;
                return false;
            }
        }

        public async void Leave()
        {
            leavingOnPurpose = true;
            ISession current = session;
            session = null;
            if (current != null)
            {
                try { await current.LeaveAsync(); }
                catch (Exception exception) { Debug.LogWarning($"Leaving session: {exception.Message}"); }
            }
            if (net.IsListening) net.Shutdown();
            ReturnToTitle();
        }

        private void HandleServerStarted()
        {
            GameObject roster = Instantiate(GameConfig.Get().rosterPrefab);
            roster.GetComponent<NetworkObject>().Spawn(false);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == net.LocalClientId) wasConnected = true;
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (!leavingOnPurpose)
            {
                // Netcode fills DisconnectReason with internal text too, so only show the host's own messages.
                string reason = net.DisconnectReason;
                if (reason == GameText.MatchInProgress || reason == GameText.RoomFull) PendingMessage = reason;
                else if (!wasHost) PendingMessage = wasConnected ? GameText.HostLeft : GameText.JoinFailed;
            }
            AutoPilot.Log($"STOPPED wasHost={wasHost} onPurpose={leavingOnPurpose} message={PendingMessage}");
            leavingOnPurpose = false;
            wasConnected = false;
            session = null;
            RoomCode = "";
            ReturnToTitle();
        }

        private void ReturnToTitle()
        {
            string title = GameConfig.Get().titleScene;
            if (SceneManager.GetActiveScene().name != title) SceneManager.LoadScene(title);
        }

        private void ResetFlags()
        {
            IsPractice = false;
            IsLanRoom = false;
            RoomCode = "";
            leavingOnPurpose = false;
            wasConnected = false;
        }

        private static bool HasCloudProject() => !string.IsNullOrEmpty(Application.cloudProjectId);

        private static async Task EnsureSignedIn()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // A separate profile per process lets two local copies sign in as different players.
                InitializationOptions options = new InitializationOptions();
                options.SetProfile("p" + Guid.NewGuid().ToString("N").Substring(0, 12));
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        private static string LocalIPv4()
        {
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation address in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                        return address.Address.ToString();
                }
            }
            return "127.0.0.1";
        }
    }
}
