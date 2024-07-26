// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2024 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Vincent Wing (vwing@uci.edu)
// Contributors:
// 
// Notes:
//

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;
using System.Collections;
using System.Linq;

namespace DaggerfallWorkshop.Game
{
    public class TouchscreenInputManager : MonoBehaviour
    {
        public static TouchscreenInputManager Instance { get; private set; }

        #region monobehaviour
        [Header("References")]
        [SerializeField] private Camera renderCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private UnityUIPopup confirmChangePopup;
        [SerializeField] private Canvas editControlsCanvas;
        [SerializeField] private Canvas joystickCanvas;
        [SerializeField] private Canvas buttonsCanvas;
        [SerializeField] private Button editControlsBackgroundButton;
        [SerializeField] private TouchscreenButton editTouchscreenControlsButton;
        [SerializeField] private Canvas selectedButtonOptionsPanel;
        [Header("On-Screen Control Options")]
        [SerializeField] private Button resetButtonTransformsButton;
        [SerializeField] private Button resetButtonMappingsButton;
        [SerializeField] private Slider alphaSlider;
        [SerializeField] private Toggle joystickTapsActivateCenterObjectToggle;
        [Header("Debug")]
        [SerializeField] private bool debugInEditor = false;


        public Camera RenderCamera { get { return renderCamera; } }
        public bool IsEditingControls { get { return editControlsCanvas.enabled; } }
        public TouchscreenButton CurrentlyEditingButton { get { return currentlyEditingButton; } }
        public UnityUIPopup ConfirmChangePopup { get{ return confirmChangePopup; } }
        public Slider AlphaSlider{get{return alphaSlider;}}
        public float SavedAlpha { get { return PlayerPrefs.GetFloat("TouchscreenControlsAlpha", 1f); } set { PlayerPrefs.SetFloat("TouchscreenControlsAlpha", value);} }

        public event System.Action<bool> onEditControlsToggled;
        public event System.Action<TouchscreenButton> onCurrentlyEditingButtonChanged;
        public event System.Action onResetButtonActionsToDefaultValues;
        public event System.Action onResetButtonTransformsToDefaultValues;

        private RenderTexture renderTex;
        private TouchscreenButton currentlyEditingButton;

