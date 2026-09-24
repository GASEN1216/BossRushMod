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

        /// <summary>
        /// Boss特殊掉落：统一处理所有Boss的专属掉落物（协程版本）
        /// 在掉落箱填充完成后添加专属掉落物
        /// </summary>
        internal IEnumerator AddBossSpecialLootToLootboxCoroutine(
            InteractableLootbox lootbox,
            CharacterMainControl bossMain,
            bool useLegacyBossLootProbabilities,
            float bossMaxHealth,
            int modeFPlunderLootBonusCount = 0,
            int modeFPlunderLootPenaltyCount = 0)
        {
            if (lootbox == null || bossMain == null)
            {
                FinalizeBossRushLootboxPathTracking(bossMain);
                yield break;
            }

            try
            {
                // 检查是否启用了随机掉落配置
                if (config == null || !config.enableRandomBossLoot)
                {
                    yield break;
                }

                // 等待掉落箱Inventory加载完成
                Inventory inv = lootbox.Inventory;
                if (inv == null)
                {
                    yield break;
                }

                int tries = 0;
                const int maxTries = 30;
                while (tries < maxTries && inv.Loading)
                {
                    tries++;
                    yield return new WaitForSeconds(0.1f);
                }

                if (useLegacyBossLootProbabilities && bossMaxHealth > LEGACY_BOSS_GUARANTEE_MIN_MAX_HEALTH)
                {
                    TryAddLegacyBossGuaranteeItem(inv);
                }

                if (modeFPlunderLootPenaltyCount > 0)
                {
                    ApplyModeFPlunderLootPenalty(inv, modeFPlunderLootPenaltyCount);
                }

                // 根据Boss类型添加对应的专属掉落物
                if (IsDragonDescendantBoss(bossMain))
                {
                    // 龙裔遗族：按概率掉落龙套装（龙息10%、龙头30%、龙甲60%）
                    yield return AddDragonDescendantLoot(inv);
                }
                else if (IsDragonKingBoss(bossMain))
                {
                    // 龙王：按概率掉落专属物品（飞行图腾15%、龙王之冕15%、龙王鳞铠15%、焚皇断界戟15%、焚天龙铳1%、逆鳞39%）
                    yield return AddDragonKingLoot(inv);
                }

                if (modeFPlunderLootBonusCount > 0)
                {
                    AddModeFPlunderLootBonus(inv, modeFPlunderLootBonusCount);
                }

                FrostmourneBlueBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                PhantomWitchScytheBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                // 遗种蛋与词缀熔石走同一条 defer 协议：它们在 BeforeCharacterSpawnLootOnDead
                // 里已经 roll 过，但那时官方的 characterItem 掉落箱已被本路径关掉
                // （RandomizeBossLoot_LootAndRewards 的 dropBoxOnDead = false），
                // 只能等这里箱子建好后再投进来。
                PetNestDropService.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                AffixForgeStoneDropService.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                SetBonusBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                NewWeaponBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);

                // 后山菜地种子。三个 Boss 各掉自己那一种，内部按解锁状态门控；
                // 菜地未解锁或后山关闭时零行为。放在最后是因为它与上面的专属掉落
                // 不互斥——种子是额外的，不该顶掉任何一件既有战利品。
                TryAddBackMountainSeedLoot(inv, bossMain);
                // 未来新增Boss在此添加 else if 分支
            }
            finally
            {
                FinalizeBossRushLootboxPathTracking(bossMain);
            }
        }

        internal bool ShouldDeferBlueBossExtraDropToBossRushLootbox(CharacterMainControl bossMain)
        {
            return wavesArenaRuntime.ShouldDeferBlueBossExtraDropToBossRushLootbox(bossMain);
        }

        /// <summary>
        /// 把 pending 额外掉落还回 Boss 的 `characterItem`，交给官方掉落箱。
        ///
        /// 只有「找不到 Lootbox 模板、回退原版掉落」这一条分支会用：
        /// 那条分支没有把 `dropBoxOnDead` 置 false，官方 `CreateFromItem(characterItem)`
        /// 照常执行，所以还回 characterItem 就等于交付成功。
        /// 直接复用各 integration 的进箱消费入口——它本来就是「加进给定 inventory」，
        /// 换个目标 inventory 即可，不需要第三套接口。
        /// 必须在 `FinalizeBossRushLootboxPathTracking` **之前**调用。
        /// </summary>
        internal void ReturnPendingExtraLootToCharacterItem(CharacterMainControl bossMain)
        {
            if (bossMain == null)
            {
                return;
            }

            Inventory inv;
            try
            {
                Item characterItem = bossMain.CharacterItem;
                inv = characterItem != null ? characterItem.Inventory : null;
            }
            catch (Exception e)
            {
                LogLootWarningLimited(
                    "PrefabFallbackExtraDrop_inventory", "读取 Boss characterItem 库存失败", e);
                return;
            }
            if (inv == null)
            {
                return;
            }

            try
            {
                FrostmourneBlueBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                PhantomWitchScytheBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                PetNestDropService.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                AffixForgeStoneDropService.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                SetBonusBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                NewWeaponBossDropHandler.TryConsumePendingBossRushLootboxDrop(bossMain, inv);
                // 后山种子不走 pending：官方箱路径（含「随机 Boss 掉落」关闭）在这里 roll 一次直接放进 characterItem。
                TryAddBackMountainSeedLoot(inv, bossMain);
            }
            catch (Exception e)
            {
                LogLootWarningLimited(
                    "PrefabFallbackExtraDrop_consume", "回退原版掉落时归还额外掉落失败", e);
            }
        }

        /// <summary>
        /// 把四个 integration 还挂着的 pending 额外掉落直接 `Drop` 到世界里。
        ///
        /// 只有无间炼狱分支会用：它把 `dropBoxOnDead` 关掉之后既不建 BossRush 奖励箱、
        /// 官方 characterItem 箱也没了，`AddBossSpecialLootToLootboxCoroutine` 那条
        /// 进箱通道根本不会跑，pending 会被 Finalize 直接撤销 —— 玩家什么都拿不到。
        /// 落点与无间炼狱里程碑现金一致（Boss 尸体位置略抬高）。
        /// 必须在 `FinalizeBossRushLootboxPathTracking` **之前**调用。
        /// </summary>
        internal void DropPendingExtraLootIntoWorld(CharacterMainControl bossMain)
        {
            if (bossMain == null)
            {
                return;
            }

            Vector3 position;
            try
            {
                position = bossMain.transform.position + Vector3.up * 0.1f;
            }
            catch (Exception e)
            {
                LogLootWarningLimited(
                    "InfiniteHellExtraDrop_position", "读取 Boss 位置失败，跳过世界掉落", e);
                return;
            }

            try
            {
                FrostmourneBlueBossDropHandler.TryConsumePendingAsWorldDrop(bossMain, position);
                PhantomWitchScytheBossDropHandler.TryConsumePendingAsWorldDrop(bossMain, position);
                PetNestDropService.TryConsumePendingAsWorldDrop(bossMain, position);
                AffixForgeStoneDropService.TryConsumePendingAsWorldDrop(bossMain, position);
                SetBonusBossDropHandler.TryConsumePendingAsWorldDrop(bossMain, position);
                NewWeaponBossDropHandler.TryConsumePendingAsWorldDrop(bossMain, position);
            }
            catch (Exception e)
            {
                LogLootWarningLimited(
                    "InfiniteHellExtraDrop_consume", "无间炼狱额外掉落世界投放失败", e);
            }
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

        private void ApplyModeFPlunderLootPenalty(Inventory inv, int penaltyCount)
        {
            if (inv == null || penaltyCount <= 0)
            {
                return;
            }

            modeFPlunderPenaltyScratch.Clear();
            try
            {
                List<Item> content = inv.Content;
                if (content != null)
                {
                    for (int i = 0; i < content.Count; i++)
                    {
                        Item item = content[i];
                        if (item != null && item.Quality >= 6)
                        {
                            modeFPlunderPenaltyScratch.Add(item);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogLootWarningLimited("ApplyModeFPlunderLootPenalty_scan", "扫描 Mode F 掠夺惩罚候选物失败", e);
            }

            modeFPlunderPenaltyScratch.Sort((a, b) =>
            {
                int qA = a != null ? a.Quality : 0;
                int qB = b != null ? b.Quality : 0;
                if (qA != qB)
                {
                    return qB.CompareTo(qA);
                }

                int vA = a != null ? a.Value : 0;
                int vB = b != null ? b.Value : 0;
                return vB.CompareTo(vA);
            });

            int removed = 0;
            for (int i = 0; i < modeFPlunderPenaltyScratch.Count && removed < penaltyCount; i++)
            {
                Item item = modeFPlunderPenaltyScratch[i];
                if (item == null)
                {
                    continue;
                }

                try { item.Detach(); } catch (Exception e) { LogLootWarningLimited("ApplyModeFPlunderLootPenalty_detach", "移除 Mode F 掠夺惩罚物品时 Detach 失败", e); }
                try { item.DestroyTree(); } catch (Exception e) { LogLootWarningLimited("ApplyModeFPlunderLootPenalty_destroy", "移除 Mode F 掠夺惩罚物品时 DestroyTree 失败", e); }
                removed++;
            }

            if (removed > 0)
            {
                DevLog("[ModeF] 已从被杀 Boss 奖励箱移除高品质战利品: " + removed);
            }

            modeFPlunderPenaltyScratch.Clear();
        }

        private void AddModeFPlunderLootBonus(Inventory inv, int bonusCount)
        {
            if (inv == null || bonusCount <= 0)
            {
                return;
            }

            int added = 0;
            for (int i = 0; i < bonusCount; i++)
            {
                Item reward = null;
                try
                {
                    int rewardTypeId = GetRandomInfiniteHellHighQualityRewardTypeID();
                    if (rewardTypeId <= 0)
                    {
                        continue;
                    }

                    reward = ItemAssetsCollection.InstantiateSync(rewardTypeId);
                    if (reward == null || reward.Quality < 6)
                    {
                        continue;
                    }

                    if (inv.AddItem(reward))
                    {
                        added++;
                        reward = null;
                    }
                }
                catch (Exception e)
                {
                    DevLog("[ModeF] [WARNING] 添加掠夺奖励到掉落箱失败: " + e.Message);
                }
                finally
                {
                    try
                    {
                        if (reward != null)
                        {
                            reward.DestroyTree();
                        }
                    }
                    catch (Exception e)
                    {
                        LogLootWarningLimited("AddModeFPlunderLootBonus_cleanup", "清理未加入掉落箱的掠夺奖励失败", e);
                    }
                }
            }

            if (added > 0)
            {
                DevLog("[ModeF] 已向战胜 Boss 奖励箱补入高品质战利品: " + added);
            }
        }

        /// <summary>
        /// 添加龙裔遗族专属掉落物到Inventory
        /// </summary>
    }
}
