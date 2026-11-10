using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CargoClash
{
    public struct RosterEntry : INetworkSerializable, IEquatable<RosterEntry>
    {
        public ulong ClientId;
        public FixedString32Bytes Name;
        public Team Team;
        public bool Ready;
        public bool IsBot;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref IsBot);
        }

        public bool Equals(RosterEntry other) =>
            ClientId == other.ClientId && Name.Equals(other.Name) && Team == other.Team &&
            Ready == other.Ready && IsBot == other.IsBot;
    }

    /// <summary>
    /// Who is in the room and on which team. It survives scene changes for the whole session,
    /// and the host is the only one who changes it.
    /// </summary>
    public sealed class SessionRoster : NetworkBehaviour
    {
        public static SessionRoster Instance { get; private set; }

        public NetworkList<RosterEntry> Entries;
        public readonly NetworkVariable<bool> FillWithBots = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<bool> InGame = new NetworkVariable<bool>(false);
        public readonly NetworkVariable<int> Level = new NetworkVariable<int>(0);

        private GameConfig config;

        private string LevelScene => config.Level(Level.Value).scene;

        private void Awake()
        {
            Entries = new NetworkList<RosterEntry>();
            config = GameConfig.Get();
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;

            Level.Value = RoomService.Instance.SelectedLevel;
            NetworkManager.OnClientConnectedCallback += AddHuman;
            NetworkManager.OnClientDisconnectCallback += RemoveHuman;
            NetworkManager.SceneManager.OnLoadEventCompleted += HandleSceneLoaded;
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds) AddHuman(clientId);

            if (RoomService.Instance.IsPractice) ServerStartMatch();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            if (!IsServer || NetworkManager == null) return;
            NetworkManager.OnClientConnectedCallback -= AddHuman;
            NetworkManager.OnClientDisconnectCallback -= RemoveHuman;
            if (NetworkManager.SceneManager != null)
                NetworkManager.SceneManager.OnLoadEventCompleted -= HandleSceneLoaded;
        }

        public int CountTeam(Team team)
        {
            int count = 0;
            foreach (RosterEntry entry in Entries)
                if (entry.Team == team) count++;
            return count;
        }

        public bool TryGetEntry(ulong clientId, out RosterEntry result)
        {
            foreach (RosterEntry entry in Entries)
            {
                if (!entry.IsBot && entry.ClientId == clientId)
                {
                    result = entry;
                    return true;
                }
            }
            result = default;
            return false;
        }

        /// <summary>Everyone is ready and the teams are even enough to start.</summary>
        public bool CanStart
        {
            get
            {
                int orange = CountTeam(Team.Orange);
                int blue = CountTeam(Team.Blue);
                if (Entries.Count == 0) return false;
                foreach (RosterEntry entry in Entries)
                    if (!entry.Ready) return false;
                if (FillWithBots.Value) return true;
                return orange > 0 && blue > 0 && Mathf.Abs(orange - blue) <= 1;
            }
        }

        [Rpc(SendTo.Server)]
        public void SetTeamRpc(Team team, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (InGame.Value || CountTeam(team) >= config.teamSize) return;
            UpdateEntry(sender, entry => { entry.Team = team; entry.Ready = false; return entry; });
        }

        [Rpc(SendTo.Server)]
        public void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            if (InGame.Value) return;
            UpdateEntry(rpcParams.Receive.SenderClientId, entry => { entry.Ready = ready; return entry; });
        }

        public void ServerSetFillWithBots(bool fill)
        {
            if (IsServer) FillWithBots.Value = fill;
        }

        public void ServerStartMatch()
        {
            if (!IsServer || InGame.Value) return;
            RemoveBots();
            if (FillWithBots.Value || RoomService.Instance.IsPractice) AddBots();
            InGame.Value = true;
            NetworkManager.SceneManager.LoadScene(LevelScene, LoadSceneMode.Single);
        }

        public void ServerRestartMatch()
        {
            if (!IsServer) return;
            NetworkManager.SceneManager.LoadScene(LevelScene, LoadSceneMode.Single);
        }

        public void ServerBackToLobby()
        {
            if (!IsServer) return;
            RemoveBots();
            for (int i = 0; i < Entries.Count; i++)
            {
                RosterEntry entry = Entries[i];
                entry.Ready = false;
                Entries[i] = entry;
            }
            InGame.Value = false;
            NetworkManager.SceneManager.LoadScene(config.titleScene, LoadSceneMode.Single);
        }

        private void HandleSceneLoaded(string sceneName, LoadSceneMode mode,
            List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (sceneName != LevelScene) return;
            GameObject director = Instantiate(config.directorPrefab);
            director.GetComponent<NetworkObject>().Spawn(true);
        }

        private void AddHuman(ulong clientId)
        {
            if (TryGetEntry(clientId, out _)) return;
            if (InGame.Value)
            {
                NetworkManager.DisconnectClient(clientId, GameText.MatchInProgress);
                return;
            }
            if (Entries.Count >= config.maxPlayers)
            {
                NetworkManager.DisconnectClient(clientId, GameText.RoomFull);
                return;
            }

            Team team = CountTeam(Team.Orange) <= CountTeam(Team.Blue) ? Team.Orange : Team.Blue;
            Entries.Add(new RosterEntry
            {
                ClientId = clientId,
                Name = new FixedString32Bytes("Player " + (clientId + 1)),
                Team = team,
                Ready = RoomService.Instance.IsPractice,
            });
        }

        private void RemoveHuman(ulong clientId)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].IsBot && Entries[i].ClientId == clientId) Entries.RemoveAt(i);
            }
        }

        private void AddBots()
        {
            int number = 1;
            foreach (Team team in new[] { Team.Orange, Team.Blue })
            {
                while (CountTeam(team) < config.teamSize)
                {
                    Entries.Add(new RosterEntry
                    {
                        ClientId = NetworkManager.ServerClientId,
                        Name = new FixedString32Bytes("Bot " + number++),
                        Team = team,
                        Ready = true,
                        IsBot = true,
                    });
                }
            }
        }

        private void RemoveBots()
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (Entries[i].IsBot) Entries.RemoveAt(i);
            }
        }

        private void UpdateEntry(ulong clientId, Func<RosterEntry, RosterEntry> change)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].IsBot || Entries[i].ClientId != clientId) continue;
                Entries[i] = change(Entries[i]);
                return;
            }
        }
    }
}
