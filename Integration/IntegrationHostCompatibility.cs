// Integration 子系统的历史宿主入口；保留签名、委托身份、原回调顺序和空值门。
using ItemStatsSystem;
using System.Collections;
using UnityEngine;
using Duckov;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using System.Reflection;
using BossRush.Utils;

namespace BossRush
{
    public partial class ModBehaviour
    {
        #region AffinityRuntimeModuleHostBridge

        private AffinityRuntimeModule affinityRuntime;

        internal void AttachAffinityRuntimeModule(AffinityRuntimeModule module)
        {
            affinityRuntime = module;
        }

        internal void DetachAffinityRuntimeModule(AffinityRuntimeModule module)
        {
            if (ReferenceEquals(affinityRuntime, module))
            {
                affinityRuntime = null;
            }
        }

        private void InitializeAffinitySystem()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.InitializeAffinitySystem();
            }
        }

        internal void TickAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.TickAffinityRuntime();
            }
        }

        internal void OnSceneUnloadAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.OnAffinitySceneUnload();
            }
        }

        internal void CleanupAffinityRuntimeFromHost()
        {
            if (affinityRuntime != null)
            {
                affinityRuntime.Cleanup();
            }
        }

        #endregion

        #region AffixForgeHostCleanup
// ============================================================================
// AffixForgeHostCleanup.cs - 词缀锻造的宿主销毁清理入口
// ============================================================================
// 为什么单独一个文件：
//   ModBehaviour.OnDestroy 已经很长，把各系统的清理内联进去有两个坏处——
//   一是可读性，二是 StaticCacheLifecycleGuard 判断「某个 ResetStaticCaches
//   是否在 OnDestroy 路径上」时只向前回溯有限字符找所属方法声明，链条太长会让
//   末尾的调用被误判成漏清理。因此按仓库既有约定（CleanupAchievementRuntime /
//   CleanupIntegrationRuntimeOnDestroy 等）收口成具名方法。
//
// 顺序是硬约束：
//   先 ShutdownRuntime 退订三组静态事件（Health 伤害/死亡、手持变化、槽位变化），
//   再清 Buff 工厂缓存的运行时 GameObject，最后清词缀表缓存。
//   顺序颠倒会出现「事件还挂着但缓存已空」的窗口，卸载 Mod 时可能空引用。
// ============================================================================

        /// <summary>词缀锻造的宿主销毁清理。幂等，异常经 SafeRuntime 自吞不拖崩宿主。</summary>
        internal void CleanupAffixForgeRuntimeOnDestroy()
        {
            SafeRuntime.Run("AffixRuntimeService.ShutdownRuntime", () => AffixRuntimeService.ShutdownRuntime());
            SafeRuntime.Run("AffixRuntimeService.ResetStaticCaches", () => AffixRuntimeService.ResetStaticCaches());
            // 掉落轨的 per-character 订阅必须与运行时服务同批退订，否则宿主重建后
            // 旧 handler 仍挂在未回收的 Boss 上。
            SafeRuntime.Run("AffixForgeStoneDropService.ResetStaticCaches", () => AffixForgeStoneDropService.ResetStaticCaches());
            SafeRuntime.Run("AffixBuffFactory.ResetStaticCaches", () => AffixBuffFactory.ResetStaticCaches());
            SafeRuntime.Run("AffixDefinitions.ResetStaticCaches", () => AffixDefinitions.ResetStaticCaches());
        }

        #endregion

        #region BackMountainSeedDropsHostBridge

        private void TryAddBackMountainSeedLoot(Inventory inv, CharacterMainControl bossMain) { backMountainRuntime.TryAddBackMountainSeedLoot(inv, bossMain); }
        private void TryAddBackMountainSeedToCharacterItem(CharacterMainControl bossMain) { backMountainRuntime.TryAddBackMountainSeedToCharacterItem(bossMain); }
        private void TryDropBackMountainSeedIntoWorld(CharacterMainControl bossMain) { backMountainRuntime.TryDropBackMountainSeedIntoWorld(bossMain); }
        internal bool IsBackMountainDragonDescendantBoss(CharacterMainControl bossMain) { return IsDragonDescendantBoss(bossMain); }
        internal bool IsBackMountainDragonKingBoss(CharacterMainControl bossMain) { return IsDragonKingBoss(bossMain); }

        #endregion

        #region SetBonusRuntimeHostBridge

        private void RegisterDragonSetEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.RegisterDragonSetEvents();
            }
        }

        private void UnregisterDragonSetEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UnregisterDragonSetEvents();
            }
        }

        private void RegisterSetBonusEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.RegisterSetBonusEvents();
            }
        }

        private void UnregisterSetBonusEvents()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UnregisterSetBonusEvents();
            }
        }

        private void UpdateDragonDash()
        {
            if (setBonusRuntime != null)
            {
                setBonusRuntime.UpdateDragonDash();
            }
        }

        internal bool HasSetBonusElementHealing
        {
            get { return setBonusRuntime != null && setBonusRuntime.HasSetBonusElementHealing; }
        }

        internal bool IsDragonDashEnabledForRuntime
        {
            get { return config != null && config.enableDragonDash; }
        }

        #endregion

        #region ContentBuildingBridges
