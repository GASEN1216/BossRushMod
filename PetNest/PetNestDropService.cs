// ============================================================================
// PetNestDropService.cs - 遗种巢掉落双轨（实施计划 步骤 4）
// ============================================================================
// 双轨：
//   欧轨 —— Boss 击杀后低概率直掉该血脉的「遗种蛋」（实体物品 500059）；
//   非轨 —— 每次击杀必记「遗魂」到账本（纯账本，不掉实体），同血脉攒够可定向凝蛋。
//
// 挂接点（单点覆盖，零新增补丁）：
//   LootAndRewards/LootAndRewards.cs 的 RegisterBossRandomLootTracking 体内加一行并联。
//   那个函数覆盖全部 Boss 生成调用位，并且天然**不含** Mode G 托管路径
//   （ModeG adapter 会 ClearBossRandomLootTracking）与丧尸模式，正好等于首版掉落范围。
//
// 三段式（照 RegisterBossRandomLootTracking 自身的纪律）：
//   注册前先退订旧 handler -> 注册 -> 失败回滚；角色回收时必须退订。
//
// fail-closed：
//   - 开关关闭 / 未 bootstrap -> 不注册任何 handler（dormant）；
//   - 血脉不在目录里 -> 既不掉蛋也不记遗魂（不给不可孵化的血脉发货）；
//   - 蛋实例化失败 -> 只记遗魂，不抛。
// ============================================================================

