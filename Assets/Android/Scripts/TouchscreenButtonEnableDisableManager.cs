
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;

namespace DaggerfallWorkshop.Game
{
    public class TouchscreenButtonEnableDisableManager : MonoBehaviour
    {
        #region Singleton
        public static TouchscreenButtonEnableDisableManager Instance { get; private set; }
        private bool SetupSingleton()
        {
            if (Instance)
            {
                Debug.LogError("There should be only one instance of the TouchscreenButtonEnableDisableManager singleton! Destroying self");
                Destroy(gameObject);
                return false;
            }
            Instance = this;
            return true;
        }
        #endregion

        #region Properties

        public bool IsLeftJoystickEnabled { get; set; }
        public bool IsRightJoystickEnabled { get; set; }

        public RectTransform ButtonsParent {get{return buttonsParent;}}
        public RectTransform ButtonsPoolParent {get{return buttonsPoolParent;}}

        #endregion

        [SerializeField] private GameObject buttonPrefabReference;
        [SerializeField] private RectTransform buttonsParent;
        [SerializeField] private RectTransform buttonsPoolParent;
        [SerializeField] private UnityUIPopup confirmationPopup;
        [SerializeField] private Button disableCurrentlyEditingButtonButton;
        [SerializeField] private TMPro.TMP_Dropdown enableNewButtonDropdown;
        [SerializeField] private List<TouchscreenButton> allButtons = new List<TouchscreenButton>();
        [SerializeField] private List<TouchscreenButton> buttonsPool = new List<TouchscreenButton>();
        [SerializeField] private Toggle leftJoystickToggle, rightJoystickToggle;
        [SerializeField] private VirtualJoystick leftJoystick, rightJoystick;
        private Dictionary<string, bool> allButtonDefaultValues = new Dictionary<string, bool>();

        private bool hasShownPopup = true; // set to false if you want to show a popup to the user when disabling a button
        private void Awake()
        {
            if (!SetupSingleton())
                return;

            foreach (var button in allButtons)
                allButtonDefaultValues[button.gameObject.name] = button.gameObject.activeSelf;
            UpdateAllButtonsEnabledStatus();

            leftJoystick.isInMouseLookMode = !IsLeftJoystickEnabled;
            rightJoystick.isInMouseLookMode = !IsRightJoystickEnabled;
            leftJoystickToggle.isOn = IsLeftJoystickEnabled;
            rightJoystickToggle.isOn = IsRightJoystickEnabled;

            disableCurrentlyEditingButtonButton.onClick.AddListener(DisableCurrentlyEditingButton);
            enableNewButtonDropdown.onValueChanged.AddListener(EnableNewButtonFromDropdown);
            leftJoystickToggle.onValueChanged.AddListener(OnLeftJoystickToggleValueChanged);
            rightJoystickToggle.onValueChanged.AddListener(OnRightJoystickToggleValueChanged);

            TouchscreenLayoutsManager.LayoutLoaded += TouchscreenLayoutsManager_LayoutLoaded;
        }
        private void OnDestroy()
        {
            TouchscreenLayoutsManager.LayoutLoaded -= TouchscreenLayoutsManager_LayoutLoaded;
        }
        public void ReturnAllButtonsToPool()
        {
            List<TouchscreenButton> allButtonsCopy = new(allButtons);
            foreach(var b in allButtonsCopy){
                ReturnButtonToPool(b);
            }
            allButtons.Clear();
        }
        public void ReturnButtonToPool(TouchscreenButton butt)
        {
            if(!butt)
                return;
            butt.image.sprite = null;
            butt.GetComponent<StaticTouchscreenJoystickOrDPad>().knob.GetComponent<Image>().sprite = null;
            butt.gameObject.SetActive(false);
            buttonsPool.Add(butt);
            butt.transform.SetParent(buttonsPoolParent, true);
            allButtons.Remove(butt);
        }
        public TouchscreenButton AddButtonFromPool(TouchscreenButtonConfiguration buttonConfig)
        {
            TouchscreenButton button;
            if(buttonsPool.Count == 0){
                button = GameObject.Instantiate(buttonPrefabReference, Vector3.zero, Quaternion.identity, buttonsParent).GetComponent<TouchscreenButton>();
            }
            else
            {
                button = buttonsPool[0];
                buttonsPool.RemoveAt(0);
                button.gameObject.SetActive(true);
                button.transform.SetParent(buttonsParent, true);
            }
            button.ApplyConfiguration(buttonConfig);
            allButtons.Add(button);
            return button;
        }
        public void DeleteButton(TouchscreenButton butt)
        {
            allButtons.Remove(butt);
            allButtonDefaultValues.Remove(butt.gameObject.name);
            buttonsPool.Remove(butt);
            GameObject.Destroy(butt.gameObject);
        }
        public void RemoveNullButtons()
        {
            allButtons.RemoveAll(p => p == null);
        }
        public List<TouchscreenButton> GetAllEnabledButtons()
        {
            return allButtons.Where(p => p.gameObject.activeSelf).ToList();
        }
        public List<TouchscreenButton> GetAllButtons()
        {
            return allButtons;
        }
        public TouchscreenButton GetButtonBehaviour(string buttonName)
        {
            TouchscreenButton theButton = allButtons.FirstOrDefault(p => p.gameObject.name == buttonName);
            if(theButton && theButton != default(TouchscreenButton))
                return theButton;
            else
                return null;
        }

