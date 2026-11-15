using UnityEngine;

namespace CargoClash.RiverV10
{
    // Presentation and grip only. The game's host must validate pickup and consume the inventory unit.
    public sealed class WoodPickupView : MonoBehaviour
    {
        public Transform gripSocket;
        public Collider pickupCollider;
        public Renderer[] visuals;
        public const int BoardUnits = 1;
        public void SetAvailable(bool available)
        {
            foreach(var r in visuals)r.enabled=available;
            pickupCollider.enabled=available;
        }
    }
}