        private void Awake()
        {
            #region singleton
            if (Instance)
            {
                Debug.LogError("Two TouchscreenInputManager singletons are present!");
                Destroy(gameObject);
            }
            Instance = this;

            #endregion

            Setup();
        }
        public void SetupUIRenderTexture()
        {
            if(renderCamera.targetTexture != null)
            {
                Destroy(renderCamera.targetTexture);
                renderCamera.targetTexture = null;
            }
            renderCamera.aspect = Camera.main.aspect;
            renderTex = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            renderTex.depthStencilFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt;
            renderTex.isPowerOfTwo = false;
            renderCamera.targetTexture = renderTex;

        }
        private void Setup()
        {
            editControlsCanvas.enabled = false;
            selectedButtonOptionsPanel.enabled = false;

            SetupUIRenderTexture();

            _debugInEditor = debugInEditor;
            if (!isMobilePlatform)
            {
                Debug.Log("Disabling touchscreen input manager");
                gameObject.SetActive(false);
            }

            editControlsBackgroundButton.gameObject.SetActive(false);

            alphaSlider.maxValue = 1;
            alphaSlider.minValue = 0.15f;
            canvasGroup.alpha = alphaSlider.value = SavedAlpha;

            joystickTapsActivateCenterObjectToggle.isOn = VirtualJoystick.JoystickTapsShouldActivateCenterObject;

            resetButtonMappingsButton.onClick.AddListener(OnResetButtonMappingsButtonClicked);
            resetButtonTransformsButton.onClick.AddListener(OnResetButtonTransformsButtonClicked);
            editControlsBackgroundButton.onClick.AddListener(OnEditControlsBackgroundClicked);
            alphaSlider.onValueChanged.AddListener(OnAlphaSliderValueChanged);
            joystickTapsActivateCenterObjectToggle.onValueChanged.AddListener(OnJoystickTapsToggleChanged);


            TouchscreenLayoutsManager.Instance.LoadDefaultLayout();
        }
        private void Update()
        {
            _isInDaggerfallGUI = !IsEditingControls && GameManager.IsGamePaused;
            canvas.enabled = IsTouchscreenActive;
            buttonsCanvas.enabled = IsTouchscreenActive;
            joystickCanvas.enabled = !IsEditingControls && IsTouchscreenActive;
        }
        private void OnGUI()
        {
            if (IsTouchscreenActive)
            {
                GUI.depth = 0;
                DaggerfallUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), renderTex, ScaleMode.ScaleAndCrop, true);
            }
        }

        public void EditTouchscreenButton(TouchscreenButton touchscreenButton)
        {
            if (touchscreenButton == currentlyEditingButton)
                return;

            StopEditingCurrentButton();
            selectedButtonOptionsPanel.enabled = touchscreenButton && (touchscreenButton.CanActionBeEdited || touchscreenButton.CanButtonBeRemoved);
            currentlyEditingButton = touchscreenButton;
            onCurrentlyEditingButtonChanged?.Invoke(touchscreenButton);
        }
        private void StopEditingCurrentButton()
        {
            selectedButtonOptionsPanel.enabled = false;
            currentlyEditingButton = null;
            onCurrentlyEditingButtonChanged?.Invoke(null);
        }

        private void OnEditControlsBackgroundClicked()
        {
            if (currentlyEditingButton != null)
                StopEditingCurrentButton();
        }
        private void OnResetButtonTransformsButtonClicked()
        {
            //if (!resetButtonTransformsButton.WasDragging)
            confirmChangePopup.Open("Do you want to reset the button positions, sizes, and enabled statuses to their default values?", onResetButtonTransformsToDefaultValues);
        }
        private void OnResetButtonMappingsButtonClicked()
        {
            //if (!resetButtonMappingsButton.WasDragging)
            confirmChangePopup.Open("Do you want to reset the button action mappings to their default values?", onResetButtonActionsToDefaultValues);
        }
        public void OnEditTouchscreenControlsButtonClicked(TouchscreenButton editControlsButton)
        {
            if (!editControlsButton.WasDragging)
            {
                editControlsCanvas.enabled = !editControlsCanvas.enabled;
                editControlsBackgroundButton.gameObject.SetActive(editControlsCanvas.enabled);
                GameManager.Instance.PauseGame(editControlsCanvas.enabled, true);
                onEditControlsToggled?.Invoke(editControlsCanvas.enabled);
            }
        }
        private void OnAlphaSliderValueChanged(float newVal)
        {
            SavedAlpha = newVal;
            canvasGroup.alpha = newVal;
        }
        private void OnJoystickTapsToggleChanged(bool val){
            VirtualJoystick.JoystickTapsShouldActivateCenterObject = val;
        }
        #endregion

        #region statics
        public static bool IsTouchscreenInputEnabled
        {
            get { return PlayerPrefs.GetInt("IsTouchscreenInputEnabled", 1) == 1; }
            set { PlayerPrefs.SetInt("IsTouchscreenInputEnabled", value ? 1 : 0); }
        }
        public static bool IsTouchscreenActive => IsTouchscreenInputEnabled && isMobilePlatform && !_isInDaggerfallGUI;
        public static bool IsTouchscreenTouched => IsTouchscreenActive && (axes.Any(p => p.Value != 0) || keys.Any(p => p.Value));

        private static Dictionary<int, float> axes = new Dictionary<int, float>();
        private static Dictionary<int, bool> keys = new Dictionary<int, bool>();
        private static bool isMobilePlatform => Application.isMobilePlatform || Application.isEditor && _debugInEditor;
        private static bool _debugInEditor = false;
        private static bool _isInDaggerfallGUI = false;

        public static void SetAxis(InputManager.AxisActions action, float value) => axes[(int)action] = value;
        public static float GetAxis(InputManager.AxisActions action)
        {
            if (!isMobilePlatform)
                return 0;
            return axes.ContainsKey((int)action) ? axes[(int)action] : 0;
        }
        public static void SetKey(KeyCode k, bool value) => keys[(int)InputManager.ConvertJoystickButtonKeyCode(k)] = value;
        public static bool GetKey(KeyCode k)
        {
            if (!isMobilePlatform)
                return false;
            return keys.ContainsKey((int)k) && keys[(int)k];
        }
        /// <summary>
        /// Triggers the given Daggerfall action by setting its bound key held then unheld.
        /// </summary>
        public static void TriggerAction(InputManager.Actions action){
            Instance.StartCoroutine(TriggerActionCoroutine(action));
        }
        private static IEnumerator TriggerActionCoroutine(InputManager.Actions action){
            yield return new WaitForEndOfFrame();
            KeyCode actionKey = InputManager.Instance.GetBinding(action);
            SetKey(actionKey, true);
            yield return new WaitForEndOfFrame();
            SetKey(actionKey, false);
        }
        public static bool GetPollKey(KeyCode k)
        {
            if (!isMobilePlatform)
                return false;
            return GetKey(k);
        }
        #endregion
    }
}