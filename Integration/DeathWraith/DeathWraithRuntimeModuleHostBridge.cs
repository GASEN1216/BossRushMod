using Duckov;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>保留跨目录入口与既有生命周期调用槽的薄宿主桥。</summary>
    public partial class ModBehaviour
    {
        private DeathWraithRuntimeModule deathWraithRuntimeModule;

        internal void AttachDeathWraithRuntimeModule_DeathWraith(DeathWraithRuntimeModule module)
        {
            deathWraithRuntimeModule = module;
        }

        internal void DetachDeathWraithRuntimeModule_DeathWraith(DeathWraithRuntimeModule module)
        {
            if (ReferenceEquals(deathWraithRuntimeModule, module)) deathWraithRuntimeModule = null;
        }

        internal bool IsDeathWraithSystemEnabledForModule_DeathWraith() => IsDeathWraithSystemEnabled();
        internal bool IsRuntimeCharacterPresetCloneForModule_DeathWraith(CharacterRandomPreset preset) => IsRuntimeCharacterPresetClone(preset);
        internal void NormalizeDamageMultiplierForModule_DeathWraith(CharacterMainControl character) => NormalizeDamageMultiplier(character);
        internal void ApplyBossStatMultiplierForModule_DeathWraith(CharacterMainControl character) => ApplyBossStatMultiplier(character);

        private void HandleDeathWraithConfigChanged_DeathWraith() => deathWraithRuntimeModule?.HandleDeathWraithConfigChanged_DeathWraith();
        private void RefreshDeathWraithEventBindings_DeathWraith() => deathWraithRuntimeModule?.RefreshDeathWraithEventBindings_DeathWraith();
        private void OnSetFile_DeathWraith() => deathWraithRuntimeModule?.OnSetFile_DeathWraith();
        private void ClearDeathWraithState_DeathWraith() => deathWraithRuntimeModule?.ClearDeathWraithState_DeathWraith();
        private void InvalidateStoredDeathWraithRecords_DeathWraith(string reason) => deathWraithRuntimeModule?.InvalidateStoredDeathWraithRecords_DeathWraith(reason);
        internal void FlushDeathWraithListIfDirty_DeathWraith() => deathWraithRuntimeModule?.FlushDeathWraithListIfDirty_DeathWraith();
        private bool IsDeathWraithCharacter_DeathWraith(CharacterMainControl character) => deathWraithRuntimeModule != null && deathWraithRuntimeModule.IsDeathWraithCharacter_DeathWraith(character);

        internal void PrimeDeathWraithData_DeathWraith(Health hurtHealth, DamageInfo damageInfo) => deathWraithRuntimeModule?.PrimeDeathWraithData_DeathWraith(hurtHealth, damageInfo);
        internal void RecordDeathWraithData_DeathWraith(Health deadHealth, DamageInfo damageInfo) => deathWraithRuntimeModule?.RecordDeathWraithData_DeathWraith(deadHealth, damageInfo);
        internal void RecordManualDeathWraithData_DeathWraith(CharacterMainControl main, DamageInfo damageInfo, string source) => deathWraithRuntimeModule?.RecordManualDeathWraithData_DeathWraith(main, damageInfo, source);
        internal void OnWraithDied_DeathWraith(Health deadHealth, DamageInfo damageInfo) => deathWraithRuntimeModule?.OnWraithDied_DeathWraith(deadHealth, damageInfo);
        internal void OnCollectSaveData_BoundMeleeSnapshot_DeathWraith() => deathWraithRuntimeModule?.OnCollectSaveData_BoundMeleeSnapshot_DeathWraith();
        internal void UpdateDeferredDeathWraithSave_DeathWraith() => deathWraithRuntimeModule?.UpdateDeferredDeathWraithSave_DeathWraith();

        internal void NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith(DeadBodyManager.DeathInfo info) =>
            deathWraithRuntimeModule?.NotifyOriginalMainCharacterDeathInfoCaptured_DeathWraith(info);

        internal void NotifyOriginalDeadBodySpawnRequested_DeathWraith(DeadBodyManager.DeathInfo info) =>
            deathWraithRuntimeModule?.NotifyOriginalDeadBodySpawnRequested_DeathWraith(info);

        internal void NotifyOriginalDeadBodyLootboxCreated_DeathWraith(
            InteractableLootbox lootbox, Item item, Vector3 position, InteractableLootbox prefab) =>
            deathWraithRuntimeModule?.NotifyOriginalDeadBodyLootboxCreated_DeathWraith(lootbox, item, position, prefab);

        internal void NotifyOriginalDeadBodyTouched_DeathWraith(DeadBodyManager.DeathInfo info) =>
            deathWraithRuntimeModule?.NotifyOriginalDeadBodyTouched_DeathWraith(info);
    }
}
