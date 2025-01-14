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


using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


namespace DaggerfallWorkshop.Game
{
    public class TouchscreenButton : Button
    {
        public enum ResizeButtonPosition { TopLeft, TopRight, BottomLeft, BottomRight }
        private const int k_defaultSnapScaleAt1080p = 20;

        private static bool s_shouldShowLabels = true;
        private static bool s_hasShownAddToDrawerPopup = false;
        private static bool s_hasShownRemoveFromDrawerPopup = false;
        private static TouchscreenButton s_drawerCurrentlyAddingTo = null;
        private static TouchscreenButton s_drawerCurrentlyRemovingFrom = null;

        public event System.Action Resized;

        public bool isButtonDrawer = false;
        public InputManager.Actions myAction = InputManager.Actions.Unknown;
        public KeyCode myKey = KeyCode.None;
        public bool WasDragging { get; private set; }
        public bool CanActionBeEdited{get{return canActionBeEdited;}}
        public bool CanButtonBeRemoved{get{return canButtonBeRemoved;}}

        [SerializeField] private bool canActionBeEdited = true;
        [SerializeField] private bool canButtonBeResized = true;
        [SerializeField] private bool canButtonBeRemoved = true;
        [SerializeField] private ResizeButtonPosition resizeButtonPos = ResizeButtonPosition.TopLeft;
        [SerializeField] private TMPro.TMP_Text label;
        [SerializeField] private RectTransform resizeButton;
        [SerializeField] private Button addToDrawerButton;
        [SerializeField] private Button removeFromDrawerButton;
        [SerializeField] private RectTransform buttonDrawerParent;

        private RectTransform rectTransform
        {
            get { return transform as RectTransform; }
        }
        private Camera RenderCam => TouchscreenInputManager.Instance.RenderCamera;
        private List<GameObject> buttonsInDrawer = new List<GameObject>();

        private InputManager.Actions myLastAction;
        private KeyCode myLastKey;

        private Vector2 defaultButtonSizeDelta;
        private Vector2 defaultButtonPosition;
        private InputManager.Actions defaultAction;

        private Vector2 pointerDownPos;
        private Vector2 pointerDownButtonSizeDelta;
        private Vector2 pointerDownButtonAnchoredPos;

        private bool pointerDownWasTouchingResizeButton;
        private bool isPointerDown;
        private int snapScale = 20;
        private bool isDrawerOpen = true;

        protected override void Start()
        {
            base.Start();
            if (Application.isPlaying)
            {
                defaultButtonSizeDelta = rectTransform.sizeDelta;
                defaultButtonPosition = rectTransform.anchoredPosition;
                defaultAction = myAction;

                snapScale = Mathf.RoundToInt(k_defaultSnapScaleAt1080p * (Mathf.Min(Screen.height, Screen.width) / 1080f));

                rectTransform.anchoredPosition = GetSavedPosition();
                rectTransform.sizeDelta = GetSavedSizeDelta();
                myAction = GetSavedAction();
                myKey = GetSavedKey();

                myLastAction = myAction;
                myLastKey = myKey;
                UpdateLabelText();
                resizeButton.gameObject.SetActive(false);

                if(isButtonDrawer){
                    addToDrawerButton.onClick.AddListener(OnAddToDrawerButtonClicked);
                    removeFromDrawerButton.onClick.AddListener(OnRemoveFromDrawerButtonClicked);
                    buttonsInDrawer.AddRange(GetSavedButtonsInMyDrawer().Select(s => TouchscreenButtonEnableDisableManager.Instance.GetButtonBehaviour(s).gameObject));
                    buttonsInDrawer.ForEach( delegate(GameObject but)
                    {
                        but.transform.SetParent(buttonDrawerParent, true); 
                        but.GetComponent<RectTransform>().anchoredPosition = but.GetComponent<TouchscreenButton>().GetSavedPosition();
                    });
                    CloseDrawer();
                }

                TouchscreenInputManager.Instance.onEditControlsToggled += Instance_onEditControlsToggled;
                TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged += Instance_onCurrentlyEditingButtonChanged;
                TouchscreenInputManager.Instance.onResetButtonActionsToDefaultValues += Instance_onResetButtonActionsToDefaultValues;
                TouchscreenInputManager.Instance.onResetButtonTransformsToDefaultValues += Instance_onResetButtonTransformsToDefaultValues;
            }
        }