        /// <summary>
        /// Sets a button's gameobject enabled or disabled, and saves that value for next game load
        /// </summary>
        public void SetButtonEnabled(TouchscreenButton button, bool enabled)
        {
            button.gameObject.SetActive(enabled);
            TouchscreenLayoutsManager.Instance.SetButtonEnabled(button.gameObject.name, enabled);
            UpdateEnableNewButtonDropdown();
        }

        // sets all buttons enabled or disabled depending on their saved value
        private void UpdateAllButtonsEnabledStatus()
        {
            foreach (TouchscreenButton button in allButtons)
            {
                var buttonConfig = button.GetCurrentConfiguration();
                button.gameObject.SetActive(buttonConfig.IsEnabled);
            }
            UpdateEnableNewButtonDropdown();
        }
        // updates the values of the 'add new button' dropdown
        private void UpdateEnableNewButtonDropdown()
        {
            enableNewButtonDropdown.ClearOptions();
            List<string> options = new List<string>();
            options.Add("");
            options.AddRange(allButtons.Where(p => !p.gameObject.activeSelf).Select(s => s.gameObject.name));
            enableNewButtonDropdown.AddOptions(options);
        }
        // callback for dropdown selection. Enables the selected button, and removes it from the dropdown list.
        private void EnableNewButtonFromDropdown(int val)
        {
            var option = enableNewButtonDropdown.options[val];
            TouchscreenButton selectedButton = allButtons.Find(p => p.gameObject.name == option.text);
            if (selectedButton)
            {
                SetButtonEnabled(selectedButton, true);
                UpdateEnableNewButtonDropdown();
            }
        }
        // disables the button currently being edited, and updates the dropdown list.
        private void DisableCurrentlyEditingButton()
        {
            System.Action onConfirmationAction = delegate
            {
                SetButtonEnabled(TouchscreenInputManager.Instance.CurrentlyEditingButton, false);
                TouchscreenInputManager.Instance.EditTouchscreenButton(null);
            };
            if (hasShownPopup)
                onConfirmationAction.Invoke();
            else
                confirmationPopup.Open($"Do you want to remove the '{TouchscreenInputManager.Instance.CurrentlyEditingButton.gameObject.name}' " +
                    "button? You can add it back in again with the 'Add Button' dropdown.", onConfirmationAction, null, "Yes, remove the button");
            hasShownPopup = true;
        }
        private void OnLeftJoystickToggleValueChanged(bool newVal)
        {
            leftJoystick.isInMouseLookMode = !newVal;
            IsLeftJoystickEnabled = newVal;
        }
        private void OnRightJoystickToggleValueChanged(bool newVal)
        {
            rightJoystick.isInMouseLookMode = !newVal;
            IsRightJoystickEnabled = newVal;
        }

        private void TouchscreenLayoutsManager_LayoutLoaded(string newLayoutName)
        {
            UpdateEnableNewButtonDropdown();

            leftJoystick.isInMouseLookMode = !IsLeftJoystickEnabled;
            rightJoystick.isInMouseLookMode = !IsRightJoystickEnabled;
            leftJoystickToggle.isOn = IsLeftJoystickEnabled;
            rightJoystickToggle.isOn = IsRightJoystickEnabled;
        }
    }
}