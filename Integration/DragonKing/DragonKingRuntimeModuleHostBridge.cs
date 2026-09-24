using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DragonKingRuntimeModule
    {
        private ModBehaviour owner;

        public override string ModuleName { get { return "DragonKing"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            if (owner != null) owner.AttachDragonKingRuntimeModule(this);
        }

        public override void OnDestroy()
        {
            CleanupTrackedDragonKingsOnArenaExit();
            DragonKingRuntimeModule.ResetDragonKingRuntimeModuleStaticCaches();
            if (dragonKingSetBonusRegistered && owner != null)
                Health.OnHurt -= owner.OnDragonKingBossHurt;
            dragonKingSetBonusRegistered = false;
            activeDragonKingHealths.Clear();

            ModBehaviour currentOwner = owner;
            owner = null;
            if (currentOwner != null) currentOwner.DetachDragonKingRuntimeModule(this);
        }

        internal bool DragonKingSetBonusRegistered
        {
            get { return dragonKingSetBonusRegistered; }
            set { dragonKingSetBonusRegistered = value; }
        }

        internal HashSet<Health> ActiveDragonKingHealths { get { return activeDragonKingHealths; } }

        private MonoBehaviour currentBoss
        {
            get { return owner != null ? owner.DragonKingCurrentBoss : null; }
            set { if (owner != null) owner.DragonKingCurrentBoss = value; }
        }

        private List<MonoBehaviour> currentWaveBosses
        {
            get { return owner != null ? owner.DragonKingCurrentWaveBosses : null; }
        }

        private int bossesPerWave { get { return owner != null ? owner.DragonKingBossesPerWave : 0; } }
        private List<EnemyPresetInfo> enemyPresets { get { return owner != null ? owner.DragonKingEnemyPresets : null; } }
        private Dictionary<CharacterMainControl, float> bossSpawnTimes { get { return owner != null ? owner.DragonKingBossSpawnTimes : null; } }
        private Dictionary<CharacterMainControl, int> bossOriginalLootCounts { get { return owner != null ? owner.DragonKingBossOriginalLootCounts : null; } }

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

        private Item FindItemByTypeId(int typeId)
        {
            return owner != null ? owner.FindDragonDescendantSharedItemByTypeId(typeId) : null;
        }

        private Item FindItemByName(string itemName)
        {
            return owner != null ? owner.FindDragonDescendantSharedItemByName(itemName) : null;
        }

        private void EquipArmorItem(CharacterMainControl character, Item armorItem, int slotHash)
        {
            if (owner != null) owner.EquipDragonDescendantSharedArmorItem(character, armorItem, slotHash);
        }

        private void RefreshEquipmentModels(CharacterMainControl character)
        {
            if (owner != null) owner.RefreshDragonDescendantSharedEquipmentModels(character);
        }

        private UniTask LoadHighestTierAmmo(CharacterMainControl character)
        {
            return owner != null ? owner.LoadDragonDescendantSharedHighestTierAmmo(character) : default(UniTask);
        }

        private bool IsModeEActive { get { return owner != null && owner.IsModeEActive; } }

        private bool CheckBossKillAchievementsOnce(CharacterMainControl boss, string bossTypeOverride = null)
        {
            return owner != null && owner.CheckDragonKingBossKillAchievementsOnce(boss, bossTypeOverride);
        }

        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            if (owner != null) owner.NotifyDragonKingBossSpawnFailed(preset);
        }

        private void ApplyBossStatMultiplier(CharacterMainControl character)
        {
            if (owner != null) owner.ApplyDragonKingBossStatMultiplier(character);
        }

        private void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        {
            if (owner != null) owner.RegisterDragonKingEnemyRecoveryAnchor(enemy, anchorPosition);
        }

        private void SetupAIAggro(CharacterMainControl character)
        {
            if (owner != null) owner.SetupAIAggro(character);
        }

        private CharacterRandomPreset FindQuestionMarkPreset()
        {
            return owner != null ? owner.FindQuestionMarkPreset() : null;
        }

        private CharacterRandomPreset FindFallbackPreset()
        {
            return owner != null ? owner.FindFallbackPreset() : null;
        }

        private void UnregisterEnemyRecovery(CharacterMainControl enemy)
        {
            if (owner != null) owner.UnregisterDragonKingEnemyRecovery(enemy);
        }

        private void RegisterBossRandomLootTracking(CharacterMainControl character, int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            if (owner != null) owner.RegisterDragonKingBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }

        private void MarkBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (owner != null) owner.MarkDragonKingBossRushLootboxPathTracking(character);
        }

        private void OnBossBeforeSpawnLoot(CharacterMainControl character, DamageInfo damageInfo)
        {
            if (owner != null) owner.OnDragonKingBossBeforeSpawnLoot(character, damageInfo);
        }

        private void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            if (owner != null) owner.ClearDragonKingBossRandomLootTracking(character);
        }

        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (owner != null) owner.FinalizeDragonKingBossRushLootboxPathTracking(character);
        }

        private void NotifyBossSpawnFailed(EnemyPresetInfo preset)
        {
            if (owner != null) owner.NotifyDragonKingBossSpawnFailed(preset);
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
            if (owner != null) owner.CleanupModeGManagedCharacter(character, runtimeNameKey, runtimePresetName, logTag);
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }

    /// <summary>保留龙王旧入口，并将实例逻辑转交唯一运行模块。</summary>
    public partial class ModBehaviour
    {
        private DragonKingRuntimeModule dragonKingRuntimeModule;

        internal void AttachDragonKingRuntimeModule(DragonKingRuntimeModule module) { dragonKingRuntimeModule = module; }
        internal void DetachDragonKingRuntimeModule(DragonKingRuntimeModule module)
        {
            if (ReferenceEquals(dragonKingRuntimeModule, module)) dragonKingRuntimeModule = null;
        }

        internal IEnumerator DelayedBossPositionValidationForBossModule(CharacterMainControl boss, float delay)
        {
            return DelayedBossPositionValidation(boss, delay);
        }

        internal bool CheckDragonKingBossKillAchievementsOnce(CharacterMainControl boss, string bossTypeOverride = null)
        {
            return CheckBossKillAchievementsOnce(boss, bossTypeOverride);
        }

        internal MonoBehaviour DragonKingCurrentBoss
        {
            get { return currentBoss; }
            set { currentBoss = value; }
        }
        internal List<MonoBehaviour> DragonKingCurrentWaveBosses { get { return currentWaveBosses; } }
        internal int DragonKingBossesPerWave { get { return bossesPerWave; } }
        internal List<EnemyPresetInfo> DragonKingEnemyPresets { get { return enemyPresets; } }
        internal Dictionary<CharacterMainControl, float> DragonKingBossSpawnTimes { get { return bossSpawnTimes; } }
        internal Dictionary<CharacterMainControl, int> DragonKingBossOriginalLootCounts { get { return bossOriginalLootCounts; } }

        internal void ApplyDragonKingBossStatMultiplier(CharacterMainControl character) { ApplyBossStatMultiplier(character); }
        internal void RegisterDragonKingEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchor) { RegisterEnemyRecoveryAnchor(enemy, anchor); }
        internal void UnregisterDragonKingEnemyRecovery(CharacterMainControl enemy) { UnregisterEnemyRecovery(enemy); }
        internal void RegisterDragonKingBossRandomLootTracking(CharacterMainControl character, int originalLootCount, float spawnTimeOffset)
        {
            RegisterBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }
        internal void MarkDragonKingBossRushLootboxPathTracking(CharacterMainControl character) { MarkBossRushLootboxPathTracking(character); }
        internal void OnDragonKingBossBeforeSpawnLoot(CharacterMainControl character, DamageInfo damageInfo) { OnBossBeforeSpawnLoot(character, damageInfo); }
        internal void ClearDragonKingBossRandomLootTracking(CharacterMainControl character) { ClearBossRandomLootTracking(character); }
        internal void FinalizeDragonKingBossRushLootboxPathTracking(CharacterMainControl character) { FinalizeBossRushLootboxPathTracking(character); }
        internal void NotifyDragonKingBossSpawnFailed(EnemyPresetInfo preset) { OnBossSpawnFailed(preset); }

        public async UniTask<CharacterMainControl> SpawnDragonKing(Vector3 position,
            bool notifyBossRushOnFailure = true, bool deferActivationUntilNextFrame = false,
            bool isNonWaveSpawn = false, Func<bool> isActiveCheck = null)
        {
            return dragonKingRuntimeModule != null
                ? await dragonKingRuntimeModule.SpawnDragonKing(position, notifyBossRushOnFailure,
                    deferActivationUntilNextFrame, isNonWaveSpawn, isActiveCheck)
                : null;
        }

        public static void ClearDragonKingStaticCache() { DragonKingRuntimeModule.ClearDragonKingStaticCache(); }
        public static void ReleaseDragonKingInstance() { DragonKingRuntimeModule.ReleaseDragonKingInstance(); }

        private void CleanupTrackedDragonKingsOnArenaExit()
        {
            if (dragonKingRuntimeModule != null) dragonKingRuntimeModule.CleanupTrackedDragonKingsOnArenaExit();
        }

        internal void CleanupCancelledDragonKing(CharacterMainControl character, bool releaseAssetReference = true)
        {
            if (dragonKingRuntimeModule != null) dragonKingRuntimeModule.CleanupCancelledDragonKing(character, releaseAssetReference);
        }

        private bool IsDragonKingPreset(EnemyPresetInfo preset)
        {
            return dragonKingRuntimeModule != null && dragonKingRuntimeModule.IsDragonKingPreset(preset);
        }

        private void RegisterDragonKingPreset()
        {
            if (dragonKingRuntimeModule != null) dragonKingRuntimeModule.RegisterDragonKingPreset();
        }

        private CharacterRandomPreset FindDragonKingBasePreset()
        {
            return dragonKingRuntimeModule != null
                ? dragonKingRuntimeModule.FindDragonKingBasePreset() : null;
        }

        internal void OnDragonKingBossHurt(Health health, DamageInfo damageInfo)
        {
            if (dragonKingRuntimeModule != null) dragonKingRuntimeModule.OnDragonKingBossHurt(health, damageInfo);
        }

        internal bool DragonKingSetBonusRegistered
        {
            get { return dragonKingRuntimeModule != null && dragonKingRuntimeModule.DragonKingSetBonusRegistered; }
            set { if (dragonKingRuntimeModule != null) dragonKingRuntimeModule.DragonKingSetBonusRegistered = value; }
        }

        private bool dragonKingSetBonusRegistered
        {
            get { return DragonKingSetBonusRegistered; }
            set { DragonKingSetBonusRegistered = value; }
        }

        private HashSet<Health> activeDragonKingHealths
        {
            get { return dragonKingRuntimeModule != null ? dragonKingRuntimeModule.ActiveDragonKingHealths : EmptyDragonKingHealthSet; }
        }

        private HashSet<Health> EmptyDragonKingHealthSet { get { return new HashSet<Health>(); } }

        internal async UniTask<ManagedBossPrepareResult> PrepareManagedDragonKingAsync(Vector3 position, ManagedBossSpawnContext context)
        {
            return dragonKingRuntimeModule != null
                ? await dragonKingRuntimeModule.PrepareManagedDragonKingAsync(position, context)
                : null;
        }

    }
}
