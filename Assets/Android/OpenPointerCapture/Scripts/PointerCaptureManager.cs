using UnityEngine;
using System;

namespace OpenPointerCapture
{
    // This MonoBehaviour manages the capture state based on Cursor.lockState
    // and polls the Android helper for input events when captured.
    [DefaultExecutionOrder(-1000)]
    public class PointerCaptureManager : MonoBehaviour
    {
        // Events that other scripts (like CapturedInput) can subscribe to
        public static event Action<Vector2> OnCapturedPointerMoved;
        public static event Action<int, bool> OnCapturedMouseButton; // buttonIndex, isDown
        public static event Action<Vector2> OnCapturedScroll; // scrollDelta (vertical, horizontal)

        public bool toggleCapturedStateWithCursorLockState = true;

        private CursorLockMode lastLockState;
        private const bool debugMouseLook = false;
        private float nextDebugLogTime;

        void Awake()
        {
            CapturedInput.Initialize();
        }

        void Start()
        {
            lastLockState = Cursor.lockState;
        }

        void Update()
        {
            if (toggleCapturedStateWithCursorLockState)
            {
                // Manage capture state based on Cursor.lockState
                if (Cursor.lockState == CursorLockMode.Locked && !PointerCaptureNativeInterface.isPointerCaptured())
                {
                    // Request capture when Cursor.lockState becomes Locked
                    CapturedInput.SetSimulatedMousePosition(Input.mousePosition);
                    PointerCaptureNativeInterface.beginCapture();
                }
                else if (Cursor.lockState != CursorLockMode.Locked && PointerCaptureNativeInterface.isPointerCaptured())
                {
                    // Release capture when Cursor.lockState becomes unlocked
                    PointerCaptureNativeInterface.endCapture();
                }
            }

            bool isPointerCaptured = PointerCaptureNativeInterface.isPointerCaptured();

            // Poll captured relative movement only after capture is active. Buttons and
            // scroll are polled below so they can also be mapped while menus are open.
            if (isPointerCaptured)
            {
                // --- Handle Pointer Movement ---
                float dx = PointerCaptureNativeInterface.getLastDx();
                float dy = PointerCaptureNativeInterface.getLastDy();
                Vector2 delta = new Vector2(dx, -dy);
                if (delta.x != 0 || delta.y != 0)
                {
                    LogMouseLookDebug("ML_POINTER_MANAGER poll dx=" + dx + " dy=" + dy + " unityDelta=" + delta);
                    OnCapturedPointerMoved?.Invoke(delta);
                }
            }

            // --- Handle Mouse Buttons ---
            // We check getLastActionButton which tells us which button changed state
            int actionButton = PointerCaptureNativeInterface.getLastActionButton();
            if (actionButton != 0)
            {
                // We need to map the Android button constants to Unity button indices
                int unityButtonIndex = MapAndroidButtonToUnity(actionButton);
                if (unityButtonIndex != -1) // Only process if it's a button we map (0-6)
                {
                    // Check the current state of this button using getLastButtonState
                    // Note: getButtonState is a bitfield, so we check if the specific button's bit is set.
                    int buttonStateBit = GetAndroidButtonStateBit(actionButton);
                    bool isDown = (PointerCaptureNativeInterface.getLastButtonState() & buttonStateBit) != 0;

                    //Debug.Log($"Captured Mouse Button: button={unityButtonIndex} (Android:{actionButton}), isDown={isDown}");
                    OnCapturedMouseButton?.Invoke(unityButtonIndex, isDown);
                }
            }

            // --- Handle Scroll Wheel ---
            float vScroll = PointerCaptureNativeInterface.getLastVerticalScrollDelta();
            float hScroll = PointerCaptureNativeInterface.getLastHorizontalScrollDelta();
            Vector2 scrollDelta = new Vector2(hScroll, vScroll); // Unity's scrollDelta is (x, y)
            if (scrollDelta.x != 0 || scrollDelta.y != 0)
            {
                //Debug.Log($"Captured Scroll: delta={scrollDelta}");
                OnCapturedScroll?.Invoke(scrollDelta);
            }

            lastLockState = Cursor.lockState;
        }

        void LateUpdate()
        {
            CapturedInput.ManualLateUpdate();
        }

        // Helper to map Android MotionEvent button constants to Unity button indices (0=Left, 1=Right, 2=Middle)
        private int MapAndroidButtonToUnity(int androidButton)
        {
            switch (androidButton)
            {
                case 1: return 0; // Primary -> Left
                case 2: return 1; // Secondary -> Right
                case 4: return 2; // Tertiary -> Middle
                case 8: return 3;
                case 16: return 4;
                case 32: return 5;
                case 64: return 6;
                default: return -1; // Unmapped button
            }
        }

        // Helper to get the corresponding bit from the button state bitfield
        private int GetAndroidButtonStateBit(int androidButton)
        {
            // The bitfield uses the same values as the action button constants
            return androidButton;
        }

        private void LogMouseLookDebug(string message)
        {
            if (!debugMouseLook || Time.realtimeSinceStartup < nextDebugLogTime)
                return;

            nextDebugLogTime = Time.realtimeSinceStartup + 0.25f;
            Debug.Log(message);
        }
    }

}
