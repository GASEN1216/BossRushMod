using System;
using System.Collections;
using System.Collections.Generic;
using Duckov.Utilities;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Data;
using ItemStatsSystem.Items;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    // 目标地图入场等待：从 ZombieModeRuntimeModule.cs 原样拆出（LargeFileBudgetGuard 1200 行上限），行为逐字不变。
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal System.Collections.IEnumerator WaitForZombieModeTargetSceneActiveThenInitialize(Scene scene, Vector3? customPos)
        {
            int expectedEntryAttemptId = entryAttemptId;
            ZombieModeRunState expectedRunState = runState;
            ZombieModeEntryTransaction expectedTransaction = entryTransaction;
            ModBehaviour expectedOwner = owner;
            int expectedRunId = expectedRunState != null ? expectedRunState.RunId : 0;
            const float maxWait = 30f;
            const float interval = 0.1f;
            float elapsed = 0f;
            int attempt = 0;

            while (elapsed < maxWait)
            {
                if (!IsZombieModeTargetSceneEntryCurrent(expectedEntryAttemptId, expectedRunState,
                    expectedTransaction, expectedOwner, expectedRunId, scene.name))
                {
                    yield break;
                }

                attempt++;
                Scene activeScene = SceneManager.GetActiveScene();
                bool sceneLoaded = scene.isLoaded;
                bool activeMatches = activeScene.name == scene.name && activeScene.handle == scene.handle;
                bool sceneLoaderDone = owner.ReadSceneLoaderDoneWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
                bool levelInited = owner.ReadLevelInitedWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
                // 官方 InitLevel 置 levelInited 后还会等 0.25 秒再 SetPosition(startPos)、置 afterInit；
                // 在这之前初始化会拿到未定位的玩家，失败回基地还会让官方 SetPosition 打到已销毁的角色。
                bool levelAfterInit = IsOfficialLevelAfterInitForZombieMode();

                if (sceneLoaded && activeMatches && sceneLoaderDone && levelInited && levelAfterInit)
                {
                    break;
                }

                if (attempt % 10 == 0)
                {
                    ModBehaviour.DevLog("[ZombieMode] 等待目标地图激活: target=" + scene.name
                        + ", active=" + activeScene.name
                        + ", sceneLoaded=" + sceneLoaded
                        + ", sceneLoaderDone=" + sceneLoaderDone
                        + ", levelInited=" + levelInited
                        + ", afterInit=" + levelAfterInit
                        + ", elapsed=" + elapsed + "s");
                }

                yield return new WaitForSeconds(interval);
                elapsed += interval;
            }

            if (!IsZombieModeTargetSceneEntryCurrent(expectedEntryAttemptId, expectedRunState,
                expectedTransaction, expectedOwner, expectedRunId, scene.name))
            {
                yield break;
            }

            Scene finalActiveScene = SceneManager.GetActiveScene();
            // 等待达到时限不代表官方初始化已经完成；最后一次 yield 后可能刚好变为 ready。
            bool finalSceneLoaderDone = owner.ReadSceneLoaderDoneWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
            bool finalLevelInited = owner.ReadLevelInitedWithWarningForRuntimeModule("ZombieModeTargetSceneInitialize");
            bool finalLevelAfterInit = IsOfficialLevelAfterInitForZombieMode();
            if (!IsZombieModeTargetSceneEntryCurrent(expectedEntryAttemptId, expectedRunState,
                expectedTransaction, expectedOwner, expectedRunId, scene.name))
            {
                yield break;
            }

            if (!scene.isLoaded || finalActiveScene.name != scene.name || finalActiveScene.handle != scene.handle ||
                !finalSceneLoaderDone || !finalLevelInited || !finalLevelAfterInit)
            {
                ModBehaviour.DevLog("[ZombieMode] 初始化失败：目标地图或官方初始化未就绪, target=" + scene.name
                    + ", active=" + finalActiveScene.name + ", sceneLoaderDone=" + finalSceneLoaderDone
                    + ", levelInited=" + finalLevelInited + ", afterInit=" + finalLevelAfterInit);
                ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            int runId = BeginZombieModeRunShell(scene.buildIndex, scene.name);
            // 状态机推进：LoadingMap → InitializingRun（WaitingStarterChoice 由 InitializeZombieModeRunAfterMapLoaded 末尾设置）
            runState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
            runState.CombatPhase = ZombieModeCombatPhase.None;
            runState.PurificationPoints = InitialPurificationPoints;
            if (runState.ConfirmedCashInvested > 0L)
            {
                // 100 现金 = 1 净化点数（向下取整）
                runState.PurificationPoints = (int)System.Math.Min(
                    int.MaxValue,
                    runState.ConfirmedCashInvested / ZombieModeTuning.CashToPurificationRatio);
            }

            ZombieModeMapSelectionHelper.ClearPendingZombieEntry();
            if (!InitializeZombieModeRunAfterMapLoaded(runId))
            {
                FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed);
                yield break;
            }

            ModBehaviour.DevLog("[ZombieMode] 已进入目标地图: " + scene.name + "，等待初始流派选择");
        }

        private bool IsZombieModeTargetSceneEntryCurrent(int expectedEntryAttemptId, ZombieModeRunState expectedRunState,
            ZombieModeEntryTransaction expectedTransaction, ModBehaviour expectedOwner, int expectedRunId, string targetSceneName)
        {
            // 旧等待只能结束自身，不能清掉新一轮入场的待处理标记、资源或运行状态。
            return owner != null && ReferenceEquals(owner, expectedOwner) &&
                   entryAttemptId == expectedEntryAttemptId && ReferenceEquals(runState, expectedRunState) &&
                   ReferenceEquals(entryTransaction, expectedTransaction) && runState != null &&
                   runState.RunId == expectedRunId && !runState.IsCleaningUp && pendingEntry &&
                   runState.LifecyclePhase == ZombieModeLifecyclePhase.LoadingMap &&
                   ZombieModeMapSelectionHelper.HasPendingZombieEntry &&
                   ZombieModeMapSelectionHelper.GetPendingTargetSubSceneName() == targetSceneName;
        }
    }
}
