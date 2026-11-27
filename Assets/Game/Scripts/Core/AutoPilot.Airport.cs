using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CargoClash
{
    /// <summary>
    /// -ccAirportTest, with -ccMode practice -ccLevel 2: the host walks the airport's facilities with real key presses
    /// and checks them, then lets the bots finish the match. The bots stand still while it runs (BotsIdle). Ported from the
    /// teammate's own test for the level (Map3 de_airport, Tools/UnityEditor/Airport-Editor-Tools.patch).
    /// </summary>
    public sealed partial class AutoPilot
    {
        private IEnumerator TestTheAirport()
        {
            while (MatchDirector.Instance == null || !MatchDirector.Instance.IsSpawned || MatchDirector.Instance.Phase.Value != MatchPhase.Playing
                   || PlayerAvatar.Local == null || AirportFacility.Instance == null || AirportMatch.Instance == null)
                yield return null;
            MatchDirector match = MatchDirector.Instance;
            PlayerAvatar player = PlayerAvatar.Local;
            AirportFacility facility = AirportFacility.Instance;
            AirportMatch airport = AirportMatch.Instance;
            if (!player.IsServer)
            {
                Log("AIRPORT test skipped: it runs on the host.");
                yield break;
            }
            BotsIdle = true;
            DrivesInput = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (InputDevice device in InputSystem.devices) InputSystem.DisableDevice(device);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("AirportTestKeyboard");
            Mouse mouse = InputSystem.AddDevice<Mouse>("AirportTestMouse");
            keyboard.MakeCurrent();
            mouse.MakeCurrent();

            LevelLayout layout = LevelLayout.Instance;
            Log($"AIRPORT queued={airport.Queued.Value} cargo={CargoItem.All.Count} deck={facility.UpperDeckHeight:0.00}");
            Expect(airport.Queued.Value == facility.dispatchOrder.Length, $"the warehouse holds the whole queue at three a side ({airport.Queued.Value})");
            Expect(CargoItem.All.Count <= 4 + airport.Dispatched.Value, "only the opening cases start on the floor");
            Expect(layout.InTruckZone(layout.truckZone.position), "the truck zone counts on the upper deck");
            Expect(!layout.InTruckZone(layout.truckZone.position + Vector3.down * facility.UpperDeckHeight),
                "standing under the truck on the floor below doesn't count");

            // The iron door, from both sides.
            Vector3 intoRoom = facility.doorSwing.position - facility.doorHinge.position;
            intoRoom.y = 0f;
            intoRoom.Normalize();
            Vector3 doorFeet = facility.doorCenter.position - Vector3.up * 1.15f;
            player.TeleportRpc(doorFeet - intoRoom * 1.35f, Quaternion.LookRotation(intoRoom));
            yield return new WaitForSeconds(0.16f);
            yield return PressE();
            Expect(airport.DoorState.Value == 1, "E opens the door away from the deck side");
            Shot("airport-door-open");
            player.TeleportRpc(doorFeet - intoRoom * 1.35f, Quaternion.LookRotation(-intoRoom));
            yield return new WaitForSeconds(0.15f);
            yield return PressE();
            Expect(airport.DoorState.Value == 0, "E shuts it again from outside, even looking away");
            player.TeleportRpc(doorFeet + intoRoom * 1.35f, Quaternion.LookRotation(-intoRoom));
            yield return new WaitForSeconds(0.16f);
            yield return PressE();
            Expect(airport.DoorState.Value == -1, "from inside the ladder room it swings out the other way");

            // B03 carries loose cargo.
            Vector3 along = facility.BeltDirection;
            CargoItem box = CargoItem.All.Find(c => c.State.Value == CargoState.Ground && c.Definition.size == CargoSize.Small);
            Expect(box != null, "a small case is on the floor");
            if (box != null)
            {
                Rigidbody physics = box.GetComponent<Rigidbody>();
                Vector3 onBelt = facility.beltIn.position + along * 2f + Vector3.up * (box.HalfExtents.y + 0.08f);
                Log($"BELTBOX {box.Definition.kind} at={box.transform.position} kinematic={physics.isKinematic} state={box.State.Value}");
                physics.position = onBelt;
                physics.linearVelocity = Vector3.zero;
                physics.angularVelocity = Vector3.zero;
                yield return new WaitForSeconds(0.85f);
                Log($"BELTBOX moved={Vector3.Dot(physics.position - onBelt, along):0.00} now={physics.position} velocity={physics.linearVelocity} " +
                    $"kinematic={physics.isKinematic} sleeping={physics.IsSleeping()} state={box.State.Value} bottom={box.BottomHeight:0.000} conveyed={facility.Conveyed}");
                Expect(Vector3.Dot(physics.position - onBelt, along) > 0.5f, "B03 carries a loose case along");
            }

            // The goose crate waits on the floor before the ramp, and lifting it keeps it on that floor.
            CargoItem crate = CargoItem.All.Find(c => c.State.Value == CargoState.Ground && c.Definition.requiredCarriers > 1);
            Expect(crate != null, "the goose crate starts on the floor");
            if (crate != null)
            {
                float floor = CargoItem.GroundBelow(crate.transform.position, out float height) ? height : 0f;
                Vector3 grab = crate.transform.position - Vector3.right * (crate.Definition.coCarryReach + 0.1f);
                grab.y = floor + 0.02f;
                player.TeleportRpc(grab, Quaternion.LookRotation(Vector3.right));
                yield return new WaitForSeconds(0.12f);
                bool held = crate.ServerTryGrab(player);
                Expect(held && Mathf.Abs(crate.transform.position.y - floor - crate.Definition.coCarryHeight) < 0.05f,
                    $"lifting the crate keeps it on its own floor ({crate.transform.position.y - floor:0.00} over it)");
                if (held) crate.ServerRelease(player);
                yield return new WaitForSeconds(0.2f);
            }

            // The ladder, up and down, with a case in hand.
            CargoItem gold = CargoItem.All.Find(c => c.State.Value == CargoState.Ground && c.Definition.size == CargoSize.Medium);
            Expect(gold != null && gold.ServerTryGrab(player), "can hold a gold case before climbing");
            yield return new WaitForSeconds(0.2f);
            Vector3 across = Vector3.Cross(Vector3.up, (facility.ladderLow.position - facility.ladderShaft.position).normalized);
            yield return Traverse(AirportUse.LadderUp, facility.ladderLow.position + across * 0.72f, facility.ladderShaft.position);
            Shot("airport-ladder-top");
            Expect(gold != null && player.CarriedCargo == gold, "the case stays in hand on the way up");
            yield return Traverse(AirportUse.LadderDown, facility.ladderHigh.position, facility.ladderShaft.position);
            Expect(gold != null && player.CarriedCargo == gold, "the case stays in hand on the way down");
            if (gold != null && player.CarriedCargo == gold) gold.ServerRelease(player);
            yield return new WaitForSeconds(0.2f);

            // The duct, down only.
            yield return Traverse(AirportUse.DuctDown, facility.ductUpper.position, facility.ductWaypoints[1].position);
            Expect(!player.KnockedOut.Value, "sliding down the duct is safe");
            yield return WalkIntoDuctOutlet();

            // The belt carries someone standing on it.
            Vector3 beltFeet = facility.shaftLanding.position + Vector3.up * 0.03f;
            player.TeleportRpc(beltFeet, Quaternion.LookRotation(along));
            yield return new WaitForSeconds(0.78f);
            Expect(Vector3.Dot(player.transform.position - beltFeet, along) > 0.55f, "B03 carries someone standing on it");

            // Falls from the deck: onto the belt is safe, onto the floor beside it isn't.
            Vector3 rim = beltFeet - along * 9f;
            rim.y = facility.UpperDeckHeight + 0.03f;
            yield return WalkOff(rim);
            Expect(!player.KnockedOut.Value && player.transform.position.y < beltFeet.y + 0.3f, "a fall onto B03 is safe");
            Vector3 aside = Vector3.ProjectOnPlane(facility.unsafeDrop.position - facility.shaftLanding.position, along);
            aside.y = 0f;
            yield return WalkOff(rim + aside);
            Expect(player.KnockedOut.Value && player.Health.Value == 0, "a fall beside B03 knocks you out");
            Shot("airport-fell");

            // The bots play the rest of the half, and the second half starts with the door shut again.
            BotsIdle = false;
            DrivesInput = false;
            SendKeys(keyboard);
            while (match.Half.Value != 2 || match.Phase.Value != MatchPhase.Playing) yield return null;
            Expect(airport.DoorState.Value == 0, "the iron door shuts again at half-time");
            BotsIdle = true;
            DrivesInput = true;
            while (player.IsKnockedDown || player.IsStaggered) yield return null;
            player.TeleportRpc(doorFeet - intoRoom * 1.35f, Quaternion.LookRotation(intoRoom));
            yield return new WaitForSeconds(0.2f);
            for (float until = Time.time + 2.2f; Time.time < until;)
            {
                SendMouse(mouse, Vector2.zero, true, false);
                yield return null;
            }
            SendMouse(mouse, Vector2.zero, false, false);
            Expect(airport.DoorState.Value == 0 && facility.doorLeaf.gameObject.activeInHierarchy, "the hammer can't break the iron door");
            yield return WalkOff(rim + aside);
            Expect(player.KnockedOut.Value && player.Health.Value == 0, "the other side falls the same way");
            BotsIdle = false;
            DrivesInput = false;
            SendKeys(keyboard);
            Log("AIRPORT test done");

            IEnumerator PressE()
            {
                SendKeys(keyboard, Key.E);
                yield return new WaitForSeconds(0.10f);
                SendKeys(keyboard);
                yield return new WaitForSeconds(0.12f);
            }

            IEnumerator WalkOff(Vector3 from)
            {
                while (player.IsKnockedDown || player.IsStaggered) yield return null;
                player.TeleportRpc(from, Quaternion.LookRotation(along));
                yield return new WaitForSeconds(0.5f);
                for (float until = Time.time + 1f; Time.time < until && player.transform.position.y >= from.y - 0.18f;)
                {
                    SendKeys(keyboard, Key.W);
                    yield return null;
                }
                SendKeys(keyboard);
                yield return new WaitForSeconds(1.05f);
            }

            IEnumerator Traverse(AirportUse use, Vector3 from, Vector3 toward)
            {
                Vector3 facing = Vector3.ProjectOnPlane(toward - from, Vector3.up);
                player.TeleportRpc(from, Quaternion.LookRotation(facing));
                yield return new WaitForSeconds(0.17f);
                for (float until = Time.time + 0.55f; Time.time < until && !player.IsTravellingAirport;)
                {
                    SendKeys(keyboard, Key.W);
                    yield return null;
                }
                SendKeys(keyboard);
                Expect(player.IsTravellingAirport, $"walking into it starts {use} without pressing anything");
                Vector3[] route = facility.Route(use);
                yield return new WaitForSeconds(facility.TravelSeconds(use, from) + 0.45f);
                Expect(Vector3.Distance(player.transform.position, route[route.Length - 1]) < 0.55f, $"{use} gets to the other end");
            }

            IEnumerator WalkIntoDuctOutlet()
            {
                Vector3 from = facility.ductLower.position;
                Vector3 lip = facility.ductWaypoints[facility.ductWaypoints.Length - 2].position;
                Vector3 facing = Vector3.ProjectOnPlane(lip - from, Vector3.up).normalized;
                player.TeleportRpc(from + Vector3.up * 0.035f, Quaternion.LookRotation(facing));
                yield return new WaitForSeconds(0.16f);
                float highest = from.y;
                bool started = false;
                for (float until = Time.time + 1.7f; Time.time < until;)
                {
                    SendKeys(keyboard, Key.W);
                    yield return null;
                    highest = Mathf.Max(highest, player.transform.position.y);
                    started |= player.IsTravellingAirport;
                }
                SendKeys(keyboard);
                Expect(Vector3.Dot(player.transform.position - from, facing) > 2.7f, "the duct's outlet can be walked into");
                Expect(!started && highest < from.y + 1.5f, $"the duct can't be climbed from below (got to {highest - from.y:0.0} m)");
            }
        }
    }
}
