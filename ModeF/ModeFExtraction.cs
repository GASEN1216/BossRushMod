using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 撤离与结算

        private const float MODEF_EXTRACTION_COUNTDOWN_SECONDS = 15f;
        private static readonly string[] ModeFOriginalExtractionExcludedNamePrefixes =
        {
            "ModeF_",
            "BossRush_"
        };

        private readonly List<GameObject> modeFDisabledOriginalExtractionObjects = new List<GameObject>();
        private readonly Dictionary<int, bool> modeFOriginalExtractionActiveStateByObjectId = new Dictionary<int, bool>();
        private Duckov.MiniMaps.SimplePointOfInterest modeFExtractionMapMarker = null;

        /// <summary>
        /// 在地图上为撤离点创建标记
        /// </summary>
        private void CreateModeFExtractionMapMarker(Vector3 position)
        {
            CleanupModeFExtractionMapMarker();
            try
            {
                string sceneId = null;
                try
                {
                    var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    sceneId = activeScene.name;
                }
                catch { }

                string label = L10n.T("撤离点", "Extraction");
                modeFExtractionMapMarker = Duckov.MiniMaps.SimplePointOfInterest.Create(
                    position, sceneId, label, null, false);
                if (modeFExtractionMapMarker != null)
                {
                    modeFExtractionMapMarker.Color = new Color(0.2f, 1f, 0.2f, 1f);
                    modeFExtractionMapMarker.ScaleFactor = 1.5f;
                }
                DevLog("[ModeF] 撤离点地图标记已创建: " + position);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 创建撤离点地图标记失败: " + e.Message);
            }
        }

        /// <summary>
        /// 清理撤离点地图标记
        /// </summary>
        private void CleanupModeFExtractionMapMarker()
        {
            if (modeFExtractionMapMarker != null)
            {
                try { UnityEngine.Object.Destroy(modeFExtractionMapMarker.gameObject); } catch { }
                modeFExtractionMapMarker = null;
            }
        }

        /// <summary>
        /// 清除地图原有撤离点
        /// </summary>
        private void ClearOriginalExtractionPoints()
        {
            try
            {
                bool usedExitCreatorSnapshot;
                int cleared = OriginalExtractionPointIsolationHelper.Disable(
                    modeFDisabledOriginalExtractionObjects,
                    modeFOriginalExtractionActiveStateByObjectId,
                    ShouldSkipModeFOriginalExtractionArea,
                    ModeFOriginalExtractionExcludedNamePrefixes,
                    null,
                    out usedExitCreatorSnapshot,
                    out _);

                DevLog("[ModeF] 已清除 " + cleared + " 个原始撤离点"
                    + " (source=" + (usedExitCreatorSnapshot ? "ExitCreator" : "fallback") + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ClearOriginalExtractionPoints 失败: " + e.Message);
            }
        }

        private void RestoreOriginalExtractionPoints()
        {
            try
            {
                int restored = OriginalExtractionPointIsolationHelper.Restore(
                    modeFDisabledOriginalExtractionObjects,
                    modeFOriginalExtractionActiveStateByObjectId);
                DevLog("[ModeF] 已恢复 " + restored + " 个原始撤离点");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RestoreOriginalExtractionPoints 失败: " + e.Message);
            }
        }

        private bool ShouldSkipModeFOriginalExtractionArea(CountDownArea area)
        {
            return modeFState.ActiveExtractionArea != null && area == modeFState.ActiveExtractionArea;
        }

        /// <summary>
        /// 生成最终撤离点
        /// </summary>
        private void SpawnFinalExtractionPoint()
        {
            try
            {
                if (modeFState.ExtractionPointSpawned)
                {
                    DevLog("[ModeF] 撤离点已生成，跳过");
                    return;
                }

                // 选择撤离点位置（从刷怪点中选一个）
                Vector3 extractionPos = FindModeFExtractionPoint();

                ModeExtractionPointRequest request = new ModeExtractionPointRequest();
                request.ObjectName = "ModeF_ExtractionPoint";
                request.Position = extractionPos;
                request.CountdownSeconds = MODEF_EXTRACTION_COUNTDOWN_SECONDS;
                request.FallbackTriggerRadius = 3f;
                request.LogPrefix = "[ModeF]";
                request.IsCurrentArea = delegate(CountDownArea area)
                {
                    return modeFActive && modeFState.ActiveExtractionArea == area;
                };
                request.OnSucceed = delegate
                {
                    if (!modeFActive || modeFState.ActiveExtractionArea == null || modeFState.ExtractionResolved)
                    {
                        return;
                    }

                    modeFState.ExtractionResolved = true;
                    OnModeFExtractionSuccess();
                };
                request.OnFallbackNotify = TryNotifyModeFExtraction;
                request.OnFallbackLoadBase = TryLoadBaseSceneAfterModeFExtraction;

                ModeExtractionPointResult result = ModeExtractionPointFactory.CreateExtractionPoint(request);
                if (result == null || result.GameObject == null || result.CountDownArea == null)
                {
                    DevLog("[ModeF] [ERROR] 最终撤离点创建失败");
                    return;
                }

                CountDownArea countDown = result.CountDownArea;
                modeFState.ActiveExtractionArea = countDown;
                modeFState.ExtractionPointSpawned = true;

                // 在地图上创建撤离点标记
                CreateModeFExtractionMapMarker(extractionPos);

                DevLog("[ModeF] 最终撤离点已生成: " + extractionPos);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] SpawnFinalExtractionPoint 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 撤离成功处理。
        /// 当前悬赏奖励逐件直接复用共享高品质奖励池，并写入寄存/缓冲，不额外追加 Mode F 专属的 >=6 二次过滤。
        /// </summary>
        private void OnModeFExtractionSuccess()
        {
            bool exitAttempted = false;
            try
            {
                if (!modeFActive)
                {
                    return;
                }

                int marks = modeFState.PlayerBountyMarks;
                int storedUtilityRewards = DeliverModeFPendingUtilityRewardsToStorage();
                DevLog("[ModeF] 撤离成功！玩家印记: " + marks);
                if (storedUtilityRewards > 0)
                {
                    DevLog("[ModeF] 撤离结算：已将 " + storedUtilityRewards + " 个待发工事补给送入寄存/缓冲");
                }

                if (marks > 0)
                {
                    int storageRewards = 0;
                    int failedRewards = 0;
                    int prePushCount = GetModeFStorageNotificationQueueCount();
                    for (int i = 0; i < marks; i++)
                    {
                        Item reward = null;
                        try
                        {
                            int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                            if (rewardTypeId <= 0) continue;

                            reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                            if (reward != null)
                            {
                                try
                                {
                                    PlayerStorage.Push(reward, true);
                                    storageRewards++;
                                }
                                catch
                                {
                                    try
                                    {
                                        ItemStatsSystem.Data.ItemTreeData rewardData = ItemStatsSystem.Data.ItemTreeData.FromItem(reward);
                                        PlayerStorageBuffer.Buffer.Add(rewardData);
                                        reward.DestroyTree();
                                        storageRewards++;
                                        DevLog("[ModeF] [WARNING] PlayerStorage.Push 失败，已回退直写寄存缓冲");
                                    }
                                    catch (Exception fallbackEx)
                                    {
                                        failedRewards++;
                                        DevLog("[ModeF] [ERROR] 撤离奖励写入寄存失败: " + fallbackEx.Message);
                                        try { reward.DestroyTree(); } catch { }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            try { if (reward != null) reward.DestroyTree(); } catch { }
                        }
                    }

                    ClearModeFStorageNotificationQueue(prePushCount);
                    try { PlayerStorageBuffer.SaveBuffer(); } catch { }

                    ShowBigBanner(L10n.T(
                        "<color=green>血猎追击胜利！</color> 已向寄存点发送 " + storageRewards + " 件悬赏奖励",
                        "<color=green>Bloodhunt Victory!</color> " + storageRewards + " bounty rewards sent to storage"
                    ));

                    DevLog("[ModeF] 寄存点奖励发放: storage=" + storageRewards + ", failed=" + failedRewards);
                }
                else
                {
                    ShowBigBanner(L10n.T(
                        "<color=green>血猎追击胜利！</color> 你成功撤离了",
                        "<color=green>Bloodhunt Victory!</color> You successfully extracted"
                    ));
                }

                ExitModeF();
                exitAttempted = true;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] OnModeFExtractionSuccess 失败: " + e.Message);
            }
            finally
            {
                if (!exitAttempted && modeFActive)
                {
                    try { ExitModeF(); } catch (Exception exitEx) { DevLog("[ModeF] [ERROR] OnModeFExtractionSuccess 强制退出失败: " + exitEx.Message); }
                }
            }
        }

        private Vector3 FindModeFExtractionPoint()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            Vector3 extractionPos;

            Vector3[] preferredPoints = GetSharedCommonNPCSpawnPointsForScene(sceneName);
            if (TryFindModeFPointFromCandidates(preferredPoints, 30f, out extractionPos))
            {
                DevLog("[ModeF] 使用公共 NPC 点池生成撤离点: " + extractionPos);
                return GetSafeBossSpawnPosition(extractionPos);
            }

            Vector3[] fallbackSpawnPoints = GetModeEFlattenedSpawnPoints();
            if (TryFindModeFPointFromCandidates(fallbackSpawnPoints, 30f, out extractionPos))
            {
                DevLog("[ModeF] [WARNING] 公共 NPC 点池为空，回退使用刷怪点池生成撤离点: " + extractionPos);
                return GetSafeBossSpawnPosition(extractionPos);
            }

            extractionPos = FindSpawnPointAwayFromPlayer(30f);
            DevLog("[ModeF] [WARNING] 未找到白名单撤离点，回退到通用远离玩家点位: " + extractionPos);
            return GetSafeBossSpawnPosition(extractionPos);
        }

        private bool TryFindModeFPointFromCandidates(Vector3[] candidatePoints, float preferredMinDistance, out Vector3 selectedPoint)
        {
            selectedPoint = Vector3.zero;
            if (candidatePoints == null || candidatePoints.Length <= 0)
            {
                return false;
            }

            CharacterMainControl player = CharacterMainControl.Main;
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
            float preferredMinDistanceSqr = preferredMinDistance * preferredMinDistance;
            float farthestDistanceSqr = -1f;
            reusableSpawnCandidates.Clear();

            for (int i = 0; i < candidatePoints.Length; i++)
            {
                Vector3 point = candidatePoints[i];
                float distanceSqr = (point - playerPos).sqrMagnitude;
                if (distanceSqr >= preferredMinDistanceSqr)
                {
                    reusableSpawnCandidates.Add(point);
                }

                if (distanceSqr > farthestDistanceSqr)
                {
                    farthestDistanceSqr = distanceSqr;
                    selectedPoint = point;
                }
            }

            if (reusableSpawnCandidates.Count > 0)
            {
                selectedPoint = reusableSpawnCandidates[UnityEngine.Random.Range(0, reusableSpawnCandidates.Count)];
                return true;
            }

            return farthestDistanceSqr >= 0f;
        }

        private int GetModeFStorageNotificationQueueCount()
        {
            try
            {
                FieldInfo pendingTextsField = typeof(NotificationText).GetField("pendingTexts", BindingFlags.NonPublic | BindingFlags.Static);
                if (pendingTextsField != null)
                {
                    Queue<string> queue = pendingTextsField.GetValue(null) as Queue<string>;
                    if (queue != null) return queue.Count;
                }
            }
            catch { }
            return 0;
        }

        private void ClearModeFStorageNotificationQueue(int prePushCount)
        {
            try
            {
                if (prePushCount < 0)
                {
                    return;
                }

                // NotificationText.pendingTexts 是全局共享队列，无法可靠区分来源。
                // 为避免误删快递员或其他系统的提示，这里保守地不做清理。
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] ClearModeFStorageNotificationQueue 失败: " + e.Message);
            }
        }

        private void TryNotifyModeFExtraction(Vector3 extractionPos)
        {
            try
            {
                if (LevelManager.Instance == null)
                {
                    return;
                }

                EvacuationInfo info = new EvacuationInfo(
                    Duckov.Scenes.MultiSceneCore.ActiveSubSceneID,
                    extractionPos
                );
                LevelManager.Instance.NotifyEvacuated(info);
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryNotifyModeFExtraction 失败: " + e.Message);
            }
        }

        private void TryLoadBaseSceneAfterModeFExtraction()
        {
            try
            {
                if (SceneLoader.Instance == null)
                {
                    DevLog("[ModeF] [WARNING] TryLoadBaseSceneAfterModeFExtraction: SceneLoader.Instance 为 null");
                    return;
                }

                Cysharp.Threading.Tasks.UniTaskExtensions.Forget(SceneLoader.Instance.LoadBaseScene(null, true));
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] TryLoadBaseSceneAfterModeFExtraction 失败: " + e.Message);
            }
        }

        #endregion
    }
}
