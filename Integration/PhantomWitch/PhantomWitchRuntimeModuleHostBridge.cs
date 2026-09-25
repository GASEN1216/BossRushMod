using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class PhantomWitchRuntimeModule
    {
        private ModBehaviour owner;

        public override string ModuleName { get { return "PhantomWitch"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            if (owner != null) owner.AttachPhantomWitchRuntimeModule(this);
        }

        public override void OnDestroy()
        {
            CleanupPhantomWitchTrackedStateOnArenaExit();
            PhantomWitchRuntimeModule.ResetPhantomWitchRuntimeModuleStaticCaches();
            ModBehaviour currentOwner = owner;
            owner = null;
            if (currentOwner != null) currentOwner.DetachPhantomWitchRuntimeModule(this);
        }

        private MonoBehaviour currentBoss
        {
            get { return owner != null ? owner.PhantomWitchCurrentBoss : null; }
            set { if (owner != null) owner.PhantomWitchCurrentBoss = value; }
        }

        private List<MonoBehaviour> currentWaveBosses
        {
            get { return owner != null ? owner.PhantomWitchCurrentWaveBosses : null; }
        }

        private int bossesPerWave { get { return owner != null ? owner.PhantomWitchBossesPerWave : 0; } }
        private List<EnemyPresetInfo> enemyPresets { get { return owner != null ? owner.PhantomWitchEnemyPresets : null; } }
        private Dictionary<CharacterMainControl, float> bossSpawnTimes
        {
            get { return owner != null ? owner.PhantomWitchBossSpawnTimes : null; }
        }

        private Coroutine StartCoroutine(IEnumerator routine)
        {
            return owner != null && routine != null ? owner.StartCoroutine(routine) : null;
        }

        private IEnumerator DelayedBossPositionValidation(CharacterMainControl boss, float delay)
        {
            return owner != null ? owner.DelayedBossPositionValidationForBossModule(boss, delay) : null;
        }

        private void ShowMessage(string message)
        {
            if (owner != null) owner.ShowMessage(message);
        }

        private void RefreshEquipmentModels(CharacterMainControl character)
        {
            if (owner != null) owner.RefreshDragonDescendantSharedEquipmentModels(character);
        }

        private void ApplyBossStatMultiplier(CharacterMainControl character)
        {
            if (owner != null) owner.ApplyPhantomWitchBossStatMultiplier(character);
        }

        private void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        {
            if (owner != null) owner.RegisterPhantomWitchEnemyRecoveryAnchor(enemy, anchorPosition);
        }

        private void SetupAIAggro(CharacterMainControl character)
        {
            if (owner != null) owner.SetupAIAggro(character);
        }

        private void RegisterBossRandomLootTracking(CharacterMainControl character,
            int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            if (owner != null)
                owner.RegisterPhantomWitchBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }

        private void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            if (owner != null) owner.ClearPhantomWitchBossRandomLootTracking(character);
        }

        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (owner != null) owner.FinalizePhantomWitchBossRushLootboxPathTracking(character);
        }

        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            if (owner != null) owner.NotifyPhantomWitchBossSpawnFailed(preset);
        }

        private UniTask<CharacterMainControl> CreateModeGManagedCharacterAsync(
            CharacterRandomPreset preset, Vector3 position, ManagedBossSpawnContext context,
            string runtimeNameKey, string runtimePresetName)
        {
            return owner != null
                ? owner.CreateModeGManagedCharacterAsync(preset, position, context, runtimeNameKey, runtimePresetName)
                : UniTask.FromResult<CharacterMainControl>(null);
        }

        private static bool IsManagedOwnerValid(ManagedBossSpawnContext context)
        {
            return ModBehaviour.IsManagedOwnerValid(context);
        }

        private void BeginActivateModeGManagedCharacter(CharacterMainControl character)
        {
            if (owner != null) owner.BeginActivateModeGManagedCharacter(character);
        }

        private void CompleteActivateModeGManagedCharacter(CharacterMainControl character)
        {
            if (owner != null) owner.CompleteActivateModeGManagedCharacter(character);
        }

        private void CleanupModeGManagedCharacter(CharacterMainControl character,
            string runtimeNameKey, string runtimePresetName, string logTag)
        {
            if (owner != null)
                owner.CleanupModeGManagedCharacter(character, runtimeNameKey, runtimePresetName, logTag);
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }

    /// <summary>保留幽灵女巫旧入口，并将实例逻辑转交唯一运行模块。</summary>
    public partial class ModBehaviour
    {
        private PhantomWitchRuntimeModule phantomWitchRuntimeModule;

        internal void AttachPhantomWitchRuntimeModule(PhantomWitchRuntimeModule module)
        {
            phantomWitchRuntimeModule = module;
        }

        internal void DetachPhantomWitchRuntimeModule(PhantomWitchRuntimeModule module)
        {
            if (ReferenceEquals(phantomWitchRuntimeModule, module)) phantomWitchRuntimeModule = null;
        }

        internal MonoBehaviour PhantomWitchCurrentBoss
        {
            get { return currentBoss; }
            set { currentBoss = value; }
        }

        internal List<MonoBehaviour> PhantomWitchCurrentWaveBosses { get { return currentWaveBosses; } }
        internal int PhantomWitchBossesPerWave { get { return bossesPerWave; } }
        internal List<EnemyPresetInfo> PhantomWitchEnemyPresets { get { return enemyPresets; } }
        internal Dictionary<CharacterMainControl, float> PhantomWitchBossSpawnTimes { get { return bossSpawnTimes; } }

        internal void ApplyPhantomWitchBossStatMultiplier(CharacterMainControl character)
        {
            ApplyBossStatMultiplier(character);
        }

        internal void RegisterPhantomWitchEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        {
            RegisterEnemyRecoveryAnchor(enemy, anchorPosition);
        }

        internal void RegisterPhantomWitchBossRandomLootTracking(
            CharacterMainControl character, int originalLootCount, float spawnTimeOffset)
        {
            RegisterBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }

        internal void ClearPhantomWitchBossRandomLootTracking(CharacterMainControl character)
        {
            ClearBossRandomLootTracking(character);
        }

        internal void FinalizePhantomWitchBossRushLootboxPathTracking(CharacterMainControl character)
        {
            FinalizeBossRushLootboxPathTracking(character);
        }

        internal void NotifyPhantomWitchBossSpawnFailed(EnemyPresetInfo preset)
        {
            OnBossSpawnFailed(preset);
        }

        public async UniTask<CharacterMainControl> SpawnPhantomWitch(
            Vector3 position,
            bool notifyBossRushOnFailure = true,
            bool deferActivationUntilNextFrame = false,
            PhantomWitchDeathPresentation deathPresentation = PhantomWitchDeathPresentation.Standard,
            float extraModelScale = 1f,
            bool isNonWaveSpawn = false,
            Func<bool> isActiveCheck = null)
        {
            return phantomWitchRuntimeModule != null
                ? await phantomWitchRuntimeModule.SpawnPhantomWitch(position, notifyBossRushOnFailure,
                    deferActivationUntilNextFrame, deathPresentation, extraModelScale, isNonWaveSpawn, isActiveCheck)
                : null;
        }

        public static void ClearPhantomWitchStaticCache()
        {
            PhantomWitchRuntimeModule.ClearPhantomWitchStaticCache();
        }

        public static void ReleasePhantomWitchInstance()
        {
            PhantomWitchRuntimeModule.ReleasePhantomWitchInstance();
        }

        private void CleanupPhantomWitchTrackedStateOnArenaExit()
        {
            if (phantomWitchRuntimeModule != null)
                phantomWitchRuntimeModule.CleanupPhantomWitchTrackedStateOnArenaExit();
        }

        private bool IsPhantomWitchPreset(EnemyPresetInfo preset)
        {
            return phantomWitchRuntimeModule != null && phantomWitchRuntimeModule.IsPhantomWitchPreset(preset);
        }

        private void RegisterPhantomWitchPreset()
        {
            if (phantomWitchRuntimeModule != null) phantomWitchRuntimeModule.RegisterPhantomWitchPreset();
        }

        private CharacterRandomPreset FindPhantomWitchBasePreset()
        {
            return phantomWitchRuntimeModule != null
                ? phantomWitchRuntimeModule.FindPhantomWitchBasePreset() : null;
        }

        private void CleanupFailedPhantomWitchSpawn(CharacterMainControl character)
        {
            if (phantomWitchRuntimeModule != null)
                phantomWitchRuntimeModule.CleanupFailedPhantomWitchSpawn(character);
        }

        private bool IsManagedBossPreset(EnemyPresetInfo preset)
        {
            return IsDragonDescendantPreset(preset)
                || IsDragonKingPreset(preset)
                || IsPhantomWitchPreset(preset);
        }

        internal async UniTask<ManagedBossPrepareResult> PrepareManagedPhantomWitchAsync(
            Vector3 position, ManagedBossSpawnContext context)
        {
            return phantomWitchRuntimeModule != null
                ? await phantomWitchRuntimeModule.PrepareManagedPhantomWitchAsync(position, context)
                : null;
        }
        private void InitializePhantomWitchScytheSystem()
        {
            phantomWitchRuntimeModule.InitializePhantomWitchScytheSystem();
        }

        private void SetupPhantomWitchScytheForScene(UnityEngine.SceneManagement.Scene scene)
        {
            phantomWitchRuntimeModule.SetupPhantomWitchScytheForScene(scene);
        }

        private void CleanupPhantomWitchScytheSystem()
        {
            phantomWitchRuntimeModule.CleanupPhantomWitchScytheSystem();
        }

        internal static WaitForSeconds PhantomWitchScytheSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }

    }
}
