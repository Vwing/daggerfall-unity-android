// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors: Vivian V Wing (vwing@multitude.city)
//
// Notes:
//

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using DaggerfallWorkshop.Utility;
using System.Collections;


namespace DaggerfallWorkshop.Game.UserInterface
{
    /// <summary>
    /// Simple cross-platform folder browser.
    /// </summary>
    public class FolderBrowserAndroid : Panel
    {
        private const string daggerfallDataDirName = "Daggerfall";

        int maxChars = 43;
        string importDataText = "Import Daggerfall Game Data";

        int minWidth = 200;
        int minHeight = 100;
        TextLabel pathLabel = new TextLabel();
        Button importDataButton = new Button();

        Color importDataButtonColor = new Color(0.0f, 0.5f, 0.0f, 0.4f);

        string currentPath;
        bool confirmEnabled = true;
        int importDataButtonWasClicked = 0; // hacky way to delay the native file picker so that the 'loading data...' text is drawn

        // Progress bar UI fields
        private Panel progressBarBg;
        private Panel progressBarFill;
        private TextLabel progressText;

        public delegate void OnConfirmPathHandler();
        public event OnConfirmPathHandler OnConfirmPath;

        public delegate void OnPathChangedHandler();
        public event OnPathChangedHandler OnPathChanged;

        #region Properties

        /// <summary>
        /// Maximum right-most characters to display in path label.
        /// </summary>
        public int MaxPathLabelChars
        {
            get { return maxChars; }
            set { maxChars = value; }
        }

        /// <summary>
        /// Gets current path selected by browser.
        /// </summary>
        public string CurrentPath
        {
            get { return currentPath; }
        }

        /// <summary>
        /// Enable or disable confirm button, e.g. based on path validation.
        /// </summary>
        public bool ConfirmEnabled
        {
            get { return confirmEnabled; }
            set { confirmEnabled = value; }
        }

        #endregion

        #region Constructors

        public FolderBrowserAndroid()
        {
            Setup();
        }

        #endregion

        #region Overrides

        public override void Update()
        {
            base.Update();

            if (importDataButtonWasClicked > 0)
            {
                importDataButtonWasClicked--;
                if(importDataButtonWasClicked == 0)
                    PickArena2Folder();
            }
        }

        #endregion

        #region Private Methods

        void Setup()
        {
            // Setup panels
            Components.Add(importDataButton);
            Components.Add(pathLabel);


            // Enforce minimum size
            Vector2 size = Size;
            if (size.x < minWidth) size.x = minWidth;
            if (size.y < minHeight) size.y = minHeight;
            Size = size;

            // Set path label
            pathLabel.Position = new Vector2(2, 2);
            pathLabel.VerticalAlignment = VerticalAlignment.Middle;
            pathLabel.HorizontalAlignment = HorizontalAlignment.Center;
            pathLabel.ShadowPosition = Vector2.zero;
            pathLabel.MaxWidth = (int)Size.x - 4;

            // set import data buttons
            importDataButton.Position = new Vector2(100, 75);
            importDataButton.Size = new Vector2(100, 12);
            importDataButton.Outline.Enabled = true;
            importDataButton.Label.Text = importDataText;
            importDataButton.HorizontalAlignment = HorizontalAlignment.Center;
            importDataButton.BackgroundColor = importDataButtonColor;

            // Setup events
            importDataButton.OnMouseClick += ImportDataButton_OnMouseClick;

            // Initialize progress bar UI (hidden initially)
            progressBarBg = new Panel();
            progressBarBg.Size = new Vector2(Size.x - 20, 10);
            progressBarBg.Position = new Vector2(Size.x - progressBarBg.Size.x + 15, Size.y - 40);
            progressBarBg.BackgroundColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            progressBarBg.Outline.Enabled = true;
            progressBarBg.Enabled = false;
            Components.Add(progressBarBg);

            progressBarFill = new Panel();
            progressBarFill.Position = progressBarBg.Position + new Vector2(1, 1);
            progressBarFill.Size = new Vector2(0, progressBarBg.Size.y - 2);
            progressBarFill.BackgroundColor = Color.green;
            progressBarFill.Enabled = false;
            Components.Add(progressBarFill);

            progressText = new TextLabel();
            progressText.TextColor = Color.black;
            progressText.Position = new Vector2(Size.x / 2 - 50, Size.y - 38);
            progressText.Size = new Vector2(100, 10);
            progressText.HorizontalAlignment = HorizontalAlignment.Center;
            progressText.Enabled = false;
            Components.Add(progressText);
        }

