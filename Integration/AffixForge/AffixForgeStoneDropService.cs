// ============================================================================
// AffixForgeStoneDropService.cs - 词缀熔石的 Boss 掉落轨
// ============================================================================
// 为什么需要本文件：
//   熔石此前只有消耗端（AffixForgeSystem 的 ConsumeItem）而没有任何产出端，
//   而游戏内 Wiki（WikiContent/zh/system__affix_forge.md）已经向玩家承诺了两条
//   获取途径：「哥布林商店（要好感度）」与「Boss 掉落（概率不高）」。
//   商店那条由 GoblinAffinityConfig.GetShopItems 提供，本文件补掉落这条。
//
// 挂接点（单点覆盖，零新增补丁）：
//   LootAndRewards/LootAndRewards.cs 的 RegisterBossRandomLootTracking 体内并联一行。
//   形态**逐字照 PetNest/PetNestDropService.cs**——那个函数覆盖全部 Boss 生成调用位，
//   且天然不含 Mode G 托管路径（adapter 会 ClearBossRandomLootTracking）与丧尸模式。
//
// 三段式（照 RegisterBossRandomLootTracking 自身的纪律）：
//   注册前先退订旧 handler -> 注册 -> 失败回滚；角色回收时必须退订。
//
// fail-closed：
//   - 开关关闭 -> 不注册任何 handler（dormant，与 IsAffixForgeConfiguredEnabled 一致）；
//   - 熔石实例化失败 / 塞不进 Boss 库存 -> 静默降级为不掉，绝不抛给宿主。
//
// 热路径纪律：概率判定只在 Boss 死亡帧走一次，不是每帧；判据是两次判空加一次
// no-throw 配置 getter，关闭态成本可忽略。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>词缀熔石掉落服务。per-character 订阅，与掉落追踪同寿命。</summary>
    internal static class AffixForgeStoneDropService
    {
        #region 状态

        private static readonly Dictionary<CharacterMainControl, Action<DamageInfo>> _hooks =
            new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        /// <summary>PruneDestroyedEntries 的重建暂存表。复用同一个实例，避免每次分配。</summary>
        private static readonly Dictionary<CharacterMainControl, Action<DamageInfo>> _scratch =
            new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        /// <summary>当前追踪中的 Boss 数（诊断用）。</summary>
        internal static int TrackedCount { get { return _hooks.Count; } }

        /// <summary>
        /// 已 roll 中、但要等 BossRush 奖励箱建好再投放的熔石。
        ///
        /// 主掉落 handler 会把 `dropBoxOnDead = false` 并另建一个带全新本地 Inventory
        /// 的箱子，官方那句 `if (dropBoxOnDead) CreateFromItem(characterItem)` 于是不再执行——
        /// 直接塞进 `boss.CharacterItem.Inventory` 的熔石会被整只丢掉。
        /// 寒霜长矛与女巫镰刀早就走这条 defer 协议，这里照搬。
        /// </summary>
        private static readonly HashSet<CharacterMainControl> _pendingLootboxDrops =
            new HashSet<CharacterMainControl>();

        #endregion

        #region 注册 / 退订（三段式）

        /// <summary>
        /// 并联到 RegisterBossRandomLootTracking：给这只 Boss 挂熔石掉落 handler。
        /// 开关关闭时直接返回（dormant）。幂等：重复调用先退旧再挂新。
        /// </summary>
        internal static void TryTrack(ModBehaviour owner, CharacterMainControl character)
        {
            if (owner == null || character == null) return;
            if (!IsEnabled(owner)) return;

            try
            {
                // 三段式第一段：先退订旧 handler，避免重绑叠加
                ClearTracking(character);

                // 顺手清掉已随场景销毁的死条目（对齐 CR-2026-08-29-020 的结论）：
                // 中途弃局 / 直接撤离 / 切图销毁的 Boss 不会走 ClearTracking，
                // 它在 _hooks 里的条目（死角色 key + 捕获 owner/character 的委托）永不移除，
                // 长会话跨多局会无上限累积。
                // 词缀锻造没有自己的场景回调，因此就在注册路径上顺带清理——
                // 本方法每局只在 Boss 生成时调用若干次，表本身也只有个位数条目，成本可忽略。
                PruneDestroyedEntries();

                CharacterMainControl captured = character;
                ModBehaviour capturedOwner = owner;
                Action<DamageInfo> handler = delegate (DamageInfo info)
                {
                    OnBossBeforeSpawnLoot(capturedOwner, captured);
                };
                _hooks[character] = handler;

                try
                {
                    character.BeforeCharacterSpawnLootOnDead += handler;
                }
                catch (Exception e)
                {
                    // 三段式第三段：注册失败必须回滚追踪状态
                    _hooks.Remove(character);
                    ModBehaviour.DevLog(
                        "[AffixForge] [WARNING] 注册熔石掉落事件失败，已回滚: " + e.Message);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] 熔石掉落追踪注册失败: " + e.Message);
            }
        }

        /// <summary>
        /// 并联到 ClearBossRandomLootTracking：退订这只 Boss 的 handler。幂等。
        /// </summary>
        internal static void ClearTracking(CharacterMainControl character)
        {
            if (object.ReferenceEquals(character, null)) return;

            Action<DamageInfo> handler;
            if (!_hooks.TryGetValue(character, out handler))
            {
                return;
            }
            _hooks.Remove(character);

            try
            {
                if (!(character == null) && handler != null)
                {
                    character.BeforeCharacterSpawnLootOnDead -= handler;
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] [WARNING] 退订熔石掉落事件失败: " + e.Message);
            }
        }

        /// <summary>
        /// 移除 key 已被 Unity 销毁的条目。
        ///
        /// 用「只保留存活项重建」而不是「逐个 Remove 死 key」：多个已销毁的 Unity 对象
        /// 之间的相等性与哈希不可依赖（`==` 对已销毁对象返回 true），
        /// 按死 key 去 Remove 可能只清掉其中一个、留下其余。重建没有这个问题。
        /// 也不走 `ClearTracking`：那会对已销毁对象做 `-=`，白付一次异常。
        /// </summary>
        private static void PruneDestroyedEntries()
        {
            if (_hooks.Count == 0) return;

            List<CharacterMainControl> alive = null;
            foreach (KeyValuePair<CharacterMainControl, Action<DamageInfo>> pair in _hooks)
            {
                if (pair.Key == null) continue;
                if (alive == null) alive = new List<CharacterMainControl>();
                alive.Add(pair.Key);
            }

            int aliveCount = alive != null ? alive.Count : 0;
            if (aliveCount == _hooks.Count) return;

            _scratch.Clear();
            for (int i = 0; i < aliveCount; i++)
            {
                CharacterMainControl key = alive[i];
                Action<DamageInfo> handler;
                if (_hooks.TryGetValue(key, out handler)) _scratch[key] = handler;
            }
            _hooks.Clear();
            foreach (KeyValuePair<CharacterMainControl, Action<DamageInfo>> pair in _scratch)
            {
                _hooks[pair.Key] = pair.Value;
            }
            _scratch.Clear();
        }

        /// <summary>清空全部追踪（切图 / run 结束 / 宿主销毁）。</summary>
        internal static void ClearAllTracking()
        {
            _scratch.Clear();
            // pending 必须无条件清：_hooks 可能已被逐个 ClearTracking 清空，
            // 而 pending 还挂着上一局的 Boss —— 早返会把它们留到下一局。
            _pendingLootboxDrops.Clear();
            if (_hooks.Count == 0) return;
            List<CharacterMainControl> keys = new List<CharacterMainControl>(_hooks.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ClearTracking(keys[i]);
            }
            _hooks.Clear();
        }

        /// <summary>宿主销毁 / Mod 卸载时复位静态状态。</summary>
        internal static void ResetStaticCaches()
        {
            ClearAllTracking();
        }

        #endregion

        #region 掉落结算

        private static void OnBossBeforeSpawnLoot(ModBehaviour owner, CharacterMainControl boss)
        {
            try
            {
                if (boss == null) return;

                // 第二道防线：关闭路径已并联 ClearAllTracking，这里再查一次开关，
                // 防止将来新增关闭/停机路径时又漏清 —— 那会让已挂接的 handler
                // 穿透 dormant 契约（关了开关照样掉熔石）。
                if (!IsEnabled(owner)) return;

                if (UnityEngine.Random.value >= AffixDefinitions.ForgeStoneBossDropChance) return;

                // roll 必须在 defer 判定之前：defer 记的是"这只 Boss 中了"，
                // 不是"稍后再 roll"（与寒霜长矛同序）。
                if (ShouldDeferToBossRushLootbox(owner, boss))
                {
                    _pendingLootboxDrops.Add(boss);
                    return;
                }

                TrySpawnStoneIntoBossInventory(boss);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] 熔石掉落结算失败: " + e.Message);
            }
        }

        private static void TrySpawnStoneIntoBossInventory(CharacterMainControl boss)
        {
            Item bossItem = boss != null ? boss.CharacterItem : null;
            Inventory inventory = bossItem != null ? bossItem.Inventory : null;
            if (inventory == null) return;
            TryAddStoneToInventory(inventory, "Boss 库存");
        }

        /// <summary>造一颗按配置堆好数量的熔石。造不出来返回 null。</summary>
        private static Item TryCreateStone()
        {
            Item stone = null;
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(AffixForgeStoneConfig.TYPE_ID);
                stone = ItemAssetsCollection.InstantiateSync(AffixForgeStoneConfig.TYPE_ID);
                if (stone == null)
                {
                    ModBehaviour.DevLog("[AffixForge] 熔石实例化失败，本次不掉落");
                    return null;
                }

                try { stone.StackCount = AffixDefinitions.ForgeStoneBossDropCount; }
                catch (Exception)
                {
                    // 堆叠数写不进去就按 1 颗掉，不阻断掉落
                }
                return stone;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] 熔石创建失败: " + e.Message);
                DestroyStoneQuietly(stone);
                return null;
            }
        }

        private static void DestroyStoneQuietly(Item stone)
        {
            if (stone == null) return;
            try { stone.DestroyTree(); }
            catch (Exception)
            {
                // 回收失败只丢引用，不阻断掉落流程
            }
        }

        /// <summary>把熔石加进指定库存。失败即销毁，绝不泄漏悬空 Item。</summary>
        private static void TryAddStoneToInventory(Inventory inventory, string destinationLabel)
        {
            if (inventory == null) return;
            Item stone = TryCreateStone();
            if (stone == null) return;

            try
            {
                EnsureExtraInventoryCapacity(inventory);
                if (!inventory.AddAndMerge(stone, 0))
                {
                    ModBehaviour.DevLog(
                        "[AffixForge] 熔石无法加入" + destinationLabel + "，本次不掉落");
                    DestroyStoneQuietly(stone);
                    return;
                }
                ModBehaviour.DevLog("[AffixForge] 掉落词缀熔石（" + destinationLabel + "）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] 熔石掉落失败: " + e.Message);
                DestroyStoneQuietly(stone);
            }
        }

        #endregion

        #region BossRush 奖励箱 defer 协议

        /// <summary>
        /// 是否要把这次掉落 defer 到 Mod 自己的投放点。
        ///
        /// 直接用 handler 闭包里捕获的 `owner`，不查全局单例：
        /// 调用点在 `OnBossBeforeSpawnLoot` 的 `IsEnabled(owner)` 之后，
        /// 那一步已经保证 owner 非空，再查一次单例既多余、又会把这里
        /// 卷进宿主单例引用的分类台账（见 ModBehaviourInstanceClassificationGuard）。
        /// </summary>
        private static bool ShouldDeferToBossRushLootbox(ModBehaviour owner, CharacterMainControl boss)
        {
            return owner != null && owner.ShouldDeferExtraBossDropToModPath(boss);
        }

        /// <summary>
        /// 并联到 AddBossSpecialLootToLootboxCoroutine：把 defer 的熔石投进 BossRush 奖励箱。
        /// 幂等：取出即从 pending 移除，重复调用不会掉两颗。
        /// </summary>
        internal static void TryConsumePendingBossRushLootboxDrop(
            CharacterMainControl boss, Inventory inventory)
        {
            PrunePendingEntries();
            if (boss == null || inventory == null) return;
            if (!_pendingLootboxDrops.Remove(boss)) return;

            TryAddStoneToInventory(inventory, "BossRush 奖励箱");
        }

        /// <summary>
        /// 无间炼狱专用：那条分支根本不建箱子，奖励通道是把物品 `Drop` 到世界里。
        /// 不接这条，无间炼狱下的熔石会和箱子一起消失。
        /// </summary>
        internal static void TryConsumePendingAsWorldDrop(
            CharacterMainControl boss, Vector3 position)
        {
            PrunePendingEntries();
            if (boss == null) return;
            if (!_pendingLootboxDrops.Remove(boss)) return;

            Item stone = TryCreateStone();
            if (stone == null) return;
            try
            {
                Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                stone.Drop(position, true, dir, UnityEngine.Random.Range(30f, 60f));
                ModBehaviour.DevLog("[AffixForge] 掉落词缀熔石（世界掉落）");
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForge] 熔石世界掉落失败: " + e.Message);
                DestroyStoneQuietly(stone);
            }
        }

        /// <summary>
        /// 并联到 FinalizeBossRushLootboxPathTracking：这只 Boss 不会再有奖励箱了，
        /// 撤销它的 pending，避免条目常驻累积。
        /// </summary>
        internal static void CancelPendingBossRushLootboxDrop(CharacterMainControl boss)
        {
            if (object.ReferenceEquals(boss, null)) return;
            _pendingLootboxDrops.Remove(boss);
            PrunePendingEntries();
        }

        /// <summary>清掉已随场景销毁的 Boss 条目（key 是 Unity 对象，会变成"假 null"）。</summary>
        private static void PrunePendingEntries()
        {
            if (_pendingLootboxDrops.Count == 0) return;
            _pendingLootboxDrops.RemoveWhere(delegate (CharacterMainControl boss)
            {
                return boss == null;
            });
        }

        private static void EnsureExtraInventoryCapacity(Inventory inventory)
        {
            if (inventory == null) return;
            try
            {
                int contentCount = inventory.Content != null ? inventory.Content.Count : 0;
                int required = Mathf.Max(inventory.Capacity, contentCount + 1);
                if (required > inventory.Capacity)
                {
                    inventory.SetCapacity(required);
                }
            }
            catch (Exception)
            {
                // 扩容失败时 AddAndMerge 会自己失败，届时按"不掉落"降级
            }
        }

        #endregion

        #region 辅助

        private static bool IsEnabled(ModBehaviour owner)
        {
            try
            {
                return owner != null && owner.IsAffixForgeConfiguredEnabled();
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion
    }
}
