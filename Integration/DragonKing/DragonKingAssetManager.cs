// ============================================================================
// DragonKingAssetManager.cs - 龙王Boss资源管理器
// ============================================================================
// 模块说明：
//   负责AssetBundle的加载、缓存和卸载
//   管理龙王Boss的所有特效预制体
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace BossRush
{
    /// <summary>
    /// 龙王Boss资源管理器
    /// </summary>
    public static class DragonKingAssetManager
    {
        // ========== 资源缓存 ==========

        /// <summary>
        /// 已加载的AssetBundle
        /// </summary>
        private static AssetBundle loadedBundle = null;

        /// <summary>
        /// 预制体缓存
        /// </summary>
        private static Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        /// <summary>
        /// 是否已尝试加载
        /// </summary>
        private static bool loadAttempted = false;

        /// <summary>
        /// 加载是否成功
        /// </summary>
        private static bool loadSucceeded = false;

        /// <summary>
        /// AssetBundle的依赖计数（用于正确管理生命周期）
        /// 当有Boss实例时递增，销毁时递减，为0时才真正卸载
        /// </summary>
        private static int assetBundleRefCount = 0;

        /// <summary>
        /// 动态创建的Material跟踪列表（防止内存泄漏）
        /// </summary>
        private static List<Material> dynamicMaterials = new List<Material>();
        
        // ========== 公开方法 ==========
        
        /// <summary>
        /// 加载AssetBundle（异步版本，内部使用同步加载）
        /// </summary>
        /// <param name="modBasePath">Mod基础路径</param>
        /// <returns>是否加载成功</returns>
        public static async UniTask<bool> LoadAssetBundle(string modBasePath)
        {
            // 直接调用同步版本，避免UniTask扩展方法的依赖问题
            return await UniTask.FromResult(LoadAssetBundleSync(modBasePath));
        }
        
        /// <summary>
        /// 同步加载AssetBundle
        /// </summary>
        public static bool LoadAssetBundleSync(string modBasePath)
        {
            if (loadedBundle != null) return true;
            if (loadAttempted) return loadSucceeded;
            
            loadAttempted = true;
            
            try
            {
                string bundlePath = Path.Combine(modBasePath, DragonKingConfig.AssetBundlePath);
                
                ModBehaviour.DevLog($"[DragonKing] 同步加载AssetBundle: {bundlePath}");
                
                if (!File.Exists(bundlePath))
                {
                    ModBehaviour.DevLog("[DragonKing] [WARNING] AssetBundle文件不存在");
                    return false;
                }
                
                loadedBundle = AssetBundle.LoadFromFile(bundlePath);
                
                if (loadedBundle == null)
                {
                    ModBehaviour.DevLog("[DragonKing] [ERROR] AssetBundle加载失败");
                    return false;
                }
                
                loadSucceeded = true;
                assetBundleRefCount++; // 增加引用计数
                ModBehaviour.DevLog($"[DragonKing] AssetBundle加载成功，引用计数: {assetBundleRefCount}");

                PreloadPrefabs();
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [ERROR] 同步加载AssetBundle异常: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// 预加载所有预制体到缓存
        /// </summary>
        private static void PreloadPrefabs()
        {
            if (loadedBundle == null) return;
            
            try
            {
                // 预加载所有已知的预制体
                string[] prefabNames = new string[]
                {
                    DragonKingConfig.PrismaticBoltPrefab,
                    DragonKingConfig.SunBeamGroupPrefab,
                    DragonKingConfig.RainbowStarPrefab,
                    DragonKingConfig.EtherealLancePrefab,
                    DragonKingConfig.DashTrailPrefab,
                    DragonKingConfig.TeleportFXPrefab,
                    DragonKingConfig.PhaseTransitionPrefab
                };
                
                foreach (string name in prefabNames)
                {
                    GetPrefab(name);
                }
                
                ModBehaviour.DevLog($"[DragonKing] 预制体预加载完成，缓存数量: {prefabCache.Count}");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 预加载预制体失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 获取预制体
        /// </summary>
        /// <param name="name">预制体名称</param>
        /// <returns>预制体GameObject，失败返回null</returns>
        public static GameObject GetPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            
            // 检查缓存
            if (prefabCache.TryGetValue(name, out GameObject cached))
            {
                return cached;
            }
            
            // 从AssetBundle加载
            if (loadedBundle == null)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] AssetBundle未加载，无法获取预制体: {name}");
                return null;
            }
            
            try
            {
                GameObject prefab = loadedBundle.LoadAsset<GameObject>(name);
                if (prefab != null)
                {
                    prefabCache[name] = prefab;
                    ModBehaviour.DevLog($"[DragonKing] 加载预制体成功: {name}");
                    
                    // 输出预制体详细信息用于调试
                    LogPrefabDetails(prefab, name);
                }
                else
                {
                    ModBehaviour.DevLog($"[DragonKing] [WARNING] 预制体不存在: {name}");
                }
                return prefab;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [ERROR] 加载预制体失败: {name} - {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 输出预制体详细信息（用于调试）
        /// </summary>
        private static void LogPrefabDetails(GameObject prefab, string name)
        {
            try
            {
                // 统计组件
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
                var lights = prefab.GetComponentsInChildren<Light>(true);
                var allComponents = prefab.GetComponentsInChildren<Component>(true);
                
                ModBehaviour.DevLog($"[DragonKing] 预制体详情 [{name}]: " +
                    $"子对象={prefab.transform.childCount}, " +
                    $"Renderer={renderers.Length}, " +
                    $"MeshFilter={meshFilters.Length}, " +
                    $"Light={lights.Length}, " +
                    $"总组件={allComponents.Length}");
                
                // 列出所有组件类型
                var componentTypes = new System.Collections.Generic.HashSet<string>();
                foreach (var comp in allComponents)
                {
                    if (comp != null)
                    {
                        componentTypes.Add(comp.GetType().Name);
                    }
                }
                ModBehaviour.DevLog($"[DragonKing] 预制体组件类型 [{name}]: {string.Join(", ", componentTypes)}");
                
                // 检查材质
                foreach (var renderer in renderers)
                {
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        var mat = renderer.sharedMaterial;
                        ModBehaviour.DevLog($"[DragonKing] 预制体材质 [{name}]: 材质={mat.name}, Shader={(mat.shader != null ? mat.shader.name : "null")}");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 输出预制体详情失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 实例化特效
        /// </summary>
        /// <param name="prefabName">预制体名称</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <returns>实例化的GameObject，失败返回null</returns>
        public static GameObject InstantiateEffect(string prefabName, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = GetPrefab(prefabName);
            
            // 如果预制体不存在或为空，使用后备特效
            if (prefab == null)
            {
                return CreateFallbackEffect(prefabName, position, rotation);
            }
            
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                
                // 检查实例化的对象是否有有效的渲染器
                // 如果没有，添加后备视觉效果
                var renderers = instance.GetComponentsInChildren<Renderer>();
                
                if (renderers.Length == 0)
                {
                    ModBehaviour.DevLog($"[DragonKing] 预制体 {prefabName} 没有渲染器，添加后备视觉效果");
                    AddFallbackVisuals(instance, prefabName);
                }
                
                // 手动播放所有粒子系统（确保特效显示）
                PlayAllParticleSystems(instance);
                
                // 启用所有Light组件
                EnableAllLights(instance);
                
                return instance;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [ERROR] 实例化特效失败: {prefabName} - {e.Message}");
                return CreateFallbackEffect(prefabName, position, rotation);
            }
        }
        
        /// <summary>
        /// 手动播放所有粒子系统
        /// </summary>
        private static void PlayAllParticleSystems(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                // 使用反射获取ParticleSystem组件并播放
                // 避免直接引用ParticleSystemModule
                var particleSystems = obj.GetComponentsInChildren<Component>(true);
                int playedCount = 0;
                
                foreach (var comp in particleSystems)
                {
                    if (comp != null && comp.GetType().Name == "ParticleSystem")
                    {
                        // 确保游戏对象启用
                        comp.gameObject.SetActive(true);
                        
                        // 使用反射调用Play方法
                        var playMethod = comp.GetType().GetMethod("Play", new System.Type[] { typeof(bool) });
                        if (playMethod != null)
                        {
                            playMethod.Invoke(comp, new object[] { true });
                            playedCount++;
                        }
                    }
                }
                
                if (playedCount > 0)
                {
                    ModBehaviour.DevLog($"[DragonKing] 已播放 {playedCount} 个粒子系统");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] 播放粒子系统失败: {e.Message}");
            }
        }
        
        /// <summary>
        /// 启用所有Light组件
        /// </summary>
        private static void EnableAllLights(GameObject obj)
        {
            if (obj == null) return;
            
            try
            {
                var lights = obj.GetComponentsInChildren<Light>(true);
                foreach (var light in lights)
                {
                    if (light != null)
                    {
                        light.enabled = true;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [WARNING] EnableAllLights异常: {e.Message}");
            }
        }
        
        /// <summary>
        /// 创建后备特效（当AssetBundle预制体不可用时）
        /// </summary>
        private static GameObject CreateFallbackEffect(string prefabName, Vector3 position, Quaternion rotation)
        {
            try
            {
                GameObject fallback = new GameObject($"Fallback_{prefabName}");
                fallback.transform.position = position;
                fallback.transform.rotation = rotation;
                
                AddFallbackVisuals(fallback, prefabName);
                
                ModBehaviour.DevLog($"[DragonKing] 创建后备特效: {prefabName}");
                return fallback;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [ERROR] 创建后备特效失败: {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 为对象添加后备视觉效果
        /// </summary>
        private static void AddFallbackVisuals(GameObject obj, string prefabName)
        {
            // 根据预制体类型选择不同的后备效果
            Color effectColor = GetEffectColor(prefabName);
            float scale = GetEffectScale(prefabName);
            
            // 创建发光球体
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(obj.transform);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * scale;
            
            // 移除碰撞器（特效不需要碰撞）
            var collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }
            
            // 设置材质颜色
            var renderer = sphere.GetComponent<Renderer>();
            if (renderer != null)
            {
                // 尝试使用发光材质
                try
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    mat.SetColor("_Color", effectColor);
                    mat.SetColor("_EmissionColor", effectColor * 2f);
                    mat.EnableKeyword("_EMISSION");
                    renderer.material = mat;

                    // 跟踪动态创建的Material以便清理（添加null检查）
                    if (mat != null)
                    {
                        lock (dynamicMaterials)
                        {
                            dynamicMaterials.Add(mat);
                        }
                    }
                }
                catch (Exception e)
                {
                    // 如果Standard着色器不可用，使用默认颜色
                    ModBehaviour.DevLog($"[DragonKing] [WARNING] 创建发光材质失败，使用默认颜色: {e.Message}");
                    renderer.material.color = effectColor;
                }
            }
            
            // 添加点光源增强视觉效果
            Light light = obj.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = effectColor;
            light.intensity = 2f;
            light.range = scale * 3f;
            
            // 添加旋转动画（让特效更生动）
            var rotator = obj.AddComponent<SimpleRotator>();
            rotator.rotationSpeed = GetRotationSpeed(prefabName);
        }
        
        /// <summary>
        /// 根据预制体名称获取特效颜色
        /// </summary>
        private static Color GetEffectColor(string prefabName)
        {
            switch (prefabName)
            {
                case DragonKingConfig.PrismaticBoltPrefab:
                    return new Color(1f, 0.5f, 1f, 1f); // 粉紫色
                case DragonKingConfig.SunBeamGroupPrefab:
                    return new Color(1f, 0.9f, 0.3f, 1f); // 金黄色
                case DragonKingConfig.RainbowStarPrefab:
                    return new Color(0.5f, 1f, 1f, 1f); // 青色
                case DragonKingConfig.EtherealLancePrefab:
                    return new Color(0.8f, 0.8f, 1f, 1f); // 淡蓝色
                case DragonKingConfig.DashTrailPrefab:
                    return new Color(1f, 0.3f, 0.3f, 1f); // 红色
                case DragonKingConfig.TeleportFXPrefab:
                    return new Color(0.5f, 0f, 1f, 1f); // 紫色
                case DragonKingConfig.PhaseTransitionPrefab:
                    return new Color(1f, 1f, 0f, 1f); // 黄色
                default:
                    return Color.white;
            }
        }
        
        /// <summary>
        /// 根据预制体名称获取特效大小
        /// </summary>
        private static float GetEffectScale(string prefabName)
        {
            switch (prefabName)
            {
                case DragonKingConfig.PrismaticBoltPrefab:
                    return 0.3f;
                case DragonKingConfig.SunBeamGroupPrefab:
                    return 1.5f;
                case DragonKingConfig.RainbowStarPrefab:
                    return 0.4f;
                case DragonKingConfig.EtherealLancePrefab:
                    return 0.5f;
                case DragonKingConfig.DashTrailPrefab:
                    return 0.8f;
                case DragonKingConfig.TeleportFXPrefab:
                    return 1.0f;
                case DragonKingConfig.PhaseTransitionPrefab:
                    return 2.0f;
                default:
                    return 0.5f;
            }
        }
        
        /// <summary>
        /// 根据预制体名称获取旋转速度
        /// </summary>
        private static Vector3 GetRotationSpeed(string prefabName)
        {
            switch (prefabName)
            {
                case DragonKingConfig.PrismaticBoltPrefab:
                    return new Vector3(0f, 180f, 0f);
                case DragonKingConfig.RainbowStarPrefab:
                    return new Vector3(0f, 90f, 45f);
                case DragonKingConfig.EtherealLancePrefab:
                    return new Vector3(0f, 0f, 360f);
                default:
                    return new Vector3(0f, 45f, 0f);
            }
        }
        
        /// <summary>
        /// 实例化特效（带父对象）
        /// </summary>
        public static GameObject InstantiateEffect(string prefabName, Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject prefab = GetPrefab(prefabName);
            if (prefab == null) return null;
            
            try
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation, parent);
                return instance;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog($"[DragonKing] [ERROR] 实例化特效失败: {prefabName} - {e.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// 卸载AssetBundle
        /// </summary>
        /// <param name="unloadAllLoadedObjects">是否卸载所有已加载的对象</param>
        public static void UnloadAssetBundle(bool unloadAllLoadedObjects = false)
        {
            if (loadedBundle != null)
            {
                loadedBundle.Unload(unloadAllLoadedObjects);
                loadedBundle = null;
                ModBehaviour.DevLog("[DragonKing] AssetBundle已卸载");
            }

            prefabCache.Clear();
            loadAttempted = false;
            loadSucceeded = false;
            assetBundleRefCount = 0;
        }

        /// <summary>
        /// 清理缓存（Boss销毁时调用）
        /// 使用引用计数管理，只有当所有Boss实例都销毁后才真正卸载
        /// </summary>
        public static void ClearCache()
        {
            assetBundleRefCount--;
            ModBehaviour.DevLog($"[DragonKing] 资源缓存清理，引用计数: {assetBundleRefCount}");

            // 只有当引用计数为0时才真正清理
            if (assetBundleRefCount <= 0)
            {
                prefabCache.Clear();
                loadAttempted = false;
                loadSucceeded = false;
                assetBundleRefCount = 0;
                ModBehaviour.DevLog("[DragonKing] 所有资源已清理，AssetBundle仍保持加载以便复用");
            }
        }

        /// <summary>
        /// 强制清理所有资源（场景切换时调用）
        /// </summary>
        public static void ForceCleanup()
        {
            prefabCache.Clear();
            loadAttempted = false;
            loadSucceeded = false;
            assetBundleRefCount = 0;

            // 清理所有动态创建的Material（防止内存泄漏）
            CleanupDynamicMaterials();

            ModBehaviour.DevLog("[DragonKing] 强制清理完成");
        }

        /// <summary>
        /// 清理所有动态创建的Material
        /// </summary>
        private static void CleanupDynamicMaterials()
        {
            lock (dynamicMaterials)
            {
                foreach (var mat in dynamicMaterials)
                {
                    if (mat != null)
                    {
                        UnityEngine.Object.Destroy(mat);
                    }
                }
                dynamicMaterials.Clear();
            }
        }
        
        /// <summary>
        /// 检查AssetBundle是否已加载
        /// </summary>
        public static bool IsLoaded => loadedBundle != null;
    }
    
    /// <summary>
    /// 简单旋转组件（用于后备特效动画）
    /// </summary>
    public class SimpleRotator : MonoBehaviour
    {
        /// <summary>
        /// 旋转速度（度/秒）
        /// </summary>
        public Vector3 rotationSpeed = new Vector3(0f, 45f, 0f);
        
        private void Update()
        {
            transform.Rotate(rotationSpeed * Time.deltaTime);
        }
    }
}
