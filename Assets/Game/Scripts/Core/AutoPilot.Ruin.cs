using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace CargoClash
{
    /// <summary>Opt-in host-side runtime checks: -ccMode practice -ccLevel 4 -ccRuinTest.</summary>
    public sealed partial class AutoPilot
    {
        private bool ruinTest;

        [System.Serializable] private sealed class RuinTestMap
        {
            public Vector4[] pits;
            public Vector3[] pitReturns, vinePickups;
            public Vector3 boostStart, boostExit, ladderStart, ladderExit, truckZone;
        }

        private IEnumerator TestTheRuins()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned ||
                   MatchDirector.Instance.Phase.Value != MatchPhase.Playing || PlayerAvatar.Local == null)
                yield return null;
            PlayerAvatar local = PlayerAvatar.Local;
            RuinCourtyard ruins = RuinCourtyard.Instance;
            Expect(ruins != null, "RUIN test runs in the ruin scene");
            if (ruins == null) yield break;
            GameConfig config = GameConfig.Get();
            LevelLayout layout = LevelLayout.Instance;
            RuinTestMap map = JsonUtility.FromJson<RuinTestMap>(Resources.Load<TextAsset>("RuinCourtyard/RuinLayout").text);
            BotsIdle = true;
            double cargoDeadline = MatchDirector.Instance.Now + 5;
            while (CargoItem.All.Count < 43 && MatchDirector.Instance.Now < cargoDeadline) yield return null;
            CheckRuinCargo(config, layout);
            if (HalfSecondsOverride <= 0)
                Expect(Mathf.Approximately(config.EffectiveHalfSeconds, 120f), "RUIN defaults to two minutes per half");
            Expect(layout.collectorSpawns.Length == layout.interrupterSpawns.Length &&
                   GameObject.Find("Spawn assembly markers") == null,
                "RUIN both teams have equal spawn slots and nothing is painted on the ground for them");
            if (!local.IsServer)
            {
                // The client must not deliver unrelated cargo during the host's score-isolation checks.
                BotsIdle = true;
                bool wall = false, ladder = false, coCarry = false;
                int half = MatchDirector.Instance.Half.Value;
                while (MatchDirector.Instance.Half.Value == half && MatchDirector.Instance.Phase.Value == MatchPhase.Playing)
                {
                    ulong bits = MatchDirector.Instance.RuinBits.Value;
                    wall |= (bits & 3UL) == 2UL;
                    ladder |= (bits & (1UL << 51)) != 0;
                    coCarry |= CargoItem.All.Any(c => c.Definition.value == 100 && !c.HasFreeSlot && c.State.Value == CargoState.Carried);
                    yield return null;
                }
                Expect(wall, "RUIN client observes the wall break");
                Expect(ladder, "RUIN client observes the replicated ladder state");
                Expect(coCarry, "RUIN client observes two carriers on a 100 crate");
                BotsIdle = false;
                yield break;
            }
            PlayerAvatar[] collectors = PlayerAvatar.All.Where(p => p.Side == Side.Collector && p.IsOwner).ToArray();
            PlayerAvatar interrupter = PlayerAvatar.All.FirstOrDefault(p => p.Side == Side.Interrupter && p.IsOwner);
            Expect(collectors.Length >= 2 && interrupter != null, "RUIN has host-owned actors on both sides");
            if (collectors.Length < 2 || interrupter == null)
            {
                BotsIdle = false;
                yield break;
            }
            PlayerAvatar a = collectors[0], b = collectors[1];
            PlayerAvatar[] actors = { a, b, interrupter };
            Vector3[] starts = actors.Select(p => p.transform.position).ToArray();
            MatchDirector director = MatchDirector.Instance;
            BotsIdle = true;
            try
            {
                Expect(layout.collectorSpawns.Concat(layout.interrupterSpawns).All(spot =>
                    Physics.Raycast(spot.position + Vector3.up * .3f, Vector3.down, .6f, GameLayers.LevelMask)),
                    "RUIN ground collider supports both teams' spawn points");
                Vector3 road = new Vector3(map.truckZone.x - 3.4f, .05f, -9f);
                Vector3 apron = road + Vector3.forward * 6f;
                NavMeshPath roadPath = new NavMeshPath();
                Expect(Physics.Linecast(apron + Vector3.up * .5f, road + Vector3.up * .5f, GameLayers.LevelMask) &&
                       !(NavMesh.CalculatePath(apron, road, NavMesh.AllAreas, roadPath) &&
                         roadPath.status == NavMeshPathStatus.PathComplete),
                    "RUIN the road guardrail, crushed low at the gap, keeps players and bots off the road");
                Expect(ruins.MayaBlockerCount >= 20 &&
                       Physics.Raycast(new Vector3(17f, 1f, 2f), Vector3.forward, 4f, GameLayers.LevelMask) &&
                       Physics.Raycast(new Vector3(113f, 1f, 52f), Vector3.left, 3f, GameLayers.LevelMask),
                    "RUIN the six Maya structures inside the play area block players");
                // The play area was drawn in to x 10-115, north to z 66.4.
                Expect(Physics.Raycast(new Vector3(12f, 1f, 44f), Vector3.left, 2.5f, GameLayers.LevelMask) &&
                       Physics.Raycast(new Vector3(113f, 1f, 44f), Vector3.right, 2.5f, GameLayers.LevelMask) &&
                       Physics.Raycast(new Vector3(40f, 1f, 64.5f), Vector3.forward, 2.5f, GameLayers.LevelMask) &&
                       layout.mapArea.xMin == 10f && Mathf.Approximately(layout.mapArea.xMax, 115f) && Mathf.Approximately(layout.mapArea.yMax, 66.4f),
                    "RUIN the drawn-in edges (x 10-115, z 66.4) stop players and frame the minimap");
                Vector3 truckGoal = map.truckZone + Vector3.back * 4.2f;
                bool allReach = true;
                // Every spot, the terrace and the open vaults included, with all walls standing.
                foreach (Transform spot in layout.collectorSpawns.Concat(layout.interrupterSpawns).Concat(layout.smallSpots)
                             .Concat(layout.mediumSpots).Concat(layout.largeSpots))
                {
                    NavMeshPath route = new NavMeshPath();
                    if (!NavMesh.SamplePosition(spot.position, out NavMeshHit from, 2.5f, NavMesh.AllAreas) ||
                        !NavMesh.CalculatePath(from.position, truckGoal, NavMesh.AllAreas, route) ||
                        route.status != NavMeshPathStatus.PathComplete)
                    {
                        allReach = false;
                        Log($"RUIN no route from {spot.name} {spot.position} to the truck");
                    }
                }
                Expect(allReach, "RUIN every spawn and cargo spot still has a walking route to the truck");
                Log("RUIN blockers dropped with their cleared models: " + string.Join(", ", ruins.DroppedBlockers));
                Expect(!ruins.DroppedBlockers.Any(name => name.StartsWith("U2_") || name.StartsWith("U3_") ||
                                                          name.StartsWith("U4_") || name.StartsWith("RuinButtress")),
                    "RUIN the rampart keeps the old gate frames and buttress it joins");
                Expect(ruins.PitSignSolids >= 4 && Physics.Raycast(new Vector3(23.6f, .3f, 33.5f), Vector3.forward, 2.5f, GameLayers.LevelMask),
                    $"RUIN the pit warning signs are solid ({ruins.PitSignSolids} pieces)");
                Expect(ruins.ClearedClutter > 30, $"RUIN loose rocks and grass clumps are cleared off the ground ({ruins.ClearedClutter})");
                Expect(ruins.ClearedIslands > 20,
                    $"RUIN shipped trees, rocks and rim plants are cleared under the new structures ({ruins.ClearedIslands})");
                Expect(ruins.MayaTrackCount == 4 && ruins.DepthOfFieldReady,
                    "RUIN tyre tracks are painted and the far blur is on the main camera");
                Vector3[] tape = ruins.CordonRoute;
                Expect(tape != null && tape.Length == 4 && tape.All(p => p.z > .3f),
                    "RUIN warning tape runs only along the west, north and east edges, never toward the road");
                Expect(Physics.Raycast(new Vector3(30f, .5f, -3f), Vector3.back, 4f, GameLayers.LevelMask) &&
                       Physics.Raycast(new Vector3(100f, .5f, -3.5f), Vector3.back, 4f, GameLayers.LevelMask) &&
                       NavMesh.SamplePosition(new Vector3(30f, 0f, -4.5f), out _, .5f, NavMesh.AllAreas),
                    "RUIN the strip between the ruin and the road is playable and ends at the guardrail");
                int ramp = 0;
                foreach ((Vector3 foot, Vector3 top) in ruins.MayaRamps)
                {
                    ramp++;
                    Vector3 up = Vector3.ProjectOnPlane(top - foot, Vector3.up).normalized;
                    Vector3 summit = top + up * .3f;
                    yield return StandRuin(a, foot - up * 1.2f + Vector3.up * .05f, summit);
                    yield return WalkMayaRamp(a, foot - up * 1.2f, summit, ramp);
                }
                yield return StandRuin(a, starts[0], starts[0] + Vector3.forward);
                foreach (Vector4 rect in map.pits)
                {
                    Vector3 pit = RuinPitCenter(rect, .1f);
                    Expect(!Physics.Raycast(pit, Vector3.down, .6f, GameLayers.LevelMask), "RUIN pit has an actual ground opening");
                    Expect(!NavMesh.SamplePosition(pit, out _, .25f, NavMesh.AllAreas), "RUIN navigation does not bridge a pit");
                }

                Expect(!Physics.Linecast(new Vector3(44.5f, 1f, 31f), new Vector3(44.5f, 1f, 35f), GameLayers.LevelMask) &&
                       !Physics.Linecast(new Vector3(82f, 1f, 32f), new Vector3(82f, 1f, 36f), GameLayers.LevelMask),
                    "RUIN both vaults stand open without gates");

                // Five breakable walls are left: the three rampart gates and the two vault side walls.
                Expect(ruins.WallCount == 5 && new[] { "R3", "R4", "R5", "R9", "R10" }.All(id => ruins.WallIndex(id) >= 0),
                    "RUIN keeps five breakable walls: rampart gates R3, R5, R4 and vault sides R9, R10");
                int r3 = ruins.WallIndex("R3"), r4 = ruins.WallIndex("R4"), r5 = ruins.WallIndex("R5");
                ulong WallBits(int index) => director.RuinBits.Value >> (index * 2) & 3UL;
                // Feet south of a rampart gate (inside its stone frame) and the middle of its face.
                Vector3 GateFeet(int index, float back) => ruins.WallCentre(index) - Vector3.up * ruins.WallCentre(index).y + Vector3.back * back;
                Vector3 GateFace(int index) => new Vector3(ruins.WallCentre(index).x, 1f, ruins.WallCentre(index).z);

                // The rampart: its gates standing, the interrupters go round an end to reach the truck and the
                // collectors round an end to reach the vaults; broken, both go straight through.
                float toTruckIntact = RuinRouteLength(layout.interrupterSpawns[0].position, truckGoal);
                float toVaultIntact = RuinRouteLength(layout.collectorSpawns[2].position, layout.largeSpots[0].position);

                // One hammer blow knocks R3 down; sucking it back takes the full restore time.
                yield return StandRuin(interrupter, GateFeet(r3, 1.6f), GateFace(r3));
                Vector3 hitAim = (GateFace(r3) - interrupter.EyePosition).normalized;
                int wallIndex = ruins.FindWall(interrupter, hitAim, false);
                Expect(wallIndex == r3, "RUIN hammer aims at R3");
                ruins.ServerHitWall(interrupter, wallIndex, hitAim);
                Expect(WallBits(r3) == 2UL, "RUIN one hammer blow knocks R3 down");
                yield return StandRuin(interrupter, starts[2], starts[2] + Vector3.forward);
                yield return StandRuin(a, GateFeet(r3, 2.2f), GateFace(r3));
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                float until = Time.time + config.ruinRestoreSeconds - .4f;
                while (Time.time < until)
                {
                    ruins.ServerRestoreWall(a, Time.deltaTime, (GateFace(r3) - a.EyePosition).normalized);
                    yield return null;
                }
                Expect(WallBits(r3) == 2UL, $"RUIN sucking a wall back takes the full {config.ruinRestoreSeconds:0.#} s");
                until = Time.time + .75f;
                while (Time.time < until)
                {
                    ruins.ServerRestoreWall(a, Time.deltaTime, (GateFace(r3) - a.EyePosition).normalized);
                    yield return null;
                }
                Expect(WallBits(r3) == 0UL, "RUIN collector sucks R3 back up");

                // A collector's blower knocks R4 down in exactly three blasts.
                yield return StandRuin(a, GateFeet(r4, 1.8f), GateFace(r4));
                Vector3 blastAim = (GateFace(r4) - a.EyePosition).normalized;
                int blasted = ruins.FindWall(a, blastAim, false);
                // Each blast knocks out another batch of its stones; the last brings the rest down.
                Expect(ruins.StoneCount(r4) > 12 && ruins.StonesStanding(r4) == ruins.StoneCount(r4),
                    $"RUIN R4 is a stack of {ruins.StoneCount(r4)} separate stones, all in place");
                int standingBefore = ruins.StonesStanding(r4);
                ulong[] afterBlast = { 1UL, 3UL, 2UL };
                for (int blast = 1; blast <= ruins.BlastsToBreak; blast++)
                {
                    ruins.ServerHitWall(a, blasted, blastAim);
                    yield return null;
                    int standing = ruins.StonesStanding(r4);
                    bool last = blast == ruins.BlastsToBreak;
                    Expect(blasted == r4 && WallBits(r4) == (last ? 2UL : afterBlast[blast - 1]) &&
                           standing < standingBefore && (standing == 0) == last,
                        $"RUIN blower blast {blast} of {ruins.BlastsToBreak} at R4 " +
                        (last ? "brings the rest down" : $"knocks out stones ({standingBefore} -> {standing} left)"));
                    standingBefore = standing;
                    yield return new WaitForSeconds(.3f);
                }
                // Knocked-out stones land and stay, solid, on the far side from the blower.
                yield return new WaitForSeconds(1.2f);
                var lying = ruins.LyingStones(r4);
                Expect(lying.Count == ruins.StoneCount(r4) &&
                       lying.All(stone => stone.center.z > ruins.WallCentre(r4).z + .3f &&
                                          Physics.Raycast(stone.center + Vector3.up, Vector3.down, out RaycastHit top, 2f,
                                              GameLayers.LevelMask, QueryTriggerInteraction.Ignore) &&
                                          Mathf.Abs(top.point.y - stone.max.y) < .03f),
                    $"RUIN all {lying.Count} R4 stones lie solid beyond the wall, away from the blower");
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                // Standing right in the gap, where people stand to suck it back: the wall still comes back, and
                // they step out of it.
                yield return StandRuin(a, GateFeet(r4, 0f), GateFace(r4) + Vector3.forward);
                until = Time.time + config.ruinRestoreSeconds + .35f;
                while (Time.time < until && WallBits(r4) != 0UL)
                {
                    ruins.ServerRestoreWall(a, Time.deltaTime, Vector3.forward);
                    yield return null;
                }
                yield return new WaitForSeconds(.3f);
                Expect(WallBits(r4) == 0UL && !ruins.WallBounds(r4).Intersects(a.BodyBoundsAt(a.transform.position)),
                    $"RUIN collector standing in the R4 gap sucks it back up and is moved out of it ({a.transform.position:F2})");
                yield return new WaitForSeconds(1.2f);
                Expect(ruins.StonesStanding(r4) == ruins.StoneCount(r4) && ruins.LyingStones(r4).Count == 0,
                    "RUIN every R4 stone flies back into place and no solid stone is left lying");

                // The same with the real buttons: hold left click at R5 until it falls, then right click until it
                // stands again. The blasts land on the swing rhythm, so it takes three of them.
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                yield return StandRuin(a, GateFeet(r5, 1.8f), GateFace(r5));
                if (a == PlayerAvatar.Local) Shot("blow-down-prompt");
                int mostBlasts = 0;
                float pressed = Time.time;
                BotBrain.TestAttacker = a;
                ulong shownBits = 0UL;
                float shotAt = float.PositiveInfinity;
                while (WallBits(r5) != 2UL && Time.time < pressed + 8f)
                {
                    mostBlasts = Mathf.Max(mostBlasts, ruins.WallBlasts(r5));
                    // A picture of the stones in the air after each blast.
                    if (WallBits(r5) != shownBits) { shownBits = WallBits(r5); shotAt = Time.time + .18f; }
                    if (Time.time >= shotAt) { Shot($"wall-blast-{ruins.WallBlasts(r5)}"); shotAt = float.PositiveInfinity; }
                    yield return null;
                }
                yield return new WaitForSeconds(.2f);
                Shot("wall-blast-down");
                yield return null;
                BotBrain.TestAttacker = null;
                Expect(WallBits(r5) == 2UL && mostBlasts == ruins.BlastsToBreak - 1,
                    $"RUIN holding the blower at R5 blows it down on blast {ruins.BlastsToBreak} ({Time.time - pressed:F1} s)");
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .3f);
                pressed = Time.time;
                BotBrain.TestSucker = a;
                while (WallBits(r5) != 0UL && Time.time < pressed + 6f) yield return null;
                float sucked = Time.time - pressed;
                BotBrain.TestSucker = null;
                yield return new WaitForSeconds(.25f);
                Shot("wall-stones-returning");
                yield return null;
                Expect(WallBits(r5) == 0UL && sucked >= config.ruinRestoreSeconds - .05f,
                    $"RUIN holding suction at R5 puts it back after {sucked:F1} s");
                yield return StandRuin(a, starts[0], starts[0] + Vector3.forward);

                // All three gates down: both sides have a shorter way.
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                int[] gates = { r3, r5, r4 };
                foreach (int i in gates)
                {
                    yield return StandRuin(interrupter, GateFeet(i, 1.6f), GateFace(i));
                    Vector3 aim = (GateFace(i) - interrupter.EyePosition).normalized;
                    int found = ruins.FindWall(interrupter, aim, false);
                    ruins.ServerHitWall(interrupter, found, aim);
                    Vector3 feet = GateFeet(i, 1.6f);
                    Expect(found == i && WallBits(i) == 2UL &&
                           !Physics.Linecast(feet + Vector3.up, feet + Vector3.forward * 3.2f + Vector3.up, GameLayers.LevelMask),
                        $"RUIN hammer breaks rampart gate {i + 1} and opens its passage");
                }
                yield return new WaitForSeconds(.4f);
                float toTruckOpen = RuinRouteLength(layout.interrupterSpawns[0].position, truckGoal);
                float toVaultOpen = RuinRouteLength(layout.collectorSpawns[2].position, layout.largeSpots[0].position);
                Expect(toTruckOpen < toTruckIntact - 8f,
                    $"RUIN broken rampart gates give interrupters a shorter way to the truck ({toTruckIntact:F0} m -> {toTruckOpen:F0} m)");
                Expect(toVaultOpen < toVaultIntact - 8f,
                    $"RUIN broken rampart gates give collectors a shorter way to the cargo ({toVaultIntact:F0} m -> {toVaultOpen:F0} m)");
                yield return StandRuin(interrupter, starts[2], starts[2] + Vector3.forward);
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                foreach (int i in gates)
                {
                    yield return StandRuin(a, GateFeet(i, 2.2f), GateFace(i));
                    float restoreUntil = Time.time + config.ruinRestoreSeconds + .35f;
                    while (Time.time < restoreUntil)
                    {
                        ruins.ServerRestoreWall(a, Time.deltaTime, (GateFace(i) - a.EyePosition).normalized);
                        yield return null;
                    }
                    Expect(WallBits(i) == 0UL, $"RUIN collector restores rampart gate {i + 1}");
                }

                Expect(ruins.VinePickupCount == 0 && GameObject.Find("VinePickup_01") == null,
                    "RUIN has no vine coils to pick up");

                // No coils in the level now; hand a vine over to keep checking the ladder rule.
                a.HasRuinVine.Value = true;
                Vector3 ladder = map.ladderStart + Vector3.up * .85f;
                yield return StandRuin(a, map.ladderStart + Vector3.right * .7f, ladder);
                UseRuinTest(ruins, a, ladder);
                Expect(a.RuinClimbing.Value && !a.HasRuinVine.Value, "RUIN ladder consumes a vine and begins climbing");
                yield return new WaitForSeconds(config.ruinClimbSeconds + .4f);
                Expect(!a.RuinClimbing.Value && Vector3.Distance(a.transform.position, map.ladderExit) < .3f,
                    "RUIN ladder arrives at the inward safe landing");

                Vector3 boost = map.boostStart + Vector3.up;
                yield return StandRuin(a, map.boostStart, boost);
                yield return StandRuin(b, map.boostStart - Vector3.right, boost);
                UseRuinTest(ruins, a, boost);
                UseRuinTest(ruins, b, boost);
                yield return new WaitForSeconds(config.ruinBoostSeconds + .4f);
                Expect(!b.RuinClimbing.Value && Vector3.Distance(b.transform.position, map.boostExit) < .3f, "RUIN two teammates can boost one onto the terrace");
                UseRuinTest(ruins, b, b.EyePosition + b.transform.forward);
                yield return new WaitForSeconds(config.ruinBoostSeconds + .4f);
                Expect(!a.RuinClimbing.Value && a.transform.position.y > map.boostExit.y - .2f, "RUIN upper teammate can pull the helper up");

                int orange = director.OrangeScore.Value, blue = director.BlueScore.Value;
                CargoItem lost = CargoItem.All.First(c => c.State.Value == CargoState.Ground && c.Definition.value == 10);
                Vector3 cargoStart = lost.transform.position;
                Expect(ruins.PitStairPaths != null && ruins.PitStairPaths.Length == map.pits.Length,
                    "RUIN every pit has an authored stair escape route");
                if (ruins.PitStairPaths != null && ruins.PitStairPaths.Length == map.pits.Length)
                    for (int i = 0; i < map.pits.Length; i++)
                    {
                        Vector3[] path = ruins.PitStairPaths[i];
                        Vector3 bottom = path[0];
                        Expect(config.ruinPitReturnHeight < bottom.y - .5f && !ruins.TryPitReturn(bottom, out _),
                            $"RUIN pit {i + 1} floor is above the failsafe threshold");
                        BoxCollider box = lost.GetComponent<BoxCollider>();
                        float boxBottom = (box.center.y - box.size.y * .5f) * lost.transform.localScale.y;
                        Vector3 cargoBottom = bottom + Vector3.forward * .85f;
                        lost.Body.position = cargoBottom + Vector3.up * (.12f - boxBottom);
                        lost.Body.linearVelocity = lost.Body.angularVelocity = Vector3.zero;
                        yield return new WaitForSeconds(.6f);
                        Expect(Vector2.Distance(new Vector2(lost.transform.position.x, lost.transform.position.z),
                                   new Vector2(cargoBottom.x, cargoBottom.z)) < .3f &&
                               Mathf.Abs(lost.transform.position.y + boxBottom - bottom.y) < .2f,
                            $"RUIN cargo remains accessible on pit {i + 1} floor");
                        yield return StandRuin(a, bottom, lost.transform.position);
                        yield return new WaitForSeconds(.4f);
                        Expect(Vector3.Distance(a.transform.position, bottom) < .2f,
                            $"RUIN player stays on pit {i + 1} floor");
                        a.TestGrab(lost);
                        yield return new WaitForSeconds(.2f);
                        Expect(a.CarriedCargo == lost, $"RUIN player can recover a 10 box from pit {i + 1} floor");
                        yield return WalkRuinStairs(a, path, i + 1);
                        Expect(a.CarriedCargo == lost && lost.transform.position.y > path[path.Length - 1].y,
                            $"RUIN player carries the 10 box up pit {i + 1} stairs to ground level");
                        if (a.CarriedCargo == lost) a.TestLetGo();
                        yield return new WaitForSeconds(.2f);
                        lost.Body.position = RuinPitCenter(map.pits[i], config.ruinPitReturnHeight - 1);
                        lost.Body.linearVelocity = lost.Body.angularVelocity = Vector3.zero;
                        yield return new WaitForSeconds(.4f);
                        Expect(Vector3.Distance(lost.transform.position, cargoStart) < .3f,
                            $"RUIN cargo recovers to its actual spawn only below pit {i + 1} failsafe");
                        yield return StandRuin(a, RuinPitCenter(map.pits[i], config.ruinPitReturnHeight - 1), map.pitReturns[i]);
                        yield return new WaitForSeconds(.4f);
                        Expect(Vector3.Distance(a.transform.position, map.pitReturns[i]) < .3f,
                            $"RUIN player recovers from below pit {i + 1} failsafe");
                    }
                Expect(director.OrangeScore.Value == orange && director.BlueScore.Value == blue, "RUIN pit recovery and stair traversal do not change scores");

                CargoItem large = CargoItem.All.First(c => c.State.Value == CargoState.Ground && c.Definition.value == 100);
                Vector3 axis = large.transform.right;
                float reach = large.Definition.coCarryReach;
                Expect(reach > 0 && Mathf.Approximately(large.CoCarryLift, large.Definition.coCarryHeight),
                    "RUIN Art crate uses Astra's authored carry reach and lift");
                Physics.Raycast(large.transform.position + Vector3.up * .5f, Vector3.down, out RaycastHit largeFloor, 4f,
                    GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
                Vector3 firstFeet = large.transform.position - axis * reach;
                firstFeet.y = largeFloor.point.y;
                yield return StandRuin(a, firstFeet, large.transform.position);
                a.TestGrab(large);
                yield return new WaitForSeconds(.2f);
                yield return StandRuin(b, large.transform.position+axis*reach-Vector3.up*large.CoCarryLift, large.transform.position);
                b.TestGrab(large);
                yield return new WaitForSeconds(.3f);
                Expect(a.CarriedCargo == large && b.CarriedCargo == large && !large.HasFreeSlot, "RUIN Astra Art 100 crate accepts two carriers");
                Expect(new[] { a, b }.All(actor => Vector3.Distance(actor.transform.position,
                        large.GrabPointWorld(actor) - Vector3.up * large.CoCarryLift) < .12f) &&
                       Mathf.Abs(Vector3.Dot(large.GrabPointWorld(a) - large.transform.position, large.transform.forward)) < .01f,
                    "RUIN co-carry feet and grips follow the actual cargo axis, lift and reach");
                Vector4 edge = map.pits[0];
                // Use the north/east rim, away from the stairs; one grip extends into the void.
                Vector3 rim = Mathf.Abs(axis.z) > Mathf.Abs(axis.x)
                    ? new Vector3(edge.z - 1.5f, large.CoCarryLift, edge.w + .4f)
                    : new Vector3(edge.z + .4f, large.CoCarryLift, edge.w - 2.8f);
                large.transform.position = rim;
                yield return new WaitForSeconds(.4f);
                Expect(large.State.Value == CargoState.Ground && a.CarriedCargo == null && b.CarriedCargo == null,
                    "RUIN co-carry releases when one carrier loses pit-edge support while idle");
                large.ServerResetFromRuinPit();
                if (ruins.PitStairPaths != null)
                    for (int i = 0; i < ruins.PitStairPaths.Length; i++)
                    {
                        large.ServerResetFromRuinPit();
                        Vector3 bottom = ruins.PitStairPaths[i][0];
                        large.Body.position = bottom + Vector3.up * large.CoCarryLift;
                        large.transform.position = large.Body.position;
                        large.Body.linearVelocity = large.Body.angularVelocity = Vector3.zero;
                        Vector3 firstGrip = large.transform.position - large.transform.right * reach - Vector3.up * large.CoCarryLift;
                        Vector3 secondGrip = large.transform.position + large.transform.right * reach - Vector3.up * large.CoCarryLift;
                        bool firstFloor = CargoItem.TryRuinCarryFloor(firstGrip, out float firstY);
                        bool secondFloor = CargoItem.TryRuinCarryFloor(secondGrip, out float secondY);
                        Expect(firstFloor && secondFloor, $"RUIN pit {i + 1} co-carry setup has a floor at each grip");
                        firstGrip.y = firstY;
                        secondGrip.y = secondY;
                        yield return StandRuin(a, firstGrip, large.transform.position);
                        a.TestGrab(large);
                        yield return new WaitForSeconds(.2f);
                        yield return StandRuin(b, secondGrip, large.transform.position);
                        b.TestGrab(large);
                        yield return new WaitForSeconds(.3f);
                        Expect(a.CarriedCargo == large && b.CarriedCargo == large,
                            $"RUIN two players pick up the 100 box at pit {i + 1} bottom");
                        yield return CoCarryRuinStairs(a, b, large, ruins.PitStairPaths[i], i + 1);
                    }
                large.ServerResetFromRuinPit();
                Expect(director.OrangeScore.Value == orange && director.BlueScore.Value == blue,
                    "RUIN carrying both cargo sizes out of pits does not award delivery points");
                CargoItem thrown = CargoItem.All.First(c => c.State.Value == CargoState.Ground && c.Definition.value == 10 && c != lost);
                yield return TestRuinDelivery(ruins, layout, a, thrown);
                Log("RUIN interaction suite finished");
            }
            finally
            {
                BotBrain.TestAttacker = BotBrain.TestSucker = null;
                a.TestCoCarryMove(Vector2.zero);
                b.TestCoCarryMove(Vector2.zero);
                for (int i = 0; i < actors.Length; i++) actors[i].TeleportRpc(starts[i], Quaternion.identity);
                BotsIdle = false;
            }
        }

        private void CheckRuinCargo(GameConfig config, LevelLayout layout)
        {
            Expect(CargoItem.All.Count == 43, "RUIN spawns exactly 43 boxes");
            foreach (var tier in new[] { (10, 23), (30, 17), (100, 3) })
                Expect(CargoItem.All.Count(c => c.Definition.value == tier.Item1) == tier.Item2,
                    $"RUIN value {tier.Item1} has {tier.Item2} boxes");
            Expect(CargoItem.All.Sum(c => c.Definition.value) == 1040, "RUIN total available score is 1040");
            Expect(config.ruinCargo.Length == 4, "RUIN uses four isolated Astra definitions");
            foreach (var expected in new[] { (CargoKind.Tobacco, 12), (CargoKind.Cash, 11), (CargoKind.Gold, 17), (CargoKind.Art, 3) })
            {
                CargoDefinition definition = config.ruinCargo.FirstOrDefault(d => d.kind == expected.Item1);
                CargoDefinition original = config.cargo.FirstOrDefault(d => d.kind == expected.Item1);
                Expect(definition != null && original != null && definition != original &&
                       definition.prefab.GetComponent<CargoItem>().Definition == definition &&
                       definition.prefab.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)
                           .SequenceEqual(original.prefab.GetComponentsInChildren<MeshFilter>(true).Select(m => m.sharedMesh)),
                    $"RUIN {expected.Item1} variant keeps current GitHub geometry and its own definition");
                CargoItem[] boxes = CargoItem.All.Where(c => c.Definition.kind == expected.Item1).ToArray();
                Expect(boxes.Length == expected.Item2 && boxes.All(c => c.Definition == definition),
                    $"RUIN {expected.Item1} has {expected.Item2} correctly defined network instances");
            }
            foreach (CargoSize size in new[] { CargoSize.Small, CargoSize.Medium, CargoSize.Large })
                foreach (Transform spot in layout.SpotsFor(size))
                {
                    CargoItem cargo = CargoItem.All.Where(c => c.Definition.size == size)
                        .OrderBy(c => (Vector3.ProjectOnPlane(c.transform.position - spot.position, Vector3.up)).sqrMagnitude)
                        .FirstOrDefault();
                    Expect(cargo != null && Vector3.Distance(cargo.transform.position,
                            CargoItem.RuinSpawnPosition(cargo.Definition, spot)) < .15f,
                        $"RUIN {spot.name} sits on its floor using the actual prefab dimensions");
                }
        }

        /// <summary>Walking distance between two points on the navigation mesh, or infinity with no way through.</summary>
        private static float RuinRouteLength(Vector3 from, Vector3 to)
        {
            NavMeshPath route = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 2.5f, NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to, out NavMeshHit end, 2.5f, NavMesh.AllAreas) ||
                !NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, route) ||
                route.status != NavMeshPathStatus.PathComplete) return float.PositiveInfinity;
            float length = 0f;
            for (int i = 1; i < route.corners.Length; i++) length += Vector3.Distance(route.corners[i - 1], route.corners[i]);
            return length;
        }

        private IEnumerator WalkRuinStairs(PlayerAvatar actor, Vector3[] path, int pit)
        {
            CharacterController body = actor.GetComponent<CharacterController>();
            Expect(actor.IsOwner && body != null && body.enabled, $"RUIN pit {pit} walk uses the owned CharacterController");
            if (body == null || !body.enabled || !actor.IsOwner) yield break;
            for (int waypoint = 1; waypoint < path.Length; waypoint++)
            {
                float deadline = Time.time + 3;
                Vector3 goal = path[waypoint];
                while (Time.time < deadline)
                {
                    Vector3 delta = Vector3.ProjectOnPlane(goal - actor.transform.position, Vector3.up);
                    if (delta.magnitude < .12f && Mathf.Abs(actor.transform.position.y - goal.y) < .16f) break;
                    // Only horizontal input and gravity: the real controller must step up the stone colliders.
                    body.Move(Vector3.ClampMagnitude(delta, 2.4f * Time.deltaTime) + Vector3.down * (2 * Time.deltaTime));
                    yield return null;
                }
                if (Time.time >= deadline)
                {
                    Expect(false, $"RUIN pit {pit} stair walk blocked at {waypoint}/{path.Length - 1}: {actor.transform.position} toward {goal}");
                    yield break;
                }
            }
            Expect(Vector3.Distance(actor.transform.position, path[path.Length - 1]) < .25f,
                $"RUIN player physically walks every step out of pit {pit}");
        }

        private IEnumerator WalkMayaRamp(PlayerAvatar actor, Vector3 from, Vector3 summit, int ramp)
        {
            CharacterController body = actor.GetComponent<CharacterController>();
            if (body == null || !body.enabled || !actor.IsOwner)
            {
                Expect(false, $"RUIN Maya ramp {ramp} walk needs the owned CharacterController");
                yield break;
            }
            float deadline = Time.time + 8f;
            while (Time.time < deadline)
            {
                Vector3 delta = Vector3.ProjectOnPlane(summit - actor.transform.position, Vector3.up);
                if (delta.magnitude < .2f) break;
                // Horizontal input and gravity only, as a player holding W would.
                body.Move(Vector3.ClampMagnitude(delta, 3f * Time.deltaTime) + Vector3.down * (2 * Time.deltaTime));
                yield return null;
            }
            Expect(actor.transform.position.y > summit.y - .35f,
                $"RUIN player walks up Maya ramp {ramp} to the summit ({actor.transform.position.y:0.00} of {summit.y:0.00} m)");
        }

        private IEnumerator CoCarryRuinStairs(PlayerAvatar a, PlayerAvatar b, CargoItem cargo, Vector3[] path, int pit)
        {
            float startY = cargo.transform.position.y;
            try
            {
                for (int waypoint = 1; waypoint < path.Length; waypoint++)
                {
                    float deadline = Time.time + 5;
                    Vector3 goal = path[waypoint];
                    while (Time.time < deadline && a.CarriedCargo == cargo && b.CarriedCargo == cargo)
                    {
                        Vector3 delta = Vector3.ProjectOnPlane(goal - cargo.transform.position, Vector3.up);
                        if (delta.magnitude < .16f) break;
                        Vector2 input = new Vector2(delta.x, delta.z).normalized;
                        a.TestCoCarryMove(input);
                        b.TestCoCarryMove(input);
                        yield return null;
                    }
                    a.TestCoCarryMove(Vector2.zero);
                    b.TestCoCarryMove(Vector2.zero);
                    if (Time.time >= deadline || a.CarriedCargo != cargo || b.CarriedCargo != cargo)
                    {
                        // Each carrier's feet and the tread a quarter metre ahead, to tell a wall from a missing floor.
                        Vector3 ahead = Vector3.ProjectOnPlane(goal - cargo.transform.position, Vector3.up).normalized * .25f;
                        string feet = string.Join(" / ", new[] { a, b }.Select(actor =>
                            $"{actor.transform.position:F2} ahead " +
                            (CargoItem.TryRuinCarryFloor(actor.transform.position + ahead, out float floor) ? floor.ToString("F2") : "none")));
                        Expect(false, $"RUIN pit {pit} 100 co-carry blocked/released at {waypoint}/{path.Length - 1}: {cargo.transform.position} toward {goal}; carriers {feet}");
                        yield break;
                    }
                }
                yield return new WaitForSeconds(.2f);
                Expect(a.CarriedCargo == cargo && b.CarriedCargo == cargo && !cargo.HasFreeSlot &&
                       cargo.transform.position.y - startY > 3f &&
                       Mathf.Abs(cargo.transform.position.y - path[path.Length - 1].y - cargo.CoCarryLift) < .2f &&
                       new[] { a, b }.All(actor => Mathf.Abs(actor.transform.position.y - path[path.Length - 1].y) < .2f),
                    $"RUIN two players physically co-carry the 100 box up pit {pit} stairs");
            }
            finally
            {
                a.TestCoCarryMove(Vector2.zero);
                b.TestCoCarryMove(Vector2.zero);
            }
        }

        private IEnumerator TestRuinDelivery(RuinCourtyard ruins, LevelLayout layout, PlayerAvatar actor, CargoItem cargo)
        {
            Transform zone = layout.truckZone;
            Vector3 bedCenter = layout.bedOrigin.position;
            bedCenter.y = zone.position.y;
            Expect(Vector3.Distance(zone.position, bedCenter) < .03f &&
                   Vector3.Angle(zone.forward, Vector3.ProjectOnPlane(layout.bedOrigin.forward, Vector3.up)) < .1f,
                "RUIN delivery volume follows the actual truck bed center and yaw");
            for (int axis = 0; axis < 2; axis++)
                foreach (float sign in new[] { -1f, 1f })
                {
                    float half = axis == 0 ? layout.truckZoneHalfSize.x : layout.truckZoneHalfSize.y;
                    Vector3 direction = axis == 0 ? Vector3.right : Vector3.forward;
                    Vector3 inside = zone.TransformPoint(direction * (sign * (half - .05f)) + Vector3.up);
                    Vector3 outside = zone.TransformPoint(direction * (sign * (half + .05f)) + Vector3.up);
                    Expect(ruins.ContainsDeliveryPoint(inside) && layout.InTruckZone(inside) &&
                           !ruins.ContainsDeliveryPoint(outside) && !layout.InTruckZone(outside),
                        $"RUIN truck bed delivery edge {axis}/{sign} has matching carried/thrown boundaries");
                }
            foreach (float y in new[] { -.3f, ruins.DeliveryHeight + .1f })
            {
                Vector3 point = zone.TransformPoint(Vector3.up * y);
                Expect(!ruins.ContainsDeliveryPoint(point) && !layout.InTruckZone(point),
                    $"RUIN delivery rejects cargo outside its vertical volume at local y={y:0.00}");
            }

            MatchDirector director = MatchDirector.Instance;
            int orange = director.OrangeScore.Value, blue = director.BlueScore.Value;
            Vector3 initial = zone.TransformPoint(Vector3.up * Mathf.Min(2f, ruins.DeliveryHeight - .3f));
            cargo.transform.position = cargo.Body.position = initial;
            cargo.Body.linearVelocity = cargo.Body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForSeconds(.4f);
            Expect(cargo.State.Value == CargoState.Ground && director.OrangeScore.Value == orange && director.BlueScore.Value == blue,
                "RUIN unhandled initial ground cargo inside the volume does not score");
            cargo.ServerResetFromRuinPit();

            // The collector stands outside the ring and throws toward the bed through normal grab/release input.
            Vector3 feet = zone.TransformPoint(new Vector3(layout.truckZoneHalfSize.x + 1.4f, 0, 0));
            Vector3 cargoFeet = zone.TransformPoint(new Vector3(layout.truckZoneHalfSize.x + .2f, 0, 0));
            bool actorFloor = Physics.Raycast(feet + Vector3.up, Vector3.down, out RaycastHit actorGround, 3f,
                GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            bool cargoFloor = Physics.Raycast(cargoFeet + Vector3.up, Vector3.down, out RaycastHit cargoGround, 3f,
                GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            Expect(actorFloor && cargoFloor, "RUIN outside throw setup has actual ground under actor and cargo");
            if (actorFloor) feet.y = actorGround.point.y;
            if (cargoFloor) cargoFeet.y = cargoGround.point.y;
            BoxCollider box = cargo.GetComponent<BoxCollider>();
            float bottom = (box.center.y - box.size.y * .5f) * cargo.transform.localScale.y;
            Vector3 setup = cargoFeet + Vector3.up * (.03f - bottom);
            cargo.transform.position = cargo.Body.position = setup;
            cargo.Body.linearVelocity = cargo.Body.angularVelocity = Vector3.zero;
            cargo.Body.WakeUp();
            Physics.SyncTransforms();
            yield return StandRuin(actor, feet, zone.position);
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            LogRuinDelivery("before-grab", layout, actor, cargo);
            Expect(actor.CarriedCargo == null && actor.GetComponent<CharacterController>().enabled &&
                   !layout.InTruckZone(actor.transform.position) &&
                   cargo.IsWithin(actor.transform.position, GameConfig.Get().pickupRange + GameConfig.Get().reachTolerance) &&
                   RuinCourtyard.CargoReach(actor, cargo),
                "RUIN outside throw setup has a free collector, enabled controller and clear validated pickup reach");
            actor.TestGrab(cargo);
            yield return new WaitForSeconds(.5f);
            LogRuinDelivery("after-grab", layout, actor, cargo);
            Expect(actor.CarriedCargo == cargo && !layout.InTruckZone(actor.transform.position),
                "RUIN collector picks up cargo while remaining outside the delivery ring");
            Vector3 towardBed = Vector3.ProjectOnPlane(zone.position - actor.transform.position, Vector3.up).normalized;
            actor.transform.rotation = Quaternion.LookRotation(towardBed);
            actor.TestLook(actor.transform.eulerAngles.y, 14f);
            Physics.SyncTransforms();
            yield return null;
            LogRuinDelivery("before-release", layout, actor, cargo);
            actor.TestLetGo();
            float deadline = Time.time + 3;
            float nextLog = Time.time;
            while (cargo.State.Value != CargoState.Rescued && Time.time < deadline)
            {
                if (Time.time >= nextLog)
                {
                    LogRuinDelivery("in-flight", layout, actor, cargo);
                    nextLog = Time.time + .3f;
                }
                yield return null;
            }
            LogRuinDelivery("after-flight", layout, actor, cargo);
            int orangeExpected = orange + (actor.Team == Team.Orange ? cargo.Definition.value : 0);
            int blueExpected = blue + (actor.Team == Team.Blue ? cargo.Definition.value : 0);
            Expect(cargo.State.Value == CargoState.Rescued && actor.CarriedCargo == null &&
                   director.OrangeScore.Value == orangeExpected && director.BlueScore.Value == blueExpected,
                "RUIN normal collector throw into the truck bed ring rescues and scores once");
            yield return new WaitForSeconds(.8f);
            Expect(director.OrangeScore.Value == orangeExpected && director.BlueScore.Value == blueExpected,
                "RUIN thrown cargo remaining in the delivery volume cannot score again");
        }

        private static void LogRuinDelivery(string stage, LevelLayout layout, PlayerAvatar actor, CargoItem cargo)
        {
            bool blocked = Physics.Linecast(actor.EyePosition, cargo.transform.position, out RaycastHit hit,
                GameLayers.LevelMask, QueryTriggerInteraction.Ignore);
            CharacterController controller = actor.GetComponent<CharacterController>();
            Log($"RUIN DELIVERY {stage} phase={MatchDirector.Instance.Phase.Value} " +
                $"actor={actor.transform.position:F3} eye={actor.EyePosition:F3} facing={actor.transform.forward:F3} " +
                $"carried={actor.CarriedCargoId.Value} controller={controller != null && controller.enabled} " +
                $"knocked={actor.IsKnockedDown} staggered={actor.IsStaggered} climbing={actor.RuinClimbing.Value} " +
                $"cargo={cargo.transform.position:F3} body={cargo.Body.position:F3} state={cargo.State.Value} " +
                $"kinematic={cargo.Body.isKinematic} velocity={cargo.Body.linearVelocity:F3} " +
                $"edge={cargo.EdgeDistance(actor.transform.position):F3} " +
                $"heightGap={Mathf.Abs(actor.transform.position.y - cargo.transform.position.y + cargo.HalfExtents.y):F3} " +
                $"reach={RuinCourtyard.CargoReach(actor, cargo)} blocked={(blocked ? hit.collider.name : "none")} " +
                $"hit={(blocked ? hit.point.ToString("F3") : "none")} " +
                $"actorZone={layout.truckZone.InverseTransformPoint(actor.transform.position):F3} " +
                $"cargoZone={layout.truckZone.InverseTransformPoint(cargo.transform.position):F3} " +
                $"inside={layout.InTruckZone(cargo.transform.position)} " +
                $"score={MatchDirector.Instance.OrangeScore.Value}/{MatchDirector.Instance.BlueScore.Value}");
        }

        private static Vector3 RuinPitCenter(Vector4 rect, float y) => new Vector3((rect.x + rect.z) * .5f, y, (rect.y + rect.w) * .5f);

        private static IEnumerator StandRuin(PlayerAvatar actor, Vector3 feet, Vector3 target)
        {
            Vector3 facing = target-feet;
            facing.y = 0;
            if (facing.sqrMagnitude < .01f) facing = Vector3.forward;
            actor.TeleportRpc(feet, Quaternion.LookRotation(facing));
            yield return new WaitForSeconds(.2f);
        }

        private static void UseRuinTest(RuinCourtyard ruins, PlayerAvatar actor, Vector3 target)
        {
            Vector3 direction = (target-actor.EyePosition).normalized;
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude > .001f) actor.transform.rotation = Quaternion.LookRotation(flat);
            actor.TestLook(actor.transform.eulerAngles.y, Mathf.Atan2(-direction.y, flat.magnitude)*Mathf.Rad2Deg);
            ruins.ServerUse(actor, ruins.FindUse(actor, direction), direction);
        }
    }
}
