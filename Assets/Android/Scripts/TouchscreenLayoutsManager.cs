using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Linq;

namespace DaggerfallWorkshop.Game
{
    [System.Serializable]
    public class TouchscreenLayoutConfiguration
    {
        public string name = "default-layout";
        public float defaultUIAlpha = 1f;
        public bool leftJoystickEnabled = true;
        public bool rightJoystickEnabled = true;
        public bool screenTapsActivateCenterObject = true;
        public List<TouchscreenButtonConfiguration> buttons;

        public static TouchscreenLayoutConfiguration ReadFromPath(string path)
        {
            if(!File.Exists(path))
            {
                Debug.LogError($"TouchscreenButtonSerializable file path {path} does not exist");
                return null;
            }
            return Deserialize(File.ReadAllText(path));
        }
        public static void WriteToPath(TouchscreenLayoutConfiguration layout, string path)
        {
            try{
                File.WriteAllText(path, Serialize(layout));
            } catch (Exception e) {
                Debug.LogError($"Failed to write button {layout} to path {path} due to error {e}");
            }
        }
        public static TouchscreenLayoutConfiguration Deserialize(string json)
        {
            try {
                return JsonConvert.DeserializeObject<TouchscreenLayoutConfiguration>(json);
            } catch (Exception e) {
                Debug.LogError($"Failed to deserialize json string into a TouchscreenLayoutConfiguration object due to error {e}\n\nJSON contents:\n{json}");
                return null;
            }
        }
        public static string Serialize(TouchscreenLayoutConfiguration layout)
        {
            try{
                return JsonConvert.SerializeObject(layout);
            } catch (Exception e) {
                Debug.LogError($"Failed to serialize layout {layout.name} due to error {e}");
                return "";
            }
        }

    }
    public class TouchscreenLayoutsManager : MonoBehaviour
    {
        public static TouchscreenLayoutsManager Instance{get; private set;}

        [Header("Selected Button Options")]
        [SerializeField] private TMPro.TMP_Dropdown actionMappingDropdown;
        [SerializeField] private TMPro.TMP_Dropdown keycodeDropdown;
        [SerializeField] private TMPro.TMP_InputField buttonNameInputField; // new
        [SerializeField] private TMPro.TMP_Dropdown buttonTypeDropdown; // new
        [SerializeField] private TMPro.TMP_Dropdown anchorDropdown;
        [SerializeField] private TMPro.TMP_Dropdown labelAnchorDropdown;
        [SerializeField] private TMPro.TMP_Dropdown spriteDropdown; // new
        [SerializeField] private TMPro.TMP_Dropdown knobSpriteDropdown; // new

        private List<TouchscreenLayoutConfiguration> loadedLayouts = new List<TouchscreenLayoutConfiguration>();
        private Dictionary<string, int> acceptedKeyCodes = new Dictionary<string, int>();


        private void Awake()
        {
            Instance = this;
        }
        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            SetupUI();
            TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged += SetupUIBasedOnCurrentlyEditingTouchscreenButton;
#if UNITY_EDITOR
            yield return new WaitForSecondsRealtime(1f);
            var layout = GetCurrentLayoutConfig();
            string path = Path.Combine(Paths.PersistentDataPath, layout.name + ".json");
            TouchscreenLayoutConfiguration.WriteToPath(layout, path);
            string buttonPath = Path.Combine(Paths.PersistentDataPath, layout.buttons[0].Name + ".json");
            TouchscreenButtonConfiguration.WriteToPath(layout.buttons[0], buttonPath);
#endif
        }
        public void SetupUIBasedOnCurrentlyEditingTouchscreenButton(TouchscreenButton touchscreenButton)
        {
            if (touchscreenButton){
                actionMappingDropdown.interactable = touchscreenButton.CanActionBeEdited;
                actionMappingDropdown.value = (int)touchscreenButton.myAction;
                keycodeDropdown.interactable = touchscreenButton.CanActionBeEdited;
                keycodeDropdown.value = keycodeDropdown.options.FindIndex(p => p.text == touchscreenButton.myKey.ToString());
            }
        }
        private void SetupUI()
        {
            // edit controls canvas setup

            // Add button action mapping options
            actionMappingDropdown.ClearOptions();
            List<string> options = new List<string>();
            for (int i = 0; i <= (int)InputManager.Actions.Custom10; ++i)
                options.Add(((InputManager.Actions)i).ToString());
            actionMappingDropdown.AddOptions(options);
            actionMappingDropdown.onValueChanged.AddListener(OnEditControlsDropdownValueChanged);

            // Add button key mapping options
            IEnumerable<int> allKeyCodes = ((KeyCode[])System.Enum.GetValues(typeof(KeyCode))).Select(s => (int)s);
            keycodeDropdown.ClearOptions();
            foreach (var key in allKeyCodes)
                if (key < (int)KeyCode.Joystick1Button0 && !InputManager.unacceptedAnyKeys.Contains(key))
                    acceptedKeyCodes[((KeyCode)key).ToString()] = key;
            keycodeDropdown.AddOptions(acceptedKeyCodes.Select(s => s.Key).ToList());
            keycodeDropdown.onValueChanged.AddListener(OnEditControlsKeyCodeDropdownValueChanged);

            // button name changed
            buttonNameInputField.onEndEdit.AddListener(null);

            // Add button type options
            buttonTypeDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonType.CameraDPad; ++i)
                options.Add(((TouchscreenButtonType)i).ToString());
            buttonTypeDropdown.AddOptions(options);
            buttonTypeDropdown.onValueChanged.AddListener(OnButtonTypeDropdownValueChanged);

