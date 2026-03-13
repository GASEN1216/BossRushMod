using System;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Wedding runtime bridge helpers for ModBehaviour.
    /// Uses cached building state and marker-based NPC checks to avoid per-frame reflection/scans.
    /// </summary>
    public partial class ModBehaviour
    {
        public bool HasWeddingBuildingPlaced()
        {
            if (weddingBuildingPresenceKnown)
            {
                return cachedWeddingBuildingPresent;
            }

            return RefreshWeddingBuildingPresence();
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
                    MarkWeddingNpcInstance(spouseInstance, spouseNpcId);
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

                WeddingNpcResidentMarker marker = GetWeddingNpcMarker(npcTransform);
                if (marker == null)
                {
                    return false;
                }

                if (string.IsNullOrEmpty(marker.NpcId))
                {
                    return true;
                }

                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                return !string.IsNullOrEmpty(spouseNpcId)
                    && string.Equals(marker.NpcId, spouseNpcId, StringComparison.Ordinal);
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

            GoblinNPCController goblinController = spouseInstance.GetComponent<GoblinNPCController>();
            if (goblinController != null)
            {
                goblinController.EnterStationaryIdleState();
            }

            NurseMovement nurseMovement = spouseInstance.GetComponent<NurseMovement>();
            if (nurseMovement != null)
            {
                nurseMovement.StopMove();
                nurseMovement.enabled = false;
            }

            NurseNPCController nurseController = spouseInstance.GetComponent<NurseNPCController>();
            if (nurseController != null)
            {
                nurseController.StartIdleAnimation();
            }
        }

        private void MarkWeddingNpcInstance(GameObject npcInstance, string npcId)
        {
            if (npcInstance == null)
            {
                return;
            }

            WeddingNpcResidentMarker marker = npcInstance.GetComponent<WeddingNpcResidentMarker>();
            if (marker == null)
            {
                marker = npcInstance.AddComponent<WeddingNpcResidentMarker>();
            }

            marker.NpcId = npcId ?? string.Empty;
        }

        private static WeddingNpcResidentMarker GetWeddingNpcMarker(Transform npcTransform)
        {
            if (npcTransform == null)
            {
                return null;
            }

            WeddingNpcResidentMarker marker = npcTransform.GetComponent<WeddingNpcResidentMarker>();
            if (marker != null)
            {
                return marker;
            }

            Transform root = npcTransform.root;
            if (root != null && root != npcTransform)
            {
                return root.GetComponent<WeddingNpcResidentMarker>();
            }

            return null;
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

    internal sealed class WeddingNpcResidentMarker : MonoBehaviour
    {
        public string NpcId;
    }
}
