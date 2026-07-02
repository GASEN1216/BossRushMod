using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BossRush.Utils;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private Coroutine baseBuildingAreaRepaintCoroutine = null;

        private void InjectWeddingBuildingData()
        {
            // 获取 BuildingDataCollection 类型和实例
            Type bdcType = FindGameType("Duckov.Buildings.BuildingDataCollection");
            if (bdcType == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] 无法找到 BuildingDataCollection 类型");
                return;
            }

            // 获取单例实例
            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp?.GetValue(null);

            if (bdcInstance == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] BuildingDataCollection.Instance 为 null");
                return;
            }

            // ---- 注入 BuildingInfo 到 infos 列表 ----
            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            if (infosField == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] 无法获取 infos 字段");
                return;
            }

            object infosList = infosField.GetValue(bdcInstance);
            if (infosList == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] infos 列表为 null");
                return;
            }

            // 获取 BuildingInfo 类型
            Type buildingInfoType = FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null)
            {
                ModBehaviour.LogError("[WeddingBuilding] 无法找到 BuildingInfo 类型");
                return;
            }

            // 检查是否已存在同ID的建筑（避免重复注入）
            bool alreadyExists = false;
            var enumerator = ((IEnumerable)infosList).GetEnumerator();
            while (enumerator.MoveNext())
            {
                object info = enumerator.Current;
                FieldInfo infoIdField = buildingInfoType.GetField("id");
                if (infoIdField != null)
                {
                    string existingId = infoIdField.GetValue(info) as string;
                    if (existingId == WEDDING_BUILDING_ID)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
            }

            if (alreadyExists)
            {
                DevLog("[WeddingBuilding] 建筑数据已存在于 infos 列表中，跳过注入");
                return;
            }

            // 创建 BuildingInfo 实例
            object newBuildingInfo = Activator.CreateInstance(buildingInfoType);

            // 设置字段（BuildingInfo 是 struct，public 字段可直接设置）
            buildingInfoType.GetField("id")?.SetValue(newBuildingInfo, WEDDING_BUILDING_ID);
            buildingInfoType.GetField("prefabName")?.SetValue(newBuildingInfo, WEDDING_PREFAB_NAME);
            buildingInfoType.GetField("maxAmount")?.SetValue(newBuildingInfo, WEDDING_BUILDING_MAX_AMOUNT);
            buildingInfoType.GetField("requireBuildings")?.SetValue(newBuildingInfo, new string[0]);
            buildingInfoType.GetField("alternativeFor")?.SetValue(newBuildingInfo, new string[0]);
            buildingInfoType.GetField("requireQuests")?.SetValue(newBuildingInfo, new int[0]);

            // 设置建筑图标（用于建造UI按钮显示）
            if (weddingBuildingIcon != null)
            {
                buildingInfoType.GetField("iconReference")?.SetValue(newBuildingInfo, weddingBuildingIcon);
                DevLog("[WeddingBuilding] 建筑图标已设置到 BuildingInfo");
            }
            else
            {
                DevLog("[WeddingBuilding] 警告：建筑图标为空，建造UI将显示空白图标");
            }

            // 设置建造费用
            SetBuildingCost(buildingInfoType, ref newBuildingInfo);

            // 添加到 infos 列表
            MethodInfo addMethod = infosList.GetType().GetMethod("Add");
            if (addMethod != null)
            {
                addMethod.Invoke(infosList, new object[] { newBuildingInfo });
                DevLog("[WeddingBuilding] BuildingInfo 已注入到 infos 列表");
            }

            // ---- 注入 Building prefab 到 prefabs 列表 ----
            FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            if (prefabsField != null)
            {
                object prefabsList = prefabsField.GetValue(bdcInstance);
                if (prefabsList != null)
                {
                    // 获取 Building 组件
                    Type buildingType = FindGameType("Duckov.Buildings.Building");
                    if (buildingType != null)
                    {
                        Component buildingComp = weddingBuildingPrefabGO.GetComponent(buildingType);
                        if (buildingComp != null)
                        {
                            MethodInfo prefabAddMethod = prefabsList.GetType().GetMethod("Add");
                            prefabAddMethod?.Invoke(prefabsList, new object[] { buildingComp });
                            DevLog("[WeddingBuilding] Building prefab 已注入到 prefabs 列表");
                        }
                    }
                }
            }

            // ---- 重置 readonlyInfos 缓存 ----
            // readonlyInfos 是 public 字段，直接置 null 让它下次访问时重新生成
            FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
            if (readonlyField != null)
            {
                readonlyField.SetValue(bdcInstance, null);
                DevLog("[WeddingBuilding] readonlyInfos 缓存已重置");
            }

            DevLog("[WeddingBuilding] 建筑数据注入完成！建筑应该出现在建造UI中了");
        }

        /// <summary>
        /// 设置建筑的建造费用
        /// </summary>
        private void SetBuildingCost(Type buildingInfoType, ref object buildingInfo)
        {
            try
            {
                // 获取 Cost 类型
                Type costType = FindGameType("Duckov.Economy.Cost");
                if (costType == null)
                {
                    DevLog("[WeddingBuilding] 无法找到 Cost 类型，建筑将免费");
                    return;
                }

                // 使用 Cost(long money) 构造函数
                ConstructorInfo costCtor = costType.GetConstructor(new Type[] { typeof(long) });
                if (costCtor != null)
                {
                    object cost = costCtor.Invoke(new object[] { WEDDING_BUILDING_COST });
                    buildingInfoType.GetField("cost")?.SetValue(buildingInfo, cost);
                    DevLog("[WeddingBuilding] 建造费用设置为: " + WEDDING_BUILDING_COST);
                }
                else
                {
                    // 备用：直接设置 money 字段
                    object cost = Activator.CreateInstance(costType);
                    costType.GetField("money")?.SetValue(cost, WEDDING_BUILDING_COST);
                    costType.GetField("items")?.SetValue(cost, Array.CreateInstance(
                        costType.GetNestedType("ItemEntry") ?? typeof(object), 0));
                    buildingInfoType.GetField("cost")?.SetValue(buildingInfo, cost);
                    DevLog("[WeddingBuilding] 建造费用设置为: " + WEDDING_BUILDING_COST + "（备用方式）");
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] 设置费用失败: " + e.Message + "，建筑将免费");
            }
        }

        private void RequestBaseBuildingAreaRepaint(string source)
        {
            if (!IsBaseHubSceneName(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                return;
            }

            if (baseBuildingAreaRepaintCoroutine != null)
            {
                StopCoroutine(baseBuildingAreaRepaintCoroutine);
            }

            baseBuildingAreaRepaintCoroutine = StartCoroutine(RepaintBaseBuildingAreasDelayed(source));
        }

        private IEnumerator RepaintBaseBuildingAreasDelayed(string source)
        {
            yield return null;
            yield return null;

            try
            {
                if (!IsBaseHubSceneName(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                {
                    yield break;
                }

                Type buildingAreaType = FindGameType("Duckov.Buildings.BuildingArea");
                if (buildingAreaType == null)
                {
                    yield break;
                }

                MethodInfo repaintMethod = buildingAreaType.GetMethod(
                    "RepaintAll",
                    BindingFlags.Public | BindingFlags.Instance);
                if (repaintMethod == null)
                {
                    yield break;
                }

                ObjectCache.InvalidateSceneObjectsByType(buildingAreaType);
                UnityEngine.Object[] buildingAreas = ObjectCache.GetSceneObjectsByType(buildingAreaType);
                int repaintCount = 0;
                for (int i = 0; i < buildingAreas.Length; i++)
                {
                    Component buildingArea = buildingAreas[i] as Component;
                    if (buildingArea == null)
                    {
                        continue;
                    }

                    try
                    {
                        repaintMethod.Invoke(buildingArea, null);
                        repaintCount++;
                    }
                    catch (Exception repaintError)
                    {
                        DevLog("[WeddingBuilding] 重绘基地建筑区失败: " + repaintError.Message);
                    }
                }

                if (repaintCount > 0)
                {
                    DevLog("[WeddingBuilding] 已重绘基地建筑区 " + repaintCount + " 个, source=" + source);
                }
            }
            finally
            {
                baseBuildingAreaRepaintCoroutine = null;
            }
        }

        // ============================================================================
        // 事件监听 - 建筑放置/拆除时生成/移除NPC
        // ============================================================================

        /// <summary>
        /// 注册建筑系统事件
        /// </summary>
        private void RegisterWeddingBuildingEvents()
        {
            try
            {
                // 获取 BuildingManager 类型
                Type bmType = FindGameType("Duckov.Buildings.BuildingManager");
                if (bmType == null)
                {
                    ModBehaviour.LogError("[WeddingBuilding] 无法找到 BuildingManager 类型");
                    return;
                }

                // 订阅 OnBuildingBuilt 事件
                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                if (builtEvent != null)
                {
                    Action<int> builtHandler = OnWeddingBuildingBuilt;
                    builtEvent.AddEventHandler(null, builtHandler);
                    DevLog("[WeddingBuilding] 已订阅 OnBuildingBuilt 事件");
                }

                // 订阅 OnBuildingDestroyed 事件
                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);
                if (destroyedEvent != null)
                {
                    Action<int> destroyedHandler = OnWeddingBuildingDestroyed;
                    destroyedEvent.AddEventHandler(null, destroyedHandler);
                    DevLog("[WeddingBuilding] 已订阅 OnBuildingDestroyed 事件");
                }
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 注册事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 取消注册建筑系统事件
        /// </summary>
        private void UnregisterWeddingBuildingEvents()
        {
            try
            {
                Type bmType = FindGameType("Duckov.Buildings.BuildingManager");
                if (bmType == null) return;

                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                if (builtEvent != null)
                {
                    Action<int> builtHandler = OnWeddingBuildingBuilt;
                    builtEvent.RemoveEventHandler(null, builtHandler);
                }

                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);
                if (destroyedEvent != null)
                {
                    Action<int> destroyedHandler = OnWeddingBuildingDestroyed;
                    destroyedEvent.RemoveEventHandler(null, destroyedHandler);
                }

                DevLog("[WeddingBuilding] 已取消事件订阅");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 取消事件订阅失败: " + e.Message);
            }
        }

        /// <summary>
        /// 建筑被放置时的回调
        /// 检查是否是我们的婚礼建筑，如果是则生成NPC
        /// </summary>
        private void OnWeddingBuildingBuilt(int guid)
        {
            try
            {
                // 通过反射获取建筑数据
                MethodInfo getBuildingData = GetBuildingDataMethod();
                if (getBuildingData == null) return;

                object buildingData = getBuildingData.Invoke(null, new object[] { guid, null });
                if (buildingData == null) return;

                // 获取建筑ID
                PropertyInfo idProp = buildingData.GetType().GetProperty("ID");
                string buildingId = idProp?.GetValue(buildingData) as string;

                if (buildingId != WEDDING_BUILDING_ID) return;

                DevLog("[WeddingBuilding] 检测到婚礼教堂被放置，GUID=" + guid);
                SetWeddingBuildingPresence(true);
                ResetWeddingBuildingLocationCache();
                ObjectCache.InvalidateSceneObjectsByType(GetBuildingType());

                // 延迟生成NPC（等待建筑实例化完成）
                StartCoroutine(DelayedSpawnWeddingNPC(guid));

                // 延迟检查并修复放置后的建筑模型渲染
                StartCoroutine(DelayedFixBuildingRenderers(guid));
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] OnBuildingBuilt 处理失败: " + e.Message);
            }
        }

        /// <summary>
        /// 建筑被拆除时的回调
        /// 需要验证被拆除的是我们的婚礼建筑，避免误清理
        /// </summary>
        private void OnWeddingBuildingDestroyed(int guid)
        {
            try
            {
                // 场景中已经没有婚礼建筑了才清理NPC
                // （因为 OnBuildingDestroyed 事件不携带建筑ID，需要反向检查）
                if (RefreshWeddingBuildingPresence())
                {
                    return;
                }

                ResetWeddingBuildingLocationCache();
                ObjectCache.InvalidateSceneObjectsByType(GetBuildingType());
                DestroyWeddingPlaceholder();

                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                if (spouseNpcId == GoblinAffinityConfig.NPC_ID)
                {
                    DestroyGoblinNPC();
                }
                else if (spouseNpcId == NurseAffinityConfig.NPC_ID)
                {
                    DestroyNurseNPC();
                }

                DevLog("[WeddingBuilding] 婚礼教堂被拆除，已清理驻留NPC");
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] OnBuildingDestroyed 处理失败: " + e.Message);
            }
        }

        // ============================================================================
        // NPC 生成
        // ============================================================================

        /// <summary>
        /// 延迟生成婚礼NPC
        /// 等待建筑实例化完成后，在 NPCSpawnPoint 位置生成NPC
        /// </summary>
        private IEnumerator DelayedSpawnWeddingNPC(int buildingGuid)
        {
            // 等待建筑实例化完成
            yield return new WaitForSeconds(0.5f);

            try
            {
                // 查找场景中已实例化的婚礼建筑
                Vector3 npcPosition = FindWeddingBuildingNPCPosition();

                if (npcPosition == Vector3.zero)
                {
                    DevLog("[WeddingBuilding] ????????NPC???");
                    yield break;
                }

                if (TrySpawnMarriedNpcAtWeddingPoint() != null)
                {
                    yield break;
                }

                if (weddingNPCInstance != null)
                {
                    UnityEngine.Object.Destroy(weddingNPCInstance);
                    weddingNPCInstance = null;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 生成NPC失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟检查并修复放置后建筑的渲染器
        /// 解决 Instantiate 克隆后材质/Mesh 可能丢失的问题
        /// </summary>
        private IEnumerator DelayedFixBuildingRenderers(int buildingGuid)
        {
            yield return new WaitForSeconds(0.3f);

            try
            {
                // 查找场景中所有 Building 实例
                Type buildingType = FindGameType("Duckov.Buildings.Building");
                if (buildingType == null) yield break;

                FieldInfo idField = buildingType.GetField("id", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField == null) yield break;

                UnityEngine.Object[] allBuildings = ObjectCache.GetSceneObjectsByType(buildingType);
                if (allBuildings == null) yield break;

                foreach (Component b in allBuildings)
                {
                    string bid = idField.GetValue(b) as string;
                    if (bid != WEDDING_BUILDING_ID) continue;

                    // 找到婚礼建筑实例，检查渲染器
                    GameObject buildingGO = b.gameObject;
                    Renderer[] renderers = buildingGO.GetComponentsInChildren<Renderer>(true);

                    DevLog("[WeddingBuilding] 放置后建筑诊断: " + buildingGO.name
                        + " pos=" + buildingGO.transform.position
                        + " active=" + buildingGO.activeSelf
                        + " renderers=" + renderers.Length);

                    foreach (Renderer r in renderers)
                    {
                        bool hasMesh = true;
                        MeshFilter mf = r.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh == null) hasMesh = false;

                        DevLog("[WeddingBuilding]   Renderer: " + r.gameObject.name
                            + " | enabled=" + r.enabled
                            + " | active=" + r.gameObject.activeSelf
                            + " | hasMesh=" + hasMesh
                            + " | material=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "NULL")
                            + " | layer=" + r.gameObject.layer
                            + " | bounds=" + r.bounds);

                        // 如果材质丢失或仍是 Standard shader，强制修复
                        if (r.sharedMaterial == null ||
                            r.sharedMaterial.shader.name == "Standard" ||
                            r.sharedMaterial.shader.name.Contains("Standard") ||
                            r.sharedMaterial.shader.name == "Hidden/InternalErrorShader")
                        {
                            // 尝试获取游戏兼容的 shader
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
                                if (targetShader != null) break;
                            }

                            if (targetShader != null && r.sharedMaterial != null)
                            {
                                // 保存原始贴图
                                Texture mainTex = r.sharedMaterial.HasProperty("_MainTex")
                                    ? r.sharedMaterial.GetTexture("_MainTex") : null;
                                Color color = r.sharedMaterial.HasProperty("_Color")
                                    ? r.sharedMaterial.GetColor("_Color") : Color.white;

                                // 创建新材质
                                Material newMat = new Material(targetShader);

                                // 恢复贴图
                                if (mainTex != null)
                                {
                                    if (newMat.HasProperty("_BaseMap"))
                                        newMat.SetTexture("_BaseMap", mainTex);
                                    if (newMat.HasProperty("_MainTex"))
                                        newMat.SetTexture("_MainTex", mainTex);
                                }

                                // 恢复颜色
                                if (newMat.HasProperty("_BaseColor"))
                                    newMat.SetColor("_BaseColor", color);
                                if (newMat.HasProperty("_Color"))
                                    newMat.SetColor("_Color", color);

                                r.material = newMat;
                                DevLog("[WeddingBuilding]   → 已修复材质 Shader 为 " + targetShader.name);
                            }
                            else if (targetShader != null)
                            {
                                // 材质为 null，创建纯色材质
                                Material newMat = new Material(targetShader);
                                newMat.color = new Color(1f, 0.75f, 0.8f, 1f);
                                r.material = newMat;
                                DevLog("[WeddingBuilding]   → 材质为空，创建新材质");
                            }
                        }

                        // 确保渲染器启用且在正确层
                        r.enabled = true;
                        r.gameObject.layer = 0;
                    }

                    // 同时检查 Graphics 容器状态
                    Transform graphicsTr = buildingGO.transform.Find("Graphics");
                    if (graphicsTr != null)
                    {
                        DevLog("[WeddingBuilding]   Graphics: active=" + graphicsTr.gameObject.activeSelf
                            + " childCount=" + graphicsTr.childCount);
                        for (int i = 0; i < graphicsTr.childCount; i++)
                        {
                            Transform child = graphicsTr.GetChild(i);
                            DevLog("[WeddingBuilding]     child[" + i + "]: " + child.name
                                + " active=" + child.gameObject.activeSelf
                                + " pos=" + child.localPosition
                                + " scale=" + child.localScale);
                        }
                    }
                    else
                    {
                        DevLog("[WeddingBuilding]   警告：未找到 Graphics 子物体！");
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 修复渲染器失败: " + e.Message);
            }
        }

        /// <summary>
        /// 查找场景中婚礼建筑的NPC站位点世界坐标
        /// 遍历所有 Building 实例，找到我们的婚礼建筑
        /// </summary>
        private Vector3 FindWeddingBuildingNPCPosition()
        {
            try
            {
                Vector3 cachedPosition;
                if (TryUseCachedWeddingNpcPosition(out cachedPosition))
                {
                    return cachedPosition;
                }

                Type buildingType = GetBuildingType();
                PropertyInfo idProp = GetBuildingIdProperty();
                if (buildingType == null || idProp == null)
                {
                    return Vector3.zero;
                }

                if (!cachedWeddingBuildingPresent)
                {
                    return Vector3.zero;
                }

                var allBuildings = ObjectCache.GetSceneObjectsByType(buildingType);
                foreach (Component building in allBuildings)
                {
                    string id = idProp?.GetValue(building) as string;

                    if (id == WEDDING_BUILDING_ID)
                    {
                        SetWeddingBuildingPresence(true);
                        cachedWeddingBuildingTransform = building.transform;
                        EnsureWeddingBuildingFunctionPoints(building.gameObject);

                        Transform spawnPoint = building.transform.Find("Function/NPCSpawnPoint");
                        if (spawnPoint != null)
                        {
                            cachedWeddingNpcSpawnPoint = spawnPoint;
                            return spawnPoint.position;
                        }

                        Vector3 fallbackPos = building.transform.TransformPoint(WEDDING_NPC_OFFSET);
                        DevLog("[WeddingBuilding] 使用建筑中心位置作为NPC站位: " + fallbackPos);
                        return fallbackPos;
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 查找NPC站位失败: " + e.Message);
            }

            ResetWeddingBuildingLocationCache();
            return Vector3.zero;
        }

        private void EnsureWeddingBuildingFunctionPoints(GameObject buildingGO)
        {
            if (buildingGO == null)
            {
                return;
            }

            try
            {
                Transform functionContainer = buildingGO.transform.Find("Function");
                if (functionContainer == null)
                {
                    GameObject functionObject = new GameObject("Function");
                    functionContainer = functionObject.transform;
                    functionContainer.SetParent(buildingGO.transform, false);
                    DevLog("[WeddingBuilding] 运行时补建 Function 容器: " + buildingGO.name);
                }

                Transform npcSpawnPoint = functionContainer.Find("NPCSpawnPoint");
                if (npcSpawnPoint == null)
                {
                    GameObject npcPointObject = new GameObject("NPCSpawnPoint");
                    npcSpawnPoint = npcPointObject.transform;
                    npcSpawnPoint.SetParent(functionContainer, false);
                    DevLog("[WeddingBuilding] 运行时补建 NPCSpawnPoint");
                }

                npcSpawnPoint.localPosition = WEDDING_NPC_OFFSET;
                npcSpawnPoint.localRotation = Quaternion.identity;

                Transform interactPoint = functionContainer.Find("WeddingChapelInteractPoint");
                if (interactPoint == null)
                {
                    GameObject interactPointObject = new GameObject("WeddingChapelInteractPoint");
                    interactPoint = interactPointObject.transform;
                    interactPoint.SetParent(functionContainer, false);
                    DevLog("[WeddingBuilding] 运行时补建 WeddingChapelInteractPoint");
                }

                interactPoint.localPosition = WEDDING_INTERACT_OFFSET;
                interactPoint.localRotation = Quaternion.identity;

                WeddingChapelInteractable interactable = interactPoint.GetComponent<WeddingChapelInteractable>();
                if (interactable == null)
                {
                    bool restoreActive = interactPoint.gameObject.activeSelf;
                    if (restoreActive)
                    {
                        interactPoint.gameObject.SetActive(false);
                    }

                    if (interactPoint.GetComponent<Collider>() == null)
                    {
                        SphereCollider sphere = interactPoint.gameObject.AddComponent<SphereCollider>();
                        sphere.radius = 0.75f;
                        sphere.center = Vector3.zero;
                        sphere.isTrigger = false;
                    }

                    interactable = interactPoint.gameObject.AddComponent<WeddingChapelInteractable>();

                    if (restoreActive)
                    {
                        interactPoint.gameObject.SetActive(true);
                    }

                    DevLog("[WeddingBuilding] 已附加 WeddingChapelInteractable");
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBuilding] 修复 Function 点位失败: " + e.Message);
            }
        }

        /// <summary>
        /// 在指定位置生成婚礼 NPC 占位体
        /// </summary>
        private void SpawnWeddingNPCAtPosition(Vector3 position)
        {
            // 清理旧实例
            if (weddingNPCInstance != null)
            {
                UnityEngine.Object.Destroy(weddingNPCInstance);
                weddingNPCInstance = null;
            }

            // 目前用一个临时占位胶囊体标记 NPC 位置
            weddingNPCInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            weddingNPCInstance.name = "WeddingNPC_Placeholder";
            weddingNPCInstance.transform.position = position;
            weddingNPCInstance.transform.localScale = new Vector3(0.5f, 1f, 0.5f);

            // 设置占位颜色（红色，容易辨认）
            Renderer renderer = weddingNPCInstance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("Standard"));
                renderer.material.color = Color.red;
            }

            // 移除碰撞体，避免影响玩家移动
            Collider col = weddingNPCInstance.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);

            WeddingNpcResidentMarker marker = weddingNPCInstance.GetComponent<WeddingNpcResidentMarker>();
            if (marker == null)
            {
                marker = weddingNPCInstance.AddComponent<WeddingNpcResidentMarker>();
            }
            marker.NpcId = string.Empty;

            DevLog("[WeddingBuilding] 婚礼NPC占位已生成在: " + position);
        }

        // ============================================================================
        // 场景加载时恢复已放置的婚礼建筑NPC
        // ============================================================================

        /// <summary>
        /// 在基地场景加载完成后，检查是否已有婚礼建筑被放置
        /// 如果有，恢复NPC生成（因为存档中保存了建筑数据）
        /// </summary>
        public void RestoreWeddingBuildingNPC()
        {
            try
            {
                bool hasWeddingBuilding = RefreshWeddingBuildingPresence();
                if (hasWeddingBuilding)
                {
                    DevLog("[WeddingBuilding] 检测到已放置的婚礼教堂，恢复NPC");
                    StartCoroutine(DelayedRestoreWeddingNPC());
                }
                else
                {
                    DestroyWeddingPlaceholder();
                }
            }
            catch (Exception e)
            {
                ModBehaviour.LogError("[WeddingBuilding] 恢复NPC失败: " + e.Message);
            }
        }

        /// <summary>
        /// 延迟恢复NPC（等待建筑显示完成）
        /// </summary>
        private IEnumerator DelayedRestoreWeddingNPC()
        {
            yield return new WaitForSeconds(1f);

            Vector3 npcPos = FindWeddingBuildingNPCPosition();
            if (npcPos == Vector3.zero)
            {
                yield break;
            }

            if (TrySpawnMarriedNpcAtWeddingPoint() != null)
            {
                yield break;
            }

            if (weddingNPCInstance != null)
            {
                UnityEngine.Object.Destroy(weddingNPCInstance);
                weddingNPCInstance = null;
            }
        }
    }
}
