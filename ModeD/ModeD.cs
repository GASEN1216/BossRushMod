// ============================================================================
// ModeD.cs - 白手起家模式核心逻辑
// ============================================================================
// 模块说明：
//   Mode D（白手起家）是 BossRush 的一个特殊玩法模式。
//   玩家需要"裸体"（不携带任何装备，仅允许携带船票）进入竞技场，
//   系统会随机发放开局装备，玩家通过击杀敌人获取更好的装备。
//   
// 主要功能：
//   - 检测玩家是否满足"裸体"入场条件
//   - 初始化物品池（武器、护甲、头盔、弹药、医疗品）
//   - 初始化敌人池（小怪池、Boss池）
//   - 管理 Mode D 的启动和结束
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using Duckov.Utilities;

using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;

namespace BossRush
{
    /// <summary>
    /// Mode D（白手起家）：无限波次赌狗向挑战模式
    /// <para>玩家裸体+船票入场，获得随机开局装备，通过击杀敌人获取更好装备</para>
    /// </summary>
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        #region ModeD

        #region Mode D 状态变量
        
        private bool modeDActive { get { return modeDRuntime.modeDActive; } set { modeDRuntime.modeDActive = value; } }
        private int modeDWaveIndex { get { return modeDRuntime.modeDWaveIndex; } set { modeDRuntime.modeDWaveIndex = value; } }
        private List<CharacterMainControl> modeDCurrentWaveEnemies { get { return modeDRuntime.modeDCurrentWaveEnemies; } }
        private List<EnemyPresetInfo> modeDMinionPool { get { return modeDRuntime.modeDMinionPool; } }
        private List<EnemyPresetInfo> modeDBossPool { get { return modeDRuntime.modeDBossPool; } set { modeDRuntime.modeDBossPool = value; } }
        private static Dictionary<string, CharacterRandomPreset> cachedCharacterPresets
        {
            get { return ModeDRuntimeModule.cachedCharacterPresets; }
            set { ModeDRuntimeModule.cachedCharacterPresets = value; }
        }
        private static List<EnemyPresetInfo> presetFilterCache { get { return ModeDRuntimeModule.presetFilterCache; } }
        private static List<EnemyPresetInfo> presetFilterCache2 { get { return ModeDRuntimeModule.presetFilterCache2; } }
        private ModeDItemPool modeDItemPool { get { return modeDRuntime.ItemPool; } }
        private bool modeDEnemyPoolsInitialized { get { return modeDRuntime.modeDEnemyPoolsInitialized; } set { modeDRuntime.modeDEnemyPoolsInitialized = value; } }
        private bool modeDWaveCompletePending { get { return modeDRuntime.modeDWaveCompletePending; } set { modeDRuntime.modeDWaveCompletePending = value; } }
        private int modeDExpectedEnemiesInCurrentWave { get { return modeDRuntime.modeDExpectedEnemiesInCurrentWave; } set { modeDRuntime.modeDExpectedEnemiesInCurrentWave = value; } }
        private int modeDSpawnResolvedInCurrentWave { get { return modeDRuntime.modeDSpawnResolvedInCurrentWave; } set { modeDRuntime.modeDSpawnResolvedInCurrentWave = value; } }
        private Coroutine modeDAutoNextWaveCoroutine { get { return modeDRuntime.modeDAutoNextWaveCoroutine; } set { modeDRuntime.modeDAutoNextWaveCoroutine = value; } }

        #endregion
        
        #region Mode D 配置
        
        /// <summary>Mode D 每波敌人数（可配置，1-10，默认3）</summary>
        private int modeDEnemiesPerWave { get { return modeDRuntime.modeDEnemiesPerWave; } set { modeDRuntime.modeDEnemiesPerWave = value; } }
        
        #endregion
        
        #region Mode D 公共属性

        /// <summary>是否处于 Mode D 模式</summary>
        public bool IsModeDActive { get { return modeDActive; } }

        /// <summary>Mode D 当前波次</summary>
        public int ModeDWaveIndex { get { return modeDWaveIndex; } }
        
        #endregion

        #region Mode D 核心方法

        /// <summary>
        /// 检测玩家是否满足"裸体"条件（完全为空，包括狗子背包）
        /// </summary>
        public bool IsPlayerNaked()
        {
            return IsPlayerNakedWithAllowedItems("ModeD", GetBossRushTicketTypeId(), -1, true);
        }
        
