namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>
        /// 鏃犻棿鐐肩嫳鍗曟尝瀹屾垚锛氭帀钀界幇閲戙€佹洿鏂版樉绀哄苟鍑嗗涓嬩竴娉?
        /// </summary>
        private void OnInfiniteHellWaveCompleted()
        {
            OnInfiniteHellWaveCompleted_LootAndRewards();
            return;
        }

        /// <summary>
        /// 鎵€鏈夋晫浜哄嚮璐ュ畬鎴?
        /// </summary>
        private void OnAllEnemiesDefeated()
        {
            // 姣忔棩鎸戞垬閫氬叧缁撶畻

            OnAllEnemiesDefeated_LootAndRewards();
            return;
        }

        /// <summary>
        /// 鐜╁姝讳骸淇濇姢锛圔ossRush鏈熼棿锛? 鍙傝€僰eep_items_on_death瀹炵幇
        /// 涓嶅共棰勬父鎴忔浜℃祦绋嬶紝鍙樆姝㈢墿鍝佹帀钀?
        /// </summary>
        private void OnPlayerDeathInBossRush(Health deadHealth, DamageInfo damageInfo)
        {
            // 姣忔棩鎸戞垬姝讳骸缁撶畻

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