        private void OnAddToDrawerButtonClicked()
        {
            void AddToDrawerOnConfirmation()
            {
                s_drawerCurrentlyAddingTo = this;
                s_drawerCurrentlyRemovingFrom = null;
                s_hasShownAddToDrawerPopup = true;
                // fade out all buttons that aren't in the drawer
                TouchscreenButtonEnableDisableManager.Instance.GetAllEnabledButtons().Where(p => !buttonsInDrawer.Contains(p.gameObject) || p == this).ToList().ForEach(p => p.image.color = new Color(1, 1, 1, 0.5f));
            }
            if(!s_hasShownAddToDrawerPopup)
                TouchscreenInputManager.Instance.ConfirmChangePopup.Open("Tap on the button you would like to add to the drawer.", AddToDrawerOnConfirmation, null, "Okay", "Cancel");
            else
                AddToDrawerOnConfirmation();
        }
        private void OnRemoveFromDrawerButtonClicked()
        {
            void RemoveFromDrawerOnConfirmation()
            {
                s_drawerCurrentlyAddingTo = null;
                s_drawerCurrentlyRemovingFrom = this;
                s_hasShownRemoveFromDrawerPopup = true;
                // fade out all buttons that aren't in the drawer
                TouchscreenButtonEnableDisableManager.Instance.GetAllEnabledButtons().Where(p => !buttonsInDrawer.Contains(p.gameObject) || p == this).ToList().ForEach(p => p.image.color = new Color(1, 1, 1, 0.5f));
            }
            if(!s_hasShownRemoveFromDrawerPopup)
                TouchscreenInputManager.Instance.ConfirmChangePopup.Open("Tap on the button you would like to remove from the drawer.", RemoveFromDrawerOnConfirmation, null, "Okay", "Cancel");
            else
                RemoveFromDrawerOnConfirmation();
        }

        protected override void OnDestroy()
        {
            if (TouchscreenInputManager.Instance)
            {
                TouchscreenInputManager.Instance.onEditControlsToggled -= Instance_onEditControlsToggled;
                TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged -= Instance_onCurrentlyEditingButtonChanged;
                TouchscreenInputManager.Instance.onResetButtonActionsToDefaultValues -= Instance_onResetButtonActionsToDefaultValues;
                TouchscreenInputManager.Instance.onResetButtonTransformsToDefaultValues -= Instance_onResetButtonTransformsToDefaultValues;
            }
        }
        
        private void Update()
        {
            UpdateResizeButtonPosition();
            UpdateLabelText();

            if (Application.isPlaying)
            {
                if (myLastAction != myAction)
                {
                    myLastAction = myAction;
                    SetSavedAction(myAction);
                }
                if (myLastKey != myKey)
                {
                    myLastKey = myKey;
                    SetSavedKey(myKey);
                }

                UpdateButtonTransform();
            }
        }
        public void CloseDrawer(){
            isDrawerOpen = false;
            buttonDrawerParent.gameObject.SetActive(false);
            // foreach(var button in buttonsInDrawer){
            //     button.gameObject.SetActive(false);
            // }
        }
        public void OpenDrawer(){
            isDrawerOpen = true;
            buttonDrawerParent.gameObject.SetActive(true);
            // foreach(var button in buttonsInDrawer){
            //     button.gameObject.SetActive(true);
            // }
        }