        private void PickArena2Folder()
        {
            NativeFilePicker.FilePickedCallback filePickedCallback = new NativeFilePicker.FilePickedCallback(OnFilePicked);
            NativeFilePicker.PickFile(filePickedCallback, "application/zip"); // , ".zip"
        }

        private bool ValidateArena2Path(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string pathResult = DaggerfallUnity.TestArena2Exists(path);
            DaggerfallConnect.Utility.DFValidator.ValidationResults validationResults;
            DaggerfallConnect.Utility.DFValidator.ValidateArena2Folder(pathResult, out validationResults, true);
            return validationResults.AppearsValid;
        }

        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath);
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        private void OnFilePicked(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !filePath.ToLower().EndsWith(".zip"))
            {
                SetPathLabelText("");
                return;
            }

            // begin asynchronous import with progress
            ShowProgressBar();
            CoroutineManager.Instance.StartCoroutine(ImportArena2Coroutine(filePath));
        }

        private IEnumerator ImportArena2Coroutine(string filePath)
        {
            string outputPath = Path.Combine(Paths.PersistentDataPath, daggerfallDataDirName);
            string cachePath = Path.Combine(Application.temporaryCachePath, "DaggerfallArena2Unzipped");
            string sourcePath = null;
            string destinationPath = null;
            DaggerfallConnect.Utility.DFValidator.ValidationResults validationResults;
            try
            {
                // unzip phase
                SetProgress(0f, "Unzipping...");
                yield return null;
                yield return CoroutineManager.Instance.StartCoroutine(ZipFileUtils.UnzipFileAsync(filePath, cachePath,
                    (progress) =>
                    {
                        SetProgress(progress * 25f, "Unzipping...");
                    }));

                // search phase
                SetProgress(25f, "Searching data...");
                yield return null;
                SetProgress(25f, "Expanding data...");
                if (!TryGetValidImportSource(cachePath, outputPath, out sourcePath, out destinationPath, out validationResults))
                {
                    SetPathLabelText(GetValidationFailureText(validationResults), Color.red);
                    HideProgressBar();
                    yield break;
                }

                // delete output path if it exists
                if (Directory.Exists(outputPath))
                {
                    Directory.Delete(outputPath, true);
                }

                // copy phase
                var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                int total = allFiles.Length;
                for (int i = 0; i < total; i++)
                {
                    var src = allFiles[i];
                    var rel = src.Substring(sourcePath.Length + 1);
                    var dst = Path.Combine(destinationPath, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);

                    float pct = total > 0 ? 25f + 75f * (i + 1) / total : 100f;
                    SetProgress(pct, "Copying data...");
                    if (i % 10 == 0)
                        yield return null;
                }

                // finalize
                SetProgress(100f, "Finalizing...");
                yield return new WaitForSecondsRealtime(0.2f);

                // validate and finish
                if (ValidateArena2Path(outputPath))
                {
                    currentPath = outputPath;
                    pathLabel.Text = filePath;
                    confirmEnabled = true;
                    RaisePathChangedEvent();
                    RaiseOnConfirmPathEvent();
                }
                else
                {
                    SetPathLabelText("Validation failed", Color.red);
                    confirmEnabled = false;
                }
            }
            finally
            {
                if (Directory.Exists(cachePath))
                {
                    Directory.Delete(cachePath, true);
                }
                HideProgressBar();
            }
        }

        private bool TryGetValidImportSource(
            string cachePath,
            string outputPath,
            out string sourcePath,
            out string destinationPath,
            out DaggerfallConnect.Utility.DFValidator.ValidationResults bestValidationResults)
        {
            sourcePath = null;
            destinationPath = null;
            bestValidationResults = new DaggerfallConnect.Utility.DFValidator.ValidationResults();
            int bestScore = -1;

            foreach (var arena2Path in GetArena2Candidates(cachePath))
            {
                TryUnpackPackedDat(arena2Path);

                DaggerfallConnect.Utility.DFValidator.ValidationResults validationResults;
                DaggerfallConnect.Utility.DFValidator.ValidateArena2Folder(arena2Path, out validationResults, true);

                int score = GetValidationScore(validationResults);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestValidationResults = validationResults;
                }

                if (!validationResults.AppearsValid)
                    continue;

                sourcePath = GetImportSourcePath(cachePath, arena2Path);
                destinationPath = string.Equals(sourcePath, arena2Path, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(outputPath, "arena2")
                    : outputPath;
                return true;
            }

            return false;
        }

        private void TryUnpackPackedDat(string arena2Path)
        {
            string packedDatPath = GetFileIgnoreCase(arena2Path, "PACKED.DAT");
            if (string.IsNullOrEmpty(packedDatPath))
                return;

            bool arch3dExists = !string.IsNullOrEmpty(GetFileIgnoreCase(arena2Path, "ARCH3D.BSA"));
            bool daggerSndExists = !string.IsNullOrEmpty(GetFileIgnoreCase(arena2Path, "DAGGER.SND"));
            if (arch3dExists && daggerSndExists)
                return;

            DirectoryInfo arena2Parent = Directory.GetParent(arena2Path);
            string outputPath = arena2Parent != null ? arena2Parent.FullName : arena2Path;

            try
            {
                PackedDatFileUtils.UnpackFile(packedDatPath, outputPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to unpack PACKED.DAT: " + ex.Message);
            }
        }

        private string GetFileIgnoreCase(string path, string filename)
        {
            if (!Directory.Exists(path))
                return null;

            foreach (var file in Directory.GetFiles(path))
            {
                if (string.Equals(Path.GetFileName(file), filename, StringComparison.OrdinalIgnoreCase))
                    return file;
            }

            return null;
        }

        private IEnumerable<string> GetArena2Candidates(string cachePath)
        {
            if (Directory.Exists(cachePath))
            {
                yield return cachePath;

                foreach (var dir in Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories))
                {
                    if (string.Equals(Path.GetFileName(dir), "arena2", StringComparison.OrdinalIgnoreCase))
                        yield return dir;
                }
            }
        }

        private string GetImportSourcePath(string cachePath, string arena2Path)
        {
            if (string.Equals(cachePath, arena2Path, StringComparison.OrdinalIgnoreCase))
                return arena2Path;

            DirectoryInfo parent = Directory.GetParent(arena2Path);
            if (parent == null || string.Equals(parent.FullName, cachePath, StringComparison.OrdinalIgnoreCase))
                return cachePath;

            return parent.FullName;
        }

        private int GetValidationScore(DaggerfallConnect.Utility.DFValidator.ValidationResults validationResults)
        {
            int score = 0;
            if (validationResults.FolderValid) score++;
            if (validationResults.TexturesValid) score++;
            if (validationResults.ModelsValid) score++;
            if (validationResults.BlocksValid) score++;
            if (validationResults.MapsValid) score++;
            if (validationResults.SoundsValid) score++;
            if (validationResults.WoodsValid) score++;
            if (validationResults.VideosValid) score++;
            return score;
        }

        private string GetValidationFailureText(DaggerfallConnect.Utility.DFValidator.ValidationResults validationResults)
        {
            if (string.IsNullOrEmpty(validationResults.PathTested))
                return "Archive did not contain an arena2 folder";

            List<string> missing = new List<string>();
            if (!validationResults.TexturesValid) missing.Add("textures");
            if (!validationResults.ModelsValid) missing.Add("ARCH3D.BSA");
            if (!validationResults.BlocksValid) missing.Add("BLOCKS.BSA");
            if (!validationResults.MapsValid) missing.Add("MAPS.BSA");
            if (!validationResults.SoundsValid) missing.Add("DAGGER.SND");
            if (!validationResults.WoodsValid) missing.Add("WOODS.WLD");
            if (!validationResults.VideosValid) missing.Add("videos");

            if (missing.Count == 0)
                return "Archive did not contain a valid Daggerfall folder";

            return "Archive is missing " + string.Join(", ", missing.ToArray());
        }

        private void ShowProgressBar()
        {
            progressBarBg.Enabled = true;
            progressBarFill.Enabled = true;
            progressText.Enabled = true;
        }

        private void HideProgressBar()
        {
            progressBarBg.Enabled = false;
            progressBarFill.Enabled = false;
            progressText.Enabled = false;
        }

        private void SetProgress(float percent, string status)
        {
            float width = (progressBarBg.Size.x - 2) * Mathf.Clamp01(percent / 100f);
            progressBarFill.Size = new Vector2(width, progressBarBg.Size.y - 2);
            progressText.Text = string.Format("{0} {1:0}%", status, percent);
        }

        private void ImportDataButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            SetPathLabelText("Loading data...");
            importDataButtonWasClicked = 4;
        }

        void SetPathLabelText(string txt) => SetPathLabelText(txt, DaggerfallUI.DaggerfallDefaultTextColor);
        void SetPathLabelText(string txt, Color color)
        {
            pathLabel.Text = txt;
            pathLabel.TextColor = color;
        }

        void RaiseOnConfirmPathEvent()
        {
            if (OnConfirmPath != null)
                OnConfirmPath();
        }

        void RaisePathChangedEvent()
        {
            if (OnPathChanged != null)
                OnPathChanged();
        }

        #endregion
    }
}
