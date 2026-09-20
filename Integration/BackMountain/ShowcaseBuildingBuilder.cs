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
// 模型优先读取 Assets/buildings/bossrush_backmountain_showcase，缺包保留图元；资源由本 owner 释放。
//
// 共享反射工具（FindGameType / AssignBuildingContainerField /
// RequestBaseBuildingAreaRepaint）由共享工具与显式 _owner 提供，模块不再借 partial 访问兄弟私有状态。
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    /// <summary>展示柜建筑注入器（显式 ModBehaviour owner）。</summary>
    internal sealed partial class ShowcaseBuildingBuilder
    {
        private readonly ModBehaviour _owner;

        internal ShowcaseBuildingBuilder(ModBehaviour owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _owner = owner;
        }

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
        private AssetBundle showcaseModelBundle;
        private Sprite backMountainShowcaseIcon;
        private Texture2D _iconTexture;
        private readonly List<Material> _materials = new List<Material>();

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

                bool unlocked = _owner.IsBackMountainConfiguredEnabled()
                    && BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Showcase);
                if (!unlocked && !HasPendingShowcaseBuildingsInManager())
                {
                    ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜未解锁且未建过，跳过建筑注入（dormant）");
                    return;
                }

                BackMountainLocalization.InjectBuildingKeys();
                LoadShowcaseBuildingIcon();
                CreateShowcaseBuildingPrefab();
                if (!InjectShowcaseBuildingData()) return;

                backMountainShowcaseInjected = true;

                if (!isEarlyInit && HasPendingShowcaseBuildingsInManager())
                {
                    _owner.RequestBaseBuildingAreaRepaint("InitBackMountainShowcase");
                }

                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜建筑注入完成");
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
                if (!activeScene.IsValid() || !ModBehaviour.IsBaseHubSceneName(activeScene.name)) return;

                Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
                if (bdcType == null) return;

                PropertyInfo instanceProp = bdcType.GetProperty(
                    "Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProp == null || instanceProp.GetValue(null, null) == null) return;

                InitBackMountainShowcase(true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜早期注入跳过: " + e.Message);
            }
        }

        #endregion

        #region 资源与预制体

        private void LoadShowcaseBuildingIcon()
        {
            if (backMountainShowcaseIcon != null) return;
            backMountainShowcaseIcon = ProductionIconCache.Get("Assets/buildings/" + BACKMOUNTAIN_SHOWCASE_BUILDING_ID + ".png");
            if (backMountainShowcaseIcon != null || !ProductionIconCache.AllowRawFallback) return;
            try
            {
                string modDir = Path.GetDirectoryName(typeof(ModBehaviour).Assembly.Location);
                if (string.IsNullOrEmpty(modDir)) return;
                string iconPath = Path.Combine(modDir, Path.Combine("Assets", "buildings"));
                iconPath = Path.Combine(iconPath, BACKMOUNTAIN_SHOWCASE_BUILDING_ID + ".png");
                if (!File.Exists(iconPath)) return;

                backMountainShowcaseIcon = RawImageLoader.LoadSprite(iconPath, BACKMOUNTAIN_SHOWCASE_BUILDING_ID);
                if (backMountainShowcaseIcon != null) _iconTexture = backMountainShowcaseIcon.texture;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜图标加载失败: " + e.Message);
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
            if (!BuildingModelHelper.TryInstantiateBundle(BACKMOUNTAIN_SHOWCASE_BUILDING_ID, BACKMOUNTAIN_SHOWCASE_PREFAB_NAME, graphicsContainer.transform, out showcaseModelBundle))
                CreateShowcasePlaceholderModel(graphicsContainer);

            GameObject functionContainer = new GameObject("Function");
            functionContainer.transform.SetParent(backMountainShowcasePrefabGO.transform, false);

            GameObject interactPoint = new GameObject("ShowcaseInteractPoint");
            interactPoint.transform.SetParent(functionContainer.transform, false);

            AddShowcaseBuildingComponent(backMountainShowcasePrefabGO);
            EnsureShowcaseFunctionPoints(backMountainShowcasePrefabGO);
            backMountainShowcasePrefabGO.SetActive(true);

            ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜预制体创建完成");
        }

        /// <summary>
        /// 占位模型：木质陈列台 + 玻璃罩 + 三个小基座。
        /// CreatePrimitive 自带碰撞体，必须删掉——留着会干扰建筑放置与交互射线。
        /// </summary>
        private void CreateShowcasePlaceholderModel(GameObject graphicsContainer)
        {
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");

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
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜占位模型创建失败: " + e.Message);
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
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", color);
                    if (color.a < 1f)
                    {
                        material.SetFloat("_Surface", 1f);
                        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        material.SetFloat("_ZWrite", 0f);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        material.renderQueue = 3000;
                        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    _materials.Add(material);
                    renderer.sharedMaterial = material;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜占位部件失败 " + name + ": " + e.Message);
            }
        }

        private void AddShowcaseBuildingComponent(GameObject go)
        {
            Type buildingType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building");
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
                BuildingInjectionHelper.AssignBuildingContainerField(graphicsField, buildingComp, go.transform.Find("Graphics"));
            }

            FieldInfo functionField = buildingType.GetField("functionContainer", privateFlags);
            if (functionField != null)
            {
                BuildingInjectionHelper.AssignBuildingContainerField(functionField, buildingComp, go.transform.Find("Function"));
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
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜交互点装配失败: " + e.Message);
            }
        }

        #endregion

        /// <summary>
        /// 换槽 / 删档：把注入闸复位，并把已注入的建筑条目从官方长寿表里摘掉。
        ///
        /// `BuildingDataCollection` 是跨场景跨存档槽都不会重建的 ScriptableObject，
        /// 而注入时 requireBuildings / requireQuests 都被显式设成空数组，官方
        /// `RequirementsSatisfied()` 因此恒为 true——一旦在 A 档解锁并注入，
        /// 同一次游戏会话里切到 B 档（哪怕全新档）建造菜单里照样列着展示柜，
        /// 玩家能花 800 建一个在该档永远打不开的柜子（交互体的 IsInteractable
        /// 会因 B 档未解锁而恒 false）。
        ///
        /// 摘掉之后不影响 A 档：回到 A 档时基地装配管线会重新走
        /// InitBackMountainShowcase 按该槽的解锁状态重新注入。
        /// </summary>
        internal void NotifyShowcaseSlotChanged()
        {
            try
            {
                RemoveShowcaseBuildingData();
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix
                    + "[WARNING] 展示柜建筑条目摘除失败: " + e.Message);
            }
            finally
            {
                // 闸必须复位：不复位的话新槽解锁后反而注入不上（早返直接吃掉）
                backMountainShowcaseInjected = false;
            }
        }

        /// <summary>按 id 从官方 infos 列表里摘掉展示柜条目。找不到即无操作。</summary>
        private void RemoveShowcaseBuildingData()
        {
            Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null) return;

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
            if (bdcInstance == null) return;

            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            IList infosList = infosField != null ? infosField.GetValue(bdcInstance) as IList : null;
            if (infosList == null) return;

            Type buildingInfoType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingInfo");
            FieldInfo infoIdField = buildingInfoType != null ? buildingInfoType.GetField("id") : null;
            if (infoIdField == null) return;

            for (int i = infosList.Count - 1; i >= 0; i--)
            {
                object info = infosList[i];
                if (info == null) continue;
                string existingId = infoIdField.GetValue(info) as string;
                if (string.Equals(existingId, BACKMOUNTAIN_SHOWCASE_BUILDING_ID, StringComparison.Ordinal))
                {
                    infosList.RemoveAt(i);
                }
            }
        }

        #region 数据注入

        private bool InjectShowcaseBuildingData()
        {
            Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null) return false;

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
            if (bdcInstance == null) return false;

            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            object infosList = infosField != null ? infosField.GetValue(bdcInstance) : null;
            if (infosList == null) return false;

            Type buildingInfoType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null) return false;

            Type buildingType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building");
            Component buildingComp = buildingType != null && backMountainShowcasePrefabGO != null
                ? backMountainShowcasePrefabGO.GetComponent(buildingType) : null;
            FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            IList prefabsList = prefabsField != null ? prefabsField.GetValue(bdcInstance) as IList : null;
            if (buildingComp == null || prefabsList == null || !(infosList is IList)) return false;
            if (!prefabsList.Contains(buildingComp)) prefabsList.Add(buildingComp);

            // 判重：BuildingDataCollection 是长寿 ScriptableObject，不能重复注入
            FieldInfo infoIdField = buildingInfoType.GetField("id");
            IEnumerator enumerator = ((IEnumerable)infosList).GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (infoIdField == null) break;
                string existingId = infoIdField.GetValue(enumerator.Current) as string;
                if (string.Equals(existingId, BACKMOUNTAIN_SHOWCASE_BUILDING_ID, StringComparison.Ordinal))
                {
                    return true;
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

            ((IList)infosList).Add(newInfo);

            FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
            if (readonlyField != null) readonlyField.SetValue(bdcInstance, null);
            return true;
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
                Type costType = BuildingInjectionHelper.FindGameType("Duckov.Economy.Cost");
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
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜费用设置失败: " + e.Message);
            }
        }

        /// <summary>老档里是否已经建过展示柜（dormant 契约的例外判定）。</summary>
        private bool HasPendingShowcaseBuildingsInManager()
        {
            try
            {
                Type managerType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingManager");
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
                RemoveShowcaseBuildingData();
                Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
                PropertyInfo instance = bdcType != null ? bdcType.GetProperty("Instance") : null;
                object collection = instance != null ? instance.GetValue(null, null) : null;
                FieldInfo field = bdcType != null ? bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance) : null;
                IList prefabs = collection != null && field != null ? field.GetValue(collection) as IList : null;
                if (prefabs != null && backMountainShowcasePrefabGO != null)
                {
                    for (int i = prefabs.Count - 1; i >= 0; i--)
                    {
                        Component entry = prefabs[i] as Component;
                        if (entry != null && entry.gameObject == backMountainShowcasePrefabGO) prefabs.RemoveAt(i);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(BackMountainConfig.LogPrefix + "展示柜清理失败: " + e.Message);
            }
            finally
            {
                if (backMountainShowcasePrefabGO != null) UnityEngine.Object.Destroy(backMountainShowcasePrefabGO);
                backMountainShowcasePrefabGO = null;
                BossRush.Utils.AssetBundleUnloadHelper.TryUnload(showcaseModelBundle, BackMountainConfig.LogPrefix);
                showcaseModelBundle = null;
                foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
                _materials.Clear();
                if (backMountainShowcaseIcon != null && !ProductionIconCache.IsBorrowed(backMountainShowcaseIcon)) UnityEngine.Object.Destroy(backMountainShowcaseIcon);
                if (_iconTexture != null) UnityEngine.Object.Destroy(_iconTexture);
                backMountainShowcaseIcon = null;
                _iconTexture = null;
                backMountainShowcaseInjected = false;
            }
        }

        #endregion
    }
}
