using ItemStatsSystem;
using SharedModeEnemyEquipmentMaterializationPlan = BossRush.ModeDItemPool.SharedModeEnemyEquipmentMaterializationPlan;

namespace BossRush
{
    public partial class ModBehaviour
    {
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
    }
}
