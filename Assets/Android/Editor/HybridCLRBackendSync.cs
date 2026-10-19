using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Enables HybridCLR for IL2CPP builds and disables it for Mono builds, before HybridCLR's own build checks run.
/// Left enabled, HybridCLR switches a Mono build's scripting backend to IL2CPP, so 32-bit Mono builds need it off.
/// </summary>
class HybridCLRBackendSync : IPreprocessBuildWithReport
{
    // HybridCLR's build processors use callbackOrder 0
    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        bool il2cpp = PlayerSettings.GetScriptingBackend(report.summary.platformGroup) == ScriptingImplementation.IL2CPP;
        if (HybridCLRSettings.instance.enable == il2cpp)
            return;

        // Not saved, so the committed setting stays enabled and every build sets it from its own backend
        HybridCLRSettings.instance.enable = il2cpp;
        Debug.Log($"[HybridCLRBackendSync] HybridCLR {(il2cpp ? "enabled for this IL2CPP" : "disabled for this Mono")} build");
    }
}