// 新内容建筑的宿主桥。旧场景装配/早期恢复/清理入口保持不变，具体状态归各模块所有。
// 不新增生命周期或 MonoBehaviour；协程与基地重绘仍由创建该模块的宿主执行。

        private DailyReportMailboxBuilder _dailyReportMailboxBuilder;
        private CampaignBoardBuilder _campaignBoardBuilder;
        private ShowcaseBuildingBuilder _showcaseBuildingBuilder;
        private PetNestBuilder _petNestBuilder;

        private DailyReportMailboxBuilder DailyReportMailbox
        {
            get { return _dailyReportMailboxBuilder ?? (_dailyReportMailboxBuilder = new DailyReportMailboxBuilder(this)); }
        }

        private CampaignBoardBuilder CampaignBoard
        {
            get { return _campaignBoardBuilder ?? (_campaignBoardBuilder = new CampaignBoardBuilder(this)); }
        }

        private ShowcaseBuildingBuilder ShowcaseBuilding
        {
            get { return _showcaseBuildingBuilder ?? (_showcaseBuildingBuilder = new ShowcaseBuildingBuilder(this)); }
        }

        private PetNestBuilder PetNestBuilding
        {
            get { return _petNestBuilder ?? (_petNestBuilder = new PetNestBuilder(this)); }
        }

        // 报箱只借已有模型；加载、缓存和卸载继续由许愿台的原生命周期负责。
        internal GameObject StarwishBuildingModelPrefab { get { return wishFountainRuntime != null ? wishFountainRuntime.ModelPrefab : null; } }

        public void InitDailyReportMailbox() { DailyReportMailbox.InitDailyReportMailbox(); }
        internal void TryInitializeDailyReportMailboxEarly() { DailyReportMailbox.TryInitializeDailyReportMailboxEarly(); }
        public void RestoreDailyReportMailboxes() { DailyReportMailbox.RestoreDailyReportMailboxes(); }
        public void CleanupDailyReportMailbox() { DailyReportMailbox.CleanupDailyReportMailbox(); }

        public void InitCampaignBoardBuilding() { CampaignBoard.InitCampaignBoardBuilding(); }
        internal void TryInitializeCampaignBoardEarly() { CampaignBoard.TryInitializeCampaignBoardEarly(); }
        internal void RegisterCampaignNotesForScene() { CampaignBoard.RegisterCampaignNotesForScene(); }
        public void CleanupCampaignBoardBuilding() { CampaignBoard.CleanupCampaignBoardBuilding(); }

        public void InitBackMountainShowcase() { ShowcaseBuilding.InitBackMountainShowcase(); }
        internal void TryInitializeBackMountainShowcaseEarly() { ShowcaseBuilding.TryInitializeBackMountainShowcaseEarly(); }
        internal void NotifyShowcaseSlotChanged() { ShowcaseBuilding.NotifyShowcaseSlotChanged(); }
        public void CleanupBackMountainShowcase() { ShowcaseBuilding.CleanupBackMountainShowcase(); }

        public void InitPetNestBuilding() { PetNestBuilding.InitPetNestBuilding(); }
        private void TryInitializePetNestEarly() { PetNestBuilding.TryInitializePetNestEarly(); }
        public void RestorePetNestBuildings() { PetNestBuilding.RestorePetNestBuildings(); }
        public void CleanupPetNestBuilding() { PetNestBuilding.CleanupPetNestBuilding(); }

        #endregion

        #region DeathWraithRuntimeModuleHostBridge

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

        #endregion

        #region DragonDescendantBossStaticCacheReset

        public static void ResetDragonDescendantBossStaticCaches()
        {
            ClearDragonDescendantStaticCache();
        }

        #endregion

        #region DragonDescendantRuntimeModuleHostBridge

        internal static bool IsManagedOwnerValid(ManagedBossSpawnContext context) { return ModeGManagedCharacterService.IsManagedOwnerValid(context); }
        internal UniTask<CharacterMainControl> CreateModeGManagedCharacterAsync(
            CharacterRandomPreset preset, Vector3 position, ManagedBossSpawnContext context,
            string runtimeNameKey, string runtimePresetName)
        { return ModeGManagedCharacterService.CreateModeGManagedCharacterAsync(preset, position, context, runtimeNameKey, runtimePresetName); }
        internal void BeginActivateModeGManagedCharacter(CharacterMainControl character) { ModeGManagedCharacterService.BeginActivateModeGManagedCharacter(character); }
        internal void CompleteActivateModeGManagedCharacter(CharacterMainControl character) { ModeGManagedCharacterService.CompleteActivateModeGManagedCharacter(this, character); }
        internal void CleanupModeGManagedCharacter(CharacterMainControl character, string runtimeNameKey, string runtimePresetName, string logTag)
        { ModeGManagedCharacterService.CleanupModeGManagedCharacter(this, character, runtimeNameKey, runtimePresetName, logTag); }
        internal static void DestroyManagedCharacterQuiet(CharacterMainControl character) { ModeGManagedCharacterService.DestroyManagedCharacterQuiet(character); }
        private static bool HasModeGPlayerAuthoredBuff(CharacterMainControl character) { return ModeGManagedCharacterService.HasModeGPlayerAuthoredBuff(character); }
        private void ActivateModeGManagedCharacter(CharacterMainControl character) { ModeGManagedCharacterService.ActivateModeGManagedCharacter(this, character); }

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

        #endregion

        #region DragonKingRuntimeModuleHostBridge

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

        internal bool IsDragonKingPreset(EnemyPresetInfo preset)
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

        private void InitializeFenHuangHalberdSystem()
        {
            dragonKingRuntimeModule.InitializeFenHuangHalberdSystem();
        }

        private void SetupFenHuangHalberdForScene(UnityEngine.SceneManagement.Scene scene)
        {
            dragonKingRuntimeModule.SetupFenHuangHalberdForScene(scene);
        }

        private void CleanupFenHuangHalberdSystem()
        {
            dragonKingRuntimeModule.CleanupFenHuangHalberdSystem();
        }

        internal static WaitForSeconds FenHuangHalberdSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }


        #endregion

        #region EquipmentRuntimeHooks

        internal void TickEquipmentAbilityRuntime()
        {
            UpdateDragonDash();
        }

        #endregion

        #region FlightTotemRuntimeModuleHostBridge

        private void InitializeFlightTotemSystem()
        {
            flightTotemRuntime.InitializeFlightTotemSystem();
        }

        private void SetupFlightTotemForScene(Scene scene)
        {
            flightTotemRuntime.SetupFlightTotemForScene(scene);
        }

        private void CleanupFlightTotemSystem()
        {
            flightTotemRuntime.CleanupFlightTotemSystem();
        }

        internal static WaitForSeconds FlightTotemSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }

        #endregion

        #region FrostmourneRuntimeModuleHostBridge

        private void InitializeFrostmourneSystem()
        {
            frostmourneRuntime.InitializeFrostmourneSystem();
        }

        private void SetupFrostmourneForScene(Scene scene)
        {
            frostmourneRuntime.SetupFrostmourneForScene(scene);
        }

        private void CleanupFrostmourneSystem()
        {
            frostmourneRuntime.CleanupFrostmourneSystem();
        }

        internal static WaitForSeconds FrostmourneSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }

        #endregion

        #region MutatorRuntimeBridge

        internal void TryRollMutatorsForMode(string modeTag)
        {
            MutatorModeFlow.TryRollMutatorsForMode(modeTag, config != null && config.enableMutators,
                config != null ? config.mutatorCount : 0, MutatorCountMin, MutatorCountMax);
        }

        internal void ClearMutatorsForMode(string modeTag) { MutatorModeFlow.ClearMutatorsForMode(modeTag); }

        #endregion

        #region CommonNpcRuntimeHooks

        /// <summary>
        /// 统一生成公共NPC（快递员、哥布林、护士）
        /// </summary>
        private void SpawnCommonNPCs(string context)
        {
            int count = NPCModuleRegistry.SpawnForCurrentScene(this, context);
            DevLog("[NPCSpawn] " + context + "，已触发模块数量: " + count);
        }

        private bool ShouldSpawnCommonNPCsInScene(string sceneName)
        {
            return NPCModuleRegistry.ShouldSpawnAnyInScene(this, sceneName);
        }

        private void DestroyCommonNPCs(string context)
        {
            NPCModuleRegistry.DestroyAll(this, context);
        }

        #endregion

        #region CourierNPC
