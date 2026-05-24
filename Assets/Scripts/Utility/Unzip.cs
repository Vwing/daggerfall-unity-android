// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vivian V Wing (vwing@multitude.city)
// Contributors:
// 
// Notes:
//

using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using ICSharpCode.SharpZipLib.Zip;


namespace DaggerfallWorkshop.Utility
{
    /// <summary>
    /// Utility class for unzipping zip files
    /// </summary>
    public static class ZipFileUtils
    {
        // Call this method with the path to the zip file and the output folder.
        public static string UnzipFile(string zipFilePath, string outputFolderPath)
        {
            // Ensure the output directory exists
            if (!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            // Initialize the Zip input stream
            using (FileStream fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read))
            using (ZipInputStream zipInputStream = new ZipInputStream(fileStream))
            {
                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    // Create directory for the entry if it does not exist
                    string directoryName = Path.GetDirectoryName(entry.Name);
                    string fileName = Path.GetFileName(entry.Name);
                    string directoryPath = Path.Combine(outputFolderPath, directoryName);

                    if (!Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);

                    // If the entry is a file, extract it
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string filePath = Path.Combine(directoryPath, fileName);
                        using (FileStream streamWriter = File.Create(filePath))
                        {
                            int size = 2048;
                            byte[] buffer = new byte[size];

                            try
                            {
                                while ((size = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    streamWriter.Write(buffer, 0, size);
                                }
                            }
                            catch
                            {
                                Debug.LogWarning("Couldn't extract " + filePath + " from zip archive");
                            }
                        }
                    }
                }
            }

            return outputFolderPath;
        }

        /// <summary>
        /// Asynchronously unzips a zip file, reporting progress via callback. Use with StartCoroutine.
        /// </summary>
        /// <param name="zipFilePath">Path to the zip file.</param>
        /// <param name="outputFolderPath">Output directory for extracted files.</param>
        /// <param name="progress">Callback reporting progress from 0 to 1.</param>
        /// <returns>IEnumerator for coroutine.</returns>
        public static System.Collections.IEnumerator UnzipFileAsync(string zipFilePath, string outputFolderPath, Action<float> progress = null)
        {
            if (!Directory.Exists(outputFolderPath))
                Directory.CreateDirectory(outputFolderPath);

            // First, count total entries for progress
            int totalEntries = 0;
            using (FileStream fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read))
            using (ZipInputStream zipInputStream = new ZipInputStream(fileStream))
            {
                while (zipInputStream.GetNextEntry() != null)
                    totalEntries++;
            }
            if (totalEntries == 0)
            {
                progress?.Invoke(1f);
                yield break;
            }

            int currentEntry = 0;
            using (FileStream fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read))
            using (ZipInputStream zipInputStream = new ZipInputStream(fileStream))
            {
                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    string directoryName = Path.GetDirectoryName(entry.Name);
                    string fileName = Path.GetFileName(entry.Name);
                    string directoryPath = Path.Combine(outputFolderPath, directoryName);
                    if (!Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string filePath = Path.Combine(directoryPath, fileName);
                        using (FileStream streamWriter = File.Create(filePath))
                        {
                            int size = 2048;
                            byte[] buffer = new byte[size];
                            try
                            {
                                while ((size = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    streamWriter.Write(buffer, 0, size);
                                }
                            }
                            catch
                            {
                                Debug.LogWarning("Couldn't extract " + filePath + " from zip archive");
                            }
                        }
                    }
                    currentEntry++;
                    progress?.Invoke((float)currentEntry / totalEntries);
                    if (currentEntry % 5 == 0) // yield every few files for smoother UI
                        yield return null;
                }
            }
            progress?.Invoke(1f);
        }

        /// <summary>
        /// Zips the specified file or directory to a given zip file path.
        /// </summary>
        /// <param name="inputPath">The file or directory to zip.</param>
        /// <param name="zipFilePath">The path to the output zip file.</param>
        /// <param name="compressionLevel">0 - store only to 9 - means best compression</param>
        public static string ZipFile(string inputPath, string zipFilePath, int compressionLevel = 0)
        {
            using (FileStream fsOut = File.Create(zipFilePath))
            using (ZipOutputStream zipStream = new ZipOutputStream(fsOut))
            {
                zipStream.SetLevel(compressionLevel);

                int folderOffset = inputPath.Length + (inputPath.EndsWith("\\") || inputPath.EndsWith("/") ? 0 : 1);

                CompressFolder(inputPath, zipStream, folderOffset);

                zipStream.IsStreamOwner = true; // Makes the Close also Close the underlying stream
                zipStream.Close();
            }
            return zipFilePath;
        }

        /// <summary>
        /// Recursively compresses a folder.
        /// </summary>
        /// <param name="path">The path to compress.</param>
        /// <param name="zipStream">The zip output stream.</param>
        /// <param name="folderOffset">The folder offset.</param>
        private static void CompressFolder(string path, ZipOutputStream zipStream, int folderOffset)
        {
            string[] files = Directory.GetFiles(path);
            string[] folders = Directory.GetDirectories(path);

            foreach (string filename in files)
            {
                FileInfo fi = new FileInfo(filename);

                string entryName = filename.Substring(folderOffset); // Makes the name in zip based on the folder
                entryName = ZipEntry.CleanName(entryName); // Removes drive from name and fixes slash direction
                ZipEntry newEntry = new ZipEntry(entryName)
                {
                    DateTime = fi.LastWriteTime, // Note the zip format stores 2 second granularity
                    Size = fi.Length
                };

                zipStream.PutNextEntry(newEntry);

                // Zip the file in buffered chunks
                byte[] buffer = new byte[4096];
                using (FileStream streamReader = File.OpenRead(filename))
                {
                    ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(streamReader, zipStream, buffer);
                }
                zipStream.CloseEntry();
            }

            foreach (string folder in folders)
            {
                CompressFolder(folder, zipStream, folderOffset);
            }
        }
    }
}