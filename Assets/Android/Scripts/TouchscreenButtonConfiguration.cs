using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Linq;

namespace DaggerfallWorkshop.Game
{
    [System.Serializable]
    public class TouchscreenButtonConfiguration
    {
        [SerializeField][JsonProperty] private string name;
        [SerializeField][JsonProperty] private string buttonType;
        [SerializeField][JsonProperty] private bool defaultIsEnabled;
        [SerializeField][JsonProperty] private bool isEnabled;
        [SerializeField][JsonProperty] private bool canButtonBeEdited;
        [SerializeField][JsonProperty] private bool canButtonBeRemoved;
        [SerializeField][JsonProperty] private bool canButtonBeResized;
        [SerializeField][JsonProperty] private float defaultPositionX;
        [SerializeField][JsonProperty] private float defaultPositionY;
        [SerializeField][JsonProperty] private float positionX;
        [SerializeField][JsonProperty] private float positionY;
        [SerializeField][JsonProperty] private float defaultScaleX;
        [SerializeField][JsonProperty] private float defaultScaleY;
        [SerializeField][JsonProperty] private float scaleX;
        [SerializeField][JsonProperty] private float scaleY;
        [SerializeField][JsonProperty] private string anchor;
        [SerializeField][JsonProperty] private string labelAnchor;
        [SerializeField][JsonProperty] private string defaultActionMapping;
        [SerializeField][JsonProperty] private string defaultKeyCodeMapping;
        [SerializeField][JsonProperty] private string actionMapping;
        [SerializeField][JsonProperty] private string keyCodeMapping;
        [SerializeField][JsonProperty] private bool usesBuiltInTextures;
        [SerializeField][JsonProperty] private string textureFileName;
        [SerializeField][JsonProperty] private string spriteName;
        [SerializeField][JsonProperty] private string knobTextureFileName;
        [SerializeField][JsonProperty] private string knobSpriteName;
        [SerializeField][JsonProperty] private List<string> buttonsInDrawer;
        [SerializeField][JsonProperty] private string text;
        [SerializeField][JsonProperty] private float joystickSensitivity;
        [SerializeField][JsonProperty] private bool isToggleForEditOnScreenControls;


        [JsonIgnore] public string Name { get { return name; } set { name = value; } }
        [JsonIgnore] public string Text { get { return text; } set { text = value; } }
        [JsonIgnore] public bool IsToggleForEditOnScreenControls { get { return isToggleForEditOnScreenControls; } set { isToggleForEditOnScreenControls = value; } }
        [JsonIgnore] public float JoystickSensitivity { get { return joystickSensitivity; } set { joystickSensitivity = value; } }
        [JsonIgnore]
        public TouchscreenButtonType ButtonType
        {
            get { return (TouchscreenButtonType)Enum.Parse(typeof(TouchscreenButtonType), buttonType); }
            set { buttonType = value.ToString(); }
        }
        [JsonIgnore] public bool DefaultIsEnabled { get { return defaultIsEnabled; } set { defaultIsEnabled = value; } }
        [JsonIgnore] public bool IsEnabled { get { return isEnabled; } set { isEnabled = value; } }
        [JsonIgnore] public bool CanButtonBeEdited { get { return canButtonBeEdited; } set { canButtonBeEdited = value; } }
        [JsonIgnore] public bool CanButtonBeRemoved { get { return canButtonBeRemoved; } set { canButtonBeRemoved = value; } }
        [JsonIgnore] public bool CanButtonBeResized { get { return canButtonBeResized; } set { canButtonBeResized = value; } }
        [JsonIgnore] public Vector2 DefaultPosition { get { return new Vector2(defaultPositionX, defaultPositionY); } set { defaultPositionX = value.x; defaultPositionY = value.y; } }
        [JsonIgnore] public Vector2 Position { get { return new Vector2(positionX, positionY); } set { positionX = value.x; positionY = value.y; } }
        [JsonIgnore] public Vector2 DefaultScale { get { return new Vector2(defaultScaleX, defaultScaleY); } set { defaultScaleX = value.x; defaultScaleY = value.y; } }
        [JsonIgnore] public Vector2 Scale { get { return new Vector2(scaleX, scaleY); } set { scaleX = value.x; scaleY = value.y; } }
        [JsonIgnore]
        public TouchscreenButtonAnchor Anchor
        {
            get { return (TouchscreenButtonAnchor)Enum.Parse(typeof(TouchscreenButtonAnchor), anchor); }
            set { anchor = value.ToString(); }
        }
        [JsonIgnore] public TouchscreenButtonAnchor LabelAnchor
        {
            get { return (TouchscreenButtonAnchor)Enum.Parse(typeof(TouchscreenButtonAnchor), labelAnchor); }
            set { labelAnchor = value.ToString(); }
        }
        [JsonIgnore] public InputManager.Actions DefaultActionMapping
        {
            get { return (InputManager.Actions)Enum.Parse(typeof(InputManager.Actions), defaultActionMapping); }
            set { defaultActionMapping = value.ToString(); }
        }
        [JsonIgnore] public KeyCode DefaultKeyCodeMapping
        {
            get { return (KeyCode)Enum.Parse(typeof(KeyCode), defaultKeyCodeMapping); }
            set { defaultKeyCodeMapping = value.ToString(); }
        }
        [JsonIgnore] public InputManager.Actions ActionMapping
        {
            get { return (InputManager.Actions)Enum.Parse(typeof(InputManager.Actions), actionMapping); }
            set { actionMapping = value.ToString(); }
        }
        [JsonIgnore] public KeyCode KeyCodeMapping
        {
            get { return (KeyCode)Enum.Parse(typeof(KeyCode), keyCodeMapping); }
            set { keyCodeMapping = value.ToString(); }
        }
        [JsonIgnore] public bool UsesBuiltInTextures { get { return usesBuiltInTextures; } set { usesBuiltInTextures = value; } }
        [JsonIgnore] public string TextureFileName { get { return textureFileName; } set { textureFileName = value; } }
        [JsonIgnore] public string SpriteName { get { return spriteName; } set { spriteName = value; } }
        [JsonIgnore] public string KnobTextureFileName { get { return knobTextureFileName; } set { knobTextureFileName = value; } }
        [JsonIgnore] public string KnobSpriteName { get { return knobSpriteName; } set { knobSpriteName = value; } }

