using System;
using System.IO;
using System.Linq;
using DaggerfallWorkshop;
using HybridCLR;
using HybridCLR.Commands;
using HybridCLR.Installer;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Entry points for the HybridCLR spike. Installs HybridCLR from the reviewed clones next to the project,
/// then builds an ARM64 APK either under a separate package name so it can be installed beside the release build,
/// or under the release package name so it installs over the release build and keeps the user's data.
/// Batch mode: Unity.exe -batchmode -quit -projectPath . -executeMethod HybridCLRSpikeBuild.Install (then .Build or .BuildAsUpdate)
/// </summary>
public static class HybridCLRSpikeBuild
{
    const string SideBySideApplicationId = "net.dfworkshop.dfu.hybridclr";
    const string SideBySideProductName = "DFU HybridCLR";
    const string SideBySideOutputPath = "Builds/DFU-HybridCLR.apk";
    const string UpdateApplicationId = "net.dfworkshop.dfu";
    const string UpdateOutputPath = "Builds/DFU-HybridCLR-As-Update.apk";
    const string MetadataResourcesRoot = "Assets/Android/HybridCLR/Resources";
    const string MetadataResourcesDir = MetadataResourcesRoot + "/" + HybridCLRMetadataLoader.ResourcesFolder;

    [MenuItem("Daggerfall Tools/Android/HybridCLR Spike/Install From Local Clones")]
    public static void Install()
    {
        HybridCLRSettings settings = HybridCLRSettings.instance;
        settings.enable = true;
        settings.useGlobalIl2cpp = false;
        settings.hybridclrRepoURL = "https://github.com/focus-creative-games/hybridclr";
        settings.il2cppPlusRepoURL = "https://github.com/focus-creative-games/il2cpp_plus";
        settings.hotUpdateAssemblies = new string[0];
        settings.hotUpdateAssemblyDefinitions = new AssemblyDefinitionAsset[0];
        settings.preserveHotUpdateAssemblies = new string[0];
        HybridCLRSettings.Save();

        // Mirror what the installer does after cloning: libil2cpp from il2cpp_plus with hybridclr dropped inside it
        string gitRoot = Path.GetFullPath(Path.Combine(SettingsUtil.ProjectDir, ".."));
        string il2cppPlusDir = Path.Combine(gitRoot, "il2cpp_plus", "libil2cpp");
        string hybridclrDir = Path.Combine(gitRoot, "hybridclr", "hybridclr");
        if (!Directory.Exists(il2cppPlusDir) || !Directory.Exists(hybridclrDir))
            throw new DirectoryNotFoundException($"Expected HybridCLR clones at {il2cppPlusDir} and {hybridclrDir}");

        string stagingDir = Path.Combine(SettingsUtil.HybridCLRDataDir, "local_libil2cpp");
        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, true);
        CopyDirectory(il2cppPlusDir, stagingDir);
        CopyDirectory(hybridclrDir, Path.Combine(stagingDir, "hybridclr"));

        var installer = new InstallerController();
        installer.InstallFromLocal(stagingDir);
        if (!installer.HasInstalledHybridCLR())
            throw new Exception("HybridCLR install failed");
        Debug.Log($"[HybridCLRSpikeBuild] Installed HybridCLR {installer.PackageVersion} into {SettingsUtil.LocalIl2CppDir}");
    }

    [MenuItem("Daggerfall Tools/Android/HybridCLR Spike/Build APK")]
    public static void Build()
    {
        BuildApk(SideBySideApplicationId, SideBySideProductName, SideBySideOutputPath);
    }

    // Same package name and debug signing key as the release build, so it installs as an update
    [MenuItem("Daggerfall Tools/Android/HybridCLR Spike/Build APK As Update")]
    public static void BuildAsUpdate()
    {
        BuildApk(UpdateApplicationId, VersionInfo.DaggerfallUnityProductName, UpdateOutputPath);
    }

    static void BuildApk(string applicationId, string productName, string outputPath)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, applicationId);
        PlayerSettings.productName = productName;
        PlayerSettings.bundleVersion = VersionInfo.DaggerfallUnityForAndroidVersion;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        Debug.Log($"[HybridCLRSpikeBuild] Managed stripping level: {PlayerSettings.GetManagedStrippingLevel(BuildTargetGroup.Android)}");
        Debug.Log($"[HybridCLRSpikeBuild] Building {applicationId} version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");

        // Generates il2cpp defines, method bridges and the stripped AOT assemblies used for supplementary metadata
        PrebuildCommand.GenerateAll();
        CopyAOTMetadata();
        try
        {
            AddressableAssetSettings.BuildPlayerContent(out var addressablesResult);
            if (!string.IsNullOrEmpty(addressablesResult.Error))
                throw new Exception($"Addressables build failed: {addressablesResult.Error}");

            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"Player build {report.summary.result} with {report.summary.totalErrors} errors");
            Debug.Log($"[HybridCLRSpikeBuild] Built {outputPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
        }
        finally
        {
            // Anything left in a Resources folder ships in every later build, Mono ones included
            AssetDatabase.DeleteAsset(MetadataResourcesRoot);
        }
    }

    static void CopyAOTMetadata()
    {
        string strippedDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.Android);
        Directory.CreateDirectory(MetadataResourcesDir);
        foreach (string assemblyName in HybridCLRMetadataLoader.AssemblyNames)
        {
            string source = Path.Combine(strippedDir, assemblyName + ".dll");
            if (!File.Exists(source))
                throw new FileNotFoundException($"Stripped AOT assembly missing: {source}");
            File.Copy(source, Path.Combine(MetadataResourcesDir, assemblyName + ".dll.bytes"), true);
        }
        AssetDatabase.Refresh();
    }

    static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
