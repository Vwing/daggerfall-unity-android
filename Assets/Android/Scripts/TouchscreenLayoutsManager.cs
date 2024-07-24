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
        private List<TouchscreenLayoutConfiguration> loadedLayouts = new List<TouchscreenLayoutConfiguration>();
        private void Awake()
        {
            Instance = this;
        }
#if UNITY_EDITOR
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1f);
            var layout = GetCurrentLayoutConfig();
            string path = Path.Combine(Paths.PersistentDataPath, layout.name + ".json");
            TouchscreenLayoutConfiguration.WriteToPath(layout, path);
            string buttonPath = Path.Combine(Paths.PersistentDataPath, layout.buttons[0].Name + ".json");
            TouchscreenButtonConfiguration.WriteToPath(layout.buttons[0], buttonPath);
        }
#endif
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
    }
}