using System;
using UnityEngine;

namespace OpenPointerCapture
{
    public static class PointerCaptureNativeInterface
    {
        private const string HelperClassName = "com.example.androidinputcapture.PointerCaptureHelper";

        // Keep a reference to the AndroidJavaClass for the helper
        private static AndroidJavaClass helperClass = null;

        // Initialize the Android helper when the application loads
        // RuntimeInitializeLoadType.BeforeSceneLoad ensures this runs very early.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log("PointerCaptureNativeInterface: Attempting to initialize Android PointerCaptureHelper using AndroidJavaClass.");
        try
        {
            // Get the AndroidJavaClass instance for the helper
            helperClass = new AndroidJavaClass(HelperClassName);

            if (helperClass != null)
            {
                // Get the current Android Activity context using AndroidJavaObject
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    // Call the static Java initialize method
                    helperClass.CallStatic("initialize", context);
                    Debug.Log("PointerCaptureNativeInterface: Android PointerCaptureHelper initialized.");
                }
            }
            else
            {
                Debug.LogError($"PointerCaptureNativeInterface: Failed to get AndroidJavaClass for {HelperClassName}.");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"PointerCaptureNativeInterface: Exception during initialization: {e.Message}");
            // Ensure helperClass is null if initialization fails
            helperClass = null;
        }
#else
            Debug.Log("PointerCaptureNativeInterface: Android PointerCaptureHelper initialization skipped (not on Android).");
#endif
        }

        // Helper method to check if the helperClass is valid before calling
        private static bool IsHelperAvailable()
        {
            if (helperClass == null)
            {
                return false;
            }
            return true;
        }


        // --- Public methods for Unity to call native methods ---

        public static void beginCapture()
        {
            if (!IsHelperAvailable()) return;
            helperClass.CallStatic("beginCapture");
        }

        public static void endCapture()
        {
            if (!IsHelperAvailable()) return;
            helperClass.CallStatic("endCapture");
        }

        public static bool isPointerCaptured()
        {
            if (!IsHelperAvailable()) return false;
            return helperClass.CallStatic<bool>("isPointerCaptured");
        }

        public static float getLastDx()
        {
            if (!IsHelperAvailable()) return 0f;
            return helperClass.CallStatic<float>("getLastDx");
        }

        public static float getLastDy()
        {
            if (!IsHelperAvailable()) return 0f;
            return helperClass.CallStatic<float>("getLastDy");
        }

        public static int getLastButtonState()
        {
            if (!IsHelperAvailable()) return 0;
            return helperClass.CallStatic<int>("getLastButtonState");
        }

        public static int getLastActionButton()
        {
            if (!IsHelperAvailable()) return 0;
            return helperClass.CallStatic<int>("getLastActionButton");
        }

        public static float getLastVerticalScrollDelta()
        {
            if (!IsHelperAvailable()) return 0f;
            return helperClass.CallStatic<float>("getLastVerticalScrollDelta");
        }

        public static float getLastHorizontalScrollDelta()
        {
            if (!IsHelperAvailable()) return 0f;
            return helperClass.CallStatic<float>("getLastHorizontalScrollDelta");
        }
    }
}