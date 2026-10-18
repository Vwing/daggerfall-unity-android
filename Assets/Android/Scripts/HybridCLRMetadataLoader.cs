using UnityEngine;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Loads supplementary AOT metadata so HybridCLR can interpret generic instantiations
    /// that precompiled mod assemblies use but the IL2CPP build never generated code for.
    /// </summary>
    public static class HybridCLRMetadataLoader
    {
        // Stripped AOT assemblies copied into Resources by HybridCLRSpikeBuild before the player build.
        // Covers every assembly Android/HybridCLR/link.xml preserves, since mods call their generic methods with
        // types the base game never instantiates (e.g. Dynamic Skies' JsonUtility.FromJson<T> on its own classes).
        public const string ResourcesFolder = "HybridCLRAOTMetadata";
        public static readonly string[] AssemblyNames =
        {
            "mscorlib", "System", "System.Core",
            "Assembly-CSharp", "Assembly-CSharp-firstpass",
            "UnityEngine.CoreModule", "UnityEngine.AnimationModule", "UnityEngine.AssetBundleModule", "UnityEngine.AudioModule",
            "UnityEngine.IMGUIModule", "UnityEngine.InputLegacyModule", "UnityEngine.JSONSerializeModule",
            "UnityEngine.ParticleSystemModule", "UnityEngine.PhysicsModule", "UnityEngine.TextRenderingModule",
            "UnityEngine.UI", "UnityEngine.UIModule",
        };

#if ENABLE_IL2CPP && !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LoadMetadata()
        {
            foreach (string assemblyName in AssemblyNames)
            {
                TextAsset dll = Resources.Load<TextAsset>($"{ResourcesFolder}/{assemblyName}.dll");
                if (dll == null)
                {
                    Debug.LogWarning($"[HybridCLR] No AOT metadata found for {assemblyName}");
                    continue;
                }

                HybridCLR.LoadImageErrorCode result = HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(dll.bytes, HybridCLR.HomologousImageMode.SuperSet);
                Debug.Log($"[HybridCLR] Loaded AOT metadata for {assemblyName}: {result}");
                Resources.UnloadAsset(dll);
            }
        }
#endif
    }
}
