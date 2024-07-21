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
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

namespace DaggerfallWorkshop.Game
{
    /// <summary>
    /// A joystick (or dpad) that can be added to the screen similarly to other buttons
    /// </summary>
    public class StaticTouchscreenJoystickOrDPad : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public RectTransform background;
        public RectTransform knob;

        [Header("Input Settings")]
        public InputManager.AxisActions horizontalAxisAction = InputManager.AxisActions.MovementHorizontal;
        public InputManager.AxisActions verticalAxisAction = InputManager.AxisActions.MovementVertical;
        public float deadzone = 0.08f;
        public bool isDPad;

        public Vector2 TouchStartPos {get; private set;}

        private Vector2 inputVector;
        private float joystickRadius;
        private bool isTouching = false;
        private Camera myCam;
        private TouchscreenButton myButton;
        
        private int myTouchFingerID = -1;

        void Start()
        {
            // get refs
            myCam = GetComponentInParent<Canvas>().worldCamera;
            myButton = GetComponent<TouchscreenButton>();
            myButton.Resized += SetJoystickRadius;
            
            // set vars
            SetJoystickRadius();
            knob.gameObject.SetActive(false);
        }
        void OnDestroy()
        {
            if(myButton)
                myButton.Resized -= SetJoystickRadius;
        }
        private void LateUpdate()
        {
            if (isTouching && myTouchFingerID >= 0)
            {
                Touch myTouch = Input.touches.FirstOrDefault(p => p.fingerId == myTouchFingerID);
                if (myTouch.fingerId != myTouchFingerID || myTouch.phase == TouchPhase.Ended || myTouch.phase == TouchPhase.Canceled)
                {
                    Debug.Log("Touch ended");
                    OnPointerUp(null);
                }
            }
        }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (Cursor.visible)
                return;
            knob.gameObject.SetActive(true);
            TouchStartPos = eventData.position;
            knob.position = background.position;
            isTouching = true;
            OnDrag(eventData);
            for (int i = 0; i < Input.touchCount; i++)
            {
                if (Vector2.Distance(Input.GetTouch(i).position, TouchStartPos) < 3f)
                {
                    Debug.Log("Set touch to " + i);
                    myTouchFingerID = Input.GetTouch(i).fingerId;
                    break;
                }
            }
        }
        
        public void OnPointerUp(PointerEventData eventData)
        {
            knob.gameObject.SetActive(false);
            inputVector = Vector2.zero;
            UpdateVirtualAxes(Vector2.zero);
            isTouching = false;
            myTouchFingerID = -1;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isTouching)
                return;
            Vector2 backgroundPosScreenSpace = RectTransformUtility.WorldToScreenPoint(myCam, background.position);
            Vector2 direction = eventData.position - backgroundPosScreenSpace;
            inputVector = Vector2.ClampMagnitude(direction / joystickRadius, 1f);
            if(isDPad)
                inputVector = SnapTo8Directions(inputVector);
            Vector2 knobPosScreenSpace = backgroundPosScreenSpace + inputVector * joystickRadius;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(knob.parent as RectTransform, knobPosScreenSpace, myCam, out Vector2 knobPos))
                knob.localPosition = knobPos;
            UpdateVirtualAxes(inputVector);
        }
        private Vector2 SnapTo8Directions(Vector2 input)
        {
            if (input.magnitude <  deadzone)
                return Vector2.zero;

            float angle = Mathf.Atan2(input.y, input.x);
            angle = Mathf.Round(angle / (Mathf.PI / 4)) * (Mathf.PI / 4);

            Vector2 toReturn = new(Mathf.Cos(angle), Mathf.Sin(angle));
            if(Mathf.Abs(toReturn.x) < .01f)
                toReturn.x = 0;
            if(Mathf.Abs(toReturn.y) < .01f)
                toReturn.y = 0;

            return toReturn;
        }
        private void SetJoystickRadius(){
            Rect joystickRect = UnityUIUtils.GetScreenspaceRect(background, myCam);
            Rect knobRect = UnityUIUtils.GetScreenspaceRect(knob, myCam);
            joystickRadius = joystickRect.width / 2f - knobRect.width/2f;
        }
        private void UpdateVirtualAxes(Vector2 inputVec)
        {
            TouchscreenInputManager.SetAxis(horizontalAxisAction, inputVec.x);
            TouchscreenInputManager.SetAxis(verticalAxisAction, inputVec.y);
        }
    }
}
