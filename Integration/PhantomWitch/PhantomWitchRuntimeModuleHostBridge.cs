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

        private void RegisterArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RegisterArenaWaveBossFromContent(character); }
        private void ClearArenaCurrentBoss(CharacterMainControl character) { if (owner != null) owner.ClearArenaCurrentBossFromContent(character); }
        private void RemoveArenaWaveBoss(CharacterMainControl character) { if (owner != null) owner.RemoveArenaWaveBossFromContent(character); }
        private bool HasArenaEnemyPresetCatalog { get { return owner != null && owner.HasArenaEnemyPresetCatalog; } }
        private EnemyPresetInfo FindArenaEnemyPreset(string nameKey) { return owner != null ? owner.FindArenaEnemyPreset(nameKey) : null; }
        private void AddArenaEnemyPreset(EnemyPresetInfo preset) { if (owner != null) owner.AddArenaEnemyPreset(preset); }
        private void CopyArenaTrackedBossCharactersTo(ICollection<CharacterMainControl> destination) { if (owner != null) owner.CopyArenaTrackedBossCharactersTo(destination); }

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

}
