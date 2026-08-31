// ============================================================================
// BackMountainSeedDrops.cs - 三个自定义 Boss 的菜地种子掉落
// ============================================================================
// 接入点是 LootAndRewards/LootAndRewardsSpecialLoot.cs 的 Boss 掉落箱协程，
// 一行调用，内部自带门控——后山关闭或菜地未解锁时零行为。
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
                if (!IsBackMountainConfiguredEnabled()) return;
                if (!BackMountainUnlocks.IsFacilityUnlocked(BackMountainFacility.Garden)) return;

                int seedTypeId = ResolveBackMountainSeedTypeId(bossMain);
                if (seedTypeId <= 0) return;

                if (UnityEngine.Random.value > BackMountainSeedDropChance) return;

                // 种子物品可能还没注册（玩家刚解锁、还没进过基地）：现注册一次
                if (!BackMountainItems.EnsureRuntimeRegistration(seedTypeId)) return;

                Item seed = ItemAssetsCollection.InstantiateSync(seedTypeId);
                if (seed == null) return;

                inv.AddItem(seed);
                DevLog(BackMountainConfig.LogPrefix + "掉落菜地种子: " + seedTypeId);
            }
            catch (Exception e)
            {
                DevLog(BackMountainConfig.LogPrefix + "[WARNING] 种子掉落失败: " + e.Message);
            }
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
