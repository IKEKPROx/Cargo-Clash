using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds playable copies of the game into the Builds folder, which Git ignores.</summary>
public static class CargoClashPlayerBuild
{
    [MenuItem("Tools/Cargo Clash/Build Mac Player")]
    public static void Mac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/CargoClash.app");

    [MenuItem("Tools/Cargo Clash/Build Windows Player")]
    public static void Windows()
    {
        // On an Apple silicon Mac, Unity otherwise builds an ARM64 player, which ordinary Windows PCs can't run.
        EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneWindows64), "Architecture", "x64");
        // Direct3D 11 works on the widest range of lab PCs and drivers. Direct3D 12 is only the fallback.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });

        const string path = "Builds/Windows/CargoClash.exe";
        Build(BuildTarget.StandaloneWindows64, path);
        CheckIsX64(path);
    }

    /// <summary>Stops a build that ordinary Windows PCs couldn't run. A wrong architecture still counts as a successful build.</summary>
    private static void CheckIsX64(string path)
    {
        const ushort x64 = 0x8664;
        using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
        {
            reader.BaseStream.Position = 0x3C;
            reader.BaseStream.Position = reader.ReadInt32() + 4;
            ushort machine = reader.ReadUInt16();
            if (machine == x64)
            {
                Debug.Log($"[Build] {path} is x64.");
                return;
            }
            Debug.LogError($"[Build] {path} isn't x64: its machine type is {machine:X4}.");
        }
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }

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
