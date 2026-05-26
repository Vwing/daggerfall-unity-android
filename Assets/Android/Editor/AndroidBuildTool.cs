
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class AndroidBuildTool : EditorWindow
{
    private List<BuildConfig> buildConfigs = new List<BuildConfig>();
    private Vector2 scrollPosition;

    [System.Serializable]
    private class BuildConfig
    {
        public bool enabled = true;
        public bool armv7 = true;
        public bool armv64 = false;
        public bool il2cpp = true;
        public bool devBuild = false;
        public string apkName = "MyApp";
        public string version = "1.0";
        public string bundleVersion = "1";

        public BuildConfig Copy()
        {
            return new BuildConfig(){
                enabled = this.enabled,
                armv7 = this.armv7,
                armv64 = this.armv64,
                il2cpp = this.il2cpp,
                devBuild = this.devBuild,
                apkName = this.apkName,
                version = this.version,
                bundleVersion = this.bundleVersion
            };
        }
    }

    [MenuItem("Daggerfall Tools/Android/Multi-Build Tool")]
    public static void ShowWindow()
    {
        GetWindow<AndroidBuildTool>("Android Multi-Build Tool");
    }

    private void OnEnable()
    {
        LoadSettings();
    }

    private void OnGUI()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        for (int i = 0; i < buildConfigs.Count; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Build Configuration {i + 1}", EditorStyles.boldLabel);

            BuildConfig config = buildConfigs[i];

            config.enabled = EditorGUILayout.Toggle("Enabled", config.enabled);
            EditorGUI.BeginDisabledGroup(!config.enabled);
            {
                config.armv7 = EditorGUILayout.Toggle("ARMv7", config.armv7);
                config.armv64 = EditorGUILayout.Toggle("ARMv64", config.armv64);
                config.il2cpp = EditorGUILayout.Toggle("IL2CPP", config.il2cpp);
                config.devBuild = EditorGUILayout.Toggle("Development Build", config.devBuild);
                config.apkName = EditorGUILayout.TextField("APK Name", config.apkName);
                config.version = EditorGUILayout.TextField("Version", config.version);
                config.bundleVersion = EditorGUILayout.TextField("Bundle Version", config.bundleVersion);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Remove"))
            {
                buildConfigs.RemoveAt(i);
                i--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        if (GUILayout.Button("Add Build Configuration"))
        {
            buildConfigs.Add(buildConfigs.Count > 0 ? buildConfigs[^1].Copy() : new BuildConfig());
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Build All"))
        {
            BuildAll();
        }

        EditorGUILayout.EndScrollView();
    }

    private void BuildAll()
    {
        for (int i = 0; i < buildConfigs.Count; i++)
        {
            BuildConfig config = buildConfigs[i];
            if (!config.enabled)
                continue;

            BuildAndroid(config, i + 1);
        }
    }

    private void BuildAndroid(BuildConfig config, int buildNumber)
    {
        // Set build target to Android
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // Configure player settings
        PlayerSettings.Android.targetArchitectures = GetTargetArchitectures(config);
        EditorUserBuildSettings.development = config.devBuild;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, config.il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.bundleVersion = config.version;
        PlayerSettings.Android.bundleVersionCode = int.Parse(config.bundleVersion);

        // Set build options
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = GetEnabledScenes();
        buildPlayerOptions.locationPathName = $"Builds/{config.apkName}";
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = config.devBuild ? BuildOptions.Development : BuildOptions.None;

        // Build the APK
        BuildPipeline.BuildPlayer(buildPlayerOptions);
    }

    private AndroidArchitecture GetTargetArchitectures(BuildConfig config)
    {
        AndroidArchitecture arch = AndroidArchitecture.None;
        if (config.armv7) arch |= AndroidArchitecture.ARMv7;
        if (config.armv64) arch |= AndroidArchitecture.ARM64;
        return arch;
    }

    private string[] GetEnabledScenes()
    {
        List<string> enabledScenes = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                enabledScenes.Add(scene.path);
            }
        }
        return enabledScenes.ToArray();
    }

    private void OnDisable()
    {
        SaveSettings();
    }

    private void SaveSettings()
    {
        string json = JsonUtility.ToJson(new SerializableList<BuildConfig> { list = buildConfigs });
        EditorPrefs.SetString("AndroidBuildToolSettings", json);
    }

    private void LoadSettings()
    {
        string json = EditorPrefs.GetString("AndroidBuildToolSettings", "");
        if (!string.IsNullOrEmpty(json))
        {
            SerializableList<BuildConfig> loadedList = JsonUtility.FromJson<SerializableList<BuildConfig>>(json);
            buildConfigs = loadedList != null && loadedList.list != null ? loadedList.list : new List<BuildConfig>();

            if (!json.Contains("\"enabled\""))
            {
                foreach (BuildConfig config in buildConfigs)
                {
                    config.enabled = true;
                }
            }
        }
    }

    [System.Serializable]
    private class SerializableList<T>
    {
        public List<T> list;
    }
}
