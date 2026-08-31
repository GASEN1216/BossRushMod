// ============================================================================
// ShowcaseBuildingBuilder.cs - 基地「战利品展示柜」建筑注入器
// ============================================================================
// 形态与 Campaign/CampaignBoardBuilder.cs 逐条对齐（两者都照
// Integration/DailyReport/DailyReportMailboxBuilder.cs 这个母版）。
//
// 【dormant 契约】
//   后山关闭或展示柜未解锁时不往建造 UI 里塞建筑。
//   **老档已建过是例外**：必须照常注册 prefab，否则官方 BuildingArea 会报缺 prefab，
//   留下一个幽灵建筑。
//
// 零新增 Unity 资源：走程序化占位模型；补上 Assets/buildings/ 同名 bundle 与 png
// 之后本文件零改动。
//
// 共享反射工具（FindGameType / AssignBuildingContainerField /
// RequestBaseBuildingAreaRepaint）定义在 Integration/Wedding/ 下、
// 属于同一个 partial class ModBehaviour，**不得重复定义**。
// ============================================================================

using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>展示柜建筑注入器（partial class ModBehaviour）。</summary>
    public partial class ModBehaviour
    {
        #region 常量

        private const string BACKMOUNTAIN_SHOWCASE_BUILDING_ID = BackMountainConfig.ShowcaseBuildingId;
        private const string BACKMOUNTAIN_SHOWCASE_PREFAB_NAME = "BossRushBackMountainShowcase";
        private static readonly Vector2Int BACKMOUNTAIN_SHOWCASE_SIZE = new Vector2Int(2, 1);
        private const long BACKMOUNTAIN_SHOWCASE_COST = BackMountainConfig.ShowcaseBuildCost;
        private const int BACKMOUNTAIN_SHOWCASE_MAX_AMOUNT = 1;

        #endregion

        #region 状态

        private bool backMountainShowcaseInjected;
        private GameObject backMountainShowcasePrefabGO;
        private static Sprite backMountainShowcaseIcon;

        #endregion

        #region 初始化

        /// <summary>基地场景装配管线调用的公开入口。</summary>
        public void InitBackMountainShowcase()
        {
            InitBackMountainShowcase(false);
        }

        private void InitBackMountainShowcase(bool isEarlyInit)
        {
            try
            {
                if (backMountainShowcaseInjected) return;

                bool unlocked = IsBackMountainConfiguredEnabled()
                    && BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase);
                if (!unlocked && !HasPendingShowcaseBuildingsInManager())
                {
                    DevLog(BackMountainConfig.LogPrefix + "展示柜未解锁且未建过，跳过建筑注入（dormant）");
                    return;
                }

                BackMountainLocalization.InjectBuildingKeys();
                LoadShowcaseBuildingIcon();
                CreateShowcaseBuildingPrefab();
                InjectShowcaseBuildingData();

                backMountainShowcaseInjected = true;

                if (!isEarlyInit && HasPendingShowcaseBuildingsInManager())
                {
                    RequestBaseBuildingAreaRepaint("InitBackMountainShowcase");
                }

                DevLog(BackMountainConfig.LogPrefix + "展示柜建筑注入完成");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError(BackMountainConfig.LogPrefix + "展示柜建筑初始化失败: "
                    + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 早期注入：老存档里已建过展示柜时，必须赶在 BuildingArea.Start 之前注册 prefab。
        /// </summary>
        internal void TryInitializeBackMountainShowcaseEarly()
        {
            try
            {
                if (backMountainShowcaseInjected) return;

                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid() || !IsBaseHubSceneName(activeScene.name)) return;

                Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (bdcType == null) return;

                PropertyInfo instanceProp = bdcType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null || instanceProp.GetValue(null, null) == null) return;

                InitBackMountainShowcase(true);
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜早期注入跳过: " + e.Message);
            }
        }

        #endregion

        #region 资源与预制体

        private void LoadShowcaseBuildingIcon()
        {
            if (backMountainShowcaseIcon != null) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string iconPath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                iconPath = Path.Combine(iconPath, BACKMOUNTAIN_SHOWCASE_BUILDING_ID + ".png");
                if (!File.Exists(iconPath)) return;

                byte[] bytes = File.ReadAllBytes(iconPath);
                Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!texture.LoadImage(bytes)) return;
                backMountainShowcaseIcon = Sprite.Create(
                    texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜图标加载失败: " + e.Message);
            }
        }

        private void CreateShowcaseBuildingPrefab()
        {
            if (backMountainShowcasePrefabGO != null) return;

            // 先 inactive：官方 Building.Awake 会解引用 functionContainer，
            // 必须等反射把容器字段填好之后再激活。
            backMountainShowcasePrefabGO = new GameObject(BACKMOUNTAIN_SHOWCASE_PREFAB_NAME);
            UnityEngine.Object.DontDestroyOnLoad(backMountainShowcasePrefabGO);
            backMountainShowcasePrefabGO.transform.position = new Vector3(0f, -9999f, 0f);
            backMountainShowcasePrefabGO.SetActive(false);

            GameObject graphicsContainer = new GameObject("Graphics");
            graphicsContainer.transform.SetParent(backMountainShowcasePrefabGO.transform, false);
            CreateShowcasePlaceholderModel(graphicsContainer);

            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(backMountainShowcasePrefabGO.transform, false);

            GameObject interactPoint = new GameObject("ShowcaseInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);

            AddShowcaseBuildingComponent(backMountainShowcasePrefabGO);
            EnsureShowcaseFunctionPoints(backMountainShowcasePrefabGO);
            backMountainShowcasePrefabGO.SetActive(true);

            DevLog(BackMountainConfig.LogPrefix + "展示柜预制体创建完成");
        }

        /// <summary>
        /// 占位模型：木质陈列台 + 玻璃罩 + 三个小基座。
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互射线。
        /// </summary>
        private void CreateShowcasePlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader == null) shader = Shader.Find("Standard");

                Color wood = new Color(0.42f, 0.30f, 0.20f, 1f);
                Color glass = new Color(0.62f, 0.78f, 0.85f, 0.35f);
                Color pedestal = new Color(0.30f, 0.30f, 0.34f, 1f);

                CreateShowcasePart(graphicsContainer, PrimitiveType.Cube, "Counter",
                    new Vector3(1.80f, 0.55f, 0.80f), new Vector3(0f, 0.28f, 0f), wood, shader);
                CreateShowcasePart(graphicsContainer, PrimitiveType.Cube, "Glass",
                    new Vector3(1.70f, 0.62f, 0.70f), new Vector3(0f, 0.88f, 0f), glass, shader);

                for (int i = 0; i < 3; i++)
                {
                    CreateShowcasePart(graphicsContainer, PrimitiveType.Cylinder, "Pedestal" + i,
                        new Vector3(0.22f, 0.06f, 0.22f), new Vector3(-0.55f + i * 0.55f, 0.60f, 0f),
                        pedestal, shader);
                }
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜占位模型创建失败: " + e.Message);
            }
        }

        private void CreateShowcasePart(
            GameObject parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 localPos, Color color, Shader shader)
        {
            try
            {
                GameObject part = GameObject.CreatePrimitive(type);
                part.name = name;
                part.transform.SetParent(parent.transform, false);
                part.transform.localPosition = localPos;
                part.transform.localScale = scale;

                Collider collider = part.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.Destroy(collider);

                Renderer renderer = part.GetComponent<Renderer>();
                if (renderer != null && shader != null)
                {
                    Material material = new Material(shader);
                    material.color = color;
                    renderer.material = material;
                }
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜占位部件失败 " + name + ": " + e.Message);
            }
        }

        private void AddShowcaseBuildingComponent(GameObject go)
        {
            Type buildingType = FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                ModBehaviour.LogError(BackMountainConfig.LogPrefix + "无法找到 Building 类型");
                return;
            }

            Component buildingComp = go.AddComponent(buildingType);
            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo idField = buildingType.GetField("id", privateFlags);
            if (idField != null) idField.SetValue(buildingComp, BACKMOUNTAIN_SHOWCASE_BUILDING_ID);

            FieldInfo dimField = buildingType.GetField("dimensions", privateFlags);
            if (dimField != null) dimField.SetValue(buildingComp, BACKMOUNTAIN_SHOWCASE_SIZE);

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
        }

        private void EnsureShowcaseFunctionPoints(GameObject root)
        {
            try
            {
                if (root == null) return;
                Transform function = root.transform.Find("Function");
                if (function == null) return;
                Transform point = function.Find("ShowcaseInteractPoint");
                if (point == null) return;

                GameObject pointGO = point.gameObject;

                BoxCollider collider = pointGO.GetComponent<BoxCollider>();
                if (collider == null) collider = pointGO.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(2.0f, 1.6f, 1.2f);
                collider.center = new Vector3(0f, 0.8f, 0f);

                if (pointGO.GetComponent<ShowcaseInteractable>() == null)
                {
                    pointGO.AddComponent<ShowcaseInteractable>();
                }
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜交互点装配失败: " + e.Message);
            }
        }

        #endregion

        #region 数据注入

        private void InjectShowcaseBuildingData()
        {
            Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null) return;

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
            if (bdcInstance == null) return;

            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            object infosList = infosField != null ? infosField.GetValue(bdcInstance) : null;
            if (infosList == null) return;

            Type buildingInfoType = FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null) return;

            // 判重：BuildingDataCollection 是长寿 ScriptableObject，不能重复注入
            FieldInfo infoIdField = buildingInfoType.GetField("id");
            IEnumerator enumerator = ((IEnumerable)infosList).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (infoIdField == null) break;
                string existingId = infoIdField.GetValue(enumerator.Current) as string;
                if (string.Equals(existingId, BACKMOUNTAIN_SHOWCASE_BUILDING_ID, StringComparison.Ordinal))
                {
                    return;
                }
            }

            object newInfo = Activator.CreateInstance(buildingInfoType);
            SetShowcaseInfoField(buildingInfoType, newInfo, "id", BACKMOUNTAIN_SHOWCASE_BUILDING_ID);
            SetShowcaseInfoField(buildingInfoType, newInfo, "prefabName", BACKMOUNTAIN_SHOWCASE_PREFAB_NAME);
            SetShowcaseInfoField(buildingInfoType, newInfo, "maxAmount", BACKMOUNTAIN_SHOWCASE_MAX_AMOUNT);
            // 这三个必须给空数组不能留 null：官方 RequirementsSatisfied 会直接遍历
            SetShowcaseInfoField(buildingInfoType, newInfo, "requireBuildings", new string[0]);
            SetShowcaseInfoField(buildingInfoType, newInfo, "alternativeFor", new string[0]);
            SetShowcaseInfoField(buildingInfoType, newInfo, "requireQuests", new int[0]);
            if (backMountainShowcaseIcon != null)
            {
                SetShowcaseInfoField(buildingInfoType, newInfo, "iconReference", backMountainShowcaseIcon);
            }
            SetShowcaseBuildingCost(buildingInfoType, ref newInfo);

            MethodInfo addMethod = infosList.GetType().GetMethod("Add");
            if (addMethod != null) addMethod.Invoke(infosList, new object[] { newInfo });

            FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            object prefabsList = prefabsField != null ? prefabsField.GetValue(bdcInstance) : null;
            if (prefabsList != null)
            {
                Type buildingType = FindGameType("Duckov.Buildings.Building");
                Component buildingComp = buildingType != null && backMountainShowcasePrefabGO != null
                    ? backMountainShowcasePrefabGO.GetComponent(buildingType)
                    : null;
                if (buildingComp != null)
                {
                    MethodInfo prefabAddMethod = prefabsList.GetType().GetMethod("Add");
                    if (prefabAddMethod != null) prefabAddMethod.Invoke(prefabsList, new object[] { buildingComp });
                }
            }

            FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
            if (readonlyField != null) readonlyField.SetValue(bdcInstance, null);
        }

        private static void SetShowcaseInfoField(
            Type buildingInfoType, object target, string fieldName, object value)
        {
            FieldInfo field = buildingInfoType.GetField(fieldName);
            if (field != null) field.SetValue(target, value);
        }

        /// <summary>官方 Cost 是 struct，必须整体 boxing 后写回。</summary>
        private void SetShowcaseBuildingCost(Type buildingInfoType, ref object buildingInfo)
        {
            try
            {
                Type costType = FindGameType("Duckov.Economy.Cost");
                if (costType == null) return;

                object cost;
                ConstructorInfo costCtor = costType.GetConstructor(new Type[] { typeof(long) });
                if (costCtor != null)
                {
                    cost = costCtor.Invoke(new object[] { BACKMOUNTAIN_SHOWCASE_COST });
                }
                else
                {
                    cost = Activator.CreateInstance(costType);
                    FieldInfo moneyField = costType.GetField("money");
                    if (moneyField != null) moneyField.SetValue(cost, BACKMOUNTAIN_SHOWCASE_COST);
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
                DevLog(BackMountainConfig.LogPrefix + "展示柜费用设置失败: " + e.Message);
            }
        }

        /// <summary>老档里是否已经建过展示柜（dormant 契约的例外判定）。</summary>
        private bool HasPendingShowcaseBuildingsInManager()
        {
            try
            {
                Type managerType = FindGameType("Duckov.Buildings.BuildingManager");
                if (managerType == null) return false;

                MethodInfo getAmount = managerType.GetMethod(
                    "GetBuildingAmount", BindingFlags.Public | BindingFlags.Static);
                if (getAmount == null) return false;

                object result = getAmount.Invoke(null, new object[] { BACKMOUNTAIN_SHOWCASE_BUILDING_ID });
                if (result == null) return false;
                return Convert.ToInt32(result) > 0;
            }
            catch (Exception)
            {
                // 读不到就当作没建过：dormant 时不注入是更保守的一侧
                return false;
            }
        }

        #endregion

        #region 清理

        /// <summary>Mod 卸载路径的清理。</summary>
        public void CleanupBackMountainShowcase()
        {
            try
            {
                backMountainShowcaseIcon = null;
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "展示柜清理失败: " + e.Message);
            }
        }

        #endregion
    }
}
