using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Linq;
using UnityEngine.UI;
using NativeFilePickerNamespace;

namespace DaggerfallWorkshop.Game
{
    public class TouchscreenLayoutsManager : MonoBehaviour
    {
        public static TouchscreenLayoutsManager Instance{get; private set;}

        public static event System.Action<string> LayoutLoaded;

        [SerializeField] private Button importLayoutButton;
        [SerializeField] private Button exportLayoutButton;
        [SerializeField] private Button deleteLayoutButton;
        [SerializeField] private Button importTextureButton;
        [SerializeField] private Button createNewButtonButton;
        [SerializeField] private Button deleteSelectedButtonButton;
        [SerializeField] private TMPro.TMP_Dropdown layoutsDropdown;
        [SerializeField] private TMPro.TMP_InputField currentLayoutName;
        [SerializeField] private List<BuiltInSpriteConfig> builtInSprites = new List<BuiltInSpriteConfig>();
        [SerializeField] private List<BuiltInSpriteConfig> builtInKnobSprites = new List<BuiltInSpriteConfig>();

        [Header("Selected Button Options")]
        [SerializeField] private TMPro.TMP_Dropdown actionMappingDropdown;
        [SerializeField] private TMPro.TMP_Dropdown keycodeDropdown;
        [SerializeField] private TMPro.TMP_InputField buttonNameInputField;
        [SerializeField] private TMPro.TMP_Dropdown buttonTypeDropdown;
        [SerializeField] private TMPro.TMP_Dropdown anchorDropdown;
        [SerializeField] private TMPro.TMP_Dropdown labelAnchorDropdown;
        [SerializeField] private TMPro.TMP_Dropdown spriteDropdown;
        [SerializeField] private TMPro.TMP_Dropdown knobSpriteDropdown;
        [SerializeField] private Slider selectedJoystickSensitivitySlider;

        [Header("Assets")]
        [SerializeField] private TextAsset initialDefaultLayout;

        private List<string> cachedSpritePaths = new List<string>();
        private List<string> cachedKnobSpritePaths = new List<string>();
        [System.Serializable]
        public struct BuiltInSpriteConfig
        {
            public string textureName;
            public string spriteName;
        }
        private readonly List<string> imageSearchPatterns = new List<string>(new string[]{"*.png", "*.jpg", "*.jpeg"});

        private Dictionary<string, int> acceptedKeyCodes = new Dictionary<string, int>();

        public static string LayoutsPath { get { return Path.Combine(Paths.PersistentDataPath, "TouchscreenLayouts"); } }
        public static string LastSelectedLayout{get{return PlayerPrefs.GetString("TouchscreenLayoutsManager_LastSelectedLayout", "default-layout");} set {PlayerPrefs.SetString("TouchscreenLayoutsManager_LastSelectedLayout", value);}}

        public TouchscreenLayoutConfiguration CurrentlyLoadedLayout {get{return currentlyLoadedLayout;}}

        private TouchscreenLayoutConfiguration currentlyLoadedLayout;
        private List<string> cachedLayoutNames = new List<string>();

        private void Awake()
        {
            Instance = this;
        }
        private void Start()
        {
            currentLayoutName.text = "default-layout";
            importLayoutButton.onClick.AddListener(ImportNewLayout);
            exportLayoutButton.onClick.AddListener(ExportCurrentLayout);
            deleteLayoutButton.onClick.AddListener(DeleteCurrentLayout);
            importTextureButton.onClick.AddListener(ImportNewButtonTexture);
            createNewButtonButton.onClick.AddListener(CreateNewButtonInLayout);
            deleteSelectedButtonButton.onClick.AddListener(DeleteCurrentlySelectedButtonFromLayout);
            currentLayoutName.onEndEdit.AddListener(AttemptRenamingLayout);
            buttonNameInputField.onEndEdit.AddListener(AttemptRenamingButton);
            layoutsDropdown.onValueChanged.AddListener(OnLayoutsDropdownValueChanged);
            actionMappingDropdown.onValueChanged.AddListener(OnEditControlsDropdownValueChanged);
            keycodeDropdown.onValueChanged.AddListener(OnEditControlsKeyCodeDropdownValueChanged);
            buttonTypeDropdown.onValueChanged.AddListener(OnButtonTypeDropdownValueChanged);
            anchorDropdown.onValueChanged.AddListener(OnButtonAnchorDropdownValueChanged);
            labelAnchorDropdown.onValueChanged.AddListener(OnLabelAnchorDropdownValueChanged);
            selectedJoystickSensitivitySlider.onValueChanged.AddListener(OnSelectedJoystickSensitivityChanged);
            SetupUI();
            TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged += SetupUIBasedOnCurrentlyEditingTouchscreenButton;
            TouchscreenInputManager.Instance.onEditControlsToggled += TouchscreenInputManager_OnEditControlsToggled;
            //Invoke("WriteCurrentLayoutToPath", 1f);
            //Invoke("ImportNewLayout", 1.5f);
        }
        private void OnDestroy()
        {
            if(TouchscreenInputManager.Instance)
                TouchscreenInputManager.Instance.onCurrentlyEditingButtonChanged -= SetupUIBasedOnCurrentlyEditingTouchscreenButton;
        }

