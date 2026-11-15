using UnityEngine;

namespace CargoClash.Art
{
    /// <summary>Shared visual interface for map-specific wrecks; gameplay remains in the game.</summary>
    public sealed class TruckVariantReferences : MonoBehaviour
    {
        public string variantId = "RainRiver_11";
        public Transform body;
        public Transform cargoBed;
        public Transform tailgate;
        public Transform cargoCenter;
        public Transform bedSeat;
        public Transform[] liftAnchors;
        public Transform[] chassisLocks;
        public Transform damageDetails;
        public GameObject optionalGroundDressing;
    }
}
