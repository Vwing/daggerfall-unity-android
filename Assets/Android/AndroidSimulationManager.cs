using DaggerfallWorkshop;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop.Game
{

    /// <summary>
    /// Handles things to make the Editor experience better when using the Android simulator
    /// </summary>
    public class AndroidSimulationManager : MonoBehaviour
    {
#if UNITY_ANDROID && UNITY_EDITOR

        private Resolution lastResolution;

        private void Awake()
        {
            // Set resolution to the current resolution, just so that it's not using a stale setting from when
            // another Simulator was being used.
            if (AndroidUtils.IsRunningInSimulator)
            {
                DaggerfallUnity.Settings.ResolutionWidth = Screen.currentResolution.width;
                DaggerfallUnity.Settings.ResolutionHeight = Screen.currentResolution.height;
                lastResolution = Screen.currentResolution;
            }
            else
            {
                DaggerfallUnity.Settings.ResolutionWidth = Screen.width;
                DaggerfallUnity.Settings.ResolutionHeight = Screen.height;
                lastResolution = new Resolution() { width = Screen.width, height = Screen.height };
            }
        }
        private void Update()
        {
            if (Screen.width != lastResolution.width || Screen.height != lastResolution.height)
            {
                // looks like we changed simulator devices. Let's update the daggerfall unity resolution
                Debug.Log("AndroidSimulationManager: Current screen updated to new resolution");
                DaggerfallUnity.Settings.ResolutionWidth = Screen.width;
                DaggerfallUnity.Settings.ResolutionHeight = Screen.height;

                SettingsManager.SetScreenResolution(Screen.width, Screen.height, true);
                var allCams = FindObjectsOfType<Camera>();
                foreach (var cam in allCams)
                    cam.ResetAspect();
                TouchscreenInputManager.Instance.SetupUIRenderTexture();

                lastResolution = new Resolution() { width = Screen.width, height = Screen.height };
            }
        }
#endif
    }
}