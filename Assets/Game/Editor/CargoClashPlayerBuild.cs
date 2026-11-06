using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds playable copies of the game into the Builds folder, which Git ignores.</summary>
public static class CargoClashPlayerBuild
{
    [MenuItem("Tools/Cargo Clash/Build Mac Player")]
    public static void Mac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/CargoClash.app");

    [MenuItem("Tools/Cargo Clash/Build Windows Player")]
    public static void Windows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/CargoClash.exe");

    private static void Build(BuildTarget target, string path)
    {
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"[Build] {target} {summary.result} errors={summary.totalErrors} warnings={summary.totalWarnings} size={summary.totalSize / (1024 * 1024)}MB time={summary.totalTime}");
        if (Application.isBatchMode && summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
