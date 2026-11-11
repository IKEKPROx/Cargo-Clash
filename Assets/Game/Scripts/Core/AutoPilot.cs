using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

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
        public static float CameraZoom;

        /// <summary>A test is running. It leaves the mouse pointer alone, because someone may be using the computer.</summary>
        public static bool Active;

        /// <summary>A test is sending real key and mouse events, so the window doesn't need to be in front.</summary>
        public static bool DrivesInput;

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
        private bool uncapped;
        private bool hideUi;
        private string tourSpots;
        private string eyeTourSpots;
        private bool driveInput;
        private float burstEvery;
        private int peopleHit;
        private int cargoSucked;
        private bool suckUntested;
        private int slamsLanded;
        private string startView;
        private BodyType body = BodyType.Medium;
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
            Active = true;
            AutoPilot pilot = rig.AddComponent<AutoPilot>();
            pilot.Parse(args);

            // Without a display nothing limits the frame rate, and thousands of frames per second
            // make a test behave unlike a real game. 60 is what players see.
            if (Application.isBatchMode) Application.targetFrameRate = 60;
            if (pilot.uncapped)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
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
                    case "-ccCameraZoom": CameraZoom = float.Parse(next); break;
                    case "-ccShotDir": shotDirectory = next; break;
                    case "-ccShotEvery": shotEvery = float.Parse(next); break;
                    case "-ccExpectClients": expectClients = int.Parse(next); break;
                    case "-ccTimeout": timeoutSeconds = float.Parse(next); break;
                    case "-ccQuit": quitAtEnd = true; break;
                    case "-ccUseUi": useUi = true; break;
                    case "-ccAgain": again = true; break;
                    case "-ccWaitHostLeave": waitHostLeave = true; break;
                    case "-ccForceCoCarry": forceCoCarry = true; break;
                    case "-ccUncapped": uncapped = true; break;
                    case "-ccTour": tourSpots = next; break;
                    case "-ccEyeTour": eyeTourSpots = next; break;
                    case "-ccDriveInput": driveInput = true; break;
                    case "-ccSwingBurst": burstEvery = float.Parse(next); break;
                    case "-ccView": startView = next; break;
                    case "-ccHideUi": hideUi = true; break;
                    case "-ccBody": body = (BodyType)Enum.Parse(typeof(BodyType), next, true); break;
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
            Log($"START mode={mode} halfSeconds={HalfSecondsOverride} localBot={LocalIsBot} body={body}");
            // A test plays as the body it's told to (medium unless -ccBody), whatever was last picked on this
            // computer. While a test runs, nothing it changes is saved.
            CrewLook look = GameSettings.Look;
            look.Body = body;
            GameSettings.Look = look;

            RoomService rooms = RoomService.Instance;
            NetworkManager net = NetworkManager.Singleton;
            if (!string.IsNullOrEmpty(shotDirectory) && mode != "menu") StartCoroutine(TakeScreenshots());
            if (forceCoCarry) StartCoroutine(ForceCoCarry());
            if (uncapped) StartCoroutine(MeasureFrames());
            if (!string.IsNullOrEmpty(tourSpots)) StartCoroutine(TourTheLevel());
            if (!string.IsNullOrEmpty(eyeTourSpots)) StartCoroutine(TourAtEyeLevel());
            if (driveInput) StartCoroutine(DriveWithKeysAndMouse());
            if (!string.IsNullOrEmpty(startView)) GameSettings.FirstPerson = startView != "third";

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
                    yield return TourTheMenus(net);
                    Log($"DONE errors={errorCount} timedOut=False");
                    Application.Quit(errorCount > 0 ? 1 : 0);
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
            bool checkedLook = false;
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
                    // What was picked on this computer has to be what everyone sees, clients included.
                    if (!checkedLook && lastPhase == MatchPhase.Playing && PlayerAvatar.Local != null)
                    {
                        checkedLook = true;
                        PlayerAvatar me = PlayerAvatar.Local;
                        Expect(me.Look.Equals(GameSettings.Look), $"the crew picked here is the one in the match (picked {GameSettings.Look.Body}, playing {me.Body})");
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
                            if (MatchHud.Instance.againButton.gameObject.activeInHierarchy)
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
        /// Points the camera at each "x,z" spot in turn and takes a picture, so every corner of the
        /// level can be checked without waiting for a bot to walk there.
        /// </summary>
        private IEnumerator TourTheLevel()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned || MatchDirector.Instance.Phase.Value != MatchPhase.Playing)
                yield return null;
            foreach (string spot in tourSpots.Split(';'))
            {
                string[] parts = spot.Split(',');
                FollowCamera.TestFocus = new Vector3(float.Parse(parts[0]), 0f, float.Parse(parts[1]));
                yield return new WaitForSeconds(1.2f);
                Shot();
                yield return new WaitForSeconds(0.2f);
            }
            FollowCamera.TestFocus = null;
        }

        /// <summary>Stands at eye height at each "x,z,yaw" spot and takes a picture, to check what the level looks like from the ground.</summary>
        private IEnumerator TourAtEyeLevel()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned || MatchDirector.Instance.Phase.Value != MatchPhase.Playing)
                yield return null;
            foreach (string spot in eyeTourSpots.Split(';'))
            {
                string[] parts = spot.Split(',');
                FollowCamera.TestEye = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                yield return new WaitForSeconds(0.8f);
                Shot();
                yield return new WaitForSeconds(0.2f);
            }
            FollowCamera.TestEye = null;
        }

        /// <summary>
        /// Plays the local character the way a person would, with real key and mouse events going through
        /// the Input System, so the bindings, the mouse look and aiming at cargo all get tested. It carries
        /// cargo to the truck as a collector, smashes it as an interrupter, and tries the other keys along the way.
        /// </summary>
        private IEnumerator DriveWithKeysAndMouse()
        {
            DrivesInput = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (InputDevice device in InputSystem.devices) InputSystem.DisableDevice(device);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("TestKeyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("TestMouse");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();

            GameConfig config = GameConfig.Get();
            NavMeshPath path = new NavMeshPath();
            float nextPathAt = 0f;
            CargoItem target = null;
            CargoItem carrying = null;
            bool testedKeys = false;
            bool pressedInteract = false;
            bool pressedRam = false;
            int grabs = 0;
            int delivered = 0;
            int hits = 0;
            int broken = 0;
            int rams = 0;
            int swingsAtPeople = 0;
            bool wasAimingAtPerson = false;
            int testedHitInHalf = 0;
            bool photographedCoCarry = false;
            bool photographedPickUp = false;
            int timesKnockedOut = 0;
            bool wasKnockedOut = false;
            int timesStaggered = 0;
            bool wasStaggered = false;
            float holdAfterHitUntil = 0f;
            float stuckFor = 0f;
            float longestStuck = 0f;
            float halfStartedAt = Time.time;
            float turned = 0f;
            float walked = 0f;
            float lastYaw = 0f;
            Vector3 lastPosition = Vector3.zero;
            float lastDurability = 1f;
            float nextRamAt = 12f;
            float startedPlaying = -1f;
            bool first = true;

            while (true)
            {
                MatchDirector director = MatchDirector.Instance;
                PlayerAvatar local = PlayerAvatar.Local;
                if (director == null || !director.IsSpawned || local == null || director.Phase.Value == MatchPhase.Results)
                {
                    if (director != null && director.IsSpawned && director.Phase.Value == MatchPhase.Results) break;
                    SendKeys(keyboard);
                    SendMouse(mouse, Vector2.zero, false, false);
                    yield return null;
                    continue;
                }

                if (director.Phase.Value != MatchPhase.Playing)
                {
                    SendKeys(keyboard);
                    SendMouse(mouse, Vector2.zero, false, false);
                    target = null;
                    first = true;
                    halfStartedAt = Time.time;
                    yield return null;
                    continue;
                }

                if (startedPlaying < 0f) startedPlaying = Time.time;
                if (first)
                {
                    first = false;
                    lastYaw = local.LookYaw;
                    lastPosition = local.transform.position;
                }
                turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, local.LookYaw));
                lastYaw = local.LookYaw;
                walked += Vector3.Distance(lastPosition, local.transform.position);
                lastPosition = local.transform.position;

                // The other keys, once, a little way in.
                if (!testedKeys && Time.time - startedPlaying > 6f)
                {
                    testedKeys = true;
                    yield return TestOtherKeys(keyboard, mouse, local);
                    lastPosition = local.transform.position;
                    continue;
                }

                if (local.KnockedOut.Value && !wasKnockedOut) timesKnockedOut++;
                wasKnockedOut = local.KnockedOut.Value;

                // Someone who's just been hit usually keeps the button held, and the next swing has to happen.
                bool staggeredNow = local.IsStaggered;
                if (staggeredNow && !wasStaggered) timesStaggered++;
                wasStaggered = staggeredNow;
                if (staggeredNow || local.IsKnockedDown) holdAfterHitUntil = Time.time + 1.2f;
                // A slam takes up the hammer until it's finished.
                bool shouldBeSwinging = mouse.leftButton.isPressed && local.CarriedCargo == null && !staggeredNow && !local.IsKnockedDown && !local.IsSlamming;
                stuckFor = shouldBeSwinging && !local.Swinging.Value ? stuckFor + Time.deltaTime : 0f;
                if (stuckFor > 0.7f && longestStuck <= 0.7f) Log("STUCK " + local.ToolState);
                longestStuck = Mathf.Max(longestStuck, stuckFor);

                // Once a half, as each side: hitting someone, and pictures of the hands if asked for.
                // After the other keys, and a client goes later, so that two computers testing this don't get in each other's way.
                float testHitAfter = NetworkManager.Singleton.IsServer ? 12f : 22f;
                if (testedHitInHalf != director.Half.Value && Time.time - halfStartedAt > testHitAfter && local.CarriedCargo == null && !local.IsKnockedDown)
                {
                    testedHitInHalf = director.Half.Value;
                    if (burstEvery > 0f) yield return PhotographTheHands(keyboard, mouse);
                    yield return LetGoOfCargo(keyboard, local);
                    yield return TestHittingSomeone(keyboard, mouse, local);
                    yield return LetGoOfCargo(keyboard, local);
                    yield return TestKnockingSomeoneOut(keyboard, mouse, local);
                    yield return LetGoOfCargo(keyboard, local);
                    if (local.Side == Side.Collector)
                    {
                        yield return TestPullingSomeone(keyboard, mouse, local);
                        yield return TestSucking(keyboard, mouse, local);
                    }
                    else
                    {
                        yield return TestSlamming(keyboard, mouse, local);
                    }
                    target = null;
                    first = true;
                    lastPosition = local.transform.position;
                    continue;
                }

                CargoItem carried = local.CarriedCargo;
                if (burstEvery > 0f && !photographedPickUp && carried != null && carried.Definition.requiredCarriers == 1 && carrying == null)
                {
                    photographedPickUp = true;
                    yield return PhotographPickingUp(keyboard, mouse);
                    lastYaw = local.LookYaw;
                    lastPosition = local.transform.position;
                }
                if (burstEvery > 0f && !photographedCoCarry && carried != null && carried.Definition.requiredCarriers > 1)
                {
                    photographedCoCarry = true;
                    yield return PhotographCoCarrying(keyboard, mouse);
                    lastYaw = local.LookYaw;
                    lastPosition = local.transform.position;
                    continue;
                }
                if (carried != carrying)
                {
                    if (carried != null) grabs++;
                    if (carrying != null && carried == null && carrying.State.Value == CargoState.Rescued) delivered++;
                    carrying = carried;
                }

                Vector3 goal = local.transform.position;
                bool go = false;
                bool wantGrab = false;
                bool wantSmash = false;
                if (carried != null)
                {
                    goal = LevelLayout.Instance.ClosestTruckZonePoint(local.transform.position);
                    go = true;
                }
                else
                {
                    bool interrupter = local.Side == Side.Interrupter;
                    if (target == null || target.State.Value != CargoState.Ground || !target.IsSpawned)
                    {
                        if (target != null && interrupter && target.State.Value == CargoState.Destroyed) broken++;
                        target = NearestCargoFor(local);
                        lastDurability = 1f;
                    }
                    if (target != null)
                    {
                        goal = target.transform.position;
                        go = target.EdgeDistance(local.transform.position) > (interrupter ? config.smashRange : config.pickupRange) * 0.55f;
                        if (interrupter)
                        {
                            wantSmash = local.AimedSmash == target;
                            if (target.Durability.Value < lastDurability)
                            {
                                hits++;
                                lastDurability = target.Durability.Value;
                            }
                        }
                        else
                        {
                            wantGrab = local.AimedGrab == target;
                        }
                    }
                }

                if (Time.time >= nextPathAt)
                {
                    nextPathAt = Time.time + 0.4f;
                    NavMesh.SamplePosition(local.transform.position, out NavMeshHit from, 3f, NavMesh.AllAreas);
                    NavMesh.SamplePosition(goal, out NavMeshHit to, 4f, NavMesh.AllAreas);
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                }
                Vector3 toward = goal;
                if (path.corners.Length > 1 && (path.corners[1] - local.transform.position).sqrMagnitude > 0.3f) toward = path.corners[1];
                else if (path.corners.Length > 2) toward = path.corners[2];
                Vector3 direction = toward - local.transform.position;
                direction.y = 0f;

                float wantYaw = direction.sqrMagnitude > 0.001f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : local.LookYaw;
                float degrees = Mathf.Clamp(Mathf.DeltaAngle(local.LookYaw, wantYaw), -9f, 9f);
                float perPixel = config.lookDegreesPerPixel * GameSettings.LookSpeed;
                Vector2 delta = new Vector2(degrees / perPixel, (local.LookPitch - 16f) * 0.15f / perPixel);
                bool facing = Mathf.Abs(Mathf.DeltaAngle(local.LookYaw, wantYaw)) < 35f;

                // A key only counts as pressed if it was up the frame before.
                bool interact = wantGrab && !pressedInteract;
                pressedInteract = interact;
                bool ram = Time.time - startedPlaying > nextRamAt && !pressedRam && carried == null && local.RamCooldown01 <= 0f;
                pressedRam = ram;
                if (ram)
                {
                    rams++;
                    nextRamAt = Time.time - startedPlaying + 7f;
                }

                // Anyone within reach gets hit, by either side, as long as the hands are free.
                bool aimingAtPerson = carried == null && local.AimedEnemy != null;
                if (aimingAtPerson && !wasAimingAtPerson) swingsAtPeople++;
                wasAimingAtPerson = aimingAtPerson;

                SendKeys(keyboard, go && facing ? Key.W : Key.None, interact ? Key.E : Key.None, ram ? Key.Space : Key.None);
                SendMouse(mouse, delta, wantSmash || aimingAtPerson || Time.time < holdAfterHitUntil, false);
                yield return null;
            }

            Log($"DRIVEN turned={turned:0} walked={walked:0} grabs={grabs} delivered={delivered} hits={hits} broken={broken} rams={rams} swingsAtPeople={swingsAtPeople} peopleHit={peopleHit} timesStaggered={timesStaggered} timesKnockedOut={timesKnockedOut} sucked={cargoSucked} slams={slamsLanded} longestStuck={longestStuck:0.00}");
            Expect(turned > 90f, "the mouse turned the view");
            Expect(walked > 10f, "W walked the character forward, whichever way it was looking");
            Expect(grabs > 0, "E picked up the cargo being aimed at");
            // Forced onto a big chest, there may be no time left to carry anything else.
            if (!forceCoCarry) Expect(delivered > 0, "cargo carried by hand reached the truck");
            Expect(hits > 0, "holding the mouse button landed hammer hits");
            Expect(broken > 0, "hammer hits broke cargo");
            if (!forceCoCarry)
            {
                Expect(peopleHit > 0, "hitting someone got tested");
                // Only when there was nothing left to try it on. Hanging on for 3 s with nothing coming in is a failure.
                Expect(cargoSucked > 0 || suckUntested, "right click sucked cargo into the hands");
                Expect(slamsLanded > 0, "a charged slam landed");
            }
            // A slow frame can take up a good part of this by itself, as it can for anyone.
            Expect(longestStuck < 1f, "a held button keeps swinging after being hit or ramming");
        }

        /// <summary>Just after picking something up, and walking with it. For checking how the hands hold it.</summary>
        private IEnumerator PhotographPickingUp(Keyboard keyboard, Mouse mouse)
        {
            SendMouse(mouse, Vector2.zero, false, false);
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                Shot();
                yield return new WaitForSeconds(0.04f);
            }
            float ends = Time.time + 0.8f;
            while (Time.time < ends)
            {
                SendKeys(keyboard, Key.W);
                yield return new WaitForSeconds(0.2f);
                Shot();
            }
            SendKeys(keyboard);
        }

        /// <summary>A run of pictures close together: running, standing still, then swinging. For checking that the hands move well.</summary>
        private IEnumerator PhotographTheHands(Keyboard keyboard, Mouse mouse)
        {
            float ends = Time.time + 1.1f;
            while (Time.time < ends)
            {
                SendKeys(keyboard, Key.W);
                SendMouse(mouse, Vector2.zero, false, false);
                yield return new WaitForSeconds(burstEvery);
                Shot();
            }
            SendKeys(keyboard);
            yield return new WaitForSeconds(0.5f);
            Shot();
            ends = Time.time + 1.1f;
            while (Time.time < ends)
            {
                SendMouse(mouse, Vector2.zero, true, false);
                yield return new WaitForSeconds(burstEvery);
                Shot();
            }
            SendMouse(mouse, Vector2.zero, false, false);
            yield return new WaitForSeconds(0.5f);

            // The flourish on F, all the way through.
            FirstPersonRig rig = FirstPersonRig.Instance;
            if (rig == null || !rig.ShowsTool) yield break;
            SendKeys(keyboard, Key.F);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            while (rig.Inspecting)
            {
                yield return new WaitForSeconds(burstEvery * 2.5f);
                Shot();
            }

            // The right button, held and then let go.
            SendMouse(mouse, Vector2.zero, false, true);
            ends = Time.time + 1.1f;
            while (Time.time < ends)
            {
                yield return new WaitForSeconds(burstEvery * 2f);
                Shot();
            }
            SendMouse(mouse, Vector2.zero, false, false);
            ends = Time.time + 0.6f;
            while (Time.time < ends)
            {
                yield return new WaitForSeconds(burstEvery);
                Shot();
            }
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>A look all the way round while carrying the big chest, for checking the hands from every side of it.</summary>
        private IEnumerator PhotographCoCarrying(Keyboard keyboard, Mouse mouse)
        {
            GameConfig config = GameConfig.Get();
            float pixelsPerStep = 45f / (config.lookDegreesPerPixel * GameSettings.LookSpeed);
            for (int step = 0; step < 8; step++)
            {
                SendKeys(keyboard);
                SendMouse(mouse, new Vector2(pixelsPerStep, 0f), false, false);
                yield return null;
                SendMouse(mouse, Vector2.zero, false, false);
                yield return new WaitForSeconds(burstEvery);
                Shot();
            }
        }

        /// <summary>
        /// The rule for left click: the first hit pushes someone back and stops them, and a second straight
        /// after knocks them down. On a client this goes over the network both ways. A client can't be made
        /// safe from a passing bot, so it gets a few tries.
        /// </summary>
        private IEnumerator TestHittingSomeone(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            bool host = NetworkManager.Singleton.IsServer;
            string side = local.Side.ToString();
            for (int attempt = 0; attempt < 4; attempt++)
            {
                PlayerAvatar enemy = ChooseSomeoneToHit(local);
                if (enemy == null) yield break;

                // So that a passing bot can't knock the tester over halfway through.
                if (host) local.ImmuneUntil.Value = local.Now + 3.5;

                bool collector = local.Side == Side.Collector;
                // A blast has to reach farther than the hammer could.
                float apart = collector ? GameConfig.Get().hitRange + 0.5f : 1.5f;
                int healthBefore = enemy.Health.Value;
                bool staggered = false;
                bool heldStaggered = false;
                bool interrupted = false;
                float ends = Time.time + 2.5f;
                while (Time.time < ends && !enemy.IsKnockedDown && !interrupted)
                {
                    if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing)
                    {
                        SendMouse(mouse, Vector2.zero, false, false);
                        yield break;
                    }
                    interrupted = local.IsKnockedDown || local.IsStaggered || (enemy.IsImmune && !enemy.IsKnockedDown);

                    // Keeps them just in front, however they try to walk away. Only the host can move other people.
                    if (host)
                    {
                        Vector3 inFront = local.transform.position + Quaternion.Euler(0f, local.LookYaw, 0f) * Vector3.forward * apart;
                        enemy.TeleportRpc(inFront, Quaternion.LookRotation(local.transform.position - inFront));
                    }
                    else
                    {
                        Vector3 away = local.transform.position - enemy.transform.position;
                        away.y = 0f;
                        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                        local.TeleportRpc(enemy.transform.position + away * apart, Quaternion.LookRotation(-away));
                    }
                    if (enemy.IsStaggered)
                    {
                        staggered = true;
                        heldStaggered |= !enemy.Swinging.Value;
                    }
                    SendKeys(keyboard);
                    SendMouse(mouse, Vector2.zero, !interrupted, false);
                    yield return null;
                }
                SendMouse(mouse, Vector2.zero, false, false);

                // Someone else got to one of them first, so this says nothing about the rule. Go again.
                if (interrupted || (enemy.IsKnockedDown && !staggered))
                {
                    while (local.IsKnockedDown || local.IsStaggered) yield return null;
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                string where = host ? "host" : "client";
                Expect(staggered && heldStaggered, $"as {side} on the {where}, the first hit staggers someone and stops them swinging");
                Expect(enemy.IsKnockedDown, $"as {side} on the {where}, a second hit in a row knocks them down");
                Expect(enemy.Health.Value < healthBefore, $"as {side} on the {where}, hits take health");
                peopleHit++;

                // Flat on the ground, or just back up, they can't be hit again.
                float immuneEnds = Time.time + 1f;
                while (Time.time < immuneEnds)
                {
                    SendMouse(mouse, Vector2.zero, false, false);
                    yield return null;
                }
                Expect(!local.Swinging.Value, "the swing stops when the button comes up");
                yield break;
            }
        }

        /// <summary>Out of health: down for a while, everyone hears about it, and back at the start with full health. Only the host can set someone's health.</summary>
        private IEnumerator TestKnockingSomeoneOut(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            if (!NetworkManager.Singleton.IsServer) yield break;
            GameConfig config = GameConfig.Get();
            float waited = 0f;
            PlayerAvatar enemy = ChooseSomeoneToHit(local);
            while (enemy == null && waited < 4f)
            {
                waited += Time.deltaTime;
                yield return null;
                enemy = ChooseSomeoneToHit(local);
            }
            if (enemy == null || MatchDirector.Instance.Phase.Value != MatchPhase.Playing) yield break;

            local.ImmuneUntil.Value = local.Now + 3f;
            enemy.Health.Value = 1;
            float apart = local.Side == Side.Collector ? 2.5f : 1.5f;
            float ends = Time.time + 2f;
            while (Time.time < ends && !enemy.KnockedOut.Value)
            {
                Vector3 inFront = local.transform.position + Quaternion.Euler(0f, local.LookYaw, 0f) * Vector3.forward * apart;
                enemy.TeleportRpc(inFront, Quaternion.LookRotation(local.transform.position - inFront));
                SendKeys(keyboard);
                SendMouse(mouse, Vector2.zero, true, false);
                yield return null;
            }
            SendMouse(mouse, Vector2.zero, false, false);
            Expect(enemy.KnockedOut.Value, "running out of health knocks someone out");
            Expect(enemy.Health.Value == 0, "someone knocked out has no health");
            Vector3 fellAt = enemy.transform.position;

            ends = Time.time + config.respawnSeconds + 0.6f;
            while (Time.time < ends)
            {
                if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing) yield break;
                yield return null;
            }
            Expect(!enemy.KnockedOut.Value && enemy.Health.Value == enemy.MaxHealth, "someone knocked out comes back with full health");
            Expect(enemy.IsImmune, "someone who's just come back can't be hit straight away");
            Expect(Vector3.Distance(enemy.transform.position, fellAt) > 2f, "someone knocked out comes back at their own start");
        }

        /// <summary>Most of what these tests try needs empty hands.</summary>
        private static IEnumerator LetGoOfCargo(Keyboard keyboard, PlayerAvatar local)
        {
            if (local.CarriedCargo == null) yield break;
            SendKeys(keyboard, Key.E);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>From a good way off, cargo comes to you and ends up in your hands.</summary>
        private IEnumerator TestSucking(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            GameConfig config = GameConfig.Get();
            string untested = "interrupted every time";
            // Standing still with both hands busy, this is the easiest time for a bot to knock you over.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing)
                {
                    untested = "the half ended";
                    break;
                }
                while (local.IsKnockedDown || local.IsStaggered) yield return null;
                yield return LetGoOfCargo(keyboard, local);

                // Late in a half, everything small may already have been carried off or broken.
                CargoItem cargo = NearestSmallCargoFor(local);
                if (cargo == null)
                {
                    untested = "no small cargo left on the ground";
                    break;
                }
                Vector3 away = local.transform.position - cargo.transform.position;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                float distance = config.suckRange * 0.6f;
                Vector3 spot = cargo.transform.position + away * distance;
                if (NavMesh.SamplePosition(spot, out NavMeshHit onGround, 3f, NavMesh.AllAreas)) spot = onGround.position;
                Vector3 toCargo = cargo.transform.position - spot;
                toCargo.y = 0f;
                local.TeleportRpc(spot, Quaternion.LookRotation(toCargo));
                yield return null;
                yield return null;

                bool interrupted = false;
                float ends = Time.time + 3f;
                while (Time.time < ends && local.CarriedCargo == null)
                {
                    if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing || local.IsKnockedDown || local.IsStaggered)
                    {
                        interrupted = true;
                        break;
                    }
                    SendKeys(keyboard);
                    SendMouse(mouse, Vector2.zero, false, true);
                    yield return null;
                }
                SendMouse(mouse, Vector2.zero, false, false);
                yield return null;
                // Something nearer the mouth may have come in first, which is just as good.
                if (local.CarriedCargo != null)
                {
                    cargoSucked++;
                    yield break;
                }
                // Someone else picking it up, breaking it or delivering it first isn't the vacuum's fault either.
                if (interrupted || cargo == null || cargo.State.Value != CargoState.Ground) continue;
                Log($"SUCK nothing came in: {cargo.name} distance={cargo.EdgeDistance(local.transform.position):0.00}");
                yield break;
            }
            suckUntested = true;
            Log("SUCK untested: " + untested);
        }

        /// <summary>The vacuum also drags people. Only a bot's owner is this computer, so only the host can see that happening.</summary>
        private IEnumerator TestPullingSomeone(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            if (!NetworkManager.Singleton.IsServer) yield break;
            // A bot may have just knocked you over, and nobody can use the vacuum from the ground.
            while (local.IsKnockedDown || local.IsStaggered) yield return null;
            if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing || local.Side != Side.Collector) yield break;

            PlayerAvatar enemy = null;
            foreach (PlayerAvatar other in PlayerAvatar.All)
                if (other.Team != local.Team && other.IsBotVar.Value && !other.IsKnockedDown) enemy = other;
            if (enemy == null) yield break;

            local.ImmuneUntil.Value = local.Now + 1.5;
            bool pulled = false;
            float ends = Time.time + 1f;
            while (Time.time < ends && !pulled)
            {
                Vector3 inFront = local.transform.position + Quaternion.Euler(0f, local.LookYaw, 0f) * Vector3.forward * 4f;
                enemy.TeleportRpc(inFront, Quaternion.LookRotation(local.transform.position - inFront));
                SendKeys(keyboard);
                SendMouse(mouse, Vector2.zero, false, true);
                pulled = enemy.IsBeingPulled;
                yield return null;
            }
            SendMouse(mouse, Vector2.zero, false, false);
            Expect(pulled, "the vacuum drags people in front of it");
        }

        /// <summary>Hold the right button until it's ready, and letting go knocks down whoever's in front.</summary>
        private IEnumerator TestSlamming(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            GameConfig config = GameConfig.Get();
            bool host = NetworkManager.Singleton.IsServer;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing) yield break;
                while (local.IsKnockedDown || local.IsStaggered) yield return null;
                PlayerAvatar enemy = ChooseSomeoneToHit(local);
                if (enemy == null)
                {
                    yield return new WaitForSeconds(0.6f);
                    continue;
                }

                if (host) local.ImmuneUntil.Value = local.Now + 2.5;
                bool interrupted = false;
                float ready = Time.time + config.chargeSeconds + 0.2f;
                float ends = ready + 0.5f;
                bool charged = false;
                while (Time.time < ends && !enemy.IsKnockedDown && !interrupted)
                {
                    if (MatchDirector.Instance.Phase.Value != MatchPhase.Playing) break;
                    interrupted = local.IsKnockedDown || local.IsStaggered;
                    if (host)
                    {
                        Vector3 inFront = local.transform.position + Quaternion.Euler(0f, local.LookYaw, 0f) * Vector3.forward * 1.6f;
                        enemy.TeleportRpc(inFront, Quaternion.LookRotation(local.transform.position - inFront));
                    }
                    else
                    {
                        Vector3 away = local.transform.position - enemy.transform.position;
                        away.y = 0f;
                        away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                        local.TeleportRpc(enemy.transform.position + away * 1.6f, Quaternion.LookRotation(-away));
                    }
                    charged |= local.IsCharging && local.Charge01 >= 1f;
                    SendKeys(keyboard);
                    SendMouse(mouse, Vector2.zero, false, Time.time < ready && !interrupted);
                    yield return null;
                }
                SendMouse(mouse, Vector2.zero, false, false);
                if (charged && enemy.IsKnockedDown)
                {
                    slamsLanded++;
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        private static CargoItem NearestSmallCargoFor(PlayerAvatar local)
        {
            CargoItem best = null;
            float bestDistance = float.MaxValue;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value != CargoState.Ground || cargo.Definition.size != CargoSize.Small) continue;
                float distance = cargo.EdgeDistance(local.transform.position);
                if (distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Another person if there is one, because that's what goes over the network.</summary>
        private static PlayerAvatar ChooseSomeoneToHit(PlayerAvatar local)
        {
            PlayerAvatar best = null;
            foreach (PlayerAvatar other in PlayerAvatar.All)
            {
                if (other.Team == local.Team || other.IsKnockedDown || other.IsImmune) continue;
                if (best == null || !other.IsBotVar.Value) best = other;
            }
            return best;
        }

        private IEnumerator TestOtherKeys(Keyboard keyboard, Mouse mouse, PlayerAvatar local)
        {
            bool wasFirstPerson = GameSettings.FirstPerson;
            SendKeys(keyboard, Key.V);
            SendMouse(mouse, Vector2.zero, false, false);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            yield return null;
            yield return null;
            Expect(GameSettings.FirstPerson != wasFirstPerson, "V switches the view");
            SendKeys(keyboard, Key.V);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            yield return null;
            Expect(GameSettings.FirstPerson == wasFirstPerson, "V switches the view back");

            // One quick click, with the button straight back up, still throws a whole swing. Hands have to be free
            // for that and for F, and the tall body is fast enough to be carrying something already.
            yield return LetGoOfCargo(keyboard, local);
            if (local.CarriedCargo == null && !local.IsKnockedDown && !local.IsStaggered)
            {
                SendMouse(mouse, Vector2.zero, true, false);
                yield return null;
                yield return null;
                SendMouse(mouse, Vector2.zero, false, false);
                yield return null;
                yield return null;
                yield return null;
                Expect(local.Swinging.Value, "a quick click keeps swinging after the button is up");
                float quickClickEnds = Time.time + 0.7f;
                while (Time.time < quickClickEnds) yield return null;
                Expect(!local.Swinging.Value, "the swing ends once the button is up");

                // Switching input method changes the keyboard's layout, and the Input System rebinds every action
                // when that happens. Someone holding a button down shouldn't have to let go and press it again.
                SendMouse(mouse, Vector2.zero, true, false);
                yield return new WaitForSeconds(0.1f);
                InputSystem.QueueConfigChangeEvent(keyboard);
                yield return new WaitForSeconds(0.5f);
                if (!local.IsKnockedDown && !local.IsStaggered) Expect(local.Swinging.Value, "a held button survives the keyboard layout changing");
                SendMouse(mouse, Vector2.zero, false, false);
                yield return new WaitForSeconds(0.7f);
            }


            // F shows off what's in your hands, and using it stops that. Only in first person, where you can see them.
            FirstPersonRig rig = FirstPersonRig.Instance;
            if (rig != null && rig.ShowsTool)
            {
                yield return new WaitForSeconds(0.3f);
                SendKeys(keyboard, Key.F);
                yield return null;
                yield return null;
                SendKeys(keyboard);
                yield return null;
                Expect(rig.Inspecting, "F starts inspecting what's in your hands");
                yield return new WaitForSeconds(0.25f);
                Expect(rig.Inspecting, "inspecting carries on by itself");
                SendMouse(mouse, Vector2.zero, true, false);
                yield return null;
                yield return null;
                SendMouse(mouse, Vector2.zero, false, false);
                yield return null;
                yield return null;
                yield return null;
                Expect(!rig.Inspecting, "attacking stops inspecting");
                yield return new WaitForSeconds(0.7f);
            }

            // A real mouse sends nothing while it's still, and the view has to stop turning when it does.
            SendMouse(mouse, new Vector2(60f, 0f), false, false);
            yield return null;
            yield return null;
            float yawAfterMove = local.LookYaw;
            for (int i = 0; i < 5; i++) yield return null;
            Expect(Mathf.Abs(Mathf.DeltaAngle(yawAfterMove, local.LookYaw)) < 0.01f, "the view stops turning when the mouse stops");

            // Turning with the arrow keys, for anyone without a mouse.
            // Knocked flat, you can't look around, so that wouldn't say anything about the key.
            float yawBefore = local.LookYaw;
            bool leftAlone = !local.IsKnockedDown;
            for (int i = 0; i < 12; i++)
            {
                SendKeys(keyboard, Key.RightArrow);
                yield return null;
                leftAlone &= !local.IsKnockedDown;
            }
            SendKeys(keyboard);
            yield return null;
            if (leftAlone) Expect(Mathf.DeltaAngle(yawBefore, local.LookYaw) > 1f, "the right arrow key turns the view");

            // With the pause menu open, the game ignores the keys and the mouse.
            SendKeys(keyboard, Key.Escape);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            yield return null;
            Expect(MatchHud.Instance.MenuOpen, "Esc opens the pause menu");
            Expect(!MatchHud.Instance.WantsCursorLocked, "the pointer is free for the pause menu");
            yield return new WaitForSeconds(0.9f);
            yield return ShotThenWait();
            yawBefore = local.LookYaw;
            Vector3 positionBefore = local.transform.position;
            for (int i = 0; i < 10; i++)
            {
                SendKeys(keyboard, Key.W);
                SendMouse(mouse, new Vector2(40f, 0f), false, false);
                yield return null;
            }
            SendKeys(keyboard);
            SendMouse(mouse, Vector2.zero, false, false);
            yield return null;
            Expect(Mathf.Abs(Mathf.DeltaAngle(yawBefore, local.LookYaw)) < 0.01f, "the view doesn't turn behind the pause menu");
            Expect(Vector3.Distance(positionBefore, local.transform.position) < 0.3f, "the character doesn't walk behind the pause menu");
            float speedBefore = GameSettings.LookSpeed;
            MatchHud.Instance.lookSpeedSlider.value = 1.5f;
            Expect(Mathf.Approximately(GameSettings.LookSpeed, 1.5f), "the slider sets the look speed");
            GameSettings.LookSpeed = speedBefore;
            MatchHud.Instance.lookSpeedSlider.SetValueWithoutNotify(speedBefore);

            SendKeys(keyboard, Key.Escape);
            yield return null;
            yield return null;
            SendKeys(keyboard);
            yield return null;
            Expect(!MatchHud.Instance.MenuOpen, "Esc closes the pause menu");
        }

        private static void SendKeys(Keyboard keyboard, params Key[] keys)
        {
            List<Key> down = new List<Key>();
            foreach (Key key in keys)
                if (key != Key.None) down.Add(key);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(down.ToArray()));
        }

        private static void SendMouse(Mouse mouse, Vector2 delta, bool left, bool right)
        {
            MouseState state = new MouseState { delta = delta, position = new Vector2(400f, 300f) };
            if (left) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            if (right) state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right);
            InputSystem.QueueStateEvent(mouse, state);
        }

        private static CargoItem NearestCargoFor(PlayerAvatar local)
        {
            CargoItem best = null;
            float bestDistance = float.MaxValue;
            foreach (CargoItem cargo in CargoItem.All)
            {
                if (cargo.State.Value != CargoState.Ground || cargo.Definition.requiredCarriers > 1) continue;
                float distance = cargo.EdgeDistance(local.transform.position);
                if (distance < bestDistance)
                {
                    best = cargo;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>Logs the frame rate every few seconds, to compare how heavy two builds are.</summary>
        private IEnumerator MeasureFrames()
        {
            while (true)
            {
                int frames = Time.frameCount;
                float started = Time.realtimeSinceStartup;
                yield return new WaitForSecondsRealtime(5f);
                float seconds = Time.realtimeSinceStartup - started;
                Log($"FPS {(Time.frameCount - frames) / seconds:0}");
            }
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

        private void Expect(bool passed, string what)
        {
            if (!passed) errorCount++;
            Log($"CHECK {(passed ? "ok" : "FAILED")}: {what}");
        }

        /// <summary>Clicks a button the way the mouse would, so it fails if something is covering the button.</summary>
        private void Click(Button button)
        {
            // Without a display nothing is drawn, so there is nothing for a click to hit.
            if (Application.isBatchMode)
            {
                button.onClick.Invoke();
                return;
            }

            RectTransform rect = (RectTransform)button.transform;
            PointerEventData pointer = new PointerEventData(EventSystem.current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)),
            };
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            bool reached = hits.Count > 0 && hits[0].gameObject == button.gameObject;
            if (!reached)
            {
                errorCount++;
                Log($"CHECK FAILED: {button.name} under {rect.parent.name} can't be clicked. Top hit: {(hits.Count > 0 ? hits[0].gameObject.name : "nothing")}");
                return;
            }
            ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>Goes through every screen of the start scene with the real buttons, and takes a picture of each.</summary>
        private IEnumerator TourTheMenus(NetworkManager net)
        {
            TitleFlow flow = FindAnyObjectByType<TitleFlow>();
            GameConfig config = GameConfig.Get();
            yield return new WaitForSeconds(3f);
            Expect(flow.titlePanel.activeSelf, "the game opens on the title");
            yield return ShotThenWait();
            yield return new WaitForSeconds(6f);
            yield return ShotThenWait();

            // The button held down, to see its face sink.
            PointerEventData press = new PointerEventData(EventSystem.current);
            ExecuteEvents.Execute(flow.playButton.gameObject, press, ExecuteEvents.pointerDownHandler);
            yield return new WaitForSeconds(0.4f);
            yield return ShotThenWait();
            ExecuteEvents.Execute(flow.playButton.gameObject, press, ExecuteEvents.pointerUpHandler);

            Click(flow.playButton);
            yield return new WaitForSeconds(0.6f);
            yield return ShotThenWait();
            yield return new WaitForSeconds(2f);
            Expect(flow.menuPanel.activeSelf && !flow.titlePanel.activeSelf, "Play opens the menu");
            yield return ShotThenWait();

            flow.codeInput.text = "NOSUCHROOM";
            yield return null;
            yield return null;
            Click(flow.joinButton);
            yield return new WaitForSeconds(0.5f);
            Expect(flow.menuPanel.activeSelf && flow.statusText.text.Length > 0, "a code that can't work leaves a message on the menu");
            yield return ShotThenWait();
            flow.codeInput.text = "";
            yield return new WaitForSeconds(0.2f);

            Click(flow.crewButton);
            yield return new WaitForSeconds(1.2f);
            Expect(flow.crewPanel.activeSelf && flow.preview.gameObject.activeSelf, "Crew opens the crew page with the preview");
            yield return ShotThenWait();
            // The look starts as whatever was last picked on this computer, so the next hat and glasses are counted from it.
            CrewLook before = GameSettings.Look;
            Click(flow.bodyButtons[(int)BodyType.Tall]);
            Click(flow.hatNext);
            Click(flow.glassesNext);
            yield return new WaitForSeconds(0.6f);
            Expect(GameSettings.Look.Body == BodyType.Tall && GameSettings.Look.Hat == (before.Hat + 1) % (CrewLook.Hats.Length + 1) &&
                GameSettings.Look.Glasses == (before.Glasses + 1) % (CrewLook.GlassesStyles.Length + 1),
                "the body, hat and glasses buttons change the look");
            Expect(flow.statValues[0].text == config.tallBody.maxHealth.ToString(), "the stats follow the body");
            yield return ShotThenWait();
            Click(flow.bodyButtons[(int)BodyType.Short]);
            Click(flow.crewRandomButton);
            Click(flow.crewOutfitButton);
            yield return new WaitForSeconds(0.6f);
            Expect(GameSettings.Look.Body == BodyType.Short, "Random keeps the body");
            yield return ShotThenWait();
            flow.faceSliders[5].value = 1f;
            yield return null;
            Expect(GameSettings.Look.FaceValue(5) > 0.9f, "a face slider changes the face");
            Click(flow.crewBackButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.menuPanel.activeSelf && !flow.crewPanel.activeSelf && !flow.preview.gameObject.activeSelf, "Back leaves the crew page");

            Click(flow.practiceButton);
            yield return new WaitForSeconds(1.5f);
            Expect(flow.levelPanel.activeSelf, "Practice opens the level select");
            Expect(flow.statusText.text.Length == 0, "the old message is gone");
            yield return ShotThenWait();

            int locked = 0;
            for (int i = 0; i < flow.levelCards.Length; i++)
            {
                if (config.Level(i).IsReady) continue;
                locked++;
                Click(flow.levelCards[i].button);
                yield return null;
                Expect(flow.levelPanel.activeSelf && !net.IsListening, $"locked level {i + 1} starts nothing");
            }
            Log($"LEVELS total={config.levels.Length} locked={locked} cards={flow.levelCards.Length}");
            yield return new WaitForSeconds(0.1f);
            yield return ShotThenWait();

            Click(flow.levelCards[0].button);
            yield return new WaitForSeconds(1f);
            Expect(flow.crewPanel.activeSelf && !net.IsListening, "practice goes through the crew page before it starts");
            Click(flow.crewBackButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.levelPanel.activeSelf, "Back from there returns to the level select");

            Click(flow.levelBackButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.menuPanel.activeSelf, "Back returns to the menu");

            Click(flow.createButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.levelPanel.activeSelf, "Create Room opens the level select");
            Click(flow.levelCards[0].button);
            yield return new WaitForSeconds(2f);
            Expect(flow.lobbyPanel.activeSelf, "choosing the tutorial opens the lobby");
            Expect(SessionRoster.Instance != null && SessionRoster.Instance.Level.Value == 0, "the room knows its level");
            Click(flow.fillButton);
            Click(flow.readyButton);
            yield return new WaitForSeconds(0.5f);
            yield return ShotThenWait();

            Click(flow.lobbyCrewButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.crewPanel.activeSelf, "the lobby's Crew button opens the crew page");
            Click(flow.bodyButtons[(int)BodyType.Medium]);
            yield return new WaitForSeconds(0.5f);
            Expect(SessionRoster.Instance.TryGetEntry(net.LocalClientId, out RosterEntry mine) && mine.Look.Body == BodyType.Medium,
                "a change on the crew page reaches the room");
            Click(flow.crewStartButton);
            yield return new WaitForSeconds(1f);
            Expect(flow.lobbyPanel.activeSelf, "Done returns to the lobby");

            Click(flow.leaveButton);
            yield return new WaitForSeconds(1.5f);
            Expect(flow.menuPanel.activeSelf && !net.IsListening, "Leave returns to the menu");
            yield return ShotThenWait();
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>A screenshot is taken at the end of the frame, so this keeps later changes out of the picture.</summary>
        private IEnumerator ShotThenWait()
        {
            Shot();
            yield return null;
        }

        /// <summary>Goes through the real title and lobby buttons, the way a player would.</summary>
        private IEnumerator DriveMenus(NetworkManager net)
        {
            yield return new WaitForSeconds(1f);
            TitleFlow flow = FindAnyObjectByType<TitleFlow>();
            Click(flow.playButton);
            yield return new WaitForSeconds(1f);

            if (mode == "practice")
            {
                Click(flow.practiceButton);
                yield return new WaitForSeconds(1f);
                Click(flow.levelCards[0].button);
                yield return new WaitForSeconds(1f);
                Click(flow.crewStartButton);
                yield break;
            }

            if (mode == "host")
            {
                Click(flow.createButton);
                yield return new WaitForSeconds(1f);
                Click(flow.levelCards[0].button);
                while (SessionRoster.Instance == null || net.ConnectedClientsIds.Count < expectClients) yield return null;
                yield return new WaitForSeconds(1f);
                Click(flow.fillButton);
                yield return new WaitForSeconds(1f);
                Click(flow.readyButton);
                while (!SessionRoster.Instance.CanStart) yield return null;
                yield return new WaitForSeconds(1f);
                Log($"LOBBY code={RoomService.Instance.RoomCode} players={SessionRoster.Instance.Entries.Count} canStart={SessionRoster.Instance.CanStart}");
                Shot();
                yield return new WaitForSeconds(1f);
                Click(flow.startButton);
                yield break;
            }

            flow.codeInput.text = address;
            yield return null;
            yield return null;
            Click(flow.joinButton);
            while (SessionRoster.Instance == null || !SessionRoster.Instance.IsSpawned) yield return null;
            yield return new WaitForSeconds(1f);
            Click(flow.readyButton);
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
            if (hideUi)
            {
                foreach (Canvas canvas in FindObjectsByType<Canvas>()) canvas.enabled = false;
            }
            string path = Path.Combine(shotDirectory, $"{mode}-{shotNumber++:00}.png");
            ScreenCapture.CaptureScreenshot(path);
            Log("SHOT " + path);
        }
    }
}
