using System;
using System.Reflection;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// 婚礼系统对 ModBehaviour 的桥接方法。
    /// 通过 partial 拆分，避免继续扩大主文件修改面。
    /// </summary>
    public partial class ModBehaviour
    {
        private const float WeddingNpcDistanceThreshold = 3f;

        public bool HasWeddingBuildingPlaced()
        {
            try
            {
                Type buildingManagerType = FindGameType("Duckov.Buildings.BuildingManager");
                if (buildingManagerType == null)
                {
                    return false;
                }

                MethodInfo anyMethod = buildingManagerType.GetMethod(
                    "Any",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), typeof(bool) },
                    null);

                if (anyMethod == null)
                {
                    return false;
                }

                object result = anyMethod.Invoke(null, new object[] { "wedding_chapel", false });
                return result is bool && (bool)result;
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 检查婚礼建筑失败: " + e.Message);
                return false;
            }
        }

        public Transform GetWeddingNpcTransform()
        {
            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            GameObject spouseInstance = GetSpouseInstance(spouseNpcId);
            if (spouseInstance != null)
            {
                return spouseInstance.transform;
            }

            return weddingNPCInstance != null ? weddingNPCInstance.transform : null;
        }

        public Transform TrySpawnMarriedNpcAtWeddingPoint()
        {
            try
            {
                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseNpcId) || !HasWeddingBuildingPlaced())
                {
                    return null;
                }

                Vector3 weddingPosition = FindWeddingBuildingNPCPosition();
                if (weddingPosition == Vector3.zero)
                {
                    return null;
                }

                GameObject spouseInstance = GetSpouseInstance(spouseNpcId);
                if (spouseInstance == null)
                {
                    if (spouseNpcId == GoblinAffinityConfig.NPC_ID)
                    {
                        SpawnGoblinNPC(weddingPosition, true, true);
                    }
                    else if (spouseNpcId == NurseAffinityConfig.NPC_ID)
                    {
                        SpawnNurseNPC(weddingPosition, true, true);
                    }

                    spouseInstance = GetSpouseInstance(spouseNpcId);
                }
                else
                {
                    spouseInstance.transform.position = weddingPosition;
                    SetWeddingNpcIdle(spouseInstance);
                }

                if (spouseInstance != null)
                {
                    DestroyWeddingPlaceholder();
                    return spouseInstance.transform;
                }

                SpawnWeddingNPCAtPosition(weddingPosition);
                return weddingNPCInstance != null ? weddingNPCInstance.transform : null;
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 生成已婚NPC失败: " + e.Message);
                return null;
            }
        }

        public bool IsWeddingNpcInstance(Transform npcTransform)
        {
            try
            {
                if (npcTransform == null || !HasWeddingBuildingPlaced())
                {
                    return false;
                }

                if (IsSameNpcObject(npcTransform, weddingNPCInstance))
                {
                    return true;
                }

                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                GameObject spouseInstance = GetSpouseInstance(spouseNpcId);
                if (spouseInstance == null || !IsSameNpcObject(npcTransform, spouseInstance))
                {
                    return false;
                }

                Vector3 weddingPosition = FindWeddingBuildingNPCPosition();
                if (weddingPosition == Vector3.zero)
                {
                    return false;
                }

                return Vector3.Distance(npcTransform.position, weddingPosition) <= WeddingNpcDistanceThreshold;
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 判断婚礼NPC失败: " + e.Message);
                return false;
            }
        }

        public void HandleDivorceNpcRelocation(string npcId)
        {
            try
            {
                DestroyWeddingPlaceholder();

                if (npcId == GoblinAffinityConfig.NPC_ID)
                {
                    DestroyGoblinNPC();
                    SpawnGoblinNPC(null, false, false);
                }
                else if (npcId == NurseAffinityConfig.NPC_ID)
                {
                    DestroyNurseNPC();
                    SpawnNurseNPC(null, false, false);
                }
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 处理离婚NPC复位失败: " + e.Message);
            }
        }

        private GameObject GetSpouseInstance(string spouseNpcId)
        {
            if (spouseNpcId == GoblinAffinityConfig.NPC_ID)
            {
                return goblinNPCInstance;
            }

            if (spouseNpcId == NurseAffinityConfig.NPC_ID)
            {
                return nurseNPCInstance;
            }

            return null;
        }

        private void SetWeddingNpcIdle(GameObject spouseInstance)
        {
            if (spouseInstance == null)
            {
                return;
            }

            GoblinMovement goblinMovement = spouseInstance.GetComponent<GoblinMovement>();
            if (goblinMovement != null)
            {
                goblinMovement.StopMove();
                goblinMovement.enabled = false;
            }

            NurseMovement nurseMovement = spouseInstance.GetComponent<NurseMovement>();
            if (nurseMovement != null)
            {
                nurseMovement.StopMove();
                nurseMovement.enabled = false;
            }
        }

        private void DestroyWeddingPlaceholder()
        {
            if (weddingNPCInstance != null)
            {
                UnityEngine.Object.Destroy(weddingNPCInstance);
                weddingNPCInstance = null;
            }
        }

        private static bool IsSameNpcObject(Transform npcTransform, GameObject instance)
        {
            if (npcTransform == null || instance == null)
            {
                return false;
            }

            Transform instanceTransform = instance.transform;
            return npcTransform == instanceTransform
                || npcTransform.root == instanceTransform
                || instanceTransform.root == npcTransform;
        }
    }
}
