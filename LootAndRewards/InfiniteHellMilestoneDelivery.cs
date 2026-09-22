using System;
using UnityEngine;
using ItemStatsSystem;
using Duckov.Economy;

namespace BossRush
{
    // 原奖励按 tier 翻倍；以合法物品堆叠表示，超出实体预算的部分按完整标价到账户。
    // 每帧最多生成 8 件，只有持有未发里程碑时 Tick；失败余额留在当前局的唯一 owner 中重试。
    internal sealed class InfiniteHellMilestoneDelivery
    {
        private const int PhysicalCrowns = 100;
        private const int PhysicalCashPiles = 100;
        private int completedTier;
        private int requestedTier;
        private int activeTier;
        private long cashRemaining;
        private long crownsRemaining;
        private long accountRemaining;
        private int crownDrops;
        private int cashDrops;
        private int cashStackLimit;
        private long cashPerPile;
        private int crownValue;
        private float retryAt;
        private Vector3 position;
        private bool converted;

        internal static long SaturatingReward(int tier, long unit)
        {
            if (tier <= 0 || unit <= 0) return 0;
            // 先按宿主 long 的范围收敛，避免 C# 的移位计数回绕及乘法溢出。
            for (int i = 1; i < tier; i++)
            {
                if (unit > long.MaxValue / 2) return long.MaxValue;
                unit *= 2;
            }
            return unit;
        }

        private static long SaturatingAdd(long a, long b)
        {
            return a > long.MaxValue - b ? long.MaxValue : a + b;
        }

        internal void Enqueue(int tier, Vector3 dropPosition)
        {
            requestedTier = Math.Max(requestedTier, tier);
            position = dropPosition;
        }

        internal void Tick(ModBehaviour owner)
        {
            if (completedTier >= requestedTier || Time.unscaledTime < retryAt) return;
            if (owner == null || !owner.IsActive || BossRushUI.IsGamePaused()) return;
            try
            {
                if (activeTier == 0)
                {
                    Item crown = ItemAssetsCollection.GetPrefab(1254);
                    Item cash = ItemAssetsCollection.GetPrefab(EconomyManager.CashItemID);
                    if (crown == null || cash == null || EconomyManager.Instance == null)
                    { retryAt = Time.unscaledTime + 1f; return; }
                    crownValue = crown.GetTotalRawValue();
                    if (crownValue <= 0 || cash.MaxStackCount <= 0)
                    { retryAt = Time.unscaledTime + 1f; return; }
                    activeTier = completedTier + 1;
                    crownsRemaining = SaturatingReward(activeTier, 1);
                    cashRemaining = SaturatingReward(activeTier, 10000000);
                    cashPerPile = Math.Max(1, cashRemaining / PhysicalCashPiles);
                    cashStackLimit = cash.MaxStackCount;
                    crownDrops = (int)Math.Min(PhysicalCrowns, crownsRemaining);
                    cashDrops = PhysicalCashPiles;
                    converted = false;
                }
                for (int budget = 0; budget < 8 && (crownDrops > 0 || cashDrops > 0); budget++)
                {
                    bool isCrown = crownDrops > 0;
                    int typeId = isCrown ? 1254 : EconomyManager.CashItemID;
                    int amount = isCrown ? 1 : (int)Math.Min(cashRemaining, Math.Min(cashPerPile, (long)cashStackLimit));
                    Item item = null;
                    bool delivered = false;
                    try
                    {
                        item = ItemAssetsCollection.InstantiateSync(typeId);
                        if (item == null) { retryAt = Time.unscaledTime + 1f; return; }
                        item.StackCount = amount;
                        item.Drop(position, true, UnityEngine.Random.insideUnitSphere.normalized, UnityEngine.Random.Range(30f, 60f));
                        delivered = true;
                    }
                    finally
                    {
                        if (!delivered && item != null) item.DestroyTree();
                    }
                    if (isCrown) { crownDrops--; crownsRemaining--; }
                    else { cashDrops--; cashRemaining -= amount; }
                }
                if (crownDrops > 0 || cashDrops > 0) return;
                if (!converted)
                {
                    long crownCash = crownsRemaining > long.MaxValue / crownValue
                        ? long.MaxValue : crownsRemaining * crownValue;
                    accountRemaining = SaturatingAdd(cashRemaining, crownCash);
                    converted = true;
                    if (accountRemaining > 0)
                        owner.ShowMessage(L10n.T("里程碑超量奖励已按完整标价折现，转入账户。", "Excess milestone rewards are converted at full item value and credited to your account."));
                }
                if (accountRemaining > 0)
                {
                    if (EconomyManager.Instance == null) { retryAt = Time.unscaledTime + 1f; return; }
                    long before = Math.Max(0, EconomyManager.Money);
                    long credit = Math.Min(accountRemaining, long.MaxValue - before);
                    if (credit > 0)
                    {
                        try
                        {
                            if (!EconomyManager.Add(credit)) { retryAt = Time.unscaledTime + 1f; return; }
                        }
                        catch
                        {
                            // 官方 Add 先写余额再广播；广播异常时按回读扣除已到账的部分。
                            accountRemaining -= Math.Min(credit, Math.Max(0, EconomyManager.Money - before));
                            retryAt = Time.unscaledTime + 1f;
                            return;
                        }
                    }
                    // 超过宿主 long 余额可表示范围时饱和，不允许回绕为负数。
                    accountRemaining = 0;
                }
                completedTier = activeTier;
                activeTier = 0;
            }
            catch (Exception e)
            {
                retryAt = Time.unscaledTime + 1f;
                ModBehaviour.DevLog("[InfiniteHell] 里程碑奖励暂缓重试: " + e.Message);
            }
        }
    }
}