        public void SetButtonEnabled(string buttonName, bool enabled)
        {
            int indx = currentlyLoadedLayout.buttons.FindIndex(p => p.Name == buttonName);
            if(indx >= 0){
                currentlyLoadedLayout.buttons[indx].IsEnabled = enabled;
                WriteCurrentLayoutToPath();
            }
        }

        private void ImportNewLayout()
        {
            NativeFilePicker.FilePickedCallback filePickedCallback = new NativeFilePicker.FilePickedCallback(OnLayoutFilePicked);
            NativeFilePicker.PickFile(filePickedCallback, ".zip");
        }
        private void ImportNewButtonTexture()
        {
            NativeFilePicker.FilePickedCallback filePickedCallback = new NativeFilePicker.FilePickedCallback(OnButtonTexturePicked);
            NativeFilePicker.PickFile(filePickedCallback, "image/*");
        }
        private void DeleteCurrentLayout()
        {
            bool isDefaultLayout = currentlyLoadedLayout == null || currentlyLoadedLayout.name == "default-layout";

            void DoDeleteCurrentLayout()
            {
                if(isDefaultLayout) {
                    Directory.Delete(Path.Combine(LayoutsPath, "default-layout"), true);
                    RegenerateDefaultLayoutIfMissing();
                    layoutsDropdown.value = 0;
                    LoadLayoutByName("default-layout");
                } else {
                    Directory.Delete(Path.Combine(LayoutsPath, currentlyLoadedLayout.name), true);
                    int lastSelectedLayoutIndex = layoutsDropdown.options.FindIndex(p => p.text == currentlyLoadedLayout.name);
                    currentlyLoadedLayout = null;
                    UpdateLayoutsDropdown();
                    if(lastSelectedLayoutIndex >= 0 && lastSelectedLayoutIndex < layoutsDropdown.options.Count)
                        layoutsDropdown.value = lastSelectedLayoutIndex;
                    else
                        layoutsDropdown.value = layoutsDropdown.options.Count - 1;
                    LoadLayoutByName(layoutsDropdown.options[layoutsDropdown.value].text);
                }
            }
            if(isDefaultLayout){
                TouchscreenInputManager.Instance.PopupMessage.Open($"Delete the default layout, returning all buttons to their initial values?", DoDeleteCurrentLayout, null, "Yes", "No");
            } else {
                TouchscreenInputManager.Instance.PopupMessage.Open($"Delete the current layout, {currentlyLoadedLayout.name}?", DoDeleteCurrentLayout, null, "Yes", "No");
            }
        }
        private void ExportCurrentLayout()
        {
            // get current layout to export
            TouchscreenLayoutConfiguration exportedConfig = GetCurrentLayoutConfig();

            // create cached folder that we will zip up and export
            string cachePath = Path.Combine(Application.temporaryCachePath, "TouchscreenLayouts");
            if(Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);
            string folderToZipPath = Path.Combine(cachePath, currentlyLoadedLayout.name);
            Directory.CreateDirectory(folderToZipPath);
            Directory.CreateDirectory(Path.Combine(folderToZipPath, "textures"));

            // copy the textures to cache
            for(int i = 0; i < exportedConfig.buttons.Count; ++i){
                if(!exportedConfig.buttons[i].UsesBuiltInTextures){
                    if(File.Exists(exportedConfig.buttons[i].TextureFilePath)){
                        string exportPath = Path.Combine(folderToZipPath, "textures", exportedConfig.buttons[i].TextureFileName);
                        File.Copy(exportedConfig.buttons[i].TextureFilePath, exportPath, true);
                    }
                    if(File.Exists(exportedConfig.buttons[i].KnobTextureFilePath)){
                        string exportPath = Path.Combine(folderToZipPath, "textures", exportedConfig.buttons[i].KnobTextureFileName);
                        File.Copy(exportedConfig.buttons[i].KnobTextureFilePath, exportPath, true);
                    }
                }
            }

            // write the json to cache
            TouchscreenLayoutConfiguration.WriteToPath(exportedConfig, Path.Combine(folderToZipPath, currentlyLoadedLayout.name + ".json"));
            
            // zip it all up
            string zipPath = folderToZipPath.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
            DaggerfallWorkshop.Utility.ZipFileUtils.ZipFile(folderToZipPath, zipPath);
            Directory.Delete(folderToZipPath, true);

            // export via native file picker
            void exportFileCallback(bool success){
                if(success) {
                    Directory.Delete(cachePath, true);
                } else {
                    TouchscreenInputManager.Instance.PopupMessage.Open($"Failed to export layout via native file picker. You can find it at {zipPath}", null, null, "Okay", "", false);
                }
            }
            NativeFilePicker.ExportFile(zipPath, exportFileCallback);
        }
        private void AttemptRenamingButton(string newButtonName)
        {
            TouchscreenButton curButton = TouchscreenInputManager.Instance.CurrentlyEditingButton;
            if(string.IsNullOrEmpty(newButtonName) || newButtonName == curButton.gameObject.name)
                buttonNameInputField.text = curButton.gameObject.name;
            else if(currentlyLoadedLayout.buttons.Any(p => p.Name == newButtonName)) {
                TouchscreenInputManager.Instance.PopupMessage.Open("That button name is already being used in this layout.", null, null, "Okay", "", false);
                buttonNameInputField.text = curButton.gameObject.name;
            } else {
                string oldButtonName = curButton.gameObject.name;
                curButton.gameObject.name = newButtonName;
                int buttIndex = currentlyLoadedLayout.buttons.FindIndex(p => p.Name == oldButtonName);
                currentlyLoadedLayout.buttons[buttIndex].Name = newButtonName;
                WriteCurrentLayoutToPath();
            }
        }