        /// <summary>
        /// 启动 Mode D 模式
        /// </summary>
        public void StartModeD() { modeDRuntime.StartModeD(); }

        /// <summary>
        /// 检查并尝试启动 Mode D（在进入竞技场时调用）
        /// </summary>
        public bool TryStartModeD() { return ModeDRuntimeModule.TryStartModeD(this); }

        /// <summary>
        /// 结束 Mode D 模式
        /// </summary>
        public void EndModeD() { modeDRuntime.EndModeD(); }

        /// <summary>
        /// 初始化 Mode D 物品池（按 Tag 筛选，包含游戏中所有该品类物品）
        /// </summary>
        private void InitializeModeDItemPools()
        {
            modeDItemPool.InitializeModeDItemPools(FindTagByName);
        }

        private Duckov.Utilities.Tag FindTagByNameInInit(string tagName)
        {
            return modeDItemPool.FindTagByNameInInit(tagName);
        }

        /// <summary>
        /// 初始化 Mode D 敌人池（小怪池 = showName==false，Boss池复用现有）
        /// </summary>
        private void InitializeModeDEnemyPools()
        {
            modeDRuntime.InitializeModeDEnemyPools(enemyPresets, GetLocalizedCharacterName);
        }

        #endregion

        #region Mode D/E 共用辅助方法

        private int GetBossRushTicketTypeId()
        {
            return bossRushTicketTypeId > 0 ? bossRushTicketTypeId : 868;
        }

        private bool TryGetMainCharacterItem(out CharacterMainControl main, out Item characterItem)
        { return ModeEntryInventory.TryGetMainCharacterItem(out main, out characterItem); }
        private Item FindFirstPlayerInventoryItemByTypeId(int typeId, string logTag = null, string itemLabel = null)
        { return ModeEntryInventory.FindFirstPlayerInventoryItemByTypeId(typeId, logTag, itemLabel); }
        private bool IsPlayerNakedWithAllowedItems(string logTag, int allowedTypeIdA, int allowedTypeIdB, bool allowFactionFlags)
        { return ModeEntryInventory.IsPlayerNakedWithAllowedItems(logTag, allowedTypeIdA, allowedTypeIdB, allowFactionFlags); }
        private static bool IsFactionFlagTypeId(int typeId) { return ModeEntryInventory.IsFactionFlagTypeId(typeId); }
        internal void ResetArenaForModeD()
        {
                infiniteHellMode = false;
                infiniteHellWaveIndex = 0;
                infiniteHellCashPool = 0L;
                infiniteHellMilestoneRewardTier = 0;
                infiniteHellWaveCashThisWave = 0L;
                ClearCashMagnetState();

        }
        internal void ClearModeDEnemyRecoveryState() { ClearEnemyRecoveryMonitorState(); }
        internal void ClearModeDMutators(string mode) { ClearMutatorsForMode(mode); }
        internal void InitializeModeDEnemyPoolsForRuntime() { InitializeModeDEnemyPools(); }

        #endregion

        #endregion

        #region ModeDStaticCacheReset

        private static void ResetModeDStaticCaches()
        {
            ModeDRuntimeModule.ResetStaticCaches();
        }

        #endregion

        #region ModeDEquipmentHostBridge

        private void BindModeDItemPoolQueries()
        {
            modeDItemPool.BindQueries(wavesArenaRuntime,
                () => config != null && config.useLegacyBossLootProbabilities,
                () => modeDActive && IsCampaignConfiguredEnabled() && CampaignObjectiveTracker.NeedsMeleeStarterKit(),
                IsZombieModeRewardCandidateAllowed);
        }

