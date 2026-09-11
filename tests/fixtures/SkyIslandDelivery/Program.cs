using System;
using BossRush;
using ItemStatsSystem;

class Program
{
    static int checks;
    static void Check(bool ok, string text) { if (!ok) throw new Exception("FAIL " + text); checks++; Console.WriteLine("PASS " + text); }
    static void Reset() { ItemUtilities.ThrowBefore = false; ItemUtilities.ThrowAfterOwnership = false; ItemUtilities.ThrowAfterBuffer = false; SkyIslandInventoryTransaction.Buffer.Clear(); ItemAssetsCollection.MissingPrefab = false; }
    static void Main()
    {
        int calls;
        Reset(); calls = 0; Func<bool> r = () => { calls++; return true; }; Check(SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, false, r), "ownership before notification throw counts as delivered"); Check(calls == 1, "successful record is written once");
        Reset(); ItemUtilities.ThrowAfterBuffer = true; calls = 0; r = () => { calls++; return true; }; Check(SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, true, r), "precise buffer receipt survives post-buffer throw"); Check(SkyIslandInventoryTransaction.Buffer.Count == 1, "buffer receipt remains available");
        Reset(); ItemUtilities.ThrowBefore = true; bool rolled = false; Check(!SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, false, () => true, () => { rolled = true; return true; }), "undelivered item returns false"); Check(rolled, "undelivered item rolls back accepted ledger");
        Reset(); ItemUtilities.ThrowBefore = true; bool rollbackCalled = false; Check(!SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, false, () => true, () => { rollbackCalled = true; throw new InvalidOperationException("rollback failed"); }), "rollback failure remains retry-blocked"); Check(rollbackCalled, "rollback failure is attempted exactly once");
        Reset(); Check(!SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, false, () => false, () => { throw new Exception("must not run"); }), "ledger rejection prevents transfer");
        Reset(); ItemAssetsCollection.MissingPrefab = true; Check(!SkyIslandItems.TryGive(BossRushItemIds.SkyIslandHomecomingBadge, false, () => { throw new Exception("must not run"); }), "missing prefab leaves claim retryable");
        Console.WriteLine("checks=" + checks);
    }
}
