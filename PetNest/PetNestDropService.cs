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
                Action<DamageInfo> handler = delegate (DamageInfo info)
                {
                    OnBossBeforeSpawnLoot(captured, lineageKey);
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

        private static void OnBossBeforeSpawnLoot(CharacterMainControl boss, string lineageKey)
        {
            try
            {
                if (boss == null || string.IsNullOrEmpty(lineageKey)) return;

                // 非轨：遗魂必掉（纯账本，不掉实体）
                int souls = ComputeSoulReward(boss);
                if (souls > 0)
                {
                    // 只入队不落盘：一局可能几十次击杀，逐次 SaveFile 会拖帧。
                    // 官方 OnCollectSaveData 与切图/回基地的 flush 会把它写下去。
                    PetNestService.AddSouls(lineageKey, souls, false);
                    PetNestService.StageCommit();
                    _stagedSoulWrites++;
                }

                // 欧轨：低概率直掉遗种蛋
                if (UnityEngine.Random.value < PetNestTuning.EggDropChance)
                {
                    TrySpawnEggIntoBossInventory(boss, lineageKey);
                }
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种掉落结算失败: " + e.Message);
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
                int souls = Mathf.FloorToInt(maxHealth / PetNestTuning.SoulDropHealthDivisor);
                return Mathf.Max(PetNestTuning.MinSoulDropPerKill, souls);
            }
            catch (Exception)
            {
                return PetNestTuning.MinSoulDropPerKill;
            }
        }

        private static void TrySpawnEggIntoBossInventory(CharacterMainControl boss, string lineageKey)
        {
            Item egg = null;
            try
            {
                Item bossItem = boss.CharacterItem;
                Inventory inventory = bossItem != null ? bossItem.Inventory : null;
                if (inventory == null) return;

                BossRushDynamicItemRegistry.EnsureRegistered(RelicEggConfig.TYPE_ID);
                egg = ItemAssetsCollection.InstantiateSync(RelicEggConfig.TYPE_ID);
                if (egg == null)
                {
                    ModBehaviour.DevLog("[PetNest] 遗种蛋实例化失败，本次只记遗魂");
                    return;
                }

                if (!RelicEggConfig.TryStampLineage(egg, lineageKey))
                {
                    // 血脉写不进去的蛋是废蛋（孵化侧会 fail-closed），不如不掉
                    return;
                }

                EnsureExtraInventoryCapacity(inventory);
                if (!inventory.AddAndMerge(egg, 0))
                {
                    ModBehaviour.DevLog("[PetNest] 遗种蛋无法加入 Boss 库存，本次只记遗魂");
                    return;
                }

                egg = null;
                ModBehaviour.DevLog("[PetNest] 掉落遗种蛋，血脉=" + lineageKey);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[PetNest] 遗种蛋掉落失败: " + e.Message);
            }
            finally
            {
                try
                {
                    if (egg != null) egg.DestroyTree();
                }
                catch (Exception)
                {
                    // 回收失败只丢引用，不阻断掉落流程
                }
            }
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
            _stagedSoulWrites = 0;
        }

        #endregion
    }
}
