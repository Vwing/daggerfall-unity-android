using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using DaggerfallWorkshop.Game.Utility.ModSupport;
#if ENABLE_IL2CPP && !UNITY_EDITOR
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
#endif

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Compiles mods' C# source with Roslyn on IL2CPP builds, which can't run the Mono compiler the game otherwise uses.
    /// It compiles against the game's assemblies as IL2CPP's linker left them, which the build puts in Resources, and the
    /// assemblies of mods loaded before. Each mod's assembly is kept in the mods data folder, so a mod's source is only
    /// compiled again when it or the game changes.
    /// </summary>
    public static class ModSourceCompiler
    {
        /// <summary>
        /// The Resources folder with the stripped assemblies that aren't in <see cref="HybridCLRMetadataLoader.ResourcesFolder"/>.
        /// </summary>
        public const string ReferencesResourcesFolder = "ModSourceReferences";

        const string CacheFolder = "CompiledScripts";
        const string AssemblyExtension = ".dll";
        const string FailedExtension = ".failed.txt";
        const int CacheVersion = 1;

        public enum Status
        {
            NotCompiled,
            Compiled,
            Failed,
        }

        // Assemblies of mods loaded so far, which later mods may use
        static readonly List<KeyValuePair<string, byte[]>> modAssemblies = new List<KeyValuePair<string, byte[]>>();

        /// <summary>
        /// True if this build compiles mods' source with this class.
        /// </summary>
        public static bool IsSupported
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            get { return true; }
#else
            get { return false; }
#endif
        }

        /// <summary>
        /// Adds a loaded mod assembly to the references of mods compiled after it.
        /// </summary>
        public static void AddModAssembly(string name, byte[] image)
        {
            if (image != null)
                modAssemblies.Add(new KeyValuePair<string, byte[]>(name, image));
        }

        /// <summary>
        /// Tells whether a mod's source was compiled, or failed to, the last time the game started with it enabled.
        /// </summary>
        public static Status GetStatus(Mod mod, IList<string> sources)
        {
            try
            {
                string directory = GetCacheDirectory(mod);
                if (!Directory.Exists(directory))
                    return Status.NotCompiled;

                FileInfo newest = new DirectoryInfo(directory).GetFiles(GetSourceHash(sources) + ".*")
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (newest == null)
                    return Status.NotCompiled;
                return newest.Name.EndsWith(FailedExtension, StringComparison.Ordinal) ? Status.Failed : Status.Compiled;
            }
            catch (Exception)
            {
                return Status.NotCompiled;
            }
        }

        /// <summary>
        /// Gets a mod's compiled source from the cache, or compiles it.
        /// </summary>
        /// <returns>The assembly's image, or null if the source failed to compile, with the errors logged.</returns>
        public static byte[] GetOrCompile(Mod mod, IList<string> sources)
        {
            string directory = GetCacheDirectory(mod);
            string key = GetSourceHash(sources) + "." + GetReferencesHash();
            string assemblyPath = Path.Combine(directory, key + AssemblyExtension);
            string failedPath = Path.Combine(directory, key + FailedExtension);
            try
            {
                if (File.Exists(assemblyPath))
                {
                    Debug.Log($"[{mod.Title}] Using the scripts compiled when the game last started with this mod");
                    return File.ReadAllBytes(assemblyPath);
                }
                if (File.Exists(failedPath))
                {
                    Debug.LogError($"[{mod.Title}] The mod's scripts failed to compile when the game last started with it:\n{File.ReadAllText(failedPath)}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{mod.Title}] Couldn't read compiled scripts: {ex.Message}");
            }

            string errors;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            byte[] image = Compile(sources.Select(ModAssemblyPatcher.PatchSource).ToList(), $"ModSource.{GetAssemblyNamePart(mod.Title)}.{key.Substring(0, 8)}", out errors);
            if (image != null)
                Debug.Log($"[{mod.Title}] Compiled the mod's scripts in {timer.ElapsedMilliseconds} ms");
            else
                Debug.LogError($"[{mod.Title}] The mod's scripts failed to compile:\n{errors}");

            try
            {
                // Only keep the latest version of a mod's assembly
                if (Directory.Exists(directory))
                {
                    foreach (string file in Directory.GetFiles(directory))
                        File.Delete(file);
                }
                Directory.CreateDirectory(directory);
                if (image != null)
                    File.WriteAllBytes(assemblyPath, image);
                else
                    File.WriteAllText(failedPath, errors);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{mod.Title}] Couldn't save compiled scripts: {ex.Message}");
            }

            return image;
        }

        static string GetCacheDirectory(Mod mod)
        {
            return Path.Combine(Path.Combine(ModManager.Instance.ModDataDirectory, CacheFolder), mod.FileName);
        }

        // Source compiled by another build of the game may not match its assemblies
        static string GetSourceHash(IList<string> sources)
        {
            var text = new StringBuilder();
            text.Append(CacheVersion).Append('\n').Append(Application.version).Append('\n').Append(Application.buildGUID).Append('\n');
            foreach (string source in sources)
                text.Append(source.Length).Append('\n').Append(source);
            return GetHash(Encoding.UTF8.GetBytes(text.ToString())).Substring(0, 16);
        }

        // Enabling or updating the mods a mod uses can fix or break its source
        static string GetReferencesHash()
        {
            var data = new MemoryStream();
            foreach (KeyValuePair<string, byte[]> assembly in modAssemblies)
            {
                byte[] name = Encoding.UTF8.GetBytes(assembly.Key);
                data.Write(name, 0, name.Length);
                data.Write(assembly.Value, 0, assembly.Value.Length);
            }
            return GetHash(data.ToArray()).Substring(0, 8);
        }

        static string GetHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(data).Select(b => b.ToString("x2")));
        }

        static string GetAssemblyNamePart(string title)
        {
            var name = new StringBuilder();
            foreach (char c in title ?? string.Empty)
                name.Append(char.IsLetterOrDigit(c) ? c : '_');
            return name.Length > 0 ? name.ToString() : "Mod";
        }

