using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>
    /// The airport level's rules. It sits on the MatchDirector's network object and does nothing on a level without an
    /// <see cref="AirportFacility"/>.
    ///
    /// The iron door between the ladder room and the upper deck: either team presses E beside it, and it swings away from
    /// whoever opened it. And the warehouse: each half only a few cases start out on the floor (the level's
    /// LevelLayout.kindSpots); the rest come out of the warehouse one at a time onto B03, in Astra's order, spread over
    /// the first two thirds of the half. A blocked belt mouth holds the next one back until there's room.
    /// </summary>
    public sealed class AirportMatch : NetworkBehaviour
    {
        public static AirportMatch Instance { get; private set; }

        /// <summary>0 shut, 1 swung into the ladder room, -1 swung out onto the deck.</summary>
        public readonly NetworkVariable<int> DoorState = new NetworkVariable<int>();
        /// <summary>How many cases the warehouse sends out this half (fewer with fewer players), and how many have gone.</summary>
        public readonly NetworkVariable<int> Queued = new NetworkVariable<int>();
        public readonly NetworkVariable<int> Dispatched = new NetworkVariable<int>();

        private GameConfig config;
        private AirportFacility facility;
        private readonly Queue<CargoDefinition> queue = new Queue<CargoDefinition>();
        private double nextDispatchAt;
        private float dispatchGap;

        public override void OnNetworkSpawn()
        {
            config = GameConfig.Get();
            facility = AirportFacility.Instance;
            if (facility == null) return;
            Instance = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Cases the warehouse still has to send out this half.</summary>
        public bool HasPending => queue.Count > 0;
        public int Pending => queue.Count;

        // ---- Host only ----

        /// <summary>A new half: the door shuts, the duct is free and the warehouse fills up again.</summary>
        public void ServerResetHalf(int thirds)
        {
            DoorState.Value = 0;
            facility.ResetTraversal();
            queue.Clear();
            int count = facility.dispatchOrder.Length * thirds / 3;
            for (int i = 0; i < count; i++)
            {
                CargoDefinition definition = System.Array.Find(config.cargo, d => d.kind == facility.dispatchOrder[i]);
                if (definition != null) queue.Enqueue(definition);
            }
            Queued.Value = queue.Count;
            Dispatched.Value = 0;
        }

        /// <summary>The whistle: the first case comes out shortly, the last by airportDispatchFraction of the half.</summary>
        public void ServerStartHalf(double now, float halfSeconds)
        {
            nextDispatchAt = now + config.airportDispatchStart;
            float spread = halfSeconds * config.airportDispatchFraction - config.airportDispatchStart;
            dispatchGap = Mathf.Max(config.airportDispatchMinGap, spread / Mathf.Max(1, queue.Count - 1));
        }

        /// <summary>The next case, once it's due and there's room for it at the belt's mouth; null otherwise.</summary>
        public CargoDefinition ServerTryDispatch(double now, out Vector3 position)
        {
            position = default;
            if (queue.Count == 0 || now < nextDispatchAt) return null;
            CargoDefinition definition = queue.Peek();
            Vector3 halfExtents = definition.prefab.GetComponent<CargoItem>().HalfExtents;
            if (!facility.TryDispatchPosition(halfExtents, out position)) return null;
            queue.Dequeue();
            Dispatched.Value++;
            nextDispatchAt = now + dispatchGap;
            return definition;
        }

        /// <summary>Someone pressed E beside the iron door.</summary>
        public void ServerToggleDoor(Vector3 feet)
        {
            DoorState.Value = facility.DoorStateAfterToggle(DoorState.Value, feet);
            AutoPilot.Log($"AIRPORT_DOOR state={DoorState.Value}");
        }
    }
}