// ============================================================================
// CourierNPC.cs - 快递员 NPC 宿主兼容入口
// ============================================================================

        private GameObject courierNPCInstance
        {
            get { return courierNpcRuntime != null ? courierNpcRuntime.CourierNPCInstance : null; }
        }

        private CourierNPCController courierController
        {
            get { return courierNpcRuntime != null ? courierNpcRuntime.CourierController : null; }
        }

        private static GameObject courierPrefab
        {
            get { return CourierNpcRuntimeModule.CourierPrefab; }
        }

        private bool LoadCourierAssetBundle()
        {
            return courierNpcRuntime != null && courierNpcRuntime.LoadCourierAssetBundle();
        }

        private void AddCourierInteraction(GameObject courier)
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.AddCourierInteraction(courier);
            }
        }

        public void SpawnCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.SpawnCourierNPC();
            }
        }

        public void DestroyCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.DestroyCourierNPC();
            }
        }

        public void NotifyCourierBossFightStart()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossFightStart();
            }
        }

        public void NotifyCourierBossFightEnd()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossFightEnd();
            }
        }

        public void NotifyCourierNoBoss(bool noBoss)
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierNoBoss(noBoss);
            }
        }

        public void NotifyCourierBossRushCompleted()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.NotifyCourierBossRushCompleted();
            }
        }

        public void TeleportToCourierNPC()
        {
            if (courierNpcRuntime != null)
            {
                courierNpcRuntime.TeleportToCourierNPC();
            }
        }

        #endregion

        #region GoblinNPCRuntimeModuleHostBridge

        private GoblinNpcRuntimeModule goblinNpcRuntime;

        internal GoblinNpcRuntimeModule GoblinNpcRuntime
        {
            get { return goblinNpcRuntime; }
        }

        // 旧 partial 与 UI 仍通过这些同名只读属性读取运行状态；实例只由模块持有。
        private GameObject goblinNPCInstance
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinNPCInstance : null; }
        }

        private GoblinNPCController goblinController
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinController : null; }
        }

        // ZombieMode 复用同一资源缓存，保留原来的私有宿主调用点。
        private GameObject goblinPrefab
        {
            get { return goblinNpcRuntime != null ? goblinNpcRuntime.GoblinPrefab : null; }
        }

        private bool LoadGoblinAssetBundle()
        {
            return goblinNpcRuntime != null && goblinNpcRuntime.LoadGoblinAssetBundle();
        }

        internal GameObject GoblinCourierNpcInstanceForRuntime
        {
            get { return courierNPCInstance; }
        }

        public void SpawnGoblinNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.SpawnGoblinNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn);
            }
        }

        public void DestroyGoblinNPC()
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.DestroyGoblinNPC();
            }
        }

        public void SummonGoblin()
        {
            if (goblinNpcRuntime != null)
            {
                goblinNpcRuntime.SummonGoblin();
            }
        }

        public GoblinNPCController GetGoblinController()
        {
            return goblinNpcRuntime != null ? goblinNpcRuntime.GetGoblinController() : null;
        }

        #endregion

        #region NurseNPCRuntimeModuleHostBridge

        private NurseNpcRuntimeModule nurseNpcRuntime;

        private GameObject nurseNPCInstance { get { return nurseNpcRuntime != null ? nurseNpcRuntime.NurseNPCInstance : null; } }
        private NurseNPCController nurseController { get { return GetNurseController(); } }
        internal GameObject NurseCourierNpcInstanceForRuntime { get { return courierNPCInstance; } }
        internal GameObject NurseGoblinNpcInstanceForRuntime { get { return goblinNPCInstance; } }

        public void SpawnNurseNPC(Vector3? overrideSpawnPos = null, bool stayStillOnSpawn = false, bool forceSpawn = false)
        { if (nurseNpcRuntime != null) nurseNpcRuntime.SpawnNurseNPC(overrideSpawnPos, stayStillOnSpawn, forceSpawn); }
        public void DestroyNurseNPC() { if (nurseNpcRuntime != null) nurseNpcRuntime.DestroyNurseNPC(); }
        public NurseNPCController GetNurseController() { return nurseNpcRuntime != null ? nurseNpcRuntime.GetNurseController() : null; }
        public bool IsNurseSpawned() { return nurseNpcRuntime != null && nurseNpcRuntime.IsNurseSpawned(); }

        #endregion

        #region NewWeaponBootstrap
