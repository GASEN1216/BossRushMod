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

        /// <summary>
        /// 许愿台 bundle 模型的材质修整：与基地建筑（报箱、遗种巢）走同一条 ConvertBaseBuildingMaterials，
        /// 统一换成官方 SodaCharacter（_Tint、_BaseMap/_MainTex，按源材质去重、不复制实例）。
        /// 旧版单独维护一张 URP/Lit 优先的候选表，转成 URP/Lit 会带 PBR 高光，站在官方建筑旁边显得塑料（VA-33）；
        /// 实机里 URP/Lit 本来就找不到、落到的也是 SodaCharacter，这里只是去掉那份重复的候选表。
        /// </summary>
        internal static void FixStarwishModelShaders(GameObject modelInstance)
        {
            if (modelInstance == null)
            {
                return;
            }

            ConvertBaseBuildingMaterials(modelInstance);
        }

        /// <summary>缺包占位模型用的官方着色器纯色材质（SodaCharacter 写 _Tint），有明暗、吃基地灯光。找不到着色器返回 null。</summary>
        internal static Material CreateOfficialTintMaterial(string name, Color tint)
        {
            Shader official = Shader.Find(OfficialModelShaderName);
            if (official == null)
            {
                return null;
            }

            Material material = new Material(official);
            material.name = name;
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", tint);
            return material;
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
            LogSolidCollider(modelInstance);
        }

        /// <summary>
        /// 建预制体时把实体碰撞体的实际参数写进 Player.log（每座建筑每局一行，正式构建也打）。
        /// 2026-09-22 实测「报箱没有碰撞」：离线核对代码、层、包内尺寸都与许愿台等价，找不到差异（复核 UNVERIFIED），
        /// 下次实机看这一行就能分清是「碰撞体没建出来 / 尺寸为 0 / 被设成 trigger」还是「Default 层根本不挡玩家」。
        /// </summary>
        private static void LogSolidCollider(GameObject modelInstance)
        {
            try
            {
                BoxCollider box = modelInstance.GetComponent<BoxCollider>();
                Transform root = modelInstance.transform.root;
                string owner = root != null ? root.name : modelInstance.name;
                if (box == null)
                {
                    Debug.LogWarning("[BaseBuilding] " + owner + " 没有建出实体碰撞体（模型没有可用渲染包围盒）");
                    return;
                }
                Debug.Log("[BaseBuilding] " + owner + " 实体碰撞体 size=" + box.size + " center=" + box.center
                    + " lossyScale=" + modelInstance.transform.lossyScale + " layer=" + LayerMask.LayerToName(modelInstance.layer)
                    + " trigger=" + box.isTrigger + " enabled=" + box.enabled);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[BaseBuilding] 记录碰撞体参数失败: " + e.Message);
            }
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
