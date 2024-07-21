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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DaggerfallWorkshop.Game
{
    public static class UnityUIUtils
    {
        // gotten from sildeflask in https://forum.unity.com/threads/how-to-get-a-rect-in-screen-space-from-a-recttransform.1490806/
        public static Rect GetScreenspaceRect(RectTransform rtf, Camera cam)
        {
            // Get the corners of the RectTransform in world space
            Vector3[] corners = new Vector3[4];
            rtf.GetWorldCorners(corners);

            // Convert world space to screen space in pixel values and round to integers
            for (int i = 0; i < corners.Length; i++)
            {
                corners[i] = cam.WorldToScreenPoint(corners[i]);
                corners[i] = new Vector3(Mathf.RoundToInt(corners[i].x), Mathf.RoundToInt(corners[i].y), corners[i].z);
            }

            // Calculate the screen space rectangle
            float minX = Mathf.Min(corners[0].x, corners[1].x, corners[2].x, corners[3].x);
            float minY = Mathf.Min(corners[0].y, corners[1].y, corners[2].y, corners[3].y);
            float width = Mathf.Max(corners[0].x, corners[1].x, corners[2].x, corners[3].x) - minX;
            float height = Mathf.Max(corners[0].y, corners[1].y, corners[2].y, corners[3].y) - minY;

            // Display the screen space rectangle
            Rect screenRect = new Rect(minX, minY, width, height);

            return screenRect;
        }
        public static void MatchRectTFToScreenspaceRect(RectTransform rtf, Rect rect, Camera cam)
        {
            if (!rtf.parent || rtf.parent is not RectTransform)
                return;

            Vector2 localPoint, rectMin, rectMax;
            // Set the position of the RectTransform
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.position, cam, out localPoint))
            {
                rtf.anchoredPosition = localPoint;
            }
            // Set the sizeDelta of the RectTransform
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.min, cam, out rectMin)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)rtf.parent, rect.max, cam, out rectMax))
            {
                rtf.sizeDelta = rectMax - rectMin;
            }
        }
        public static bool Approximately(this Vector2 v1, Vector2 v2)
        {
            return Mathf.Approximately(v1.x, v2.x) && Mathf.Approximately(v1.y, v2.y);
        }
    }
    public class TouchscreenButton : Button
    {
        public enum ResizeButtonPosition { TopLeft, TopRight, BottomLeft, BottomRight }
        private const int k_defaultSnapScaleAt1080p = 20;

        private static bool s_shouldShowLabels = true;

        public event System.Action Resized;

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

        private RectTransform rectTransform
        {
            get { return transform as RectTransform; }
        }
        private Camera RenderCam => TouchscreenInputManager.Instance.RenderCamera;

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

                TouchscreenInputManager.Instance.onEditControlsToggled += Instance_onEditControlsToggled;
                TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged += Instance_onCurrentlyEditingButtonChanged;
                TouchscreenInputManager.Instance.onResetButtonActionsToDefaultValues += Instance_onResetButtonActionsToDefaultValues;
                TouchscreenInputManager.Instance.onResetButtonTransformsToDefaultValues += Instance_onResetButtonTransformsToDefaultValues;
            }
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

        #region overrides

        public override void OnPointerDown(PointerEventData eventData)
        {
            isPointerDown = true;
            WasDragging = false;
            pointerDownPos = eventData.position;
            pointerDownButtonSizeDelta = rectTransform.sizeDelta;
            pointerDownButtonAnchoredPos = rectTransform.anchoredPosition;
            Debug.Log("OnPointerDown " + gameObject.name);
            if (TouchscreenInputManager.Instance.IsEditingControls)
            {
                s_shouldShowLabels = !canActionBeEdited;
                transform.SetAsLastSibling();
                pointerDownWasTouchingResizeButton = IsPointerTouchingResizeButton(eventData);

                if (!pointerDownWasTouchingResizeButton)
                    TouchscreenInputManager.Instance.EditTouchscreenButton(this);
            }
            else
            {
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

        }
        public override void OnPointerUp(PointerEventData eventData)
        {
            isPointerDown = false;
            s_shouldShowLabels = true;
            if (TouchscreenInputManager.Instance.IsEditingControls)
            {
                if(!rectTransform.anchoredPosition.Approximately(pointerDownButtonAnchoredPos))
                    SetSavedPosition(rectTransform.anchoredPosition);
                if (!rectTransform.sizeDelta.Approximately(pointerDownButtonSizeDelta))
                    SetSavedSizeDelta(rectTransform.sizeDelta);
            }
            else
            {
                KeyCode actionKey = InputManager.Instance.GetBinding(myAction);
                TouchscreenInputManager.SetKey(actionKey, false);
                if(myKey != KeyCode.None)
                    TouchscreenInputManager.SetKey(myKey, false);
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

        #endregion

        #region event listeners

        private void Instance_onEditControlsToggled(bool isEditingControls)
        {
            if (resizeButton && !isEditingControls)
                resizeButton.gameObject.SetActive(false);
        }

        private void Instance_onCurrentlyEditingButtonChanged(TouchscreenButton currentlyEditingButton)
        {
            if(resizeButton)
                resizeButton.gameObject.SetActive(currentlyEditingButton == this && currentlyEditingButton.canButtonBeResized);
            if(currentlyEditingButton != this)
                WasDragging = false;
        }

        private void Instance_onResetButtonTransformsToDefaultValues()
        {
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