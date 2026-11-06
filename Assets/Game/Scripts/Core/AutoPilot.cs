using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// Plays the game from command-line flags so builds can be tested without a person.
    /// Example: CargoClash.app/Contents/MacOS/Cargo\ Clash -ccMode practice -ccBotLocal -ccHalfSeconds 40 -ccQuit
    /// It does nothing unless -ccMode is present.
    /// </summary>
    public sealed class AutoPilot : MonoBehaviour
    {
        public static float HalfSecondsOverride;
        public static float SmashScaleOverride;
        public static ushort PortOverride;
        public static bool LocalIsBot;

        private string mode;
        private string address = "127.0.0.1";
        private string shotDirectory;
        private float shotEvery = 15f;
        private int expectClients = 2;
        private float timeoutSeconds = 600f;
        private bool quitAtEnd;
        private bool useUi;
        private bool again;
        private bool waitHostLeave;
        private bool forceCoCarry;
        private int errorCount;
        private int shotNumber;

        public static void Log(string message)
        {
            Debug.Log("[CCTEST] " + message);
        }

        public static void TryStart(GameObject rig)
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-ccMode") < 0) return;
            AutoPilot pilot = rig.AddComponent<AutoPilot>();
            pilot.Parse(args);
        }

        private void Parse(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string next = i + 1 < args.Length ? args[i + 1] : "";
                switch (args[i])
                {
                    case "-ccMode": mode = next; break;
                    case "-ccAddress": address = next; break;
                    case "-ccHalfSeconds": HalfSecondsOverride = float.Parse(next); break;
                    case "-ccSmashScale": SmashScaleOverride = float.Parse(next); break;
                    case "-ccPort": PortOverride = ushort.Parse(next); break;
                    case "-ccBotLocal": LocalIsBot = true; break;
                    case "-ccShotDir": shotDirectory = next; break;
                    case "-ccShotEvery": shotEvery = float.Parse(next); break;
                    case "-ccExpectClients": expectClients = int.Parse(next); break;
                    case "-ccTimeout": timeoutSeconds = float.Parse(next); break;
                    case "-ccQuit": quitAtEnd = true; break;
                    case "-ccUseUi": useUi = true; break;
                    case "-ccAgain": again = true; break;
                    case "-ccWaitHostLeave": waitHostLeave = true; break;
                    case "-ccForceCoCarry": forceCoCarry = true; break;
                }
            }
        }

        private void OnEnable()
        {
            Application.logMessageReceived += HandleLog;
        }

        private void OnDisable()
        {
            Application.logMessageReceived -= HandleLog;
        }

        private void HandleLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorCount++;
            if (errorCount <= 12) Debug.Log($"[CCTEST] ERROR #{errorCount}: {message}\n{stack}");
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            Log($"START mode={mode} halfSeconds={HalfSecondsOverride} localBot={LocalIsBot}");

            RoomService rooms = RoomService.Instance;
            NetworkManager net = NetworkManager.Singleton;
            if (!string.IsNullOrEmpty(shotDirectory) && mode != "menu") StartCoroutine(TakeScreenshots());
            if (forceCoCarry) StartCoroutine(ForceCoCarry());

            if (useUi)
            {
                yield return DriveMenus(net);
                mode += "-ui";
            }

            switch (mode)
            {
                case "practice":
                    rooms.StartPractice();
                    break;
                case "host":
                    rooms.StartLanHost();
                    while (SessionRoster.Instance == null || net.ConnectedClientsIds.Count < expectClients) yield return null;
                    SessionRoster.Instance.ServerSetFillWithBots(true);
                    yield return new WaitForSeconds(1f);
                    SessionRoster.Instance.ServerStartMatch();
                    break;
                case "client":
                    _ = rooms.JoinRoom(address);
                    break;
                case "menu":
                    FindAnyObjectByType<CargoClashStartMenu>().OnGoPressed();
                    yield return new WaitForSeconds(1f);
                    Shot();
                    yield return new WaitForSeconds(1f);
                    Application.Quit(0);
                    yield break;
                case "practice-ui":
                case "host-ui":
                case "client-ui":
                    break;
                default:
                    Log("UNKNOWN MODE " + mode);
                    Application.Quit(2);
                    yield break;
            }

            float startedAt = Time.realtimeSinceStartup;
            MatchPhase lastPhase = MatchPhase.Loading;
            while (Time.realtimeSinceStartup - startedAt < timeoutSeconds)
            {
                MatchDirector director = MatchDirector.Instance;
                if (director != null && director.IsSpawned)
                {
                    if (director.Phase.Value != lastPhase)
                    {
                        lastPhase = director.Phase.Value;
                        Log($"PHASE {lastPhase} half={director.Half.Value} collectors={director.CollectorTeam.Value} orange={director.OrangeScore.Value} blue={director.BlueScore.Value} avatars={PlayerAvatar.All.Count} cargo={CargoItem.All.Count}");
                    }
                    if (lastPhase == MatchPhase.Results && director.Results != null)
                    {
                        Log($"RESULT orange={director.OrangeScore.Value} blue={director.BlueScore.Value} titles={director.Results.Length}");
                        foreach (ResultLine line in director.Results)
                            Log($"TITLE {line.Title} -> {line.Player} ({line.Value})");
                        yield return new WaitForSeconds(1f);
                        Shot();
                        if (again)
                        {
                            again = false;
                            bool backToLobby = !RoomService.Instance.IsPractice;
                            if (MatchHud.Instance.againButton.gameObject.activeSelf)
                                MatchHud.Instance.againButton.onClick.Invoke();
                            yield return new WaitForSeconds(4f);
                            lastPhase = MatchPhase.Loading;
                            Shot();
                            if (backToLobby)
                            {
                                TitleFlow flow = FindAnyObjectByType<TitleFlow>();
                                Log($"BACK IN LOBBY title={flow != null} lobbyShown={flow != null && flow.lobbyPanel.activeSelf} players={SessionRoster.Instance?.Entries.Count}");
                                break;
                            }
                            continue;
                        }
                        break;
                    }
                }
                yield return null;
            }

            if (waitHostLeave)
            {
                while (net.IsListening && Time.realtimeSinceStartup - startedAt < timeoutSeconds) yield return null;
                yield return new WaitForSeconds(2f);
                TitleFlow flow = FindAnyObjectByType<TitleFlow>();
                Log($"AFTER HOST LEFT title={flow != null} status='{(flow != null ? flow.statusText.text : "")}'");
                Shot();
            }

            yield return new WaitForSeconds(2f);
            bool timedOut = Time.realtimeSinceStartup - startedAt >= timeoutSeconds;
            Log($"DONE errors={errorCount} timedOut={timedOut}");
            if (quitAtEnd) Application.Quit(errorCount > 0 || timedOut ? 1 : 0);
        }

        /// <summary>
        /// Puts two collectors on a large item each half, people before bots, so the co-carry path
        /// is exercised every run instead of only when bots happen to choose it.
        /// </summary>
        private IEnumerator ForceCoCarry()
        {
            int lastHalf = 0;
            while (true)
            {
                MatchDirector director = MatchDirector.Instance;
                if (director == null || !director.IsSpawned || !director.IsServer ||
                    director.Phase.Value != MatchPhase.Playing || director.Half.Value == lastHalf)
                {
                    yield return null;
                    continue;
                }

                lastHalf = director.Half.Value;
                yield return new WaitForSeconds(0.5f);

                List<PlayerAvatar> collectors = new List<PlayerAvatar>();
                foreach (PlayerAvatar avatar in PlayerAvatar.All)
                {
                    if (avatar.Side != Side.Collector) continue;
                    if (avatar.IsBotVar.Value) collectors.Add(avatar);
                    else collectors.Insert(0, avatar);
                }

                CargoItem large = null;
                foreach (CargoItem cargo in CargoItem.All)
                {
                    if (cargo.Definition.requiredCarriers > 1 && cargo.State.Value == CargoState.Ground) large = cargo;
                }
                if (large == null || collectors.Count < 2)
                {
                    Log($"COCARRY half={lastHalf} skipped large={large != null} collectors={collectors.Count}");
                    continue;
                }

                PlayerAvatar a = collectors[0];
                PlayerAvatar b = collectors[1];
                a.ServerStopActions();
                b.ServerStopActions();

                // The second half starts next to the truck, so the delivery itself gets tested every run.
                if (lastHalf == 2)
                {
                    LevelLayout layout = LevelLayout.Instance;
                    Vector3 nearTruck = layout.ClosestTruckZonePoint(Vector3.zero) + new Vector3(-10f, 0.55f, 0f);
                    large.transform.SetPositionAndRotation(nearTruck, Quaternion.identity);
                }

                Vector3 ground = large.transform.position;
                ground.y = 0f;
                a.TeleportRpc(ground + Vector3.left * 2f, Quaternion.identity);
                b.TeleportRpc(ground + Vector3.right * 2f, Quaternion.identity);
                yield return new WaitForSeconds(0.5f);

                bool grabbedA = large.ServerTryGrab(a);
                bool grabbedB = large.ServerTryGrab(b);
                Vector3 start = large.transform.position;
                Log($"COCARRY half={lastHalf} a={a.DisplayName} b={b.DisplayName} grabbed={grabbedA},{grabbedB} start={start}");
                yield return new WaitForSeconds(2.5f);
                Shot();
                yield return new WaitForSeconds(2.5f);
                Log($"COCARRY half={lastHalf} moved={Vector3.Distance(start, large.transform.position):0.0}m state={large.State.Value} a={a.transform.position} slotGap={Vector3.Distance(a.transform.position, b.transform.position):0.0}");
            }
        }

        /// <summary>Goes through the real title and lobby buttons, the way a player would.</summary>
        private IEnumerator DriveMenus(NetworkManager net)
        {
            yield return new WaitForSeconds(1f);
            TitleFlow flow = FindAnyObjectByType<TitleFlow>();
            flow.startMenu.OnGoPressed();
            yield return new WaitForSeconds(1f);

            if (mode == "practice")
            {
                flow.practiceButton.onClick.Invoke();
                yield break;
            }

            if (mode == "host")
            {
                flow.createButton.onClick.Invoke();
                while (SessionRoster.Instance == null || net.ConnectedClientsIds.Count < expectClients) yield return null;
                flow.fillButton.onClick.Invoke();
                yield return new WaitForSeconds(1f);
                flow.readyButton.onClick.Invoke();
                while (!SessionRoster.Instance.CanStart) yield return null;
                yield return new WaitForSeconds(1f);
                Log($"LOBBY code={RoomService.Instance.RoomCode} players={SessionRoster.Instance.Entries.Count} canStart={SessionRoster.Instance.CanStart}");
                Shot();
                yield return new WaitForSeconds(1f);
                flow.startButton.onClick.Invoke();
                yield break;
            }

            flow.codeInput.text = address;
            flow.joinButton.onClick.Invoke();
            while (SessionRoster.Instance == null || !SessionRoster.Instance.IsSpawned) yield return null;
            yield return new WaitForSeconds(1f);
            flow.readyButton.onClick.Invoke();
            Log($"JOINED code={RoomService.Instance.RoomCode}");
        }

        private IEnumerator TakeScreenshots()
        {
            while (true)
            {
                yield return new WaitForSeconds(shotEvery);
                Shot();
            }
        }

        private void Shot()
        {
            if (string.IsNullOrEmpty(shotDirectory)) return;
            Directory.CreateDirectory(shotDirectory);
            string path = Path.Combine(shotDirectory, $"{mode}-{shotNumber++:00}.png");
            ScreenCapture.CaptureScreenshot(path);
            Log("SHOT " + path);
        }
    }
}
