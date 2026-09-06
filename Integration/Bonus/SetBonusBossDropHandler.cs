// ============================================================================
// SetBonusBossDropHandler.cs - 冰霜/雷霆套装的原版 Boss 额外掉落
// ============================================================================
// 模块说明：
//   雷霆套装 ← 官方风暴区 Boss（Cname_StormBoss1..5，四骑士 / 口口口口等）
//   冰霜套装 ← 官方「???」Boss（Cname_Boss_Blue，霜之哀伤也从它额外掉落，两条互不影响）
//
//   接入点是 Harmony 的 CharacterMainControl.OnDead 前缀（Patches/Combat/CharacterOnDeadPatch.cs），
//   **不是** BossRush 奖励箱专用的 AddBossSpecialLootToLootboxCoroutine——所以原版地图里
//   打这些 Boss 一样能掉，玩家不必进竞技场。形态逐字照 FrostmourneBlueBossDropHandler。
//
// defer 协议（tests/ExtraBossDropDeferGuard.py 守卫，四处接线一处都不能少）：
//   RandomizeBossLoot_LootAndRewards 会把 bossMain.dropBoxOnDead = false 并另建一个箱子，
//   官方那句 CreateFromItem(characterItem) 于是不再执行。因此在 Mod 奖励箱路径上不能直写
//   boss.CharacterItem.Inventory，必须先登记 pending，再由下面三个消费入口之一取走：
//     1. TryConsumePendingBossRushLootboxDrop —— 进 Mod 奖励箱（正常路径 + 找不到模板的回退）
//     2. TryConsumePendingAsWorldDrop        —— 无间炼狱（两种箱子都没有，只能落地）
//     3. CancelPendingBossRushLootboxDrop    —— Finalize 撤销
//
//   掉率：单格共享 roll，头 20% / 甲 20% / 60% 空。掉落黑名单不影响这里——
//   黑名单只挡随机奖池，专属掉落格与 NPC 商店都不查它（龙王套装、词缀熔石同款）。
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using ItemStatsSystem;

namespace BossRush
{
    internal static class SetBonusBossDropHandler
    {
        private const string StormZoneBossNameKeyPrefix = "Cname_StormBoss";
        private const string BlueBossNameKey = "Cname_Boss_Blue";

        /// <summary>单件掉率；两件共享一格 roll，合计 40% 掉一件。</summary>
        private const float PieceDropChance = 0.20f;

        private const string LogPrefix = "[SetBonusLoot] ";

        /// <summary>
        /// Boss → 本次已 roll 中的套装件 TypeID。
        /// 存 TypeID 而不是 bool：roll 只在死亡帧发生一次，之后无论走哪条消费通道都要发同一件，
        /// 不能到消费时再摇一次（那会让「进箱 / 落地 / 回退」三条路掉出不同东西）。
        /// </summary>
        private static readonly Dictionary<CharacterMainControl, int> pendingBossRushLootboxDrops
            = new Dictionary<CharacterMainControl, int>();

        // 复用的清理缓冲：PruneInvalidHooks 每次 Boss 死亡都会跑，热路径不要每次 new 一个 List
        private static readonly List<CharacterMainControl> pruneScratch
            = new List<CharacterMainControl>();

        /// <summary>Mod 卸载 / 宿主重建：清空 pending 表（由 OnDestroy_Integration 调用）。</summary>
        internal static void ResetStaticCaches()
        {
            pendingBossRushLootboxDrops.Clear();
            pruneScratch.Clear();
        }

        /// <summary>
        /// Harmony OnDead 前缀入口。非目标 Boss、roll 空时零副作用。
        /// Mode G / Mode H / 遗种巢随从的死亡抑制由调用方（CharacterOnDeadPatch）统一门控。
        /// </summary>
        internal static void TryHandleSetBonusBossDeath(CharacterMainControl boss)
        {
            try
            {
                PruneInvalidHooks();

                int typeId;
                if (!TryRollSetPiece(boss, out typeId))
                {
                    return;
                }

                if (ShouldDeferToBossRushLootbox(boss))
                {
                    pendingBossRushLootboxDrops[boss] = typeId;
                    return;
                }

                // 原版掉落路径：官方仍会 CreateFromItem(characterItem)，直接塞进 Boss 身上的库存
                Item bossCharacterItem = boss.CharacterItem;
                Inventory inventory = bossCharacterItem != null ? bossCharacterItem.Inventory : null;
                if (inventory == null)
                {
                    return;
                }

                TryAddSetPieceToInventory(inventory, typeId, LogPrefix + "原版掉落箱额外掉落");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "Boss 额外掉落处理失败: " + e.Message);
            }
        }

