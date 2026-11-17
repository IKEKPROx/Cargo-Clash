using System;
using System.Linq;
using CargoClash;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Upgrades only the new ruin scene to the desktop version's HUD. Never runs Build Game Content.</summary>
public static class RuinMacIntegration
{
    public static void Prepare()
    {
        const string targetPath = "Assets/Game/Scenes/LevelRuinCourtyard.unity";
        Scene target = EditorSceneManager.OpenScene(targetPath, OpenSceneMode.Single);
        Scene source = EditorSceneManager.OpenScene("Assets/Game/Scenes/Level01.unity", OpenSceneMode.Additive);
        MatchHud previous = FindHud(target);
        MatchHud template = FindHud(source);
        if (template.settings == null || template.pauseSettingsButton == null)
            throw new InvalidOperationException("Desktop HUD has no settings page.");
        GameObject oldRoot = previous.transform.root.gameObject;
        GameObject sourceRoot = template.transform.root.gameObject;
        if (oldRoot.GetComponentInChildren<LevelLayout>(true) != null || sourceRoot.GetComponentInChildren<LevelLayout>(true) != null)
            throw new InvalidOperationException("HUD must have a separate scene root.");

        GameObject replacement = Object.Instantiate(sourceRoot);
        replacement.name = sourceRoot.name;
        SceneManager.MoveGameObjectToScene(replacement, target);
        Object.DestroyImmediate(oldRoot);
        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(target);
        MatchHud hud = FindHud(target);
        if (hud.settings == null || hud.pauseSettingsButton == null)
            throw new InvalidOperationException("Settings references were not copied.");
        EditorSceneManager.MarkSceneDirty(target);
        if (!EditorSceneManager.SaveScene(target)) throw new InvalidOperationException("Could not save ruin HUD upgrade.");

        GameConfig config = GameConfig.Get();
        if (config.ruinCargo == null || config.ruinCargo.Length != 4 ||
            config.ruinCargo.Count(d => d.size == CargoSize.Small && d.value == 10) != 2 ||
            config.ruinCargo.Count(d => d.size == CargoSize.Medium && d.value == 30) != 1 ||
            config.ruinCargo.Count(d => d.size == CargoSize.Large && d.value == 100) != 1)
            throw new InvalidOperationException("Ruin cargo must use four Astra kinds across the 10/30/100 tiers.");
        foreach (string scene in new[] { "Level01", "LevelRiver", RuinCourtyard.SceneName })
            if (!EditorBuildSettings.scenes.Any(s => s.enabled && s.path.EndsWith("/"+scene+".unity", StringComparison.Ordinal)))
                throw new InvalidOperationException("Missing playable scene: " + scene);
        Debug.Log("[RuinMacIntegration] Prepared ruin HUD with settings/localization; tutorial, river and ruin registered; cargo values 10/30/100 preserved.");
    }

    private static MatchHud FindHud(Scene scene) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<MatchHud>(true)).Single();
}