        private string _layoutParentName;
        [JsonIgnore] public string LayoutParentName
        { 
            get {return _layoutParentName ?? TouchscreenLayoutsManager.Instance?.CurrentlyLoadedLayout?.name ?? "default-layout"; } // this is hacky
            set {_layoutParentName = value;} 
        }
        [JsonIgnore] public string TextureFilePath { get {return UsesBuiltInTextures ? textureFileName : Path.Combine(Paths.LayoutsPath, LayoutParentName, "textures", textureFileName);}}
        [JsonIgnore] public string KnobTextureFilePath { get {return UsesBuiltInTextures ? knobTextureFileName : Path.Combine(Paths.LayoutsPath, LayoutParentName, "textures", knobTextureFileName);} }
        [JsonIgnore] public List<string> ButtonsInDrawer
        {
            get { return buttonsInDrawer; }
            set { buttonsInDrawer = value ?? new List<string>(); }
        }

        public TouchscreenButtonConfiguration(string name, Vector2 defaultPosition, Vector2 defaultScale,
            TouchscreenButtonType buttonType = TouchscreenButtonType.Button, bool defaultIsEnabled = true, bool usesBuiltInTexture = true,
            string textureFileName = "linux_buttons", string spriteName = "button_blank", string knobTextureFileName = "", string knobSpriteName = "",
            InputManager.Actions defaultActionMapping = InputManager.Actions.Unknown, KeyCode defaultKeyCodeMapping = KeyCode.None,
            TouchscreenButtonAnchor anchor = TouchscreenButtonAnchor.MiddleMiddle, TouchscreenButtonAnchor labelAnchor = TouchscreenButtonAnchor.TopMiddle,
            bool canButtonBeEdited = true, bool canButtonBeRemoved = true, bool canButtonBeResized = true, List<string> buttonsInDrawer = null,
            string text = "", bool isToggleForEditOnScreenControls = false, string layoutParentName = "")
        {
            this.Name = name;
            this.ButtonType = buttonType;
            this.DefaultIsEnabled = this.IsEnabled = defaultIsEnabled;
            this.CanButtonBeEdited = canButtonBeEdited;
            this.CanButtonBeRemoved = canButtonBeRemoved;
            this.CanButtonBeResized = canButtonBeResized;
            this.DefaultPosition = this.Position = defaultPosition;
            this.DefaultScale = this.Scale = defaultScale;
            this.Anchor = anchor;
            this.LabelAnchor = labelAnchor;
            this.DefaultActionMapping = this.ActionMapping = defaultActionMapping;
            this.DefaultKeyCodeMapping = this.KeyCodeMapping = defaultKeyCodeMapping;
            this.UsesBuiltInTextures = usesBuiltInTexture;
            this.TextureFileName = textureFileName;
            this.SpriteName = spriteName;
            this.KnobTextureFileName = knobTextureFileName;
            this.KnobSpriteName = knobSpriteName;
            this.ButtonsInDrawer = buttonsInDrawer;
            this.Text = text;
            this.IsToggleForEditOnScreenControls = isToggleForEditOnScreenControls;
            this.LayoutParentName = layoutParentName;
        }
        public Sprite LoadSprite(bool isKnob = false)
        {
            string texPath;
            string spriteName;
            try{
                texPath = isKnob ? KnobTextureFilePath : TextureFilePath;
                spriteName = isKnob ? KnobSpriteName : SpriteName;
            } catch (Exception e) {
                // Path.Combine(Paths.LayoutsPath, LayoutParentName, "textures", textureFileName)
                Debug.LogError($"Combined path: {Paths.LayoutsPath} {LayoutParentName} textures {textureFileName}");
                throw e;
            }
            if (UsesBuiltInTextures)
            {
                if (!string.IsNullOrEmpty(spriteName))
                {
                    try{
                        return Resources.LoadAll<Sprite>(texPath).FirstOrDefault(p => p.name == spriteName);
                    } catch (Exception e){
                        Debug.LogError($"{texPath}: {e}");
                        return null;
                    }
                }
                else
                {
                    return Resources.Load<Sprite>(texPath);
                }
            }
            else
            {
                if (File.Exists(texPath))
                {
                    byte[] fileData = File.ReadAllBytes(texPath);
                    Texture2D texture = new Texture2D(0, 0);
                    if (texture.LoadImage(fileData))
                    {
                        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    }
                    else
                    {
                        Debug.LogError($"Failed to load image from path: {texPath}");
                        return null;
                    }
                }
                else
                {
                    Debug.LogError($"Texture file not found at path: {texPath}");
                    return null;
                }
            }
        }
        public static TouchscreenButtonConfiguration ReadFromPath(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"TouchscreenButtonSerializable file path {path} does not exist");
                return null;
            }
            return Deserialize(File.ReadAllText(path));
        }
        public static void WriteToPath(TouchscreenButtonConfiguration button, string path)
        {
            try
            {
                File.WriteAllText(path, Serialize(button));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to write button {button} to path {path} due to error {e}");
            }
        }
        public static TouchscreenButtonConfiguration Deserialize(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<TouchscreenButtonConfiguration>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to deserialize json string into a TouchscreenButtonSerializable object due to error {e}\n\nJSON contents:\n{json}");
                return null;
            }
        }
        public static string Serialize(TouchscreenButtonConfiguration button)
        {
            try
            {
                return JsonConvert.SerializeObject(button, Formatting.Indented);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to serialize button {button.name} due to error {e}");
                return "";
            }
        }
    }

    public class TouchscreenButtonConfigurationComparer : IComparer<TouchscreenButtonConfiguration>
    {
        public int Compare(TouchscreenButtonConfiguration a, TouchscreenButtonConfiguration b)
        {
            // First, sort by button type to ensure Drawers are at the end
            int typeComparison = a.ButtonType.CompareTo(b.ButtonType);
            if (typeComparison != 0)
            {
                return typeComparison;
            }

            // For Drawer buttons, check if one contains the other
            if (a.ButtonType == TouchscreenButtonType.Drawer && b.ButtonType == TouchscreenButtonType.Drawer)
            {
                bool aContainsB = a.ButtonsInDrawer.Contains(b.Name);
                bool bContainsA = b.ButtonsInDrawer.Contains(a.Name);
                if (aContainsB && !bContainsA)
                {
                    return 1;
                }
                else if (!aContainsB && bContainsA)
                {
                    return -1;
                }
            }
            return 0;
        }
    }
}