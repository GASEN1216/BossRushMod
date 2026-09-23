// ============================================================================
// BackMountainSeedDrops.cs - 三个自定义 Boss 的菜地种子掉落
// ============================================================================
// 接入点（每次 Boss 死亡只走其中一条，内部自带门控——后山关闭或菜地未解锁时零行为）：
//   1. BossRush 奖励箱：LootAndRewardsSpecialLoot 的 Boss 掉落箱协程；
//   2. 官方 / 原生掉落箱（characterItem 建箱）：随机掉落开关关闭、Mode E/F、未追踪的 Boss、找不到奖励箱模板
//      —— 经 ReturnPendingExtraLootToCharacterItem 与 Mode E/F 分支；
//   3. 无间炼狱（两种箱子都没有）：落在 Boss 尸体处。
// 2026-09-23 前只接了第 1 条，玩家关掉「随机 Boss 掉落」就永远拿不到种子（owner 实测第 15 条）。
//
// 【为什么是额外掉落而不是替换】
//   种子不该顶掉玩家本来能拿到的龙套装/龙王专属掉落。菜地是附加的养成线，
//   不是战利品池的竞争者。因此这里只往箱子里**追加**，不参与既有概率分配。
//
// 【女巫分支形状与另两个不同】
//   龙裔/龙王在掉落协程里是显式 if 分支，女巫走的是
//   PhantomWitchScytheBossDropHandler 的 pending 消费。所以本文件统一用
//   「按 Boss 判定函数分派」而不是挂进那两条分支，三个 Boss 一视同仁。
// ============================================================================

using System;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    public partial class ModBehaviour
    {
        /// <summary>种子掉率。草案值，待 owner 审定。</summary>
        private const float BackMountainSeedDropChance = 0.25f;

        /// <summary>
        /// 按 Boss 类型往掉落箱里追加一颗对应种子。
        /// 后山关闭、菜地未解锁、或该 Boss 不是三个自定义 Boss 之一时静默返回。
        /// </summary>
        private void TryAddBackMountainSeedLoot(Inventory inv, CharacterMainControl bossMain)
        {
            try
            {
                if (inv == null || bossMain == null) return;
                Item seed = TryRollBackMountainSeed(bossMain);
                if (seed == null) return;

                if (!InteractableLootboxInventoryHelper.TryAddExtraItem(inv, seed)) return;
                DevLog(BackMountainConfig.LogPrefix + "掉落菜地种子: " + seed.TypeID);
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "[WARNING] 种子掉落失败: " + e.Message);
            }
        }

        /// <summary>
        /// 官方 / 原生掉落箱路径：种子直接放进 Boss 的 characterItem，官方随后按它建箱。
        /// 只能在 BeforeCharacterSpawnLootOnDead 回调里、且 dropBoxOnDead 仍为 true 的分支上调用。
        /// </summary>
        private void TryAddBackMountainSeedToCharacterItem(CharacterMainControl bossMain)
        {
            Inventory inv = null;
            try
            {
                Item characterItem = bossMain != null ? bossMain.CharacterItem : null;
                inv = characterItem != null ? characterItem.Inventory : null;
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "[WARNING] 读取 Boss characterItem 失败，种子不投放: " + e.Message);
                return;
            }
            TryAddBackMountainSeedLoot(inv, bossMain);
        }

        /// <summary>无间炼狱：没有任何掉落箱，种子落在 Boss 尸体处（与其它额外掉落的世界投放同口径）。</summary>
        private void TryDropBackMountainSeedIntoWorld(CharacterMainControl bossMain)
        {
            Item seed = null;
            try
            {
                if (bossMain == null) return;
                seed = TryRollBackMountainSeed(bossMain);
                if (seed == null) return;

                Vector3 position = bossMain.transform.position + Vector3.up * 0.1f;
                seed.Drop(position, true, UnityEngine.Random.insideUnitSphere.normalized, UnityEngine.Random.Range(30f, 60f));
                DevLog(BackMountainConfig.LogPrefix + "掉落菜地种子（世界掉落）: " + seed.TypeID);
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "[WARNING] 种子世界掉落失败: " + e.Message);
                if (seed != null)
                {
                    try { seed.DestroyTree(); }
                    catch (Exception destroyError)
                    {
                        DevLog(BackMountainConfig.LogPrefix + "[WARNING] 回收未掉出的种子失败: " + destroyError.Message);
                    }
                }
            }
        }

        /// <summary>
        /// 门控 + 掉率 roll + 实例化。未中、门控不过或实例化失败返回 null（不产生任何实例）。
        /// 先判 Boss 种类再 roll：普通 Boss 不消耗随机数。
        /// </summary>
        private Item TryRollBackMountainSeed(CharacterMainControl bossMain)
        {
            if (bossMain == null) return null;
            if (!IsBackMountainConfiguredEnabled()) return null;
            if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden)) return null;

            int seedTypeId = ResolveBackMountainSeedTypeId(bossMain);
            if (seedTypeId <= 0) return null;

            if (UnityEngine.Random.value > BackMountainSeedDropChance) return null;

            // 种子物品可能还没注册（玩家刚解锁、还没进过基地）：现注册一次
            if (!BackMountainItems.EnsureRuntimeRegistration(seedTypeId)) return null;

            return ItemAssetsCollection.InstantiateSync(seedTypeId);
        }

        /// <summary>Boss → 对应种子 TypeID。不是三个自定义 Boss 之一时返回 0。</summary>
        private int ResolveBackMountainSeedTypeId(CharacterMainControl bossMain)
        {
            try
            {
                if (IsDragonDescendantBoss(bossMain)) return BossRushItemIds.DragonSeed;
                if (IsDragonKingBoss(bossMain)) return BossRushItemIds.EmberSeed;
                if (IsBackMountainPhantomWitchBoss(bossMain)) return BossRushItemIds.PhantomSpore;
                return 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// 幽灵女巫判定。掉落文件里没有现成的 IsPhantomWitchBoss，
        /// 这里按与 IsDragonKingBoss 相同的口径本地判一次（名字 + preset nameKey）。
        /// </summary>
        private bool IsBackMountainPhantomWitchBoss(CharacterMainControl boss)
        {
            try
            {
                if (boss == null) return false;

                if (boss.gameObject != null && boss.gameObject.name.Contains("PhantomWitch")) return true;

                if (boss.characterPreset != null
                    && boss.characterPreset.nameKey == PhantomWitchConfig.BossNameKey)
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
