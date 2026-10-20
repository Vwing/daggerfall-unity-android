using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using dnlib.DotNet;
using dnlib.DotNet.Emit;
#endif

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Points mods' uses of Application.streamingAssetsPath at the game's StreamingAssets folder. On Android that property
    /// gives a jar: URL into the APK, which file APIs can't open, while the game keeps its StreamingAssets, and the loose
    /// files mods are installed with, in a folder on storage that <see cref="Paths.StreamingAssetsPath"/> gives.
    /// </summary>
    public static class ModAssemblyPatcher
    {
        const string GetterName = "get_streamingAssetsPath";
        const string Replacement = "global::DaggerfallWorkshop.Paths.StreamingAssetsPath";

        static readonly byte[] getterNameBytes = Encoding.UTF8.GetBytes(GetterName);
        static readonly Regex streamingAssetsPath = new Regex(@"(?:\bglobal::)?(?:\bUnityEngine\s*\.\s*)?\bApplication\s*\.\s*streamingAssetsPath\b");

        /// <summary>
        /// Rewrites calls to Application.streamingAssetsPath in a precompiled mod assembly.
        /// </summary>
        /// <returns>The patched image, or image itself if it has no such calls or can't be patched.</returns>
        public static byte[] Patch(byte[] image, string modTitle)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (image == null || IndexOf(image, getterNameBytes) < 0)
                return image;

            try
            {
                ModuleDefMD module = ModuleDefMD.Load(image);
                AssemblyRef gameAssembly = module.GetAssemblyRefs().FirstOrDefault(assembly => assembly.Name == "Assembly-CSharp") ??
                    new AssemblyRefUser("Assembly-CSharp");
                var paths = new TypeRefUser(module, "DaggerfallWorkshop", "Paths", gameAssembly);
                var getter = new MemberRefUser(module, "get_StreamingAssetsPath", MethodSig.CreateStatic(module.CorLibTypes.String), paths);

                int count = 0;
                foreach (TypeDef type in module.GetTypes())
                {
                    foreach (MethodDef method in type.Methods)
                    {
                        if (!method.HasBody)
                            continue;

                        foreach (Instruction instruction in method.Body.Instructions)
                        {
                            var target = instruction.Operand as IMethod;
                            if (instruction.OpCode.Code == Code.Call && target != null && target.Name == GetterName &&
                                target.DeclaringType != null && target.DeclaringType.FullName == "UnityEngine.Application")
                            {
                                instruction.Operand = getter;
                                count++;
                            }
                        }
                    }
                }

                if (count == 0)
                    return image;

                using (var output = new MemoryStream())
                {
                    module.Write(output);
                    Debug.Log($"[{modTitle}] Pointed {count} uses of Application.streamingAssetsPath at the game's StreamingAssets folder");
                    return output.ToArray();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{modTitle}] Couldn't point the mod's uses of Application.streamingAssetsPath at the game's StreamingAssets folder: {ex.Message}");
                return image;
            }
#else
            return image;
#endif
        }

        /// <summary>
        /// Rewrites uses of Application.streamingAssetsPath in mod source.
        /// </summary>
        public static string PatchSource(string source)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (source == null || source.IndexOf("streamingAssetsPath", StringComparison.Ordinal) < 0)
                return source;
            return streamingAssetsPath.Replace(source, Replacement);
#else
            return source;
#endif
        }

        static int IndexOf(byte[] data, byte[] value)
        {
            for (int i = Array.IndexOf(data, value[0]); i >= 0 && i <= data.Length - value.Length; i = Array.IndexOf(data, value[0], i + 1))
            {
                int j = 1;
                while (j < value.Length && data[i + j] == value[j])
                    j++;
                if (j == value.Length)
                    return i;
            }
            return -1;
        }
    }
}