#if ENABLE_IL2CPP && !UNITY_EDITOR
        static byte[] Compile(IList<string> sources, string assemblyName, out string errors)
        {
            var loadedAssets = new List<TextAsset>();
            try
            {
                var references = new List<MetadataReference>();
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string folder in new[] { HybridCLRMetadataLoader.ResourcesFolder, ReferencesResourcesFolder })
                {
                    foreach (TextAsset asset in Resources.LoadAll<TextAsset>(folder))
                    {
                        loadedAssets.Add(asset);
                        if (names.Add(asset.name))
                            references.Add(MetadataReference.CreateFromImage(asset.bytes));
                    }
                }
                foreach (KeyValuePair<string, byte[]> assembly in modAssemblies)
                {
                    if (names.Add(assembly.Key + ".dll"))
                        references.Add(MetadataReference.CreateFromImage(assembly.Value));
                }

                var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
                var syntaxTrees = sources.Select((source, i) => CSharpSyntaxTree.ParseText(source, parseOptions, i.ToString())).ToList();
                var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release,
                    allowUnsafe: true,
                    concurrentBuild: false);
                CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, syntaxTrees, references, options);

                using (var output = new MemoryStream())
                {
                    var result = compilation.Emit(output);
                    if (result.Success)
                    {
                        errors = null;
                        return output.ToArray();
                    }

                    errors = string.Join("\n", result.Diagnostics
                        .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                        .Take(50)
                        .Select(diagnostic => diagnostic.ToString()));
                    return null;
                }
            }
            catch (Exception ex)
            {
                errors = ex.ToString();
                return null;
            }
            finally
            {
                foreach (TextAsset asset in loadedAssets)
                    Resources.UnloadAsset(asset);
            }
        }
#else
        static byte[] Compile(IList<string> sources, string assemblyName, out string errors)
        {
            errors = "This build of the game compiles mods' source with the Mono compiler";
            return null;
        }
#endif
    }
}
