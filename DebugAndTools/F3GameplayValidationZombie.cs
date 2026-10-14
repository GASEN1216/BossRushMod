using System;
using System.Collections;
using System.Diagnostics;
using Duckov.Economy;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class ZombieModeRuntimeModule
    {
        internal bool ValidationStartZombie(out string reason)
        {
            reason = null;
            if (!ModBehaviour.DevModeEnabled) { reason = "dev_build_required"; return false; }
            try
            {
                // 已在目标地图的专用用例省去过图；现金取公开零投入路线，费用/转存/选流派均用生产入口。
                if (!TryBeginMapSelectionShell()) { reason = "map_entry_rejected"; return false; }
                string cashReason;
                ZombieModeFailureReason entryReason;
                if (!ConfigureZombieModePendingCashInvestment(0L, out cashReason)
                    || !TryRunZombieModePrechecks(out entryReason))
                { reason = cashReason ?? "entry_precheck_failed"; return false; }
                if (!CommitZombieModeEntryResourcesShell(out entryReason))
                { reason = "entry_commit_failed:" + entryReason; FailZombieModeBeforeActive(entryReason); return false; }
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                int runId = BeginZombieModeRunShell(scene.buildIndex, scene.name);
                runState.LifecyclePhase = ZombieModeLifecyclePhase.InitializingRun;
                runState.CombatPhase = ZombieModeCombatPhase.None;
                runState.PurificationPoints = 0;
                if (!InitializeZombieModeRunAfterMapLoaded(runId))
                { reason = "initialize_failed"; FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed); return false; }
                SelectZombieModeStarterLoadout(runId, ZombieModeStarterLoadout.Gunner);
                bool started = coopZombieStarterChosen || !BossRushCoop.IsConnected && IsZombieModeActive;
                if (!started) reason = "starter_choice_not_committed";
                return started;
            }
            catch (Exception e)
            {
                reason = e.GetType().Name + ":" + e.Message;
                try { FailZombieModeBeforeActive(ZombieModeFailureReason.InitializationFailed); }
                catch (Exception cleanupError) { ModBehaviour.DevLog("[Validation] Zombie 失败回滚异常: " + cleanupError.Message); }
                return false;
            }
        }

        // 原生联机使用生产全员同圈协程和异步个人收据；钱包必须在这些步骤完成后比较。
        internal IEnumerator ValidationCompleteZombieExtraction(Action<bool, string, string> completed)
        {
            if (!ModBehaviour.DevModeEnabled || !IsZombieModeActive)
            { completed(false, string.Empty, "zombie_not_active"); yield break; }
            int runId = runState.RunId;
            string asset = BossRushCoop.LocalAssetId;
            int slot = Saves.SavesSystem.CurrentSlot;
            if (BossRushCoop.IsConnected && (!IsCoopZombieLocalAssetCurrent() || !coopZombieStarterChosen))
            { completed(false, string.Empty, "local_starter_unconfirmed"); yield break; }
            int pointsBeforePickup = runState.PurificationPoints;
            CollectZombieModePurificationPoint(runId, 3, null, null);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (runState.PurificationPoints != pointsBeforePickup + 3 && Time.realtimeSinceStartup < deadline
                && runState.RunId == runId && asset == BossRushCoop.LocalAssetId && slot == Saves.SavesSystem.CurrentSlot) yield return null;
            if (runState.PurificationPoints != pointsBeforePickup + 3)
            { completed(false, string.Empty, "purification_receipt_not_applied"); yield break; }
            if (asset != BossRushCoop.LocalAssetId || slot != Saves.SavesSystem.CurrentSlot)
            { completed(false, string.Empty, "settlement_asset_changed"); yield break; }
            BeginZombieModeExtractionOpportunity(runId);
            EnsureZombieModeExtractionArea(runId);
            CountDownArea area = runState.ActiveExtractionArea;
            if (area == null || CharacterMainControl.Main == null)
            { completed(false, string.Empty, "extraction_area_or_player_missing"); yield break; }
            CharacterMainControl.Main.SetPosition(area.transform.position);
            deadline = Time.realtimeSinceStartup + 30f;
            while (!CoopZombieAllInsideExtraction(area) && Time.realtimeSinceStartup < deadline
                && IsZombieModeRunValid(runId) && asset == BossRushCoop.LocalAssetId && slot == Saves.SavesSystem.CurrentSlot) yield return null;
            if (!CoopZombieAllInsideExtraction(area))
            { completed(false, string.Empty, "living_participants_not_inside_extraction"); yield break; }
            if (asset != BossRushCoop.LocalAssetId || slot != Saves.SavesSystem.CurrentSlot)
            { completed(false, string.Empty, "settlement_asset_changed"); yield break; }
            string startReason = StartZombieModeExtractionFromUi(runId);
            if (!string.IsNullOrEmpty(startReason) || !runState.ExtractionChanneling)
            { completed(false, string.Empty, startReason ?? "extraction_channel_missing"); yield break; }
            long points = runState.PurificationPoints;
            long before = EconomyManager.Money;
            var succeed = area.onCountDownSucceed;
            if (!BossRushCoop.IsConnected)
            {
                if (succeed == null) { completed(false, string.Empty, "extraction_event_missing"); yield break; }
                succeed.Invoke();
            }
            deadline = Time.realtimeSinceStartup + 60f;
            while (IsZombieModeActive && Time.realtimeSinceStartup < deadline
                && asset == BossRushCoop.LocalAssetId && slot == Saves.SavesSystem.CurrentSlot) yield return null;
            if (asset != BossRushCoop.LocalAssetId || slot != Saves.SavesSystem.CurrentSlot)
            { completed(false, string.Empty, "settlement_asset_changed"); yield break; }
            long first = EconomyManager.Money;
            // 单机重复同一官方事件；联机监听已停用，重发收据的覆盖来自网络执行回归。
            if (!BossRushCoop.IsConnected && succeed != null) succeed.Invoke();
            yield return null;
            if (asset != BossRushCoop.LocalAssetId || slot != Saves.SavesSystem.CurrentSlot)
            { completed(false, string.Empty, "settlement_asset_changed"); yield break; }
            long second = EconomyManager.Money;
            bool settled = points > 0 && first - before == points && second == first && !IsZombieModeActive;
            string metrics = "pickup=" + pointsBeforePickup + "->" + points + ",points=" + points
                + ",cash=" + before + "->" + first + "->" + second + ",active=" + IsZombieModeActive
                + ",replay_scope=" + (BossRushCoop.IsConnected ? "l2_personal_receipt" : "official_event");
            completed(settled, metrics, settled ? null : "extraction_settlement_or_idempotency_failed");
        }
    }

    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunZombieExtraction()
        {
            Stopwatch sw = Stopwatch.StartNew();
            string reason;
            string metrics = string.Empty;
            try
            {
                if (!PrepareZombieValidationEntry(out reason) || !StartZombieValidationEntry(out reason))
                { Record("MODE_ZOMBIE_EXTRACTION", "FAIL", sw.ElapsedMilliseconds, metrics, reason); yield break; }
                yield return WaitSeconds(6f);
                if (ShouldAbort())
                { Record("MODE_ZOMBIE_EXTRACTION", "SKIP", sw.ElapsedMilliseconds, metrics, DescribeAbortReason()); yield break; }
                bool settled = false;
                yield return _host.ValidationCompleteZombieExtraction(delegate(bool ok, string collected, string error)
                { settled = ok; metrics = collected; reason = error; });
                float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
                while (settled && !IsRuntimeReady(BaseSceneNameForValidation()) && !ShouldAbort()
                    && Time.realtimeSinceStartup < deadline)
                {
#if BOSSRUSH_DEV
                    TryClickAutotestClosureContinue();
#endif
                    yield return null;
                }
                bool ready = IsRuntimeReady(BaseSceneNameForValidation());
                Record("MODE_ZOMBIE_EXTRACTION", settled && ready ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    metrics + ",base_ready=" + ready, reason ?? (ready ? null : "extraction_base_not_ready"));
            }
            finally { try { _host.ValidationSafeCleanup(); } finally { RestoreModeDValidationEntry(); } }
        }
    }
}