            // Add button anchor mapping options
            anchorDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
                options.Add(((TouchscreenButtonAnchor)i).ToString());
            anchorDropdown.AddOptions(options);
            anchorDropdown.onValueChanged.AddListener(OnButtonAnchorDropdownValueChanged);

            // Add label anchor mapping options
            labelAnchorDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
                options.Add(((TouchscreenButtonAnchor)i).ToString());
            labelAnchorDropdown.AddOptions(options);
            labelAnchorDropdown.onValueChanged.AddListener(OnLabelAnchorDropdownValueChanged);

            // Add sprite options
            spriteDropdown.ClearOptions();
            options.Clear();
            // for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
            //     options.Add(((TouchscreenButtonAnchor)i).ToString());
            spriteDropdown.AddOptions(options);
            spriteDropdown.onValueChanged.AddListener(null);

            // Add knob sprite options
            knobSpriteDropdown.ClearOptions();
            options.Clear();
            // for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
            //     options.Add(((TouchscreenButtonAnchor)i).ToString());
            knobSpriteDropdown.AddOptions(options);
            knobSpriteDropdown.onValueChanged.AddListener(null);
        }
        public void LoadLayout(TouchscreenLayoutConfiguration layoutConfig)
        {
            TouchscreenButtonEnableDisableManager.Instance.ReturnAllButtonsToPool();
            TouchscreenInputManager.Instance.SavedAlpha = layoutConfig.defaultUIAlpha;
            VirtualJoystick.JoystickTapsShouldActivateCenterObject = layoutConfig.screenTapsActivateCenterObject;
            TouchscreenButtonEnableDisableManager.Instance.IsLeftJoystickEnabled = layoutConfig.leftJoystickEnabled;
            TouchscreenButtonEnableDisableManager.Instance.IsRightJoystickEnabled = layoutConfig.rightJoystickEnabled;
            layoutConfig.buttons.Sort(new TouchscreenButtonConfigurationComparer());
            foreach (var buttonConfig in layoutConfig.buttons)
            {
                TouchscreenButtonEnableDisableManager.Instance.AddButtonFromPool(buttonConfig);
            }
            DaggerfallGC.ThrottledUnloadUnusedAssets();
        }
        public TouchscreenLayoutConfiguration GetCurrentLayoutConfig()
        {
            var layout = new TouchscreenLayoutConfiguration(){
                defaultUIAlpha = TouchscreenInputManager.Instance.SavedAlpha,
                screenTapsActivateCenterObject = VirtualJoystick.JoystickTapsShouldActivateCenterObject,
                leftJoystickEnabled = TouchscreenButtonEnableDisableManager.Instance.IsLeftJoystickEnabled,
                rightJoystickEnabled = TouchscreenButtonEnableDisableManager.Instance.IsRightJoystickEnabled,
                buttons = TouchscreenButtonEnableDisableManager.Instance.GetAllButtons().Select(s => s.GetCurrentConfiguration()).ToList()
            };
            layout.buttons.Sort(new TouchscreenButtonConfigurationComparer());
            return layout;
        }
        private void OnEditControlsDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.myAction = (InputManager.Actions)newVal;
            }
        }
        private void OnEditControlsKeyCodeDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                KeyCode newKey = (KeyCode)acceptedKeyCodes[keycodeDropdown.options[newVal].text];
                TouchscreenInputManager.Instance.CurrentlyEditingButton.myKey = newKey;
            }
        }
        private void OnButtonTypeDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetButtonType((TouchscreenButtonType)newVal);
            }
        }
        private void OnButtonAnchorDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetButtonAnchor((TouchscreenButtonAnchor)newVal);
            }
        }
        private void OnLabelAnchorDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetLabelAnchor((TouchscreenButtonAnchor)newVal);
            }
        }
    }
}