// ============================================================================
// DailyReportMailboxBuilder.cs - 基地「报箱」建筑注入器（P0 步骤 6）
// ============================================================================
// 模块说明：
//   通过反射把「报箱」注入官方建筑系统，让它出现在基地地堡的建造 UI 里，
//   玩家花 500 金自建一个，交互即可阅读《鸭科夫日报》。
//   形态照 PetNest/PetNestBuilder.cs（仓库最新的建筑注入两件套先例）。
//
// 零新增 Unity 资源：没有专属 AssetBundle 与图标 PNG 时走占位模型 fallback。
//   将来补美术时只需在 Assets/buildings/ 放同名 bundle 与 png，本文件零改动。
//
// 共享反射工具（FindGameType / GetBuildingType / AssignBuildingContainerField /
// RequestBaseBuildingAreaRepaint 等）定义在 Integration/Wedding/ 下、
// 属于同一个 partial class ModBehaviour，**不得重复定义**。
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
    /// <summary>报箱建筑注入器（partial class ModBehaviour）。</summary>
    public partial class ModBehaviour
    {
        // ====================================================================
        // 常量
        // ====================================================================

        /// <summary>
        /// 建筑 ID（官方本地化 key 为 "Building_" + id）。
        /// 玩家放置记录会以此进官方 BuildingData 存档，**发布后永不可改名**，
        /// 否则老存档里的报箱会变成缺 prefab 的幽灵。
        /// </summary>
        private const string DAILYREPORT_BUILDING_ID = DailyReportTuning.MailboxBuildingId;

        /// <summary>建筑预制体名称。必须与 BuildingInfo.prefabName 严格一致
        /// （官方 GetPrefab 按 e.name == prefabName 匹配）。</summary>
        private const string DAILYREPORT_PREFAB_NAME = "BossRushDailyMailbox";

        /// <summary>占位 AssetBundle 文件标记（bundle 尚未产出时的占位内容）。</summary>
        private const string DAILYREPORT_BUNDLE_PLACEHOLDER_MARKER = "DAILYREPORT_PLACEHOLDER_BUNDLE";

        /// <summary>建筑占地尺寸。信箱很小，占 1x1。</summary>
        private static readonly Vector2Int DAILYREPORT_BUILDING_SIZE = new Vector2Int(1, 1);

        /// <summary>建筑费用。</summary>
        private const long DAILYREPORT_BUILDING_COST = DailyReportTuning.MailboxCost;

        /// <summary>最大建造数量。</summary>
        private const int DAILYREPORT_BUILDING_MAX_AMOUNT = DailyReportTuning.MailboxMaxAmount;

        /// <summary>交互点偏移。</summary>
        private static readonly Vector3 DAILYREPORT_INTERACT_OFFSET = new Vector3(0f, 0f, 0f);

        // ====================================================================
        // 状态
        // ====================================================================

        private bool dailyReportBuildingInjected;
        private GameObject dailyReportBuildingPrefabGO;
        private static Sprite dailyReportBuildingIcon;
        private static AssetBundle dailyReportAssetBundle;
        private static GameObject dailyReportModelPrefab;
        private Coroutine dailyReportRestoreCoroutine;
        private readonly HashSet<int> preparedDailyReportBuildingInstanceIds = new HashSet<int>();
        private int preparedDailyReportSceneHandle = int.MinValue;

        // ====================================================================
        // 初始化
        // ====================================================================

        /// <summary>基地场景装配管线调用的公开入口。</summary>
        public void InitDailyReportMailbox()
        {
            InitDailyReportMailbox(false);
        }

        private void InitDailyReportMailbox(bool isEarlyInit)
        {
            try
            {
                if (dailyReportBuildingInjected)
                {
                    DevLog(DailyReportTuning.LogPrefix + "建筑已注入，跳过");
                    return;
                }

                DailyReportLocalization.InjectBuildingKeys();
                LoadDailyReportBuildingIcon();
                LoadDailyReportBuildingModel();
                CreateDailyReportBuildingPrefab();
                InjectDailyReportBuildingData();
                RegisterDailyReportBuildingEvents();

                dailyReportBuildingInjected = true;

                // 早期注入时 BuildingArea 还没 Start，重绘会白跑一趟
                if (!isEarlyInit && HasPendingDailyReportBuildingsInManager())
                {
                    RequestBaseBuildingAreaRepaint("InitDailyReportMailbox");
                }

                DevLog(DailyReportTuning.LogPrefix + "建筑注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError(DailyReportTuning.LogPrefix + "建筑初始化失败: "
                    + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 早期注入：老存档里已经建过报箱时，必须**赶在 BuildingArea.Start 之前**
        /// 把 prefab 注册好，否则官方会先报"缺 prefab"。
        /// </summary>
        internal void TryInitializeDailyReportMailboxEarly()
        {
            try
            {
                if (dailyReportBuildingInjected) return;

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name)) return;

                Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (bdcType == null) return;

                PropertyInfo instanceProp = bdcType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null || instanceProp.GetValue(null, null) == null) return;

                InitDailyReportMailbox(true);
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "早期注入跳过: " + e.Message);
            }
        }

        /// <summary>Mod 卸载路径的清理。</summary>
        public void CleanupDailyReportMailbox()
        {
            try
            {
                if (dailyReportRestoreCoroutine != null)
                {
                    StopCoroutine(dailyReportRestoreCoroutine);
                    dailyReportRestoreCoroutine = null;
                }
                ResetDailyReportPreparedBuildingCache();
                UnregisterDailyReportBuildingEvents();
                AssetBundleUnloadHelper.TryUnload(dailyReportAssetBundle, DailyReportTuning.LogPrefix);
                dailyReportAssetBundle = null;
                dailyReportModelPrefab = null;
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "建筑清理失败: " + e.Message);
            }
        }

        // ====================================================================
        // 资源加载（零新增资源：缺文件即走占位）
        // ====================================================================

        private void LoadDailyReportBuildingIcon()
        {
            if (dailyReportBuildingIcon != null) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string iconPath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                iconPath = Path.Combine(iconPath, DAILYREPORT_BUILDING_ID + ".png");
                if (!File.Exists(iconPath))
                {
                    DevLog(DailyReportTuning.LogPrefix + "建筑图标缺失，使用官方默认图标");
                    return;
                }

                byte[] bytes = File.ReadAllBytes(iconPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) return;
                dailyReportBuildingIcon = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "建筑图标加载失败: " + e.Message);
            }
        }

        private void LoadDailyReportBuildingModel()
        {
            if (dailyReportModelPrefab != null) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string bundlePath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                bundlePath = Path.Combine(bundlePath, DAILYREPORT_BUILDING_ID);

                if (!File.Exists(bundlePath) || IsDailyReportPlaceholderBundle(bundlePath))
                {
                    DevLog(DailyReportTuning.LogPrefix + "建筑模型 bundle 缺失或为占位，使用占位模型");
                    return;
                }

                dailyReportAssetBundle = AssetBundle.LoadFromFile(bundlePath);
                if (dailyReportAssetBundle == null) return;

                dailyReportModelPrefab = dailyReportAssetBundle.LoadAsset<GameObject>(DAILYREPORT_PREFAB_NAME);
                if (dailyReportModelPrefab == null)
                {
                    string[] names = dailyReportAssetBundle.GetAllAssetNames();
                    if (names != null && names.Length > 0)
                    {
                        dailyReportModelPrefab = dailyReportAssetBundle.LoadAsset<GameObject>(names[0]);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "建筑模型加载失败: " + e.Message);
            }
        }

        private static bool IsDailyReportPlaceholderBundle(string bundlePath)
        {
            try
            {
                FileInfo info = new FileInfo(bundlePath);
                if (!info.Exists) return false;
                if (info.Length == 0L) return true;
                if (info.Length > 128L) return false;
                string text = File.ReadAllText(bundlePath);
                return string.Equals(
                    text.Trim(), DAILYREPORT_BUNDLE_PLACEHOLDER_MARKER, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ====================================================================
        // 预制体
        // ====================================================================

        private void CreateDailyReportBuildingPrefab()
        {
            if (dailyReportBuildingPrefabGO != null) return;

            // 先 inactive：官方 Building.Awake 会解引用 functionContainer（CreateAreaMesh），
            // 必须等反射把容器字段填好之后再激活。
            dailyReportBuildingPrefabGO = new GameObject(DAILYREPORT_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(dailyReportBuildingPrefabGO);
            dailyReportBuildingPrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            dailyReportBuildingPrefabGO.SetActive(false);

            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(dailyReportBuildingPrefabGO.transform, false);

            if (dailyReportModelPrefab != null)
            {
                GameObject modelInstance = UnityEngine.Object.Instantiate(
                    dailyReportModelPrefab, graphicsContainer.transform);
                modelInstance.name = "Model";
                modelInstance.SetActive(true);
            }
            else
            {
                CreateDailyReportPlaceholderModel(graphicsContainer);
            }

            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(dailyReportBuildingPrefabGO.transform, false);

            GameObject interactPoint = new GameObject("DailyReportInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);
            interactPoint.transform.localPosition = DAILYREPORT_INTERACT_OFFSET;

            AddDailyReportBuildingComponent(dailyReportBuildingPrefabGO);
            EnsureDailyReportFunctionPoints(dailyReportBuildingPrefabGO);
            dailyReportBuildingPrefabGO.SetActive(true);

            DevLog(DailyReportTuning.LogPrefix + "预制体创建完成");
        }

        /// <summary>
        /// 占位模型：立柱 + 邮筒箱体 + 斜顶盖 + 一面小红旗。零新增资源的 fallback。
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互射线。
        /// </summary>
        private void CreateDailyReportPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                // 立柱
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "Post",
                    new Vector3(0.12f, 0.5f, 0.12f), new Vector3(0f, 0.5f, 0f),
                    new Color(0.30f, 0.24f, 0.18f, 1f), shader, Quaternion.identity);

                // 箱体
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Box",
                    new Vector3(0.55f, 0.38f, 0.36f), new Vector3(0f, 1.18f, 0f),
                    new Color(0.42f, 0.20f, 0.18f, 1f), shader, Quaternion.identity);

                // 斜顶盖
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Lid",
                    new Vector3(0.60f, 0.06f, 0.40f), new Vector3(0f, 1.40f, 0f),
                    new Color(0.28f, 0.14f, 0.12f, 1f), shader,
                    Quaternion.Euler(0f, 0f, 8f));

                // 小红旗（有新报纸的视觉暗示）
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "FlagPole",
                    new Vector3(0.04f, 0.22f, 0.04f), new Vector3(0.32f, 1.32f, 0f),
                    new Color(0.25f, 0.22f, 0.20f, 1f), shader, Quaternion.identity);

                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Flag",
                    new Vector3(0.16f, 0.11f, 0.02f), new Vector3(0.41f, 1.40f, 0f),
                    new Color(0.78f, 0.24f, 0.20f, 1f), shader, Quaternion.identity);

                // 底座
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "BasePlate",
                    new Vector3(0.42f, 0.05f, 0.42f), new Vector3(0f, 0.025f, 0f),
                    new Color(0.20f, 0.17f, 0.14f, 1f), shader, Quaternion.identity);
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "占位模型创建失败: " + e.Message);
            }
        }

        private static void CreateDailyReportPlaceholderPart(
            GameObject parent, PrimitiveType primitive, string name,
            Vector3 localScale, Vector3 localPosition, Color color, Shader shader,
            Quaternion localRotation)
        {
            GameObject go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localScale = localScale;
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

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
