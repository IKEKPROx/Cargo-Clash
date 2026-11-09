using Unity.Netcode;
using UnityEngine;

namespace CargoClash
{
    /// <summary>Creates the persistent network rig before any scene loads, so Play works from any scene.</summary>
    public static class GameBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            // Characters walk through cargo; only the level blocks them.
            Physics.IgnoreLayerCollision(GameLayers.Player, GameLayers.Cargo, true);
            // Bits of broken cargo land on the level and pass through everything else.
            for (int layer = 0; layer < 32; layer++)
                Physics.IgnoreLayerCollision(GameLayers.Debris, layer, layer != GameLayers.Level);
            Application.runInBackground = true;
            if (!Application.isBatchMode) QualitySettings.vSyncCount = 1;

            if (NetworkManager.Singleton != null) return;
            GameObject prefab = Resources.Load<GameObject>("NetworkRig");
            if (prefab == null)
            {
                Debug.LogWarning("Resources/NetworkRig is missing. Run Tools > Cargo Clash > Build Game Content.");
                return;
            }

            GameObject rig = Object.Instantiate(prefab);
            rig.name = "NetworkRig";
            Object.DontDestroyOnLoad(rig);
            AutoPilot.TryStart(rig);
        }
    }
}
