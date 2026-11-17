using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CargoClash;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

/// <summary>Refreshes NGO identities for ruin-only copies and checks their references without regenerating content.</summary>
public static class RuinCargoValidation
{
    public static void BuildMac()
    {
        Prepare();
        CargoClashPlayerBuild.Mac();
    }

    public static void Prepare()
    {
        GameConfig config = GameConfig.Get();
        CargoDefinition[] definitions = config.ruinCargo;
        if (definitions == null || definitions.Length != 4 ||
            definitions.Count(d => d.size == CargoSize.Small && d.value == 10) != 2 ||
            definitions.Count(d => d.kind == CargoKind.Gold && d.value == 30) != 1 ||
            definitions.Count(d => d.kind == CargoKind.Art && d.value == 100) != 1)
            throw new InvalidOperationException("Ruin cargo must be current Tobacco/Cash10, Gold30 and Art100.");

        MethodInfo validate = typeof(NetworkObject).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (validate == null) throw new InvalidOperationException("NGO prefab identity validation is unavailable.");
        MethodInfo hashString = typeof(NetworkObject).Assembly.GetType("Unity.Netcode.XXHash", true)
            .GetMethod("Hash32", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (hashString == null) throw new InvalidOperationException("NGO prefab identity hashing is unavailable.");
        var hashes = new HashSet<uint>();
        var prefabs = new List<GameObject> { config.avatarPrefab, config.directorPrefab, config.rosterPrefab };
        prefabs.AddRange(config.cargo.Select(d => d.prefab));
        prefabs.AddRange(definitions.Select(d => d.prefab));
        if (config.plank != null) prefabs.Add(config.plank.prefab);
        foreach (GameObject prefab in prefabs.Distinct())
        {
            NetworkObject network = prefab.GetComponent<NetworkObject>();
            if (network == null) throw new InvalidOperationException("Missing NetworkObject: " + prefab.name);
            // NGO uses the asset GUID AND component fileID. Read existing prefabs without changing them.
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(network);
            if (id.identifierType == 0) throw new InvalidOperationException("Missing prefab asset identity: " + prefab.name);
            uint expected = (uint)hashString.Invoke(null, new object[] { id.ToString() });
            if (expected == 0 || !hashes.Add(expected)) throw new InvalidOperationException("Duplicate/empty network prefab identity: " + prefab.name);
            if (definitions.Any(d => d.prefab == prefab))
            {
                validate.Invoke(network, null);
                uint hash = new SerializedObject(network).FindProperty("GlobalObjectIdHash").uintValue;
                if (hash != expected) throw new InvalidOperationException("Ruin prefab did not receive its canonical identity: " + prefab.name);
                EditorUtility.SetDirty(network);
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssetIfDirty(prefab);
                Debug.Log($"[RuinCargoValidation] {prefab.name} hash={hash}");
            }
        }

        foreach (CargoDefinition definition in definitions)
        {
            CargoItem item = definition.prefab.GetComponent<CargoItem>();
            if (item == null || item.Definition != definition || definition.kind == CargoKind.Legacy || !definition.Inspectable)
                throw new InvalidOperationException("Ruin prefab must reference its isolated current-model definition: " + definition.name);
            if (definition.prefab.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Missing cargo collider: " + definition.name);
        }
        Debug.Log("[RuinCargoValidation] Current Astra geometry, inspect definitions, 10/30/100 tiers and unique registered network identities checked.");
    }
}
