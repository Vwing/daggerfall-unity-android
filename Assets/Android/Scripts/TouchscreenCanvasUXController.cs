using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using UnityEngine;
using UnityEngine.UI;


public class TouchscreenCanvasUXController : MonoBehaviour
{
    [SerializeField] private Toggle leftJoystickToggle;
    [SerializeField] private Toggle rightJoystickToggle;
    [SerializeField] private TMPro.TMP_Dropdown selectedButtonTypeDropdown;
    [SerializeField] private RectTransform configPanelCanvasRTF;
    [SerializeField] private RectTransform selectedButtonAdvancedCanvasRTF;
    [SerializeField] private CanvasGroup leftJoystickSliderCG;
    [SerializeField] private CanvasGroup rightJoystickSliderCG;
    [SerializeField] private CanvasGroup touchscreenSensitivitySliderCG;
    [SerializeField] private GameObject selectedButtonKnobSpriteDropdownGO;
    [SerializeField] private GameObject selectedButtonJoystickSliderGO;
    [SerializeField] private GameObject selectedButtonDeleteButtonGO;
    [SerializeField] private float configPanelUnexpandedHeight = 334.3f;
    [SerializeField] private float configPanelExpandedHeight = 383.4f;
    [SerializeField] private float selectedButtonAdvancedUnexpandedHeight = 329f;
    [SerializeField] private float selectedButtonAdvancedExpanded1Height = 393f;
    [SerializeField] private float selectedButtonAdvancedExpanded2Height = 450f;
    
    private void Start()
    {
        leftJoystickToggle.onValueChanged.AddListener(delegate(bool b) { UpdateUIUX(); });
        rightJoystickToggle.onValueChanged.AddListener(delegate(bool b) { UpdateUIUX(); });
        selectedButtonTypeDropdown.onValueChanged.AddListener(delegate(int i) {UpdateUIUX();});
        TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged += TouchscreenInputManager_onCurrentlyEditingButtonChanged;
        UpdateUIUX();
    }
    private void OnDestroy()
    {
        TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged -= TouchscreenInputManager_onCurrentlyEditingButtonChanged;
    }
    private void UpdateUIUX()
    {
        switch((TouchscreenButtonType)selectedButtonTypeDropdown.value){
            case TouchscreenButtonType.DPad:
            case TouchscreenButtonType.CameraDPad:
                selectedButtonAdvancedCanvasRTF.sizeDelta = new Vector2(selectedButtonAdvancedCanvasRTF.sizeDelta.x, selectedButtonAdvancedExpanded1Height);
                selectedButtonKnobSpriteDropdownGO.SetActive(true);
                selectedButtonJoystickSliderGO.SetActive(false);
                break;
            case TouchscreenButtonType.CameraJoystick:
            case TouchscreenButtonType.Joystick:
                selectedButtonAdvancedCanvasRTF.sizeDelta = new Vector2(selectedButtonAdvancedCanvasRTF.sizeDelta.x, selectedButtonAdvancedExpanded2Height);
                selectedButtonKnobSpriteDropdownGO.SetActive(true);
                selectedButtonJoystickSliderGO.SetActive(true);
                break;
            default:
                selectedButtonAdvancedCanvasRTF.sizeDelta = new Vector2(selectedButtonAdvancedCanvasRTF.sizeDelta.x, selectedButtonAdvancedUnexpandedHeight);
                selectedButtonKnobSpriteDropdownGO.SetActive(false);
                selectedButtonJoystickSliderGO.SetActive(false);
                break;
        }
        bool isConfigPanelExpanded = !leftJoystickToggle.isOn || !rightJoystickToggle.isOn;
        configPanelCanvasRTF.sizeDelta = new Vector2(configPanelCanvasRTF.sizeDelta.x, isConfigPanelExpanded ?  configPanelExpandedHeight : configPanelUnexpandedHeight);
        static void SetCGEnabled(CanvasGroup cg, bool enable){
            cg.interactable = enable;
            cg.blocksRaycasts = enable;
            // cg.alpha = enable ? 1 : 0.4f;
            cg.alpha = enable ? 1 : 0f;
        }
        SetCGEnabled(leftJoystickSliderCG, leftJoystickToggle.isOn);
        SetCGEnabled(rightJoystickSliderCG, rightJoystickToggle.isOn);
        SetCGEnabled(touchscreenSensitivitySliderCG, isConfigPanelExpanded);
        selectedButtonDeleteButtonGO.SetActive(TouchscreenInputManager.Instance.CurrentlyEditingButton != null);
    }
    private void TouchscreenInputManager_onCurrentlyEditingButtonChanged(TouchscreenButton newButton) => UpdateUIUX();
}
