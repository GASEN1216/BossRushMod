// ============================================================================
// LootAndRewardsRandomBossLoot.cs - Boss 随机掉落流程
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using Duckov.ItemUsage;
using Duckov.Scenes;
using Duckov.Economy;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Duckov.UI.DialogueBubbles;
using Duckov.UI;
using UnityEngine.AI;
using Duckov.ItemBuilders;

namespace BossRush
{
    public partial class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        internal bool ShouldUseBossDeadBoxPrefabForArena()
        {
            return config != null && !config.lootBoxBlocksBullets;
        }

        internal bool IsModeEOrFActiveForArenaLoot()
        {
            return modeEActive || modeFActive;
        }

        /// <summary>
        /// 玩家死亡保护（BossRush期间）- 参考keep_items_on_death实现（LootAndRewards 分部实现）
        /// 不干预游戏死亡流程，只阻止物品掉落
        /// </summary>
        private void OnPlayerDeathInBossRush_LootAndRewards(Health deadHealth, DamageInfo damageInfo)
        {
            try
            {
                // 检查是否是BossRush期间的玩家死亡
                if (!IsActive) return;

                var character = deadHealth.GetComponent<CharacterMainControl>();
                if (character == null) return;

                // 检查是否是玩家
                bool isPlayer = false;
                try
                {
                    isPlayer = CharacterMainControlExtensions.IsMainCharacter(character);
                }
                catch
                {
                    isPlayer = (character == CharacterMainControl.Main);
                }

                if (!isPlayer) return;

                DevLog("[BossRush] 检测到玩家死亡，不再掉落物品，直接结束BossRush");

                // 结束BossRush
                SetBossRushRuntimeActive(false);
                bossRushArenaActive = false;
                currentBoss = null;
                try
                {
                    if (ammoShop != null)
                    {
                        try
                        {
                            if (ammoShop.gameObject != null)
                            {
                                UnityEngine.Object.Destroy(ammoShop.gameObject);
                            }
                        }
                        catch (Exception e)
                        {
                            LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopDestroy", "玩家死亡时销毁加油站商店对象失败", e);
                        }
                        ammoShop = null;
                    }
                }
                catch (Exception e)
                {
                    LogLootWarningLimited("OnPlayerDeathInBossRush_ammoShopCleanup", "玩家死亡时清理加油站商店失败", e);
                }

                // 取消敌人死亡监听
                Health.OnDead -= OnEnemyDiedWithDamageInfo;

                // 如果是 Mode D 模式，结束 Mode D
                if (modeDActive)
                {
                    EndModeD();
                }

                // 如果是 Mode E 模式，结束 Mode E
                if (modeEActive)
                {
                    EndModeE();
                }

                ShowMessage(L10n.T("BossRush挑战失败！", "BossRush challenge failed!"));
            }
            catch (Exception e)
            {
                DevLog("[BossRush] OnPlayerDeathInBossRush错误: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>Boss 掉落事件沿用旧订阅委托，实际处理归竞技场模块。</summary>
        private void OnBossBeforeSpawnLoot_LootAndRewards(CharacterMainControl bossMain, DamageInfo dmgInfo)
        {
            wavesArenaRuntime.OnBossBeforeSpawnLoot_LootAndRewards(bossMain, dmgInfo);
        }

        internal bool IsRandomBossLootEnabledForArena() { return config != null && config.enableRandomBossLoot; }
        internal bool UseLegacyBossLootProbabilitiesForArena() { return config != null && config.useLegacyBossLootProbabilities; }
        internal void TryAddBackMountainSeedToCharacterItemForArena(CharacterMainControl boss) { TryAddBackMountainSeedToCharacterItem(boss); }
        internal void TryDropBackMountainSeedIntoWorldForArena(CharacterMainControl boss) { TryDropBackMountainSeedIntoWorld(boss); }
        internal bool TryHandleModeFBossPreLootPlunderForArena(CharacterMainControl killer, CharacterMainControl victim) { return TryHandleModeFBossPreLootPlunder(killer, victim); }
        internal int ConsumeModeFBossPendingHighQualityLootPenaltyCountForArena(CharacterMainControl boss) { return ConsumeModeFBossPendingHighQualityLootPenaltyCount(boss); }
        internal int ConsumeModeFBossCarriedHighQualityLootCountForArena(CharacterMainControl boss) { return ConsumeModeFBossCarriedHighQualityLootCount(boss); }

        private void RandomizeBossLoot_LootAndRewards(
            CharacterMainControl bossMain,
            int totalCount,
            float killDuration,
            float highChanceBonusByHealth,
            bool useLegacyProbabilities,
            float legacyBonusFactor,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            wavesArenaRuntime.RandomizeBossLoot_LootAndRewards(bossMain, totalCount, killDuration,
                highChanceBonusByHealth, useLegacyProbabilities, legacyBonusFactor, bossMaxHealth,
                modeFPlunderLootBonusCount, modeFPlunderLootPenaltyCount);
        }
    }
}
