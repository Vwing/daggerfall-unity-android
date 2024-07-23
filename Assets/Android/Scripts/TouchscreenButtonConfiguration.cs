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
        [SerializeField] private string name;
        [SerializeField] private string buttonType;
        [SerializeField] private bool canButtonBeEdited;
        [SerializeField] private bool canButtonBeRemoved;
        [SerializeField] private bool canButtonBeResized;
        [SerializeField] private float defaultPositionX;
        [SerializeField] private float defaultPositionY;
        [SerializeField] private float positionX;
        [SerializeField] private float positionY;
        [SerializeField] private float defaultScaleX;
        [SerializeField] private float defaultScaleY;
        [SerializeField] private float scaleX;
        [SerializeField] private float scaleY;
        [SerializeField] private string anchor;
        [SerializeField] private string labelAnchor;
        [SerializeField] private string defaultActionMapping;
        [SerializeField] private string defaultKeyCodeMapping;
        [SerializeField] private string actionMapping;
        [SerializeField] private string keyCodeMapping;
        [SerializeField] private bool usesBuiltInTextures;
        [SerializeField] private string texturePath;
        [SerializeField] private string spriteName;
        [SerializeField] private string knobTexturePath;
        [SerializeField] private string knobSpriteName;
        [SerializeField] private string buttonsInDrawer;

        public string Name {get{return name;} set{name = value;}}
        public TouchscreenButtonType ButtonType 
        {
            get { return (TouchscreenButtonType)Enum.Parse(typeof(TouchscreenButtonType), buttonType); } 
            set { buttonType = value.ToString(); } 
        }
        public bool CanButtonBeEdited { get{return canButtonBeEdited;} set{ canButtonBeEdited = value;}}
        public bool CanButtonBeRemoved { get{return canButtonBeRemoved;} set{ canButtonBeRemoved = value;}}
        public bool CanButtonBeResized {get {return canButtonBeResized; } set {canButtonBeResized = value; }}
        public Vector2 DefaultPosition{ get { return new Vector2(defaultPositionX, defaultPositionY); } set { defaultPositionX = value.x; defaultPositionY = value.y;} }
        public Vector2 Position{ get { return new Vector2(positionX, positionY); } set { positionX = value.x; positionY = value.y;} }
        public Vector2 DefaultScale { get { return new Vector2(defaultScaleX, defaultScaleY); } set { defaultScaleX = value.x; defaultScaleY = value.y;} }
        public Vector2 Scale { get { return new Vector2(scaleX, scaleY); } set { scaleX = value.x; scaleY = value.y;} }
        public TouchscreenButtonAnchor Anchor {
            get { return (TouchscreenButtonAnchor)Enum.Parse(typeof(TouchscreenButtonAnchor), anchor); }  
            set{ anchor = value.ToString(); } 
        }
        public TouchscreenButtonAnchor LabelAnchor {
            get { return (TouchscreenButtonAnchor)Enum.Parse(typeof(TouchscreenButtonAnchor), labelAnchor); } 
            set { labelAnchor = value.ToString(); } 
        }
        public InputManager.Actions DefaultActionMapping {
            get { return (InputManager.Actions)Enum.Parse(typeof(InputManager.Actions), defaultActionMapping); }  
            set { defaultActionMapping = value.ToString(); } 
        }
        public KeyCode DefaultKeyCodeMapping {
            get { return (KeyCode)Enum.Parse(typeof(KeyCode), defaultKeyCodeMapping); }
            set { defaultKeyCodeMapping = value.ToString(); } 
        }
        public InputManager.Actions ActionMapping {
            get { return (InputManager.Actions)Enum.Parse(typeof(InputManager.Actions), actionMapping); } 
            set { actionMapping = value.ToString(); } 
        }
        public KeyCode KeyCodeMapping {
            get { return (KeyCode)Enum.Parse(typeof(KeyCode), keyCodeMapping); } 
            set { keyCodeMapping = value.ToString(); } 
        }
        public bool UsesBuiltInTextures {get{return usesBuiltInTextures;} set{usesBuiltInTextures = value;}}
        public string TexturePath {get{return texturePath;} set{texturePath = value;}}
        public string SpriteName {get{return spriteName;} set{ spriteName = value;}}
        public string KnobTexturePath {get{return knobTexturePath;} set{knobTexturePath = value;}}
        public string KnobSpriteName {get{return knobSpriteName;} set{ knobSpriteName = value;}}
        public List<string> ButtonsInDrawer {
            get { return buttonsInDrawer.Split(new char[]{',', ' '}).ToList(); } 
            set { buttonsInDrawer = value == null ? "" : string.Join(',', value); }
        }

        public TouchscreenButtonConfiguration(string name, Vector2 defaultPosition, Vector2 defaultScale, 
            TouchscreenButtonType buttonType = TouchscreenButtonType.Button, bool usesBuiltInTexture = true, string texturePath = "knob", 
            string spriteName = "", string knobTexturePath = "", string knobSpriteName = "",
            InputManager.Actions defaultActionMapping = InputManager.Actions.Unknown, KeyCode defaultKeyCodeMapping = KeyCode.None, 
            TouchscreenButtonAnchor anchor = TouchscreenButtonAnchor.MiddleMiddle, TouchscreenButtonAnchor labelAnchor = TouchscreenButtonAnchor.TopMiddle, 
            bool canButtonBeEdited = true, bool canButtonBeRemoved = true, bool canButtonBeResized = true, List<string> buttonsInDrawer = null)
        {
            this.Name = name;
            this.ButtonType = buttonType;
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
            this.TexturePath = texturePath;
            this.SpriteName = spriteName;
            this.KnobTexturePath = knobTexturePath;
            this.KnobSpriteName = knobSpriteName;
            this.ButtonsInDrawer = buttonsInDrawer;
        }
        public Sprite LoadSprite(bool isKnob = false)
        {
            string texPath = isKnob ? KnobTexturePath : TexturePath;
            string spriteName = isKnob ? KnobSpriteName : SpriteName;
            if(UsesBuiltInTextures){
                if(!string.IsNullOrEmpty(spriteName)){
                    return Resources.LoadAll<Sprite>(texPath).FirstOrDefault(p => p.name == spriteName);
                } else {
                    return Resources.Load<Sprite>(texPath);
                }
            } else {
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
            if(!File.Exists(path))
            {
                Debug.LogError($"TouchscreenButtonSerializable file path {path} does not exist");
                return null;
            }
            return Deserialize(File.ReadAllText(path));
        }
        public static void WriteToPath(TouchscreenButtonConfiguration button, string path)
        {
            try{
                File.WriteAllText(path, Serialize(button));
            } catch (Exception e) {
                Debug.LogError($"Failed to write button {button} to path {path} due to error {e}");
            }
        }
        public static TouchscreenButtonConfiguration Deserialize(string json)
        {
            try {
                return JsonConvert.DeserializeObject<TouchscreenButtonConfiguration>(json);
            } catch (Exception e) {
                Debug.LogError($"Failed to deserialize json string into a TouchscreenButtonSerializable object due to error {e}\n\nJSON contents:\n{json}");
                return null;
            }
        }
        public static string Serialize(TouchscreenButtonConfiguration button)
        {
            try{
                return JsonConvert.SerializeObject(button);
            } catch (Exception e) {
                Debug.LogError($"Failed to serialize button {button.name} due to error {e}");
                return "";
            }
        }
    }
}