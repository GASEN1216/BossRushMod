// ============================================================================
// WeddingBuildingInjector.cs - 婚礼教堂建筑注入器
// ============================================================================
// 模块说明：
//   通过反射将自定义婚礼教堂建筑注入到游戏原版建筑系统中，
//   使其出现在基地地堡的建造UI中，玩家可自由放置/旋转/拆除。
//   建筑内包含一个NPC站位点，用于生成婚礼NPC。
//
// 技术方案：
//   - 纯反射注入 BuildingDataCollection 的 infos 和 prefabs 列表
//   - 不使用 Harmony，最大兼容性
//   - 动态创建 Building prefab（支持 AssetBundle 加载或临时占位模型）
//   - 监听 BuildingManager.OnBuildingBuilt 事件，在建筑放置后生成NPC
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 婚礼教堂建筑注入器（partial class ModBehaviour）
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 婚礼建筑常量
        // ============================================================================

        /// <summary>建筑ID，用于 BuildingInfo 和 BuildingManager 识别</summary>
        private const string WEDDING_BUILDING_ID = "wedding_chapel";

        /// <summary>建筑预制体名称，用于 BuildingDataCollection.GetPrefab 查找</summary>
        private const string WEDDING_PREFAB_NAME = "WeddingChapel";

        /// <summary>建筑占地尺寸（格子数）</summary>
        private static readonly Vector2Int WEDDING_BUILDING_SIZE = new Vector2Int(3, 3);

        /// <summary>建筑费用（金币）</summary>
        private const long WEDDING_BUILDING_COST = 5000;

        /// <summary>最大建造数量（1个就够了）</summary>
        private const int WEDDING_BUILDING_MAX_AMOUNT = 1;

        /// <summary>解锁婚礼建筑所需的NPC好感度等级（任意NPC达到此等级即可）</summary>
        private const int WEDDING_BUILDING_REQUIRED_AFFINITY_LEVEL = 10;

        /// <summary>NPC站位相对于建筑中心的偏移量</summary>
        private static readonly Vector3 WEDDING_NPC_OFFSET = new Vector3(0f, 0f, 2f);

        /// <summary>婚礼教堂交互点相对于建筑中心的偏移量</summary>
        private static readonly Vector3 WEDDING_INTERACT_OFFSET = new Vector3(0f, 0f, 1f);

        // ============================================================================
        // 状态追踪
        // ============================================================================

        /// <summary>是否已注入建筑数据</summary>
        private bool weddingBuildingInjected = false;

        /// <summary>动态创建的建筑预制体缓存</summary>
        private GameObject weddingBuildingPrefabGO = null;

        /// <summary>AssetBundle 缓存</summary>
        private static AssetBundle weddingAssetBundle = null;

        /// <summary>从 AssetBundle 加载的模型</summary>
        private static GameObject weddingModelPrefab = null;

        /// <summary>建筑图标 Sprite（从 PNG 加载）</summary>
        private static Sprite weddingBuildingIcon = null;

        /// <summary>当前场景中已放置的婚礼建筑实例（用于NPC生成追踪）</summary>
        private GameObject weddingNPCInstance = null;

        /// <summary>婚礼教堂是否存在的缓存状态</summary>
        private bool weddingBuildingPresenceKnown = false;

        /// <summary>婚礼教堂是否存在</summary>
        private bool cachedWeddingBuildingPresent = false;

        /// <summary>缓存的婚礼建筑Transform，避免重复全场景扫描</summary>
        private Transform cachedWeddingBuildingTransform = null;

        /// <summary>缓存的婚礼NPC站位点</summary>
        private Transform cachedWeddingNpcSpawnPoint = null;

        /// <summary>BuildingManager 类型缓存</summary>
        private static Type cachedBuildingManagerType = null;
        private static bool buildingManagerTypeResolved = false;

        /// <summary>BuildingManager.Any 方法缓存</summary>
        private static MethodInfo cachedBuildingManagerAnyMethod = null;
        private static bool buildingManagerAnyMethodResolved = false;

        /// <summary>BuildingManager.GetBuildingData 方法缓存</summary>
        private static MethodInfo cachedGetBuildingDataMethod = null;
        private static bool getBuildingDataMethodResolved = false;

        /// <summary>Building 类型缓存</summary>
        private static Type cachedBuildingType = null;
        private static bool buildingTypeResolved = false;

        /// <summary>Building.ID 属性缓存</summary>
        private static PropertyInfo cachedBuildingIdProperty = null;
        private static bool buildingIdPropertyResolved = false;

        // ============================================================================
        // 工具方法
        // ============================================================================

        /// <summary>
        /// 在所有已加载的程序集中查找指定全名的类型
        /// 替代 ReflectionCache.GetType，避免依赖外部工具类
        /// </summary>
        private static Type FindGameType(string fullTypeName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullTypeName);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static Type GetBuildingManagerType()
        {
            if (!buildingManagerTypeResolved)
            {
                cachedBuildingManagerType = FindGameType("Duckov.Buildings.BuildingManager");
                buildingManagerTypeResolved = true;
            }

            return cachedBuildingManagerType;
        }

        private static MethodInfo GetBuildingManagerAnyMethod()
        {
            if (!buildingManagerAnyMethodResolved)
            {
                Type buildingManagerType = GetBuildingManagerType();
                if (buildingManagerType != null)
                {
                    cachedBuildingManagerAnyMethod = buildingManagerType.GetMethod(
                        "Any",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new Type[] { typeof(string), typeof(bool) },
                        null);
                }

                buildingManagerAnyMethodResolved = true;
            }

            return cachedBuildingManagerAnyMethod;
        }

        private static MethodInfo GetBuildingDataMethod()
        {
            if (!getBuildingDataMethodResolved)
            {
                Type buildingManagerType = GetBuildingManagerType();
                if (buildingManagerType != null)
                {
                    cachedGetBuildingDataMethod = buildingManagerType.GetMethod(
                        "GetBuildingData",
                        BindingFlags.NonPublic | BindingFlags.Static);
                }

                getBuildingDataMethodResolved = true;
            }

            return cachedGetBuildingDataMethod;
        }

        private static Type GetBuildingType()
        {
            if (!buildingTypeResolved)
            {
                cachedBuildingType = FindGameType("Duckov.Buildings.Building");
                buildingTypeResolved = true;
            }

            return cachedBuildingType;
        }

        private static PropertyInfo GetBuildingIdProperty()
        {
            if (!buildingIdPropertyResolved)
            {
                Type buildingType = GetBuildingType();
                if (buildingType != null)
                {
                    cachedBuildingIdProperty = buildingType.GetProperty("ID");
                }

                buildingIdPropertyResolved = true;
            }

            return cachedBuildingIdProperty;
        }

        private void ResetWeddingBuildingLocationCache()
        {
            cachedWeddingBuildingTransform = null;
            cachedWeddingNpcSpawnPoint = null;
        }

        private void SetWeddingBuildingPresence(bool isPresent)
        {
            weddingBuildingPresenceKnown = true;
            cachedWeddingBuildingPresent = isPresent;

            if (!isPresent)
            {
                ResetWeddingBuildingLocationCache();
            }
        }

        private bool RefreshWeddingBuildingPresence()
        {
            try
            {
                MethodInfo anyMethod = GetBuildingManagerAnyMethod();
                bool isPresent = anyMethod != null
                    && anyMethod.Invoke(null, new object[] { WEDDING_BUILDING_ID, false }) is bool result
                    && result;

                SetWeddingBuildingPresence(isPresent);
                return isPresent;
            }
            catch (Exception e)
            {
                SetWeddingBuildingPresence(false);
                ModBehaviour.LogError("[WeddingBuilding] 刷新建筑存在状态失败: " + e.Message);
                return false;
            }
        }

        private bool TryUseCachedWeddingNpcPosition(out Vector3 position)
        {
            if (cachedWeddingNpcSpawnPoint != null)
            {
                position = cachedWeddingNpcSpawnPoint.position;
                return true;
            }

            if (cachedWeddingBuildingTransform != null)
            {
                EnsureWeddingBuildingFunctionPoints(cachedWeddingBuildingTransform.gameObject);
                Transform spawnPoint = cachedWeddingBuildingTransform.Find("Function/NPCSpawnPoint");
                if (spawnPoint != null)
                {
                    cachedWeddingNpcSpawnPoint = spawnPoint;
                    position = spawnPoint.position;
                    return true;
                }

                position = cachedWeddingBuildingTransform.TransformPoint(WEDDING_NPC_OFFSET);
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        // ============================================================================
        // 公共接口
        // ============================================================================

        /// <summary>
        /// 初始化婚礼建筑系统
        /// 在基地场景加载后调用，注入建筑数据到原版系统
        /// 要求：任意一个NPC好感度达到10级后才允许制造
        /// </summary>
        public void InitWeddingBuilding()
        {
            try
            {
                if (weddingBuildingInjected)
                {
                    DevLog("[WeddingBuilding] 建筑数据已注入，跳过");
                    return;
                }

                // 检查好感度解锁条件：任意NPC历史上达到过10级（基于持久化标记，不受衰减影响）
                if (!AffinityManager.HasAnyNPCEverReachedMaxLevel())
                {
                    DevLog("[WeddingBuilding] 尚未有NPC好感度达到过" + WEDDING_BUILDING_REQUIRED_AFFINITY_LEVEL + "级，婚礼建筑暂不解锁");
                    return;
                }

                // 注入建筑本地化（确保建造UI显示正确名称）
                LocalizationInjector.InjectWeddingBuildingLocalization();

                // 加载建筑图标（用于建造UI按钮显示）
                LoadWeddingBuildingIcon();

                // 加载婚礼建筑模型（AssetBundle 或临时占位）
                LoadWeddingBuildingModel();

                // 创建 Building 预制体
                CreateWeddingBuildingPrefab();

                // 注入到 BuildingDataCollection
                InjectWeddingBuildingData();

                // 注册建筑放置事件监听
                RegisterWeddingBuildingEvents();

                weddingBuildingInjected = true;
                if (RefreshWeddingBuildingPresence())
                {
                    RequestWeddingBuildingAreaRepaint("InitWeddingBuilding");
                }
                else
                {
                    DevLog("[WeddingBuilding] 当前存档未放置婚礼教堂，跳过建筑区重绘");
                }
                DevLog("[WeddingBuilding] 婚礼教堂建筑系统初始化完成（好感度条件已满足）");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 初始化失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void TryInitializeWeddingBuildingEarly()
        {
            try
            {
                if (weddingBuildingInjected)
                {
                    return;
                }

                UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name))
                {
                    return;
                }

                if (!AffinityManager.HasAnyNPCEverReachedMaxLevel())
                {
                    return;
                }

                Type buildingDataCollectionType = FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (buildingDataCollectionType == null)
                {
                    return;
                }

                PropertyInfo instanceProperty = buildingDataCollectionType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null || instanceProperty.GetValue(null, null) == null)
                {
                    return;
                }

                InitWeddingBuilding();
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] 早期初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理婚礼建筑系统
        /// 在场景卸载或mod销毁时调用
        /// </summary>
        public void CleanupWeddingBuilding()
        {
            try
            {
                UnregisterWeddingBuildingEvents();
                SetWeddingBuildingPresence(false);
                AssetBundleUnloadHelper.TryUnload(weddingAssetBundle, "[WeddingBuilding]");
                weddingAssetBundle = null;
                weddingModelPrefab = null;

                // 清理NPC实例
                if (weddingNPCInstance != null)
                {
                    UnityEngine.Object.Destroy(weddingNPCInstance);
                    weddingNPCInstance = null;
                }

                DevLog("[WeddingBuilding] 婚礼建筑系统已清理");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 清理失败: " + e.Message);
            }
        }

        // ============================================================================
        // 图标加载
        // ============================================================================

        /// <summary>
        /// 加载建筑图标 PNG 文件为 Sprite
        /// 用于建造UI按钮上的图标显示
        /// </summary>
        private void LoadWeddingBuildingIcon()
        {
            if (weddingBuildingIcon != null)
            {
                DevLog("[WeddingBuilding] 图标已缓存，跳过加载");
                return;
            }

            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string iconPath = Path.Combine(modDir, "Assets", "buildings", "wedding_chapel.png");

                if (File.Exists(iconPath))
                {
                    byte[] imageData = File.ReadAllBytes(iconPath);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(imageData))
                    {
                        weddingBuildingIcon = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f)
                        );
                        DevLog("[WeddingBuilding] 建筑图标加载成功: " + iconPath + " (" + texture.width + "x" + texture.height + ")");
                    }
                    else
                    {
                        DevLog("[WeddingBuilding] 图标 PNG 解码失败: " + iconPath);
                    }
                }
                else
                {
                    DevLog("[WeddingBuilding] 未找到建筑图标文件: " + iconPath + "，建造UI将显示空白图标");
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] 加载建筑图标异常: " + e.Message);
            }
        }

        // ============================================================================
        // 模型加载
        // ============================================================================

        /// <summary>
        /// 加载婚礼建筑3D模型
        /// 优先从 AssetBundle 加载，失败则使用临时占位模型
        /// </summary>
        private void LoadWeddingBuildingModel()
        {
            if (weddingModelPrefab != null)
            {
                DevLog("[WeddingBuilding] 模型已缓存，跳过加载");
                return;
            }

            // 尝试从 AssetBundle 加载
            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "buildings", "weddingchapel");

                if (File.Exists(bundlePath))
                {
                    DevLog("[WeddingBuilding] 尝试加载 AssetBundle: " + bundlePath);

                    if (weddingAssetBundle != null)
                    {
                        AssetBundleUnloadHelper.TryUnload(weddingAssetBundle, "[WeddingBuilding]");
                        weddingAssetBundle = null;
                    }

                    weddingAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                    if (weddingAssetBundle != null)
                    {
                        // 列出 bundle 中所有资源名称，方便调试
                        string[] allNames = weddingAssetBundle.GetAllAssetNames();
                        DevLog("[WeddingBuilding] AssetBundle 已加载，包含 " + allNames.Length + " 个资源:");
                        for (int i = 0; i < allNames.Length; i++)
                        {
                            DevLog("[WeddingBuilding]   [" + i + "] " + allNames[i]);
                        }

                        weddingModelPrefab = weddingAssetBundle.LoadAsset<GameObject>("WeddingChapel");
                        if (weddingModelPrefab != null)
                        {
                            DevLog("[WeddingBuilding] AssetBundle 模型加载成功");
                            return;
                        }

                        // 尝试加载第一个资源
                        string[] assetNames = weddingAssetBundle.GetAllAssetNames();
                        if (assetNames.Length > 0)
                        {
                            weddingModelPrefab = weddingAssetBundle.LoadAsset<GameObject>(assetNames[0]);
                            if (weddingModelPrefab != null)
                            {
                                DevLog("[WeddingBuilding] 从 AssetBundle 加载了: " + assetNames[0]);
                                return;
                            }
                        }
                    }

                    DevLog("[WeddingBuilding] AssetBundle 加载失败，使用临时占位模型");
                }
                else
                {
                    DevLog("[WeddingBuilding] 未找到 AssetBundle 文件: " + bundlePath + "，使用临时占位模型");
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] AssetBundle 加载异常: " + e.Message + "，使用临时占位模型");
            }

            // AssetBundle 不可用，不创建临时模型（后续在 CreateWeddingBuildingPrefab 中处理）
            weddingModelPrefab = null;
        }

        // ============================================================================
        // 预制体创建
        // ============================================================================

        /// <summary>
        /// 创建婚礼建筑的 Building 预制体
        /// 结构：WeddingChapel → Graphics(视觉) + Function(功能/NPC站位)
        ///
        /// 重要：预制体构建阶段先保持 inactive，等容器/字段补齐后再激活。
        /// 这样可以避免 Building.Awake() 在 Graphics/Function 尚未准备好时提前执行。
        /// 对实例化出的克隆体，则预先填充容器字段，避免再次出现错误引用。
        /// </summary>
        private void CreateWeddingBuildingPrefab()
        {
            if (weddingBuildingPrefabGO != null)
            {
                DevLog("[WeddingBuilding] 预制体已存在，跳过创建");
                return;
            }

            // 创建根物体（先保持 inactive，避免 Building.Awake 在字段尚未补齐时提前触发）
            weddingBuildingPrefabGO = new GameObject(WEDDING_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(weddingBuildingPrefabGO);
            weddingBuildingPrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            weddingBuildingPrefabGO.SetActive(false);

            // 创建 Graphics 容器（视觉模型）
            // 名称必须是 "Graphics"，Building.Awake() 会通过 Find("Graphics") 查找
            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(weddingBuildingPrefabGO.transform, false);

            if (weddingModelPrefab != null)
            {
                // 从 AssetBundle 加载的模型
                GameObject modelInstance = UnityEngine.Object.Instantiate(weddingModelPrefab, graphicsContainer.transform);
                modelInstance.name = "Model";
                modelInstance.SetActive(true);

                // 诊断日志
                DevLog("[WeddingBuilding] 使用 AssetBundle 模型，子物体数量: " + modelInstance.transform.childCount);
                DevLog("[WeddingBuilding] 模型 localScale: " + modelInstance.transform.localScale);

                // 检查 Renderer 信息
                Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                DevLog("[WeddingBuilding] Renderer 数量: " + renderers.Length);

                // 计算模型的合并 bounds（用于自动缩放）
                Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
                bool hasBounds = false;
                foreach (Renderer r in renderers)
                {
                    DevLog("[WeddingBuilding]   Renderer: " + r.gameObject.name
                        + " | bounds.size=" + r.bounds.size
                        + " | enabled=" + r.enabled
                        + " | shader=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "NULL"));

                    if (!hasBounds)
                    {
                        combinedBounds = new Bounds(r.bounds.center, r.bounds.size);
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(r.bounds);
                    }
                }

                // 自动缩放：目标约 2.5m 宽（3x3格子建筑）
                float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                DevLog("[WeddingBuilding] 模型合并 bounds: size=" + combinedBounds.size + " maxDim=" + maxDim);

                if (maxDim > 0.001f && maxDim < 2.5f)
                {
                    // 模型偏小，放大到约 2.5m
                    float scaleFactor = 2.5f / maxDim;
                    modelInstance.transform.localScale *= scaleFactor;
                    DevLog("[WeddingBuilding] 模型偏小，放大 " + scaleFactor + " 倍");
                }
                else if (maxDim > 10f)
                {
                    float scaleFactor = 3f / maxDim;
                    modelInstance.transform.localScale *= scaleFactor;
                    DevLog("[WeddingBuilding] 模型过大，缩小到 " + scaleFactor + " 倍");
                }

                // 重新计算缩放后的 bounds，对齐底部到 y=0
                combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
                hasBounds = false;
                renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer r in renderers)
                {
                    if (!hasBounds)
                    {
                        combinedBounds = new Bounds(r.bounds.center, r.bounds.size);
                        hasBounds = true;
                    }
                    else
                    {
                        combinedBounds.Encapsulate(r.bounds);
                    }
                }
                // 将模型底部对齐到 Graphics 容器的 y=0（即地面）
                float modelBottomLocal = combinedBounds.min.y - graphicsContainer.transform.position.y;
                modelInstance.transform.localPosition = new Vector3(0f, -modelBottomLocal, 0f);
                DevLog("[WeddingBuilding] 缩放后 bounds: " + combinedBounds.size + " 底部对齐偏移: " + (-modelBottomLocal));

                // 修复模型材质：将 Standard shader 替换为游戏兼容的 shader
                // AssetBundle 中的材质使用 Built-in Standard Shader，在 URP 游戏中会显示为透明
                // 解决方案：运行时替换为游戏已有的 shader（通过 Shader.Find 获取）
                FixWeddingModelShaders(modelInstance);

                // 为模型添加碰撞体（BoxCollider）
                // 游戏中的建筑在 Graphics 容器下都有 Collider，用于：
                // 1. 阻挡玩家和子弹穿过建筑
                // 2. BuildingArea.PhysicsCollide() 检测放置位置冲突
                // 注意：SetupPreview() 会在预览时禁用这些 Collider，放置后保持启用
                BoxCollider boxCol = modelInstance.AddComponent<BoxCollider>();
                // 根据缩放后的模型 bounds 设置碰撞体大小
                // combinedBounds 是世界空间的，需要转换为本地空间
                Renderer firstRenderer = modelInstance.GetComponentInChildren<Renderer>();
                if (firstRenderer != null)
                {
                    // 用 Renderer bounds 计算本地空间的碰撞体
                    Bounds localBounds = firstRenderer.bounds;
                    boxCol.center = modelInstance.transform.InverseTransformPoint(localBounds.center);
                    boxCol.size = new Vector3(
                        localBounds.size.x / modelInstance.transform.lossyScale.x,
                        localBounds.size.y / modelInstance.transform.lossyScale.y,
                        localBounds.size.z / modelInstance.transform.lossyScale.z);
                    DevLog("[WeddingBuilding] 已添加 BoxCollider: center=" + boxCol.center + " size=" + boxCol.size);
                }
            }
            else
            {
                // 临时占位模型：一个简单的立方体
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                placeholder.name = "PlaceholderModel";
                placeholder.transform.SetParent(graphicsContainer.transform, false);
                placeholder.transform.localScale = new Vector3(2.5f, 3f, 2.5f);
                placeholder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

                // 设置占位颜色（粉色，婚礼主题）
                Renderer renderer = placeholder.GetComponent<Renderer>();
                if (renderer != null)
                {
                    // 优先使用 Unlit/Color（不受光照影响，确保可见）
                    Shader shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Standard");
                    if (shader == null) shader = Shader.Find("Sprites/Default");

                    if (shader != null)
                    {
                        renderer.material = new Material(shader);
                        renderer.material.color = new Color(1f, 0.75f, 0.8f, 1f);
                    }
                    else
                    {
                        renderer.material.color = new Color(1f, 0.75f, 0.8f, 1f);
                    }
                    DevLog("[WeddingBuilding] 占位模型 Shader: " + renderer.material.shader.name);
                }

                // 移除碰撞体（占位模型不需要物理碰撞）
                Collider col = placeholder.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col);

                DevLog("[WeddingBuilding] 使用临时占位模型（粉色立方体）");
            }

            // 创建 Function 容器（功能元素）
            // 名称必须是 "Function"，Building.Awake() 会通过 Find("Function") 查找
            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(weddingBuildingPrefabGO.transform, false);

            // 创建 NPC 站位点
            GameObject npcSpawnPoint = new GameObject("NPCSpawnPoint");
            npcSpawnPoint.transform.SetParent(functionContainer.transform, false);
            npcSpawnPoint.transform.localPosition = WEDDING_NPC_OFFSET;

            // 创建教堂交互点
            GameObject interactPoint = new GameObject("WeddingChapelInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);
            interactPoint.transform.localPosition = WEDDING_INTERACT_OFFSET;

            // 添加 Building 组件，并在激活前补齐容器引用
            AddBuildingComponent(weddingBuildingPrefabGO);
            weddingBuildingPrefabGO.SetActive(true);

            DevLog("[WeddingBuilding] 预制体创建完成，尺寸: " + WEDDING_BUILDING_SIZE);
        }

        /// <summary>
        /// 修复婚礼建筑模型的 Shader
        /// AssetBundle 中的材质使用 Built-in Standard Shader，在 URP 游戏中会显示为透明/粉红色
        /// 解决方案：运行时替换为游戏已有的 shader
        /// </summary>
        private void FixWeddingModelShaders(GameObject modelInstance)
        {
            try
            {
                // 尝试获取游戏使用的 Shader（按优先级尝试）
                // 1. SodaCraft/SodaCharacter - 游戏角色/NPC 使用的 shader
                // 2. Universal Render Pipeline/Lit - URP 标准 lit shader
                // 3. Universal Render Pipeline/Simple Lit - URP 简化 lit shader
                // 4. Unlit/Texture - 无光照但保留贴图（最后回退）
                Shader targetShader = null;
                string[] shaderCandidates = new string[]
                {
                    "Universal Render Pipeline/Lit",
                    "Universal Render Pipeline/Simple Lit",
                    "SodaCraft/SodaCharacter",
                    "Unlit/Texture"
                };

                foreach (string shaderName in shaderCandidates)
                {
                    targetShader = Shader.Find(shaderName);
                    if (targetShader != null)
                    {
                        DevLog("[WeddingBuilding] 找到目标 Shader: " + shaderName);
                        break;
                    }
                }

                if (targetShader == null)
                {
                    DevLog("[WeddingBuilding] 警告：未找到任何兼容的 Shader，保持原始材质");
                    return;
                }

                Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.materials != null)
                    {
                        Material[] materials = renderer.materials;
                        for (int i = 0; i < materials.Length; i++)
                        {
                            Material mat = materials[i];
                            if (mat != null && mat.shader != null)
                            {
                                string originalShaderName = mat.shader.name;

                                // 如果是 Standard shader 或其变体，替换为目标 shader
                                if (originalShaderName == "Standard" ||
                                    originalShaderName.Contains("Standard") ||
                                    originalShaderName == "Hidden/InternalErrorShader")
                                {
                                    // 保存原始贴图
                                    Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                                    Texture normalMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                                    Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;

                                    // 创建新材质
                                    Material newMat = new Material(targetShader);

                                    // 恢复贴图（URP shader 使用 _BaseMap 而不是 _MainTex）
                                    if (mainTex != null)
                                    {
                                        if (newMat.HasProperty("_BaseMap"))
                                            newMat.SetTexture("_BaseMap", mainTex);
                                        if (newMat.HasProperty("_MainTex"))
                                            newMat.SetTexture("_MainTex", mainTex);
                                    }

                                    // 恢复法线贴图
                                    if (normalMap != null && newMat.HasProperty("_BumpMap"))
                                    {
                                        newMat.SetTexture("_BumpMap", normalMap);
                                    }

                                    // 恢复颜色
                                    if (newMat.HasProperty("_BaseColor"))
                                        newMat.SetColor("_BaseColor", color);
                                    if (newMat.HasProperty("_Color"))
                                        newMat.SetColor("_Color", color);

                                    materials[i] = newMat;
                                    DevLog("[WeddingBuilding] Renderer '" + renderer.gameObject.name
                                        + "' Shader 已替换: " + originalShaderName + " -> " + targetShader.name);
                                }
                            }
                        }
                        renderer.materials = materials;
                    }

                    // 确保 Renderer 在正确的渲染层
                    renderer.gameObject.layer = 0; // Default 层
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] 修复 Shader 出错: " + e.Message);
            }
        }

        /// <summary>
        /// 通过反射为 GameObject 添加 Building 组件并设置字段
        /// 只设置 id 和 dimensions，不设置 graphicsContainer/functionContainer
        /// 让 Building.Awake() 通过 Find("Graphics")/Find("Function") 自动查找克隆体的子物体
        /// </summary>
        private void AddBuildingComponent(GameObject go)
        {
            // 获取 Building 类型
            Type buildingType = FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] 无法找到 Building 类型");
                return;
            }

            // 根物体当前保持 inactive，这里添加组件不会立刻触发 Awake，
            // 可以先把 Building 依赖字段补齐，再统一激活。
            Component buildingComp = go.AddComponent(buildingType);

            // 设置私有字段
            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            // id 字段
            FieldInfo idField = buildingType.GetField("id", privateFlags);
            if (idField != null) idField.SetValue(buildingComp, WEDDING_BUILDING_ID);

            // dimensions 字段
            FieldInfo dimField = buildingType.GetField("dimensions", privateFlags);
            if (dimField != null) dimField.SetValue(buildingComp, WEDDING_BUILDING_SIZE);

            FieldInfo graphicsField = buildingType.GetField("graphicsContainer", privateFlags);
            if (graphicsField != null)
            {
                AssignBuildingContainerField(graphicsField, buildingComp, go.transform.Find("Graphics"));
            }

            FieldInfo functionField = buildingType.GetField("functionContainer", privateFlags);
            if (functionField != null)
            {
                AssignBuildingContainerField(functionField, buildingComp, go.transform.Find("Function"));
            }

            // areaMesh 维持为空，让实例在自己的 Awake 中按需创建
            FieldInfo areaMeshField = buildingType.GetField("areaMesh", privateFlags);
            if (areaMeshField != null) areaMeshField.SetValue(buildingComp, null);

            // 诊断：打印预制体完整层级树
            DumpGameObjectHierarchy(go, 0);

            DevLog("[WeddingBuilding] Building 组件已添加，ID=" + WEDDING_BUILDING_ID + "（容器引用已预填充）");
        }

        private void AssignBuildingContainerField(FieldInfo field, Component buildingComp, Transform container)
        {
            if (field == null || buildingComp == null)
            {
                return;
            }

            if (container == null)
            {
                field.SetValue(buildingComp, null);
                return;
            }

            Type fieldType = field.FieldType;
            if (typeof(Transform).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container);
            }
            else if (typeof(GameObject).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container.gameObject);
            }
            else if (typeof(Component).IsAssignableFrom(fieldType))
            {
                field.SetValue(buildingComp, container.GetComponent(fieldType));
            }
            else
            {
                field.SetValue(buildingComp, container);
            }
        }

        /// <summary>
        /// 递归打印 GameObject 层级树（用于诊断）
        /// </summary>
        private void DumpGameObjectHierarchy(GameObject go, int depth)
        {
            string indent = new string(' ', depth * 2);
            string components = "";
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c != null) components += c.GetType().Name + ", ";
            }
            DevLog("[WeddingBuilding] " + indent + go.name
                + " [active=" + go.activeSelf + "] (" + components.TrimEnd(',', ' ') + ")");

            for (int i = 0; i < go.transform.childCount; i++)
            {
                DumpGameObjectHierarchy(go.transform.GetChild(i).gameObject, depth + 1);
            }
        }

        // ============================================================================
        // 数据注入
        // ============================================================================

        /// <summary>
        /// 将婚礼建筑数据注入到 BuildingDataCollection
        /// 通过反射访问 private List 字段，直接 Add 数据
        /// </summary>
    }
}
