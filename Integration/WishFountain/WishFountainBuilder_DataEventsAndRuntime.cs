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
    public partial class ModBehaviour
    {
        /// <summary>
        /// 添加 Building 组件（通过反射）
        /// </summary>
        private void AddStarwishBuildingComponent(GameObject go)
        {
            Type buildingType = FindGameType("Duckov.Buildings.Building");
            if (buildingType == null)
            {
                ModBehaviour.LogError("[WishFountain] 无法找到 Building 类型");
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
                ModBehaviour.LogError("[WishFountain] 无法找到 BuildingDataCollection 类型");
                return;
            }

            PropertyInfo instanceProp = bdcType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            object bdcInstance = instanceProp != null ? instanceProp.GetValue(null) : null;

            if (bdcInstance == null)
            {
                ModBehaviour.LogError("[WishFountain] BuildingDataCollection.Instance 为 null");
                return;
            }

            // 注入 BuildingInfo
            FieldInfo infosField = bdcType.GetField("infos", BindingFlags.NonPublic | BindingFlags.Instance);
            if (infosField == null)
            {
                ModBehaviour.LogError("[WishFountain] 无法获取 infos 字段");
                return;
            }

            object infosList = infosField.GetValue(bdcInstance);
            if (infosList == null)
            {
                ModBehaviour.LogError("[WishFountain] infos 列表为 null");
                return;
            }

            Type buildingInfoType = FindGameType("Duckov.Buildings.BuildingInfo");
            if (buildingInfoType == null)
            {
                ModBehaviour.LogError("[WishFountain] 无法找到 BuildingInfo 类型");
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
                if (!IsStarwishBuildingGuid(buildingInstanceId))
                {
                    return;
                }

                RequestRestoreWishFountainBuildings("OnBuildingBuilt");
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] OnBuildingBuilt 异常: " + e.Message);
            }
        }

        public void RestoreWishFountainBuildings()
        {
            RequestRestoreWishFountainBuildings("SceneInit");
        }

        private void RequestRestoreWishFountainBuildings(string source)
        {
            if (starwishRestoreCoroutine != null)
            {
                return;
            }

            starwishRestoreCoroutine = StartCoroutine(RestoreWishFountainBuildingsDelayed(source));
        }

        private void ResetStarwishPreparedBuildingCache()
        {
            preparedStarwishBuildingInstanceIds.Clear();
            preparedStarwishSceneHandle = int.MinValue;
        }

        private void RefreshStarwishPreparedBuildingCacheForActiveScene()
        {
            int currentSceneHandle = int.MinValue;
            try
            {
                currentSceneHandle = SceneManager.GetActiveScene().handle;
            }
            catch
            {
            }

            if (currentSceneHandle != preparedStarwishSceneHandle)
            {
                preparedStarwishBuildingInstanceIds.Clear();
                preparedStarwishSceneHandle = currentSceneHandle;
            }
        }

        private bool IsStarwishBuildingGuid(int buildingGuid)
        {
            try
            {
                MethodInfo getBuildingData = GetBuildingDataMethod();
                if (getBuildingData == null)
                {
                    return false;
                }

                object buildingData = getBuildingData.Invoke(null, new object[] { buildingGuid, null });
                if (buildingData == null)
                {
                    return false;
                }

                PropertyInfo idProp = buildingData.GetType().GetProperty("ID");
                string buildingId = idProp != null ? idProp.GetValue(buildingData, null) as string : null;
                return string.Equals(buildingId, STARWISH_BUILDING_ID, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private bool HasPendingStarwishBuildingsInManager()
        {
            try
            {
                MethodInfo anyMethod = GetBuildingManagerAnyMethod();
                return anyMethod != null
                    && anyMethod.Invoke(null, new object[] { STARWISH_BUILDING_ID, false }) is bool result
                    && result;
            }
            catch
            {
                return true;
            }
        }

        private bool IsStarwishBuildingComponent(Component buildingComp)
        {
            if (buildingComp == null)
            {
                return false;
            }

            GameObject buildingGO = buildingComp.gameObject;
            if (buildingGO == null || object.ReferenceEquals(buildingGO, starwishBuildingPrefabGO))
            {
                return false;
            }

            try
            {
                PropertyInfo idProperty = GetBuildingIdProperty();
                if (idProperty != null)
                {
                    string buildingId = idProperty.GetValue(buildingComp, null) as string;
                    if (string.Equals(buildingId, STARWISH_BUILDING_ID, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            try
            {
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                FieldInfo idField = buildingComp.GetType().GetField("id", flags);
                if (idField != null)
                {
                    string buildingId = idField.GetValue(buildingComp) as string;
                    if (string.Equals(buildingId, STARWISH_BUILDING_ID, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return IsStarwishBuildingObject(buildingGO);
        }

        private bool NeedsStarwishFunctionPointRepair(GameObject buildingGO)
        {
            if (buildingGO == null)
            {
                return false;
            }

            Transform interactTr = buildingGO.transform.Find("Function/WishInteractPoint");
            if (interactTr == null)
            {
                return true;
            }

            if (interactTr.GetComponent<BoxCollider>() == null)
            {
                return true;
            }

            return interactTr.GetComponent<WishFountainInteractable>() == null;
        }

        private IEnumerator RestoreWishFountainBuildingsDelayed(string source)
        {
            // 等待一帧确保建筑完全初始化
            yield return null;
            yield return null;

            try
            {
                RefreshStarwishPreparedBuildingCacheForActiveScene();

                if (!HasPendingStarwishBuildingsInManager())
                {
                    yield break;
                }

                Type buildingType = GetBuildingType();
                if (buildingType == null)
                {
                    DevLog("[WishFountain] 未找到 Building 类型，跳过恢复");
                    yield break;
                }

                int restoredCount = 0;
                UnityEngine.Object[] allBuildings = UnityEngine.Object.FindObjectsOfType(buildingType);
                for (int i = 0; i < allBuildings.Length; i++)
                {
                    Component buildingComponent = allBuildings[i] as Component;
                    if (!IsStarwishBuildingComponent(buildingComponent))
                    {
                        continue;
                    }

                    GameObject buildingGO = buildingComponent.gameObject;
                    if (buildingGO == null)
                    {
                        continue;
                    }

                    int instanceId = buildingGO.GetInstanceID();
                    if (preparedStarwishBuildingInstanceIds.Contains(instanceId)
                        && !NeedsStarwishFunctionPointRepair(buildingGO))
                    {
                        continue;
                    }

                    EnsureStarwishFunctionPoints(buildingGO);
                    preparedStarwishBuildingInstanceIds.Add(instanceId);
                    restoredCount++;
                }

                DevLog("[WishFountain] 已恢复/检查场景中的布满了灰尘的星愿许愿台建筑数: "
                    + restoredCount + ", source=" + source);
            }
            catch (Exception e)
            {
                DevLog("[WishFountain] 设置交互组件异常: " + e.Message);
            }
            finally
            {
                starwishRestoreCoroutine = null;
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

            if (object.ReferenceEquals(obj, starwishBuildingPrefabGO))
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
            preparedStarwishBuildingInstanceIds.Clear();

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
