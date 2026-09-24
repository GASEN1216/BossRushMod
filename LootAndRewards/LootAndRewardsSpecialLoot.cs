// ============================================================================
// LootAndRewardsSpecialLoot.cs - Boss 特殊掉落与清理
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
        /// <summary>
        /// 调试：记录 Boss 掉落实际物品列表（LootAndRewards 分部实现）
        /// </summary>
        private IEnumerator LogBossLootInventory_LootAndRewards(InteractableLootbox lootbox)
        {
            return wavesArenaRuntime.LogBossLootInventory_LootAndRewards(lootbox);
        }

        private IEnumerator CleanupDifficultyRewardLootboxInventory_LootAndRewards(InteractableLootbox lootbox, int highQualityCount)
        {
            return wavesArenaRuntime.CleanupDifficultyRewardLootboxInventory_LootAndRewards(lootbox, highQualityCount);
        }

        private void ClearDifficultyRewardCleanupScratch()
        {
            wavesArenaRuntime.ClearDifficultyRewardCleanupScratch();
        }

        private bool InventoryContainsItemAtLeastQuality(Inventory inv, int minimumQuality)
        {
            return wavesArenaRuntime.InventoryContainsItemAtLeastQuality(inv, minimumQuality);
        }

        private int GetLegacyBossGuaranteeTypeId(int desiredQuality, out int actualQuality)
        {
            return wavesArenaRuntime.GetLegacyBossGuaranteeTypeId(desiredQuality, out actualQuality);
        }

        private bool TryAddLegacyBossGuaranteeItem(Inventory inv)
        {
            return wavesArenaRuntime.TryAddLegacyBossGuaranteeItem(inv);
        }

        /// <summary>
        /// 判断Boss是否是龙裔遗族（支持多Boss模式）
        /// 通过GameObject名称或预设nameKey判断，不依赖单一实例引用
        /// </summary>
        private bool IsDragonDescendantBoss(CharacterMainControl boss)
        {
            return wavesArenaRuntime.IsDragonDescendantBoss(boss);
        }

        private bool IsDragonKingBoss(CharacterMainControl boss)
        {
            return wavesArenaRuntime.IsDragonKingBoss(boss);
        }

        private IEnumerator AddDragonDescendantLoot(Inventory inv)
        {
            return wavesArenaRuntime.AddDragonDescendantLoot(inv);
        }

        private IEnumerator AddDragonKingLoot(Inventory inv)
        {
            return wavesArenaRuntime.AddDragonKingLoot(inv);
        }

        private bool TryAddDragonKingLootItem(Inventory inv, int typeId, string itemName)
        {
            return wavesArenaRuntime.TryAddDragonKingLootItem(inv, typeId, itemName);
        }

        internal IEnumerator AddBossSpecialLootToLootboxCoroutine(
            InteractableLootbox lootbox,
            CharacterMainControl bossMain,
            bool useLegacyBossLootProbabilities,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            return wavesArenaRuntime.AddBossSpecialLootToLootboxCoroutine(
                lootbox, bossMain, useLegacyBossLootProbabilities, bossMaxHealth,
                modeFPlunderLootBonusCount, modeFPlunderLootPenaltyCount);
        }

        internal void TryAddBackMountainSeedLootForArena(Inventory inv, CharacterMainControl boss)
        {
            TryAddBackMountainSeedLoot(inv, boss);
        }

        internal bool ShouldDeferBlueBossExtraDropToBossRushLootbox(CharacterMainControl bossMain)
        {
            return wavesArenaRuntime.ShouldDeferBlueBossExtraDropToBossRushLootbox(bossMain);
        }

        internal void ReturnPendingExtraLootToCharacterItem(CharacterMainControl bossMain)
        {
            wavesArenaRuntime.ReturnPendingExtraLootToCharacterItem(bossMain);
        }

        internal void DropPendingExtraLootIntoWorld(CharacterMainControl bossMain)
        {
            wavesArenaRuntime.DropPendingExtraLootIntoWorld(bossMain);
        }

        /// <summary>
        /// 额外掉落（寒霜长矛 / 女巫镰刀 / 遗种蛋 / 词缀熔石）是否必须 defer，
        /// 即"不能直接塞进 `boss.CharacterItem.Inventory`"。
        ///
        /// 判据是**官方那只 characterItem 掉落箱到底会不会被创建**，而不是
        /// "会不会有 BossRush 奖励箱"——本 Mod 有两条路径都会把它关掉：
        ///   1. BossRush 奖励箱路径：`RandomizeBossLoot_LootAndRewards` 置
        ///      `dropBoxOnDead = false` 后另建带全新本地 Inventory 的箱子；
        ///   2. 无间炼狱：`OnBossBeforeSpawnLoot` 同样置 false，但连箱子都不建，
        ///      奖励改走世界掉落。
        /// 两条路径下写进 characterItem 的物品都会被静默丢掉，所以都要 defer。
        /// 消费点分别是 `AddBossSpecialLootToLootboxCoroutine`（进箱）与
        /// `DropPendingExtraLootIntoWorld`（落地）。
        /// </summary>
        internal bool ShouldDeferExtraBossDropToModPath(CharacterMainControl bossMain)
        {
            return wavesArenaRuntime.ShouldDeferExtraBossDropToModPath(bossMain);
        }

    }
}
