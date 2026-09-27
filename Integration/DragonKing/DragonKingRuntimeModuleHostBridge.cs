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

        private void RegisterArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RegisterArenaWaveBossFromContent(character); }
        private void ClearArenaCurrentBoss(CharacterMainControl character) { if (owner != null) owner.ClearArenaCurrentBossFromContent(character); }
        private void RemoveArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RemoveArenaWaveBossFromContent(character); }
        private bool HasArenaEnemyPresetCatalog { get { return owner != null && owner.HasArenaEnemyPresetCatalog; } }
        private EnemyPresetInfo FindArenaEnemyPreset(string nameKey) { return owner != null ? owner.FindArenaEnemyPreset(nameKey) : null; }
        private void AddArenaEnemyPreset(EnemyPresetInfo preset) { if (owner != null) owner.AddArenaEnemyPreset(preset); }
        private void RecordArenaBossLoot(CharacterMainControl character, float spawnTime, int count) { if (owner != null) owner.RecordArenaBossLoot(character, spawnTime, count); }
        private void RemoveArenaBossLootRecord(CharacterMainControl character) { if (owner != null) owner.RemoveArenaBossLootRecord(character); }
        private int ArenaBossLootRecordCount { get { return owner != null ? owner.ArenaBossLootRecordCount : 0; } }

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

}
