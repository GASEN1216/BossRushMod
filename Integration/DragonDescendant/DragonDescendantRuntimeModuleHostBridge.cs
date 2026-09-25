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

        private MonoBehaviour currentBoss
        {
            get { return owner != null ? owner.DragonDescendantCurrentBoss : null; }
            set { if (owner != null) owner.DragonDescendantCurrentBoss = value; }
        }

        private List<MonoBehaviour> currentWaveBosses
        {
            get { return owner != null ? owner.DragonDescendantCurrentWaveBosses : null; }
        }

        private int bossesPerWave { get { return owner != null ? owner.DragonDescendantBossesPerWave : 0; } }
        private List<EnemyPresetInfo> enemyPresets { get { return owner != null ? owner.DragonDescendantEnemyPresets : null; } }
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

    /// <summary>保留龙裔遗族旧入口，并将实例逻辑转交唯一运行模块。</summary>
    public partial class ModBehaviour
    {
        private DragonDescendantRuntimeModule dragonDescendantRuntimeModule;

        internal void AttachDragonDescendantRuntimeModule(DragonDescendantRuntimeModule module) { dragonDescendantRuntimeModule = module; }
        internal void DetachDragonDescendantRuntimeModule(DragonDescendantRuntimeModule module)
        {
            if (ReferenceEquals(dragonDescendantRuntimeModule, module)) dragonDescendantRuntimeModule = null;
        }

        internal MonoBehaviour DragonDescendantCurrentBoss
        {
            get { return currentBoss; }
            set { currentBoss = value; }
        }
        internal List<MonoBehaviour> DragonDescendantCurrentWaveBosses { get { return currentWaveBosses; } }
        internal int DragonDescendantBossesPerWave { get { return bossesPerWave; } }
        internal List<EnemyPresetInfo> DragonDescendantEnemyPresets { get { return enemyPresets; } }
        internal bool DragonDescendantModeEActive { get { return modeEActive; } }

        internal Item FindDragonDescendantSharedItemByTypeId(int typeId)
        {
            return dragonDescendantRuntimeModule != null
                ? dragonDescendantRuntimeModule.FindItemByTypeId(typeId) : null;
        }

        internal Item FindDragonDescendantSharedItemByName(string itemName)
        {
            return dragonDescendantRuntimeModule != null
                ? dragonDescendantRuntimeModule.FindItemByName(itemName) : null;
        }

        internal void EquipDragonDescendantSharedArmorItem(CharacterMainControl character, Item armorItem, int slotHash)
        {
            if (dragonDescendantRuntimeModule != null)
                dragonDescendantRuntimeModule.EquipArmorItem(character, armorItem, slotHash);
        }

        internal void RefreshDragonDescendantSharedEquipmentModels(CharacterMainControl character)
        {
            if (dragonDescendantRuntimeModule != null)
                dragonDescendantRuntimeModule.RefreshEquipmentModels(character);
        }

        internal UniTask LoadDragonDescendantSharedHighestTierAmmo(CharacterMainControl character)
        {
            return dragonDescendantRuntimeModule != null
                ? dragonDescendantRuntimeModule.LoadHighestTierAmmo(character) : default(UniTask);
        }

        internal CharacterRandomPreset FindQuestionMarkPreset()
        {
            return dragonDescendantRuntimeModule != null
                ? dragonDescendantRuntimeModule.FindQuestionMarkPreset() : null;
        }

        internal CharacterRandomPreset FindFallbackPreset()
        {
            return dragonDescendantRuntimeModule != null
                ? dragonDescendantRuntimeModule.FindFallbackPreset() : null;
        }

        internal void ApplyDragonDescendantBossStatMultiplier(CharacterMainControl character) { ApplyBossStatMultiplier(character); }
        internal void RegisterDragonDescendantEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchor) { RegisterEnemyRecoveryAnchor(enemy, anchor); }
        internal void UnregisterDragonDescendantEnemyRecovery(CharacterMainControl enemy) { UnregisterEnemyRecovery(enemy); }
        internal void RegisterDragonDescendantBossRandomLootTracking(CharacterMainControl character, int originalLootCount, float spawnTimeOffset)
        {
            RegisterBossRandomLootTracking(character, originalLootCount, spawnTimeOffset);
        }
        internal void MarkDragonDescendantBossRushLootboxPathTracking(CharacterMainControl character) { MarkBossRushLootboxPathTracking(character); }
        internal void OnDragonDescendantBossBeforeSpawnLoot(CharacterMainControl character, DamageInfo damageInfo) { OnBossBeforeSpawnLoot(character, damageInfo); }
        internal void ClearDragonDescendantBossRandomLootTracking(CharacterMainControl character) { ClearBossRandomLootTracking(character); }
        internal void FinalizeDragonDescendantBossRushLootboxPathTracking(CharacterMainControl character) { FinalizeBossRushLootboxPathTracking(character); }
        internal void NotifyDragonDescendantBossSpawnFailed(EnemyPresetInfo preset) { OnBossSpawnFailed(preset); }
        internal void SetupAIAggro(CharacterMainControl character)
        {
            if (dragonDescendantRuntimeModule != null)
                dragonDescendantRuntimeModule.SetupAIAggroForModeG(character);
        }

        public async UniTask<CharacterMainControl> SpawnDragonDescendant(Vector3 position,
            bool isChildProtectionSummon = false, bool notifyBossRushOnFailure = true,
            bool deferActivationUntilNextFrame = false, bool isNonWaveSpawn = false,
            Func<bool> isActiveCheck = null)
        {
            return dragonDescendantRuntimeModule != null
                ? await dragonDescendantRuntimeModule.SpawnDragonDescendant(position, isChildProtectionSummon,
                    notifyBossRushOnFailure, deferActivationUntilNextFrame, isNonWaveSpawn, isActiveCheck)
                : null;
        }

        public static void ClearDragonDescendantStaticCache()
        {
            DragonDescendantRuntimeModule.ClearDragonDescendantStaticCache();
        }

        internal static Projectile GetCachedDragonDescendantPhase2BulletPrefab()
        {
            return DragonDescendantRuntimeModule.GetCachedDragonDescendantPhase2BulletPrefab();
        }

        public void CleanupDragonDescendant()
        {
            if (dragonDescendantRuntimeModule != null) dragonDescendantRuntimeModule.CleanupDragonDescendant();
        }

        internal void CleanupCancelledDragonDescendant(CharacterMainControl character)
        {
            if (dragonDescendantRuntimeModule != null) dragonDescendantRuntimeModule.CleanupCancelledDragonDescendant(character);
        }

        internal bool IsDragonDescendantPreset(EnemyPresetInfo preset)
        {
            return dragonDescendantRuntimeModule != null && dragonDescendantRuntimeModule.IsDragonDescendantPreset(preset);
        }

        private void RegisterDragonDescendantPreset()
        {
            if (dragonDescendantRuntimeModule != null) dragonDescendantRuntimeModule.RegisterDragonDescendantPreset();
        }

        internal void OnDragonDescendantHurt(Health health, DamageInfo damageInfo)
        {
            if (dragonDescendantRuntimeModule != null) dragonDescendantRuntimeModule.OnDragonDescendantHurt(health, damageInfo);
        }

        internal async UniTask<ManagedBossPrepareResult> PrepareManagedDragonDescendantAsync(
            Vector3 position, ManagedBossSpawnContext context)
        {
            return dragonDescendantRuntimeModule != null
                ? await dragonDescendantRuntimeModule.PrepareManagedDragonDescendantAsync(position, context)
                : null;
        }

        /// <summary>保留历史公开嵌套类型供能力控制器与外部调用点使用。</summary>
        public class OriginalWeaponData
        {
            public Projectile bulletPrefab;
            public GameObject muzzleFxPrefab;
            public string shootKey;
            public float bulletSpeed;
            public float shootSpeed;
            public float damage;
            public float bulletDistance;
        }
    }
}
