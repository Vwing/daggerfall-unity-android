using UnityEngine;
using System;
using System.Collections;

namespace DaggerfallWorkshop
{
    public static class AndroidUtils
    {
        // gotten from Tamaya's stackoverflow answer at https://stackoverflow.com/a/70151431
        public static void RestartAndroid()
        {
#if UNITY_ANDROID
            if (Application.isEditor) return;

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                const int kIntent_FLAG_ACTIVITY_CLEAR_TASK = 0x00008000;
                const int kIntent_FLAG_ACTIVITY_NEW_TASK = 0x10000000;

                var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var pm = currentActivity.Call<AndroidJavaObject>("getPackageManager");
                var intent = pm.Call<AndroidJavaObject>("getLaunchIntentForPackage", Application.identifier);

                intent.Call<AndroidJavaObject>("setFlags", kIntent_FLAG_ACTIVITY_NEW_TASK | kIntent_FLAG_ACTIVITY_CLEAR_TASK);
                currentActivity.Call("startActivity", intent);
                currentActivity.Call("finish");
                var process = new AndroidJavaClass("android.os.Process");
                int pid = process.CallStatic<int>("myPid");
                process.CallStatic("killProcess", pid);
            }
#endif
        }

        private const string folderPickerCallbackObjectName = "AndroidFolderPickerCallback";
        private static Action<string> folderPickerCallback;
        private static FolderPickerCallbackReceiver folderPickerReceiver;

        public static void PickFolder(Action<string> folderPickedCallback)
        {
            folderPickerCallback = folderPickedCallback;

#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFolderPanel("Select data folder", "", "");
            InvokeFolderPickerCallback(string.IsNullOrEmpty(path) ? null : path);
#elif UNITY_ANDROID
            if (Application.isEditor)
            {
                InvokeFolderPickerCallback(null);
                return;
            }

            EnsureFolderPickerReceiver();
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var picker = new AndroidJavaClass("com.dfworkshop.daggerfallunityandroid.FolderPicker"))
            {
                AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                picker.CallStatic("pickFolder", currentActivity, folderPickerCallbackObjectName, "OnFolderPicked");
            }
#else
            InvokeFolderPickerCallback(null);
#endif
        }

        public static bool HasAllFilesAccess()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var picker = new AndroidJavaClass("com.dfworkshop.daggerfallunityandroid.FolderPicker"))
            {
                return picker.CallStatic<bool>("hasAllFilesAccess");
            }
#else
            return true;
#endif
        }

        public static void OpenAllFilesAccessSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var picker = new AndroidJavaClass("com.dfworkshop.daggerfallunityandroid.FolderPicker"))
            {
                AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                picker.CallStatic("openAllFilesAccessSettings", currentActivity);
            }
#endif
        }

        private static void EnsureFolderPickerReceiver()
        {
            if (folderPickerReceiver != null)
                return;

            GameObject go = new GameObject(folderPickerCallbackObjectName);
            UnityEngine.Object.DontDestroyOnLoad(go);
            folderPickerReceiver = go.AddComponent<FolderPickerCallbackReceiver>();
        }

        private static void InvokeFolderPickerCallback(string path)
        {
            Action<string> pickedCallback = folderPickerCallback;
            folderPickerCallback = null;
            if (pickedCallback != null)
                pickedCallback(path);
        }

        private class FolderPickerCallbackReceiver : MonoBehaviour
        {
            public void OnFolderPicked(string path)
            {
                InvokeFolderPickerCallback(string.IsNullOrEmpty(path) ? null : path);
            }
        }

        public enum ApplicationRunMode
        {
            Device,
            Editor,
            Simulator
        }
        public static bool IsRunningInSimulator => CurrentApplicationRunMode == ApplicationRunMode.Simulator;
        public static ApplicationRunMode CurrentApplicationRunMode
        {
            get
            {
#if UNITY_EDITOR
                return UnityEngine.Device.Application.isEditor && !UnityEngine.Device.Application.isMobilePlatform ? ApplicationRunMode.Editor : ApplicationRunMode.Simulator;
#else
      return ApplicationRunMode.Device;
#endif
            }
        }
    }
}
