using DaggerfallWorkshop;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop.Game
{

    /// <summary>
    /// Handles Android's Screen stuff like resolution setting and orientation
    /// </summary>
    public class AndroidScreenManager : MonoBehaviour
    {
        public static event System.Action<Resolution> ScreenResolutionChanged;
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
        // private void OnGUI()
        // {
        //     if (GUI.Button(new Rect(10, 70, 50, 30), "Any"))
        //         SetOrientationToAny();
        //     if (GUI.Button(new Rect(10, 170, 50, 30), "Landscape"))
        //         SetOrientationToLandscape();
        //     if (GUI.Button(new Rect(10, 270, 50, 30), "Portrait"))
        //         SetOrientationToPortrait();
        // }
        private void Update()
        {
            int x = Screen.width;
            int y = Screen.height;
            if (x != lastResolution.width || y != lastResolution.height){
                // looks like the resolution changed. Let's update the daggerfall unity resolution
                SetResolution(x, y);
                lastResolution = new Resolution() { width = x, height = y };
            }
        }
        public static void SetResolution(int x, int y)
        {
            Debug.Log("AndroidSimulationManager: Current screen updated to new resolution");
            DaggerfallUnity.Settings.ResolutionWidth = x;
            DaggerfallUnity.Settings.ResolutionHeight = y;

            SettingsManager.SetScreenResolution(x, y, true);
            var allCams = FindObjectsOfType<Camera>();
            foreach (var cam in allCams)
                cam.ResetAspect();
            if(TouchscreenInputManager.Instance)
                TouchscreenInputManager.Instance.SetupUIRenderTexture();

            ScreenResolutionChanged?.Invoke(new Resolution(){width=x, height=y});
        }
        public static void SetOrientationToAny()
        {
            Debug.Log("AndroidScreenManager: Setting oritentation to Any");
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
        public static void SetOrientationToPortrait()
        {
            Debug.Log("AndroidScreenManager: Setting oritentation to Portrait");
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = true;
            Screen.orientation = ScreenOrientation.AutoRotation;
        }
        public static void SetOrientationToLandscape()
        {
            Debug.Log("AndroidScreenManager: Setting oritentation to Landscape");
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.orientation = ScreenOrientation.AutoRotation;
            // DeviceOrientationManager.ForceOrientation(ScreenOrientation.AutoRotation);
        }
    }
}