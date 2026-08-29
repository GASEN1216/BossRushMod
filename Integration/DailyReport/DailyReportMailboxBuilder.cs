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

        /// <summary>
        /// bundle 模型归一化后的最大边长。报箱占地 1x1，比许愿台（2.2）小一号，
        /// 免得借来的替身模型撑出格子压到旁边的建筑。
        /// </summary>
        private const float DAILYREPORT_MODEL_TARGET_MAX_DIM = 1.5f;

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

                // dormant 契约：开关关闭时不往官方建造 UI 里塞死建筑——报箱要花 500 金自建，
                // 买下后 DailyReportInteractable.IsInteractable 恒 false，连交互提示都不出，
                // 玩家没有任何反馈。老档已建过是例外：必须照常注册 prefab，
                // 否则官方 BuildingArea 会报缺 prefab。形态与 PetNestBuilder 一致。
                if (!IsDailyReportConfiguredEnabled() && !HasPendingDailyReportBuildingsInManager())
                {
                    DevLog(DailyReportTuning.LogPrefix + "入口开关关闭且未建过，跳过建筑注入（dormant）");
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
                    if (TryBorrowStarwishModelAsStandIn()) return;
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

        /// <summary>
        /// 临时替身：没有自己的 bundle 时，借用许愿台**已经加载好的**模型 prefab，
        /// 让报箱先有一个真模型，等 Meshy 出图后再换掉。
        ///
        /// 为什么是借引用而不是把 starwish_fountain 文件改名复制一份：
        /// AssetBundle 的**内部 CAB 名不随文件名改变**（starwish 那份是
        /// CAB-013421218a0469224cbbccdeb8d99772），而许愿台把 bundle 常驻在静态字段里
        /// 直到 Cleanup 才卸载。于是改名复制后第二次 LoadFromFile 会因为
        /// "同一批 serialized 文件已被加载"而失败，静默退回几何体占位——
        /// 看起来像"没生效"，实际是撞了 Unity 的 bundle 唯一性限制。
        /// 借引用完全绕开这条限制，还不新增任何二进制文件。
        ///
        /// 依赖执行顺序：许愿台的建筑注入排在报箱之前（基地场景装配管线与
        /// OnSceneLoaded 早期注入两条路径都是），所以这里读到的静态字段已填好。
        /// 读不到就老实退回几何体，不报错。
        /// </summary>
        private bool TryBorrowStarwishModelAsStandIn()
        {
            try
            {
                if (starwishModelPrefab == null) return false;

                dailyReportModelPrefab = starwishModelPrefab;
                DevLog(DailyReportTuning.LogPrefix
                    + "[临时] 借用许愿台模型作为报箱替身（等待专属 AssetBundle）");
                return true;
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "借用许愿台模型失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// bundle 模型的统一修整：按包围盒归一到 1x1 建筑的尺度、底部对齐地面、
        /// 修 shader、补碰撞体。复用许愿台那套已经趟平的工具方法
        /// （同一个 partial class ModBehaviour，参数是通用的）。
        /// </summary>
        private void PrepareDailyReportBundleModel(GameObject modelInstance, GameObject graphicsContainer)
        {
            try
            {
                if (modelInstance == null || graphicsContainer == null) return;

                Renderer[] renderers = CollectStarwishRenderableComponents(modelInstance);
                if (renderers.Length == 0)
                {
                    DevLog(DailyReportTuning.LogPrefix + "bundle 模型未找到可用 Renderer，跳过修整");
                    return;
                }

                Bounds bounds;
                if (!TryGetCombinedBounds(renderers, out bounds)) return;

                // 报箱占地 1x1，比许愿台小一号；过大过小都拉回目标尺度
                float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
                if (maxDim > 0.001f)
                {
                    float scaleFactor = DAILYREPORT_MODEL_TARGET_MAX_DIM / maxDim;
                    modelInstance.transform.localScale *= scaleFactor;
                    DevLog(DailyReportTuning.LogPrefix + "bundle 模型缩放 " + scaleFactor + " 倍");
                }

                // 底部对齐地面，避免模型半截埋进地里或悬空
                renderers = CollectStarwishRenderableComponents(modelInstance);
                if (TryGetCombinedBounds(renderers, out bounds))
                {
                    float bottomLocal = bounds.min.y - graphicsContainer.transform.position.y;
                    modelInstance.transform.localPosition = new Vector3(0f, -bottomLocal, 0f);
                }

                FixStarwishModelShaders(modelInstance);
                AddStarwishGraphicsCollider(modelInstance, CollectStarwishRenderableComponents(modelInstance));
            }
            catch (Exception e)
            {
                DevLog(DailyReportTuning.LogPrefix + "修整 bundle 模型失败: " + e.Message);
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
                PrepareDailyReportBundleModel(modelInstance, graphicsContainer);
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
        /// 占位模型：石砌基座 + 箱体 + **半圆柱顶** + 小红旗 + 探出的报纸卷。
        /// 剪影刻意与建造图标 `Assets/buildings/bossrush_daily_mailbox.png` 对齐，
        /// 让玩家在菜单里看到的和放下去看到的是同一个东西。
        ///
        /// 圆顶用的是躺倒的圆柱：Unity 圆柱轴向是 Y，绕 X 转 90° 后轴向变成 Z（前后），
        /// 于是 localScale.y 决定筒长（即箱体进深），localScale.x/z 决定直径（即箱体宽度）。
        ///
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互射线。
        /// 这是无美术期的 fallback；`Assets/buildings/bossrush_daily_mailbox` 放上
        /// AssetBundle 后本方法自动不再被调用。
        /// </summary>
        private void CreateDailyReportPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                Color stone = new Color(0.54f, 0.52f, 0.48f, 1f);
                Color body = new Color(0.44f, 0.21f, 0.18f, 1f);
                Color trim = new Color(0.30f, 0.15f, 0.13f, 1f);
                Color paper = new Color(0.88f, 0.86f, 0.79f, 1f);

                // 石砌基座（图标里是一段矮而粗的石台，不是细长杆）
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Pedestal",
                    new Vector3(0.40f, 0.80f, 0.40f), new Vector3(0f, 0.40f, 0f),
                    stone, shader, Quaternion.identity);

                // 基座压顶
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "PedestalCap",
                    new Vector3(0.50f, 0.06f, 0.50f), new Vector3(0f, 0.82f, 0f),
                    new Color(0.46f, 0.44f, 0.41f, 1f), shader, Quaternion.identity);

                // 箱体
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Box",
                    new Vector3(0.58f, 0.36f, 0.44f), new Vector3(0f, 1.03f, 0f),
                    body, shader, Quaternion.identity);

                // 半圆柱顶：绕 X 转 90°，轴向指向前后
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "RoundTop",
                    new Vector3(0.58f, 0.22f, 0.58f), new Vector3(0f, 1.21f, 0f),
                    body, shader, Quaternion.Euler(90f, 0f, 0f));

                // 正面投递口面板
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "DoorPanel",
                    new Vector3(0.34f, 0.20f, 0.02f), new Vector3(0f, 1.00f, 0.225f),
                    trim, shader, Quaternion.identity);

                // 探出投递口的报纸卷
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cylinder, "NewspaperRoll",
                    new Vector3(0.11f, 0.13f, 0.11f), new Vector3(-0.13f, 1.16f, 0.28f),
                    paper, shader, Quaternion.Euler(90f, 0f, 0f));

                // 小红旗（有新报纸的视觉暗示）
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "FlagPole",
                    new Vector3(0.03f, 0.34f, 0.03f), new Vector3(0.33f, 1.30f, 0f),
                    new Color(0.25f, 0.22f, 0.20f, 1f), shader, Quaternion.identity);

                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "Flag",
                    new Vector3(0.15f, 0.10f, 0.02f), new Vector3(0.41f, 1.42f, 0f),
                    new Color(0.78f, 0.24f, 0.20f, 1f), shader, Quaternion.identity);

                // 基座旁堆的旧报纸
                CreateDailyReportPlaceholderPart(
                    graphicsContainer, PrimitiveType.Cube, "PaperStack",
                    new Vector3(0.20f, 0.07f, 0.16f), new Vector3(0.30f, 0.035f, 0.10f),
                    paper, shader, Quaternion.Euler(0f, 18f, 0f));
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
