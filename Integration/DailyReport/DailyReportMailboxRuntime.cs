// ============================================================================
// DailyReportMailboxRuntime.cs - 报箱建筑的数据注入、事件与场景恢复
// ============================================================================
// 与 DailyReportMailboxBuilder.cs 同属模块类型 DailyReportMailboxBuilder。
// 形态照 PetNest/PetNestBuilder_DataEventsAndRuntime.cs。
//
// 共享反射工具（FindGameType / GetBuildingType / GetBuildingDataMethod /
// GetBuildingManagerType / GetBuildingManagerAnyMethod / GetBuildingIdProperty /
// AssignBuildingContainerField）来自 BuildingInjectionHelper；重绘与协程经显式 _owner。
// ============================================================================

using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DailyReportMailboxBuilder
    {
        // ====================================================================
        // Building 组件（反射挂官方类型 + 填私有字段）
        // ====================================================================

        private void AddDailyReportBuildingComponent(GameObject go)
        {
            Type buildingType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                ModBehaviour.LogError(DailyReportTuning.LogPrefix + "无法找到 Building 类型");
                return;
            }

            Component buildingComp = go.AddComponent(buildingType);
            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            FieldInfo idField = buildingType.GetField("id", privateFlags);
            if (idField != null) idField.SetValue(buildingComp, DAILYREPORT_BUILDING_ID);

            FieldInfo dimField = buildingType.GetField("dimensions", privateFlags);
            if (dimField != null) dimField.SetValue(buildingComp, DAILYREPORT_BUILDING_SIZE);

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

            ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "Building 组件已添加，ID=" + DAILYREPORT_BUILDING_ID);
        }

        // ====================================================================
        // 数据注入（BuildingDataCollection）
        // ====================================================================

        private bool InjectDailyReportBuildingData()
        {
            try
            {
                Type bdcType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingDataCollection");
                Type infoType = BuildingInjectionHelper.FindGameType("Duckov.Buildings.BuildingInfo");
                Type buildingType = BuildingInjectionHelper.GetBuildingType();
                if (bdcType == null || infoType == null || buildingType == null)
                    throw new InvalidOperationException("Building types unavailable");

                PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object instance = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (instance == null) throw new InvalidOperationException("BuildingDataCollection.Instance unavailable");
                FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
                FieldInfo prefabsField = bdcType.GetField("prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
                IList infos = infosField != null ? infosField.GetValue(instance) as IList : null;
                IList prefabs = prefabsField != null ? prefabsField.GetValue(instance) as IList : null;
                Component prefab = dailyReportBuildingPrefabGO != null
                    ? dailyReportBuildingPrefabGO.GetComponent(buildingType) : null;
                FieldInfo idField = infoType.GetField("id");
                if (infos == null || prefabs == null || prefab == null || idField == null)
                    throw new InvalidOperationException("Building lists, prefab or id binding unavailable");

                PropertyInfo prefabId = BuildingInjectionHelper.GetBuildingIdProperty();
                if (prefabId == null || !string.Equals(prefabId.GetValue(prefab, null) as string,
                    DAILYREPORT_BUILDING_ID, StringComparison.Ordinal))
                    throw new InvalidOperationException("Mailbox prefab ID binding unavailable");

                // 元数据和 prefab 是两份表。上次只写入其中一份时，重试必须补齐另一份。
                bool hasInfo = false;
                for (int i = 0; i < infos.Count; i++)
                {
                    if (string.Equals(idField.GetValue(infos[i]) as string, DAILYREPORT_BUILDING_ID, StringComparison.Ordinal))
                    { hasInfo = true; break; }
                }
                bool hasPrefab = false;
                for (int i = 0; i < prefabs.Count; i++)
                {
                    Component existing = prefabs[i] as Component;
                    if (existing != null && existing.name == DAILYREPORT_PREFAB_NAME)
                    { hasPrefab = true; break; }
                }

                object newInfo = null;
                if (!hasInfo)
                {
                    newInfo = Activator.CreateInstance(infoType);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "id", DAILYREPORT_BUILDING_ID);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "prefabName", DAILYREPORT_PREFAB_NAME);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "maxAmount", DAILYREPORT_BUILDING_MAX_AMOUNT);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "requireBuildings", new string[0]);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "alternativeFor", new string[0]);
                    SetDailyReportBuildingInfoField(infoType, newInfo, "requireQuests", new int[0]);
                    if (dailyReportBuildingIcon != null)
                        SetDailyReportBuildingInfoField(infoType, newInfo, "iconReference", dailyReportBuildingIcon);
                    SetDailyReportBuildingCost(infoType, ref newInfo);
                }
                // prefab 先就位，避免建造菜单短暂暴露无法实例化的条目。
                if (!hasPrefab) prefabs.Add(prefab);
                if (!hasInfo) infos.Add(newInfo);
                FieldInfo readonlyField = bdcType.GetField("readonlyInfos", BindingFlags.Public | BindingFlags.Instance);
                if (readonlyField != null) readonlyField.SetValue(instance, null);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.LogError(DailyReportTuning.LogPrefix + "报箱注册未完成，保留重试：" + e.Message);
                return false;
            }
        }

        private static void SetDailyReportBuildingInfoField(
            Type buildingInfoType, object target, string fieldName, object value)
        {
            FieldInfo field = buildingInfoType.GetField(fieldName);
            if (field == null) throw new MissingFieldException(buildingInfoType.FullName, fieldName);
            field.SetValue(target, value);
        }

        /// <summary>官方 Cost 是 struct，必须整体 boxing 后写回。</summary>
        private void SetDailyReportBuildingCost(Type buildingInfoType, ref object buildingInfo)
        {
            try
            {
                Type costType = BuildingInjectionHelper.FindGameType("Duckov.Economy.Cost");
                if (costType == null) throw new InvalidOperationException("Cost type unavailable");

                object cost;
                ConstructorInfo costCtor = costType.GetConstructor(new Type[] { typeof(long) });
                if (costCtor != null)
                {
                    cost = costCtor.Invoke(new object[] { DAILYREPORT_BUILDING_COST });
                }
                else
                {
                    cost = Activator.CreateInstance(costType);
                    FieldInfo moneyField = costType.GetField("money");
                    if (moneyField == null) throw new MissingFieldException(costType.FullName, "money");
                    moneyField.SetValue(cost, DAILYREPORT_BUILDING_COST);
                    FieldInfo itemsField = costType.GetField("items");
                    if (itemsField != null)
                    {
                        Type entryType = costType.GetNestedType("ItemEntry") ?? typeof(object);
                        itemsField.SetValue(cost, Array.CreateInstance(entryType, 0));
                    }
                }

                FieldInfo costField = buildingInfoType.GetField("cost");
                if (costField == null) throw new MissingFieldException(buildingInfoType.FullName, "cost");
                costField.SetValue(buildingInfo, cost);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "建筑费用设置失败: " + e.Message);
                throw; // 造价未成功绑定时不能把免费/损坏的条目加入建造菜单。
            }
        }

        // ====================================================================
        // 建筑事件（官方静态事件，反射订阅 / 退订）
        // ====================================================================

        private bool dailyReportBuildingEventsRegistered;
        private Action<int> dailyReportBuiltHandler;
        private Action<int> dailyReportDestroyedHandler;

        private void RegisterDailyReportBuildingEvents()
        {
            if (dailyReportBuildingEventsRegistered) return;
            try
            {
                Type bmType = BuildingInjectionHelper.GetBuildingManagerType();
                if (bmType == null) return;

                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);

                dailyReportBuiltHandler = OnDailyReportBuildingBuilt;
                dailyReportDestroyedHandler = OnDailyReportBuildingDestroyed;

                if (builtEvent != null) builtEvent.AddEventHandler(null, dailyReportBuiltHandler);
                if (destroyedEvent != null) destroyedEvent.AddEventHandler(null, dailyReportDestroyedHandler);

                dailyReportBuildingEventsRegistered = true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "建筑事件订阅失败: " + e.Message);
            }
        }

        private void UnregisterDailyReportBuildingEvents()
        {
            if (!dailyReportBuildingEventsRegistered) return;
            dailyReportBuildingEventsRegistered = false;
            try
            {
                Type bmType = BuildingInjectionHelper.GetBuildingManagerType();
                if (bmType == null) return;

                EventInfo builtEvent = bmType.GetEvent("OnBuildingBuilt", BindingFlags.Public | BindingFlags.Static);
                EventInfo destroyedEvent = bmType.GetEvent("OnBuildingDestroyed", BindingFlags.Public | BindingFlags.Static);

                if (builtEvent != null && dailyReportBuiltHandler != null)
                {
                    builtEvent.RemoveEventHandler(null, dailyReportBuiltHandler);
                }
                if (destroyedEvent != null && dailyReportDestroyedHandler != null)
                {
                    destroyedEvent.RemoveEventHandler(null, dailyReportDestroyedHandler);
                }
            }
            catch (Exception)
            {
                // 退订失败也要把标记清掉，避免重复订阅越滚越多
            }
            finally
            {
                dailyReportBuiltHandler = null;
                dailyReportDestroyedHandler = null;
            }
        }

        private void OnDailyReportBuildingBuilt(int buildingInstanceId)
        {
            try
            {
                if (!IsDailyReportBuildingGuid(buildingInstanceId)) return;
                ObjectCache.InvalidateSceneObjectsByType(BuildingInjectionHelper.GetBuildingType());
                RequestRestoreDailyReportBuildings("OnBuildingBuilt");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "建筑放置回调失败: " + e.Message);
            }
        }

        private void OnDailyReportBuildingDestroyed(int buildingInstanceId)
        {
            // 子物体由 Unity 随建筑一起销毁，这里只需要让缓存失效
            preparedDailyReportBuildingInstanceIds.Clear();
            ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "建筑被拆除，交互点缓存已清空");
        }

        // ====================================================================
        // 场景恢复（给已存在的建筑实例补交互点）
        // ====================================================================

        /// <summary>基地场景装配管线调用的公开入口。</summary>
        public void RestoreDailyReportMailboxes()
        {
            RequestRestoreDailyReportBuildings("SceneInit");
        }

        private void RequestRestoreDailyReportBuildings(string source)
        {
            // 已有协程在跑时天然去重
            if (dailyReportRestoreCoroutine != null) return;
            try
            {
                dailyReportRestoreCoroutine = _owner.StartCoroutine(RestoreDailyReportBuildingsDelayed(source));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "建筑恢复协程启动失败: " + e.Message);
            }
        }

        private IEnumerator RestoreDailyReportBuildingsDelayed(string source)
        {
            // 等两帧：建筑实例的 Awake/Start 要先跑完
            yield return null;
            yield return null;

            try
            {
                RefreshDailyReportPreparedBuildingCacheForActiveScene();
                if (!HasPendingDailyReportBuildingsInManager()) yield break;

                Type buildingType = BuildingInjectionHelper.GetBuildingType();
                if (buildingType == null) yield break;

                UnityEngine.Object[] allBuildings = ObjectCache.GetSceneObjectsByType(buildingType);
                if (allBuildings == null) yield break;

                for (int i = 0; i < allBuildings.Length; i++)
                {
                    Component comp = allBuildings[i] as Component;
                    if (comp == null) continue;
                    if (!IsDailyReportBuildingComponent(comp)) continue;

                    GameObject buildingGO = comp.gameObject;
                    int instanceId = buildingGO.GetInstanceID();
                    if (preparedDailyReportBuildingInstanceIds.Contains(instanceId)
                        && !NeedsDailyReportFunctionPointRepair(buildingGO))
                    {
                        continue;
                    }

                    EnsureDailyReportFunctionPoints(buildingGO);
                    preparedDailyReportBuildingInstanceIds.Add(instanceId);
                }
            }
            finally
            {
                dailyReportRestoreCoroutine = null;
            }
        }

        private void RefreshDailyReportPreparedBuildingCacheForActiveScene()
        {
            int handle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            if (handle == preparedDailyReportSceneHandle) return;
            preparedDailyReportBuildingInstanceIds.Clear();
            preparedDailyReportSceneHandle = handle;
        }

        private void ResetDailyReportPreparedBuildingCache()
        {
            preparedDailyReportBuildingInstanceIds.Clear();
            try { ObjectCache.InvalidateSceneObjectsByType(BuildingInjectionHelper.GetBuildingType()); }
            catch (Exception)
            {
                // 缓存失效失败不阻断清理
            }
            preparedDailyReportSceneHandle = int.MinValue;
        }

        /// <summary>管理器里是否有本建筑的实例。异常时 fail-open=true（宁可多扫一遍）。</summary>
        private bool HasPendingDailyReportBuildingsInManager()
        {
            try
            {
                MethodInfo anyMethod = BuildingInjectionHelper.GetBuildingManagerAnyMethod();
                if (anyMethod == null) return true;
                return anyMethod.Invoke(null, new object[] { DAILYREPORT_BUILDING_ID, false }) is bool result && result;
            }
            catch (Exception)
            {
                return true;
            }
        }

        // ====================================================================
        // 身份判定
        // ====================================================================

        private bool IsDailyReportBuildingGuid(int guid)
        {
            try
            {
                MethodInfo getData = BuildingInjectionHelper.GetBuildingDataMethod();
                if (getData == null) return false;
                object buildingData = getData.Invoke(null, new object[] { guid, null });
                if (buildingData == null) return false;
                PropertyInfo idProp = buildingData.GetType().GetProperty("ID");
                string id = idProp != null ? idProp.GetValue(buildingData, null) as string : null;
                return string.Equals(id, DAILYREPORT_BUILDING_ID, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool IsDailyReportBuildingComponent(Component buildingComp)
        {
            if (buildingComp == null) return false;
            GameObject buildingGO = buildingComp.gameObject;

            // 必须排除自己那份 DontDestroyOnLoad 的 prefab
            if (object.ReferenceEquals(buildingGO, dailyReportBuildingPrefabGO)) return false;

            try
            {
                PropertyInfo idProp = BuildingInjectionHelper.GetBuildingIdProperty();
                if (idProp != null)
                {
                    string id = idProp.GetValue(buildingComp, null) as string;
                    if (!string.IsNullOrEmpty(id))
                    {
                        return string.Equals(id, DAILYREPORT_BUILDING_ID, StringComparison.Ordinal);
                    }
                }
            }
            catch (Exception)
            {
                // 属性读取失败时退到字段与名字判定
            }

            try
            {
                FieldInfo idField = buildingComp.GetType().GetField(
                    "id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null)
                {
                    string id = idField.GetValue(buildingComp) as string;
                    if (!string.IsNullOrEmpty(id))
                    {
                        return string.Equals(id, DAILYREPORT_BUILDING_ID, StringComparison.Ordinal);
                    }
                }
            }
            catch (Exception)
            {
                // 同上
            }

            return buildingGO.name.IndexOf(DAILYREPORT_PREFAB_NAME, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ====================================================================
        // 交互点
        // ====================================================================

        private bool NeedsDailyReportFunctionPointRepair(GameObject buildingGO)
        {
            if (buildingGO == null) return false;
            try
            {
                Transform interactTr = buildingGO.transform.Find("Function/DailyReportInteractPoint");
                if (interactTr == null) return true;
                if (interactTr.GetComponent<BoxCollider>() == null) return true;
                if (interactTr.GetComponent<DailyReportInteractable>() == null) return true;
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 给建筑实例挂交互点。**时序关键**：挂组件时先 SetActive(false)，
        /// 全部字段填好之后再 SetActive(true)，让 InteractableBase.Awake 看到完整状态。
        /// </summary>
        private void EnsureDailyReportFunctionPoints(GameObject buildingGO)
        {
            if (buildingGO == null) return;
            try
            {
                Transform functionTr = buildingGO.transform.Find("Function");
                if (functionTr == null)
                {
                    GameObject functionGO = new GameObject("Function");
                    functionGO.transform.SetParent(buildingGO.transform, false);
                    functionTr = functionGO.transform;
                }

                Transform interactTr = functionTr.Find("DailyReportInteractPoint");
                if (interactTr == null)
                {
                    GameObject interactGO = new GameObject("DailyReportInteractPoint");
                    interactGO.transform.SetParent(functionTr, false);
                    interactTr = interactGO.transform;
                }
                interactTr.localPosition = DAILYREPORT_INTERACT_OFFSET;
                interactTr.localRotation = Quaternion.identity;

                bool restoreActive = interactTr.gameObject.activeSelf;
                if (restoreActive) interactTr.gameObject.SetActive(false);

                BoxCollider collider = interactTr.GetComponent<BoxCollider>();
                if (collider == null) collider = interactTr.gameObject.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.center = Vector3.zero;
                collider.size = new Vector3(1.6f, 2.2f, 1.6f);

                DailyReportInteractable interactable = interactTr.GetComponent<DailyReportInteractable>();
                if (interactable == null)
                {
                    interactable = interactTr.gameObject.AddComponent<DailyReportInteractable>();
                }
                interactable.interactCollider = collider;
                interactable.interactMarkerOffset = new Vector3(0f, 1.2f, 0f);

                if (restoreActive) interactTr.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(DailyReportTuning.LogPrefix + "交互点装配失败: " + e.Message);
            }
        }
    }
}
