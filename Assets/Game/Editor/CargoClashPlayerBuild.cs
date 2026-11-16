using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>Builds playable copies of the game into the Builds folder, which Git ignores.</summary>
public static class CargoClashPlayerBuild
{
    [MenuItem("Tools/Cargo Clash/Build Mac Player")]
    public static void Mac()
    {
        // Apple silicon only. Unity 6.6 calls Intel Macs deprecated, and a universal build fails here: Burst's
        // llvm-lipo in this Editor can't be run ("Access denied") when it joins the two halves.
        EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Architecture", "ARM64");
        const string path = "Builds/Mac/CargoClash.app";
        Build(BuildTarget.StandaloneOSX, path);
        ApplyMacIcon(path);
    }

    /// <summary>
    /// Unity makes the Mac icon from the same square picture as the Windows one, which looks too big next to other Mac
    /// apps. This puts in AppIcon_Mac.png instead, which follows Apple's grid (a rounded square with a margin and a
    /// shadow), and signs the app again the way Unity does (ad hoc, with no certificate), since changing it breaks that.
    /// </summary>
    private static void ApplyMacIcon(string appPath)
    {
        string source = Path.GetFullPath(CargoClashBuilder.MacAppIconPath);
        string target = Path.Combine(appPath, "Contents/Resources/PlayerIcon.icns");
        if (!File.Exists(source) || !File.Exists(target))
        {
            Debug.LogError($"[Build] Can't put the Mac icon in: {source} or {target} is missing.");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        string work = Path.Combine(Path.GetTempPath(), "CargoClashIcon");
        string iconset = Path.Combine(work, "AppIcon.iconset");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(iconset);
        foreach (int size in new[] { 16, 32, 128, 256, 512 })
        {
            Run("sips", $"-z {size} {size} \"{source}\" --out \"{iconset}/icon_{size}x{size}.png\"");
            Run("sips", $"-z {size * 2} {size * 2} \"{source}\" --out \"{iconset}/icon_{size}x{size}@2x.png\"");
        }
        string icns = Path.Combine(work, "AppIcon.icns");
        bool made = Run("iconutil", $"-c icns \"{iconset}\" -o \"{icns}\"");
        if (made) File.Copy(icns, target, true);
        // File-provider folders can attach Finder metadata while the icon is replaced.
        // Remove only signing-incompatible metadata; keep quarantine/provenance attributes.
        Run("xattr", $"-rd com.apple.FinderInfo \"{Path.GetFullPath(appPath)}\"");
        Run("xattr", $"-rd com.apple.ResourceFork \"{Path.GetFullPath(appPath)}\"");
        bool signed = Run("codesign", $"--force --deep --sign - \"{Path.GetFullPath(appPath)}\"");
        Directory.Delete(work, true);
        Debug.Log($"[Build] Mac icon {(made ? "in" : "FAILED")}, signed {(signed ? "again" : "FAILED")}.");
        if (Application.isBatchMode && !(made && signed)) EditorApplication.Exit(1);
    }

    private static bool Run(string tool, string arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo(tool, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using (Process process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0) return true;
            Debug.LogError($"[Build] {tool} {arguments} failed ({process.ExitCode}): {output}");
            return false;
        }
    }

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