        private void CreateNewButtonInLayout()
        {
            TouchscreenButtonConfiguration newButtonConfig = new("new-button-" + UnityEngine.Random.Range(100000, 999999).ToString(), 
                new Vector2(70, -100), new Vector2(70, 70), TouchscreenButtonType.Button, true, true, "linux_buttons", "button_blank", 
                "", "", InputManager.Actions.Unknown);
            var newButton = TouchscreenButtonEnableDisableManager.Instance.AddButtonFromPool(newButtonConfig);
            WriteCurrentLayoutToPath();
            LoadLayoutByName(currentLayoutName.text);
            TouchscreenInputManager.Instance.EditTouchscreenButton(newButton);
        }
        private void DeleteCurrentlySelectedButtonFromLayout()
        {
            TouchscreenButton curButton = TouchscreenInputManager.Instance.CurrentlyEditingButton;
            if(curButton)
            {
                TouchscreenInputManager.Instance.EditTouchscreenButton(null);
                TouchscreenButtonEnableDisableManager.Instance.ReturnButtonToPool(curButton);
                WriteCurrentLayoutToPath();
                LoadLayoutByName(currentLayoutName.text);
            }
        }
        private void AttemptRenamingLayout(string newLayoutName)
        {
            string oldLayoutName = currentlyLoadedLayout.name;
            if(string.IsNullOrEmpty(newLayoutName) || newLayoutName == currentlyLoadedLayout.name)
                currentLayoutName.text = oldLayoutName;
            else if(layoutsDropdown.options.Any(p => p.text == newLayoutName)) {
                TouchscreenInputManager.Instance.PopupMessage.Open("That layout name is already being used", null, null, "Okay", "", false);
                currentLayoutName.text = oldLayoutName;
            } else {
                currentlyLoadedLayout.name = newLayoutName;
                try{
                    Directory.Move(Path.Combine(LayoutsPath, oldLayoutName), Path.Combine(LayoutsPath, newLayoutName));
                    int layoutDropdownIndex = layoutsDropdown.options.FindIndex(p => p.text == oldLayoutName);
                    layoutsDropdown.options[layoutDropdownIndex].text = newLayoutName;
                    layoutsDropdown.GetComponentInChildren<TMPro.TMP_Text>().text = newLayoutName;
                    TouchscreenLayoutConfiguration.WriteToPath(currentlyLoadedLayout, Path.Combine(LayoutsPath, newLayoutName, newLayoutName + ".json"));
                    File.Delete(Path.Combine(LayoutsPath, newLayoutName, oldLayoutName + ".json"));
                    
                    RegenerateDefaultLayoutIfMissing();
                    UpdateLayoutsDropdown();
                } catch (Exception e){
                    Debug.LogError(e);
                    TouchscreenInputManager.Instance.PopupMessage.Open($"Error renaming layout: {e}", null, null, "Okay", "", false);
                    currentLayoutName.text = oldLayoutName;
                }
            }
        }
        private bool LoadLayoutByName(string layoutName, bool showErrorPopups = false)
        {
            Debug.Log($"TouchscreenLayoutsManager: Loading {layoutName} from {LayoutsPath}");
            static string getDirName(string dirPath) => Path.GetFileName(dirPath.TrimEnd('/', '\\'));
            // get valid layouts (directories in LayoutsPath that contain a .json named same as dir)
            cachedLayoutNames = Directory.GetDirectories(LayoutsPath).Where(p => File.Exists(Path.Combine(p, getDirName(p) + ".json"))).Select(s => getDirName(s)).ToList();
            if(!cachedLayoutNames.Contains(layoutName)){
                if(showErrorPopups)
                    TouchscreenInputManager.Instance.PopupMessage.Open($"Couldn't find a layout file named {layoutName}.json within a {Path.Combine(LayoutsPath, layoutName)} directory", null, null, "Okay", "", false);
                return false;
            } else {
                var layout = TouchscreenLayoutConfiguration.ReadFromPath(Path.Combine(LayoutsPath, layoutName, layoutName + ".json"));
                LoadLayout(layout);
                currentLayoutName.text = layout.name;
                LastSelectedLayout = layout.name;
                SelectDropdownValueForLayoutName(layout.name);
                return true;
            }
        }
        private void SelectDropdownValueForLayoutName(string layoutName)
        {
            int dropdownIndex = layoutsDropdown.options.FindIndex(p => p.text == layoutName);
            if(dropdownIndex >= 0)
                layoutsDropdown.value = dropdownIndex;
            else
                Debug.LogError("Couldn't find layouts dropdown index for " + layoutName);
        }
        public void WriteCurrentLayoutToPath() => WriteLayoutToPath(GetCurrentLayoutConfig());
        public void ReloadCurrentLayout() => LoadLayoutByName(currentlyLoadedLayout.name);
        private void WriteLayoutToPath(TouchscreenLayoutConfiguration layout)
        {
            Debug.Log($"TouchscreenLayoutsManager: Writing {layout.name} to {LayoutsPath}");
            string path = Path.Combine(LayoutsPath, layout.name, layout.name + ".json");
            TouchscreenLayoutConfiguration.WriteToPath(layout, path);
        }
        public void SetupUIBasedOnCurrentlyEditingTouchscreenButton(TouchscreenButton touchscreenButton)
        {
            if (touchscreenButton){
                actionMappingDropdown.interactable = touchscreenButton.CanActionBeEdited;
                actionMappingDropdown.value = (int)touchscreenButton.myAction;
                keycodeDropdown.interactable = touchscreenButton.CanActionBeEdited;
                keycodeDropdown.value = keycodeDropdown.options.FindIndex(p => p.text == touchscreenButton.myKey.ToString());

                var buttonConfig = touchscreenButton.GetCurrentConfiguration();
                buttonNameInputField.text = buttonConfig.Name;
                buttonTypeDropdown.value = buttonTypeDropdown.options.FindIndex(p => p.text == buttonConfig.ButtonType.ToString());
                anchorDropdown.value = anchorDropdown.options.FindIndex(p => p.text == buttonConfig.Anchor.ToString());
                labelAnchorDropdown.value = labelAnchorDropdown.options.FindIndex(p => p.text == buttonConfig.LabelAnchor.ToString());
                spriteDropdown.onValueChanged.RemoveListener(OnSpriteDropdownValueChanged);
                knobSpriteDropdown.onValueChanged.RemoveListener(OnKnobSpriteDropdownChanged);
                SyncSpriteDropdownValueToCurrentButtonSprite(true);
                SyncSpriteDropdownValueToCurrentButtonSprite(false);
                spriteDropdown.onValueChanged.AddListener(OnSpriteDropdownValueChanged);
                knobSpriteDropdown.onValueChanged.AddListener(OnKnobSpriteDropdownChanged);
                //spriteDropdown.value = spriteDropdown.options.FindIndex(p => p.text == buttonConfig.LabelAnchor.ToString());
                //knobSpriteDropdown.value = knobSpriteDropdown.options.FindIndex(p => p.text == buttonConfig.LabelAnchor.ToString());

            }
        }
        private void UpdateLayoutsDropdown()
        {
            layoutsDropdown.ClearOptions();
            // get directories that have a .json under them named the same as the directory
            List<string> layoutsInPath = Directory.GetDirectories(LayoutsPath).Where(p => File.Exists(Path.Combine(p, Path.GetFileName(p.TrimEnd('/', '\\')) + ".json"))).Select(s => s.Split(Path.DirectorySeparatorChar).Last()).ToList();
            Debug.Log(layoutsInPath.Count + "Layouts in Layouts Path: " + LayoutsPath);
            layoutsDropdown.AddOptions(layoutsInPath);
            if(currentlyLoadedLayout != null)
                SelectDropdownValueForLayoutName(currentlyLoadedLayout.name);
        }
        private void SetupUI()
        {
            UpdateLayoutsDropdown();

            // edit controls canvas setup

            // Add button action mapping options
            actionMappingDropdown.ClearOptions();
            List<string> options = new List<string>();
            for (int i = 0; i <= (int)InputManager.Actions.Custom10; ++i)
                options.Add(((InputManager.Actions)i).ToString());
            actionMappingDropdown.AddOptions(options);

            // Add button key mapping options
            IEnumerable<int> allKeyCodes = ((KeyCode[])System.Enum.GetValues(typeof(KeyCode))).Select(s => (int)s);
            keycodeDropdown.ClearOptions();
            foreach (var key in allKeyCodes)
                if (key < (int)KeyCode.Joystick1Button0 && !InputManager.unacceptedAnyKeys.Contains(key))
                    acceptedKeyCodes[((KeyCode)key).ToString()] = key;
            keycodeDropdown.AddOptions(acceptedKeyCodes.Select(s => s.Key).ToList());

            // Add button type options
            buttonTypeDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonType.Drawer; ++i)
                options.Add(((TouchscreenButtonType)i).ToString());
            buttonTypeDropdown.AddOptions(options);

