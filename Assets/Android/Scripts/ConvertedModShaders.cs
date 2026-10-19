using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Desktop bundles only carry desktop shader programs, which Android can't run. As assets of a converted mod load,
    /// this swaps each of the mod's shaders for the game's own shader of the same name, where the game has one.
    /// </summary>
    public static class ConvertedModShaders
    {
        // Looked up before converted mods load any assets, since Shader.Find may return a mod's copy of a shader once it's loaded
        static readonly string[] commonShaderNames =
        {
            MaterialReader._StandardShaderName,
            MaterialReader._DaggerfallDefaultShaderName,
            MaterialReader._DaggerfallTilemapShaderName,
            MaterialReader._DaggerfallTilemapTextureArrayShaderName,
            MaterialReader._DaggerfallBillboardShaderName,
            MaterialReader._DaggerfallBillboardBatchShaderName,
            MaterialReader._DaggerfallBillboardBatchNoShadowsShaderName,
            MaterialReader._DaggerfallGhostShaderName,
            "Unlit/Texture",
            "Unlit/Transparent",
            "Unlit/Transparent Cutout",
            "Unlit/Color",
            "Sprites/Default",
            "UI/Default",
            "Legacy Shaders/Diffuse",
            "Legacy Shaders/Transparent/Diffuse",
            "Legacy Shaders/Transparent/Cutout/Diffuse",
            "Legacy Shaders/VertexLit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Legacy Shaders/Particles/Additive",
            "Particles/Standard Unlit",
            "Mobile/Diffuse",
        };

        static Dictionary<string, Shader> gameShaders;
        static readonly HashSet<string> loggedShaderNames = new HashSet<string>();

        /// <summary>
        /// Looks up the game's common shaders. Call before a converted mod with shaders loads any assets.
        /// </summary>
        public static void FindGameShaders()
        {
            if (gameShaders != null)
                return;

            gameShaders = new Dictionary<string, Shader>();
            foreach (string name in commonShaderNames)
            {
                Shader shader = Shader.Find(name);
                if (shader != null && shader.isSupported)
                    gameShaders[name] = shader;
            }
        }

        /// <summary>
        /// Swaps the shaders of a material, of the renderers in a GameObject, or a shader itself, for the game's own.
        /// </summary>
        /// <returns>The asset, or the game's shader if the asset is a shader the game has.</returns>
        public static Object Replace(Object asset, string modTitle)
        {
            if (asset is Shader shader)
                return GetGameShader(shader, modTitle) ?? shader;

            if (asset is Material material)
            {
                ReplaceShader(material, modTitle);
            }
            else if (asset is GameObject gameObject)
            {
                foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material sharedMaterial in renderer.sharedMaterials)
                        ReplaceShader(sharedMaterial, modTitle);
                }
            }

            return asset;
        }

        static void ReplaceShader(Material material, string modTitle)
        {
            if (material == null)
                return;

            Shader shader = GetGameShader(material.shader, modTitle);
            if (shader != null)
                material.shader = shader;
        }

        // Gets the game's shader with the same name as one from a mod, or null if it's already the game's or the game has none
        static Shader GetGameShader(Shader modShader, string modTitle)
        {
            if (modShader == null)
                return null;

            FindGameShaders();
            string name = modShader.name;
            Shader shader;
            if (!gameShaders.TryGetValue(name, out shader))
            {
                shader = Shader.Find(name);
                if (shader == modShader || shader != null && !shader.isSupported)
                    shader = null;
                gameShaders[name] = shader;
            }

            if (shader == modShader)
                return null;

            if (loggedShaderNames.Add(name))
            {
                if (shader != null)
                    Debug.Log($"Converted mod {modTitle} uses shader {name}; using the game's version (mod's version supported: {modShader.isSupported}).");
                else if (!modShader.isSupported)
                    Debug.LogWarning($"Converted mod {modTitle} uses shader {name}, which has no Android version; its graphics may be missing or pink.");
            }

            return shader;
        }
    }
}