        /// <summary>
        /// 按 Boss 身份决定套装，再单格共享 roll 出具体部件。返回 false 表示非目标 Boss 或本次不掉。
        /// </summary>
        private static bool TryRollSetPiece(CharacterMainControl boss, out int typeId)
        {
            typeId = 0;

            if (boss == null || boss.characterPreset == null)
            {
                return false;
            }

            string nameKey = boss.characterPreset.nameKey;
            if (string.IsNullOrEmpty(nameKey))
            {
                return false;
            }

            int helmetId;
            int armorId;
            if (nameKey.StartsWith(StormZoneBossNameKeyPrefix, StringComparison.Ordinal))
            {
                helmetId = FrostThunderSetConfig.THUNDER_HELMET_ID;
                armorId = FrostThunderSetConfig.THUNDER_ARMOR_ID;
            }
            else if (string.Equals(nameKey, BlueBossNameKey, StringComparison.Ordinal))
            {
                helmetId = FrostThunderSetConfig.FROST_HELMET_ID;
                armorId = FrostThunderSetConfig.FROST_ARMOR_ID;
            }
            else
            {
                return false;
            }

            float roll = UnityEngine.Random.value;
            if (roll < PieceDropChance)
            {
                typeId = helmetId;
                return true;
            }
            if (roll < PieceDropChance * 2f)
            {
                typeId = armorId;
                return true;
            }

            return false;
        }

        private static bool ShouldDeferToBossRushLootbox(CharacterMainControl boss)
        {
            ModBehaviour instance = ModBehaviour.Instance;
            return instance != null && instance.ShouldDeferExtraBossDropToModPath(boss);
        }

        /// <summary>
        /// 进箱消费（Mod 奖励箱正常路径，以及找不到 Lootbox 模板时的 characterItem 回退）。
        /// 幂等：取出即从 pending 移除。
        /// </summary>
        internal static void TryConsumePendingBossRushLootboxDrop(CharacterMainControl boss, Inventory inventory)
        {
            PruneInvalidHooks();

            if (boss == null || inventory == null)
            {
                return;
            }

            int typeId;
            if (!pendingBossRushLootboxDrops.TryGetValue(boss, out typeId))
            {
                return;
            }
            pendingBossRushLootboxDrops.Remove(boss);

            TryAddSetPieceToInventory(inventory, typeId, LogPrefix + "BossRush 奖励箱额外掉落");
        }

        /// <summary>
        /// 无间炼狱专用：那条分支既不建 BossRush 奖励箱、又已把官方 characterItem 箱关掉，
        /// 额外掉落无处可去，只能走世界掉落通道。幂等：取出即从 pending 移除。
        /// </summary>
        internal static void TryConsumePendingAsWorldDrop(CharacterMainControl boss, Vector3 position)
        {
            PruneInvalidHooks();

            if (boss == null)
            {
                return;
            }

            int typeId;
            if (!pendingBossRushLootboxDrops.TryGetValue(boss, out typeId))
            {
                return;
            }
            pendingBossRushLootboxDrops.Remove(boss);

            if (!EnsureSetPiecePrefabLoaded(typeId))
            {
                return;
            }

            Item rewardItem = null;
            try
            {
                rewardItem = ItemAssetsCollection.InstantiateSync(typeId);
                if (rewardItem == null)
                {
                    ModBehaviour.DevLog(LogPrefix + "世界掉落失败：无法实例化套装件 TypeID=" + typeId);
                    return;
                }

                RestoreFullDurability(rewardItem);

                Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                rewardItem.Drop(position, true, dir, UnityEngine.Random.Range(30f, 60f));
                rewardItem = null;
                ModBehaviour.DevLog(LogPrefix + "无间炼狱额外掉落（世界掉落）TypeID=" + typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "世界掉落失败: " + e.Message);
            }
            finally
            {
                DestroyUnusedReward(rewardItem);
            }
        }

