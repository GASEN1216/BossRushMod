// ============================================================================
// PetNestBuilder.cs - 基地「遗种巢」建筑注入器（实施计划 步骤 9）
// ============================================================================
// 模块说明：
//   通过反射把「遗种巢」建筑注入官方建筑系统，让它出现在基地地堡的建造 UI 里。
//   形态照 Integration/WishFountain/WishFountainBuilder.cs 两件套先例。
//
// 最小化修改：全案**只加一个建筑**。巢 / 孵化 / 远征 / 博物馆四个功能都挂在同一个
//   交互点上，走 NPCInteractionGroupHelper 的多选项交互菜单，不新增第二、第三个建筑。
//
// 资源：Assets/buildings/petnest_relic_nest（模型 bundle，prefab 名 PetNestRelicNest）
//       + Assets/buildings/petnest_relic_nest.png（建造界面图标）。
// 两者都缺时**不 fail**，退回运行时占位圆柱体 + 官方默认图标——
// 这条 fallback 是契约的一部分，不要因为资源已落位就删掉它。
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>遗种巢建筑注入器（partial class ModBehaviour）。</summary>
    public partial class ModBehaviour
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>建筑 ID（官方本地化 key 为 "Building_" + id，冻结不改）。</summary>
        private const string PETNEST_BUILDING_ID = "petnest_relic_nest";

        /// <summary>建筑预制体名称。必须与 BuildingInfo.prefabName 严格一致
        /// （官方 GetPrefab 按 e.name == prefabName 匹配）。</summary>
        private const string PETNEST_PREFAB_NAME = "PetNestRelicNest";

        /// <summary>占位 AssetBundle 文件标记（bundle 尚未产出时的占位内容）。</summary>
        private const string PETNEST_BUNDLE_PLACEHOLDER_MARKER = "PETNEST_PLACEHOLDER_BUNDLE";

        /// <summary>建筑占地尺寸。</summary>
        private static readonly Vector2Int PETNEST_BUILDING_SIZE = new Vector2Int(2, 2);

        /// <summary>建筑费用。</summary>
        private const long PETNEST_BUILDING_COST = 1500;

        /// <summary>最大建造数量（单巢）。</summary>
        private const int PETNEST_BUILDING_MAX_AMOUNT = 1;

        /// <summary>交互点偏移。</summary>
        private static readonly Vector3 PETNEST_INTERACT_OFFSET = new Vector3(0f, 0f, 0f);

        // ====================================================================
        // 状态
        // ====================================================================

        private bool petNestBuildingInjected;
        private GameObject petNestBuildingPrefabGO;
        private static Sprite petNestBuildingIcon;
        private static AssetBundle petNestAssetBundle;
        private static GameObject petNestModelPrefab;
        private Coroutine petNestRestoreCoroutine;
        private readonly HashSet<int> preparedPetNestBuildingInstanceIds = new HashSet<int>();
        private int preparedPetNestSceneHandle = int.MinValue;

        // ====================================================================
        // 初始化
        // ====================================================================

        /// <summary>基地场景装配管线调用的公开入口。</summary>
        public void InitPetNestBuilding()
        {
            InitPetNestBuilding(false);
        }

        private void InitPetNestBuilding(bool isEarlyInit)
        {
            try
            {
                if (petNestBuildingInjected)
                {
                    DevLog("[PetNest] 建筑已注入，跳过");
                    return;
                }

                // dormant 契约：开关关闭时不往官方建造 UI 里塞一个点不动的死建筑
                // （出厂默认就是关闭，否则每个没开过这个选项的玩家都能花 1500 建一个
                // 永远点不动的 2x2 摆件）。
                // 例外是**老档里已经建过**——那种情况必须照常注册 prefab，
                // 否则官方 BuildingArea 会报缺 prefab。
                if (!IsPetNestConfiguredEnabled() && !HasPendingPetNestBuildingsInManager())
                {
                    DevLog("[PetNest] 入口开关关闭且未建过，跳过建筑注入（dormant）");
                    return;
                }

                PetNestLocalization.InjectBuildingKeys();
                LoadPetNestBuildingIcon();
                LoadPetNestBuildingModel();
                CreatePetNestBuildingPrefab();
                InjectPetNestBuildingData();
                RegisterPetNestBuildingEvents();

                petNestBuildingInjected = true;

                // 早期注入时 BuildingArea 还没 Start，重绘会白跑一趟
                if (!isEarlyInit && HasPendingPetNestBuildingsInManager())
                {
                    RequestBaseBuildingAreaRepaint("InitPetNestBuilding");
                }

                DevLog("[PetNest] 建筑注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[PetNest] 建筑初始化失败: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 早期注入：老存档里已经建过这个建筑时，必须**赶在 BuildingArea.Start 之前**
        /// 把 prefab 注册好，否则官方会先报"缺 prefab"。
        /// </summary>
        private void TryInitializePetNestEarly()
        {
            try
            {
                if (petNestBuildingInjected) return;

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name)) return;

                Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (bdcType == null) return;

                PropertyInfo instanceProp = bdcType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null || instanceProp.GetValue(null, null) == null) return;

                InitPetNestBuilding(true);
            }
            catch (Exception e)
            {
                DevLog("[PetNest] 早期注入跳过: " + e.Message);
            }
        }

        /// <summary>Mod 卸载路径的清理。</summary>
        public void CleanupPetNestBuilding()
        {
            try
            {
                if (petNestRestoreCoroutine != null)
                {
                    StopCoroutine(petNestRestoreCoroutine);
                    petNestRestoreCoroutine = null;
                }
                ResetPetNestPreparedBuildingCache();
                UnregisterPetNestBuildingEvents();
                AssetBundleUnloadHelper.TryUnload(petNestAssetBundle, "[PetNest]");
                petNestAssetBundle = null;
                petNestModelPrefab = null;
            }
            catch (Exception e)
            {
                DevLog("[PetNest] 建筑清理失败: " + e.Message);
            }
        }

        // ====================================================================
        // 资源加载（缺文件即走占位，不 fail）
        // ====================================================================

        private void LoadPetNestBuildingIcon()
        {
            if (petNestBuildingIcon != null) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string iconPath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                iconPath = Path.Combine(iconPath, PETNEST_BUILDING_ID + ".png");
                if (!File.Exists(iconPath))
                {
                    DevLog("[PetNest] 建筑图标缺失，使用官方默认图标");
                    return;
                }

                byte[] bytes = File.ReadAllBytes(iconPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) return;
                petNestBuildingIcon = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                DevLog("[PetNest] 建筑图标加载失败: " + e.Message);
            }
        }

        private void LoadPetNestBuildingModel()
        {
            if (petNestModelPrefab != null) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string bundlePath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                bundlePath = Path.Combine(bundlePath, PETNEST_BUILDING_ID);

                if (!File.Exists(bundlePath) || IsPetNestPlaceholderBundle(bundlePath))
                {
                    DevLog("[PetNest] 建筑模型 bundle 缺失或为占位，使用占位圆柱体");
                    return;
                }

                petNestAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (petNestAssetBundle == null) return;

                petNestModelPrefab = petNestAssetBundle.LoadAsset<GameObject>(PETNEST_PREFAB_NAME);
                if (petNestModelPrefab == null)
                {
                    string[] names = petNestAssetBundle.GetAllAssetNames();
                    if (names != null && names.Length > 0)
                    {
                        petNestModelPrefab = petNestAssetBundle.LoadAsset<GameObject>(names[0]);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[PetNest] 建筑模型加载失败: " + e.Message);
            }
        }

        private static bool IsPetNestPlaceholderBundle(string bundlePath)
        {
            try
            {
                FileInfo info = new FileInfo(bundlePath);
                if (!info.Exists) return false;
                if (info.Length == 0L) return true;
                if (info.Length > 128L) return false;
                string text = File.ReadAllText(bundlePath);
                return string.Equals(
                    text.Trim(), PETNEST_BUNDLE_PLACEHOLDER_MARKER, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ====================================================================
        // 预制体
        // ====================================================================

        private void CreatePetNestBuildingPrefab()
        {
            if (petNestBuildingPrefabGO != null) return;

            // 先 inactive：官方 Building.Awake 会解引用 functionContainer（CreateAreaMesh），
            // 必须等反射把容器字段填好之后再激活。
            petNestBuildingPrefabGO = new GameObject(PETNEST_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(petNestBuildingPrefabGO);
            petNestBuildingPrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            petNestBuildingPrefabGO.SetActive(false);

            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(petNestBuildingPrefabGO.transform, false);

            if (petNestModelPrefab != null)
            {
                GameObject modelInstance = UnityEngine.Object.Instantiate(
                    petNestModelPrefab, graphicsContainer.transform);
                modelInstance.name = "Model";
                modelInstance.SetActive(true);
            }
            else
            {
                CreatePetNestPlaceholderModel(graphicsContainer);
            }

            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(petNestBuildingPrefabGO.transform, false);

            GameObject interactPoint = new GameObject("PetNestInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);
            interactPoint.transform.localPosition = PETNEST_INTERACT_OFFSET;

            AddPetNestBuildingComponent(petNestBuildingPrefabGO);
            EnsurePetNestFunctionPoints(petNestBuildingPrefabGO);
            petNestBuildingPrefabGO.SetActive(true);

            DevLog("[PetNest] 预制体创建完成");
        }

        /// <summary>
        /// 占位模型：巢体（宽扁圆柱）+ 三枚蛋（球）。零新增资源的 fallback。
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互。
        /// </summary>
        private void CreatePetNestPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                CreatePetNestPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "NestBowl",
                    new Vector3(1.1f, 0.35f, 1.1f), new Vector3(0f, 0.35f, 0f),
                    new Color(0.38f, 0.28f, 0.18f, 1f), shader);

                CreatePetNestPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "BasePlate",
                    new Vector3(1.35f, 0.08f, 1.35f), new Vector3(0f, 0.04f, 0f),
                    new Color(0.22f, 0.18f, 0.14f, 1f), shader);

                CreatePetNestPlaceholderPart(
                    graphicsContainer, PrimitiveType.Sphere, "Egg_A",
                    new Vector3(0.4f, 0.5f, 0.4f), new Vector3(-0.3f, 0.8f, 0.1f),
                    new Color(0.86f, 0.82f, 0.7f, 1f), shader);

                CreatePetNestPlaceholderPart(
                    graphicsContainer, PrimitiveType.Sphere, "Egg_B",
                    new Vector3(0.4f, 0.5f, 0.4f), new Vector3(0.28f, 0.8f, -0.15f),
                    new Color(0.8f, 0.76f, 0.66f, 1f), shader);

                CreatePetNestPlaceholderPart(
                    graphicsContainer, PrimitiveType.Sphere, "Egg_Shiny",
                    new Vector3(0.36f, 0.46f, 0.36f), new Vector3(0.02f, 0.82f, 0.34f),
                    new Color(0.55f, 0.78f, 0.9f, 1f), shader);
            }
            catch (Exception e)
            {
                DevLog("[PetNest] 占位模型创建失败: " + e.Message);
            }
        }

        private static void CreatePetNestPlaceholderPart(
            GameObject parent, PrimitiveType primitive, string name,
            Vector3 localScale, Vector3 localPosition, Color color, Shader shader)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = localScale;
            go.transform.localPosition = localPosition;

            // CreatePrimitive 自带 Collider，必须删：会干扰建筑放置与交互射线
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.Destroy(collider);
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader);
                renderer.material.color = color;
            }
        }
    }
}