using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>遗种巢掉落服务。per-character 订阅，与掉落追踪同寿命。</summary>
    internal static class PetNestDropService
    {
        #region 状态

        private static readonly Dictionary<CharacterMainControl, Action<DamageInfo>> _hooks =
            new Dictionary<CharacterMainControl, Action<DamageInfo>>();

        /// <summary>
        /// 已 roll 中、但要等 BossRush 奖励箱建好再投放的蛋（Boss → 血脉）。
        ///
        /// 为什么需要它：主掉落 handler 会把 `dropBoxOnDead = false` 并另建一个带全新
        /// 本地 Inventory 的箱子，官方那句 `if (dropBoxOnDead) CreateFromItem(characterItem)`
        /// 于是不再执行——直接塞进 `boss.CharacterItem.Inventory` 的蛋会被整只丢掉。
        /// 寒霜长矛与女巫镰刀早就走这条 defer 协议，这里照搬。
        ///
        /// 用 Dictionary 而不是寒霜长矛的 HashSet：血脉必须原样带到 consume 时
        /// 才能 `TryStampLineage`，盖不上血脉的蛋是废蛋（孵化侧 fail-closed）。
        /// </summary>
        private static readonly Dictionary<CharacterMainControl, string> _pendingLootboxDrops =
            new Dictionary<CharacterMainControl, string>();

        /// <summary>PrunePendingEntries 的重建暂存表。复用同一个实例，避免每次分配。</summary>
        private static readonly Dictionary<CharacterMainControl, string> _pendingScratch =
            new Dictionary<CharacterMainControl, string>();

        /// <summary>本次会话已记账但尚未落盘的遗魂笔数（诊断用）。</summary>
        private static int _stagedSoulWrites;

        /// <summary>当前追踪中的 Boss 数（诊断用）。</summary>
        internal static int TrackedCount { get { return _hooks.Count; } }

        /// <summary>已暂存未落盘的遗魂笔数。</summary>
        internal static int StagedSoulWrites { get { return _stagedSoulWrites; } }

        #endregion

        #region 注册 / 退订（三段式）

        /// <summary>
        /// 并联到 RegisterBossRandomLootTracking：给这只 Boss 挂遗种巢掉落 handler。
        /// 开关关闭时直接返回（dormant）。幂等：重复调用先退旧再挂新。
        /// </summary>
        internal static void TryTrack(ModBehaviour owner, CharacterMainControl character)
        {
            if (character == null) return;
            if (!IsEnabled(owner)) return;

            try
            {
                // 三段式第一段：先退订旧 handler，避免重绑叠加
                ClearTracking(character);

                string lineageKey = ResolveLineageKey(character);
                if (string.IsNullOrEmpty(lineageKey)) return;
                // fail-closed：不在血脉目录里的 Boss 不产蛋也不记遗魂
                if (!PetNestLineageCatalog.IsKnownLineage(lineageKey)) return;

                CharacterMainControl captured = character;
                ModBehaviour capturedOwner = owner;
                Action<DamageInfo> handler = delegate (DamageInfo info)
                {
                    OnBossBeforeSpawnLoot(capturedOwner, captured, lineageKey);
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
                    ModBehaviour.DevLog("[PetNest] [WARNING] 注册遗种掉落事件失败，已回滚: " + e.Message);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种掉落追踪注册失败: " + e.Message);
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
                ModBehaviour.DevLog("[PetNest] [WARNING] 退订遗种掉落事件失败: " + e.Message);
            }
        }

        /// <summary>清空全部追踪（切图 / run 结束 / 宿主销毁）。</summary>
        internal static void ClearAllTracking()
        {
            // pending 必须无条件清：_hooks 可能已经被逐个 ClearTracking 清空，
            // 而 pending 还挂着上一局的 Boss —— 早返会把它们留到下一局。
            _pendingLootboxDrops.Clear();
            _pendingScratch.Clear();

            if (_hooks.Count == 0) return;
            List<CharacterMainControl> keys = new List<CharacterMainControl>(_hooks.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ClearTracking(keys[i]);
            }
            _hooks.Clear();
        }

        #endregion

        #region 掉落结算

        private static void OnBossBeforeSpawnLoot(
            ModBehaviour owner, CharacterMainControl boss, string lineageKey)
        {
            try
            {
                if (boss == null || string.IsNullOrEmpty(lineageKey)) return;

                // 第二道防线（CR-2026-08-29-016）：关闭路径已经并联 ClearAllTracking，
                // 这里再查一次开关，防止将来新增关闭/停机路径时又漏清 —— 那会让
                // 已挂接的 handler 穿透 dormant 契约（关了开关照样记遗魂、掉蛋、弹提示）。
                // 只在 Boss 死亡帧判一次，不是每帧热路径；判据本身是两次 null 检查
                // 加一个 no-throw 配置 getter，开启态成本可忽略。
                if (!IsEnabled(owner)) return;

                // 图鉴：按角色实例去重记一次血脉击杀
                PetNestMuseumStats.RecordKill(boss, lineageKey);

                // 非轨：遗魂必掉（纯账本，不掉实体）
                int souls = ComputeSoulReward(boss);
                if (souls > 0)
                {
                    // 只入队不落盘：一局可能几十次击杀，逐次 SaveFile 会拖帧。
                    // 官方 OnCollectSaveData 与切图/回基地的 flush 会把它写下去。
                    int before = PetNestService.GetSouls(lineageKey);
                    PetNestService.AddSouls(lineageKey, souls, false);
                    _stagedSoulWrites++;
                    NotifyCondensableCrossed(owner, lineageKey, before);
                }

                // 欧轨：低概率直掉遗种蛋
                if (UnityEngine.Random.value < PetNestTuning.EggDropChance)
                {
                    // roll 必须在 defer 判定之前：defer 记的是"这只 Boss 中了"，
                    // 不是"稍后再 roll"（与寒霜长矛同序）。
                    if (ShouldDeferToBossRushLootbox(owner, boss))
                    {
                        _pendingLootboxDrops[boss] = lineageKey;
                    }
                    else
                    {
                        TrySpawnEggIntoBossInventory(boss, lineageKey);
                    }
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种掉落结算失败: " + e.Message);
            }
        }

        /// <summary>
        /// 遗魂刚好攒够凝一枚蛋时提示一次。
        ///
        /// 不逐次击杀提示：无间炼狱一局几十次击杀会直接刷屏；
        /// 只在跨过「可凝蛋」阈值这一刻说一句，既有反馈又是玩家真正需要行动的时机。
        /// </summary>
        private static void NotifyCondensableCrossed(
            ModBehaviour owner, string lineageKey, int soulsBefore)
        {
            try
            {
                if (owner == null) return;
                int threshold = PetNestTuning.SoulsPerCondensedEgg;
                if (threshold <= 0) return;

                // 只在跨过阈值整数倍的那一刻提示
                int after = PetNestService.GetSouls(lineageKey);
                if (soulsBefore / threshold >= after / threshold) return;

                PetNestLineageInfo lineage;
                string lineageName = PetNestLineageCatalog.TryGet(lineageKey, out lineage)
                    && lineage != null && !string.IsNullOrEmpty(lineage.DisplayName)
                    ? lineage.DisplayName
                    : lineageKey;

                owner.ShowMessage(
                    LocalizationHelper.GetLocalizedText(
                        PetNestTuning.LocalizationPrefix + "SoulGained")
                    + " · " + lineageName + " · "
                    + LocalizationHelper.GetLocalizedText(
                        PetNestTuning.LocalizationPrefix + "CondenseEgg"));
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗魂提示失败: " + e.Message);
            }
        }

        /// <summary>
        /// 遗魂计量：镜像官方 SoulCollector 的 MaxHealth/15 公式口径。
        /// 血量越高的 Boss 给得越多，天然把"难打的血脉更难凑"压到合理区间。
        /// </summary>
        internal static int ComputeSoulReward(CharacterMainControl boss)
        {
            try
            {
                Health health = boss != null ? boss.Health : null;
                if (health == null) return PetNestTuning.MinSoulDropPerKill;
                float maxHealth = health.MaxHealth;
                if (maxHealth <= 0f) return PetNestTuning.MinSoulDropPerKill;
                // 官方 SoulCollector.cs:59 用的是 RoundToInt，这里镜像同一口径；
            // 用 Floor 会在同一 MaxHealth 下比官方少 1 点遗魂
            int souls = Mathf.RoundToInt(maxHealth / PetNestTuning.SoulDropHealthDivisor);
                return Mathf.Max(PetNestTuning.MinSoulDropPerKill, souls);
            }
            catch (Exception)
            {
                return PetNestTuning.MinSoulDropPerKill;
            }
        }

        private static void TrySpawnEggIntoBossInventory(CharacterMainControl boss, string lineageKey)
        {
            Item bossItem = boss != null ? boss.CharacterItem : null;
            Inventory inventory = bossItem != null ? bossItem.Inventory : null;
            if (inventory == null) return;
            TryAddEggToInventory(inventory, lineageKey, "Boss 库存");
        }

        /// <summary>
        /// 造一枚盖好血脉的蛋。造不出来或盖不上血脉都返回 null
        /// （盖不上的是废蛋，孵化侧 fail-closed，不如不掉）。
        /// </summary>
        private static Item TryCreateStampedEgg(string lineageKey)
        {
            Item egg = null;
            try
            {
                BossRushDynamicItemRegistry.EnsureRegistered(RelicEggConfig.TYPE_ID);
                egg = ItemAssetsCollection.InstantiateSync(RelicEggConfig.TYPE_ID);
                if (egg == null)
                {
                    ModBehaviour.DevLog("[PetNest] 遗种蛋实例化失败，本次只记遗魂");
                    return null;
                }

                if (!RelicEggConfig.TryStampLineage(egg, lineageKey))
                {
                    DestroyEggQuietly(egg);
                    return null;
                }
                return egg;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种蛋创建失败: " + e.Message);
                DestroyEggQuietly(egg);
                return null;
            }
        }

        private static void DestroyEggQuietly(Item egg)
        {
            if (egg == null) return;
            try { egg.DestroyTree(); }
            catch (Exception)
            {
                // 回收失败只丢引用，不阻断掉落流程
            }
        }

        /// <summary>把蛋加进指定库存。失败即销毁，绝不泄漏悬空 Item。</summary>
        private static void TryAddEggToInventory(
            Inventory inventory, string lineageKey, string destinationLabel)
        {
            if (inventory == null) return;
            Item egg = TryCreateStampedEgg(lineageKey);
            if (egg == null) return;

            try
            {
                EnsureExtraInventoryCapacity(inventory);
                if (!inventory.AddAndMerge(egg, 0))
                {
                    ModBehaviour.DevLog(
                        "[PetNest] 遗种蛋无法加入" + destinationLabel + "，本次只记遗魂");
                    DestroyEggQuietly(egg);
                    return;
                }
                ModBehaviour.DevLog(
                    "[PetNest] 掉落遗种蛋（" + destinationLabel + "），血脉=" + lineageKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种蛋掉落失败: " + e.Message);
                DestroyEggQuietly(egg);
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
        /// 并联到 AddBossSpecialLootToLootboxCoroutine：把 defer 的蛋投进 BossRush 奖励箱。
        /// 幂等：取出即从 pending 移除，重复调用不会掉两枚。
        /// </summary>
        internal static void TryConsumePendingBossRushLootboxDrop(
            CharacterMainControl boss, Inventory inventory)
        {
            PrunePendingEntries();
            if (boss == null || inventory == null) return;

            string lineageKey;
            if (!_pendingLootboxDrops.TryGetValue(boss, out lineageKey)) return;
            _pendingLootboxDrops.Remove(boss);

            TryAddEggToInventory(inventory, lineageKey, "BossRush 奖励箱");
        }

        /// <summary>
        /// 无间炼狱专用：那条分支根本不建箱子（`dropBoxOnDead=false` 后直接 return），
        /// 奖励通道是把物品 `Drop` 到世界里（里程碑现金就是这么发的）。
        /// 不接这条，无间炼狱下的蛋会和箱子一起消失。
        /// </summary>
        internal static void TryConsumePendingAsWorldDrop(
            CharacterMainControl boss, Vector3 position)
        {
            PrunePendingEntries();
            if (boss == null) return;

            string lineageKey;
            if (!_pendingLootboxDrops.TryGetValue(boss, out lineageKey)) return;
            _pendingLootboxDrops.Remove(boss);

            Item egg = TryCreateStampedEgg(lineageKey);
            if (egg == null) return;
            try
            {
                Vector3 dir = UnityEngine.Random.insideUnitSphere.normalized;
                egg.Drop(position, true, dir, UnityEngine.Random.Range(30f, 60f));
                ModBehaviour.DevLog("[PetNest] 掉落遗种蛋（世界掉落），血脉=" + lineageKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种蛋世界掉落失败: " + e.Message);
                DestroyEggQuietly(egg);
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

        /// <summary>
        /// 清掉已随场景销毁的 Boss 条目（key 是 Unity 对象，销毁后变成"假 null"）。
        ///
        /// 用「只保留存活项重建」而不是「逐个 Remove 死 key」——与
        /// `AffixForgeStoneDropService.PruneDestroyedEntries` 同一纪律：
        /// 已销毁 Unity 对象之间的相等性与哈希不可依赖，按死 key 去 Remove
        /// 可能只清掉其中一个、留下其余。重建没有这个问题。
        /// </summary>
        private static void PrunePendingEntries()
        {
            if (_pendingLootboxDrops.Count == 0) return;

            List<CharacterMainControl> alive = null;
            foreach (KeyValuePair<CharacterMainControl, string> pair in _pendingLootboxDrops)
            {
                if (pair.Key == null) continue;
                if (alive == null) alive = new List<CharacterMainControl>();
                alive.Add(pair.Key);
            }

            int aliveCount = alive != null ? alive.Count : 0;
            if (aliveCount == _pendingLootboxDrops.Count) return;

            _pendingScratch.Clear();
            for (int i = 0; i < aliveCount; i++)
            {
                CharacterMainControl key = alive[i];
                string lineageKey;
                if (_pendingLootboxDrops.TryGetValue(key, out lineageKey))
                {
                    _pendingScratch[key] = lineageKey;
                }
            }
            _pendingLootboxDrops.Clear();
            foreach (KeyValuePair<CharacterMainControl, string> pair in _pendingScratch)
            {
                _pendingLootboxDrops[pair.Key] = pair.Value;
            }
            _pendingScratch.Clear();
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
                // 扩容失败时 AddAndMerge 会自己失败，届时按"只记遗魂"降级
            }
        }

        #endregion

        #region 辅助

        private static bool IsEnabled(ModBehaviour owner)
        {
            try
            {
                if (owner == null) return false;
                PetNestRuntimeModule runtime = owner.PetNestRuntime;
                return runtime != null && runtime.IsEnabled && runtime.IsBootstrapped;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ResolveLineageKey(CharacterMainControl character)
        {
            try
            {
                CharacterRandomPreset preset = character.characterPreset;
                if (preset == null) return null;
                return string.IsNullOrEmpty(preset.nameKey) ? null : preset.nameKey;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region 清理

        /// <summary>静态缓存重置（Mod 卸载 / 宿主重建）。</summary>
        internal static void ResetStaticCaches()
        {
            ClearAllTracking();
            _hooks.Clear();
            _pendingLootboxDrops.Clear();
            _pendingScratch.Clear();
            _stagedSoulWrites = 0;
        }

        #endregion
    }
}
