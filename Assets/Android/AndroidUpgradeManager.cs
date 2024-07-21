using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
            yield return new WaitForSeconds(1);
#if UNITY_ANDROID
            // check if last installed version is different from current version on Player Settings
            if (LastInstalledVersion != VersionInfo.DaggerfallUnityForAndroidVersion)
            {
                UpdateSettings();
                LastInstalledVersion = VersionInfo.DaggerfallUnityForAndroidVersion;
            }
#endif
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

        [ContextMenu("Reset Last Used Version")]
        public void ResetLastUsedVersion()
        {
            PlayerPrefs.DeleteKey("LastInstalledVersion");
            PlayerPrefs.Save();
            Debug.Log("Last Used Version has been reset to 0.0.0.0");
        }
    }
}