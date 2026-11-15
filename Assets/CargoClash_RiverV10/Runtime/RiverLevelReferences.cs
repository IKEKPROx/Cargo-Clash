using UnityEngine;

namespace CargoClash.RiverV10
{
    public sealed class RiverLevelReferences : MonoBehaviour
    {
        [Header("Authored layout; assign these to the existing game's systems")]
        public Transform[] cargoSpawns;
        public string[] cargoKinds;
        public string[] cargoZones;
        public Transform recoveryBed;
        public Transform westFord;
        public Transform eastFord;
        public Transform downstreamCatcher;
        public RiverFlowPath flowPath;
        public Transform[] woodSpawns;
        public GameObject previewWood;
        public CatcherVisualController catcher;
        [Tooltip("These are visual examples. Disable before the host spawns NetworkObjects.")]
        public GameObject previewCargo;
        [Tooltip("User's requested duration for this level, not an automatic global GameConfig change.")]
        public int suggestedHalfSeconds = 180;

        public void DisablePreviewCargoForGameplay() { if (previewCargo) previewCargo.SetActive(false); }
    }
}
