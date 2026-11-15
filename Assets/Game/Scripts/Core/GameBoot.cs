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
            // Deep water stops people and nothing else; the hut roofs only keep the rain out.
            for (int layer = 0; layer < 32; layer++)
            {
                Physics.IgnoreLayerCollision(GameLayers.PlayerBlock, layer, layer != GameLayers.Player);
                Physics.IgnoreLayerCollision(GameLayers.RainOnly, layer, true);
            }
            Application.runInBackground = true;
            // VSync, the frame rate limit, graphics and volume, as the player left them (a test starts from the defaults).
            GameSettings.ApplyAtStart();

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
