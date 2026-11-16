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

        private IEnumerator TestTheRuins()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned ||
                   MatchDirector.Instance.Phase.Value != MatchPhase.Playing || PlayerAvatar.Local == null)
                yield return null;
            PlayerAvatar local = PlayerAvatar.Local;
            RuinCourtyard ruins = RuinCourtyard.Instance;
            Expect(ruins != null, "RUIN test runs in the ruin scene");
            if (ruins == null) yield break;
            if (!local.IsServer)
            {
                // The client must not deliver unrelated cargo during the host's score-isolation checks.
                BotsIdle = true;
                bool door = false, wall = false, pad = false, ladder = false, coCarry = false;
                double watchUntil = MatchDirector.Instance.Now + 40;
                while (MatchDirector.Instance.Now < watchUntil && MatchDirector.Instance.Phase.Value == MatchPhase.Playing)
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
            if (collectors.Length < 2 || interrupter == null) yield break;
            PlayerAvatar a = collectors[0], b = collectors[1];
            PlayerAvatar[] actors = { a, b, interrupter };
            Vector3[] starts = actors.Select(p => p.transform.position).ToArray();
            GameConfig config = GameConfig.Get();
            MatchDirector director = MatchDirector.Instance;
            BotsIdle = true;
            try
            {
                Expect(CargoItem.All.Count == 15, "RUIN spawns exactly 15 boxes");
                foreach (var tier in new[] { (10, 8), (30, 5), (100, 2) })
                    Expect(CargoItem.All.Count(c => c.Definition.value == tier.Item1) == tier.Item2,
                        $"RUIN value {tier.Item1} has {tier.Item2} boxes");
                Expect(CargoItem.All.Sum(c => c.Definition.value) == 430, "RUIN total available score is 430");
                Expect(Physics.Raycast(new Vector3(62, .3f, 10), Vector3.down, .6f, GameLayers.LevelMask), "RUIN ground collider supports players");
                foreach (Vector3 pit in new[] { new Vector3(28.5f, .1f, 39.5f), new Vector3(93.375f, .1f, 34) })
                {
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

                Vector3 coil = new Vector3(28, .25f, 19.7f);
                yield return StandRuin(a, new Vector3(28, 0, 21.4f), coil);
                UseRuinTest(ruins, a, coil);
                Expect(a.HasRuinVine.Value, "RUIN vine coil can be collected");
                Vector3 pad = new Vector3(30.6f, 0, 21.4f);
                UseRuinTest(ruins, a, pad);
                Expect(!a.HasRuinVine.Value && director.RuinPatches.Count == 1, "RUIN throw creates one pad and consumes the vine");
                Expect(Mathf.Approximately(ruins.SpeedAt(pad), config.ruinPatchSpeed), "RUIN vine pad applies its configured slowdown");
                yield return new WaitForSeconds(config.ruinPatchSeconds + .2f);
                Expect(director.RuinPatches.Count == 0 && Mathf.Approximately(ruins.SpeedAt(pad), 1), "RUIN vine pad expires");

                // Pickup already tested. Supply another vine to isolate the ladder rule.
                a.HasRuinVine.Value = true;
                Vector3 ladder = new Vector3(73.65f, .85f, 42);
                yield return StandRuin(a, new Vector3(74.35f, 0, 42), ladder);
                UseRuinTest(ruins, a, ladder);
                Expect(a.RuinClimbing.Value && !a.HasRuinVine.Value, "RUIN ladder consumes a vine and begins climbing");
                yield return new WaitForSeconds(config.ruinClimbSeconds + .4f);
                Expect(!a.RuinClimbing.Value && Vector3.Distance(a.transform.position, new Vector3(70.9f, 2.51f, 42)) < .3f,
                    "RUIN ladder arrives at the inward safe landing");

                Vector3 boost = new Vector3(60, 1, 34);
                yield return StandRuin(a, new Vector3(60, 0, 34), boost);
                yield return StandRuin(b, new Vector3(59, 0, 34), boost);
                UseRuinTest(ruins, a, boost);
                UseRuinTest(ruins, b, boost);
                yield return new WaitForSeconds(config.ruinBoostSeconds + .4f);
                Expect(!b.RuinClimbing.Value && b.transform.position.y > 2.3f, "RUIN two teammates can boost one onto the terrace");
                UseRuinTest(ruins, b, b.EyePosition + b.transform.forward);
                yield return new WaitForSeconds(config.ruinBoostSeconds + .4f);
                Expect(!a.RuinClimbing.Value && a.transform.position.y > 2.3f, "RUIN upper teammate can pull the helper up");

                int orange = director.OrangeScore.Value, blue = director.BlueScore.Value;
                CargoItem lost = CargoItem.All.First(c => c.State.Value == CargoState.Ground && c.Definition.value == 10);
                Vector3 cargoStart = lost.transform.position;
                lost.GetComponent<Rigidbody>().position = new Vector3(28.5f, -2.8f, 39.5f);
                yield return new WaitForSeconds(.5f);
                Expect(Vector3.Distance(lost.transform.position, cargoStart) < .3f, "RUIN unscored cargo returns from a pit");
                yield return StandRuin(a, new Vector3(28.5f, -2.8f, 39.5f), Vector3.forward);
                yield return new WaitForSeconds(.5f);
                Expect(a.transform.position.y > -.5f && Vector3.Distance(a.transform.position, new Vector3(18, 0, 32.6f)) < 1,
                    "RUIN player returns from the west pit");
                Expect(director.OrangeScore.Value == orange && director.BlueScore.Value == blue, "RUIN pit returns do not change scores");

                CargoItem large = CargoItem.All.First(c => c.State.Value == CargoState.Ground && c.Definition.value == 100);
                Vector3 axis = large.transform.right;
                float reach = large.HalfExtents.x + .55f;
                yield return StandRuin(a, large.transform.position-axis*reach-Vector3.up*large.HalfExtents.y, large.transform.position);
                a.TestGrab(large);
                yield return new WaitForSeconds(.2f);
                yield return StandRuin(b, large.transform.position+axis*reach-Vector3.up*large.HalfExtents.y, large.transform.position);
                b.TestGrab(large);
                yield return new WaitForSeconds(.3f);
                Expect(a.CarriedCargo == large && b.CarriedCargo == large && !large.HasFreeSlot, "RUIN original 100 crate accepts two carriers");
                Expect(Mathf.Abs(large.GrabPointWorld(a).x-large.transform.position.x) < .1f, "RUIN co-carry grips follow the rotated cargo axis");
                large.transform.position = new Vector3(28.5f, large.CoCarryLift, 33.5f);
                yield return new WaitForSeconds(.4f);
                Expect(large.State.Value == CargoState.Ground && a.CarriedCargo == null && b.CarriedCargo == null,
                    "RUIN co-carry releases when one carrier loses pit-edge support while idle");
                large.ServerResetFromRuinPit();
                Log("RUIN interaction suite finished");
            }
            finally
            {
                for (int i = 0; i < actors.Length; i++) actors[i].TeleportRpc(starts[i], Quaternion.identity);
                BotsIdle = false;
            }
        }

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
