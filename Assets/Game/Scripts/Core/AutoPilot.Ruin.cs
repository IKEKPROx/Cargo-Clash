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
            public Vector3 boostStart, boostExit, ladderStart, ladderExit;
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
            while (CargoItem.All.Count < 15 && MatchDirector.Instance.Now < cargoDeadline) yield return null;
            CheckRuinCargo(config, layout);
            Expect(layout.collectorSpawns.Length == layout.interrupterSpawns.Length &&
                   ruins.SpawnMarkerCount == layout.collectorSpawns.Length + layout.interrupterSpawns.Length &&
                   ruins.SpawnBannerCount == 2 && ruins.SpawnMarkersRoot != null &&
                   !ruins.SpawnMarkersRoot.GetComponentsInChildren<Collider>(true).Any(c => c.enabled),
                "RUIN both equal teams have marked spawn slots and non-blocking bilingual assembly signs");
            if (!local.IsServer)
            {
                // The client must not deliver unrelated cargo during the host's score-isolation checks.
                BotsIdle = true;
                bool door = false, wall = false, pad = false, ladder = false, coCarry = false;
                int half = MatchDirector.Instance.Half.Value;
                while (MatchDirector.Instance.Half.Value == half && MatchDirector.Instance.Phase.Value == MatchPhase.Playing)
                {
                    uint bits = MatchDirector.Instance.RuinBits.Value;
                    door |= (bits & (1u << 24)) != 0;
                    wall |= (bits & 3u) == 2u;
                    pad |= MatchDirector.Instance.RuinPatches.Count > 0;
                    ladder |= (bits & (1u << 29)) != 0;
                    coCarry |= CargoItem.All.Any(c => c.Definition.value == 100 && !c.HasFreeSlot && c.State.Value == CargoState.Carried);
                    yield return null;
                }
                Expect(door && wall, "RUIN client observes replicated gate and broken wall states");
                Expect(pad && ladder, "RUIN client observes replicated vine pad and ladder states");
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
                foreach (Vector4 rect in map.pits)
                {
                    Vector3 pit = RuinPitCenter(rect, .1f);
                    Expect(!Physics.Raycast(pit, Vector3.down, .6f, GameLayers.LevelMask), "RUIN pit has an actual ground opening");
                    Expect(!NavMesh.SamplePosition(pit, out _, .25f, NavMesh.AllAreas), "RUIN navigation does not bridge a pit");
                }

                // Use the ordinary validated scene rules, with actors outside the door sweep.
                Vector3 handle = new Vector3(44.5f, 1, 32.97f);
                yield return StandRuin(a, new Vector3(44.5f, 0, 30.35f), handle);
                UseRuinTest(ruins, a, handle);
                Expect((director.RuinBits.Value & (1u << 24)) != 0, "RUIN gate closes from outside its sweep");
                yield return new WaitForSeconds(config.ruinDoorCooldown + .1f);
                UseRuinTest(ruins, a, handle);
                Expect((director.RuinBits.Value & (1u << 24)) == 0, "RUIN gate reopens");
                yield return StandRuin(b, new Vector3(44.5f, 0, 32.5f), handle);
                yield return new WaitForSeconds(config.ruinDoorCooldown + .1f);
                UseRuinTest(ruins, a, handle);
                Expect((director.RuinBits.Value & (1u << 24)) == 0, "RUIN occupied gate refuses to close");
                yield return StandRuin(b, starts[1], starts[1] + Vector3.forward);

                Vector3 wall = new Vector3(36.5f, 1.1f, 33.6f);
                yield return StandRuin(interrupter, new Vector3(36.5f, 0, 32), wall);
                Vector3 hitAim = (wall-interrupter.EyePosition).normalized;
                int wallIndex = ruins.FindWall(interrupter, hitAim, false);
                Expect(wallIndex == 0, "RUIN hammer aims at R1");
                ruins.ServerHitWall(interrupter, wallIndex, hitAim);
                Expect((director.RuinBits.Value & 3u) == 2u, "RUIN hammer breaks R1");
                yield return StandRuin(interrupter, starts[2], starts[2] + Vector3.forward);
                yield return StandRuin(a, new Vector3(36.5f, 0, 32), wall);
                yield return new WaitForSeconds(config.ruinWallSwitchCooldown + .1f);
                float until = Time.time + config.ruinRestoreSeconds + .35f;
                while (Time.time < until)
                {
                    ruins.ServerRestoreWall(a, Time.deltaTime, (wall-a.EyePosition).normalized);
                    yield return null;
                }
                Expect((director.RuinBits.Value & 3u) == 0, "RUIN collector restores broken R1");

                Vector3 coil = map.vinePickups[0] + Vector3.up * .25f;
                Vector3 vineFeet = map.vinePickups[0] + Vector3.forward * 1.7f;
                yield return StandRuin(a, vineFeet, coil);
                UseRuinTest(ruins, a, coil);
                Expect(a.HasRuinVine.Value, "RUIN vine coil can be collected");
                Vector3 pad = vineFeet + Vector3.right * 2.6f;
                UseRuinTest(ruins, a, pad);
                Expect(!a.HasRuinVine.Value && director.RuinPatches.Count == 1, "RUIN throw creates one pad and consumes the vine");
                Expect(Mathf.Approximately(ruins.SpeedAt(pad), config.ruinPatchSpeed), "RUIN vine pad applies its configured slowdown");
                yield return new WaitForSeconds(config.ruinPatchSeconds + .2f);
                Expect(director.RuinPatches.Count == 0 && Mathf.Approximately(ruins.SpeedAt(pad), 1), "RUIN vine pad expires");

                // Pickup already tested. Supply another vine to isolate the ladder rule.
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
                        Vector3 cargoBottom = bottom + Vector3.right * .85f;
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
                a.TestCoCarryMove(Vector2.zero);
                b.TestCoCarryMove(Vector2.zero);
                for (int i = 0; i < actors.Length; i++) actors[i].TeleportRpc(starts[i], Quaternion.identity);
                BotsIdle = false;
            }
        }

        private void CheckRuinCargo(GameConfig config, LevelLayout layout)
        {
            Expect(CargoItem.All.Count == 15, "RUIN spawns exactly 15 boxes");
            foreach (var tier in new[] { (10, 8), (30, 5), (100, 2) })
                Expect(CargoItem.All.Count(c => c.Definition.value == tier.Item1) == tier.Item2,
                    $"RUIN value {tier.Item1} has {tier.Item2} boxes");
            Expect(CargoItem.All.Sum(c => c.Definition.value) == 430, "RUIN total available score is 430");
            Expect(config.ruinCargo.Length == 4, "RUIN uses four isolated Astra definitions");
            foreach (var expected in new[] { (CargoKind.Tobacco, 4), (CargoKind.Cash, 4), (CargoKind.Gold, 5), (CargoKind.Art, 2) })
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
                        Expect(false, $"RUIN pit {pit} 100 co-carry blocked/released at {waypoint}/{path.Length - 1}: {cargo.transform.position} toward {goal}");
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
