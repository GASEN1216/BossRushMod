using System;
using System.Collections.Generic;
using ItemStatsSystem;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        private ModeGEntryRuntime modeGEntryRuntime;
        private bool modeGActive { get { return modeGEntryRuntime != null && modeGEntryRuntime.IsRunActiveForHost; } }
        private ModeGRuntimeModule modeGRuntime { get { return modeGEntryRuntime != null ? modeGEntryRuntime.CurrentRunForHost : null; } }

        private ModeGEntryRuntime GetModeGEntryRuntimeForHost()
        {
            if (modeGEntryRuntime != null) return modeGEntryRuntime;
            ModeGEntryRuntime runtime = new ModeGEntryRuntime();
            runtime.BindEntryQueries(
                () => IsActive,
                () => modeDActive,
                () => modeEActive,
                () => modeFActive,
                () => IsZombieModeActive,
                () => config != null ? config.modeGAbandonHotkey : 0);
            runtime.BindEntryServices(
                DetectBossRushTicketItem,
                DetectFactionFlag,
                DetectBloodhuntTransponder,
                TryConsumeModeEntryItem,
                GetBossRushTicketTypeId,
                ShowMessage,
                ShowBigBanner);
            runtime.BindBossPoolQueries(
                InitializeEnemyPresets,
                InitializeBossPoolFilter,
                EnsureCharacterPresetsCacheReady,
                GetFilteredEnemyPresets,
                () => cachedCharacterPresets,
                IsDragonDescendantPreset,
                IsDragonKingPreset,
                IsPhantomWitchPreset,
                IsManagedBossPreset,
                BuildGeneralBossLootCandidateIdSet);
            runtime.BindSignatureQueries(
                FindQuestionMarkPreset,
                FindFallbackPreset,
                FindDragonKingBasePreset,
                FindPhantomWitchBasePreset);
            runtime.BindSpawnServices(
                SpawnEnemyCoreInternalAsync,
                PrepareManagedDragonDescendantAsync,
                PrepareManagedDragonKingAsync,
                PrepareManagedPhantomWitchAsync,
                ActivateModeGManagedCharacter,
                CleanupModeGManagedCharacter);
            runtime.BindArenaServices(
                GetCurrentSceneSpawnPoints,
                SetCurrentMapSpawnPoints,
                InitializeItemValueCacheAsync,
                TryCreateArenaDifficultyEntryPoint,
                PreCacheMapSpawnerPositions,
                DisableAllSpawners,
                ClearEnemiesForBossRush,
                () => spawnersDisabled,
                value => spawnersDisabled = value,
                value => bossRushArenaActive = value,
                value => bossRushArenaPlanned = value);
            modeGEntryRuntime = runtime;
            return runtime;
        }

        internal void SetModeGSelectedContractId(int contractId) { GetModeGEntryRuntimeForHost().SetModeGSelectedContractId(contractId); }
        private Item DetectFateEchoRelic() { return GetModeGEntryRuntimeForHost().DetectFateEchoRelic(); }
        private bool IsModeGLoadoutEligible() { return GetModeGEntryRuntimeForHost().IsModeGLoadoutEligible(); }
        public ModeGEntryPreview GetOrCreateModeGEntryPreview() { return GetModeGEntryRuntimeForHost().GetOrCreateModeGEntryPreview(); }
        internal bool IsModeGEntryPreviewValidForCurrentScene(ModeGEntryPreview preview) { return GetModeGEntryRuntimeForHost().IsModeGEntryPreviewValidForCurrentScene(preview); }
        public bool TryStartModeG() { return GetModeGEntryRuntimeForHost().TryStartModeG(); }
        internal bool TryRefundModeGPendingPrepaidTicket() { return GetModeGEntryRuntimeForHost().TryRefundModeGPendingPrepaidTicket(); }
        internal void RollbackModeGStagedArenaEntry() { GetModeGEntryRuntimeForHost().RollbackModeGStagedArenaEntry(); }
        internal bool TryRefundModeGStartupItem(int typeId, string displayName) { return GetModeGEntryRuntimeForHost().TryRefundModeGStartupItem(typeId, displayName); }
        internal int GetModeGTicketTypeId() { return GetModeGEntryRuntimeForHost().GetModeGTicketTypeId(); }
        private bool StartModeGRuntime(ModeGEntryPreview preview, bool refundTicketOnStartupFailure,
            bool refundRelicOnStartupFailure, out bool startupRefundOwnedByRuntime) { return GetModeGEntryRuntimeForHost().StartModeGRuntime(preview, refundTicketOnStartupFailure, refundRelicOnStartupFailure, out startupRefundOwnedByRuntime); }
        private void UpdateModeG(float deltaTime) { if (modeGEntryRuntime != null) modeGEntryRuntime.UpdateModeG(deltaTime); }
        private void ShutdownModeG() { if (modeGEntryRuntime != null) modeGEntryRuntime.ShutdownModeG(); }
        internal ModeGBossSnapshot CreateModeGBossSnapshot() { return GetModeGEntryRuntimeForHost().CreateModeGBossSnapshot(); }
        internal UniTask<ManagedBossPrepareResult> SpawnModeGManagedBossAsync(
            EnemyPresetInfo info, Vector3 position, int waveNumber, ManagedBossSpawnContext ctx) { return GetModeGEntryRuntimeForHost().SpawnModeGManagedBossAsync(info, position, waveNumber, ctx); }
        internal Vector3[] GetModeGSpawnPositions(int waveIndex, int count, ModeGPlanVariant variant,
            ModeGNemesisTemperament temperament, bool isNemesisWave) { return GetModeGEntryRuntimeForHost().GetModeGSpawnPositions(waveIndex, count, variant, temperament, isNemesisWave); }
        internal bool PrepareModeGArenaRuntime(ModeGEntryPreview preview) { return GetModeGEntryRuntimeForHost().PrepareModeGArenaRuntime(preview); }
        internal bool CommitModeGArenaEntry(ModeGEntryPreview preview) { return GetModeGEntryRuntimeForHost().CommitModeGArenaEntry(preview); }
        internal void ShowModeGWaveBanner(int waveIndex, ModeGWavePlan.WaveSlot wave,
            ModeGCounterAxis axis, ModeGNemesisTemperament temperament) { GetModeGEntryRuntimeForHost().ShowModeGWaveBanner(waveIndex, wave, axis, temperament); }
        internal UniTask<ManagedBossPrepareResult> SpawnModeGOfficialBossAsync(
            EnemyPresetInfo preset,
            Vector3 position,
            int waveNumber,
            Func<EnemySpawnContext, bool> onCommit) { return GetModeGEntryRuntimeForHost().SpawnModeGOfficialBossAsync(preset, position, waveNumber, onCommit); }
        internal List<string> GetModeGOfficialBossPoolKeys() { return GetModeGEntryRuntimeForHost().GetModeGOfficialBossPoolKeys(); }
        internal EnemyPresetInfo FindModeGOfficialPresetByKey(string key) { return GetModeGEntryRuntimeForHost().FindModeGOfficialPresetByKey(key); }
        internal List<ModeGRewardCandidate> GetModeGRewardCandidates() { return GetModeGEntryRuntimeForHost().GetModeGRewardCandidates(); }
        internal UniTask<ManagedBossPrepareResult> DispatchModeGManagedBossSpawnAsync(
            EnemyPresetInfo preset, Vector3 position, object managedContext, bool deferActivationUntilNextFrame) { return GetModeGEntryRuntimeForHost().DispatchModeGManagedBossSpawnAsync(preset, position, managedContext, deferActivationUntilNextFrame); }
    }
}