        /// <summary>Finalize 撤销：pending 没有去处时丢弃，避免跨 Boss 泄漏。</summary>
        internal static void CancelPendingBossRushLootboxDrop(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null))
            {
                return;
            }

            pendingBossRushLootboxDrops.Remove(boss);
        }

        private static bool TryAddSetPieceToInventory(Inventory inventory, int typeId, string logPrefix)
        {
            if (inventory == null)
            {
                return false;
            }

            if (!EnsureSetPiecePrefabLoaded(typeId))
            {
                ModBehaviour.DevLog(logPrefix + "失败：套装件 prefab 未注册 TypeID=" + typeId);
                return false;
            }

            EnsureExtraInventoryCapacity(inventory);

            Item rewardItem = null;
            try
            {
                rewardItem = ItemAssetsCollection.InstantiateSync(typeId);
                if (rewardItem == null)
                {
                    ModBehaviour.DevLog(logPrefix + "失败：无法实例化套装件 TypeID=" + typeId);
                    return false;
                }

                RestoreFullDurability(rewardItem);

                if (!inventory.AddAndMerge(rewardItem, 0))
                {
                    ModBehaviour.DevLog(logPrefix + "失败：无法加入库存 TypeID=" + typeId);
                    return false;
                }

                rewardItem = null;
                ModBehaviour.DevLog(logPrefix + "成功 TypeID=" + typeId);
                return true;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(logPrefix + "失败: " + e.Message);
                return false;
            }
            finally
            {
                DestroyUnusedReward(rewardItem);
            }
        }

        /// <summary>
        /// 掉落前兜底按需注册：存档/商店/UI 可能在延迟 bootstrap 之前就按 TypeID 取 prefab，
        /// 没注册会退化成官方 FallbackItem（契约 §6）。形态照 EnsureDragonBossRewardPrefabLoaded。
        /// </summary>
        private static bool EnsureSetPiecePrefabLoaded(int typeId)
        {
            if (BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId))
            {
                return true;
            }

            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(typeId);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "[WARNING] 兜底按需注册套装件失败: " + e.Message);
            }

            return BossRushDynamicItemRegistry.HasRegisteredPrefabWithoutEnsuring(typeId);
        }

        private static void RestoreFullDurability(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                float maxDurability = item.MaxDurability;
                if (maxDurability > 0f)
                {
                    item.Durability = maxDurability;
                    item.DurabilityLoss = 0f;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog(LogPrefix + "恢复耐久失败: " + e.Message);
            }
        }

        private static void DestroyUnusedReward(Item rewardItem)
        {
            if (rewardItem == null)
            {
                return;
            }

            try
            {
                rewardItem.DestroyTree();
            }
            catch (Exception)
            {
                // 清理未交付的临时物品失败不得抛出：调用点在 finally 里
            }
        }

        private static void EnsureExtraInventoryCapacity(Inventory inventory)
        {
            if (inventory == null)
            {
                return;
            }

            try
            {
                int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                int requiredCapacity = Mathf.Max(inventory.Capacity, contentCount + 1);
                if (requiredCapacity > inventory.Capacity)
                {
                    inventory.SetCapacity(requiredCapacity);
                }
            }
            catch (Exception)
            {
                // 扩容失败就按原容量塞，AddAndMerge 会自行判定是否放得下
            }
        }

        /// <summary>清掉已被销毁的 Boss 键（Unity 对象销毁后 == null 为真）。</summary>
        private static void PruneInvalidHooks()
        {
            if (pendingBossRushLootboxDrops.Count == 0)
            {
                return;
            }

            pruneScratch.Clear();
            foreach (KeyValuePair<CharacterMainControl, int> pair in pendingBossRushLootboxDrops)
            {
                if (pair.Key == null)
                {
                    pruneScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < pruneScratch.Count; i++)
            {
                pendingBossRushLootboxDrops.Remove(pruneScratch[i]);
            }
            pruneScratch.Clear();
        }
    }
}
