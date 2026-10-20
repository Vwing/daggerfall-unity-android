using DaggerfallWorkshop;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop.Game
{
    public static class AScreen
    {
        /// <summary>
        /// Platform-independent screen width
        /// </summary>
        public static int width
        {
            get { return Application.isMobilePlatform || AndroidUtils.IsRunningInSimulator ? Screen.currentResolution.width : Screen.width; }
        }
        /// <summary>
        /// Platform-independent screen height
        /// </summary>
        public static int height
        {
            get { return Application.isMobilePlatform || AndroidUtils.IsRunningInSimulator ? Screen.currentResolution.height : Screen.height; }
        }
    }

    /// <summary>
    /// Handles Android's Screen stuff like resolution setting and orientation
    /// </summary>
    public class AndroidScreenManager : MonoBehaviour
    {
        public static event System.Action<Resolution> ScreenResolutionChanged;
        private Resolution lastResolution;

        private bool isSetup = false;
        private Material opaqueAlphaMaterial;

        /// <summary>
        /// Meta's headsets blend an app's panel by the alpha it renders, where phones show the app opaque. Anything
        /// that leaves alpha below 1 there, like the skybox of Dynamic Skies, shows the black behind the panel.
        /// </summary>
        private static bool CompositorUsesAlpha
        {
            get { return Application.platform == RuntimePlatform.Android && SystemInfo.deviceModel.StartsWith("Oculus", System.StringComparison.OrdinalIgnoreCase); }
        }

        private IEnumerator Start()
        {
            if (CompositorUsesAlpha)
                StartCoroutine(MakeFramesOpaque());

            for (int i = 0; i < 5; ++i){ // one of those race conditions that require things setup after Start()
                lastResolution = new Resolution() { width = DaggerfallUnity.Settings.ResolutionWidth, height = DaggerfallUnity.Settings.ResolutionHeight };
                SetResolution(lastResolution.width, lastResolution.height);
                GameManager.UpdateScreenOrientation();
                yield return null;
            }
            isSetup = true;
        }
        /// <summary>
        /// Sets every pixel's alpha to 1 once each frame is drawn, leaving its colour as it is.
        /// </summary>
        private IEnumerator MakeFramesOpaque()
        {
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
                yield break;

            // Adding (0, 0, 0, 1) keeps the colour and saturates the alpha
            opaqueAlphaMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            opaqueAlphaMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            opaqueAlphaMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            opaqueAlphaMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            opaqueAlphaMaterial.SetInt("_ZWrite", 0);
            opaqueAlphaMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                GL.PushMatrix();
                GL.LoadOrtho();
                opaqueAlphaMaterial.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(Color.black);
                GL.Vertex3(0, 0, 0);
                GL.Vertex3(0, 1, 0);
                GL.Vertex3(1, 1, 0);
                GL.Vertex3(1, 0, 0);
                GL.End();
                GL.PopMatrix();
            }
        }
        private void OnDestroy()
        {
            if (opaqueAlphaMaterial)
                Destroy(opaqueAlphaMaterial);
        }
        private void Update()
        {
            if(!isSetup)
                return;

            int x = AScreen.width;
            int y = AScreen.height;
            if (x != lastResolution.width || y != lastResolution.height){
                // looks like the resolution changed. Let's update the daggerfall unity resolution
                SetResolution(x, y);
                lastResolution = new Resolution() { width = x, height = y };

                GameManager.UpdateScreenOrientation();
            }
        }
        public static void SetResolution(int x, int y)
        {
            Debug.Log("AndroidScreenManager: Current screen updated to new resolution");

            SettingsManager.SetScreenResolution(x, y, DaggerfallUnity.Settings.Fullscreen);
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