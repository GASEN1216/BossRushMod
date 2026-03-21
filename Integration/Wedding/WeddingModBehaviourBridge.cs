using System;
using System.Collections;
using UnityEngine;

namespace BossRush
{
    /// <summary>
    /// Wedding runtime bridge helpers for ModBehaviour.
    /// Uses cached building state and marker-based NPC checks to avoid per-frame reflection/scans.
    /// </summary>
    public partial class ModBehaviour
    {
        private const float SpouseFollowRestorePollInterval = 0.25f;
        private const float SpouseFollowRestoreSettleDelay = 0.75f;
        private const float SpouseFollowRestoreTimeout = 20f;
        private const float FollowingSpouseDialogueStayDuration = 0.05f;
        private int spouseFollowRestoreRequestId = 0;

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
                string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (string.IsNullOrEmpty(spouseNpcId)
                    || AffinityManager.IsSpouseFollowingPlayer(spouseNpcId)
                    || !IsBaseHubSceneName(currentSceneName)
                    || !HasWeddingBuildingPlaced())
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
                    RefreshSpouseInteractionOptions(spouseInstance);
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

        public bool CanCurrentSpouseFollowPlayer(string npcId)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                return false;
            }

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            return !string.IsNullOrEmpty(spouseNpcId)
                && string.Equals(spouseNpcId, npcId, StringComparison.Ordinal)
                && AffinityManager.IsMarriedToPlayer(npcId)
                && AffinityManager.GetLevel(npcId) >= AffinityManager.SPOUSE_FOLLOW_REQUIRED_LEVEL;
        }

        public bool IsSpouseFollowerInstance(string npcId, Transform npcTransform)
        {
            if (!CanCurrentSpouseFollowPlayer(npcId)
                || !AffinityManager.IsSpouseFollowingPlayer(npcId)
                || npcTransform == null)
            {
                return false;
            }

            GameObject spouseInstance = GetSpouseInstance(npcId);
            return spouseInstance != null && IsSameNpcObject(npcTransform, spouseInstance);
        }

        public bool ShouldShowSpouseFollowOption(string npcId, Transform npcTransform)
        {
            if (!CanCurrentSpouseFollowPlayer(npcId)
                || AffinityManager.IsSpouseFollowingPlayer(npcId)
                || npcTransform == null)
            {
                return false;
            }

            return IsWeddingNpcInstance(npcTransform);
        }

        public bool ShouldShowSpouseDivorceOption(string npcId, Transform npcTransform)
        {
            if (string.IsNullOrEmpty(npcId)
                || !AffinityManager.IsMarriedToPlayer(npcId)
                || AffinityManager.IsSpouseFollowingPlayer(npcId)
                || npcTransform == null)
            {
                return false;
            }

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            if (string.IsNullOrEmpty(spouseNpcId) || !string.Equals(spouseNpcId, npcId, StringComparison.Ordinal))
            {
                return false;
            }

            return IsWeddingNpcInstance(npcTransform);
        }

        public bool ShouldShowSpouseHomeOption(string npcId, Transform npcTransform)
        {
            return !string.IsNullOrEmpty(npcId)
                && AffinityManager.IsMarriedToPlayer(npcId)
                && IsSpouseFollowerInstance(npcId, npcTransform);
        }

        public float AdjustDialogueStayDurationForSpouseFollow(string npcId, Transform npcTransform, float requestedStayDuration)
        {
            if (requestedStayDuration <= FollowingSpouseDialogueStayDuration
                || string.IsNullOrEmpty(npcId)
                || npcTransform == null
                || !AffinityManager.IsSpouseFollowingPlayer(npcId)
                || !IsSpouseFollowerInstance(npcId, npcTransform))
            {
                return requestedStayDuration;
            }

            return FollowingSpouseDialogueStayDuration;
        }

        public bool TryStartSpouseFollowingPlayer(string npcId)
        {
            try
            {
                if (!CanCurrentSpouseFollowPlayer(npcId))
                {
                    return false;
                }

                GameObject spouseInstance = GetSpouseInstance(npcId);
                if (spouseInstance == null)
                {
                    return false;
                }

                bool stateChanged = AffinityManager.SetSpouseFollowingPlayer(npcId, true);
                if (!stateChanged && !AffinityManager.IsSpouseFollowingPlayer(npcId))
                {
                    return false;
                }

                PrepareSpouseInstanceForFollow(spouseInstance, npcId);
                ShowMessage(L10n.T("配偶开始跟随你了。", "Your spouse is now following you."));
                return true;
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 启动配偶跟随失败: " + e.Message);
                return false;
            }
        }

        public bool SendSpouseHome(string npcId, bool showMessage = true)
        {
            try
            {
                if (string.IsNullOrEmpty(npcId))
                {
                    return false;
                }

                if (AffinityManager.IsSpouseFollowingPlayer(npcId))
                {
                    AffinityManager.SetSpouseFollowingPlayer(npcId, false);
                }

                GameObject spouseInstance = GetSpouseInstance(npcId);
                if (spouseInstance != null)
                {
                    DisableSpouseFollowOnInstance(spouseInstance);
                    ClearWeddingNpcMarker(spouseInstance);
                    RefreshSpouseInteractionOptions(spouseInstance);
                }

                Transform homeTransform = TrySpawnMarriedNpcAtWeddingPoint();
                if (homeTransform != null)
                {
                    RefreshSpouseInteractionOptions(homeTransform.gameObject);
                }
                if (homeTransform == null && spouseInstance != null)
                {
                    if (npcId == GoblinAffinityConfig.NPC_ID)
                    {
                        DestroyGoblinNPC();
                    }
                    else if (npcId == NurseAffinityConfig.NPC_ID)
                    {
                        DestroyNurseNPC();
                    }
                }

                if (showMessage)
                {
                    ShowMessage(L10n.T("配偶已经回家了。", "Your spouse has gone home."));
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 配偶回家失败: " + e.Message);
                return false;
            }
        }

        public void HandleSpouseFollowAffinityLoss(string npcId)
        {
            try
            {
                if (string.IsNullOrEmpty(npcId)
                    || !AffinityManager.IsSpouseFollowingPlayer(npcId)
                    || AffinityManager.GetLevel(npcId) >= AffinityManager.SPOUSE_FOLLOW_REQUIRED_LEVEL)
                {
                    return;
                }

                SendSpouseHome(npcId, false);
                ShowMessage(L10n.T(
                    "配偶的好感度低于10级，已经先回家了。",
                    "Your spouse's Affinity fell below Lv.10, so they went home."));
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 处理配偶好感不足回家失败: " + e.Message);
            }
        }

        public void ScheduleRestoreFollowingSpouse(string expectedSceneName, string context)
        {
            spouseFollowRestoreRequestId++;
            StartCoroutine(DelayedRestoreFollowingSpouse(expectedSceneName, spouseFollowRestoreRequestId, context));
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

        private IEnumerator DelayedRestoreFollowingSpouse(string expectedSceneName, int requestId, string context)
        {
            if (!ShouldRestoreFollowingSpouseInScene(expectedSceneName))
            {
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < SpouseFollowRestoreTimeout)
            {
                if (requestId != spouseFollowRestoreRequestId)
                {
                    yield break;
                }

                if (!IsEquivalentSpouseRestoreScene(expectedSceneName, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
                {
                    yield break;
                }

                if (CharacterMainControl.Main != null && LevelManager.LevelInited)
                {
                    break;
                }

                yield return new WaitForSeconds(SpouseFollowRestorePollInterval);
                elapsed += SpouseFollowRestorePollInterval;
            }

            if (requestId != spouseFollowRestoreRequestId)
            {
                yield break;
            }

            if (!IsEquivalentSpouseRestoreScene(expectedSceneName, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                yield break;
            }

            yield return new WaitForSeconds(SpouseFollowRestoreSettleDelay);

            if (requestId != spouseFollowRestoreRequestId)
            {
                yield break;
            }

            if (!IsEquivalentSpouseRestoreScene(expectedSceneName, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name))
            {
                yield break;
            }

            RestoreFollowingSpouseForCurrentScene(context);
        }

        private bool ShouldRestoreFollowingSpouseInScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)
                || sceneName.IndexOf("Loading", StringComparison.OrdinalIgnoreCase) >= 0
                || sceneName.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
            return !string.IsNullOrEmpty(spouseNpcId) && AffinityManager.IsSpouseFollowingPlayer(spouseNpcId);
        }

        private void RestoreFollowingSpouseForCurrentScene(string context)
        {
            try
            {
                string spouseNpcId = AffinityManager.GetCurrentSpouseNpcId();
                if (string.IsNullOrEmpty(spouseNpcId) || !AffinityManager.IsSpouseFollowingPlayer(spouseNpcId))
                {
                    return;
                }

                if (!CanCurrentSpouseFollowPlayer(spouseNpcId))
                {
                    HandleSpouseFollowAffinityLoss(spouseNpcId);
                    return;
                }

                GameObject spouseInstance = GetSpouseInstance(spouseNpcId);
                Vector3 restorePosition = Vector3.zero;
                bool hasRestorePosition = TryGetSpouseFollowSpawnPosition(out restorePosition);
                if (spouseInstance == null)
                {
                    if (!hasRestorePosition)
                    {
                        return;
                    }

                    if (spouseNpcId == GoblinAffinityConfig.NPC_ID)
                    {
                        SpawnGoblinNPC(restorePosition, false, true);
                    }
                    else if (spouseNpcId == NurseAffinityConfig.NPC_ID)
                    {
                        SpawnNurseNPC(restorePosition, false, true);
                    }

                    spouseInstance = GetSpouseInstance(spouseNpcId);
                }
                else if (hasRestorePosition)
                {
                    SnapSpouseInstanceToPosition(spouseInstance, restorePosition);
                }

                if (spouseInstance == null)
                {
                    return;
                }

                if (!CanCurrentSpouseFollowPlayer(spouseNpcId))
                {
                    HandleSpouseFollowAffinityLoss(spouseNpcId);
                    return;
                }

                PrepareSpouseInstanceForFollow(spouseInstance, spouseNpcId);
                DevLog("[WeddingBridge] 已恢复配偶跟随: " + spouseNpcId + ", context=" + context);
            }
            catch (Exception e)
            {
                DevLog("[WeddingBridge] 恢复配偶跟随失败: " + e.Message);
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

        private void PrepareSpouseInstanceForFollow(GameObject spouseInstance, string npcId)
        {
            if (spouseInstance == null || string.IsNullOrEmpty(npcId))
            {
                return;
            }

            Transform playerTransform = GetPlayerTransform();
            if (playerTransform == null)
            {
                return;
            }

            spouseInstance.SetActive(true);
            DestroyWeddingPlaceholder();
            ClearWeddingNpcMarker(spouseInstance);

            string currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (npcId == GoblinAffinityConfig.NPC_ID)
            {
                GoblinNPCController goblinController = spouseInstance.GetComponent<GoblinNPCController>();
                if (goblinController != null)
                {
                    goblinController.ExitStationaryIdleState();
                }

                GoblinMovement goblinMovement = spouseInstance.GetComponent<GoblinMovement>();
                if (goblinMovement != null)
                {
                    goblinMovement.enabled = true;
                    goblinMovement.SetSceneName(currentSceneName);
                    goblinMovement.EnablePlayerFollow(playerTransform);
                }

                RefreshSpouseInteractionOptions(spouseInstance);
            }
            else if (npcId == NurseAffinityConfig.NPC_ID)
            {
                NurseMovement nurseMovement = spouseInstance.GetComponent<NurseMovement>();
                if (nurseMovement != null)
                {
                    nurseMovement.enabled = true;
                    nurseMovement.SetSceneName(currentSceneName);
                    nurseMovement.EnablePlayerFollow(playerTransform);
                }

                NurseNPCController nurseController = spouseInstance.GetComponent<NurseNPCController>();
                if (nurseController != null)
                {
                    nurseController.StartIdleAnimation();
                }

                RefreshSpouseInteractionOptions(spouseInstance);
            }
        }

        private void DisableSpouseFollowOnInstance(GameObject spouseInstance)
        {
            if (spouseInstance == null)
            {
                return;
            }

            GoblinMovement goblinMovement = spouseInstance.GetComponent<GoblinMovement>();
            if (goblinMovement != null)
            {
                goblinMovement.DisablePlayerFollow();
            }

            NurseMovement nurseMovement = spouseInstance.GetComponent<NurseMovement>();
            if (nurseMovement != null)
            {
                nurseMovement.DisablePlayerFollow();
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

        private void ClearWeddingNpcMarker(GameObject npcInstance)
        {
            if (npcInstance == null)
            {
                return;
            }

            WeddingNpcResidentMarker marker = npcInstance.GetComponent<WeddingNpcResidentMarker>();
            if (marker != null)
            {
                marker.NpcId = "__detached__";
            }
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

        private bool TryGetSpouseFollowSpawnPosition(out Vector3 spawnPosition)
        {
            spawnPosition = Vector3.zero;

            Transform playerTransform = GetPlayerTransform();
            if (playerTransform == null)
            {
                return false;
            }

            spawnPosition = playerTransform.position;
            return true;
        }

        private void SnapSpouseInstanceToPosition(GameObject spouseInstance, Vector3 targetPosition)
        {
            if (spouseInstance == null)
            {
                return;
            }

            CharacterController controller = spouseInstance.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;

            if (wasEnabled)
            {
                controller.enabled = false;
            }

            spouseInstance.transform.position = targetPosition;

            if (wasEnabled)
            {
                controller.enabled = true;
            }
        }

        private Transform GetPlayerTransform()
        {
            try
            {
                if (CharacterMainControl.Main != null)
                {
                    return CharacterMainControl.Main.transform;
                }
            }
            catch
            {
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            return player != null ? player.transform : null;
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

        private static bool IsEquivalentSpouseRestoreScene(string expectedSceneName, string actualSceneName)
        {
            if (string.Equals(expectedSceneName, actualSceneName, StringComparison.Ordinal))
            {
                return true;
            }

            return IsBaseHubSceneName(expectedSceneName) && IsBaseHubSceneName(actualSceneName);
        }

        private void RefreshSpouseInteractionOptions(GameObject spouseInstance)
        {
            if (spouseInstance == null)
            {
                return;
            }

            GoblinInteractable goblinInteractable = spouseInstance.GetComponent<GoblinInteractable>();
            if (goblinInteractable != null)
            {
                goblinInteractable.RefreshMarriageOptionVisibility();
            }

            NurseInteractable nurseInteractable = spouseInstance.GetComponent<NurseInteractable>();
            if (nurseInteractable != null)
            {
                nurseInteractable.RefreshMarriageOptionVisibility();
            }
        }
    }

    internal sealed class WeddingNpcResidentMarker : MonoBehaviour
    {
        public string NpcId;
    }
}
