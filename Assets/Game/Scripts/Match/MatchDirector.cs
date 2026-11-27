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
            public int Hits;
            public int KnockOuts;
            public int TimesOut;
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
        public readonly NetworkVariable<int> HalfRescued = new NetworkVariable<int>();
        public readonly NetworkVariable<bool> CargoRanOut = new NetworkVariable<bool>();
        public readonly NetworkVariable<double> HelicopterArrivesAt = new NetworkVariable<double>();

        public ResultLine[] Results { get; private set; }
        public event Action ResultsReady;

        private readonly Dictionary<PlayerAvatar, PlayerStats> stats = new Dictionary<PlayerAvatar, PlayerStats>();
        private readonly StringBuilder csv = new StringBuilder();
        private GameConfig config;
        private LevelLayout layout;
        private int halfDestroyed;
        private int halfLost;
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
            // Everything else on this object (the river level's rules) finishes spawning first.
            yield return null;
            SpawnAvatars();
            for (int half = 1; half <= 2; half++)
            {
                Half.Value = half;
                CollectorTeam.Value = half == 1 ? Team.Orange : Team.Blue;
                ResetField();
                yield return RunPhase(MatchPhase.HalfIntro, config.halfIntroSeconds);
                halfStartedAt = Now;
                AirportMatch airport = AirportMatch.Instance;
                if (airport != null) airport.ServerStartHalf(Now, config.EffectiveHalfSeconds);
                HelicopterArrivesAt.Value = Now + config.EffectiveHalfSeconds + config.helicopterArrivalDelay;
                yield return RunPhase(MatchPhase.Playing, config.EffectiveHalfSeconds);
                foreach (PlayerAvatar avatar in PlayerAvatar.All) avatar.ServerStopActions();
                RecordHalf(half);

                // If the cargo ran out early the helicopter is still far away, so it hurries in.
                CargoRanOut.Value = AllCargoResolved();
                HelicopterArrivesAt.Value = Math.Min(HelicopterArrivesAt.Value, Now + config.helicopterHurrySeconds);
                float untilArrival = (float)(HelicopterArrivesAt.Value - Now);
                yield return RunPhase(MatchPhase.HalfOutro, untilArrival + config.HelicopterWorkSeconds);
            }
            FinishMatch();
        }

        private IEnumerator RunPhase(MatchPhase phase, float seconds)
        {
            Phase.Value = phase;
            PhaseEndsAt.Value = Now + seconds;
            while (Now < PhaseEndsAt.Value)
            {
                if (phase == MatchPhase.Playing) DispatchFromWarehouse();
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
            // The airport's warehouse still has cases to send out.
            AirportMatch airport = AirportMatch.Instance;
            if (airport != null && airport.HasPending) return false;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.IsPlank) continue;
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
                CrewLook look = avatar.Look;
                AutoPilot.Log($"CREW {entry.Name} body={look.Body} hat={look.Hat} glasses={look.Glasses} health={avatar.MaxHealth} speed={avatar.RunSpeed:0.00} damage={avatar.Stats.damage:0.00}");
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
            HalfRescued.Value = 0;
            CargoRanOut.Value = false;
            halfDestroyed = 0;
            halfLost = 0;
            halfTotal = 0;
            if (layout.PlacesEachKind) SpawnEachKind(thirds);
            else SpawnBySize(thirds);

            // The river level's planks and catcher start over too, and the airport's door and warehouse.
            RiverMatch river = RiverMatch.Instance;
            if (river != null) river.ServerResetHalf();
            AirportMatch airport = AirportMatch.Instance;
            if (airport != null) airport.ServerResetHalf(thirds);

            int orangeIndex = 0;
            int blueIndex = 0;
            foreach (PlayerAvatar avatar in PlayerAvatar.All)
            {
                int index = avatar.Team == Team.Orange ? orangeIndex++ : blueIndex++;
                Transform spot = SpawnFor(avatar.Team, index, CollectorTeam.Value);
                avatar.ServerResetForHalf(spot.position, spot.rotation);
            }
        }

        /// <summary>A level that places every kind itself: one on each of its spots, a third or two fewer with fewer players.</summary>
        private void SpawnEachKind(int thirds)
        {
            foreach (CargoDefinition definition in config.cargo)
            {
                Transform[] spots = layout.SpotsFor(definition.kind);
                if (spots == null || spots.Length == 0) continue;
                int count = Mathf.Max(1, spots.Length * thirds / 3);
                for (int i = 0; i < count; i++)
                {
                    Transform spot = spots[i * spots.Length / count];
                    GameObject instance = Instantiate(definition.prefab, spot.position, spot.rotation);
                    instance.GetComponent<NetworkObject>().Spawn(true);
                    halfTotal += definition.value;
                }
            }
        }

        /// <summary>The airport's next case out of the warehouse, when it's due and the belt's mouth is clear.</summary>
        private void DispatchFromWarehouse()
        {
            AirportMatch airport = AirportMatch.Instance;
            if (airport == null) return;
            CargoDefinition definition = airport.ServerTryDispatch(Now, out Vector3 position);
            if (definition == null) return;
            GameObject instance = Instantiate(definition.prefab, position, Quaternion.LookRotation(AirportFacility.Instance.BeltDirection));
            instance.GetComponent<NetworkObject>().Spawn(true);
            halfTotal += definition.value;
            AutoPilot.Log($"AIRPORT_DISPATCH half={Half.Value} {definition.kind} pending={airport.Pending} left={SecondsLeft:0.0}");
        }

        private void SpawnBySize(int thirds)
        {
            // Kinds of the same size share its spots: taken in turn, spread evenly over them.
            foreach (CargoSize size in new[] { CargoSize.Small, CargoSize.Medium, CargoSize.Large })
            {
                List<CargoDefinition> pieces = new List<CargoDefinition>();
                CargoDefinition[] kinds = System.Array.FindAll(config.cargo, d => d.size == size);
                for (int round = 0; ; round++)
                {
                    bool any = false;
                    foreach (CargoDefinition definition in kinds)
                    {
                        if (round >= definition.fullCount * thirds / 3) continue;
                        pieces.Add(definition);
                        any = true;
                    }
                    if (!any) break;
                }
                Transform[] spots = layout.SpotsFor(size);
                for (int i = 0; i < pieces.Count; i++)
                {
                    Transform spot = spots[(i * spots.Length / Mathf.Max(1, pieces.Count)) % spots.Length];
                    GameObject instance = Instantiate(pieces[i].prefab, spot.position, spot.rotation);
                    instance.GetComponent<NetworkObject>().Spawn(true);
                    halfTotal += pieces[i].value;
                }
            }
        }

        public void ServerOnRescued(CargoItem cargo, List<PlayerAvatar> carriers)
        {
            int value = cargo.Definition.value;
            AutoPilot.Log($"RESCUED {cargo.Definition.size} carriers={string.Join("+", carriers.ConvertAll(c => c.DisplayName))}");
            AddScore(TeamOn(Side.Collector), value);
            HalfRescued.Value += value;
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
            Vector3 at = cargo.transform.position;
            AutoPilot.Log($"DESTROYED {cargo.Definition.kind} at=({at.x:0},{at.y:0},{at.z:0}) left={SecondsLeft:0}");
            AddScore(TeamOn(Side.Interrupter), value);
            halfDestroyed += value;
            float share = smashers.Count > 0 ? (float)value / smashers.Count : 0f;
            foreach (PlayerAvatar smasher in smashers)
            {
                if (stats.TryGetValue(smasher, out PlayerStats entry)) entry.Destroyed += share;
            }
            CargoEventRpc(cargo.transform.position, value, TeamOn(Side.Interrupter), false);
        }

        /// <summary>The river carried it off the field. Nobody scores it.</summary>
        public void ServerOnLost(CargoItem cargo)
        {
            if (cargo.IsPlank) return;
            int value = cargo.Definition.value;
            halfLost += value;
            AutoPilot.Log($"LOST {cargo.Definition.kind} value={value} at={cargo.transform.position}");
            LostRpc(cargo.transform.position, value);
        }

        public void ServerOnRam(PlayerAvatar avatar)
        {
            AutoPilot.Log($"RAM {avatar.DisplayName}");
            if (stats.TryGetValue(avatar, out PlayerStats entry)) entry.Rams++;
        }

        public void ServerOnHit(PlayerAvatar attacker, PlayerAvatar victim, Weapon weapon, bool knockedDown, bool knockedOut)
        {
            AutoPilot.Log($"HIT {attacker.DisplayName} on {victim.DisplayName} with={weapon} health={victim.Health.Value} knockedDown={knockedDown} knockedOut={knockedOut}");
            if (stats.TryGetValue(attacker, out PlayerStats entry)) entry.Hits++;
        }

        /// <summary>No attacker for a fall off the airport's upper deck.</summary>
        public void ServerOnKnockedOut(PlayerAvatar attacker, PlayerAvatar victim, Weapon weapon, bool droppedCargo)
        {
            if (attacker != null && stats.TryGetValue(attacker, out PlayerStats winner)) winner.KnockOuts++;
            if (stats.TryGetValue(victim, out PlayerStats loser))
            {
                loser.TimesOut++;
                if (droppedCargo) loser.Drops++;
            }
            KnockOutRpc(attacker != null ? attacker.NetworkObjectId : NetIds.None, victim.NetworkObjectId, weapon);
        }

        /// <summary>Back at their own side's start for this half.</summary>
        public Transform RespawnSpot(PlayerAvatar avatar)
        {
            Transform[] spots = avatar.Team == CollectorTeam.Value ? layout.collectorSpawns : layout.interrupterSpawns;
            return spots[UnityEngine.Random.Range(0, spots.Length)];
        }

        public void ServerOnKnockedDown(PlayerAvatar avatar, bool droppedCargo)
        {
            AutoPilot.Log($"KNOCKDOWN {avatar.DisplayName} dropped={droppedCargo}");
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
            int rescued = HalfRescued.Value;
            int untouched = halfTotal - rescued - halfDestroyed - halfLost;
            AirportMatch airport = AirportMatch.Instance;
            string warehouse = airport != null ? $" sentOut={airport.Dispatched.Value}/{airport.Queued.Value}" : "";
            AutoPilot.Log($"HALF {half} seconds={Now - halfStartedAt:0} collectors={CollectorTeam.Value} rescued={rescued} destroyed={halfDestroyed} lost={halfLost} untouched={untouched} total={halfTotal} orange={OrangeScore.Value} blue={BlueScore.Value}{warehouse}");
            csv.AppendLine($"half,{half},{Now - halfStartedAt:0},{CollectorTeam.Value},{rescued},{halfDestroyed},{untouched},{halfTotal},{halfLost}");
        }

        private void FinishMatch()
        {
            List<ResultLine> lines = new List<ResultLine>();
            AddTitle(lines, GameText.TitleMover, s => s.Rescued);
            AddTitle(lines, GameText.TitleWrecker, s => s.Destroyed);
            AddTitle(lines, GameText.TitleBrawler, s => s.KnockOuts);
            AddTitle(lines, GameText.TitleButterfingers, s => s.Drops);
            AddTitle(lines, GameText.TitlePunchingBag, s => s.TimesOut);

            foreach (PlayerStats entry in stats.Values)
                csv.AppendLine($"player,{entry.Name},{entry.Team},{entry.Rescued:0},{entry.Destroyed:0},{entry.Rams},{entry.KnockedDown},{entry.Drops},{entry.Hits},{entry.KnockOuts},{entry.TimesOut}");
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
            if (hud != null) hud.ShowPopup(position, GameText.CargoValue(value), team.ToColor());
            if (rescued) GameAudio.Play("rescue");
            else GameAudio.PlayAt("break", position);
        }

        [Rpc(SendTo.Everyone)]
        private void LostRpc(Vector3 position, int value)
        {
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowPopup(position, GameText.CargoLost(value), new Color(0.72f, 0.82f, 0.9f));
        }

        [Rpc(SendTo.Everyone)]
        private void KnockOutRpc(ulong attackerId, ulong victimId, Weapon weapon)
        {
            PlayerAvatar attacker = PlayerAvatar.Find(attackerId);
            PlayerAvatar victim = PlayerAvatar.Find(victimId);
            if (victim == null || (attacker == null && weapon != Weapon.Fall)) return;
            AutoPilot.Log(weapon == Weapon.Fall ? $"KNOCKOUT {victim.DisplayName} with=Fall"
                : $"KNOCKOUT {attacker.DisplayName} on {victim.DisplayName} with={weapon}");
            MatchHud hud = MatchHud.Instance;
            if (hud != null) hud.ShowKnockOut(attacker, victim, weapon);
        }

        [Rpc(SendTo.Everyone)]
        private void ShowResultsRpc(ResultLine[] lines)
        {
            Results = lines;
            ResultsReady?.Invoke();
        }
    }
}
