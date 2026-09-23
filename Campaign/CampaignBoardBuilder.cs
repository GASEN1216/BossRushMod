// ============================================================================
// CampaignBoardBuilder.cs - 基地「征程公告板」建筑注入器
// ============================================================================
// 形态照 Integration/DailyReport/DailyReportMailboxBuilder.cs（仓库最新的建筑注入先例）。
// 通过反射把公告板注入官方建筑系统，玩家在基地地堡花钱自建，交互即可接取/交付契约。
//
// 【为什么放基地而不是竞技场】
//   「接约 → 出击 → 回来交付」是个环路，而每种模式结束都会回基地——基地是天然枢纽。
//   建筑系统还自带持久化、摆放与图标管线；竞技场侧则需要 per-scene 坐标配置，
//   还会跟波次运行时抢场地。
//
// 【dormant 契约】
//   开关关闭时不往官方建造 UI 里塞建筑（否则玩家花钱买了个按不动的东西）。
//   **老档已建过是例外**：必须照常注册 prefab，否则官方 BuildingArea 会报缺 prefab，
//   留下一个幽灵建筑。这条与报箱/遗种巢逐字一致。
//
// 专属模型由 Assets/buildings/bossrush_campaign_board 加载；缺包时保留程序化占位。
// 将来补美术只需在 Assets/buildings/ 放同名 bundle 与 png，本文件零改动。
//
// 共享反射工具（FindGameType / AssignBuildingContainerField /
// RequestBaseBuildingAreaRepaint）由共享工具与显式 _owner 提供，模块不再借 partial 访问兄弟私有状态。
// ============================================================================

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>征程公告板建筑注入器（显式 ModBehaviour owner）。</summary>
    internal sealed partial class CampaignBoardBuilder
    {
        private readonly ModBehaviour _owner;

        internal CampaignBoardBuilder(ModBehaviour owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
        }

        #region 常量

        /// <summary>建筑 ID（官方本地化 key 为 "Building_" + id）。发布后永不可改名。</summary>
        private const string CAMPAIGN_BOARD_BUILDING_ID = CampaignTuning.BoardBuildingId;

        /// <summary>预制体名称。必须与 BuildingInfo.prefabName 严格一致。</summary>
        private const string CAMPAIGN_BOARD_PREFAB_NAME = "BossRushCampaignBoard";

        /// <summary>建筑占地尺寸。公告板是竖版立牌，占 1x1。</summary>
        private static readonly Vector2Int CAMPAIGN_BOARD_SIZE = new Vector2Int(1, 1);

        private const long CAMPAIGN_BOARD_COST = CampaignTuning.BoardBuildCost;

        private const int CAMPAIGN_BOARD_MAX_AMOUNT = 1;

        private static readonly Vector3 CAMPAIGN_BOARD_INTERACT_OFFSET = new Vector3(0f, 0f, 0f);

        #endregion

        #region 状态

        private bool campaignBoardInjected;
        private GameObject campaignBoardPrefabGO;
        private AssetBundle campaignBoardModelBundle;
        private static Sprite campaignBoardIcon;

        #endregion

        #region 初始化

        /// <summary>基地场景装配管线调用的公开入口。</summary>
        public void InitCampaignBoardBuilding()
        {
            InitCampaignBoardBuilding(false);
        }

        private void InitCampaignBoardBuilding(bool isEarlyInit)
        {
            try
            {
                if (campaignBoardInjected)
                {
                    return;
                }

                // 退役（2026-09-22）：征程改由官方 NPC 杰夫发放，公告板不再进建造菜单。
                // 老档已建过的必须照常注入 BuildingInfo + prefab：官方 BuildingArea 走 building.Info.Prefab
                // （Info = BuildingDataCollection.GetInfo(id)），少了 info 那条就是 "No prefab for building" 幽灵建筑。
                // 建筑管理器未就绪时不能当「没建过」：早期注入那一趟会漏注入，老档留幽灵——不置 injected，等下一个入口重试。
                CampaignBoardPresence presence = ProbeExistingCampaignBoards();
                if (presence == CampaignBoardPresence.None)
                {
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "公告板已退役且本档未建过，不进建造菜单");
                    return;
                }
                if (presence == CampaignBoardPresence.Unknown)
                {
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑管理器未就绪，公告板老档探测推迟到下一个入口");
                    return;
                }

                CampaignLocalization.InjectBuildingKeys();
                LoadCampaignBoardIcon();
                CreateCampaignBoardPrefab();
                InjectCampaignBoardData();

                campaignBoardInjected = true;

                // 早期注入时 BuildingArea 还没 Start，重绘会白跑一趟
                if (!isEarlyInit)
                {
                    _owner.RequestBaseBuildingAreaRepaint("InitCampaignBoardBuilding");
                }

                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "建筑初始化失败: "
                    + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 早期注入：老存档里已经建过公告板时，必须**赶在 BuildingArea.Start 之前**
        /// 把 prefab 注册好，否则官方会先报「缺 prefab」。
        /// </summary>
        internal void TryInitializeCampaignBoardEarly()
        {
            try
            {
                if (campaignBoardInjected) return;

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !ModBehaviour.IsBaseHubSceneName(activeScene.name)) return;

                Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (bdcType == null) return;

                PropertyInfo instanceProp = bdcType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null || instanceProp.GetValue(null, null) == null) return;

                InitCampaignBoardBuilding(true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "早期注入跳过: " + e.Message);
            }
        }

        #endregion

        #region 资源加载（缺文件即走占位）

        private void LoadCampaignBoardIcon()
        {
            if (campaignBoardIcon != null) return;
            campaignBoardIcon = ProductionIconCache.Get("Assets/buildings/" + CAMPAIGN_BOARD_BUILDING_ID + ".png");
            if (campaignBoardIcon != null || !ProductionIconCache.AllowRawFallback) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string iconPath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                iconPath = Path.Combine(iconPath, CAMPAIGN_BOARD_BUILDING_ID + ".png");
                if (!File.Exists(iconPath))
                {
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑图标缺失，使用官方默认图标");
                    return;
                }

                campaignBoardIcon = RawImageLoader.LoadSprite(iconPath, CAMPAIGN_BOARD_BUILDING_ID);
                if (campaignBoardIcon == null) return;
                CampaignAssetCache.Own(campaignBoardIcon.texture);
                CampaignAssetCache.Own(campaignBoardIcon);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑图标加载失败: " + e.Message);
            }
        }

        #endregion

        #region 预制体

        private void CreateCampaignBoardPrefab()
        {
            if (campaignBoardPrefabGO != null) return;

            // 先 inactive：官方 Building.Awake 会解引用 functionContainer（CreateAreaMesh），
            // 必须等反射把容器字段填好之后再激活。
            campaignBoardPrefabGO = new GameObject(CAMPAIGN_BOARD_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(campaignBoardPrefabGO);
            campaignBoardPrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            campaignBoardPrefabGO.SetActive(false);

            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(campaignBoardPrefabGO.transform, false);
            if (!BuildingModelHelper.TryInstantiateBundle(CAMPAIGN_BOARD_BUILDING_ID, CAMPAIGN_BOARD_PREFAB_NAME, graphicsContainer.transform, out campaignBoardModelBundle))
                CreateCampaignBoardPlaceholderModel(graphicsContainer);
            else
                BuildingModelHelper.PrepareBaseBuildingModel(graphicsContainer.transform.GetChild(0).gameObject);

            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(campaignBoardPrefabGO.transform, false);

            GameObject interactPoint = new GameObject("CampaignBoardInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);
            interactPoint.transform.localPosition = CAMPAIGN_BOARD_INTERACT_OFFSET;

            AddCampaignBoardBuildingComponent(campaignBoardPrefabGO);
            EnsureCampaignBoardFunctionPoints(campaignBoardPrefabGO);
            campaignBoardPrefabGO.SetActive(true);

            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "预制体创建完成");
        }

        /// <summary>
        /// 占位模型：木质立柱 + 倾斜的公告板面 + 钉着的悬赏纸。
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互射线。
        /// 补上 Assets/buildings/bossrush_campaign_board 的 AssetBundle 后本方法不再被调用。
        /// </summary>
        private void CreateCampaignBoardPlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");

                Color post = new Color(0.36f, 0.26f, 0.17f, 1f);
                Color board = new Color(0.52f, 0.38f, 0.24f, 1f);
                Color paper = new Color(0.90f, 0.87f, 0.78f, 1f);
                Color seal = new Color(0.72f, 0.18f, 0.16f, 1f);

                // 两根立柱
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "PostL",
                    new Vector3(0.08f, 1.10f, 0.08f), new Vector3(-0.30f, 0.55f, 0f),
                    post, shader, Quaternion.identity);
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "PostR",
                    new Vector3(0.08f, 1.10f, 0.08f), new Vector3(0.30f, 0.55f, 0f),
                    post, shader, Quaternion.identity);

                // 板面：略微后仰，像真的公告板
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "Board",
                    new Vector3(0.80f, 0.62f, 0.06f), new Vector3(0f, 0.95f, -0.04f),
                    board, shader, Quaternion.Euler(-12f, 0f, 0f));

                // 钉着的三张悬赏纸
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "Paper1",
                    new Vector3(0.20f, 0.26f, 0.02f), new Vector3(-0.20f, 1.02f, -0.08f),
                    paper, shader, Quaternion.Euler(-12f, 0f, 4f));
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "Paper2",
                    new Vector3(0.20f, 0.22f, 0.02f), new Vector3(0.08f, 1.05f, -0.08f),
                    paper, shader, Quaternion.Euler(-12f, 0f, -6f));
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Cube, "Paper3",
                    new Vector3(0.16f, 0.18f, 0.02f), new Vector3(0.26f, 0.88f, -0.08f),
                    paper, shader, Quaternion.Euler(-12f, 0f, 9f));

                // 红蜡封：一眼能认出这是「悬赏/契约」而不是普通告示
                CreateCampaignBoardPart(graphicsContainer, PrimitiveType.Sphere, "Seal",
                    new Vector3(0.07f, 0.07f, 0.03f), new Vector3(-0.20f, 0.92f, -0.10f),
                    seal, shader, Quaternion.identity);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "占位模型创建失败: " + e.Message);
            }
        }

        private void CreateCampaignBoardPart(
            GameObject parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 localPos, Color color, Shader shader, Quaternion rotation)
        {
            try
            {
                GameObject part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(parent.transform, false);
                part.transform.localPosition = localPos;
                part.transform.localRotation = rotation;
                part.transform.localScale = scale;

                // CreatePrimitive 自带碰撞体，会干扰建筑放置与交互射线
                Collider collider = part.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);

                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null && shader != null)
                {
                    Material material = new Material(shader);
                    CampaignAssetCache.Own(material);
                    material.color = color;
                    renderer.material = material;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "占位部件创建失败 " + name + ": " + e.Message);
            }
        }

        #endregion

        #region Building 组件与交互点

        private void AddCampaignBoardBuildingComponent(GameObject go)
        {
            Type buildingType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "无法找到 Building 类型");
                return;
            }

            Component buildingComp = go.AddComponent(buildingType);
            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo idField = buildingType.GetField("id", privateFlags);
            if (idField != null) idField.SetValue(buildingComp, CAMPAIGN_BOARD_BUILDING_ID);

            FieldInfo dimField = buildingType.GetField("dimensions", privateFlags);
            if (dimField != null) dimField.SetValue(buildingComp, CAMPAIGN_BOARD_SIZE);

            FieldInfo graphicsField = buildingType.GetField("graphicsContainer", privateFlags);
            if (graphicsField != null)
            {
                BuildingInjectionHelper.AssignBuildingContainerField(graphicsField, buildingComp, go.transform.Find("Graphics"));
            }

            FieldInfo functionField = buildingType.GetField("functionContainer", privateFlags);
            if (functionField != null)
            {
                BuildingInjectionHelper.AssignBuildingContainerField(functionField, buildingComp, go.transform.Find("Function"));
            }

            // areaMesh 置 null，让实例自己在 Awake 里 CreateAreaMesh
            FieldInfo areaMeshField = buildingType.GetField("areaMesh", privateFlags);
            if (areaMeshField != null) areaMeshField.SetValue(buildingComp, null);

            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "Building 组件已添加，ID=" + CAMPAIGN_BOARD_BUILDING_ID);
        }

        /// <summary>装配交互点：碰撞体 + CampaignBoardInteractable。</summary>
        private void EnsureCampaignBoardFunctionPoints(GameObject root)
        {
            try
            {
                if (root == null) return;
                Transform function = root.transform.Find("Function");
                if (function == null) return;

                Transform point = function.Find("CampaignBoardInteractPoint");
                if (point == null) return;

                GameObject pointGO = point.gameObject;

                BoxCollider collider = pointGO.GetComponent<BoxCollider>();
                if (collider == null) collider = pointGO.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(1.2f, 1.6f, 1.2f);
                collider.center = new Vector3(0f, 0.8f, 0f);

                if (pointGO.GetComponent<CampaignBoardInteractable>() == null)
                {
                    pointGO.AddComponent<CampaignBoardInteractable>();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "交互点装配失败: " + e.Message);
            }
        }

        #endregion

        #region 数据注入（BuildingDataCollection）

        private void InjectCampaignBoardData()
        {
            Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "无法找到 BuildingDataCollection 类型");
                return;
            }

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
            if (bdcInstance == null)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "BuildingDataCollection.Instance 为 null");
                return;
            }

            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            object infosList = infosField != null ? infosField.GetValue(bdcInstance) : null;
            if (infosList == null)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "无法获取 infos 列表");
                return;
            }

            Type buildingInfoType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null)
            {
                ModBehaviour.LogError(CampaignTuning.LogPrefix + "无法找到 BuildingInfo 类型");
                return;
            }

            // 判重：BuildingDataCollection 是长寿 ScriptableObject，同进程内不能重复注入
            FieldInfo infoIdField = buildingInfoType.GetField("id");
            IEnumerator enumerator = ((IEnumerable)infosList).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (infoIdField == null) break;
                string existingId = infoIdField.GetValue(enumerator.Current) as string;
                if (string.Equals(existingId, CAMPAIGN_BOARD_BUILDING_ID, StringComparison.Ordinal))
                {
                    ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑数据已存在，跳过注入");
                    return;
                }
            }

            object newInfo = Activator.CreateInstance(buildingInfoType);
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "id", CAMPAIGN_BOARD_BUILDING_ID);
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "prefabName", CAMPAIGN_BOARD_PREFAB_NAME);
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "maxAmount", CAMPAIGN_BOARD_MAX_AMOUNT);
            // 这三个必须给空数组不能留 null：官方 RequirementsSatisfied 会直接遍历
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "requireBuildings", new string[0]);
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "alternativeFor", new string[0]);
            SetCampaignBoardInfoField(buildingInfoType, newInfo, "requireQuests", new int[0]);
            if (campaignBoardIcon != null)
            {
                SetCampaignBoardInfoField(buildingInfoType, newInfo, "iconReference", campaignBoardIcon);
            }
            SetCampaignBoardCost(buildingInfoType, ref newInfo);

            MethodInfo addMethod = infosList.GetType().GetMethod("Add");
            if (addMethod != null)
            {
                addMethod.Invoke(infosList, new object[] { newInfo });
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "BuildingInfo 已注入");
            }

            FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            object prefabsList = prefabsField != null ? prefabsField.GetValue(bdcInstance) : null;
            if (prefabsList != null)
            {
                Type buildingType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building");
                Component buildingComp = buildingType != null && campaignBoardPrefabGO != null
                    ? campaignBoardPrefabGO.GetComponent(buildingType)
                    : null;
                if (buildingComp != null)
                {
                    MethodInfo prefabAddMethod = prefabsList.GetType().GetMethod("Add");
                    if (prefabAddMethod != null)
                    {
                        prefabAddMethod.Invoke(prefabsList, new object[] { buildingComp });
                        ModBehaviour.DevLog(CampaignTuning.LogPrefix + "Building prefab 已注入");
                    }
                }
            }

            // 清 readonly 缓存，让官方下次重建只读视图
            FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
            if (readonlyField != null) readonlyField.SetValue(bdcInstance, null);

            ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑数据注入完成");
        }

        private static void SetCampaignBoardInfoField(
            Type buildingInfoType, object target, string fieldName, object value)
        {
            FieldInfo field = buildingInfoType.GetField(fieldName);
            if (field != null) field.SetValue(target, value);
        }

        /// <summary>官方 Cost 是 struct，必须整体 boxing 后写回。</summary>
        private void SetCampaignBoardCost(Type buildingInfoType, ref object buildingInfo)
        {
            try
            {
                Type costType = BuildingInjectionHelper.FindGameType("Duckov.Economy.Cost");
                if (costType == null) return;

                object cost;
                ConstructorInfo costCtor = costType.GetConstructor(new Type[] { typeof(long) });
                if (costCtor != null)
                {
                    cost = costCtor.Invoke(new object[] { CAMPAIGN_BOARD_COST });
                }
                else
                {
                    cost = Activator.CreateInstance(costType);
                    FieldInfo moneyField = costType.GetField("money");
                    if (moneyField != null) moneyField.SetValue(cost, CAMPAIGN_BOARD_COST);
                    FieldInfo itemsField = costType.GetField("items");
                    if (itemsField != null)
                    {
                        Type entryType = costType.GetNestedType("ItemEntry") ?? typeof(object);
                        itemsField.SetValue(cost, Array.CreateInstance(entryType, 0));
                    }
                }

                FieldInfo costField = buildingInfoType.GetField("cost");
                if (costField != null) costField.SetValue(buildingInfo, cost);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑费用设置失败: " + e.Message);
            }
        }

        /// <summary>老档里是否已经建过公告板（dormant 契约的例外判定）。</summary>
        /// <summary>老档探测的三态：Unknown 不能当 None（会漏注入留幽灵建筑）。</summary>
        internal enum CampaignBoardPresence { Unknown = 0, None = 1, Yes = 2 }

        /// <summary>
        /// 本档有没有建过公告板（按原始 ID 计数，官方 GetBuildingAmount；不用 Any——它跳过 info 未注册的记录，
        /// 注入前恒 false，等于循环依赖）。管理器 / 方法拿不到或异常 → Unknown。
        /// </summary>
        private CampaignBoardPresence ProbeExistingCampaignBoards()
        {
            try
            {
                Type managerType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingManager");
                if (managerType == null) return CampaignBoardPresence.Unknown;

                MethodInfo getAmount = managerType.GetMethod(
                    "GetBuildingAmount", BindingFlags.Public | BindingFlags.Static);
                if (getAmount == null) return CampaignBoardPresence.Unknown;

                object result = getAmount.Invoke(null, new object[] { CAMPAIGN_BOARD_BUILDING_ID });
                if (result == null) return CampaignBoardPresence.Unknown;
                return Convert.ToInt32(result) > 0 ? CampaignBoardPresence.Yes : CampaignBoardPresence.None;
            }
            catch (Exception)
            {
                return CampaignBoardPresence.Unknown;
            }
        }

        #endregion

        #region 场景装配

        /// <summary>
        /// 基地场景装配：把线索条目注册进官方笔记图鉴。
        /// 开关关闭时跳过（dormant）；注册本身幂等，每次进基地调一次即可。
        /// </summary>
        internal void RegisterCampaignNotesForScene()
        {
            try
            {
                if (!_owner.IsCampaignConfiguredEnabled()) return;
                CampaignProgressService.EnsureInitialized();
                CampaignNoteBridge.EnsureNotesRegistered();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "线索注册跳过: " + e.Message);
            }
        }

        #endregion

        #region 清理

        /// <summary>Mod 卸载路径的清理。</summary>
        public void CleanupCampaignBoardBuilding()
        {
            try
            {
                BossRush.Utils.AssetBundleUnloadHelper.TryUnload(campaignBoardModelBundle, CampaignTuning.LogPrefix);
                campaignBoardModelBundle = null;
                campaignBoardIcon = null;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(CampaignTuning.LogPrefix + "建筑清理失败: " + e.Message);
            }
        }

        #endregion
    }
}
