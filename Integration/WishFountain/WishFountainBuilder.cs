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
using UnityEngine;

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

        /// <summary>布满了灰尘的星愿许愿台粒子共享材质</summary>
        private static Material starwishParticleMaterial = null;

        /// <summary>布满了灰尘的星愿许愿台粒子共享纹理</summary>
        private static Texture2D starwishParticleTexture = null;

        private static readonly Color StarwishParticleTint = new Color(0.76f, 0.93f, 1f, 0.96f);
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
            try
            {
                if (starwishBuildingInjected)
                {
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
                DevLog("[WishFountain] 布满了灰尘的星愿许愿台建筑系统初始化完成");
            }
            catch (Exception e)
            {
                Debug.LogError("[WishFountain] 初始化失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 清理布满了灰尘的星愿许愿台建筑系统
        /// </summary>
        public void CleanupWishFountainBuilding()
        {
            try
            {
                UnregisterStarwishBuildingEvents();
                DevLog("[WishFountain] 布满了灰尘的星愿许愿台建筑系统已清理");
            }
            catch (Exception e)
            {
                Debug.LogError("[WishFountain] 清理失败: " + e.Message);
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
                    byte[] imageData = File.ReadAllBytes(iconPath);
                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(imageData))
                    {
                        starwishBuildingIcon = Sprite.Create(
                            texture,
                            new Rect(0, 0, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f)
                        );
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
                    starwishAssetBundle.Unload(false);
                    starwishAssetBundle = null;
                }

                starwishAssetBundle = AssetBundle.LoadFromFile(bundlePath);
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
        /// 创建占位模型（蓝紫色圆柱 + 金色小球 + 粒子效果）
        /// </summary>
        private void CreateStarwishPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                // 获取 shader
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                // 底座圆柱
                GameObject bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bottle.name = "WishBottle";
                bottle.transform.SetParent(graphicsContainer.transform, false);
                bottle.transform.localScale = new Vector3(0.8f, 1.2f, 0.8f);
                bottle.transform.localPosition = new Vector3(0f, 1.2f, 0f);

                Renderer bottleRenderer = bottle.GetComponent<Renderer>();
                if (bottleRenderer != null && shader != null)
                {
                    bottleRenderer.material = new Material(shader);
                    // 深蓝紫色，半透明感
                    bottleRenderer.material.color = new Color(0.3f, 0.2f, 0.7f, 1f);
                }

                // 移除碰撞体（由 Building 自动管理）
                Collider bottleCol = bottle.GetComponent<Collider>();
                if (bottleCol != null) UnityEngine.Object.Destroy(bottleCol);

                // 顶部星星（金色小球）
                GameObject star = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                star.name = "WishStar";
                star.transform.SetParent(graphicsContainer.transform, false);
                star.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
                star.transform.localPosition = new Vector3(0f, 2.7f, 0f);

                Renderer starRenderer = star.GetComponent<Renderer>();
                if (starRenderer != null && shader != null)
                {
                    starRenderer.material = new Material(shader);
                    // 金色
                    starRenderer.material.color = new Color(1f, 0.85f, 0.3f, 1f);
                }

                Collider starCol = star.GetComponent<Collider>();
                if (starCol != null) UnityEngine.Object.Destroy(starCol);

                // 添加旋转动画（星星缓慢旋转）
                var rotator = star.AddComponent<StarwishRotator>();

                // 底座平台
                GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                basePlate.name = "BasePlate";
                basePlate.transform.SetParent(graphicsContainer.transform, false);
                basePlate.transform.localScale = new Vector3(1.2f, 0.1f, 1.2f);
                basePlate.transform.localPosition = new Vector3(0f, 0.05f, 0f);

                Renderer baseRenderer = basePlate.GetComponent<Renderer>();
                if (baseRenderer != null && shader != null)
                {
                    baseRenderer.material = new Material(shader);
                    baseRenderer.material.color = new Color(0.25f, 0.2f, 0.35f, 1f);
                }

                Collider baseCol = basePlate.GetComponent<Collider>();
                if (baseCol != null) UnityEngine.Object.Destroy(baseCol);

                // 粒子效果：星尘上升
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
                main.startSize = 0.11f;
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
                    Material particleMat = CreateStarwishParticleMaterial();
                    if (particleMat != null)
                    {
                        renderer.material = particleMat;
                    }
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
                main.startSize = 0.22f;
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
                    Material particleMat = CreateStarwishParticleMaterial();
                    if (particleMat != null)
                    {
                        renderer.material = particleMat;
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 星光闪烁特效创建异常: " + e.Message);
            }
        }

        private Material CreateStarwishParticleMaterial()
        {
            if (starwishParticleMaterial != null)
            {
                return starwishParticleMaterial;
            }

            string[] shaderCandidates = new string[]
            {
                "Particles/Alpha Blended",
                "Legacy Shaders/Particles/Alpha Blended",
                "Mobile/Particles/Alpha Blended",
                "UI/Default",
                "Sprites/Default"
            };

            for (int i = 0; i < shaderCandidates.Length; i++)
            {
                Shader particleShader = Shader.Find(shaderCandidates[i]);
                if (particleShader == null)
                {
                    continue;
                }

                Material material = new Material(particleShader);
                material.name = "StarwishParticleMat_Shared";
                material.renderQueue = 3000;
                material.hideFlags = HideFlags.DontSave;

                if (material.HasProperty("_TintColor"))
                {
                    material.SetColor("_TintColor", StarwishParticleTint);
                }
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", StarwishParticleTint);
                }

                Texture2D particleTexture = GetOrCreateStarwishParticleTexture();
                if (particleTexture != null)
                {
                    if (material.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", particleTexture);
                    }
                    else
                    {
                        material.mainTexture = particleTexture;
                    }
                }

                starwishParticleMaterial = material;
                return starwishParticleMaterial;
            }

            DevLog("[WishFountain] 未找到兼容的粒子 Shader，粒子将保持默认材质");
            return null;
        }

        private Texture2D GetOrCreateStarwishParticleTexture()
        {
            if (starwishParticleTexture != null)
            {
                return starwishParticleTexture;
            }

            int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.DontSave;
            texture.name = "StarwishParticleTexture_Shared";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    float dx = (x - size / 2f) / (size / 2f);
                    float dy = (y - size / 2f) / (size / 2f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist < 1f)
                    {
                        float alpha = (1f - dist) * (1f - dist);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                    else
                    {
                        texture.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
                    }
                }
            }

            texture.Apply();
            starwishParticleTexture = texture;
            return starwishParticleTexture;
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

        private bool TryGetCombinedBounds(Renderer[] renderers, out Bounds combinedBounds)
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

        private void AddStarwishGraphicsCollider(GameObject target, Renderer[] renderers)
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
                DevLog("[WishFountain] 添加图形碰撞体失败: " + e.Message);
            }
        }

        private void FixStarwishModelShaders(GameObject modelInstance)
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
                    DevLog("[WishFountain] 未找到兼容 Shader，保持 AssetBundle 原始材质");
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
                DevLog("[WishFountain] 修复 AssetBundle Shader 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 添加 Building 组件（通过反射）
        /// </summary>
        private void AddStarwishBuildingComponent(GameObject go)
        {
            Type buildingType = FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                Debug.LogError("[WishFountain] 无法找到 Building 类型");
                return;
            }

            Component buildingComp = go.AddComponent(buildingType);

            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo idField = buildingType.GetField("id", privateFlags);
            if (idField != null) idField.SetValue(buildingComp, STARWISH_BUILDING_ID);

            FieldInfo dimField = buildingType.GetField("dimensions", privateFlags);
            if (dimField != null) dimField.SetValue(buildingComp, STARWISH_BUILDING_SIZE);

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

            FieldInfo areaMeshField = buildingType.GetField("areaMesh", privateFlags);
            if (areaMeshField != null) areaMeshField.SetValue(buildingComp, null);

            DevLog("[WishFountain] Building 组件已添加，ID=" + STARWISH_BUILDING_ID);
        }

        // ============================================================================
        // 数据注入
        // ============================================================================

        private void InjectStarwishBuildingData()
        {
            Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null)
            {
                Debug.LogError("[WishFountain] 无法找到 BuildingDataCollection 类型");
                return;
            }

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null) : null;

            if (bdcInstance == null)
            {
                Debug.LogError("[WishFountain] BuildingDataCollection.Instance 为 null");
                return;
            }

            // 注入 BuildingInfo
            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            if (infosField == null)
            {
                Debug.LogError("[WishFountain] 无法获取 infos 字段");
                return;
            }

            object infosList = infosField.GetValue(bdcInstance);
            if (infosList == null)
            {
                Debug.LogError("[WishFountain] infos 列表为 null");
                return;
            }

            Type buildingInfoType = FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null)
            {
                Debug.LogError("[WishFountain] 无法找到 BuildingInfo 类型");
                return;
            }

            // 检查是否已存在
            bool alreadyExists = false;
            var enumerator = ((IEnumerable)infosList).GetEnumerator();
            while (enumerator.MoveNext())
            {
                object info = enumerator.Current;
                FieldInfo infoIdField = buildingInfoType.GetField("id");
                if (infoIdField != null)
                {
                    string existingId = infoIdField.GetValue(info) as string;
                    if (existingId == STARWISH_BUILDING_ID)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
            }

            if (alreadyExists)
            {
                DevLog("[WishFountain] 建筑数据已存在，跳过注入");
                return;
            }

            // 创建 BuildingInfo
            object newInfo = Activator.CreateInstance(buildingInfoType);

            buildingInfoType.GetField("id")?.SetValue(newInfo, STARWISH_BUILDING_ID);
            buildingInfoType.GetField("prefabName")?.SetValue(newInfo, STARWISH_PREFAB_NAME);
            buildingInfoType.GetField("maxAmount")?.SetValue(newInfo, STARWISH_BUILDING_MAX_AMOUNT);
            buildingInfoType.GetField("requireBuildings")?.SetValue(newInfo, new string[0]);
            buildingInfoType.GetField("alternativeFor")?.SetValue(newInfo, new string[0]);
            buildingInfoType.GetField("requireQuests")?.SetValue(newInfo, new int[0]);

            if (starwishBuildingIcon != null)
            {
                buildingInfoType.GetField("iconReference")?.SetValue(newInfo, starwishBuildingIcon);
            }

            // 设置费用（复用婚礼建筑的方法）
            SetStarwishBuildingCost(buildingInfoType, ref newInfo);

            // 添加到 infos 列表
            MethodInfo addMethod = infosList.GetType().GetMethod("Add");
            if (addMethod != null)
            {
                addMethod.Invoke(infosList, new object[] { newInfo });
                DevLog("[WishFountain] BuildingInfo 已注入");
            }

            // 注入 prefab
            FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prefabsField != null)
            {
                object prefabsList = prefabsField.GetValue(bdcInstance);
                if (prefabsList != null)
                {
                    Type buildingType = FindGameType("Duckov.Buildings.Building");
                    if (buildingType != null)
                    {
                        Component buildingComp = starwishBuildingPrefabGO.GetComponent(buildingType);
                        if (buildingComp != null)
                        {
                            MethodInfo prefabAddMethod = prefabsList.GetType().GetMethod("Add");
                            if (prefabAddMethod != null)
                            {
                                prefabAddMethod.Invoke(prefabsList, new object[] { buildingComp });
                                DevLog("[WishFountain] Building prefab 已注入");
                            }
                        }
                    }
                }
            }

            // 重置缓存
            FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
            if (readonlyField != null)
            {
                readonlyField.SetValue(bdcInstance, null);
            }

            DevLog("[WishFountain] 建筑数据注入完成");
        }

        private void SetStarwishBuildingCost(Type buildingInfoType, ref object buildingInfo)
        {
            try
            {
                Type costType = FindGameType("Duckov.Economy.Cost");
                if (costType == null) return;

                ConstructorInfo costCtor = costType.GetConstructor(new Type[] { typeof(long) });
                if (costCtor != null)
                {
                    object cost = costCtor.Invoke(new object[] { STARWISH_BUILDING_COST });
                    buildingInfoType.GetField("cost")?.SetValue(buildingInfo, cost);
                }
                else
                {
                    object cost = Activator.CreateInstance(costType);
                    costType.GetField("money")?.SetValue(cost, STARWISH_BUILDING_COST);
                    costType.GetField("items")?.SetValue(cost, Array.CreateInstance(
                        costType.GetNestedType("ItemEntry") ?? typeof(object), 0));
                    buildingInfoType.GetField("cost")?.SetValue(buildingInfo, cost);
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 设置费用失败: " + e.Message);
            }
        }

        // ============================================================================
        // 事件监听
        // ============================================================================

        private void RegisterStarwishBuildingEvents()
        {
            try
            {
                Type bmType = FindGameType("Duckov.Buildings.BuildingManager");
                if (bmType == null) return;

                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                if (builtEvent != null)
                {
                    Action<int> handler = OnStarwishBuildingBuilt;
                    builtEvent.AddEventHandler(null, handler);
                    DevLog("[WishFountain] 已订阅 OnBuildingBuilt 事件");
                }

                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);
                if (destroyedEvent != null)
                {
                    Action<int> handler = OnStarwishBuildingDestroyed;
                    destroyedEvent.AddEventHandler(null, handler);
                    DevLog("[WishFountain] 已订阅 OnBuildingDestroyed 事件");
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 注册事件失败: " + e.Message);
            }
        }

        private void UnregisterStarwishBuildingEvents()
        {
            try
            {
                Type bmType = FindGameType("Duckov.Buildings.BuildingManager");
                if (bmType == null) return;

                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                if (builtEvent != null)
                {
                    Action<int> handler = OnStarwishBuildingBuilt;
                    builtEvent.RemoveEventHandler(null, handler);
                }

                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);
                if (destroyedEvent != null)
                {
                    Action<int> handler = OnStarwishBuildingDestroyed;
                    destroyedEvent.RemoveEventHandler(null, handler);
                }
            }
            catch { }
        }

        private void OnStarwishBuildingBuilt(int buildingInstanceId)
        {
            try
            {
                // 查找场景中刚放置的布满了灰尘的星愿许愿台建筑
                StartCoroutine(RestoreWishFountainBuildingsDelayed());
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] OnBuildingBuilt 异常: " + e.Message);
            }
        }

        public void RestoreWishFountainBuildings()
        {
            StartCoroutine(RestoreWishFountainBuildingsDelayed());
        }

        private IEnumerator RestoreWishFountainBuildingsDelayed()
        {
            // 等待一帧确保建筑完全初始化
            yield return null;
            yield return null;

            try
            {
                int restoredCount = 0;
                GameObject[] allObjects = FindObjectsOfType<GameObject>();
                for (int i = 0; i < allObjects.Length; i++)
                {
                    GameObject obj = allObjects[i];
                    if (!IsStarwishBuildingObject(obj))
                    {
                        continue;
                    }

                    EnsureStarwishFunctionPoints(obj);
                    restoredCount++;
                }

                DevLog("[WishFountain] 已恢复/检查场景中的布满了灰尘的星愿许愿台建筑数: " + restoredCount);
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 设置交互组件异常: " + e.Message);
            }
        }

        private void EnsureStarwishFunctionPoints(GameObject buildingGO)
        {
            if (buildingGO == null)
            {
                return;
            }

            try
            {
                Transform functionTr = buildingGO.transform.Find("Function");
                if (functionTr == null)
                {
                    GameObject functionGO = new GameObject("Function");
                    functionTr = functionGO.transform;
                    functionTr.SetParent(buildingGO.transform, false);
                }

                Transform interactTr = functionTr.Find("WishInteractPoint");
                if (interactTr == null)
                {
                    GameObject interactGO = new GameObject("WishInteractPoint");
                    interactTr = interactGO.transform;
                    interactTr.SetParent(functionTr, false);
                }

                interactTr.localPosition = STARWISH_INTERACT_OFFSET;
                interactTr.localRotation = Quaternion.identity;

                bool restoreActive = interactTr.gameObject.activeSelf;
                if (restoreActive)
                {
                    interactTr.gameObject.SetActive(false);
                }

                BoxCollider col = interactTr.gameObject.GetComponent<BoxCollider>();
                if (col == null)
                {
                    col = interactTr.gameObject.AddComponent<BoxCollider>();
                }
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(2f, 2.5f, 2f);

                WishFountainInteractable interactable = interactTr.gameObject.GetComponent<WishFountainInteractable>();
                if (interactable == null)
                {
                    interactable = interactTr.gameObject.AddComponent<WishFountainInteractable>();
                }

                interactable.interactCollider = col;
                interactable.interactMarkerOffset = new Vector3(0f, 1.5f, 0f);

                if (restoreActive)
                {
                    interactTr.gameObject.SetActive(true);
                }
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 修复布满了灰尘的星愿许愿台交互点失败: " + e.Message);
            }
        }

        private bool IsStarwishBuildingObject(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            if (obj.name.IndexOf(STARWISH_PREFAB_NAME, StringComparison.OrdinalIgnoreCase) >= 0
                || obj.name.IndexOf("BossRush_StarWishFountain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                Type buildingType = FindGameType("Duckov.Buildings.Building");
                if (buildingType == null)
                {
                    return false;
                }

                Component buildingComp = obj.GetComponent(buildingType);
                if (buildingComp == null)
                {
                    return false;
                }

                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo idField = buildingType.GetField("id", flags);
                if (idField == null)
                {
                    return false;
                }

                string id = idField.GetValue(buildingComp) as string;
                return string.Equals(id, STARWISH_BUILDING_ID, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private void OnStarwishBuildingDestroyed(int buildingInstanceId)
        {
            // 建筑拆除时 Unity 会自动销毁子物体和组件，无需额外处理
            DevLog("[WishFountain] 布满了灰尘的星愿许愿台建筑已拆除");
        }
    }

    // ============================================================================
    // 辅助组件
    // ============================================================================

    /// <summary>
    /// 星星缓慢旋转效果
    /// </summary>
    public class StarwishRotator : MonoBehaviour
    {
        private float rotateSpeed = 30f;

        private void Update()
        {
            try
            {
                transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.Self);
            }
            catch { }
        }
    }
}
