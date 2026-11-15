using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace CargoClash
{
    /// <summary>
    /// -ccRiverTest, with -ccMode practice on the river level: the host plays out the river's rules on purpose and checks
    /// them, then lets the bots finish the match. Everyone stands still while it runs (BotsIdle).
    /// </summary>
    public sealed partial class AutoPilot
    {
        private IEnumerator TestTheRiver()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned || MatchDirector.Instance.Phase.Value != MatchPhase.Playing ||
                   PlayerAvatar.Local == null || RiverMatch.Instance == null)
                yield return null;
            GameConfig config = GameConfig.Get();
            RiverLevel level = RiverLevel.Instance;
            RiverMatch river = RiverMatch.Instance;
            MatchDirector director = MatchDirector.Instance;
            PlayerAvatar local = PlayerAvatar.Local;
            if (!local.IsServer)
            {
                // A client checks what it's shown: the catcher's slots and the planks come from the host.
                yield return WatchTheRiverFromAClient(river);
                yield break;
            }
            BotsIdle = true;
            float flowScale = config.riverFlowScale;
            float plankSeconds = config.plankSeconds;

            int planks = CargoItem.All.FindAll(c => c.IsPlank).Count;
            Log($"RIVER halfSeconds={config.EffectiveHalfSeconds} planks={planks} slots={river.SlotCount} cargo={CargoItem.All.Count - planks}");
            Expect(planks == level.plankSpots.Length && planks > 0, $"a plank waits on every spot in the huts ({planks} of {level.plankSpots.Length})");
            Expect(river.FilledCount == 0, "the catcher starts the half empty");
            if (HalfSecondsOverride <= 0f) Expect(Mathf.Approximately(config.EffectiveHalfSeconds, 180f), "the river's halves are 3 minutes");

            // Deep water stops people everywhere but the fords, and wading is slow.
            Bounds ford = level.fords[0];
            Vector3 fordMiddle = ford.center;
            level.OverWater(fordMiddle, out float surface, out _, out _);
            fordMiddle.y = surface - 0.1f;
            Expect(level.WadeMultiplier(fordMiddle) < 1f, "wading through a ford is slower");
            Vector3 bank = local.transform.position;
            Expect(Mathf.Approximately(level.WadeMultiplier(bank), 1f), "on dry land nobody is slowed");
            int deepChecked = 0;
            int deepBlocked = 0;
            for (int i = 0; i < level.flowPath.centres.Length; i += 10)
            {
                Vector3 middle = level.flowRoot.TransformPoint(level.flowPath.centres[i]);
                if (level.InFord(middle) || !level.field.Contains(new Vector2(middle.x, middle.z))) continue;
                deepChecked++;
                if (Physics.CheckSphere(middle + Vector3.up * 0.9f, 0.3f, 1 << GameLayers.PlayerBlock, QueryTriggerInteraction.Ignore)) deepBlocked++;
            }
            Expect(deepChecked > 0 && deepBlocked == deepChecked, $"deep water stops people all along the river ({deepBlocked} of {deepChecked})");
            Expect(!Physics.CheckSphere(new Vector3(ford.center.x, surface + 0.9f, ford.center.z), 0.3f, 1 << GameLayers.PlayerBlock, QueryTriggerInteraction.Ignore),
                "the fords are open to wade across");

            // The bots' map crosses at the fords: from the truck to the far bank, the way leads through one.
            NavMeshPath path = new NavMeshPath();
            Vector3 from = LevelLayout.Instance.collectorSpawns[0].position;
            Vector3 to = LevelLayout.Instance.interrupterSpawns[0].position;
            bool found = NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas) && NavMesh.SamplePosition(to, out NavMeshHit b, 3f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            bool viaFord = false;
            if (found)
            {
                for (int i = 1; i < path.corners.Length && !viaFord; i++)
                {
                    for (float t = 0f; t <= 1f; t += 0.05f)
                    {
                        if (level.InFord(Vector3.Lerp(path.corners[i - 1], path.corners[i], t))) viaFord = true;
                    }
                }
            }
            Expect(found, "the bots can walk from one side of the river to the other");
            Expect(viaFord, "the bots' way across goes through a ford");
            if (!viaFord) Log("RIVER way across: " + string.Join(" ", System.Array.ConvertAll(path.corners, c => c.ToString("F1"))));

            // Planks: take one from a hut and push it into the catcher from each bank.
            int southSlot = 0;
            int northSlot = river.SlotCount - 1;
            Vector3 southBank = StandingSpotNear(level, level.SlotPosition(southSlot), level.SlotPosition(northSlot));
            Vector3 northBank = StandingSpotNear(level, level.SlotPosition(northSlot), level.SlotPosition(southSlot));
            int inserted = 0;
            for (int round = 0; round < river.SlotCount; round++)
            {
                CargoItem plank = CargoItem.All.Find(c => c.IsPlank && c.State.Value == CargoState.Ground);
                if (plank == null)
                {
                    Expect(false, "there's a plank left to take");
                    break;
                }
                yield return TakeHold(local, plank);
                Expect(local.CarriedCargo == plank, $"picked up plank {round + 1}");
                if (local.CarriedCargo != plank) break;
                if (round == 0) yield return PhotographPlank(local, level);
                Vector3 standAt = river.ReachableGap(southBank) >= 0 ? southBank : northBank;
                local.TeleportRpc(standAt, Quaternion.LookRotation(Flat(level.SlotPosition(4) - standAt)));
                yield return new WaitForSeconds(0.35f);
                int before = river.FilledCount;
                local.TestLetGo();
                yield return new WaitForSeconds(0.3f);
                Expect(river.FilledCount == before + 1 && local.CarriedCargo == null && !plank.IsSpawned, $"plank {round + 1} went into the catcher");
                if (river.FilledCount == before + 1) inserted++;
                if (round == 0) Expect(river.ReachableGap(northBank) >= 0 && river.ReachableGap(southBank) >= 0, "both banks reach the catcher");
            }
            Expect(inserted == river.SlotCount && river.FilledCount == river.SlotCount, $"the catcher is full ({river.FilledCount} of {river.SlotCount})");
            Expect(river.ReachableGap(southBank) < 0, "a full catcher has no gap to plug");
            // Watch from the bank, a few steps upstream, looking at the catcher.
            Vector3 catcherAt = (level.SlotPosition(southSlot) + level.SlotPosition(northSlot)) * 0.5f;
            Vector3 watch = southBank + Flat(southBank - level.SlotPosition(northSlot)).normalized * 1.5f + Flat(PointUpstream(level, 4f) - catcherAt).normalized * 3f;
            if (CargoItem.GroundBelow(watch + Vector3.up * 2f, out float watchGround)) watch.y = watchGround;
            float watchYaw = Quaternion.LookRotation(Flat(catcherAt - watch)).eulerAngles.y;
            local.TeleportRpc(watch, Quaternion.Euler(0f, watchYaw, 0f));
            yield return new WaitForSeconds(0.3f);
            local.TestLook(watchYaw, 22f);
            yield return new WaitForSeconds(0.3f);
            Shot("catcher-full");

            // A full catcher holds what floats down to it; the current is sped up so the test doesn't take minutes.
            config.riverFlowScale = 5f;
            CargoItem drifter = CargoItem.All.Find(c => !c.IsPlank && c.State.Value == CargoState.Ground && c.Definition.requiredCarriers == 1 && c.Definition.kind == CargoKind.Tobacco);
            if (drifter == null) drifter = CargoItem.All.Find(c => !c.IsPlank && c.State.Value == CargoState.Ground && c.Definition.requiredCarriers == 1);
            Vector3 upstream = PointUpstream(level, 9f);
            PutInWater(drifter, upstream);
            int orange = director.OrangeScore.Value;
            int blue = director.BlueScore.Value;
            Vector3 catcherMiddle = (level.SlotPosition(southSlot) + level.SlotPosition(northSlot)) * 0.5f;
            float started = Time.time;
            float lastMoved = Time.time;
            Vector3 lastSeen = drifter.transform.position;
            while (Time.time - started < 20f && drifter.State.Value == CargoState.Ground)
            {
                if (Vector3.Distance(drifter.transform.position, lastSeen) > 0.05f)
                {
                    lastMoved = Time.time;
                    lastSeen = drifter.transform.position;
                }
                if (Time.time - lastMoved > 2.5f && Flat(drifter.transform.position - catcherMiddle).magnitude < 3f) break;
                yield return null;
            }
            Vector3 heldAt = drifter.transform.position;
            Log($"RIVER drifted from={upstream} to={heldAt} seconds={Time.time - started:0.0} state={drifter.State.Value}");
            Expect(Flat(heldAt - upstream).magnitude > 5f, "the current carried the cargo downstream");
            Expect(drifter.State.Value == CargoState.Ground && Flat(heldAt - catcherMiddle).magnitude < 3f, "the full catcher stopped it");
            Shot("catcher-holds");

            // The planks wash away on their own clocks; with none left the cargo goes on and off the field, for nobody.
            config.plankSeconds = 1f;
            yield return new WaitForSeconds(0.5f);
            Expect(river.FilledCount == 0, "the planks wash away in time");
            started = Time.time;
            while (Time.time - started < 25f && drifter.IsSpawned && drifter.State.Value == CargoState.Ground) yield return null;
            Expect(!drifter.IsSpawned || drifter.State.Value == CargoState.Lost, "with the catcher open, the river took it off the field");
            // Only the host's own players stand still for the test; someone on another computer may score meanwhile.
            bool everyoneStill = PlayerAvatar.All.TrueForAll(p => p.IsOwner);
            if (everyoneStill) Expect(director.OrangeScore.Value == orange && director.BlueScore.Value == blue, "cargo the river takes scores for nobody");
            else Log("RIVER score check untested: a client is playing");

            config.riverFlowScale = flowScale;
            config.plankSeconds = plankSeconds;

            // Two people carrying the goose crate keep it on the ground over the banks and slopes.
            yield return TestCoCarryOverSlopes(level, local);

            BotsIdle = false;
            Log("RIVER done");
        }

        /// <summary>On a client: logs what it's shown of the catcher and the planks, and checks the boards it draws match.</summary>
        private IEnumerator WatchTheRiverFromAClient(RiverMatch river)
        {
            int mostFilled = 0;
            int planksSeen = 0;
            bool drawnRight = true;
            float until = Time.time + 60f;
            while (Time.time < until && MatchDirector.Instance != null && MatchDirector.Instance.Phase.Value == MatchPhase.Playing)
            {
                int filled = river.FilledCount;
                mostFilled = Mathf.Max(mostFilled, filled);
                planksSeen = Mathf.Max(planksSeen, CargoItem.All.FindAll(c => c.IsPlank).Count);
                RiverLevel level = RiverLevel.Instance;
                for (int i = 0; i < river.SlotCount; i++)
                {
                    var slot = level.catcher.slots[i];
                    bool shown = slot.fresh.activeSelf || slot.worn.activeSelf || slot.critical.activeSelf;
                    if (shown != river.SlotFilled(i)) drawnRight = false;
                }
                yield return new WaitForSeconds(0.5f);
            }
            Log($"RIVER client mostFilled={mostFilled} planks={planksSeen}");
            Expect(planksSeen > 0, "a client sees the planks in the huts");
            Expect(mostFilled > 0, "a client sees planks go into the catcher");
            Expect(drawnRight, "a client draws a board in every filled slot and none in the empty ones");
        }

        /// <summary>Two carriers walk the goose crate down off the highground: it follows the ground and never floats or sinks.</summary>
        private IEnumerator TestCoCarryOverSlopes(RiverLevel level, PlayerAvatar local)
        {
            CargoItem crate = CargoItem.All.Find(c => c.State.Value == CargoState.Ground && c.Definition.requiredCarriers > 1);
            PlayerAvatar helper = PlayerAvatar.All.Find(p => p != local && p.Team == local.Team && p.IsOwner);
            if (crate == null || helper == null || !PlayerAvatar.All.TrueForAll(p => p.IsOwner))
            {
                Log($"RIVER co-carry untested: crate={crate != null} helper={helper != null}");
                yield break;
            }
            // From the crate's spot, walk it toward the truck: highground, slopes and banks lie between.
            yield return TakeHold(local, crate);
            yield return TakeHold(helper, crate);
            Expect(crate.State.Value == CargoState.Carried && !crate.HasFreeSlot, "two people took hold of the crate");
            if (crate.HasFreeSlot) yield break;
            Vector3 truck = LevelLayout.Instance.truckZone.position;
            float worst = 0f;
            float started = Time.time;
            Vector3 startAt = crate.transform.position;
            while (Time.time - started < 6f && crate.State.Value == CargoState.Carried)
            {
                Vector3 way = Flat(truck - crate.transform.position).normalized;
                Vector2 move = new Vector2(way.x, way.z);
                local.TestCoCarryMove(move);
                helper.TestCoCarryMove(move);
                if (CargoItem.GroundBelow(crate.transform.position, out float ground))
                {
                    float above = crate.transform.position.y - ground;
                    float wanted = crate.Definition.coCarryHeight > 0f ? crate.Definition.coCarryHeight : crate.HalfExtents.y;
                    worst = Mathf.Max(worst, Mathf.Abs(above - wanted));
                }
                yield return null;
            }
            local.TestCoCarryMove(Vector2.zero);
            helper.TestCoCarryMove(Vector2.zero);
            Log($"RIVER co-carry moved={Flat(crate.transform.position - startAt).magnitude:0.0} worstHeightError={worst:0.00}");
            Expect(Flat(crate.transform.position - startAt).magnitude > 2f, "the crate went where its carriers walked");
            Expect(worst < 0.35f, "the carried crate kept to the ground over the slopes");
            local.TestLetGo();
            helper.TestLetGo();
            yield return new WaitForSeconds(0.5f);
        }

        /// <summary>Out in the open with the plank, from both views, standing and walking toward the river.</summary>
        private IEnumerator PhotographPlank(PlayerAvatar local, RiverLevel level)
        {
            if (string.IsNullOrEmpty(shotDirectory)) yield break;
            Vector3 hut = local.transform.position;
            Vector3 outside = hut + Vector3.forward * 8f;
            if (CargoItem.GroundBelow(outside + Vector3.up * 2f, out float ground)) outside.y = ground;
            local.TeleportRpc(outside, Quaternion.LookRotation(Vector3.forward));
            yield return new WaitForSeconds(0.4f);
            local.TestLook(0f, 5f);
            bool first = GameSettings.FirstPerson;
            GameSettings.FirstPerson = true;
            yield return new WaitForSeconds(0.8f);
            Shot("plank-first");
            GameSettings.FirstPerson = false;
            yield return new WaitForSeconds(0.9f);
            Shot("plank-third");
            GameSettings.FirstPerson = first;
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>Stands next to something, facing it, and takes hold of it.</summary>
        private IEnumerator TakeHold(PlayerAvatar who, CargoItem thing)
        {
            Vector3 facing = Flat(thing.transform.position - who.transform.position);
            facing = facing.sqrMagnitude > 0.01f ? facing.normalized : Vector3.forward;
            Vector3 spot = thing.transform.position - facing * (thing.Radius + 0.6f);
            if (CargoItem.GroundBelow(spot, out float ground)) spot.y = ground;
            who.TeleportRpc(spot, Quaternion.LookRotation(facing));
            yield return new WaitForSeconds(0.3f);
            who.TestLook(Quaternion.LookRotation(facing).eulerAngles.y, 10f);
            who.TestGrab(thing);
            float waited = 0f;
            while (who.CarriedCargo != thing && waited < 2f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);
        }

        /// <summary>Dry ground on the bank beside a slot of the catcher, on the side away from `across`.</summary>
        private static Vector3 StandingSpotNear(RiverLevel level, Vector3 slot, Vector3 across)
        {
            Vector3 outward = Flat(slot - across).normalized;
            for (float out_ = 1f; out_ < 6f; out_ += 0.25f)
            {
                Vector3 spot = slot + outward * out_;
                if (!CargoItem.GroundBelow(spot + Vector3.up * 2f, out float ground)) continue;
                spot.y = ground;
                if (Physics.CheckSphere(spot + Vector3.up * 0.9f, 0.4f, 1 << GameLayers.PlayerBlock, QueryTriggerInteraction.Ignore)) continue;
                if (level.OverWater(spot, out float surface, out _, out _) && ground < surface + 0.05f) continue;
                return spot;
            }
            return slot + outward * 3f;
        }

        /// <summary>A point in the middle of the river this far upstream of the catcher.</summary>
        private static Vector3 PointUpstream(RiverLevel level, float metres)
        {
            Vector3 catcher = (level.SlotPosition(0) + level.SlotPosition(level.SlotCount - 1)) * 0.5f;
            Vector3[] centres = level.flowPath.centres;
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < centres.Length; i++)
            {
                float distance = Flat(level.flowRoot.TransformPoint(centres[i]) - catcher).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }
            // The samples run downstream half a metre apart.
            int back = Mathf.Max(0, nearest - Mathf.RoundToInt(metres / 0.5f));
            return level.flowRoot.TransformPoint(centres[back]);
        }

        private static void PutInWater(CargoItem cargo, Vector3 at)
        {
            Rigidbody body = cargo.Body;
            Vector3 place = at + Vector3.up * (cargo.HalfExtents.y + 0.1f);
            body.position = place;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            cargo.transform.position = place;
        }

        private static Vector3 Flat(Vector3 vector) => new Vector3(vector.x, 0f, vector.z);
    }
}
