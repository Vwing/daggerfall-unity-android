// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors: Vincent Wing (vwing@uci.edu)
//
// Notes:
//

using UnityEngine;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using DaggerfallWorkshop.Utility;
using System.Linq;


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
        }

        private void PickArena2Folder()
        {
            NativeFilePicker.FilePickedCallback filePickedCallback = new NativeFilePicker.FilePickedCallback(OnFilePicked);
            NativeFilePicker.PickFile(filePickedCallback);
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

            // extract zip and load the files
            string outputPath = Path.Combine(Paths.PersistentDataPath, daggerfallDataDirName);
            string cachePath = Path.Combine(Application.temporaryCachePath, "DaggerfallArena2Unzipped");
            Unzip.UnzipFile(filePath, cachePath);
            bool foundValidFolder = false;
            IEnumerable<string> dirs = Directory.EnumerateDirectories(cachePath, "*", SearchOption.AllDirectories);
            dirs = dirs.Prepend(cachePath);
            foreach (string subdirectory in dirs)
            {
                Debug.Log("Checking " + subdirectory);
                if (!string.IsNullOrEmpty(DaggerfallUnity.TestArena2Exists(subdirectory)))
                {
                    if (Directory.Exists(outputPath))
                        Directory.Delete(outputPath, true);
                    CopyDirectory(subdirectory, outputPath, true);
                    Directory.Delete(cachePath, true);
                    foundValidFolder = true;
                    break;
                }
            }

            // validate path. looks good? update path text and enable confirm button
            if (foundValidFolder && ValidateArena2Path(outputPath))
            {
                currentPath = outputPath;
                pathLabel.Text = filePath;
                confirmEnabled = true;

                // Android likes to turn the screen black while these file operations
                // are happening, so let's just spare people the trouble of pressing 'okay'
                RaisePathChangedEvent();
                RaiseOnConfirmPathEvent();
            }
            else
            {
                SetPathLabelText("Archive did not contain a valid Daggerfall folder", Color.red);
                confirmEnabled = false;
            }
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

        #region Event Handlers


        private void ImportDataButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            SetPathLabelText("Loading data...");
            importDataButtonWasClicked = 4;
        }

        #endregion
    }
}