            // Add button anchor mapping options
            anchorDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
                options.Add(((TouchscreenButtonAnchor)i).ToString());
            anchorDropdown.AddOptions(options);

            // Add label anchor mapping options
            labelAnchorDropdown.ClearOptions();
            options.Clear();
            for (int i = 0; i <= (int)TouchscreenButtonAnchor.BottomRight; ++i)
                options.Add(((TouchscreenButtonAnchor)i).ToString());
            labelAnchorDropdown.AddOptions(options);

            // Add sprite options
            LoadSpriteDropdown(true);
            LoadSpriteDropdown(false);
        }

        private void LoadSpriteDropdown(bool isKnob){
            TMPro.TMP_Dropdown dropdown = isKnob ? knobSpriteDropdown : spriteDropdown;
            dropdown.ClearOptions();
            List<BuiltInSpriteConfig> builtIns = isKnob ? builtInKnobSprites : builtInSprites;
            cachedSpritePaths.Clear();
            string curLayoutPath = Path.Combine(LayoutsPath, currentlyLoadedLayout.name);
            cachedSpritePaths.AddRange(imageSearchPatterns.SelectMany(s => Directory.GetFiles(curLayoutPath, s, SearchOption.AllDirectories)).ToList());
            
            dropdown.onValueChanged.RemoveListener(OnKnobSpriteDropdownChanged);
            dropdown.onValueChanged.RemoveListener(OnSpriteDropdownValueChanged);
            List<string> options = new List<string>();
            options.AddRange(builtIns.Select(s => string.IsNullOrEmpty(s.spriteName) ? s.textureName : $"{s.spriteName}"));
            options.AddRange(cachedSpritePaths.Select(s => {
                // string texParentLayout = Path.GetFileName(Path.GetDirectoryName(s.Replace("/textures", "").Replace("\\textures", "")));
                return $"{Path.GetFileNameWithoutExtension(s)}";
            }));
            dropdown.AddOptions(options);
            SyncSpriteDropdownValueToCurrentButtonSprite(isKnob);
            dropdown.onValueChanged.AddListener(isKnob ? OnKnobSpriteDropdownChanged : OnSpriteDropdownValueChanged);
        }
        private void SyncSpriteDropdownValueToCurrentButtonSprite(bool isKnob)
        {
            TMPro.TMP_Dropdown dropdown = isKnob ? knobSpriteDropdown : spriteDropdown;
            if(TouchscreenInputManager.Instance.CurrentlyEditingButton != null){
                string currentlyEditingButtonName = TouchscreenInputManager.Instance.CurrentlyEditingButton.gameObject.name;
                int curButtonIndex = currentlyLoadedLayout.buttons.FindIndex(p => p.Name == currentlyEditingButtonName);
                var curButton = currentlyLoadedLayout.buttons[curButtonIndex];
                if(curButton.UsesBuiltInTextures){
                    dropdown.value = dropdown.options.FindIndex(p => p.text == (isKnob ? curButton.KnobSpriteName : curButton.SpriteName));
                } else {
                    string texName = isKnob ? Path.GetFileNameWithoutExtension(curButton.KnobTextureFileName) : Path.GetFileNameWithoutExtension(curButton.TextureFileName);
                    dropdown.value = dropdown.options.FindLastIndex(p => p.text == texName);
                }
            }
        }
        private void ChangeCurrentButtonSprite(int spriteOptionIndex, bool isKnob)
        {
            List<BuiltInSpriteConfig> builtInSpriteConfigs = isKnob ? builtInKnobSprites : builtInSprites;
            string currentlyEditingButtonName = TouchscreenInputManager.Instance.CurrentlyEditingButton.gameObject.name;
            int curButtonIndex = currentlyLoadedLayout.buttons.FindIndex(p => p.Name == currentlyEditingButtonName);
            if(spriteOptionIndex < builtInSpriteConfigs.Count){
                BuiltInSpriteConfig newSpriteConfig = builtInSpriteConfigs[spriteOptionIndex];
                currentlyLoadedLayout.buttons[curButtonIndex].UsesBuiltInTextures = true;
                if(isKnob){
                    currentlyLoadedLayout.buttons[curButtonIndex].KnobTextureFileName = newSpriteConfig.textureName;
                    currentlyLoadedLayout.buttons[curButtonIndex].KnobSpriteName = newSpriteConfig.spriteName;
                } else {
                    currentlyLoadedLayout.buttons[curButtonIndex].TextureFileName = newSpriteConfig.textureName;
                    currentlyLoadedLayout.buttons[curButtonIndex].SpriteName = newSpriteConfig.spriteName;
                }
            } else {
                spriteOptionIndex -= builtInSpriteConfigs.Count;
                string newSpritePath = cachedSpritePaths[spriteOptionIndex];
                currentlyLoadedLayout.buttons[curButtonIndex].UsesBuiltInTextures = false;
                if(isKnob){
                    currentlyLoadedLayout.buttons[curButtonIndex].KnobTextureFileName = Path.GetFileName(newSpritePath);
                    currentlyLoadedLayout.buttons[curButtonIndex].KnobSpriteName = "";
                } else {
                    currentlyLoadedLayout.buttons[curButtonIndex].TextureFileName = Path.GetFileName(newSpritePath);
                    currentlyLoadedLayout.buttons[curButtonIndex].SpriteName = "";
                }
            }
            TouchscreenInputManager.Instance.CurrentlyEditingButton.ApplyConfiguration(currentlyLoadedLayout.buttons[curButtonIndex]);
        }
        public void LoadLastSelectedOrDefaultLayout()
        {
            if(!Directory.Exists(LayoutsPath))
                Directory.CreateDirectory(LayoutsPath);
            RegenerateDefaultLayoutIfMissing();
            UpdateLayoutsDropdown();
            if(!LoadLayoutByName(LastSelectedLayout, false))
                LoadLayoutByName("default-layout");
        }
        private void RegenerateDefaultLayoutIfMissing()
        {            
            string defaultLayoutPath = Path.Combine(LayoutsPath, "default-layout", "default-layout.json");
            if(!File.Exists(defaultLayoutPath)){
                string initialDefaultLayoutPath =Path.Combine(Paths.PersistentDataPath, "initial-default-layout.json");
                if(!File.Exists(initialDefaultLayoutPath))
                    File.WriteAllText(initialDefaultLayoutPath, initialDefaultLayout.text);
                string defaultLayoutText = File.ReadAllText(initialDefaultLayoutPath);
                Directory.CreateDirectory(Path.Combine(LayoutsPath, "default-layout"));
                File.WriteAllText(defaultLayoutPath, defaultLayoutText);
            }
        }
        public void LoadLayout(TouchscreenLayoutConfiguration layoutConfig)
        {
            if(currentlyLoadedLayout != null && layoutConfig.name != currentlyLoadedLayout.name)
                WriteCurrentLayoutToPath();
            TouchscreenButtonEnableDisableManager.Instance.ReturnAllButtonsToPool();
            TouchscreenInputManager.Instance.SetUIAlpha(layoutConfig.defaultUIAlpha);
            VirtualJoystick.JoystickTapsShouldActivateCenterObject = layoutConfig.screenTapsActivateCenterObject;
            TouchscreenButtonEnableDisableManager.Instance.IsLeftJoystickEnabled = layoutConfig.leftJoystickEnabled;
            TouchscreenButtonEnableDisableManager.Instance.IsRightJoystickEnabled = layoutConfig.rightJoystickEnabled;
            layoutConfig.buttons.Sort(new TouchscreenButtonConfigurationComparer());
            for(int i = 0; i < layoutConfig.buttons.Count; ++i)
            {
                var buttonConfig = layoutConfig.buttons[i];
                buttonConfig.LayoutParentName = layoutConfig.name;
                layoutConfig.buttons[i] = buttonConfig;
                TouchscreenButtonEnableDisableManager.Instance.AddButtonFromPool(buttonConfig);
            }
            // DaggerfallGC.ThrottledUnloadUnusedAssets();

            currentlyLoadedLayout = layoutConfig;

            LayoutLoaded?.Invoke(layoutConfig.name);
        }
        public TouchscreenLayoutConfiguration GetCurrentLayoutConfig()
        {
            string layoutName = name = currentlyLoadedLayout != null ? currentlyLoadedLayout.name : "default-layout";
            var layout = new TouchscreenLayoutConfiguration(){
                name = layoutName,
                defaultUIAlpha = TouchscreenInputManager.Instance.UIAlpha,
                screenTapsActivateCenterObject = VirtualJoystick.JoystickTapsShouldActivateCenterObject,
                leftJoystickEnabled = TouchscreenButtonEnableDisableManager.Instance.IsLeftJoystickEnabled,
                rightJoystickEnabled = TouchscreenButtonEnableDisableManager.Instance.IsRightJoystickEnabled,
                buttons = TouchscreenButtonEnableDisableManager.Instance.GetAllButtons().Select(s => s.GetCurrentConfiguration(layoutName)).ToList()
            };
            layout.buttons.Sort(new TouchscreenButtonConfigurationComparer());
            return layout;
        }
        private void OnLayoutFilePicked(string pickedPath)
        {
            string cachePath = Path.Combine(Application.temporaryCachePath, "TouchscreenLayouts");
            if(Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);
            Directory.CreateDirectory(cachePath);

            string pickedFileNameNoExt = Path.GetFileNameWithoutExtension(pickedPath);
            string extractedPath = Directory.CreateDirectory(Path.Combine(cachePath, pickedFileNameNoExt)).FullName;
            DaggerfallWorkshop.Utility.ZipFileUtils.UnzipFile(pickedPath, extractedPath);

            foreach (string file in Directory.GetFiles(extractedPath, "*.json", SearchOption.AllDirectories))
            {
                string layoutName = Path.GetFileNameWithoutExtension(file);
                string fileParentDir = Path.GetDirectoryName(file);
                if(TouchscreenLayoutConfiguration.ReadFromPath(file) != null)
                {
                    // this is the directory level that we want to copy over to layouts path!
                    void onConfirmOverwrite() {
                        string layoutPath = Path.Combine(LayoutsPath, layoutName);
                        Debug.Log($"{extractedPath}\n\n{fileParentDir}\n\n{file}\n\n{layoutPath}");
                        foreach(string file2 in Directory.GetFiles(extractedPath, "*", SearchOption.AllDirectories)){
                            string newFile2Path = file2.Replace(extractedPath, layoutPath);
                            Directory.CreateDirectory(Path.GetDirectoryName(newFile2Path));
                            File.Copy(file2, newFile2Path, true);
                        }
                    }
                    // but we'd better check if it already exists, and notify the user if so
                    if(Directory.Exists(Path.Combine(LayoutsPath, layoutName)))
                        TouchscreenInputManager.Instance.PopupMessage.Open("This layout name already exists! Overwrite the layout?", onConfirmOverwrite, null, "Overwrite", "Cancel");
                    else
                        onConfirmOverwrite();

                    // we can delete the cache path now
                    Directory.Delete(cachePath, true);

                    // load the new layout
                    UpdateLayoutsDropdown();
                    LoadLayoutByName(layoutName);
                    return; // We're done. Return early
                }
            }
            TouchscreenInputManager.Instance.PopupMessage.Open("This doesn't seem to be a valid layout file. Layout must be a zip file containing a '.json' named the same name (i.e. 'layout1.zip', containing 'layout1.json')", null, null, "Okay", "", true);
            Directory.Delete(cachePath, true);
        }
        private void OnButtonTexturePicked(string path)
        {
            string fileName = Path.GetFileName(path);
            string texturesPath = Path.Combine(LayoutsPath, currentlyLoadedLayout.name, "textures");
            string outPath = Path.Combine(texturesPath, fileName);
            Directory.CreateDirectory(texturesPath);
            void onConfirmOverwrite(){
                bool didFileExist = File.Exists(outPath);
                File.Copy(path, outPath, true);
                if(didFileExist){
                    // reload all buttons using that texture
                    LoadLayout(currentlyLoadedLayout);
                } else {
                    // add file to dropdown
                    LoadSpriteDropdown(false);
                    LoadSpriteDropdown(true);
                }
            }
            if(File.Exists(outPath)){
                TouchscreenInputManager.Instance.PopupMessage.Open("Texture already exists. Overwrite it with this new texture?", onConfirmOverwrite, null, "Overwrite", "Cancel");
            } else {
                onConfirmOverwrite();
            }
        }
        private void OnLayoutsDropdownValueChanged(int newVal) => LoadLayoutByName(layoutsDropdown.options[newVal].text);
        private void OnSpriteDropdownValueChanged(int newVal)
        {
            ChangeCurrentButtonSprite(newVal, false);

            WriteCurrentLayoutToPath();
        }
        private void OnKnobSpriteDropdownChanged(int newVal)
        {
            ChangeCurrentButtonSprite(newVal, true);

            WriteCurrentLayoutToPath();
        }
        private void OnEditControlsDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.myAction = (InputManager.Actions)newVal;

