using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using ItemStatsSystem;
using UnityEngine.Events;
using Duckov.UI;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region Mode F 撤离与结算

        private const float MODEF_EXTRACTION_COUNTDOWN_SECONDS = 15f;
        private static readonly BindingFlags ModeFExtractionInstanceBindingFlags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly string[] ModeFExitCreatorAreaMemberNames =
        {
            "Areas",
            "areas",
            "CountDownAreas",
            "countDownAreas",
            "Exits",
            "exits"
        };

        private static readonly FieldInfo countDownAreaRequiredExtractionTimeField =
            typeof(CountDownArea).GetField("requiredExtrationTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo countDownAreaTimeRemainingField =
            typeof(CountDownArea).GetField("countDownTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

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

        private void AddModeFOriginalExtractionAreaCandidate(
            CountDownArea area,
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            if (area == null || area.gameObject == null || seenIds == null || results == null)
            {
                return;
            }

            if (!seenIds.Add(area.GetInstanceID()))
            {
                return;
            }

            modeFOriginalExtractionActiveStateByObjectId[area.gameObject.GetInstanceID()] = area.gameObject.activeSelf;
            results.Add(area);
        }

        private void CollectModeFOriginalExtractionAreasFromObject(
            object source,
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            if (source == null)
            {
                return;
            }

            CountDownArea countDownArea = source as CountDownArea;
            if (countDownArea != null)
            {
                AddModeFOriginalExtractionAreaCandidate(countDownArea, seenIds, results);
                return;
            }

            GameObject gameObject = source as GameObject;
            if (gameObject != null)
            {
                AddModeFOriginalExtractionAreaCandidate(gameObject.GetComponent<CountDownArea>(), seenIds, results);
                return;
            }

            Component component = source as Component;
            if (component != null)
            {
                AddModeFOriginalExtractionAreaCandidate(component.GetComponent<CountDownArea>(), seenIds, results);
                return;
            }

            System.Collections.IEnumerable enumerable = source as System.Collections.IEnumerable;
            if (enumerable == null || source is string)
            {
                return;
            }

            foreach (object item in enumerable)
            {
                CollectModeFOriginalExtractionAreasFromObject(item, seenIds, results);
            }
        }

        private bool TryCollectModeFOriginalExtractionAreasFromExitCreator(
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            try
            {
                LevelManager levelManager = LevelManager.Instance;
                if (levelManager == null)
                {
                    levelManager = UnityEngine.Object.FindObjectOfType<LevelManager>();
                }

                if (levelManager == null || levelManager.ExitCreator == null)
                {
                    return false;
                }

                Component exitCreatorComponent = levelManager.ExitCreator as Component;
                if (exitCreatorComponent != null)
                {
                    CountDownArea[] childAreas = exitCreatorComponent.GetComponentsInChildren<CountDownArea>(true);
                    for (int i = 0; i < childAreas.Length; i++)
                    {
                        AddModeFOriginalExtractionAreaCandidate(childAreas[i], seenIds, results);
                    }
                }

                Type exitCreatorType = levelManager.ExitCreator.GetType();
                for (int i = 0; i < ModeFExitCreatorAreaMemberNames.Length; i++)
                {
                    string memberName = ModeFExitCreatorAreaMemberNames[i];

                    PropertyInfo property = exitCreatorType.GetProperty(memberName, ModeFExtractionInstanceBindingFlags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        CollectModeFOriginalExtractionAreasFromObject(
                            property.GetValue(levelManager.ExitCreator, null),
                            seenIds,
                            results);
                    }

                    FieldInfo field = exitCreatorType.GetField(memberName, ModeFExtractionInstanceBindingFlags);
                    if (field != null)
                    {
                        CollectModeFOriginalExtractionAreasFromObject(
                            field.GetValue(levelManager.ExitCreator),
                            seenIds,
                            results);
                    }
                }
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 从 ExitCreator 收集原始撤离点失败: " + e.Message);
            }

            return results.Count > 0;
        }

        private void CollectModeFOriginalExtractionAreasFromFallbackSceneScan(
            HashSet<int> seenIds,
            List<CountDownArea> results)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            CountDownArea[] areas = UnityEngine.Object.FindObjectsOfType<CountDownArea>(true);
            for (int i = 0; i < areas.Length; i++)
            {
                CountDownArea area = areas[i];
                if (area == null || area.gameObject == null)
                {
                    continue;
                }

                Scene areaScene = area.gameObject.scene;
                if (!areaScene.IsValid() || areaScene != activeScene)
                {
                    continue;
                }

                if (!IsLikelyOriginalExtractionArea(area))
                {
                    continue;
                }

                AddModeFOriginalExtractionAreaCandidate(area, seenIds, results);
            }
        }

        /// <summary>
        /// 清除地图原有撤离点
        /// </summary>
        private void ClearOriginalExtractionPoints()
        {
            try
            {
                modeFDisabledOriginalExtractionObjects.Clear();
                modeFOriginalExtractionActiveStateByObjectId.Clear();

                HashSet<int> seenIds = new HashSet<int>();
                List<CountDownArea> originalAreas = new List<CountDownArea>();
                bool usedExitCreatorSnapshot = TryCollectModeFOriginalExtractionAreasFromExitCreator(seenIds, originalAreas);
                if (!usedExitCreatorSnapshot)
                {
                    CollectModeFOriginalExtractionAreasFromFallbackSceneScan(seenIds, originalAreas);
                }

                if (originalAreas.Count == 0)
                {
                    DevLog("[ModeF] ClearOriginalExtractionPoints: 未找到撤离点");
                    return;
                }

                int cleared = 0;
                for (int i = 0; i < originalAreas.Count; i++)
                {
                    try
                    {
                        CountDownArea area = originalAreas[i];

                        // 跳过 Mode F 自己生成的撤离点
                        if (modeFState.ActiveExtractionArea != null && area == modeFState.ActiveExtractionArea)
                            continue;

                        if (area == null || area.gameObject == null)
                        {
                            continue;
                        }

                        int objectId = area.gameObject.GetInstanceID();
                        bool wasActive = false;
                        if (!modeFOriginalExtractionActiveStateByObjectId.TryGetValue(objectId, out wasActive))
                        {
                            wasActive = area.gameObject.activeSelf;
                            modeFOriginalExtractionActiveStateByObjectId[objectId] = wasActive;
                        }

                        area.gameObject.SetActive(false);
                        if (!modeFDisabledOriginalExtractionObjects.Contains(area.gameObject))
                        {
                            modeFDisabledOriginalExtractionObjects.Add(area.gameObject);
                        }
                        if (wasActive)
                        {
                            cleared++;
                        }
                    }
                    catch (Exception areaEx)
                    {
                        DevLog("[ModeF] [WARNING] ClearOriginalExtractionPoints failed for area: " + areaEx.Message);
                    }
                }

                DevLog("[ModeF] 已清除 " + cleared + " 个原始撤离点"
                    + " (source=" + (usedExitCreatorSnapshot ? "ExitCreator" : "fallback") + ")");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] ClearOriginalExtractionPoints 失败: " + e.Message);
            }
        }

        private bool IsLikelyOriginalExtractionArea(CountDownArea area)
        {
            if (area == null || area.gameObject == null) return false;

            if (modeFOriginalExtractionActiveStateByObjectId.ContainsKey(area.gameObject.GetInstanceID()))
            {
                return true;
            }

            string objectName = area.gameObject.name;
            if (objectName.StartsWith("ModeF_", StringComparison.Ordinal) ||
                objectName.StartsWith("BossRush_", StringComparison.Ordinal))
            {
                return false;
            }

            if (area.onCountDownSucceed != null)
            {
                int persistentCount = area.onCountDownSucceed.GetPersistentEventCount();
                for (int i = 0; i < persistentCount; i++)
                {
                    string methodName = area.onCountDownSucceed.GetPersistentMethodName(i);
                    UnityEngine.Object target = area.onCountDownSucceed.GetPersistentTarget(i);
                    string targetTypeName = target != null ? target.GetType().Name : string.Empty;

                    if (string.Equals(methodName, "NotifyEvacuated", StringComparison.Ordinal) ||
                        string.Equals(targetTypeName, "LevelManagerProxy", StringComparison.Ordinal) ||
                        string.Equals(methodName, "LoadScene", StringComparison.Ordinal) ||
                        string.Equals(methodName, "LoadBaseScene", StringComparison.Ordinal) ||
                        string.Equals(methodName, "LoadMainMenu", StringComparison.Ordinal) ||
                        string.Equals(targetTypeName, "SceneLoaderProxy", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void RestoreOriginalExtractionPoints()
        {
            try
            {
                if (modeFDisabledOriginalExtractionObjects.Count <= 0)
                {
                    return;
                }

                int restored = 0;
                for (int i = modeFDisabledOriginalExtractionObjects.Count - 1; i >= 0; i--)
                {
                    GameObject extractionObject = modeFDisabledOriginalExtractionObjects[i];
                    if (extractionObject == null)
                    {
                        continue;
                    }

                    try
                    {
                        bool shouldBeActive = true;
                        bool wasTracked = modeFOriginalExtractionActiveStateByObjectId.TryGetValue(
                            extractionObject.GetInstanceID(),
                            out shouldBeActive);
                        if (!wasTracked)
                        {
                            shouldBeActive = true;
                        }
                        extractionObject.SetActive(shouldBeActive);
                        if (shouldBeActive)
                        {
                            restored++;
                        }
                    }
                    catch (Exception areaEx)
                    {
                        DevLog("[ModeF] [WARNING] RestoreOriginalExtractionPoints failed for object: " + areaEx.Message);
                    }
                }

                DevLog("[ModeF] 已恢复 " + restored + " 个原始撤离点");
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] RestoreOriginalExtractionPoints 失败: " + e.Message);
            }
            finally
            {
                modeFDisabledOriginalExtractionObjects.Clear();
                modeFOriginalExtractionActiveStateByObjectId.Clear();
            }
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

                GameObject exitObj = CreateModeFExtractionObject(extractionPos);
                if (exitObj == null)
                {
                    DevLog("[ModeF] [ERROR] 最终撤离点创建失败");
                    return;
                }

                CountDownArea countDown = exitObj.GetComponentInChildren<CountDownArea>(true);
                if (countDown == null)
                {
                    countDown = exitObj.AddComponent<CountDownArea>();
                }

                countDown.enabled = true;
                EnsureModeFExtractionCollider(exitObj);
                ConfigureModeFExtractionCountDown(countDown, MODEF_EXTRACTION_COUNTDOWN_SECONDS);

                if (countDown.onCountDownSucceed == null)
                {
                    countDown.onCountDownSucceed = new UnityEvent();
                }
                bool shouldNotifyGameEvacuation = true;
                bool shouldLoadBaseScene = true;
                ResolveModeFExtractionFallbackActions(
                    countDown.onCountDownSucceed,
                    out shouldNotifyGameEvacuation,
                    out shouldLoadBaseScene);
                countDown.onCountDownSucceed.AddListener(() =>
                {
                    if (!modeFActive || modeFState.ActiveExtractionArea != countDown || modeFState.ExtractionResolved)
                    {
                        return;
                    }

                    modeFState.ExtractionResolved = true;
                    OnModeFExtractionSuccess();
                    if (shouldNotifyGameEvacuation)
                    {
                        TryNotifyModeFExtraction(extractionPos);
                    }
                    if (shouldLoadBaseScene)
                    {
                        TryLoadBaseSceneAfterModeFExtraction();
                    }
                });

                if (countDown.onCountDownStarted == null)
                {
                    countDown.onCountDownStarted = new UnityEvent<CountDownArea>();
                }
                countDown.onCountDownStarted.AddListener((area) =>
                {
                    if (!modeFActive || modeFState.ActiveExtractionArea != countDown) return;
                    try { EvacuationCountdownUI.Request(area); } catch { }
                });

                if (countDown.onCountDownStopped == null)
                {
                    countDown.onCountDownStopped = new UnityEvent<CountDownArea>();
                }
                countDown.onCountDownStopped.AddListener((area) =>
                {
                    if (!modeFActive || modeFState.ActiveExtractionArea != countDown) return;
                    try { EvacuationCountdownUI.Release(area); } catch { }
                });

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

        private void ResolveModeFExtractionFallbackActions(
            UnityEvent onCountDownSucceed,
            out bool shouldNotifyGameEvacuation,
            out bool shouldLoadBaseScene)
        {
            shouldNotifyGameEvacuation = true;
            shouldLoadBaseScene = true;

            if (onCountDownSucceed == null)
            {
                return;
            }

            int persistentCount = onCountDownSucceed.GetPersistentEventCount();
            for (int i = 0; i < persistentCount; i++)
            {
                string methodName = onCountDownSucceed.GetPersistentMethodName(i);
                UnityEngine.Object target = onCountDownSucceed.GetPersistentTarget(i);
                string targetTypeName = target != null ? target.GetType().Name : string.Empty;

                if (string.Equals(methodName, "NotifyEvacuated", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "LevelManagerProxy", StringComparison.Ordinal))
                {
                    shouldNotifyGameEvacuation = false;
                }

                if (string.Equals(methodName, "LoadScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadBaseScene", StringComparison.Ordinal) ||
                    string.Equals(methodName, "LoadMainMenu", StringComparison.Ordinal) ||
                    string.Equals(targetTypeName, "SceneLoaderProxy", StringComparison.Ordinal))
                {
                    shouldLoadBaseScene = false;
                }
            }
        }

        private GameObject CreateModeFExtractionObject(Vector3 extractionPos)
        {
            GameObject exitObj = TryCreateModeFExtractionFromPrefab(extractionPos);
            if (exitObj != null)
            {
                return exitObj;
            }

            try
            {
                GameObject fallbackExit = new GameObject("ModeF_ExtractionPoint");
                fallbackExit.transform.position = extractionPos;
                EnsureModeFExtractionCollider(fallbackExit);
                fallbackExit.SetActive(true);
                DevLog("[ModeF] 手动创建撤离点");
                return fallbackExit;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [ERROR] CreateModeFExtractionObject 回退创建失败: " + e.Message);
                return null;
            }
        }

        private GameObject TryCreateModeFExtractionFromPrefab(Vector3 extractionPos)
        {
            try
            {
                LevelManager levelManager = LevelManager.Instance;
                if (levelManager == null)
                {
                    levelManager = UnityEngine.Object.FindObjectOfType<LevelManager>();
                }

                if (levelManager == null || levelManager.ExitCreator == null || levelManager.ExitCreator.exitPrefab == null)
                {
                    return null;
                }

                GameObject exitObj = UnityEngine.Object.Instantiate(levelManager.ExitCreator.exitPrefab, extractionPos, Quaternion.identity);
                PrepareModeFExtractionPrefab(exitObj);
                DevLog("[ModeF] 使用官方 exitPrefab 创建撤离点");
                return exitObj;
            }
            catch (Exception e)
            {
                DevLog("[ModeF] [WARNING] 获取官方 exitPrefab 失败: " + e.Message);
                return null;
            }
        }

        private void PrepareModeFExtractionPrefab(GameObject exitObj)
        {
            if (exitObj == null)
            {
                return;
            }

            exitObj.SetActive(true);
            ActivateAllChildren(exitObj);
            EnsureModeFExtractionCollider(exitObj);

            Renderer[] renderers = exitObj.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }

            DisableExitSmokeEffects(exitObj);
        }

        private void EnsureModeFExtractionCollider(GameObject exitObj)
        {
            if (exitObj == null)
            {
                return;
            }

            Collider[] colliders = exitObj.GetComponentsInChildren<Collider>(true);
            bool hasTriggerCollider = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                {
                    continue;
                }

                collider.enabled = true;
                if (collider.isTrigger)
                {
                    hasTriggerCollider = true;
                }
            }

            if (hasTriggerCollider)
            {
                return;
            }

            SphereCollider fallbackCollider = exitObj.GetComponent<SphereCollider>();
            if (fallbackCollider == null)
            {
                fallbackCollider = exitObj.AddComponent<SphereCollider>();
            }

            fallbackCollider.enabled = true;
            fallbackCollider.isTrigger = true;
            fallbackCollider.radius = 3f;
        }

        private void ConfigureModeFExtractionCountDown(CountDownArea countDown, float countDownSeconds)
        {
            if (countDown == null)
            {
                return;
            }

            try
            {
                if (countDownAreaRequiredExtractionTimeField != null)
                {
                    countDownAreaRequiredExtractionTimeField.SetValue(countDown, countDownSeconds);
                }

                if (countDownAreaTimeRemainingField != null)
                {
                    countDownAreaTimeRemainingField.SetValue(countDown, countDownSeconds);
                }
            }
            catch { }
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