        public void AddButtonToDrawer(GameObject buttonGO)
        {
            if(buttonsInDrawer.Any(p => p.name == buttonGO.name))
                return;
            // GameObject buttonGO = TouchscreenButtonEnableDisableManager.Instance.GetButtonBehaviour(buttonName).gameObject;
            buttonsInDrawer.Add(buttonGO);
            buttonGO.GetComponent<RectTransform>().SetParent(buttonDrawerParent, true);
            buttonGO.GetComponent<RectTransform>().ForceUpdateRectTransforms();
            buttonGO.GetComponent<TouchscreenButton>().SetSavedPosition(buttonGO.GetComponent<RectTransform>().anchoredPosition);
            AddToSavedButtonsInMyDrawer(buttonGO.name);
        }
        public void RemoveButtonFromDrawer(GameObject buttonGO)
        {
            if(!buttonsInDrawer.Any(p => p.name == buttonGO.name) || buttonGO == gameObject)
                return;
            // int buttonInDrawerIndex = buttonsInDrawer.FindIndex(p => p.name == buttonName);
            Transform buttonsParent = transform.parent;
            while(buttonsParent.TryGetComponent(out TouchscreenButton b))
                buttonsParent = buttonsParent.parent;
            buttonGO.GetComponent<RectTransform>().SetParent(buttonsParent, true);
            buttonGO.GetComponent<RectTransform>().ForceUpdateRectTransforms();
            buttonGO.GetComponent<TouchscreenButton>().SetSavedPosition(buttonGO.GetComponent<RectTransform>().anchoredPosition);
            
            buttonsInDrawer.Remove(buttonGO);
            RemoveFromSavedButtonsInMyDrawer(buttonGO.name);
        }

        private void UpdateLabelText()
        {
            if (!label)
                return;
            else if (!canActionBeEdited)
                label.enabled = !Application.isPlaying || TouchscreenInputManager.Instance.IsEditingControls;
            else if (!Application.isPlaying || TouchscreenInputManager.Instance.IsEditingControls && s_shouldShowLabels)
            {
                label.enabled = true;
                if(myKey == KeyCode.None)
                    label.text = myAction.ToString();
                else if (myAction == InputManager.Actions.Unknown)
                    label.text = myKey.ToString();
                else
                    label.text = $"{myAction} + {myKey}";
            }
            else
                label.enabled = false;
        }
        private void UpdateButtonTransform()
        {
            if (!TouchscreenInputManager.Instance.IsEditingControls || !isPointerDown)
                return;

            Vector2 pointerDelta = (Vector2)Input.mousePosition - pointerDownPos;

            if (pointerDownWasTouchingResizeButton)
            {
                // resize button
                Vector2 newSize = pointerDownButtonSizeDelta + 2f * Mathf.Max(pointerDelta.x, pointerDelta.y) * pointerDownButtonSizeDelta.normalized;
                if (newSize.x < defaultButtonSizeDelta.x / 2f)
                    newSize = defaultButtonSizeDelta / 2f;
                else if (newSize.x > defaultButtonSizeDelta.x * 5f)
                    newSize = defaultButtonSizeDelta * 5f;
                newSize.x = Mathf.RoundToInt(newSize.x / snapScale) * snapScale;
                newSize.y = Mathf.RoundToInt(newSize.y / snapScale) * snapScale;

                if (Mathf.Abs(newSize.x - defaultButtonSizeDelta.x) < snapScale)
                    newSize = defaultButtonSizeDelta;

                Vector2 lastSize = rectTransform.sizeDelta;
                rectTransform.sizeDelta = newSize;
                if (!Mathf.Approximately(lastSize.x, newSize.x))
                    Resized?.Invoke();
            }
            else
            {
                // Move the button's position
                Vector2 lastAnchoredPos = rectTransform.anchoredPosition;

                Vector2 newPos = pointerDownButtonAnchoredPos + pointerDelta;

                newPos.x = Mathf.RoundToInt(newPos.x / snapScale) * snapScale;
                newPos.y = Mathf.RoundToInt(newPos.y / snapScale) * snapScale;

                if (Vector2.Distance(newPos, defaultButtonPosition) < snapScale)
                    newPos = defaultButtonPosition;

                // clamp rect to screen bounds
                rectTransform.anchoredPosition = newPos;

                Rect screenRect = UnityUIUtils.GetScreenspaceRect(rectTransform, RenderCam);

                if (screenRect.xMin < 0)
                {
                    newPos.x = lastAnchoredPos.x;
                }
                if (screenRect.yMin < 0)
                {
                    newPos.y = lastAnchoredPos.y;
                }
                if (screenRect.xMax >= Screen.width)
                {
                    newPos.x = lastAnchoredPos.x;
                }
                if (screenRect.yMax >= Screen.height)
                {
                    newPos.y = lastAnchoredPos.y;
                }
                rectTransform.anchoredPosition = newPos;

                WasDragging = WasDragging || !rectTransform.anchoredPosition.Approximately(lastAnchoredPos);
            }
        }

