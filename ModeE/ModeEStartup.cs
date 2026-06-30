// ============================================================================
// ModeEStartup.cs - Mode E startup, warmup, and recovery flow
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using TMPro;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using HarmonyLib;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        /// <summary>
        /// Mode E 入场分段耗时统计器，仅在开发模式下输出关键阶段耗时。
        /// </summary>
        private sealed class ModeEStartupProfiler
        {
            private readonly bool enabled;
            private readonly string scope;
            private readonly float startTime;
            private float lastCheckpointTime;
            private bool completed;

            public ModeEStartupProfiler(string scope, string detail = null)
            {
                enabled = DevModeEnabled && ModeEStartupProfilingEnabled;
                if (!enabled)
                {
                    return;
                }

                this.scope = string.IsNullOrEmpty(detail) ? scope : scope + " [" + detail + "]";
                startTime = Time.realtimeSinceStartup;
                lastCheckpointTime = startTime;
                DevLog("[ModeE] [Profile] " + this.scope + " begin");
            }

            public void Mark(string stageName)
            {
                if (!enabled || completed)
                {
                    return;
                }

                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE] [Profile] " + scope + " | " + stageName + ": +" + ((now - lastCheckpointTime) * 1000f).ToString("F1") + " ms");
                lastCheckpointTime = now;
            }

            public void Complete(string status = "completed")
            {
                if (!enabled || completed)
                {
                    return;
                }

                completed = true;
                float now = Time.realtimeSinceStartup;
                DevLog("[ModeE] [Profile] " + scope + " | " + status + " | total=" + ((now - startTime) * 1000f).ToString("F1") + " ms");
            }
        }

        private int BeginModeESession()
        {
            modeESessionToken = ++modeESessionSerial;
            return modeESessionToken;
        }

        private void InvalidateModeESession()
        {
            modeESessionSerial++;
            modeESessionToken = 0;
        }

        private void ResetModeESharedRuntimeState(bool clearSpawnAllocation, bool clearSpawnerCache, bool stopWarmupCoroutine)
        {
            // 尝试对称清理玩家缩放 Modifier；若玩家对象暂不可用，保留句柄供后续重试。
            RemoveModeEPlayerScalingModifiers();

            modeEPlayerFaction = Teams.player;
            modeEAliveEnemies.Clear();
            modeEAliveEnemySet.Clear();
            modeEAliveEnemyFactionMap.Clear();
            modeEFactionDeathCount.Clear();
            modeEFactionAliveMap.Clear();
            modeEEnemyScalingStates.Clear();
            modeEPlayerLastHitKillCount = 0;
            modeEEnemyDeathHandlers.Clear();
            modeEEnemyLootHandlers.Clear();
            modeEPendingScalingFactions.Clear();
            modeEScalingBatchTimer = 0f;
            modeETotalSpawnExpected = 0;
            modeESpawnResolved = 0;
            modeEDragonDescendantSpawned = false;
            modeEDragonKingSpawned = false;
            modeEWolfBossCount = 0;
            modeEWolfBossAssigned = 0;
            modeESpawnerRootRegisteredEnemies.Clear();
            modeEIntegrityTimer = 0f;

            if (clearSpawnAllocation)
            {
                modeESpawnAllocation = null;
                modeEFlattenedSpawnPoints = null;
                modeECachedSmokeVfxPrefab = null;
            }

            if (clearSpawnerCache)
            {
                modeECachedSpawnerPositions = null;
                modeECachedSpawnerSceneName = null;
            }

            if (stopWarmupCoroutine)
            {
                if (modeEStartupWarmupCoroutine != null)
                {
                    StopCoroutine(modeEStartupWarmupCoroutine);
                    modeEStartupWarmupCoroutine = null;
                }

                modeEStartupWarmupSceneName = null;
            }

            CleanupModeEVirtualSpawnerRoot();
            ClearPendingBossAggroQueue();
            ResetModeERespawnRuntimeState();
            ResetModeEFLootboxTrackerState();
        }

        internal bool IsModeESessionStillValid(int sessionToken, int relatedScene)
        {
            if (sessionToken <= 0)
            {
                return false;
            }

            return modeEActive &&
                   modeESessionToken == sessionToken &&
                   UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex == relatedScene;
        }

        internal bool IsModeEOrModeFSpawnSessionStillValid(
            int modeFSessionToken,
            int modeFRelatedScene,
            int modeESessionToken,
            int modeESessionRelatedScene)
        {
            if (modeFSessionToken > 0)
            {
                return IsModeFSessionStillValid(modeFSessionToken, modeFRelatedScene);
            }

            if (modeESessionToken > 0)
            {
                return IsModeESessionStillValid(modeESessionToken, modeESessionRelatedScene);
            }

            return modeEActive || modeFActive;
        }

        /// <summary>
        /// 在进入 Mode E 前预热重初始化逻辑，尽量把首帧卡顿摊到前置等待阶段。
        /// </summary>
        private void ScheduleModeEStartupWarmup(string reason)
        {
            try
            {
                string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (modeEStartupWarmupCoroutine != null && modeEStartupWarmupSceneName == sceneName)
                {
                    return;
                }

                if (modeEStartupWarmupCoroutine != null)
                {
                    StopCoroutine(modeEStartupWarmupCoroutine);
                    modeEStartupWarmupCoroutine = null;
                }

                modeEStartupWarmupSceneName = sceneName;
                modeEStartupWarmupCoroutine = StartCoroutine(PrepareModeEStartupCoroutine(sceneName, reason));
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] ScheduleModeEStartupWarmup failed: " + e.Message);
            }
        }

        private void ClearModeEStartupWarmupCoroutine(string sceneName)
        {
            if (modeEStartupWarmupSceneName == sceneName)
            {
                modeEStartupWarmupCoroutine = null;
            }
        }

        private void StopModeEStartupWarmupIfPending()
        {
            if (modeEStartupWarmupCoroutine != null)
            {
                StopCoroutine(modeEStartupWarmupCoroutine);
                modeEStartupWarmupCoroutine = null;
            }

            modeEStartupWarmupSceneName = null;
        }

        private bool TryRunModeEStartupWarmupStep(Action action, ModeEStartupProfiler profiler, string stageName, string errorContext, string sceneName)
        {
            try
            {
                action();
                profiler.Mark(stageName);
                return true;
            }
            catch (Exception e)
            {
                profiler.Complete("failed");
                DevLog("[ModeE] [ERROR] " + errorContext + " failed: " + e.Message);
                ClearModeEStartupWarmupCoroutine(sceneName);
                return false;
            }
        }

        /// <summary>
        /// 分帧预热 Mode E 入场所需的重缓存，未跑完时由正式启动流程继续兜底。
        /// </summary>
        private System.Collections.IEnumerator PrepareModeEStartupCoroutine(string sceneName, string reason)
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("PrepareModeEStartup", sceneName + ", " + reason);
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                InitializeModeDItemPools,
                profiler,
                "InitializeModeDItemPools",
                "PrepareModeEStartup.InitializeModeDItemPools",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                InitializeModeDEnemyPools,
                profiler,
                "InitializeModeDEnemyPools",
                "PrepareModeEStartup.InitializeModeDEnemyPools",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                BuildModeEFactionPresetCaches,
                profiler,
                "BuildModeEFactionPresetCaches",
                "PrepareModeEStartup.BuildModeEFactionPresetCaches",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                TryPrewarmModeDGlobalItemPool,
                profiler,
                "TryPrewarmModeDGlobalItemPool",
                "PrepareModeEStartup.TryPrewarmModeDGlobalItemPool",
                sceneName))
            {
                yield break;
            }
            yield return null;

            if (!TryRunModeEStartupWarmupStep(
                PreCacheMapSpawnerPositions,
                profiler,
                "PreCacheMapSpawnerPositions",
                "PrepareModeEStartup.PreCacheMapSpawnerPositions",
                sceneName))
            {
                yield break;
            }
            yield return null;

            yield return StartCoroutine(WarmModeEMerchantCachesAsync());
            profiler.Mark("WarmModeEMerchantCachesAsync");
            profiler.Complete();
            ClearModeEStartupWarmupCoroutine(sceneName);
        }

        /// <summary>
        /// 检测玩家背包中是否存在营旗物品
        /// 返回阵营和对应的 Item 引用；未找到则返回 (null, null)
        /// </summary>
        public (Teams? faction, Item flagItem) DetectFactionFlag()
        {
            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                if (main == null) return (null, null);

                Item characterItem = main.CharacterItem;
                if (characterItem == null) return (null, null);

                Inventory inventory = characterItem.Inventory;
                if (inventory == null || inventory.Content == null) return (null, null);

                // 遍历背包，匹配营旗 TypeID
                for (int i = 0; i < inventory.Content.Count; i++)
                {
                    Item item = inventory.Content[i];
                    if (item == null) continue;

                    int typeId = -1;
                    try { typeId = item.TypeID; } catch { continue; }

                    // 检查是否为随机营旗
                    if (modeERandomFlagTypeId > 0 && typeId == modeERandomFlagTypeId)
                    {
                        // 随机营旗：从可用阵营池中随机选择
                        Teams randomFaction = ModeEAvailableFactions[UnityEngine.Random.Range(0, ModeEAvailableFactions.Length)];
                        DevLog("[ModeE] 检测到随机营旗 (TypeID=" + typeId + ")，随机分配阵营: " + randomFaction);
                        return (randomFaction, item);
                    }

                    // 检查是否为指定阵营营旗
                    Teams assignedFaction;
                    if (modeEFactionFlagMap.TryGetValue(typeId, out assignedFaction))
                    {
                        DevLog("[ModeE] 检测到指定营旗 (TypeID=" + typeId + ")，阵营: " + assignedFaction);
                        return (assignedFaction, item);
                    }
                }

                return (null, null);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] DetectFactionFlag 失败: " + e.Message);
                return (null, null);
            }
        }

        /// <summary>
        /// 消耗（销毁）指定的营旗物品
        /// </summary>
        private bool TryConsumeModeEntryItem(Item item, string modeTag, string itemLabel)
        {
            if (item == null)
            {
                DevLog("[" + modeTag + "] [WARNING] " + itemLabel + " 为 null，跳过消耗");
                return false;
            }

            try
            {
                item.Detach();
                item.DestroyTree();
                DevLog("[" + modeTag + "] " + itemLabel + "已消耗");
                return true;
            }
            catch (Exception e)
            {
                DevLog("[" + modeTag + "] [WARNING] 消耗" + itemLabel + "失败: " + e.Message);
                return false;
            }
        }

        private bool ConsumeFactionFlag(Item flagItem)
        {
            return TryConsumeModeEntryItem(flagItem, "ModeE", "营旗");
        }

        private void ResetModeEStartupRecoveryState()
        {
            modeEStartupInventorySnapshot = null;
            modeEStartupFlagTypeId = -1;
            modeEStartupRecoveryArmed = false;
            modeEStartupFirstBossSpawned = false;
            modeEStartupHasPlayerPosition = false;
            modeEStartupPlayerPosition = Vector3.zero;
        }

        private void ArmModeEStartupRecovery(HashSet<int> startupInventorySnapshot, int flagTypeId, Vector3 playerPosition)
        {
            modeEStartupInventorySnapshot = startupInventorySnapshot != null
                ? new HashSet<int>(startupInventorySnapshot)
                : null;
            modeEStartupFlagTypeId = flagTypeId;
            modeEStartupRecoveryArmed = true;
            modeEStartupFirstBossSpawned = false;
            modeEStartupHasPlayerPosition = true;
            modeEStartupPlayerPosition = playerPosition;
        }

        private void DisarmModeEStartupRecovery(string reason)
        {
            if (!modeEStartupRecoveryArmed)
            {
                return;
            }

            if (!string.IsNullOrEmpty(reason))
            {
                DevLog("[ModeE] 启动验证通过，结束回滚监控: " + reason);
            }

            ResetModeEStartupRecoveryState();
        }

        private void MarkModeEStartupBossSpawned()
        {
            if (modeEStartupRecoveryArmed)
            {
                modeEStartupFirstBossSpawned = true;
            }
        }

        private bool CaptureModeEStartupInventorySnapshot(out HashSet<int> snapshot)
        {
            snapshot = new HashSet<int>();

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null)
                {
                    DevLog("[ModeE] [WARNING] 无法捕获启动前物资快照：玩家或 CharacterItem 为空");
                    snapshot = null;
                    return false;
                }

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = 0; i < inventory.Content.Count; i++)
                    {
                        Item item = inventory.Content[i];
                        if (item != null)
                        {
                            snapshot.Add(item.GetInstanceID());
                        }
                    }
                }

                if (characterItem.Slots != null)
                {
                    foreach (Slot slot in characterItem.Slots)
                    {
                        if (slot != null && slot.Content != null)
                        {
                            snapshot.Add(slot.Content.GetInstanceID());
                        }
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 捕获启动前物资快照失败: " + e.Message);
                snapshot = null;
                return false;
            }
        }

        private bool TryCaptureModeEStartupPlayerPosition(out Vector3 playerPosition)
        {
            playerPosition = Vector3.zero;

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] 无法记录启动前玩家位置：玩家或 transform 为空");
                    return false;
                }

                playerPosition = player.transform.position;
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 记录启动前玩家位置失败: " + e.Message);
                playerPosition = Vector3.zero;
                return false;
            }
        }

        private bool TryRestoreModeEStartupPlayerPosition(Vector3 playerPosition)
        {
            CharacterController cc = null;
            bool controllerWasEnabled = false;

            try
            {
                CharacterMainControl player = CharacterMainControl.Main;
                if (player == null || player.transform == null)
                {
                    DevLog("[ModeE] [WARNING] 恢复启动前玩家位置失败：玩家或 transform 为空");
                    return false;
                }

                cc = player.GetComponent<CharacterController>();
                if (cc != null)
                {
                    controllerWasEnabled = cc.enabled;
                    if (controllerWasEnabled)
                    {
                        cc.enabled = false;
                    }
                }

                try
                {
                    player.SetPosition(playerPosition);
                }
                catch (Exception setPositionEx)
                {
                    DevLog("[ModeE] [WARNING] 恢复玩家位置时 SetPosition 失败，改用 transform.position: " + setPositionEx.Message);
                    player.transform.position = playerPosition;
                }

                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 恢复启动前玩家位置失败: " + e.Message);
                return false;
            }
            finally
            {
                if (cc != null)
                {
                    try
                    {
                        cc.enabled = controllerWasEnabled;
                    }
                    catch (Exception e)
                    {
                        DevLog("[ModeE] [WARNING] 恢复玩家位置后还原 CharacterController 状态失败: " + e.Message);
                    }
                }
            }
        }

        private bool RollbackModeEStartupInventory(HashSet<int> startupInventorySnapshot)
        {
            if (startupInventorySnapshot == null)
            {
                DevLog("[ModeE] [WARNING] 启动物资回滚失败：快照为空");
                return false;
            }

            try
            {
                CharacterMainControl main = CharacterMainControl.Main;
                Item characterItem = main != null ? main.CharacterItem : null;
                if (characterItem == null)
                {
                    DevLog("[ModeE] [WARNING] 启动物资回滚失败：玩家或 CharacterItem 为空");
                    return false;
                }

                List<Item> rollbackItems = new List<Item>();
                HashSet<int> queuedItemIds = new HashSet<int>();

                Inventory inventory = characterItem.Inventory;
                if (inventory != null && inventory.Content != null)
                {
                    for (int i = inventory.Content.Count - 1; i >= 0; i--)
                    {
                        Item item = inventory.Content[i];
                        if (item == null)
                        {
                            continue;
                        }

                        int itemId = item.GetInstanceID();
                        if (!startupInventorySnapshot.Contains(itemId) && queuedItemIds.Add(itemId))
                        {
                            rollbackItems.Add(item);
                        }
                    }
                }

                if (characterItem.Slots != null)
                {
                    foreach (Slot slot in characterItem.Slots)
                    {
                        if (slot == null || slot.Content == null)
                        {
                            continue;
                        }

                        Item item = slot.Content;
                        int itemId = item.GetInstanceID();
                        if (!startupInventorySnapshot.Contains(itemId) && queuedItemIds.Add(itemId))
                        {
                            rollbackItems.Add(item);
                        }
                    }
                }

                bool rollbackSucceeded = true;
                int removedCount = 0;
                for (int i = 0; i < rollbackItems.Count; i++)
                {
                    Item item = rollbackItems[i];
                    if (item == null)
                    {
                        continue;
                    }

                    try
                    {
                        item.Detach();
                    }
                    catch (Exception e)
                    {
                        rollbackSucceeded = false;
                        DevLog("[ModeE] [WARNING] 回滚启动新增物品时 Detach 失败: " + e.Message);
                    }

                    try
                    {
                        item.DestroyTree();
                        removedCount++;
                    }
                    catch (Exception e)
                    {
                        rollbackSucceeded = false;
                        DevLog("[ModeE] [WARNING] 回滚启动新增物品时 DestroyTree 失败: " + e.Message);
                    }
                }

                DevLog("[ModeE] 启动失败时已回滚新增物品数量: " + removedCount);
                return rollbackSucceeded;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 启动物资回滚失败: " + e.Message);
                return false;
            }
        }

        private bool TryGetModeEStartupFlagTypeId(Item flagItem, out int typeId)
        {
            typeId = -1;
            if (flagItem == null)
            {
                DevLog("[ModeE] [WARNING] 启动前营旗引用为空，已取消启动");
                return false;
            }

            try
            {
                typeId = flagItem.TypeID;
                if (typeId > 0)
                {
                    return true;
                }

                DevLog("[ModeE] [WARNING] 启动前营旗 TypeID 非法: " + typeId);
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [WARNING] 读取营旗 TypeID 失败，已取消启动以避免吞旗: " + e.Message);
            }

            return false;
        }

        private bool TryRefundModeEStartupFlag(int typeId)
        {
            if (typeId <= 0)
            {
                return false;
            }

            bool refunded = TryGiveItemToPlayerOrDrop(typeId, L10n.T("营旗", "Faction Flag"), false);
            if (!refunded)
            {
                DevLog("[ModeE] [WARNING] 返还营旗失败: typeId=" + typeId);
            }

            return refunded;
        }

        private bool HandleModeEStartupFailureRecovery(string reason)
        {
            if (!modeEStartupRecoveryArmed)
            {
                DevLog("[ModeE] [WARNING] 启动失败，但未找到可用的回滚上下文: " + reason);
                return false;
            }

            HashSet<int> startupInventorySnapshot = modeEStartupInventorySnapshot != null
                ? new HashSet<int>(modeEStartupInventorySnapshot)
                : null;
            int consumedFlagTypeId = modeEStartupFlagTypeId;
            bool hasPlayerPosition = modeEStartupHasPlayerPosition;
            Vector3 startupPlayerPosition = modeEStartupPlayerPosition;

            DevLog("[ModeE] [WARNING] " + reason + "，开始回滚启动现场");
            StopModeEStartupWarmupIfPending();
            ResetModeEStartupRecoveryState();

            try
            {
                if (modeEActive)
                {
                    EndModeE(false);
                }
            }
            catch (Exception cleanupException)
            {
                DevLog("[ModeE] [WARNING] 启动失败后的 EndModeE 清理异常: " + cleanupException.Message);
            }

            bool restoredPlayerPosition = !hasPlayerPosition || TryRestoreModeEStartupPlayerPosition(startupPlayerPosition);
            bool rollbackSucceeded = RollbackModeEStartupInventory(startupInventorySnapshot);
            bool refunded = TryRefundModeEStartupFlag(consumedFlagTypeId);
            ShowModeEStartupFailureRecoveryMessage(rollbackSucceeded, refunded, restoredPlayerPosition);
            return false;
        }

        private void ShowModeEStartupFailureRecoveryMessage(bool rollbackSucceeded, bool refunded, bool restoredPlayerPosition)
        {
            string chineseMessage;
            string englishMessage;

            if (rollbackSucceeded && refunded && restoredPlayerPosition)
            {
                chineseMessage = "划地为营模式启动失败，已恢复玩家位置、回滚启动物资并返还营旗。";
                englishMessage = "Faction Battle start failed. Player position was restored, startup items were rolled back, and the faction flag was refunded.";
            }
            else
            {
                chineseMessage = "划地为营模式启动失败，已尝试恢复玩家位置、回滚启动物资并返还营旗；其中部分恢复失败，请查看日志。";
                englishMessage = "Faction Battle start failed. Player position restore, startup rollback, and faction flag refund were attempted, but some recovery steps failed. Check the log.";
            }

            ShowMessage(L10n.T(chineseMessage, englishMessage));
        }

        private System.Collections.IEnumerator WaitForModeEStartupVerification(Action<bool> onCompleted)
        {
            bool verified = false;

            try
            {
                if (!modeEStartupRecoveryArmed)
                {
                    yield break;
                }

                float deadline = Time.unscaledTime + MODEE_STARTUP_VERIFICATION_TIMEOUT_SECONDS;
                while (Time.unscaledTime < deadline)
                {
                    if (modeEStartupFirstBossSpawned || modeEAliveEnemies.Count > 0)
                    {
                        DisarmModeEStartupRecovery("已检测到首个成功生成的Boss");
                        verified = true;
                        break;
                    }

                    if (!modeEActive)
                    {
                        HandleModeEStartupFailureRecovery("Mode E 在启动验证阶段提前退出");
                        break;
                    }

                    yield return null;
                }

                if (!verified && modeEStartupRecoveryArmed)
                {
                    if (modeEStartupFirstBossSpawned || modeEAliveEnemies.Count > 0)
                    {
                        DisarmModeEStartupRecovery("超时前已检测到成功生成的Boss");
                        verified = true;
                    }
                    else
                    {
                        HandleModeEStartupFailureRecovery(
                            "Mode E 启动验证超时，未检测到任何成功生成的Boss (resolved="
                            + modeESpawnResolved + "/" + modeETotalSpawnExpected + ")");
                    }
                }
            }
            finally
            {
                onCompleted?.Invoke(verified);
            }
        }

        /// <summary>
        /// 检查并尝试启动 Mode E（在进入竞技场时调用）
        /// </summary>
        public bool TryStartModeE()
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("TryStartModeE");
            string profileStatus = "failed";
            int consumedFlagTypeId = -1;
            HashSet<int> startupInventorySnapshot = null;
            Vector3 startupPlayerPosition = Vector3.zero;
            try
            {
                ResetModeEStartupRecoveryState();

                // 互斥保护：Mode D 已激活时不启动 Mode E
                if (modeDActive)
                {
                    profileStatus = "skipped: ModeD active";
                    DevLog("[ModeE] Mode D 已激活，跳过 Mode E 启动");
                    return false;
                }

                // 检测营旗
                var (faction, flagItem) = DetectFactionFlag();
                profiler.Mark("DetectFactionFlag");
                if (!faction.HasValue || flagItem == null)
                {
                    profileStatus = "skipped: no faction flag";
                    DevLog("[ModeE] 未检测到营旗，不启动 Mode E");
                    return false;
                }

                // 检查裸装条件（复用 Mode D 的裸装检测）
                if (!IsPlayerNaked())
                {
                    profiler.Mark("IsPlayerNaked");
                    profileStatus = "skipped: player not naked";
                    DevLog("[ModeE] 玩家不满足裸装条件，拒绝启动");
                    ShowMessage(L10n.T(
                        "划地为营模式需要裸装入场！请清空所有装备后重试。",
                        "Faction Battle requires naked entry! Please remove all equipment."
                    ));
                    return false;
                }

                if (!TryCaptureModeEStartupPlayerPosition(out startupPlayerPosition))
                {
                    profileStatus = "failed: player position capture failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：无法记录玩家当前位置。",
                        "Faction Battle start failed: unable to capture the player's current position."
                    ));
                    return false;
                }

                if (!CaptureModeEStartupInventorySnapshot(out startupInventorySnapshot))
                {
                    profileStatus = "failed: snapshot capture failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：无法建立启动回滚快照。",
                        "Faction Battle start failed: unable to capture the startup rollback snapshot."
                    ));
                    return false;
                }

                // 消耗营旗
                profiler.Mark("IsPlayerNaked");
                if (!TryGetModeEStartupFlagTypeId(flagItem, out consumedFlagTypeId))
                {
                    profileStatus = "failed: flag type lookup failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：营旗数据异常，已取消消耗。",
                        "Faction Battle start failed: the faction flag data is invalid, so it was not consumed."
                    ));
                    return false;
                }
                if (!ConsumeFactionFlag(flagItem))
                {
                    profileStatus = "failed: flag consume failed";
                    ShowMessage(L10n.T(
                        "划地为营模式启动失败：营旗消耗异常。",
                        "Faction Battle start failed: unable to consume the faction flag."
                    ));
                    return false;
                }
                ArmModeEStartupRecovery(startupInventorySnapshot, consumedFlagTypeId, startupPlayerPosition);
                profiler.Mark("ConsumeFactionFlag");

                // 启动 Mode E
                bool started = StartModeE(faction.Value);
                profiler.Mark("StartModeE");
                if (!started)
                {
                    profileStatus = "failed: startup rejected after consume";
                    HandleModeEStartupFailureRecovery("StartModeE 返回失败");
                    return false;
                }
                profileStatus = "success";
                return true;
            }
            catch (Exception e)
            {
                DevLog("[ModeE] [ERROR] TryStartModeE 失败: " + e.Message);
                if (modeEStartupRecoveryArmed)
                {
                    HandleModeEStartupFailureRecovery("TryStartModeE 异常: " + e.Message);
                    profileStatus = "failed: exception recovery invoked";
                }
                return false;
            }
            finally
            {
                profiler.Complete(profileStatus);
            }
        }

        /// <summary>
        /// 启动 Mode E 模式
        /// </summary>
        private bool StartModeE(Teams faction)
        {
            ModeEStartupProfiler profiler = new ModeEStartupProfiler("StartModeE", faction.ToString());
            try
            {
                DevLog("[ModeE] 启动 Mode E 模式，阵营: " + faction);

                // 清理可能从无间炼狱残留的状态，避免 InfiniteHellCashMagnet/UI 提示误激活
                infiniteHellMode = false;
                infiniteHellWaveIndex = 0;
                infiniteHellCashPool = 0L;
                infiniteHellMilestoneRewardTier = 0;
                infiniteHellWaveCashThisWave = 0L;
                ClearCashMagnetState();

                modeEActive = true;
                int modeESessionToken = BeginModeESession();
                int relatedScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex;
                modeEPlayerLastHitKillCount = 0;
                RemoveModeEPlayerScalingModifiers();
                ResetModeESharedRuntimeState(clearSpawnAllocation: true, clearSpawnerCache: false, stopWarmupCoroutine: false);
                ResetModeEUiCaches();
                modeEPlayerFaction = faction;
                ClearEnemyRecoveryMonitorState();

                // 重置龙裔/龙王全局限制标记
                modeEDragonDescendantSpawned = false;
                modeEDragonKingSpawned = false;

                // 初始化各阵营死亡计数
                for (int i = 0; i < ModeEAvailableFactions.Length; i++)
                {
                    modeEFactionDeathCount[ModeEAvailableFactions[i]] = 0;
                }
                profiler.Mark("ResetState");

                // 设置玩家阵营
                CharacterMainControl player = CharacterMainControl.Main;
                if (player != null)
                {
                    // 爷的营旗：玩家保持 player 阵营，不需要 SetTeam
                    if (faction != Teams.player)
                    {
                        player.SetTeam(faction);
                        DevLog("[ModeE] 玩家阵营已设置为: " + faction);
                    }
                    else
                    {
                        DevLog("[ModeE] 爷的营旗：玩家保持 player 阵营，所有Boss均为敌对");
                    }
                }
                profiler.Mark("SetupPlayerFaction");

                EnsureModeEPlayerNameTag();
                profiler.Mark("SetupPlayerNameTag");

                // 显示阵营气泡
                ShowFactionBubble(faction);
                profiler.Mark("ShowFactionBubble");

                // 初始化物品池和敌人池（复用 Mode D 逻辑）
                InitializeModeDItemPools();
                profiler.Mark("InitializeModeDItemPools");
                EnsureModeEFSpawnPoolsReady("StartModeE");
                profiler.Mark("EnsureModeEFSpawnPoolsReady");

                // 前置构建全局掉落池（避免战斗中首次调用时卡顿）
                EnsureModeDGlobalItemPool();
                profiler.Mark("EnsureModeDGlobalItemPool");

                // Mode E 不激活 BossRush 运行时状态（IsActive 保持 false）
                // 仅订阅龙息Buff处理器，确保龙裔遗族Boss的龙息能触发龙焰灼烧
                DragonBreathBuffHandler.Subscribe();
                profiler.Mark("SubscribeDragonBreath");

                // 分配刷怪点给各阵营（优先使用地图配置的 Mode E 专用刷怪点，无配置时兜底使用原地图 spawner 位置）
                AllocateSpawnPoints();
                profiler.Mark("AllocateSpawnPoints");

                // 传送玩家到安全位置（远离Boss的安全点）
                TeleportPlayerToSafePosition();
                profiler.Mark("TeleportPlayerToSafePosition");

                // 发放初始装备（复用 Mode D 的 Starter Kit）
                GivePlayerStarterKit();

                // 零度挑战地图：额外发放保暖装备（头盔 ID:1312 + 护甲 ID:1307）
                ModeEGiveColdWeatherGear();

                // 独狼阵营：额外发放补给物品（3个id=881 + 3个id=660）
                if (faction == Teams.player)
                {
                    ModeEGiveLoneWolfSupplies();
                }
                profiler.Mark("GiveLoadout");

                CaptureModeEFLootboxBaseline();
                profiler.Mark("CaptureLootboxBaseline");

                // 一次性生成所有阵营的 Boss（UniTaskVoid fire-and-forget，抑制 CS4014 警告）
                #pragma warning disable CS4014
                ModeESpawnAllBosses(modeESessionToken: modeESessionToken, modeESessionRelatedScene: relatedScene);
                profiler.Mark("ScheduleBosses");
                #pragma warning restore CS4014

                // 生成神秘商人 NPC（fire-and-forget）
                #pragma warning disable CS4014
                SpawnModeEMerchant(modeESessionToken: modeESessionToken, modeESessionRelatedScene: relatedScene);
                profiler.Mark("ScheduleMerchant");
                #pragma warning restore CS4014

                // 在玩家出生点生成快递员阿稳（站在原地不移动）
                SpawnCourierNPC();
                profiler.Mark("SpawnCourier");

                // 应用变异词条（与标准 BossRush 共用同一套）
                TryRollMutatorsForMode("ModeE");
                profiler.Mark("RollMutators");

                ShowMessage(L10n.T(
                    "划地为营模式已激活！阵营：" + GetFactionDisplayName(faction),
                    "Faction Battle activated! Faction: " + faction.ToString()
                ));
                ShowBigBanner(L10n.T(
                    "欢迎来到 <color=red>划地为营</color>！",
                    "Welcome to <color=red>Faction Battle</color>!"
                ));
                profiler.Mark("ShowModeEUI");
                profiler.Complete("success");
                return true;
            }
            catch (Exception e)
            {
                profiler.Complete("failed");
                DevLog("[ModeE] [ERROR] StartModeE 失败: " + e.Message);
                try
                {
                    EndModeE(false);
                }
                catch (Exception cleanupException)
                {
                    DevLog("[ModeE] [WARNING] StartModeE 失败后的清理异常: " + cleanupException.Message);
                }
                return false;
            }
        }
    }
}
