// 基地内容建筑的实例恢复流程。每个建筑注入器持有自己的实例与取消权。
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed class BuildingRestoreCore
    {
        private readonly ModBehaviour owner;
        private readonly Func<bool> hasPendingBuildings;
        private readonly Func<Component, bool> isTargetBuilding;
        private readonly Func<GameObject, bool> needsFunctionPointRepair;
        private readonly Action<GameObject> ensureFunctionPoints;
        private readonly HashSet<int> preparedBuildingInstanceIds = new HashSet<int>();
        private Coroutine restoreCoroutine;
        private int preparedSceneHandle = int.MinValue;
        private int requestGeneration;

        internal BuildingRestoreCore(
            ModBehaviour owner,
            Func<bool> hasPendingBuildings,
            Func<Component, bool> isTargetBuilding,
            Func<GameObject, bool> needsFunctionPointRepair,
            Action<GameObject> ensureFunctionPoints)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.hasPendingBuildings = hasPendingBuildings ?? throw new ArgumentNullException(nameof(hasPendingBuildings));
            this.isTargetBuilding = isTargetBuilding ?? throw new ArgumentNullException(nameof(isTargetBuilding));
            this.needsFunctionPointRepair = needsFunctionPointRepair ?? throw new ArgumentNullException(nameof(needsFunctionPointRepair));
            this.ensureFunctionPoints = ensureFunctionPoints ?? throw new ArgumentNullException(nameof(ensureFunctionPoints));
        }

        internal void Request(string source)
        {
            if (restoreCoroutine != null) return;
            int generation = ++requestGeneration;
            restoreCoroutine = owner.StartCoroutine(RestoreDelayed(generation));
        }

        internal void Cancel()
        {
            ++requestGeneration;
            Coroutine active = restoreCoroutine;
            restoreCoroutine = null;
            if (active != null) owner.StopCoroutine(active);
        }

        internal void ClearPrepared()
        {
            preparedBuildingInstanceIds.Clear();
        }

        internal void ResetPreparedCache()
        {
            preparedBuildingInstanceIds.Clear();
            try { ObjectCache.InvalidateSceneObjectsByType(BuildingInjectionHelper.GetBuildingType()); }
            catch (Exception)
            {
                // 缓存失效失败不阻断 owner 清理。
            }
            preparedSceneHandle = int.MinValue;
        }

        private IEnumerator RestoreDelayed(int generation)
        {
            // 官方建筑的 Awake/Start 先跑完；随后读取当时的活动场景。
            yield return null;
            yield return null;

            try
            {
                if (generation != requestGeneration) yield break;
                int activeSceneHandle = SceneManager.GetActiveScene().handle;
                if (activeSceneHandle != preparedSceneHandle)
                {
                    preparedBuildingInstanceIds.Clear();
                    preparedSceneHandle = activeSceneHandle;
                }
                if (!hasPendingBuildings()) yield break;

                Type buildingType = BuildingInjectionHelper.GetBuildingType();
                if (buildingType == null) yield break;
                UnityEngine.Object[] allBuildings = ObjectCache.GetSceneObjectsByType(buildingType);
                if (allBuildings == null) yield break;

                for (int i = 0; i < allBuildings.Length; i++)
                {
                    if (generation != requestGeneration) yield break;
                    Component comp = allBuildings[i] as Component;
                    if (comp == null || !isTargetBuilding(comp)) continue;

                    GameObject buildingGO = comp.gameObject;
                    int instanceId = buildingGO.GetInstanceID();
                    if (preparedBuildingInstanceIds.Contains(instanceId)
                        && !needsFunctionPointRepair(buildingGO)) continue;

                    ensureFunctionPoints(buildingGO);
                    if (generation != requestGeneration) yield break;
                    preparedBuildingInstanceIds.Add(instanceId);
                }
            }
            finally
            {
                // StopCoroutine 可能在新请求已启动后才释放旧迭代器。
                if (generation == requestGeneration) restoreCoroutine = null;
            }
        }
    }
}