        private void GivePlayerStarterKit() { modeDItemPool.GivePlayerStarterKit(); }
        private Duckov.Utilities.Tag FindTagByName(string tagName) { return modeDItemPool.FindTagByName(tagName); }
        private void TryPrewarmModeDGlobalItemPool() { modeDItemPool.TryPrewarmModeDGlobalItemPool(); }
        private void EnsureModeDGlobalItemPool() { modeDItemPool.EnsureModeDGlobalItemPool(); }
        public void EquipEnemyForModeD(CharacterMainControl enemy, int waveIndex, float enemyHealth, bool isBoss = false)
        { modeDItemPool.EquipEnemyForModeD(enemy, waveIndex, enemyHealth, isBoss); }
        internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ) { return modeDItemPool.CreateRandomGlobalItemForModeD(minQ, maxQ); }
        internal Item CreateRandomGlobalItemForModeD(int minQ, int maxQ, float enemyHealth) { return modeDItemPool.CreateRandomGlobalItemForModeD(minQ, maxQ, enemyHealth); }
        private SharedModeEnemyEquipmentMaterializationPlan CreateSharedModeEnemyEquipmentMaterializationPlan(CharacterMainControl enemy, int waveIndex, float enemyHealth, bool isBoss)
        { return modeDItemPool.CreateSharedModeEnemyEquipmentMaterializationPlan(enemy, waveIndex, enemyHealth, isBoss); }
        private bool MaterializeNextSharedModeEnemyEquipmentPlanStep(CharacterMainControl enemy, SharedModeEnemyEquipmentMaterializationPlan plan)
        { return modeDItemPool.MaterializeNextSharedModeEnemyEquipmentPlanStep(enemy, plan); }
        private void CleanupSharedModeEnemyEquipmentMaterializationPlan(SharedModeEnemyEquipmentMaterializationPlan plan)
        { modeDItemPool.CleanupSharedModeEnemyEquipmentMaterializationPlan(plan); }

        #endregion

        #region ModeDWaves

        public bool ModeDStartNextWave() { return modeDRuntime.ModeDStartNextWave(); }
        internal void TickModeDIntegrity(float deltaTime) { modeDRuntime.TickModeDIntegrity(deltaTime); }
        internal void OnModeDWaveComplete() { modeDRuntime.OnModeDWaveComplete(); }
        private void NormalizeDamageMultiplier(CharacterMainControl character) { modeDRuntime.NormalizeDamageMultiplier(character); }
        private EnemyPresetInfo GetRandomBossPreset() { return modeDRuntime.GetRandomBossPreset(); }
        private EnemyPresetInfo GetRandomMinionPreset() { return modeDRuntime.GetRandomMinionPreset(); }
        private Vector3[] GenerateFallbackSpawnPointsAroundPlayer(Vector3 position, int pointCount = 10, float minRadius = 8f, float maxRadius = 15f)
        { return modeDRuntime.GenerateFallbackSpawnPointsAroundPlayer(position, pointCount, minRadius, maxRadius); }

        // 保持 D / Arena 在原调度位置共用完整性时钟的语义。
        internal void ResetArenaIntegrityCheck() { wavesArenaRuntime.ResetWaveIntegrityCheck(); }
        internal bool AdvanceArenaIntegrityCheck(float deltaTime) { return wavesArenaRuntime.AdvanceWaveIntegrityCheck(deltaTime); }
        internal int ModeDConfiguredEnemiesPerWave { get { return config != null ? config.modeDEnemiesPerWave : 0; } }
        internal void ShowModeDEnemyBanner(string name, Vector3 position, Vector3 playerPosition, int current, int total, bool infinite, int wave, int bosses)
        { ShowEnemyBanner_UIAndSigns(name, position, playerPosition, current, total, infinite, wave, bosses); }
        internal void SpawnModeDEnemyCore(EnemyPresetInfo preset, Vector3 position, bool isBoss, Func<bool> isActiveCheck,
            Action<EnemySpawnContext> onSpawned, Action onFailed, int waveIndex)
        { SpawnEnemyCore(preset, position, isBoss, isActiveCheck, onSpawned, onFailed, waveIndex); }
        internal void RegisterModeDEnemyRecoveryAnchor(CharacterMainControl enemy, Vector3 anchor) { RegisterEnemyRecoveryAnchor(enemy, anchor); }
        internal void CheckModeDFlawlessAchievementForRuntime() { CheckModeDFlawlessAchievement(); }
        internal void CheckModeDClearAchievementsForRuntime() { CheckModeDClearAchievements(); }

        #endregion

        #region ModeDInteractables

        public void SetupSignForModeD() { modeDRuntime.SetupSignForModeD(bossRushSignInteract); }
        public void ShowModeDNextWaveOption() { modeDRuntime.ShowModeDNextWaveOption(); }
        public void HideModeDNextWaveOption() { modeDRuntime.HideModeDNextWaveOption(); }
        public void ClearAllBossRushLootboxes() { modeDRuntime.ClearAllBossRushLootboxes(); }
        public void ClearEmptyBossRushLootboxes() { modeDRuntime.ClearEmptyBossRushLootboxes(); }

        #endregion

        #region ModeDGlobalLootStaticCacheReset

        private static void ResetModeDGlobalLootStaticCaches()
        {
            ModeDItemPool.ResetGlobalLootStaticCaches();
        }

        #endregion
    }
}