                WriteCurrentLayoutToPath();
            }
        }
        private void OnEditControlsKeyCodeDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                KeyCode newKey = (KeyCode)acceptedKeyCodes[keycodeDropdown.options[newVal].text];
                TouchscreenInputManager.Instance.CurrentlyEditingButton.myKey = newKey;

                WriteCurrentLayoutToPath();
            }
        }
        private void OnButtonTypeDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetButtonType((TouchscreenButtonType)newVal);

                WriteCurrentLayoutToPath();
            }
        }
        private void OnButtonAnchorDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetButtonAnchor((TouchscreenButtonAnchor)newVal);

                WriteCurrentLayoutToPath();
            }
        }
        private void OnLabelAnchorDropdownValueChanged(int newVal)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetLabelAnchor((TouchscreenButtonAnchor)newVal);

                WriteCurrentLayoutToPath();
            }
        }
        private void OnSelectedJoystickSensitivityChanged(float value)
        {
            if (TouchscreenInputManager.Instance.CurrentlyEditingButton)
            {
                TouchscreenInputManager.Instance.CurrentlyEditingButton.SetJoystickSensitivity(value);

                WriteCurrentLayoutToPath();
            }
        }
        private void TouchscreenInputManager_OnEditControlsToggled(bool isOn)
        {
            if(!isOn && currentlyLoadedLayout != null)
                WriteCurrentLayoutToPath();
        }
    }
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
            if (!File.Exists(path))
            {
                Debug.LogError($"TouchscreenButtonSerializable file path {path} does not exist");
                return null;
            }
            return Deserialize(File.ReadAllText(path));
        }
        public static void WriteToPath(TouchscreenLayoutConfiguration layout, string path)
        {
            try
            {
                File.WriteAllText(path, Serialize(layout));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write button {layout} to path {path} due to error {e}");
            }
        }
        public static TouchscreenLayoutConfiguration Deserialize(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<TouchscreenLayoutConfiguration>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to deserialize json string into a TouchscreenLayoutConfiguration object due to error {e}\n\nJSON contents:\n{json}");
                return null;
            }
        }
        public static string Serialize(TouchscreenLayoutConfiguration layout)
        {
            try
            {
                // List<TouchscreenButtonConfiguration> prevButtons = layout.buttons.ToList();
                // layout.buttons.ForEach(p => {
                //     p.TexturePath = Path.GetFileName(p.TexturePath);
                //     p.KnobTexturePath = Path.GetFileName(p.KnobTexturePath);
                // });
                // layout.buttons = prevButtons;
                string serializedLayout = JsonConvert.SerializeObject(layout, Formatting.Indented);
                return serializedLayout;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to serialize layout {layout.name} due to error {e}");
                return "";
            }
        }

    }
}