// ============================================================================
// NPCCommonUtils.cs - shared NPC utilities
// ============================================================================

using System;
using Duckov.UI;
using UnityEngine;

namespace BossRush.Utils
{
    public static class NPCCommonUtils
    {
        private static Shader cachedCharacterShader;
        private static bool characterShaderResolved;

        /// <summary>
        /// Replaces fallback standard shaders on spawned NPC renderers.
        /// </summary>
        public static void FixShaders(GameObject root, string logPrefix)
        {
            if (root == null)
            {
                return;
            }

            NPCExceptionHandler.TryExecute(() =>
            {
                Shader gameShader = GetPreferredCharacterShader();
                if (gameShader == null)
                {
                    return;
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    Material[] mats = renderer.materials;
                    if (mats == null || mats.Length == 0)
                    {
                        continue;
                    }

                    foreach (Material mat in mats)
                    {
                        if (mat == null || mat.shader == null)
                        {
                            continue;
                        }

                        string shaderName = mat.shader.name;
                        if (shaderName == "Standard" || shaderName.Contains("Standard"))
                        {
                            mat.shader = gameShader;
                            ModBehaviour.DevLog(logPrefix + " Replaced shader: " + shaderName + " -> " + gameShader.name);
                        }
                    }
                }
            }, logPrefix + " FixShaders");
        }

        /// <summary>
        /// Recursively sets the layer for the object tree.
        /// </summary>
        public static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null)
            {
                return;
            }

            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Checks whether any gameplay-blocking UI is open.
        /// </summary>
        public static bool IsAnyUIOpen()
        {
            try
            {
                if (View.ActiveView != null)
                {
                    return true;
                }

                if (PauseMenu.Instance != null && PauseMenu.Instance.Shown)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private static Shader GetPreferredCharacterShader()
        {
            if (!characterShaderResolved)
            {
                cachedCharacterShader = Shader.Find("SodaCraft/SodaCharacter");
                if (cachedCharacterShader == null)
                {
                    cachedCharacterShader = Shader.Find("Standard");
                }

                characterShaderResolved = true;
            }

            return cachedCharacterShader;
        }
    }
}
