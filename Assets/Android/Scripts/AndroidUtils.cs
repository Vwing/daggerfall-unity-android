using UnityEngine;
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
