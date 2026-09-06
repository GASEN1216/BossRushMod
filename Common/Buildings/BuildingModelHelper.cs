// 共享建筑模型工具。由许愿台实现原样抽取；材质、碰撞体、包围盒与回退行为不变。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class BuildingModelHelper
    {
        internal static Renderer[] CollectStarwishRenderableComponents(GameObject root)
        {
            if (root == null)
            {
                return new Renderer[0];
            }

            List<Renderer> filtered = new List<Renderer>();
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                filtered.Add(renderer);
            }

            return filtered.ToArray();
        }

        internal static bool TryGetCombinedBounds(Renderer[] renderers, out Bounds combinedBounds)
        {
            combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            if (renderers == null)
            {
                return false;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    combinedBounds = new Bounds(renderer.bounds.center, renderer.bounds.size);
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        internal static void AddStarwishGraphicsCollider(GameObject target, Renderer[] renderers)
        {
            try
            {
                if (target == null || renderers == null || renderers.Length == 0)
                {
                    return;
                }

                Bounds combinedBounds;
                if (!TryGetCombinedBounds(renderers, out combinedBounds))
                {
                    return;
                }

                BoxCollider boxCollider = target.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = target.AddComponent<BoxCollider>();
                }

                Vector3 lossyScale = target.transform.lossyScale;
                float scaleX = Mathf.Abs(lossyScale.x) > 0.0001f ? Mathf.Abs(lossyScale.x) : 1f;
                float scaleY = Mathf.Abs(lossyScale.y) > 0.0001f ? Mathf.Abs(lossyScale.y) : 1f;
                float scaleZ = Mathf.Abs(lossyScale.z) > 0.0001f ? Mathf.Abs(lossyScale.z) : 1f;

                boxCollider.center = target.transform.InverseTransformPoint(combinedBounds.center);
                boxCollider.size = new Vector3(
                    combinedBounds.size.x / scaleX,
                    combinedBounds.size.y / scaleY,
                    combinedBounds.size.z / scaleZ);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 添加图形碰撞体失败: " + e.Message);
            }
        }

        internal static void FixStarwishModelShaders(GameObject modelInstance)
        {
            try
            {
                Shader targetShader = null;
                string[] shaderCandidates = new string[]
                {
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Simple Lit",
                    "SodaCraft/SodaCharacter",
                    "Unlit/Texture"
                };

                for (int i = 0; i < shaderCandidates.Length; i++)
                {
                    targetShader = Shader.Find(shaderCandidates[i]);
                    if (targetShader != null)
                    {
                        break;
                    }
                }

                if (targetShader == null)
                {
                    ModBehaviour.DevLog("[WishFountain] 未找到兼容 Shader，保持 AssetBundle 原始材质");
                    return;
                }

                Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || renderer is ParticleSystemRenderer)
                    {
                        continue;
                    }

                    Material[] materials = renderer.materials;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        Material mat = materials[j];
                        if (mat == null || mat.shader == null)
                        {
                            continue;
                        }

                        string originalShaderName = mat.shader.name;
                        if (originalShaderName == "Standard"
                            || originalShaderName.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0
                            || originalShaderName == "Hidden/InternalErrorShader")
                        {
                            Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                            Texture normalMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                            Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                            Material newMat = new Material(targetShader);
                            if (mainTex != null)
                            {
                                if (newMat.HasProperty("_BaseMap"))
                                {
                                    newMat.SetTexture("_BaseMap", mainTex);
                                }
                                if (newMat.HasProperty("_MainTex"))
                                {
                                    newMat.SetTexture("_MainTex", mainTex);
                                }
                            }

                            if (normalMap != null && newMat.HasProperty("_BumpMap"))
                            {
                                newMat.SetTexture("_BumpMap", normalMap);
                            }

                            if (newMat.HasProperty("_BaseColor"))
                            {
                                newMat.SetColor("_BaseColor", color);
                            }
                            if (newMat.HasProperty("_Color"))
                            {
                                newMat.SetColor("_Color", color);
                            }

                            materials[j] = newMat;
                        }
                    }

                    renderer.materials = materials;
                    renderer.gameObject.layer = 0;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[WishFountain] 修复 AssetBundle Shader 失败: " + e.Message);
            }
        }
    }
}
