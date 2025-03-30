// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net), Pango
// Contributors:    
// 
// Notes:
//

using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using UnityEngine;

namespace DaggerfallWorkshop.Utility
{
    /// <summary>
    /// Changes camera viewport based on large HUD configuration.
    /// </summary>
    public class ViewportChanger : MonoBehaviour
    {
        public bool isRetroPresenter = false;
        public Camera retroClearerCamera;

        Rect standardViewportRect = new Rect(0, 0, 1, 1);
        Rect lastViewportRect;
        new Camera camera;

        private void Start()
        {
            camera = GetComponent<Camera>();
        }

        private void Update()
        {
            // HUD must be created
            if (DaggerfallUI.Instance.DaggerfallHUD == null)
                return;

            // Offload when using retro aspect correction
            if (isRetroPresenter && DaggerfallUnity.Settings.RetroModeAspectCorrection != 0)
            {
                SetRetroAspectViewport();
                return;
            }
            else
            {
                DaggerfallUI.Instance.CustomScreenRect = null;
            }

            // Change viewport when large HUD is docked
            // When not using docked the large HUD is just an overlay of variable size and main viewport does not change
            if (DaggerfallUnity.Settings.LargeHUD && DaggerfallUnity.Settings.LargeHUDDocked)
            {
                // Shrink viewport to area not occupied by docked large HUD
                // Check size every frame as HUD height can change (e.g. resizing window, changing resolution)
                HUDLarge largeHUD = DaggerfallUI.Instance.DaggerfallHUD.LargeHUD;
                float hudHeight = largeHUD.ScreenHeight / AScreen.height;
                Rect rect = new Rect(0, hudHeight, 1, 1 - hudHeight);
                SetViewport(rect);
            }
            else
            {
                // Set standard viewport area
                SetViewport(standardViewportRect);
            }
        }

        void SetViewport(Rect rect)
        {
            // Do nothing if viewport rect hasn't changed
            if (rect == lastViewportRect)
                return;

            // Set viewport rect to camera
            if (camera)
            {
                // Handle retro rendering mode
                // Camera viewport does not work with render textures so need to adjust output to appropriately size render target instead
                // Then retro presentation needs to use correct screen viewport area, not main camera
                if (DaggerfallUnity.Settings.RetroRenderingMode != 0 && !isRetroPresenter)
                {
                    camera.rect = standardViewportRect;
                    GameManager.Instance.RetroRenderer.UpdateRenderTarget();
                }
                else
                {
                    camera.rect = rect;
                }

                lastViewportRect = rect;
            }
        }

        void SetRetroAspectViewport()
        {
            // Get HUD height in pixels (if docked)
            int hudPixels = DaggerfallUnity.Settings.LargeHUD && DaggerfallUnity.Settings.LargeHUDDocked
                                ? (int)DaggerfallUI.Instance.DaggerfallHUD.LargeHUD.ScreenHeight
                                : 0;
            RetroModeAspects aspect = (RetroModeAspects)DaggerfallUnity.Settings.RetroModeAspectCorrection;

            float targetAspect = (aspect == RetroModeAspects.FourThree) ? (4f / 3f) : (16f / 10f);
            float screenAspect = AScreen.width / (float) AScreen.height;

            if (screenAspect < targetAspect)
            {
                // For portrait mode, use full width and compute content height to preserve the target aspect ratio.
                // Full width is used, so content height is derived from the aspect ratio.
                float contentHeight = AScreen.width / targetAspect;
                // Calculate available vertical space (screen height minus docked HUD area)
                float availableHeight = AScreen.height - hudPixels;
                // Center content vertically by adding equal letterbox bars at top and bottom
                float letterHeight = (availableHeight - contentHeight) / 2f;
                // Create a normalized viewport rect (x=0, full width)
                Rect rect = new Rect(0, (hudPixels + letterHeight) / (float)AScreen.height, 1, contentHeight / (float)AScreen.height);
                // Also update the UI's custom screen rect (in pixels)
                DaggerfallUI.Instance.CustomScreenRect = new Rect(0, hudPixels + letterHeight, AScreen.width, contentHeight);
                // Activate retro clearer camera if necessary
                if (retroClearerCamera && !retroClearerCamera.gameObject.activeSelf)
                    retroClearerCamera.gameObject.SetActive(true);
                SetViewport(rect);
            }
            else
            {
                // Landscape mode: use the original approach, which calculates pillarboxing.
                float heightRatio = 0;
                int viewWidth = 0;
                if (aspect == RetroModeAspects.FourThree)
                {
                    heightRatio = AScreen.height / 6f / 200f;
                    viewWidth = (int)(320f * 5f * heightRatio);
                }
                else if (aspect == RetroModeAspects.SixteenTen)
                {
                    heightRatio = AScreen.height / 6f / 200f;
                    viewWidth = (int)(320f * 6f * heightRatio);
                }
                int pillarWidth = (AScreen.width - viewWidth) / 2;
                float hudNormalized = hudPixels / (float)AScreen.height;
                float normalizedX = (float)pillarWidth / AScreen.width;
                float normalizedWidth = 1 - normalizedX * 2;
                Rect rect = new Rect(normalizedX, hudNormalized, normalizedWidth, 1.0f - hudNormalized);
                DaggerfallUI.Instance.CustomScreenRect = new Rect(pillarWidth, 0, AScreen.width - pillarWidth * 2, AScreen.height);
                if (retroClearerCamera && !retroClearerCamera.gameObject.activeSelf)
                    retroClearerCamera.gameObject.SetActive(true);
                SetViewport(rect);
            }
        }

    }
}