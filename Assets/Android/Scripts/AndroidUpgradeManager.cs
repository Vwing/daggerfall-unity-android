using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

namespace DaggerfallWorkshop.Game
{
    /// <summary>
    /// This class handles changing settings when a user upgrades from one version of the game to another.
    /// </summary>
    /// <remarks>
    /// This is necessary because DFU uses text assets for settings, and on Android we're not expecting
    /// users to have to drag those assets in every time they upgrade.
    /// </remarks>
    public class AndroidUpgradeManager : MonoBehaviour
    {
        public string LastInstalledVersion { get { return PlayerPrefs.GetString("LastInstalledVersion", "0.0.0.0"); } private set { PlayerPrefs.SetString("LastInstalledVersion", value); } }
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1);
            // check if last installed version is different from current version on Player Settings
            if (LastInstalledVersion != VersionInfo.DaggerfallUnityForAndroidVersion)
            {
                UpdateSettings();
                LastInstalledVersion = VersionInfo.DaggerfallUnityForAndroidVersion;
            }
            // check if initial-default-layout.json is outdated
            string initialDefaultLayoutPath = Path.Combine(Paths.PersistentDataPath, "initial-default-layout.json");
            var layoutConfig = File.Exists(initialDefaultLayoutPath) ? TouchscreenLayoutConfiguration.ReadFromPath(initialDefaultLayoutPath) : null;
            if (layoutConfig == null || layoutConfig.buttons == null || layoutConfig.buttons.Count == 0 
                || layoutConfig.buttons[0].Version < TouchscreenButtonConfiguration.LatestVersion)
            {
                Debug.Log("AndroidUpgradeManager: Migrating touchscreen layout due to initial-default-layout.json");
                MigrateDefaultLayout();
            }
        }

        private void UpdateSettings()
        {
            var versionSplit = LastInstalledVersion.Split('.');
            int first = int.Parse(versionSplit[0]);
            int second = int.Parse(versionSplit[1]);
            int third = int.Parse(versionSplit[2]);
            int fourth = int.Parse(versionSplit[3]);
            bool isRC = versionSplit.Length > 4;
            Debug.Log($"AndroidUpgradeManager: {first} {second} {third} {fourth} {isRC}");
            
            if(first < 1 || (first == 1 && second < 1) || (first == 1 && second == 1 && third < 1) || (first == 1 && second == 1 && third == 1 && fourth < 7))
            {
                Debug.Log("AndroidUpgradeManager: Migrating touchscreen layout due to version");
                MigrateDefaultLayout();
            }
            
            if (first <= 1 && second <= 1 && third <= 1 && fourth < 3)
            {
                Debug.Log("AndroidUpgradeManager: Updating settings");
                // reset key bindings for AutoRun and ActivateCenterObject to default values if they're using the stupid old values
                if (InputManager.Instance.GetBinding(InputManager.Actions.AutoRun) == KeyCode.F10)
                {
                    Debug.Log("AndroidUpgradeManager: Resetting AutoRun");
                    var defaultAutorun = InputManager.Instance.GetDefaultBinding(InputManager.Actions.AutoRun);
                    if (InputManager.Instance.GetActionBoundToKeycode(defaultAutorun) == InputManager.Actions.Unknown)
                        InputManager.Instance.SetBinding(defaultAutorun, InputManager.Actions.AutoRun);
                }
                if (InputManager.Instance.GetBinding(InputManager.Actions.ActivateCenterObject) == KeyCode.Mouse0)
                {
                    Debug.Log("AndroidUpgradeManager: Resetting ActivateCenterObject");
                    var defaultActivate = InputManager.Instance.GetDefaultBinding(InputManager.Actions.ActivateCenterObject);
                    if (InputManager.Instance.GetActionBoundToKeycode(defaultActivate) == InputManager.Actions.Unknown)
                        InputManager.Instance.SetBinding(defaultActivate, InputManager.Actions.ActivateCenterObject);
                }
            }
        }

        private void MigrateDefaultLayout()
        {
            string layoutsPath = TouchscreenLayoutsManager.LayoutsPath;
            string defaultLayoutPath = Path.Combine(layoutsPath, "default-layout");
            // Find the next available my-layoutN directory
            int layoutIndex = 1;
            string myLayoutNPath;
            do
            {
                myLayoutNPath = Path.Combine(layoutsPath, $"my-layout{layoutIndex}");
                layoutIndex++;
            } while (Directory.Exists(myLayoutNPath));
            // Check if the user has a customized default-layout
            if (Directory.Exists(defaultLayoutPath))
            {
                try
                {
                    // Create the new directory for the migrated layout
                    Debug.Log($"AndroidUpgradeManager: Migrating default-layout to {Path.GetFileName(myLayoutNPath)}");
                    Directory.CreateDirectory(myLayoutNPath);

                    // Copy all files and subdirectories from default-layout to my-layoutN
                    foreach (string dirPath in Directory.GetDirectories(defaultLayoutPath, "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dirPath.Replace(defaultLayoutPath, myLayoutNPath));
                    }
                    foreach (string filePath in Directory.GetFiles(defaultLayoutPath, "*.*", SearchOption.AllDirectories))
                    {
                        string newFilePath = filePath.Replace(defaultLayoutPath, myLayoutNPath);
                        File.Copy(filePath, newFilePath, true);
                    }

                    // Update the JSON file name and contents
                    string oldJsonPath = Path.Combine(myLayoutNPath, "default-layout.json");
                    string newJsonPath = Path.Combine(myLayoutNPath, $"my-layout{layoutIndex - 1}.json");
                    if (File.Exists(oldJsonPath))
                    {
                        // Read the layout configuration and update its name
                        var layoutConfig = TouchscreenLayoutConfiguration.ReadFromPath(oldJsonPath);
                        if (layoutConfig != null)
                        {
                            layoutConfig.name = $"my-layout{layoutIndex - 1}";
                            TouchscreenLayoutConfiguration.WriteToPath(layoutConfig, newJsonPath);
                            File.Delete(oldJsonPath);

                            // Set this as the selected layout and load it
                            TouchscreenLayoutsManager.LastSelectedLayout = $"my-layout{layoutIndex - 1}";
                            TouchscreenLayoutsManager.Instance.LoadLastSelectedOrDefaultLayout();
                            Debug.Log($"AndroidUpgradeManager: Successfully migrated default-layout to my-layout{layoutIndex - 1}");
                        }
                        else
                        {
                            Debug.LogWarning("AndroidUpgradeManager: Could not read layout configuration during migration");
                        }
                    }

                    // Regenerate the default layout so the user still has a default-layout
                    TouchscreenLayoutsManager.Instance.RegenerateBrokenDefaultLayout("default-layout", false);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"AndroidUpgradeManager: Error migrating layout: {e.Message}");
                    // If migration fails, make sure we don't leave things in a broken state
                    if (Directory.Exists(myLayoutNPath))
                    {
                        try
                        {
                            Directory.Delete(myLayoutNPath, true);
                        }
                        catch (System.Exception rollbackError)
                        {
                            Debug.LogError($"AndroidUpgradeManager: Error rolling back migration: {rollbackError.Message}");
                        }
                    }
                }
            }
        }

        [ContextMenu("Reset Last Used Version")]
        public void ResetLastUsedVersion()
        {
            PlayerPrefs.DeleteKey("LastInstalledVersion");
            PlayerPrefs.Save();
            Debug.Log("Last Used Version has been reset to 0.0.0.0");
        }
    }
}