        private void UpdateResizeButtonPosition()
        {
            if (resizeButton)
            {
                switch (resizeButtonPos)
                {
                    case ResizeButtonPosition.TopLeft:
                        resizeButton.anchorMax = resizeButton.anchorMin = Vector2.up;
                        resizeButton.pivot = Vector2.right;
                        break;
                    case ResizeButtonPosition.TopRight:
                        resizeButton.anchorMax = resizeButton.anchorMin = Vector2.one;
                        resizeButton.pivot = Vector2.zero;
                        break;
                    case ResizeButtonPosition.BottomLeft:
                        resizeButton.anchorMax = resizeButton.anchorMin = Vector2.zero;
                        resizeButton.pivot = Vector2.one;
                        break;
                    case ResizeButtonPosition.BottomRight:
                        resizeButton.anchorMax = resizeButton.anchorMin = Vector2.right;
                        resizeButton.pivot = Vector2.up;
                        break;
                    default:
                        break;
                }
                resizeButton.anchoredPosition = Vector2.zero;
            }
        }
        private bool IsPointerTouchingResizeButton(PointerEventData pointerData)
        {
            if (!resizeButton || !resizeButton.gameObject.activeSelf)
                return false;
            return UnityUIUtils.GetScreenspaceRect(resizeButton, RenderCam).Contains(pointerData.position);
        }

        private void OnPointerDownDuringEditMode(PointerEventData eventData)
        {
            s_shouldShowLabels = !canActionBeEdited;
            transform.SetAsLastSibling();
            pointerDownWasTouchingResizeButton = IsPointerTouchingResizeButton(eventData);

            if (!pointerDownWasTouchingResizeButton)
                TouchscreenInputManager.Instance.EditTouchscreenButton(this);
        }
        private void OnPointerUpDuringEditMode(PointerEventData eventData)
        {
            if(!rectTransform.anchoredPosition.Approximately(pointerDownButtonAnchoredPos))
                SetSavedPosition(rectTransform.anchoredPosition);
            if (!rectTransform.sizeDelta.Approximately(pointerDownButtonSizeDelta))
                SetSavedSizeDelta(rectTransform.sizeDelta);
        }
        private void OnPointerDownDuringGameplay(PointerEventData eventData)
        {
            if(isButtonDrawer){
                if(isDrawerOpen)
                    CloseDrawer();
                else
                    OpenDrawer();
            }
            if (myAction > InputManager.Actions.Unknown) // if I have a custom action, add the action to the input manager manually
            {
                InputManager.Instance.AddAction(myAction);
            }
            else // else use our touchscreen input manager normally
            {
                KeyCode actionKey = InputManager.Instance.GetBinding(myAction);
                TouchscreenInputManager.SetKey(actionKey, true);
            }
            if (myKey != KeyCode.None)
                TouchscreenInputManager.SetKey(myKey, true);
        }
        private void OnPointerUpDuringGameplay(PointerEventData eventData)
        {
            KeyCode actionKey = InputManager.Instance.GetBinding(myAction);
            TouchscreenInputManager.SetKey(actionKey, false);
            if(myKey != KeyCode.None)
                TouchscreenInputManager.SetKey(myKey, false);
        }