// ============================================================================
// NewWeaponBootstrap.cs - P0 五把新武器在宿主上的生命周期挂点
// ============================================================================
// 模块说明：
//   宿主 ModBehaviour 只保留四个挂点，实现全部在 NewWeaponRuntime（AGENTS §4.15：
//   新子系统的状态与算法放自己的类型，宿主只做生命周期分发，兼容转发尽量一行）。
//   这四个方法名被其它 partial 文件与守卫引用，**不要改名**：
//     InitializeNewWeaponSystems       <- Integration/EquipmentContentRegistry.cs
//     SetupNewWeaponsForScene          <- BossRushIntegration_StartAndScene / IntegrationDeferredBootstrap
//     ConfigureNewWeaponsAfterLoad     <- Integration/EquipmentContentRegistry.cs
//     CleanupNewWeaponSystemsOnDestroy <- Integration/EquipmentContentRegistry.cs
// ============================================================================

        /// <summary>初始化新武器系统（在 InitializeLateEquipmentAbilitySystems 中调用）</summary>
        private void InitializeNewWeaponSystems()
        {
            NewWeaponRuntime.Initialize();
        }

        /// <summary>场景加载后设置新武器系统</summary>
        private void SetupNewWeaponsForScene(Scene scene)
        {
            NewWeaponRuntime.SetupForScene(this, scene);
        }

        /// <summary>在 LoadEquipmentContent 中调用，补配置新武器</summary>
        private void ConfigureNewWeaponsAfterLoad()
        {
            NewWeaponRuntime.ConfigureAfterLoad();
        }

        /// <summary>清理新武器系统</summary>
        private void CleanupNewWeaponSystemsOnDestroy()
        {
            NewWeaponRuntime.CleanupOnDestroy();
        }

        #endregion

        #region PhantomWitchRuntimeModuleHostBridge

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


        #endregion

        #region ReverseScaleRuntimeModuleHostBridge

        private void InitializeReverseScaleSystem()
        {
            reverseScaleRuntime.InitializeReverseScaleSystem();
        }

        private void SetupReverseScaleForScene(Scene scene)
        {
            reverseScaleRuntime.SetupReverseScaleForScene(scene);
        }

        private void CleanupReverseScaleSystem()
        {
            reverseScaleRuntime.CleanupReverseScaleSystem();
        }

        internal void InjectReverseScaleLocalizationFromRuntimeModule()
        {
            reverseScaleRuntime.InjectReverseScaleLocalization();
        }

        internal static WaitForSeconds ReverseScaleSharedWait05sForRuntime
        {
            get { return sharedWait05s; }
        }
        public static bool TryConfigureReverseScale(ItemStatsSystem.Item item, string baseName)
        {
            return ReverseScaleRuntimeModule.TryConfigureReverseScale(item, baseName);
        }


        #endregion

        #region WeddingHostCompatibilityBridge

        // Other legacy building partials still compile as ModBehaviour and historically
        // shared these private helpers from WeddingBuildingInjector.cs.
        private static Type FindGameType(string fullTypeName) { return BuildingInjectionHelper.FindGameType(fullTypeName); }
        private static Type GetBuildingManagerType() { return BuildingInjectionHelper.GetBuildingManagerType(); }
        private static MethodInfo GetBuildingManagerAnyMethod() { return BuildingInjectionHelper.GetBuildingManagerAnyMethod(); }
        private static MethodInfo GetBuildingDataMethod() { return BuildingInjectionHelper.GetBuildingDataMethod(); }
        private static Type GetBuildingType() { return BuildingInjectionHelper.GetBuildingType(); }
        private static PropertyInfo GetBuildingIdProperty() { return BuildingInjectionHelper.GetBuildingIdProperty(); }
        private static void AssignBuildingContainerField(FieldInfo field, Component buildingComp, Transform container)
        {
            BuildingInjectionHelper.AssignBuildingContainerField(field, buildingComp, container);
        }

        public bool HasWeddingBuildingPlaced() { return WeddingRuntime != null && WeddingRuntime.HasWeddingBuildingPlaced(); }
        public Transform GetWeddingNpcTransform() { return WeddingRuntime != null ? WeddingRuntime.GetWeddingNpcTransform() : null; }
        public Transform TrySpawnMarriedNpcAtWeddingPoint() { return WeddingRuntime != null ? WeddingRuntime.TrySpawnMarriedNpcAtWeddingPoint() : null; }
        public bool CanCurrentSpouseFollowPlayer(string npcId) { return WeddingRuntime != null && WeddingRuntime.CanCurrentSpouseFollowPlayer(npcId); }
        public bool IsSpouseFollowerInstance(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.IsSpouseFollowerInstance(npcId, npcTransform); }
        public bool ShouldShowSpouseFollowOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseFollowOption(npcId, npcTransform); }
        public bool ShouldShowSpouseDivorceOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseDivorceOption(npcId, npcTransform); }
        public bool ShouldShowSpouseHomeOption(string npcId, Transform npcTransform) { return WeddingRuntime != null && WeddingRuntime.ShouldShowSpouseHomeOption(npcId, npcTransform); }
        public float AdjustDialogueStayDurationForSpouseFollow(string npcId, Transform npcTransform, float requestedStayDuration)
        {
            return WeddingRuntime != null ? WeddingRuntime.AdjustDialogueStayDurationForSpouseFollow(npcId, npcTransform, requestedStayDuration) : requestedStayDuration;
        }
        public bool TryStartSpouseFollowingPlayer(string npcId) { return WeddingRuntime != null && WeddingRuntime.TryStartSpouseFollowingPlayer(npcId); }
        public bool TryHandleSpouseFollowRequest(string npcId, Transform dialogueTarget)
        {
            return WeddingRuntime != null && WeddingRuntime.TryHandleSpouseFollowRequest(npcId, dialogueTarget);
        }
        public bool SendSpouseHome(string npcId, bool showMessage = true)
        {
            return WeddingRuntime != null && WeddingRuntime.SendSpouseHome(npcId, showMessage);
        }
        public void HandleSpouseFollowAffinityLoss(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.HandleSpouseFollowAffinityLoss(npcId);
        }
        public void ScheduleRestoreFollowingSpouse(string expectedSceneName, string context)
        {
            if (WeddingRuntime != null) WeddingRuntime.ScheduleRestoreFollowingSpouse(expectedSceneName, context);
        }
        public void RefreshSpouseInteractionOptionsForNpc(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.RefreshSpouseInteractionOptionsForNpc(npcId);
        }
        public bool IsWeddingNpcInstance(Transform npcTransform)
        {
            return WeddingRuntime != null && WeddingRuntime.IsWeddingNpcInstance(npcTransform);
        }
        public void HandleDivorceNpcRelocation(string npcId)
        {
            if (WeddingRuntime != null) WeddingRuntime.HandleDivorceNpcRelocation(npcId);
        }

        public void InitWeddingBuilding()
        {
            if (WeddingRuntime != null) WeddingRuntime.InitWeddingBuilding();
        }
        internal void TryInitializeWeddingBuildingEarly()
        {
            if (WeddingRuntime != null) WeddingRuntime.TryInitializeWeddingBuildingEarly();
        }
        public void CleanupWeddingBuilding()
        {
            if (WeddingRuntime != null) WeddingRuntime.CleanupWeddingBuilding();
        }
        public void RestoreWeddingBuildingNPC()
        {
            if (WeddingRuntime != null) WeddingRuntime.RestoreWeddingBuildingNPC();
        }
        internal void RequestBaseBuildingAreaRepaint(string source)
        {
            if (WeddingRuntime != null) WeddingRuntime.RequestBaseBuildingAreaRepaint(source);
        }
        internal GameObject GetSpouseInstance(string spouseNpcId)
        {
            return WeddingRuntime != null ? WeddingRuntime.GetSpouseInstance(spouseNpcId) : null;
        }

        internal GameObject GetWeddingGoblinNpcInstance() { return goblinNPCInstance; }
        internal GameObject GetWeddingNurseNpcInstance() { return nurseNPCInstance; }
        internal void DestroyWeddingGoblinNpc() { DestroyGoblinNPC(); }
        internal void DestroyWeddingNurseNpc() { DestroyNurseNPC(); }
        internal void SpawnWeddingGoblinNpc(Vector3? position, bool stayStillOnSpawn, bool forceSpawn)
        {
            SpawnGoblinNPC(position, stayStillOnSpawn, forceSpawn);
        }
        internal void SpawnWeddingNurseNpc(Vector3? position, bool stayStillOnSpawn, bool forceSpawn)
        {
            SpawnNurseNPC(position, stayStillOnSpawn, forceSpawn);
        }

        #endregion

        #region WishFountainHostCompatibilityBridge

        public void InitWishFountainBuilding()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.InitWishFountainBuilding();
        }

        internal void TryInitializeWishFountainEarly()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.TryInitializeWishFountainEarly();
        }

        public void CleanupWishFountainBuilding()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.CleanupWishFountainBuilding();
        }

        public void RestoreWishFountainBuildings()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.RestoreWishFountainBuildings();
        }

        public void OpenWishFountainUI()
        {
            if (WishFountainRuntime != null) WishFountainRuntime.OpenWishFountainUI();
        }

        #endregion

        #region DailyReportUIBridge
        /// <summary>打开《鸭科夫日报》面板；配置读取保持在原公开入口的第一步。</summary>
        public void OpenDailyReportUI() { dailyReportRuntime.OpenDailyReportUI(IsDailyReportConfiguredEnabled()); }
        private void EnsureDailyReportView() { dailyReportRuntime.EnsureDailyReportView(); }
        #endregion
    }
}
