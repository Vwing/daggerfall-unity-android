using System.Collections.Generic;
using UnityEngine;

namespace DaggerfallWorkshop
{
    /// <summary>
    /// Desktop bundles only carry desktop shader programs, which Android can't run, and <see cref="DesktopModConverter"/>
    /// can only convert the OpenGL ones. As assets of a converted mod load, this swaps each of the mod's shaders for the
    /// game's own shader of the same name, where the game has one, and a shader with no Android programs for the game's
    /// shader that's most like it.
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

        // Words in shader names that say how they draw, to choose between similar shaders
        static readonly string[] shaderNameWords = { "Transparent", "Cutout", "Additive", "Particle", "Unlit", "Billboard", "Sprite", "UI" };

        static Dictionary<string, Shader> gameShaders;
        static readonly Dictionary<string, Shader> similarShaders = new Dictionary<string, Shader>();
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

        // Gets the game's shader with the same name as one from a mod, or a similar one if the mod's has no Android programs,
        // or null if it's already the game's or should stay as it is
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

            bool similar = false;
            if (shader == null && !modShader.isSupported)
            {
                if (!similarShaders.TryGetValue(name, out shader))
                {
                    shader = GetSimilarGameShader(modShader);
                    similarShaders[name] = shader;
                }
                similar = shader != null;
            }

            if (loggedShaderNames.Add(name))
            {
                if (similar)
                    Debug.LogWarning($"Converted mod {modTitle} uses shader {name}, which has no Android version; using the game's {shader.name} instead, so its graphics may look different.");
                else if (shader != null)
                    Debug.Log($"Converted mod {modTitle} uses shader {name}; using the game's version (mod's version supported: {modShader.isSupported}).");
                else if (!modShader.isSupported)
                    Debug.LogWarning($"Converted mod {modTitle} uses shader {name}, which has no Android version; its graphics may be missing or pink.");
            }

            return shader;
        }

        // Finds the common game shader with the most of a mod shader's properties, so its materials keep their textures and
        // colors, preferring one that draws in the same render queue and has the same words in its name.
        // Returns null if none has any of its properties.
        static Shader GetSimilarGameShader(Shader modShader)
        {
            var properties = new HashSet<string>();
            int propertyCount = modShader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
                properties.Add(modShader.GetPropertyName(i));

            Shader best = null;
            int bestScore = 0;
            foreach (string candidateName in commonShaderNames)
            {
                Shader candidate;
                if (!gameShaders.TryGetValue(candidateName, out candidate) || candidate == null)
                    continue;

                int matches = 0;
                int candidateCount = candidate.GetPropertyCount();
                for (int i = 0; i < candidateCount; i++)
                {
                    if (properties.Contains(candidate.GetPropertyName(i)))
                        matches++;
                }
                if (matches == 0)
                    continue;

                int score = matches * 2;
                if (GetQueueKind(candidate.renderQueue) == GetQueueKind(modShader.renderQueue))
                    score += 3;
                foreach (string word in shaderNameWords)
                {
                    if (modShader.name.Contains(word) == candidateName.Contains(word))
                        score++;
                }

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        // Opaque, alpha tested or transparent
        static int GetQueueKind(int renderQueue)
        {
            return renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent ? 2 :
                renderQueue >= (int)UnityEngine.Rendering.RenderQueue.AlphaTest ? 1 : 0;
        }
    }
}
