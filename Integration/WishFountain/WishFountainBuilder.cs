// ============================================================================
// WishFountainBuilder.cs - 布满了灰尘的星愿许愿台建筑注入器
// ============================================================================
// 模块说明：
//   通过反射将布满了灰尘的星愿许愿台建筑注入到游戏原版建筑系统中，
//   使其出现在基地地堡的建造UI中。
//   参考 WeddingBuildingInjector.cs 的建筑注入模式。
//
// 技术方案：
//   - 纯反射注入 BuildingDataCollection 的 infos 和 prefabs 列表
//   - 动态创建 Building prefab（占位模型 + 粒子特效）
//   - 监听 Building 放置/拆除事件，管理交互点和粒子
//   - 预创建 WishFountainView，避免首次交互时再进行运行时 UI 装配
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>
    /// 布满了灰尘的星愿许愿台建筑注入器（partial class ModBehaviour）
    /// </summary>
    public partial class ModBehaviour
    {
        // ============================================================================
        // 常量
        // ============================================================================

        /// <summary>建筑ID</summary>
        private const string STARWISH_BUILDING_ID = "starwish_fountain";

        /// <summary>建筑预制体名称</summary>
        private const string STARWISH_PREFAB_NAME = "StarWishFountain";

        /// <summary>占位 AssetBundle 文件标记</summary>
        private const string STARWISH_BUNDLE_PLACEHOLDER_MARKER = "STARWISH_PLACEHOLDER_BUNDLE";

        /// <summary>建筑占地尺寸</summary>
        private static readonly Vector2Int STARWISH_BUILDING_SIZE = new Vector2Int(2, 2);

        /// <summary>建筑费用</summary>
        private const long STARWISH_BUILDING_COST = 1000;

        /// <summary>最大建造数量</summary>
        private const int STARWISH_BUILDING_MAX_AMOUNT = 1;

        /// <summary>交互点偏移</summary>
        private static readonly Vector3 STARWISH_INTERACT_OFFSET = new Vector3(0f, 0f, 0f);

        /// <summary>AssetBundle 模型目标最大尺寸</summary>
        private const float STARWISH_MODEL_TARGET_MAX_DIM = 2.2f;

        // ============================================================================
        // 状态
        // ============================================================================

        /// <summary>是否已注入</summary>
        private bool starwishBuildingInjected = false;

        /// <summary>预制体缓存</summary>
        private GameObject starwishBuildingPrefabGO = null;

        /// <summary>建筑图标</summary>
        private static Sprite starwishBuildingIcon = null;

        /// <summary>AssetBundle 缓存</summary>
        private static AssetBundle starwishAssetBundle = null;

        /// <summary>从 AssetBundle 加载的模型</summary>
        private static GameObject starwishModelPrefab = null;

        /// <summary>场景内恢复交互点的协程句柄</summary>
        private Coroutine starwishRestoreCoroutine = null;

        /// <summary>当前场景已处理过的建筑实例缓存</summary>
        private readonly HashSet<int> preparedStarwishBuildingInstanceIds = new HashSet<int>();

        /// <summary>已处理建筑缓存对应的场景句柄</summary>
        private int preparedStarwishSceneHandle = int.MinValue;

        private static readonly Color StardustIceCoreColor = new Color(0.62f, 0.86f, 1f, 0.9f);
        private static readonly Color StardustIceFadeColor = new Color(0.82f, 0.96f, 1f, 0.66f);
        private static readonly Color TwinkleIceCoreColor = new Color(0.88f, 0.97f, 1f, 0.98f);
        private static readonly Color TwinkleIceGlowColor = new Color(0.55f, 0.82f, 1f, 0.8f);

        // ============================================================================
        // 公共接口
        // ============================================================================

        /// <summary>
        /// 初始化布满了灰尘的星愿许愿台建筑系统
        /// </summary>
        public void InitWishFountainBuilding()
        {
            InitWishFountainBuilding(false);
        }

        private void InitWishFountainBuilding(bool isEarlyInit)
        {
            try
            {
                if (starwishBuildingInjected)
                {
                    EnsureWishFountainView();
                    DevLog("[WishFountain] 建筑数据已注入，跳过");
                    return;
                }

                // 注入本地化
                LocalizationInjector.InjectWishFountainLocalization();

                // 加载图标
                LoadStarwishBuildingIcon();

                // 加载建筑模型（AssetBundle 或占位）
                LoadStarwishBuildingModel();

                // 创建预制体
                CreateStarwishBuildingPrefab();

                // 注入建筑数据
                InjectStarwishBuildingData();

                // 注册事件
                RegisterStarwishBuildingEvents();

                // 预创建原版风格许愿面板，避免首次交互时触发 View 运行时装配副作用
                EnsureWishFountainView();

                starwishBuildingInjected = true;
                if (!isEarlyInit && HasPendingStarwishBuildingsInManager())
                {
                    RequestBaseBuildingAreaRepaint("InitWishFountainBuilding");
                }
                DevLog("[WishFountain] 布满了灰尘的星愿许愿台建筑系统初始化完成");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WishFountain] 初始化失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 在基地场景尽早注入许愿台建筑数据，避免已有存档在 BuildingArea.Start 阶段先报缺 prefab
        /// </summary>
        private void TryInitializeWishFountainEarly()
        {
            try
            {
                if (starwishBuildingInjected)
                {
                    return;
                }

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name))
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

                InitWishFountainBuilding(true);
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 早期初始化失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理布满了灰尘的星愿许愿台建筑系统
        /// </summary>
        public void CleanupWishFountainBuilding()
        {
            try
            {
                if (starwishRestoreCoroutine != null)
                {
                    StopCoroutine(starwishRestoreCoroutine);
                    starwishRestoreCoroutine = null;
                }

                ResetStarwishPreparedBuildingCache();
                UnregisterStarwishBuildingEvents();
                AssetBundleUnloadHelper.TryUnload(starwishAssetBundle, "[WishFountain]");
                starwishAssetBundle = null;
                starwishModelPrefab = null;
                DevLog("[WishFountain] 布满了灰尘的星愿许愿台建筑系统已清理");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WishFountain] 清理失败: " + e.Message);
            }
        }

        // ============================================================================
        // 图标
        // ============================================================================

        private void LoadStarwishBuildingIcon()
        {
            if (starwishBuildingIcon != null) return;

            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string iconPath = Path.Combine(modDir, "Assets", "buildings", "starwish_fountain.png");

                if (File.Exists(iconPath))
                {
                    starwishBuildingIcon = ItemFactory.GetSpriteFromFile(Path.Combine("Assets", "buildings", Path.GetFileName(iconPath)));
                    if (starwishBuildingIcon != null)
                    {
                        DevLog("[WishFountain] 建筑图标加载成功");
                    }
                }
                else
                {
                    DevLog("[WishFountain] 未找到建筑图标: " + iconPath);
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 加载图标异常: " + e.Message);
            }
        }

        private void LoadStarwishBuildingModel()
        {
            if (starwishModelPrefab != null)
            {
                return;
            }

            try
            {
                string assemblyLocation = typeof(ModBehaviour).Assembly.Location;
                string modDir = Path.GetDirectoryName(assemblyLocation);
                string bundlePath = Path.Combine(modDir, "Assets", "buildings", "starwish_fountain");

                if (!File.Exists(bundlePath))
                {
                    DevLog("[WishFountain] 未找到 AssetBundle 文件，使用占位模型: " + bundlePath);
                    return;
                }

                if (IsStarwishPlaceholderBundle(bundlePath))
                {
                    DevLog("[WishFountain] 检测到占位 AssetBundle 标记文件，使用占位模型");
                    return;
                }

                if (starwishAssetBundle != null)
                {
                    AssetBundleUnloadHelper.TryUnload(starwishAssetBundle, "[WishFountain]");
                    starwishAssetBundle = null;
                }

                starwishAssetBundle = ResourceBundleLoader.LoadFromFile(bundlePath);
                if (starwishAssetBundle == null)
                {
                    DevLog("[WishFountain] AssetBundle 加载失败，使用占位模型: " + bundlePath);
                    return;
                }

                starwishModelPrefab = starwishAssetBundle.LoadAsset<GameObject>(STARWISH_PREFAB_NAME);
                if (starwishModelPrefab != null)
                {
                    DevLog("[WishFountain] AssetBundle 模型加载成功: " + STARWISH_PREFAB_NAME);
                    return;
                }

                string[] assetNames = starwishAssetBundle.GetAllAssetNames();
                if (assetNames != null && assetNames.Length > 0)
                {
                    starwishModelPrefab = starwishAssetBundle.LoadAsset<GameObject>(assetNames[0]);
                    if (starwishModelPrefab != null)
                    {
                        DevLog("[WishFountain] 从 AssetBundle 加载首个模型资源: " + assetNames[0]);
                        return;
                    }
                }

                DevLog("[WishFountain] AssetBundle 中未找到可用的 GameObject，使用占位模型");
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 加载 AssetBundle 异常，使用占位模型: " + e.Message);
            }
        }

        private bool IsStarwishPlaceholderBundle(string bundlePath)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(bundlePath);
                if (!fileInfo.Exists)
                {
                    return false;
                }

                if (fileInfo.Length == 0)
                {
                    return true;
                }

                if (fileInfo.Length > 128)
                {
                    return false;
                }

                string content = File.ReadAllText(bundlePath).Trim();
                return string.Equals(content, STARWISH_BUNDLE_PLACEHOLDER_MARKER, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        // ============================================================================
        // 预制体创建
        // ============================================================================

        private void CreateStarwishBuildingPrefab()
        {
            if (starwishBuildingPrefabGO != null) return;

            // 创建根物体（先 inactive）
            starwishBuildingPrefabGO = new GameObject(STARWISH_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(starwishBuildingPrefabGO);
            starwishBuildingPrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            starwishBuildingPrefabGO.SetActive(false);

            // Graphics 容器
            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(starwishBuildingPrefabGO.transform, false);

            if (starwishModelPrefab != null)
            {
                GameObject modelInstance = UnityEngine.Object.Instantiate(starwishModelPrefab, graphicsContainer.transform);
                modelInstance.name = "Model";
                modelInstance.SetActive(true);

                PrepareStarwishAssetBundleModel(modelInstance, graphicsContainer);

                CreateStardustParticles(graphicsContainer);
                CreateStarTwinkleParticles(graphicsContainer);
            }
            else
            {
                // 占位模型：蓝紫色圆柱体（许愿瓶）+ 顶部小球（星星）
                CreateStarwishPlaceholderModel(graphicsContainer);
            }

            // Function 容器
            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(starwishBuildingPrefabGO.transform, false);

            // 交互点
            GameObject interactPoint = new GameObject("WishInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);
            interactPoint.transform.localPosition = STARWISH_INTERACT_OFFSET;

            // 添加 Building 组件
            AddStarwishBuildingComponent(starwishBuildingPrefabGO);
            EnsureStarwishFunctionPoints(starwishBuildingPrefabGO);
            starwishBuildingPrefabGO.SetActive(true);

            DevLog("[WishFountain] 预制体创建完成");
        }

        /// <summary>
        /// 创建占位模型（蓝紫色圆柱 + 金色小球 + 粒子效果）。只在缺 bundle 时出现。
        /// 上色走官方角色着色器（SodaCharacter 的 _Tint，与基地建筑同一条路），有明暗、吃基地灯光；
        /// 旧版的 Unlit/Color 在游戏里找不到，退到 Standard 在 URP 下画不出来（VA-15）。
        /// </summary>
        private void CreateStarwishPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                CreateStarwishPlaceholderPart(graphicsContainer, PrimitiveType.Cylinder, "WishBottle",
                    new Vector3(0f, 1.2f, 0f), new Vector3(0.8f, 1.2f, 0.8f), new Color(0.36f, 0.3f, 0.62f));
                GameObject star = CreateStarwishPlaceholderPart(graphicsContainer, PrimitiveType.Sphere, "WishStar",
                    new Vector3(0f, 2.7f, 0f), new Vector3(0.35f, 0.35f, 0.35f), new Color(0.95f, 0.8f, 0.42f));
                star.AddComponent<StarwishRotator>();   // 星星缓慢旋转
                CreateStarwishPlaceholderPart(graphicsContainer, PrimitiveType.Cylinder, "BasePlate",
                    new Vector3(0f, 0.05f, 0f), new Vector3(1.2f, 0.1f, 1.2f), new Color(0.3f, 0.27f, 0.36f));

                // 碰撞统一按渲染包围盒补；粒子效果：星尘上升 + 星芒闪烁
                AddStarwishGraphicsCollider(graphicsContainer, CollectStarwishRenderableComponents(graphicsContainer));
                CreateStardustParticles(graphicsContainer);
                CreateStarTwinkleParticles(graphicsContainer);

                DevLog("[WishFountain] 占位模型创建完成（瓶 + 星 + 底座 + 粒子）");
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 占位模型创建异常: " + e.Message);
            }
        }

        private GameObject CreateStarwishPlaceholderPart(GameObject parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Color tint)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent.transform, false);
            part.transform.localScale = localScale;
            part.transform.localPosition = localPosition;

            Renderer renderer = part.GetComponent<Renderer>();
            Material material = BuildingModelHelper.CreateOfficialTintMaterial("StarwishPlaceholder_" + name, tint);
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            // 移除碰撞体（由 Building 自动管理）
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.Destroy(collider);
            return part;
        }

        /// <summary>
        /// 创建星尘粒子效果
        /// </summary>
        private void CreateStardustParticles(GameObject parent)
        {
            try
            {
                GameObject particleGO = new GameObject("StardustParticles");
                particleGO.transform.SetParent(parent.transform, false);
                particleGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);

                ParticleSystem ps = particleGO.AddComponent<ParticleSystem>();

                // 主模块
                var main = ps.main;
                main.startLifetime = 3f;
                main.startSpeed = 0.3f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
                main.maxParticles = 42;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startColor = new ParticleSystem.MinMaxGradient(
                    StardustIceCoreColor,
                    StardustIceFadeColor
                );

                // 发射模块
                var emission = ps.emission;
                emission.rateOverTime = 11f;

                // 形状模块
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.75f;
                shape.rotation = new Vector3(90f, 0f, 0f);

                // 速度模块 - 向上漂浮
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                vel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

                // 大小随生命周期
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                AnimationCurve sizeCurve = new AnimationCurve();
                sizeCurve.AddKey(0f, 0.5f);
                sizeCurve.AddKey(0.5f, 1f);
                sizeCurve.AddKey(1f, 0f);
                sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

                // 透明度随生命周期
                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(StardustIceCoreColor, 0f),
                        new GradientColorKey(TwinkleIceCoreColor, 0.65f),
                        new GradientColorKey(StardustIceFadeColor, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.8f, 0.3f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                col.color = new ParticleSystem.MinMaxGradient(gradient);

                // Renderer 设置
                var renderer = particleGO.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    // 亮核光点、加色、亮档（进 HDR 吃官方泛光）；原色相走顶点色，不再被 _TintColor 再翻一倍推成白
                    Material particleMat = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.GlowDot, BossRushFxBlend.Additive, BossRushFxKit.GainBright);
                    renderer.sharedMaterial = particleMat;
                    renderer.enabled = particleMat != null;
                }

                DevLog("[WishFountain] 星尘粒子效果已创建");
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 粒子效果创建异常: " + e.Message);
            }
        }

        private void CreateStarTwinkleParticles(GameObject parent)
        {
            try
            {
                GameObject particleGO = new GameObject("StarTwinkleParticles");
                particleGO.transform.SetParent(parent.transform, false);
                particleGO.transform.localPosition = new Vector3(0f, 2.2f, 0f);

                ParticleSystem ps = particleGO.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 0.55f;
                main.startSpeed = 0.02f;
                main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.2f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.maxParticles = 12;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.startColor = TwinkleIceCoreColor;

                var emission = ps.emission;
                emission.rateOverTime = 0.65f;

                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.12f;

                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                AnimationCurve sizeCurve = new AnimationCurve();
                sizeCurve.AddKey(0f, 0f);
                sizeCurve.AddKey(0.2f, 1f);
                sizeCurve.AddKey(1f, 0f);
                sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

                var col = ps.colorOverLifetime;
                col.enabled = true;
                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(TwinkleIceCoreColor, 0f),
                        new GradientColorKey(TwinkleIceGlowColor, 0.55f),
                        new GradientColorKey(StardustIceCoreColor, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                col.color = new ParticleSystem.MinMaxGradient(gradient);

                var renderer = particleGO.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Billboard;
                    // 星芒（四主芒 + 四副芒）、加色、热档：闪烁读作一颗星，而不是一团软圆斑
                    Material particleMat = BossRushFxKit.GetShapeMaterial(BossRushParticleShape.Star, BossRushFxBlend.Additive, BossRushFxKit.GainHot);
                    renderer.sharedMaterial = particleMat;
                    renderer.enabled = particleMat != null;
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 星光闪烁特效创建异常: " + e.Message);
            }
        }

        private void PrepareStarwishAssetBundleModel(GameObject modelInstance, GameObject graphicsContainer)
        {
            try
            {
                if (modelInstance == null || graphicsContainer == null)
                {
                    return;
                }

                Renderer[] renderers = CollectStarwishRenderableComponents(modelInstance);
                if (renderers.Length == 0)
                {
                    DevLog("[WishFountain] AssetBundle 模型未找到可用 Renderer，跳过模型修整");
                    return;
                }

                Bounds combinedBounds;
                if (!TryGetCombinedBounds(renderers, out combinedBounds))
                {
                    return;
                }

                float maxDim = Mathf.Max(combinedBounds.size.x, combinedBounds.size.y, combinedBounds.size.z);
                if (maxDim > 0.001f && maxDim < STARWISH_MODEL_TARGET_MAX_DIM)
                {
                    float scaleFactor = STARWISH_MODEL_TARGET_MAX_DIM / maxDim;
                    modelInstance.transform.localScale *= scaleFactor;
                    DevLog("[WishFountain] AssetBundle 模型偏小，放大 " + scaleFactor + " 倍");
                }
                else if (maxDim > 6f)
                {
                    float scaleFactor = STARWISH_MODEL_TARGET_MAX_DIM / maxDim;
                    modelInstance.transform.localScale *= scaleFactor;
                    DevLog("[WishFountain] AssetBundle 模型过大，缩小到 " + scaleFactor + " 倍");
                }

                renderers = CollectStarwishRenderableComponents(modelInstance);
                if (TryGetCombinedBounds(renderers, out combinedBounds))
                {
                    float modelBottomLocal = combinedBounds.min.y - graphicsContainer.transform.position.y;
                    modelInstance.transform.localPosition = new Vector3(0f, -modelBottomLocal, 0f);
                    DevLog("[WishFountain] AssetBundle 模型底部对齐偏移: " + (-modelBottomLocal));
                }

                FixStarwishModelShaders(modelInstance);
                AddStarwishGraphicsCollider(modelInstance, CollectStarwishRenderableComponents(modelInstance));
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 修整 AssetBundle 模型失败: " + e.Message);
            }
        }

        private Renderer[] CollectStarwishRenderableComponents(GameObject root)
        {
            return BuildingModelHelper.CollectStarwishRenderableComponents(root);
        }

        private bool TryGetCombinedBounds(Renderer[] renderers, out Bounds combinedBounds)
        {
            return BuildingModelHelper.TryGetCombinedBounds(renderers, out combinedBounds);
        }

        private void AddStarwishGraphicsCollider(GameObject target, Renderer[] renderers)
        {
            BuildingModelHelper.AddStarwishGraphicsCollider(target, renderers);
        }

        private void FixStarwishModelShaders(GameObject modelInstance)
        {
            BuildingModelHelper.FixStarwishModelShaders(modelInstance);
        }
    }
}
