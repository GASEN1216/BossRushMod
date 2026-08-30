// ============================================================================
// AffixForgeSystem.cs - 词缀锻造的规则与结算（roll / 锁定 / 材料与金钱消耗）
// ============================================================================
// 职责：
//   - 判定装备能否附加词缀、能开几个槽、一次锻造要多少钱与多少词缀熔石。
//   - 执行"重铸全部未锁槽"与"锁定 / 解锁单槽"，并把结果写进 AFX_ KV。
//
// 硬约束：
//   1. **KV 只经 AffixItemData 写**。本文件不直接碰 Item.Variables。
//   2. **结算顺序：先扣钱、后扣材料**。金钱可以用 EconomyManager.Add 原路退回，
//      而消耗掉的物品无法安全复原；因此把不可逆的那一步放最后，
//      材料扣失败时立刻退钱，绝不出现"钱扣了材料没扣"的黑洞。
//   3. **锁定不得锁满**：至少留一个未锁槽，否则重铸按钮永远无事可做。
//   4. **未知 id fail-open**：读到本版本不认识的词缀时保留 KV 原样，
//      既不清空也不参与去重以外的任何判断。
//   5. 本文件零事件订阅、零 Harmony；行为面在 AffixRuntimeService。
// ============================================================================

using System;
using System.Collections.Generic;
using Duckov.Economy;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>一次锻造操作的结果。UI 用 Before/After 做前后对比展示。</summary>
    public sealed class AffixForgeResult
    {
        /// <summary>是否成功。</summary>
        public bool Success;

        /// <summary>失败原因（已本地化）。成功时为 null。</summary>
        public string ErrorMessage;

        /// <summary>实际消耗的词缀熔石数量。</summary>
        public int StonesConsumed;

        /// <summary>实际支付的金钱。</summary>
        public int MoneyPaid;

        /// <summary>操作前的槽位快照（含空槽，按槽位顺序）。</summary>
        public List<AffixSlotView> Before = new List<AffixSlotView>();

        /// <summary>操作后的槽位快照（含空槽，按槽位顺序）。</summary>
        public List<AffixSlotView> After = new List<AffixSlotView>();
    }

    /// <summary>词缀锻造的规则与结算。全部方法 no-throw，失败时返回带中文原因的结果对象。</summary>
    public static class AffixForgeSystem
    {
        /// <summary>
        /// 可参与词缀锻造的 Tag 白名单（枪 / 近战 / 护甲 / 头盔 / 面罩，**不含**图腾、
        /// 背包、耳机）。实际判定走 <see cref="AffixItemData.GetEquipMask"/>，
        /// 本数组保留给 UI 与 guard 做说明与断言。
        /// </summary>
        public static readonly string[] AffixForgeableTags =
        {
            "Armor",
            "Helmet",
            "Helmat",       // 官方原版拼写
            "FaceMask",
            "Weapon",
            "Gun",
            "MeleeWeapon",
            "Melee"
        };

        /// <summary>roll 用随机源。懒建，进程内共用一份。</summary>
        private static System.Random _rng;

        /// <summary>候选池复用缓冲，避免每次 roll 都分配。</summary>
        private static readonly List<AffixDefinition> _candidateScratch = new List<AffixDefinition>();

        #region 资格与费用

        /// <summary>词缀锻造总开关（no-throw；缺配置按关闭处理）。</summary>
        public static bool IsForgeEnabled()
        {
            try
            {
                return ModBehaviour.Instance != null && ModBehaviour.Instance.IsAffixForgeConfiguredEnabled();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>该物品能否进入词缀锻造流程。</summary>
        public static bool CanAffixForge(Item item)
        {
            if (item == null) return false;
            try
            {
                if (item.Quality < 1) return false;
                return AffixItemData.IsAffixEligible(item);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>该物品的词缀槽位数（首铸后以冻结在 AFX_CAP 的值为准）。</summary>
        public static int GetSlotCount(Item item)
        {
            return AffixItemData.GetCapacity(item);
        }

        /// <summary>一次重铸的金钱费用（复用重铸的价格曲线与哥布林好感度折扣）。</summary>
        public static int GetMoneyCost(Item item)
        {
            if (item == null) return AffixDefinitions.MinMoneyCost;
            try
            {
                return ReforgeSystem.GetDiscountedCost(item);
            }
            catch (Exception)
            {
                return AffixDefinitions.MinMoneyCost;
            }
        }

        /// <summary>一次重铸消耗的词缀熔石数量。</summary>
        public static int GetStoneCost(Item item)
        {
            return AffixDefinitions.ForgeStoneCostPerRoll;
        }

        /// <summary>锁定一个槽消耗的词缀熔石数量（解锁免费）。</summary>
        public static int GetLockStoneCost()
        {
            return AffixDefinitions.ForgeStoneCostPerLock;
        }

        /// <summary>玩家背包里的词缀熔石数量。</summary>
        public static int GetOwnedStoneCount()
        {
            try
            {
                return ItemFactory.GetItemCountInInventory(AffixForgeStoneConfig.TYPE_ID);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>当前未被锁定的槽位数（= 一次重铸会重掷的槽数）。</summary>
        public static int GetUnlockedSlotCount(Item item)
        {
            if (item == null) return 0;
            int capacity = GetSlotCount(item);
            int count = 0;
            for (int i = 1; i <= capacity; i++)
            {
                if (!AffixItemData.IsLocked(item, i)) count++;
            }
            return count;
        }

        #endregion

        #region 重铸

        /// <summary>
        /// 重掷全部未锁槽。扣钱 → 扣熔石 → 写 KV → 通知运行时服务重建 context。
        /// 已锁定的槽保持不变，且它们的词缀 id 会计入本次去重排除集。
        /// </summary>
        public static AffixForgeResult RollUnlockedSlots(Item item)
        {
            AffixForgeResult result = new AffixForgeResult();

            try
            {
                if (!IsForgeEnabled())
                {
                    return Fail(result, L10n.T("词缀锻造功能未启用。", "Affix forging is disabled."));
                }
                if (item == null || !CanAffixForge(item))
                {
                    return Fail(result, L10n.T("该装备无法附加词缀。", "This gear cannot carry affixes."));
                }

                int capacity = GetSlotCount(item);
                if (capacity < 1)
                {
                    return Fail(result, L10n.T("该装备没有词缀槽。", "This gear has no affix slots."));
                }

                SnapshotSlots(item, capacity, result.Before);

                if (GetUnlockedSlotCount(item) <= 0)
                {
                    return Fail(result, L10n.T("全部词缀槽都已锁定，没有可重铸的槽。",
                        "Every affix slot is locked; there is nothing to reroll."));
                }

                int stoneCost = GetStoneCost(item);
                int moneyCost = GetMoneyCost(item);

                if (GetOwnedStoneCount() < stoneCost)
                {
                    return Fail(result, L10n.T("词缀熔石不足。", "Not enough Affix Forge Stones."));
                }

                Cost cost = new Cost((long)moneyCost);
                if (!EconomyManager.IsEnough(cost, true, true))
                {
                    return Fail(result, L10n.T("金钱不足。", "Not enough money."));
                }

                // 先扣钱（可原路退回），再扣材料（不可逆）
                if (!EconomyManager.Pay(cost, true, true))
                {
                    return Fail(result, L10n.T("扣款失败。", "Payment failed."));
                }
                if (!ItemFactory.ConsumeItem(AffixForgeStoneConfig.TYPE_ID, stoneCost))
                {
                    EconomyManager.Add(moneyCost);
                    return Fail(result, L10n.T("词缀熔石扣除失败，已退还费用。",
                        "Failed to consume Affix Forge Stones; the money was refunded."));
                }

                result.MoneyPaid = moneyCost;
                result.StonesConsumed = stoneCost;

                AffixItemData.EnsureInitialized(item, capacity);
                ApplyRoll(item, capacity);
                SnapshotSlots(item, capacity, result.After);

                NotifyRuntime(item);

                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeSystem] 词缀重铸异常: " + e.Message);
                return Fail(result, L10n.T("词缀锻造出错，本次操作已中止。",
                    "Affix forging failed; the operation was aborted."));
            }
        }

        /// <summary>执行实际的槽位重掷（不含任何费用结算）。</summary>
        private static void ApplyRoll(Item item, int capacity)
        {
            System.Random rng = GetRng();
            AffixEquipMask mask = AffixItemData.GetEquipMask(item);

            // 已锁槽的 id 计入排除集：同一件装备不出现重复词缀
            HashSet<string> exclude = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i <= capacity; i++)
            {
                AffixSlotView view;
                if (!AffixItemData.TryReadSlot(item, i, out view)) continue;
                if (view.Locked && !view.IsEmpty) exclude.Add(view.AffixId);
            }

            for (int i = 1; i <= capacity; i++)
            {
                AffixSlotView view;
                if (!AffixItemData.TryReadSlot(item, i, out view)) continue;
                if (view.Locked && !view.IsEmpty) continue;

                string rolled = RollAffixId(rng, exclude, mask);
                if (string.IsNullOrEmpty(rolled))
                {
                    // 候选池被排除干净（槽数 > 可用词缀数）：清空该槽而不是塞重复词缀
                    AffixItemData.ClearSlot(item, i);
                    continue;
                }

                int tier = RollTier(rng);
                if (AffixItemData.WriteSlot(item, i, rolled, tier))
                {
                    exclude.Add(rolled);
                }
            }
        }

        #endregion

        #region 锁定

        /// <summary>锁定一个已有词缀的槽（消耗词缀熔石，不花钱）。</summary>
        public static AffixForgeResult LockSlot(Item item, int slotIndex)
        {
            AffixForgeResult result = new AffixForgeResult();

            try
            {
                if (!IsForgeEnabled())
                {
                    return Fail(result, L10n.T("词缀锻造功能未启用。", "Affix forging is disabled."));
                }
                if (item == null || !CanAffixForge(item))
                {
                    return Fail(result, L10n.T("该装备无法附加词缀。", "This gear cannot carry affixes."));
                }

                int capacity = GetSlotCount(item);
                if (slotIndex < 1 || slotIndex > capacity)
                {
                    return Fail(result, L10n.T("词缀槽不存在。", "That affix slot does not exist."));
                }

                SnapshotSlots(item, capacity, result.Before);

                AffixSlotView view;
                if (!AffixItemData.TryReadSlot(item, slotIndex, out view) || view.IsEmpty)
                {
                    return Fail(result, L10n.T("空槽无法锁定。", "An empty slot cannot be locked."));
                }
                if (view.Locked)
                {
                    return Fail(result, L10n.T("该词缀槽已经锁定。", "That affix slot is already locked."));
                }
                // 至少留一个未锁槽，否则重铸永远无事可做
                if (GetUnlockedSlotCount(item) <= 1)
                {
                    return Fail(result, L10n.T("至少要留一个未锁定的词缀槽。",
                        "At least one affix slot must stay unlocked."));
                }

                int stoneCost = GetLockStoneCost();
                if (GetOwnedStoneCount() < stoneCost)
                {
                    return Fail(result, L10n.T("词缀熔石不足。", "Not enough Affix Forge Stones."));
                }
                if (!ItemFactory.ConsumeItem(AffixForgeStoneConfig.TYPE_ID, stoneCost))
                {
                    return Fail(result, L10n.T("词缀熔石扣除失败。",
                        "Failed to consume Affix Forge Stones."));
                }

                AffixItemData.SetLock(item, slotIndex, true);
                result.StonesConsumed = stoneCost;
                SnapshotSlots(item, capacity, result.After);
                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeSystem] 锁定词缀槽异常: " + e.Message);
                return Fail(result, L10n.T("锁定失败，本次操作已中止。",
                    "Locking failed; the operation was aborted."));
            }
        }

        /// <summary>解锁一个槽（免费，不消耗任何资源）。</summary>
        public static AffixForgeResult UnlockSlot(Item item, int slotIndex)
        {
            AffixForgeResult result = new AffixForgeResult();

            try
            {
                if (item == null || !CanAffixForge(item))
                {
                    return Fail(result, L10n.T("该装备无法附加词缀。", "This gear cannot carry affixes."));
                }

                int capacity = GetSlotCount(item);
                if (slotIndex < 1 || slotIndex > capacity)
                {
                    return Fail(result, L10n.T("词缀槽不存在。", "That affix slot does not exist."));
                }

                SnapshotSlots(item, capacity, result.Before);

                if (!AffixItemData.IsLocked(item, slotIndex))
                {
                    return Fail(result, L10n.T("该词缀槽没有锁定。", "That affix slot is not locked."));
                }

                AffixItemData.SetLock(item, slotIndex, false);
                SnapshotSlots(item, capacity, result.After);
                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeSystem] 解锁词缀槽异常: " + e.Message);
                return Fail(result, L10n.T("解锁失败，本次操作已中止。",
                    "Unlocking failed; the operation was aborted."));
            }
        }

        #endregion

        #region roll 内核

        /// <summary>抽稀有档。权重之和不必刚好 100，按实际总和归一。</summary>
        internal static AffixRarity RollRarity(System.Random rng)
        {
            if (rng == null) rng = GetRng();

            float total = AffixDefinitions.RarityWeightCommon
                + AffixDefinitions.RarityWeightRare
                + AffixDefinitions.RarityWeightCurse;
            if (total <= 0f) return AffixRarity.Common;

            float roll = (float)rng.NextDouble() * total;
            if (roll < AffixDefinitions.RarityWeightCommon) return AffixRarity.Common;
            roll -= AffixDefinitions.RarityWeightCommon;
            if (roll < AffixDefinitions.RarityWeightRare) return AffixRarity.Rare;
            return AffixRarity.Curse;
        }

        /// <summary>抽强度档（1..3）。</summary>
        internal static int RollTier(System.Random rng)
        {
            if (rng == null) rng = GetRng();

            float total = AffixDefinitions.TierWeightT1
                + AffixDefinitions.TierWeightT2
                + AffixDefinitions.TierWeightT3;
            if (total <= 0f) return 1;

            float roll = (float)rng.NextDouble() * total;
            if (roll < AffixDefinitions.TierWeightT1) return 1;
            roll -= AffixDefinitions.TierWeightT1;
            if (roll < AffixDefinitions.TierWeightT2) return 2;
            return 3;
        }

        /// <summary>
        /// 抽一条词缀 id（不限装备类型）。exclude 里的 id 不会被抽中。
        /// 候选池为空时返回 null，调用方应把该槽清空而不是塞重复词缀。
        /// </summary>
        internal static string RollAffixId(System.Random rng, HashSet<string> exclude)
        {
            return RollAffixId(rng, exclude, AffixEquipMask.All);
        }

        /// <summary>
        /// 抽一条能落在 mask 类型上的词缀 id。先按档抽，档内候选为空时
        /// 退回全档候选（保证有解），仍为空返回 null。
        /// </summary>
        internal static string RollAffixId(System.Random rng, HashSet<string> exclude, AffixEquipMask mask)
        {
            if (rng == null) rng = GetRng();
            if (mask == AffixEquipMask.None) mask = AffixEquipMask.All;

            AffixRarity rarity = RollRarity(rng);

            AffixDefinition picked = PickFrom(rng, exclude, mask, true, rarity);
            if (picked == null)
            {
                picked = PickFrom(rng, exclude, mask, false, rarity);
            }
            return picked == null ? null : picked.Id;
        }

        /// <summary>把候选收进复用缓冲并均匀抽一条。matchRarity=false 表示忽略档位限制。</summary>
        private static AffixDefinition PickFrom(System.Random rng, HashSet<string> exclude,
            AffixEquipMask mask, bool matchRarity, AffixRarity rarity)
        {
            _candidateScratch.Clear();

            IReadOnlyList<AffixDefinition> all = AffixDefinitions.GetAll();
            for (int i = 0; i < all.Count; i++)
            {
                AffixDefinition def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Id)) continue;
                if (matchRarity && def.Rarity != rarity) continue;
                if (!AffixDefinitions.IsApplicableTo(def, mask)) continue;
                if (exclude != null && exclude.Contains(def.Id)) continue;
                _candidateScratch.Add(def);
            }

            if (_candidateScratch.Count == 0) return null;
            int index = rng.Next(_candidateScratch.Count);
            AffixDefinition result = _candidateScratch[index];
            _candidateScratch.Clear();
            return result;
        }

        private static System.Random GetRng()
        {
            if (_rng == null)
            {
                // Time 只在 Unity 主线程可读，取不到时退回时间戳种子
                int seed;
                try { seed = Environment.TickCount ^ Mathf.RoundToInt(Time.realtimeSinceStartup * 1000f); }
                catch (Exception) { seed = Environment.TickCount; }
                _rng = new System.Random(seed);
            }
            return _rng;
        }

        #endregion

        #region 辅助

        /// <summary>把 1..capacity 全部槽（含空槽）按顺序快照进 buffer。</summary>
        private static void SnapshotSlots(Item item, int capacity, List<AffixSlotView> buffer)
        {
            if (buffer == null) return;
            buffer.Clear();
            if (item == null) return;

            for (int i = 1; i <= capacity; i++)
            {
                AffixSlotView view;
                if (!AffixItemData.TryReadSlot(item, i, out view))
                {
                    view = new AffixSlotView();
                    view.SlotIndex = i;
                }
                buffer.Add(view);
            }
        }

        /// <summary>通知行为面重建 context。手持中的装备改词缀必须立刻生效。</summary>
        private static void NotifyRuntime(Item item)
        {
            try
            {
                AffixRuntimeService.NotifyItemAffixesChanged(item);
            }
            catch (Exception e)
            {
                ModBehaviour.DevLog("[AffixForgeSystem] 通知词缀运行时失败: " + e.Message);
            }
        }

        private static AffixForgeResult Fail(AffixForgeResult result, string message)
        {
            result.Success = false;
            result.ErrorMessage = message;
            return result;
        }

        #endregion
    }
}