        #region overrides
        
        public override void OnPointerDown(PointerEventData eventData)
        {
            Debug.Log("OnPointerDown " + gameObject.name);
            if(s_drawerCurrentlyAddingTo)
            {
                // add this button to the drawer
                if(this != s_drawerCurrentlyAddingTo)
                    s_drawerCurrentlyAddingTo.AddButtonToDrawer(gameObject);
                s_drawerCurrentlyAddingTo = null;
                // restore fade of all buttons
                TouchscreenButtonEnableDisableManager.Instance.GetAllButtons().ForEach(p => p.image.color = new Color(1, 1, 1, 1));
            }
            else if(s_drawerCurrentlyRemovingFrom)
            {
                // remove this button from the drawer
                if(this != s_drawerCurrentlyRemovingFrom)
                    s_drawerCurrentlyRemovingFrom.RemoveButtonFromDrawer(gameObject);
                s_drawerCurrentlyRemovingFrom = null;
                // restore fade of all buttons
                TouchscreenButtonEnableDisableManager.Instance.GetAllButtons().ForEach(p => p.image.color = new Color(1, 1, 1, 1));
            }
            else{
                isPointerDown = true;
                WasDragging = false;
                pointerDownPos = eventData.position;
                pointerDownButtonSizeDelta = rectTransform.sizeDelta;
                pointerDownButtonAnchoredPos = rectTransform.anchoredPosition;
                if (TouchscreenInputManager.Instance.IsEditingControls)
                    OnPointerDownDuringEditMode(eventData);
                else
                    OnPointerDownDuringGameplay(eventData);
            }

        }
        public override void OnPointerUp(PointerEventData eventData)
        {
            isPointerDown = false;
            s_shouldShowLabels = true;
            if (TouchscreenInputManager.Instance.IsEditingControls)
            {
                OnPointerUpDuringEditMode(eventData);
            }
            else
            {
                OnPointerUpDuringGameplay(eventData);
            }
            pointerDownWasTouchingResizeButton = false;
        }

        #endregion

        #region PlayerPrefs

        private InputManager.Actions GetSavedAction()
        {
            int savedActionInt = PlayerPrefs.GetInt("TouchscreenButtonAction_" + gameObject.name, (int)defaultAction);
            if (savedActionInt < 0)
                return myAction;
            return (InputManager.Actions)savedActionInt;
        }
        private void SetSavedAction(InputManager.Actions action)
        {
            PlayerPrefs.SetInt("TouchscreenButtonAction_" + gameObject.name, (int)action);
        }
        private KeyCode GetSavedKey()
        {
            int savedActionInt = PlayerPrefs.GetInt("TouchscreenButtonKeyCode_" + gameObject.name, (int)KeyCode.None);
            if (savedActionInt < 0)
                return myKey;
            return (KeyCode)savedActionInt;
        }
        private void SetSavedKey(KeyCode key)
        {
            PlayerPrefs.SetInt("TouchscreenButtonKeyCode_" + gameObject.name, (int)key);
        }
        private void SetSavedPosition(Vector2 pos)
        {
            PlayerPrefs.SetFloat("TouchscreenButtonPosX_" + gameObject.name, pos.x);
            PlayerPrefs.SetFloat("TouchscreenButtonPosY_" + gameObject.name, pos.y);
        }
        private Vector2 GetSavedPosition()
        {
            float x = PlayerPrefs.GetFloat("TouchscreenButtonPosX_" + gameObject.name, defaultButtonPosition.x);
            float y = PlayerPrefs.GetFloat("TouchscreenButtonPosY_" + gameObject.name, defaultButtonPosition.y);
            return new Vector2(x, y);
        }
        private void SetSavedSizeDelta(Vector2 size)
        {
            PlayerPrefs.SetFloat("TouchscreenButtonSizeX_" + gameObject.name, size.x);
            PlayerPrefs.SetFloat("TouchscreenButtonSizeY_" + gameObject.name, size.y);
        }
        private Vector2 GetSavedSizeDelta()
        {
            float x = PlayerPrefs.GetFloat("TouchscreenButtonSizeX_" + gameObject.name, defaultButtonSizeDelta.x);
            float y = PlayerPrefs.GetFloat("TouchscreenButtonSizeY_" + gameObject.name, defaultButtonSizeDelta.y);
            return new Vector2(x, y);
        }

