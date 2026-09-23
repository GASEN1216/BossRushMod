// 共享建筑模型工具。由许愿台实现原样抽取；材质、碰撞体、包围盒与回退行为不变。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossRush
{
    internal static class BuildingModelHelper
    {
        // 正常基地装配先经 RunSpecial 异步预载；官方早期 GetPrefab 查询保留同步兼容。
        // 只有实例创建完成才转交租约，坏包/缺 prefab 不妨碍原有占位模型。
        internal static bool TryInstantiateBundle(string buildingId, string prefabName, Transform parent, out AssetBundle lease)
        {
            lease = null;
            AssetBundle acquired = null;
            GameObject instance = null;
            try
            {
                string path = System.IO.Path.Combine(ModBehaviour.GetModPath(), "Assets", "buildings", buildingId);
                if (!System.IO.File.Exists(path)) return false;
                acquired = ResourceBundleLoader.LoadFromFile(path);
                if (acquired == null) return false;
                GameObject prefab = acquired.LoadAsset<GameObject>(prefabName);
                if (prefab == null) return false;
                instance = UnityEngine.Object.Instantiate(prefab, parent, false);
                instance.name = "Model";
                instance.SetActive(true);
                lease = acquired;
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BaseBuildings] 模型加载失败 " + buildingId + ": " + e.Message);
                return false;
            }
            finally
            {
                if (lease == null)
                {
                    if (instance != null) UnityEngine.Object.Destroy(instance);
                    if (acquired != null) acquired.Unload(true);
                }
            }
        }

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

        // 基地建筑包（tools/BaseBuildingBundleBuilder）的材质用天空岛环境着色器：它标成 Unlit、
        // 自己算光且岛外用写死的冷色环境光，基地的延迟光照照不到它，报箱和遗种巢因此发灰（2026-09-22 实测）。
        private const string BaseBuildingBundleShaderName = "BossRush/SkyIsland/Environment";
        private const string OfficialModelShaderName = "SodaCraft/SodaCharacter";
        private static readonly string[] OfficialShaderTextureSlots = { "_BaseMap", "_MainTex", "_BaseTex" };

        /// <summary>
        /// 基地建筑 bundle 模型的统一修整，与许愿台同一套做法：材质换成官方角色着色器
        /// （许愿台实机落到的就是 SodaCharacter，自带 UniversalGBuffer，基地灯光能照亮），
        /// 再按渲染包围盒补一个实体 BoxCollider（bundle 里刻意不带碰撞体）。
        /// 只在建预制体时跑一次；材质按源材质去重，随预制体整局常驻。
        /// </summary>
        internal static void PrepareBaseBuildingModel(GameObject modelInstance)
        {
            if (modelInstance == null)
            {
                return;
            }

            ConvertBaseBuildingMaterials(modelInstance);
            AddStarwishGraphicsCollider(modelInstance, CollectStarwishRenderableComponents(modelInstance));
        }

        private static void ConvertBaseBuildingMaterials(GameObject modelInstance)
        {
            try
            {
                Shader official = Shader.Find(OfficialModelShaderName);
                if (official == null)
                {
                    ModBehaviour.DevLog("[BaseBuildings] 未找到官方着色器 " + OfficialModelShaderName + "，保持原材质");
                    return;
                }

                Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
                Renderer[] renderers = CollectStarwishRenderableComponents(modelInstance);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    Material[] materials = renderer.sharedMaterials;
                    bool changed = false;
                    for (int j = 0; j < materials.Length; j++)
                    {
                        Material source = materials[j];
                        if (source == null || source.shader == null || !NeedsOfficialShader(source.shader.name))
                        {
                            continue;
                        }

                        Material replacement;
                        if (!converted.TryGetValue(source, out replacement))
                        {
                            replacement = CreateOfficialModelMaterial(source, official);
                            converted.Add(source, replacement);
                        }

                        materials[j] = replacement;
                        changed = true;
                    }

                    if (changed)
                    {
                        renderer.sharedMaterials = materials;
                    }
                    renderer.gameObject.layer = 0;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BaseBuildings] 转换建筑材质失败: " + e.Message);
            }
        }

        private static bool NeedsOfficialShader(string shaderName)
        {
            return shaderName == BaseBuildingBundleShaderName
                || shaderName == "Hidden/InternalErrorShader"
                || shaderName.IndexOf("Standard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Material CreateOfficialModelMaterial(Material source, Shader official)
        {
            Material material = new Material(official);
            material.name = "BossRushBuilding_" + source.name;

            // SodaCharacter 的颜色字段是 _Tint（实机枚举，见 ArenaPrototypeSession）；其余两个留给兼容着色器。
            Color tint = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Color")) material.SetColor("_Color", tint);

            // 基地建筑包的贴图在 _BaseMap、_MainTex 为空；只读 _MainTex 会得到一块无贴图的素色模型。
            string sourceMap = source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null ? "_BaseMap"
                : source.HasProperty("_MainTex") && source.GetTexture("_MainTex") != null ? "_MainTex" : null;
            if (sourceMap != null)
            {
                Texture texture = source.GetTexture(sourceMap);
                Vector2 scale = source.GetTextureScale(sourceMap);
                Vector2 offset = source.GetTextureOffset(sourceMap);
                for (int i = 0; i < OfficialShaderTextureSlots.Length; i++)
                {
                    string slot = OfficialShaderTextureSlots[i];
                    if (!material.HasProperty(slot)) continue;
                    material.SetTexture(slot, texture);
                    material.SetTextureScale(slot, scale);
                    material.SetTextureOffset(slot, offset);
                }
            }

            return material;
        }
    }
}
