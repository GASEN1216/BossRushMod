// ============================================================================
// EntityModelFactory.cs - 实体模型工厂
// ============================================================================
// 模块说明：
//   高性能 AssetBundle 加载和模型实例化工厂
//   采用延迟加载策略，仅在首次请求时加载资源
//   针对低端机优化：避免 GC 压力、按需加载、及时释放
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 实体模型工厂 - 自动加载 Assets/entity/ 目录下的 AssetBundle 资源
    /// </summary>
    public static class EntityModelFactory
    {
        #region 内部数据结构

        /// <summary>
        /// Bundle 信息结构体（使用 struct 避免 GC 压力）
        /// </summary>
        private struct BundleInfo
        {
            public string path;           // 文件路径
            public AssetBundle bundle;    // 已加载的 bundle（可为 null）
            public bool loadAttempted;    // 是否已尝试加载
        }

        #endregion

        #region 私有字段

        // 预制体缓存（预分配容量避免扩容）
        private static Dictionary<string, GameObject> _prefabCache;
        
        // Bundle 信息列表
        private static List<BundleInfo> _bundleInfos;
        
        // 初始化状态
        private static bool _isInitialized = false;
        
        // Mod 根目录路径
        private static string _modPath;

        // 预定义的路牌预制体名称
        private const string SIGNPOST_PREFAB_NAME = "BossRush_Signpost_Model";

        #endregion

        #region 初始化

        /// <summary>
        /// 初始化工厂，扫描 Assets/entity/ 目录
        /// </summary>
        /// <param name="modPath">Mod 根目录路径</param>
        public static void Initialize(string modPath)
        {
            if (_isInitialized)
            {
                return;
            }

            _modPath = modPath;
            _prefabCache = new Dictionary<string, GameObject>(16);
            _bundleInfos = new List<BundleInfo>(4);

            try
            {
                // 构建 entity 目录路径
                string entityPath = Path.Combine(modPath, "Assets", "entity");
                
                if (!Directory.Exists(entityPath))
                {
                    ModBehaviour.DevLog("[EntityModelFactory] [WARNING] entity 目录不存在: " + entityPath);
                    _isInitialized = true;
                    return;
                }

                // 扫描目录下的所有文件（AssetBundle 通常没有扩展名）
                string[] files = Directory.GetFiles(entityPath);
                
                for (int i = 0; i < files.Length; i++)
                {
                    string filePath = files[i];
                    
                    // 跳过 .manifest 和 .meta 文件
                    if (filePath.EndsWith(".manifest") || filePath.EndsWith(".meta"))
                    {
                        continue;
                    }

                    // 添加到 bundle 列表（延迟加载）
                    BundleInfo info = new BundleInfo
                    {
                        path = filePath,
                        bundle = null,
                        loadAttempted = false
                    };
                    _bundleInfos.Add(info);
                    
                    ModBehaviour.DevLog("[EntityModelFactory] 发现 Bundle: " + Path.GetFileName(filePath));
                }

                _isInitialized = true;
                ModBehaviour.DevLog("[EntityModelFactory] 初始化完成，发现 " + _bundleInfos.Count + " 个 Bundle 文件");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EntityModelFactory] [ERROR] 初始化异常: " + e.Message);
                _isInitialized = true; // 标记为已初始化，避免重复尝试
            }
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 尝试从未加载的 Bundle 中查找并加载预制体
        /// </summary>
        /// <param name="prefabName">预制体名称</param>
        /// <returns>是否成功加载</returns>
        private static bool TryLoadPrefabFromBundles(string prefabName)
        {
            if (_bundleInfos == null)
            {
                return false;
            }

            // 遍历所有 bundle（使用 for 循环避免 foreach 的 GC）
            for (int i = 0; i < _bundleInfos.Count; i++)
            {
                BundleInfo info = _bundleInfos[i];
                
                // 如果 bundle 未加载，先加载它
                if (!info.loadAttempted)
                {
                    info.loadAttempted = true;
                    
                    try
                    {
                        info.bundle = AssetBundle.LoadFromFile(info.path);
                        
                        if (info.bundle == null)
                        {
                            ModBehaviour.DevLog("[EntityModelFactory] [WARNING] Bundle 加载失败: " + info.path);
                        }
                        else
                        {
                            ModBehaviour.DevLog("[EntityModelFactory] 已加载 Bundle: " + Path.GetFileName(info.path));
                            
                            // 缓存 bundle 中的所有预制体名称（用于查询）
                            CacheBundlePrefabs(info.bundle);
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[EntityModelFactory] [WARNING] Bundle 加载异常: " + e.Message);
                        info.bundle = null;
                    }
                    
                    // 更新列表中的 struct
                    _bundleInfos[i] = info;
                }

                // 检查缓存中是否已有该预制体
                if (_prefabCache.ContainsKey(prefabName))
                {
                    return true;
                }
            }

            return _prefabCache.ContainsKey(prefabName);
        }

        /// <summary>
        /// 缓存 Bundle 中的所有预制体
        /// </summary>
        /// <param name="bundle">AssetBundle</param>
        private static void CacheBundlePrefabs(AssetBundle bundle)
        {
            if (bundle == null)
            {
                return;
            }

            try
            {
                // 获取所有资源名称
                string[] assetNames = bundle.GetAllAssetNames();
                
                for (int i = 0; i < assetNames.Length; i++)
                {
                    string assetPath = assetNames[i];
                    
                    // 只加载预制体
                    if (!assetPath.EndsWith(".prefab"))
                    {
                        continue;
                    }

                    try
                    {
                        GameObject prefab = bundle.LoadAsset<GameObject>(assetPath);
                        
                        if (prefab != null)
                        {
                            // 使用预制体名称作为 key（不含路径和扩展名）
                            string prefabName = prefab.name;
                            
                            if (!_prefabCache.ContainsKey(prefabName))
                            {
                                _prefabCache[prefabName] = prefab;
                                ModBehaviour.DevLog("[EntityModelFactory] 已缓存预制体: " + prefabName);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 加载预制体失败: " + assetPath + ", " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 缓存预制体异常: " + e.Message);
            }
        }

        #endregion

        #region 工厂方法

        /// <summary>
        /// 创建模型实例
        /// </summary>
        /// <param name="prefabName">预制体名称</param>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <returns>创建的 GameObject，如果预制体不存在则返回后备空对象</returns>
        public static GameObject Create(string prefabName, Vector3 position, Quaternion rotation)
        {
            // 未初始化时返回后备对象
            if (!_isInitialized || _prefabCache == null)
            {
                ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 工厂未初始化，创建后备对象: " + prefabName);
                return CreateFallbackObject(prefabName, position, rotation);
            }

            // 先检查缓存
            GameObject prefab;
            if (_prefabCache.TryGetValue(prefabName, out prefab) && prefab != null)
            {
                return InstantiatePrefab(prefab, prefabName, position, rotation);
            }

            // 缓存未命中，尝试从未加载的 bundle 中查找
            if (TryLoadPrefabFromBundles(prefabName))
            {
                if (_prefabCache.TryGetValue(prefabName, out prefab) && prefab != null)
                {
                    return InstantiatePrefab(prefab, prefabName, position, rotation);
                }
            }

            // 预制体不存在，返回后备对象
            ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 预制体不存在: " + prefabName + "，创建后备对象");
            return CreateFallbackObject(prefabName, position, rotation);
        }

        /// <summary>
        /// 实例化预制体
        /// </summary>
        private static GameObject InstantiatePrefab(GameObject prefab, string prefabName, Vector3 position, Quaternion rotation)
        {
            // 路牌模型的 pivot 在模型中心，需要向上偏移使其底部贴地
            // 路牌高度约 2 米，偏移 1 米使底部贴地
            Vector3 adjustedPosition = position;
            Quaternion adjustedRotation = rotation;
            
            if (prefabName == SIGNPOST_PREFAB_NAME)
            {
                adjustedPosition.y += 1.0f;
                // 基础模型朝向修正：X轴旋转-90度，Y轴旋转90度
                // 额外的 Y 轴旋转通过传入的 rotation 参数控制
                adjustedRotation = rotation * Quaternion.Euler(-90f, 90f, 0f);
            }
            
            GameObject instance = UnityEngine.Object.Instantiate(prefab, adjustedPosition, adjustedRotation);
            instance.name = "BossRush_" + prefabName;
            
            // 修复 Shader（从 Standard 替换为游戏使用的 Shader）
            FixShaders(instance);
            
            // 设置 Layer（确保渲染正确）
            SetLayerRecursively(instance, LayerMask.NameToLayer("Default"));
            
            return instance;
        }
        
        /// <summary>
        /// 修复模型的 Shader（从 Standard 替换为游戏 Shader）
        /// AssetBundle 中的模型通常使用 Standard Shader，但游戏使用自定义 Shader
        /// 如果不替换，模型会有影子但不显示（因为 Shader 不兼容）
        /// </summary>
        /// <param name="obj">目标 GameObject</param>
        private static void FixShaders(GameObject obj)
        {
            try
            {
                // 尝试获取游戏使用的 Shader
                // 优先使用 SodaCraft/SodaCharacter（角色/物品通用）
                // 如果找不到，尝试 Universal Render Pipeline/Lit（URP 标准）
                // 最后兜底使用 Standard
                Shader gameShader = Shader.Find("SodaCraft/SodaCharacter");
                if (gameShader == null)
                {
                    gameShader = Shader.Find("Universal Render Pipeline/Lit");
                }
                if (gameShader == null)
                {
                    gameShader = Shader.Find("Standard");
                }
                
                if (gameShader == null)
                {
                    ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 未找到可用的 Shader");
                    return;
                }
                
                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
                int fixedCount = 0;
                
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null || renderer.materials == null)
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
                        
                        string shaderName = mat.shader.name;
                        // 如果是 Standard shader 或其变体，替换为游戏 shader
                        if (shaderName == "Standard" || 
                            shaderName.Contains("Standard") ||
                            shaderName == "Hidden/InternalErrorShader")
                        {
                            mat.shader = gameShader;
                            fixedCount++;
                        }
                    }
                }
                
                if (fixedCount > 0)
                {
                    ModBehaviour.DevLog("[EntityModelFactory] 已修复 " + fixedCount + " 个材质的 Shader -> " + gameShader.name);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 修复 Shader 异常: " + e.Message);
            }
        }
        
        /// <summary>
        /// 递归设置 Layer
        /// </summary>
        /// <param name="obj">目标 GameObject</param>
        /// <param name="layer">Layer 值</param>
        private static void SetLayerRecursively(GameObject obj, int layer)
        {
            if (obj == null)
            {
                return;
            }
            
            obj.layer = layer;
            
            Transform transform = obj.transform;
            int childCount = transform.childCount;
            for (int i = 0; i < childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child != null)
                {
                    SetLayerRecursively(child.gameObject, layer);
                }
            }
        }

        /// <summary>
        /// 创建后备空对象
        /// </summary>
        private static GameObject CreateFallbackObject(string prefabName, Vector3 position, Quaternion rotation)
        {
            GameObject fallback = new GameObject("BossRush_" + prefabName + "_Fallback");
            fallback.transform.position = position;
            fallback.transform.rotation = rotation;
            return fallback;
        }

        /// <summary>
        /// 创建路牌模型（便捷方法）
        /// </summary>
        /// <param name="position">位置</param>
        /// <param name="rotation">旋转</param>
        /// <returns>路牌 GameObject</returns>
        public static GameObject CreateSignpost(Vector3 position, Quaternion rotation)
        {
            return Create(SIGNPOST_PREFAB_NAME, position, rotation);
        }

        /// <summary>
        /// 创建路牌模型（使用默认旋转）
        /// </summary>
        /// <param name="position">位置</param>
        /// <returns>路牌 GameObject</returns>
        public static GameObject CreateSignpost(Vector3 position)
        {
            return Create(SIGNPOST_PREFAB_NAME, position, Quaternion.identity);
        }

        #endregion

        #region 查询方法

        /// <summary>
        /// 检查指定预制体是否存在
        /// </summary>
        /// <param name="prefabName">预制体名称</param>
        /// <returns>是否存在</returns>
        public static bool HasPrefab(string prefabName)
        {
            if (!_isInitialized || _prefabCache == null)
            {
                return false;
            }

            // 先检查缓存
            if (_prefabCache.ContainsKey(prefabName))
            {
                return true;
            }

            // 尝试从未加载的 bundle 中查找
            TryLoadPrefabFromBundles(prefabName);
            
            return _prefabCache.ContainsKey(prefabName);
        }

        /// <summary>
        /// 获取所有已加载的预制体名称
        /// </summary>
        /// <returns>预制体名称列表（只读）</returns>
        public static IReadOnlyList<string> GetLoadedPrefabNames()
        {
            if (!_isInitialized || _prefabCache == null)
            {
                return new List<string>();
            }

            // 先加载所有 bundle
            LoadAllBundles();

            // 返回所有缓存的预制体名称
            List<string> names = new List<string>(_prefabCache.Count);
            foreach (var kvp in _prefabCache)
            {
                names.Add(kvp.Key);
            }
            return names;
        }

        /// <summary>
        /// 加载所有未加载的 Bundle
        /// </summary>
        private static void LoadAllBundles()
        {
            if (_bundleInfos == null)
            {
                return;
            }

            for (int i = 0; i < _bundleInfos.Count; i++)
            {
                BundleInfo info = _bundleInfos[i];
                
                if (!info.loadAttempted)
                {
                    info.loadAttempted = true;
                    
                    try
                    {
                        info.bundle = AssetBundle.LoadFromFile(info.path);
                        
                        if (info.bundle != null)
                        {
                            CacheBundlePrefabs(info.bundle);
                        }
                    }
                    catch
                    {
                        info.bundle = null;
                    }
                    
                    _bundleInfos[i] = info;
                }
            }
        }

        /// <summary>
        /// 检查工厂是否已初始化
        /// </summary>
        public static bool IsInitialized
        {
            get { return _isInitialized; }
        }

        #endregion

        #region 资源释放

        /// <summary>
        /// 卸载所有资源
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                // 卸载所有 AssetBundle
                if (_bundleInfos != null)
                {
                    for (int i = 0; i < _bundleInfos.Count; i++)
                    {
                        BundleInfo info = _bundleInfos[i];
                        
                        if (info.bundle != null)
                        {
                            try
                            {
                                info.bundle.Unload(true);
                            }
                            catch
                            {
                                // 忽略卸载异常
                            }
                        }
                    }
                    
                    _bundleInfos.Clear();
                    _bundleInfos = null;
                }

                // 清空预制体缓存
                if (_prefabCache != null)
                {
                    _prefabCache.Clear();
                    _prefabCache = null;
                }

                _isInitialized = false;
                _modPath = null;

                ModBehaviour.DevLog("[EntityModelFactory] 已卸载所有资源");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[EntityModelFactory] [WARNING] 卸载异常: " + e.Message);
            }
        }

        #endregion
    }
}
