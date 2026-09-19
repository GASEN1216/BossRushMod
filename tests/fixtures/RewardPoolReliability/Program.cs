using System;
using BossRush;
using ItemStatsSystem;

internal static class Program
{
    private static int checks, failures;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) { failures++; Console.WriteLine("FAIL " + message); }
    }
    private static void Reset(params int[] ids)
    {
        BossRushQualityItemPool.ResetStaticCaches();
        ItemAssetsCollection.Instance = new object();
        ItemAssetsCollection.Prefabs.Clear();
        ItemAssetsCollection.Order = ids;
        foreach (int id in ids) ItemAssetsCollection.Prefabs[id] = new Item(id, 4);
        ItemAssetsCollection.Queries = ItemAssetsCollection.Instantiations = 0;
        ItemAssetsCollection.QueryThrow = ItemAssetsCollection.InstantiateThrow = ItemAssetsCollection.InstantiateNull = false;
        CourierService.Attempts = 0; CourierService.Reject = CourierService.Fallback = false;
        CourierService.Delivered = null; LootBlacklistRegistry.Blocked.Clear();
    }
    private static bool Grant(int quality, long seed = 72)
    {
        string reason;
        return DailyReportRewards.TryGrantMilestone(quality, seed, 13, 2, out reason);
    }
    private static void ExactQuality()
    {
        Reset(101);
        Check(!Grant(7), "empty promised quality retains daily reward debt");
        Check(CourierService.Attempts == 0 && ItemAssetsCollection.Instantiations == 0,
            "empty quality never silently delivers a lower-grade reward");
        ItemAssetsCollection.Prefabs[101].Quality = 7;
        Check(Grant(7) && CourierService.Delivered.Quality == 7, "empty pool recovers when promised grade becomes available");
        Reset(101);
        LootBlacklistRegistry.Blocked.Add(101);
        Check(!Grant(4) && CourierService.Attempts == 0, "blacklist still excludes restricted content");
        LootBlacklistRegistry.Blocked.Clear();
        Check(Grant(4), "empty filtered pool is not cached");
    }
    private static void DeterministicOrder()
    {
        int[][] orders = { new[] { 101, 205, 309 }, new[] { 309, 101, 205 }, new[] { 205, 309, 101 } };
        for (long seed = 0; seed < 32; seed++)
        {
            int daily = 0, modeH = 0;
            foreach (int[] order in orders)
            {
                Reset(order);
                Check(Grant(4, seed), "daily pool grants valid reward");
                string reason;
                int pickedH = ModeHRewardItemPool.TryPickSameQualityTypeId(4, seed, "tx", 2, out reason);
                if (daily == 0) { daily = CourierService.Delivered.TypeID; modeH = pickedH; }
                else
                {
                    Check(CourierService.Delivered.TypeID == daily, "daily retry survives candidate enumeration permutation");
                    Check(pickedH == modeH, "Mode H replay survives candidate enumeration permutation");
                }
            }
        }
    }
    private static void MissingResource()
    {
        Reset(101);
        Check(Grant(4), "daily initial real item delivery");
        int queryCount = ItemAssetsCollection.Queries;
        UnityEngine.Object.Destroy(ItemAssetsCollection.Prefabs[101].gameObject);
        ItemAssetsCollection.Instantiations = CourierService.Attempts = 0;
        Check(!Grant(4), "cached daily prefab destroyed: keep unclaimed");
        Check(ItemAssetsCollection.Instantiations == 0 && CourierService.Attempts == 0,
            "daily missing prefab does not create or deliver same-ID fallback");
        Check(ItemAssetsCollection.Queries == queryCount, "cached retry does not scan full item table");
        ItemAssetsCollection.Prefabs[101] = new Item(101, 4);
        Check(Grant(4) && !CourierService.Delivered.Fallback, "daily restored prefab delivers actual reward");
        string reason;
        Check(ModeHRewardItemPool.TryInstantiate(999, out reason) == null,
            "Mode H missing prefab cannot become an escrow reward receipt");
        Check(ModeHRewardItemPool.TryInstantiate(101, out reason) != null, "Mode H real reward remains available");
        ItemAssetsCollection.InstantiateNull = true;
        Check(ModeHRewardItemPool.TryInstantiate(101, out reason) == null && !Grant(4), "null creation retains reward");
        ItemAssetsCollection.InstantiateNull = false;
        ItemAssetsCollection.InstantiateThrow = true;
        Check(ModeHRewardItemPool.TryInstantiate(101, out reason) == null && !Grant(4), "creation fault retains reward");
    }
    private static void SharedPoolReuse()
    {
        Reset(309, 101, 205);
        int[] shared = BossRushQualityItemPool.GetCandidates(4);
        int scans = ItemAssetsCollection.Queries;
        Check(Grant(4) && ItemAssetsCollection.Queries == scans, "daily rewards reuse the already built shared pool");
        Check(ReferenceEquals(shared, BossRushQualityItemPool.GetCandidates(4)), "both callers share one candidate array");
        Reset(101);
        ItemAssetsCollection.QueryThrow = true;
        Check(!Grant(4), "query exception retains reward");
        ItemAssetsCollection.QueryThrow = false;
        Check(Grant(4), "query exception is not cached");
    }
    private static void DeliveryAndRetry()
    {
        Reset(101);
        CourierService.Reject = true;
        Check(!Grant(4) && ItemAssetsCollection.LastCreated == null, "failed unowned reward cleaned up");
        CourierService.Reject = false; CourierService.Fallback = true;
        Check(Grant(4) && CourierService.Delivered != null, "successful fallback delivery kept alive");
        int calls = ItemAssetsCollection.Queries;
        Check(Grant(4) && ItemAssetsCollection.Queries == calls, "nonempty cache reused without full-table scan");
        Reset(101);
        ItemAssetsCollection.Instance = null;
        Check(!Grant(4), "unready assets retain reward");
        ItemAssetsCollection.Instance = new object();
        Check(Grant(4), "asset readiness retry succeeds");
    }
    private static int Main()
    {
        ExactQuality(); DeterministicOrder(); MissingResource(); DeliveryAndRetry(); SharedPoolReuse();
        Console.WriteLine("RewardPoolReliability: " + checks + " checks, " + failures + " failures");
        return failures == 0 ? 0 : 1;
    }
}