        private List<string> GetSavedButtonsInMyDrawer()
        {
            List<string> savedButtonsInDrawer = PlayerPrefs.GetString("TouchscreenButtonDrawerContents_" + gameObject.name, "").Split(',').Where(p => !string.IsNullOrEmpty(p)).ToList();
            return savedButtonsInDrawer;
        }
        private void SetSavedButtonsInMyDrawer(List<string> buttons)
        {
            string savedButtonsInDrawerString = "";
            foreach(string b in buttons)
                savedButtonsInDrawerString += $"{b},";
            savedButtonsInDrawerString = savedButtonsInDrawerString.TrimEnd(',');
            PlayerPrefs.SetString("TouchscreenButtonDrawerContents_" + gameObject.name, savedButtonsInDrawerString);
        }
        private void AddToSavedButtonsInMyDrawer(string button)
        {
            List<string> savedButtonsInDrawer = GetSavedButtonsInMyDrawer();
            savedButtonsInDrawer.Add(button);
            SetSavedButtonsInMyDrawer(savedButtonsInDrawer);
        }
        private void RemoveFromSavedButtonsInMyDrawer(string button)
        {
            List<string> savedButtonsInDrawer = GetSavedButtonsInMyDrawer();
            if(savedButtonsInDrawer.Contains(button))
                savedButtonsInDrawer.Remove(button);
            SetSavedButtonsInMyDrawer(savedButtonsInDrawer);
        }
        private void ClearSavedButtonsInMyDrawer()
        {
            SetSavedButtonsInMyDrawer(new List<string>());
        }

        #endregion

        #region event listeners

        private void Instance_onEditControlsToggled(bool isEditingControls)
        {
            if (resizeButton && !isEditingControls)
                resizeButton.gameObject.SetActive(false);
            if (isButtonDrawer)
            {
                if(isEditingControls)
                    OpenDrawer();
                else
                    CloseDrawer();
            }
        }

        private void Instance_onCurrentlyEditingButtonChanged(TouchscreenButton currentlyEditingButton)
        {
            if(resizeButton)
                resizeButton.gameObject.SetActive(currentlyEditingButton == this && currentlyEditingButton.canButtonBeResized);

            if(isButtonDrawer){
                addToDrawerButton.gameObject.SetActive(currentlyEditingButton == this);
                removeFromDrawerButton.gameObject.SetActive(currentlyEditingButton == this);
            }

            if(currentlyEditingButton != this)
                WasDragging = false;
        }

        private void Instance_onResetButtonTransformsToDefaultValues()
        {
            if(transform.parent && transform.parent.parent && transform.parent.parent.TryGetComponent(out TouchscreenButton myDrawer)){
                myDrawer.RemoveButtonFromDrawer(gameObject);
            }
            rectTransform.anchoredPosition = defaultButtonPosition;
            rectTransform.sizeDelta = defaultButtonSizeDelta;
            SetSavedPosition(defaultButtonPosition);
            SetSavedSizeDelta(defaultButtonSizeDelta);
        }

        private void Instance_onResetButtonActionsToDefaultValues()
        {
            myAction = defaultAction;
            myKey = KeyCode.None;
            SetSavedAction(defaultAction);
            SetSavedKey(KeyCode.None);
        }

        #endregion
    }
}