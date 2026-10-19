using System;
using System.IO;
using UnityEngine;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Records that a mod file was converted from another platform by <see cref="DesktopModConverter"/>, so the mod manager
    /// can tell the player. It's kept as JSON beside the mod file, named "[mod].dfmod.converted.json".
    /// </summary>
    [Serializable]
    public class ConvertedModInfo
    {
        const string FileSuffix = ".converted.json";

        public int originalPlatform;
        public bool hasShaders;
        public long fileSize;       // size of the converted mod file, so a replacement isn't mistaken for it

        /// <summary>
        /// True where mods built for other platforms are converted: Android players, which Unity won't let load them.
        /// </summary>
        public static bool ConversionSupported
        {
            get { return Application.platform == RuntimePlatform.Android; }
        }

        public string OriginalPlatformName
        {
            get { return DesktopModConverter.GetPlatformName(originalPlatform); }
        }

        /// <summary>
        /// Gets the conversion record for a mod file, or null if it wasn't converted.
        /// </summary>
        public static ConvertedModInfo Load(string modFilePath)
        {
            string path = modFilePath + FileSuffix;
            try
            {
                if (!File.Exists(path))
                    return null;

                ConvertedModInfo info = JsonUtility.FromJson<ConvertedModInfo>(File.ReadAllText(path));
                if (info != null && File.Exists(modFilePath) && new FileInfo(modFilePath).Length == info.fileSize)
                    return info;

                // The mod file was replaced since it was converted
                File.Delete(path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to read {path}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Records that a mod file was converted, or deletes any old record if result is null.
        /// </summary>
        public static void Save(string modFilePath, DesktopModConverter.Result result)
        {
            if (result == null)
            {
                Delete(modFilePath);
                return;
            }

            var info = new ConvertedModInfo()
            {
                originalPlatform = result.OriginalPlatform,
                hasShaders = result.HasShaders,
                fileSize = new FileInfo(modFilePath).Length,
            };
            File.WriteAllText(modFilePath + FileSuffix, JsonUtility.ToJson(info, true));
        }

        public static void Delete(string modFilePath)
        {
            string path = modFilePath + FileSuffix;
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>
        /// Converts a mod file in the mods folder in place if it was built for another platform,
        /// for mods copied there without the importer.
        /// </summary>
        /// <returns>True if the file was converted.</returns>
        public static bool TryConvertInPlace(string modFilePath)
        {
            if (!ConversionSupported)
                return false;

            try
            {
                DesktopModConverter.Result result = DesktopModConverter.Convert(modFilePath, modFilePath);
                if (result == null)
                    return false;

                Save(modFilePath, result);
                Debug.LogWarning($"Converted {Path.GetFileName(modFilePath)} from {DesktopModConverter.GetPlatformName(result.OriginalPlatform)} to Android (shaders: {result.HasShaders})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to convert {modFilePath} for Android: {ex.Message}");
                return false;
            }
        }
    }
}
