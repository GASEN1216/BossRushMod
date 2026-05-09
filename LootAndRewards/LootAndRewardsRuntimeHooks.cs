namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 无间炼狱单波完成：掉落现金、更新显示并准备下一波
        /// </summary>
        private void OnInfiniteHellWaveCompleted()
        {
            OnInfiniteHellWaveCompleted_LootAndRewards();
            return;
        }

        /// <summary>
        /// 所有敌人击败完成
        /// </summary>
        private void OnAllEnemiesDefeated()
        {
            OnAllEnemiesDefeated_LootAndRewards();
            return;
        }

        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush(Health deadHealth, DamageInfo damageInfo)
        {
            OnPlayerDeathInBossRush_LootAndRewards(deadHealth, damageInfo);
            return;
        }

        private void OnBossBeforeSpawnLoot(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            OnBossBeforeSpawnLoot_LootAndRewards(bossMain, dmgInfo);
            return;
        }
    }
}
