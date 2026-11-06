using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    public struct ResultLine : INetworkSerializable
    {
        public FixedString32Bytes Title;
        public FixedString32Bytes Player;
        public int Value;
        public Team Team;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Title);
            serializer.SerializeValue(ref Player);
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref Team);
        }
    }

    /// <summary>
    /// Runs one level on the host: two halves with a side swap, scoring, and the results.
    /// Clients read the replicated phase, timer and scores.
    /// </summary>
    public sealed class MatchDirector : NetworkBehaviour
    {
        private sealed class PlayerStats
        {
            public string Name;
            public Team Team;
            public float Rescued;
            public float Destroyed;
            public int Rams;
            public int KnockedDown;
            public int Drops;
        }

        public static MatchDirector Instance { get; private set; }

        public readonly NetworkVariable<MatchPhase> Phase = new NetworkVariable<MatchPhase>(MatchPhase.Loading);
        public readonly NetworkVariable<int> Half = new NetworkVariable<int>(1);
        public readonly NetworkVariable<Team> CollectorTeam = new NetworkVariable<Team>(Team.Orange);
        public readonly NetworkVariable<double> PhaseEndsAt = new NetworkVariable<double>();
        public readonly NetworkVariable<int> OrangeScore = new NetworkVariable<int>();
        public readonly NetworkVariable<int> BlueScore = new NetworkVariable<int>();

        public ResultLine[] Results { get; private set; }
        public event Action ResultsReady;

        private readonly Dictionary<PlayerAvatar, PlayerStats> stats = new Dictionary<PlayerAvatar, PlayerStats>();
        private readonly StringBuilder csv = new StringBuilder();
        private GameConfig config;
        private LevelLayout layout;
        private int halfRescued;
        private int halfDestroyed;
        private int halfTotal;
        private double halfStartedAt;

        public double Now => NetworkManager.ServerTime.Time;
        public float SecondsLeft => Mathf.Max(0f, (float)(PhaseEndsAt.Value - Now));

        public override void OnNetworkSpawn()
        {
            Instance = this;
            config = GameConfig.Get();
            layout = LevelLayout.Instance;
            if (IsServer) StartCoroutine(RunMatch());
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public Team TeamOn(Side side) => side == Side.Collector ? CollectorTeam.Value : CollectorTeam.Value.Other();

        /// <summary>The design doc gives the smaller team a shorter ram cooldown on levels without items.</summary>
        public float RamCooldownFor(Team team)
        {
            int mine = 0;
            int theirs = 0;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                if (avatar.Team == team) mine++;
                else theirs++;
            }
            return mine < theirs ? config.ramCooldownSmallTeam : config.ramCooldown;
        }

        // ---- Host only ----

        private IEnumerator RunMatch()
        {
            SpawnAvatars();
            for (int half = 1; half <= 2; half++)
            {
                Half.Value = half;
                CollectorTeam.Value = half == 1 ? Team.Orange : Team.Blue;
                ResetField();
                yield return RunPhase(MatchPhase.HalfIntro, config.halfIntroSeconds);
                halfStartedAt = Now;
                yield return RunPhase(MatchPhase.Playing, config.EffectiveHalfSeconds);
                foreach (PlayerAvatar avatar in PlayerAvatar.All) avatar.ServerStopActions();
                RecordHalf(half);
                yield return RunPhase(MatchPhase.HalfOutro, config.halfOutroSeconds);
            }
            FinishMatch();
        }

        private IEnumerator RunPhase(MatchPhase phase, float seconds)
        {
            Phase.Value = phase;
            PhaseEndsAt.Value = Now + seconds;
            while (Now < PhaseEndsAt.Value)
            {
                // A half has nothing left to play once every piece of cargo is saved or smashed.
                if (phase == MatchPhase.Playing && AllCargoResolved())
                {
                    PhaseEndsAt.Value = Now;
                    break;
                }
                yield return null;
            }
        }

        private bool AllCargoResolved()
        {
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value == CargoState.Ground || cargo.State.Value == CargoState.Carried) return false;
            }
            return true;
        }

        private void SpawnAvatars()
        {
            SessionRoster roster = SessionRoster.Instance;
            int orangeIndex = 0;
            int blueIndex = 0;
            foreach (RosterEntry entry in roster.Entries)
            {
                int index = entry.Team == Team.Orange ? orangeIndex++ : blueIndex++;
                Transform spot = SpawnFor(entry.Team, index, Team.Orange);
                GameObject instance = Instantiate(config.avatarPrefab, spot.position, spot.rotation);
                PlayerAvatar avatar = instance.GetComponent<PlayerAvatar>();
                avatar.ServerInit(entry);
                stats[avatar] = new PlayerStats { Name = entry.Name.ToString(), Team = entry.Team };

                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                if (entry.IsBot) networkObject.Spawn(true);
                else networkObject.SpawnAsPlayerObject(entry.ClientId, true);
            }
        }

        private Transform SpawnFor(Team team, int index, Team collectors)
        {
            Transform[] spots = team == collectors ? layout.collectorSpawns : layout.interrupterSpawns;
            return spots[index % spots.Length];
        }

        private void ResetField()
        {
            for (int i = CargoItem.All.Count - 1; i >= 0; i--)
            {
                CargoItem cargo = CargoItem.All[i];
                if (cargo.IsSpawned) cargo.NetworkObject.Despawn(true);
            }
            layout.ResetBed();

            int players = SessionRoster.Instance.Entries.Count;
            int thirds = players >= 5 ? 3 : players >= 3 ? 2 : 1;
            halfRescued = 0;
            halfDestroyed = 0;
            halfTotal = 0;
            foreach (CargoDefinition definition in config.cargo)
            {
                Transform[] spots = layout.SpotsFor(definition.size);
                int count = definition.fullCount * thirds / 3;
                for (int i = 0; i < count; i++)
                {
                    Transform spot = spots[(i * spots.Length / Mathf.Max(1, count)) % spots.Length];
                    GameObject instance = Instantiate(definition.prefab, spot.position, spot.rotation);
                    instance.GetComponent<NetworkObject>().Spawn(true);
                    halfTotal += definition.value;
                }
            }

            int orangeIndex = 0;
            int blueIndex = 0;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                int index = avatar.Team == Team.Orange ? orangeIndex++ : blueIndex++;
                Transform spot = SpawnFor(avatar.Team, index, CollectorTeam.Value);
                avatar.ServerResetForHalf(spot.position, spot.rotation);
            }
        }

        public void ServerOnRescued(CargoItem cargo, List<PlayerAvatar> carriers)
        {
            int value = cargo.Definition.value;
            AutoPilot.Log($"RESCUED {cargo.Definition.size} carriers={string.Join("+", carriers.ConvertAll(c => c.DisplayName))}");
            AddScore(TeamOn(Side.Collector), value);
            halfRescued += value;
            float share = carriers.Count > 0 ? (float)value / carriers.Count : 0f;
            foreach (PlayerAvatar carrier in carriers)
            {
                if (stats.TryGetValue(carrier, out PlayerStats entry)) entry.Rescued += share;
            }
            CargoEventRpc(cargo.transform.position, value, TeamOn(Side.Collector), true);
        }

        public void ServerOnDestroyed(CargoItem cargo, List<PlayerAvatar> smashers)
        {
            int value = cargo.Definition.value;
            AddScore(TeamOn(Side.Interrupter), value);
            halfDestroyed += value;
            float share = smashers.Count > 0 ? (float)value / smashers.Count : 0f;
            foreach (PlayerAvatar smasher in smashers)
            {
                if (stats.TryGetValue(smasher, out PlayerStats entry)) entry.Destroyed += share;
            }
            CargoEventRpc(cargo.transform.position, value, TeamOn(Side.Interrupter), false);
        }

        public void ServerOnRam(PlayerAvatar avatar)
        {
            if (stats.TryGetValue(avatar, out PlayerStats entry)) entry.Rams++;
        }

        public void ServerOnKnockedDown(PlayerAvatar avatar, bool droppedCargo)
        {
            if (!stats.TryGetValue(avatar, out PlayerStats entry)) return;
            entry.KnockedDown++;
            if (droppedCargo) entry.Drops++;
        }

        private void AddScore(Team team, int value)
        {
            if (team == Team.Orange) OrangeScore.Value += value;
            else BlueScore.Value += value;
        }

        private void RecordHalf(int half)
        {
            int untouched = halfTotal - halfRescued - halfDestroyed;
            AutoPilot.Log($"HALF {half} seconds={Now - halfStartedAt:0} collectors={CollectorTeam.Value} rescued={halfRescued} destroyed={halfDestroyed} untouched={untouched} total={halfTotal} orange={OrangeScore.Value} blue={BlueScore.Value}");
            csv.AppendLine($"half,{half},{Now - halfStartedAt:0},{CollectorTeam.Value},{halfRescued},{halfDestroyed},{untouched},{halfTotal}");
        }

        private void FinishMatch()
        {
            List<ResultLine> lines = new List<ResultLine>();
            AddTitle(lines, GameText.TitleMover, s => s.Rescued);
            AddTitle(lines, GameText.TitleWrecker, s => s.Destroyed);
            AddTitle(lines, GameText.TitleButterfingers, s => s.Drops);

            foreach (PlayerStats entry in stats.Values)
                csv.AppendLine($"player,{entry.Name},{entry.Team},{entry.Rescued:0},{entry.Destroyed:0},{entry.Rams},{entry.KnockedDown},{entry.Drops}");
            WritePlaytestLog();

            Phase.Value = MatchPhase.Results;
            ShowResultsRpc(lines.ToArray());
        }

        private void AddTitle(List<ResultLine> lines, string title, Func<PlayerStats, float> measure)
        {
            PlayerStats best = null;
            float bestValue = 0f;
            foreach (PlayerStats entry in stats.Values)
            {
                float value = measure(entry);
                if (value > bestValue)
                {
                    best = entry;
                    bestValue = value;
                }
            }
            if (best == null) return;
            lines.Add(new ResultLine
            {
                Title = new FixedString32Bytes(title),
                Player = new FixedString32Bytes(best.Name),
                Value = Mathf.RoundToInt(bestValue),
                Team = best.Team,
            });
        }

        private void WritePlaytestLog()
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "playtest.csv");
                string header = $"match,{DateTime.Now:yyyy-MM-dd HH:mm:ss},orange,{OrangeScore.Value},blue,{BlueScore.Value}\n";
                File.AppendAllText(path, header + csv);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not write the playtest log: {exception.Message}");
            }
        }

        // ---- Everyone ----

        [Rpc(SendTo.Everyone)]
        private void CargoEventRpc(Vector3 position, int value, Team team, bool rescued)
        {
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(position, "+" + value, team.ToColor());
            GameAudio.Play(rescued ? "rescue" : "break");
            if (!rescued) FxBurst.Play(position, new Color(0.75f, 0.55f, 0.3f), 24);
        }

        [Rpc(SendTo.Everyone)]
        private void ShowResultsRpc(ResultLine[] lines)
        {
            Results = lines;
            ResultsReady?.Invoke();
        }
    }
}
