using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    internal sealed partial class DragonDescendantRuntimeModule
    {
        private ModBehaviour owner;

        public override string ModuleName { get { return "DragonDescendant"; } }

        public override void OnAwake(ModBehaviour owner)
        {
            this.owner = owner;
            if (owner != null) owner.AttachDragonDescendantRuntimeModule(this);
        }

        public override void OnDestroy()
        {
            CleanupDragonDescendant();
            UnregisterDragonDescendantSetBonus(null);
            DragonDescendantRuntimeModule.ResetDragonDescendantRuntimeModuleStaticCaches();
            ModBehaviour currentOwner = owner;
            owner = null;
            if (currentOwner != null) currentOwner.DetachDragonDescendantRuntimeModule(this);
        }

        private void RegisterArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RegisterArenaWaveBossFromContent(character); }
        private void ClearArenaCurrentBoss(CharacterMainControl character) { if (owner != null) owner.ClearArenaCurrentBossFromContent(character); }
        private void RemoveArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RemoveArenaWaveBossFromContent(character); }
        private bool HasArenaEnemyPresetCatalog { get { return owner != null && owner.HasArenaEnemyPresetCatalog; } }
        private EnemyPresetInfo FindArenaEnemyPreset(string nameKey) { return owner != null ? owner.FindArenaEnemyPreset(nameKey) : null; }
        private void AddArenaEnemyPreset(EnemyPresetInfo preset) { if (owner != null) owner.AddArenaEnemyPreset(preset); }

        private bool modeEActive { get { return owner != null && owner.DragonDescendantModeEActive; } }

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

        private void ApplyBossStatMultiplier(CharacterMainControl character)
        {
            if (owner != null) owner.ApplyDragonDescendantBossStatMultiplier(character);
        }

        private void RegisterEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchorPosition)
        {
            if (owner != null) owner.RegisterDragonDescendantEnemyRecoveryAnchor(enemy, anchorPosition);
        }

        private void UnregisterEnemyRecovery(CharacterMainControl enemy)
        {
            if (owner != null) owner.UnregisterDragonDescendantEnemyRecovery(enemy);
        }

        private void RegisterBossRandomLootTracking(CharacterMainControl character, int originalLootCount = 3, float spawnTimeOffset = 1f)
        {
            if (owner != null) owner.RegisterDragonDescendantBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }

        private void MarkBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (owner != null) owner.MarkDragonDescendantBossRushLootboxPathTracking(character);
        }

        private void OnBossBeforeSpawnLoot(CharacterMainControl character, DamageInfo damageInfo)
        {
            if (owner != null) owner.OnDragonDescendantBossBeforeSpawnLoot(character, damageInfo);
        }

        private void ClearBossRandomLootTracking(CharacterMainControl character)
        {
            if (owner != null) owner.ClearDragonDescendantBossRandomLootTracking(character);
        }

        private void FinalizeBossRushLootboxPathTracking(CharacterMainControl character)
        {
            if (owner != null) owner.FinalizeDragonDescendantBossRushLootboxPathTracking(character);
        }

        private void OnBossSpawnFailed(EnemyPresetInfo preset)
        {
            if (owner != null) owner.NotifyDragonDescendantBossSpawnFailed(preset);
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

        internal void SetupAIAggroForModeG(CharacterMainControl character)
        {
            SetupAIAggro(character);
        }

        [System.Diagnostics.Conditional("BOSSRUSH_DEV")]
        private static void DevLog(string message)
        {
            ModBehaviour.DevLog(message);
        }
    }

}
