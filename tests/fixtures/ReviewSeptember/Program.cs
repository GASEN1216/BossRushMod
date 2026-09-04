using System;
using BossRush;
using ItemStatsSystem;
using ItemStatsSystem.Data;

class Program
{
    static int assertions;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        assertions++;
    }
    static void Main()
    {
        Coordinator();
        Restoration();
        Console.WriteLine("PASS: " + assertions + " assertions against production sources (host IO stubbed; no game smoke)");
    }
    static void Coordinator()
    {
        string error;
        UnityEngine.Time.frameCount = 100;
        for (int i = 0; i < 4; i++)
            Check(ModeHSaveFlushCoordinator.RequestStakeJournalWrite(new ModeHStakeJournalDto(), out error), "same-frame stake barrier " + i);
        Check(Saves.SavesSystem.PhysicalWrites == 4, "all four stake barriers actually write");
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(new ModeHSeasonDto(), out error), "ordinary frame throttle");
        Check(!ModeHProfilePersistence.HasPendingWrite, "typed queue already consumed");
        UnityEngine.Time.frameCount++;
        ModeHSaveFlushCoordinator.Tick();
        Check(Saves.SavesSystem.PhysicalWrites == 5 && !ModeHSaveFlushCoordinator.HasDeferredFlush, "throttle debt survives");
        ModeHRuntimeGates.IsModeHCombatFrameActive = true;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(new ModeHSeasonDto(), out error), "combat defers");
        for (int i = 0; i < 700; i++) { UnityEngine.Time.frameCount++; ModeHSaveFlushCoordinator.Tick(); }
        Check(!ModeHRuntimeGates.Blocked && ModeHSaveFlushCoordinator.HasDeferredFlush, "combat does not exhaust retries");
        ModeHRuntimeGates.IsModeHCombatFrameActive = false;
        ModeHSaveFlushCoordinator.Tick();
        Check(Saves.SavesSystem.PhysicalWrites == 6, "post-combat physical write");
        UnityEngine.Time.frameCount++;
        Saves.SavesSystem.FailNext = true;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(new ModeHSeasonDto(), out error), "injected IO exception");
        UnityEngine.Time.frameCount++;
        ModeHSaveFlushCoordinator.Tick();
        Check(Saves.SavesSystem.PhysicalWrites == 7, "IO debt retries after typed queue empty");
        Saves.SavesSystem.IsSaving = true;
        Check(!ModeHSaveFlushCoordinator.RequestSeasonWrite(new ModeHSeasonDto(), out error), "official save busy");
        Saves.SavesSystem.IsSaving = false;
        Check(ModeHSaveFlushCoordinator.TryFlushOnHostDestroy(), "host destroy bypasses frame throttle");
        Check(Saves.SavesSystem.PhysicalWrites == 8, "host destroy writes debt");
        ModeHRuntimeGates.IsModeHCombatFrameActive = true;
        ModeHSaveFlushCoordinator.RequestSeasonWrite(new ModeHSeasonDto(), out error);
        ModeHSaveFlushCoordinator.ResetStaticCaches();
        ModeHRuntimeGates.IsModeHCombatFrameActive = false;
        UnityEngine.Time.frameCount++;
        ModeHSaveFlushCoordinator.Tick();
        Check(Saves.SavesSystem.PhysicalWrites == 8, "slot reset cannot replay old debt");
    }
    static void Restoration()
    {
        Item item = new Item { Tree = new ItemTreeData { rootInstanceID = 910 } };
        var root = new ItemTreeData.DataEntry { instanceID = 910, typeID = 500030 };
        root.variables.Add(new CustomData("durability", CustomDataType.Float, BitConverter.GetBytes(0.625f)) { Display = true });
        root.variables.Add(new CustomData("AFX_owner", CustomDataType.String, System.Text.Encoding.UTF8.GetBytes("完整变量")));
        root.slotContents.Add(new ItemTreeData.SlotInstanceIDPair("magazine", 912));
        root.inventory.Add(new ItemTreeData.InventoryDataEntry(7, 999));
        root.inventorySortLocks.Add(7);
        var ammo = new ItemTreeData.DataEntry { instanceID = 912, typeID = 42 };
        ammo.variables.Add(new CustomData("Count", CustomDataType.Int, BitConverter.GetBytes(17)));
        var child = new ItemTreeData.DataEntry { instanceID = 999, typeID = 43 };
        item.Tree.entries.Add(root); item.Tree.entries.Add(ammo); item.Tree.entries.Add(child);
        string error;
        var snapshot = ModeHItemTreeNormalizer.TryCapture(item, 4, 2, out error);
        Check(snapshot != null, "capture nested tree");
        var restored = ModeHItemTreeNormalizer.TryRestore(snapshot, out error);
        Check(restored != null, "restore nested tree: " + error);
        Check(restored.entries.Count == 3 && restored.RootData.inventory[0].position == 7, "attachments and container positions");
        Check(restored.entries[1].StackCount == 17 && restored.RootData.inventorySortLocks[0] == 7, "stack and sort locks");
        Check(restored.RootData.variables.Find(v => v.Key == "durability").Display, "display flag");
        Check(restored.RootData.variables.Find(v => v.Key == "AFX_owner").DataType == CustomDataType.String, "dynamic variable type");
        string payload;
        Check(ModeHItemTreeNormalizer.TryWriteNormalizedPayload(restored, out payload, out error)
            && payload == snapshot.normalizedTreePayload, "byte-exact normalized roundtrip across instance IDs");
        string saved = snapshot.semanticTreeDigest;
        snapshot.semanticTreeDigest = new string('0', 64);
        Check(ModeHItemTreeNormalizer.TryRestore(snapshot, out error) == null, "reject forged digest");
        snapshot.semanticTreeDigest = saved;
        snapshot.gameQuality = 6;
        Check(ModeHItemTreeNormalizer.TryRestore(snapshot, out error) == null, "reject changed quality");
        snapshot.gameQuality = 4;
        var legacy = ModeHItemTreeNormalizer.TryCapture(item, 4, 2, out error, false);
        Check(ModeHItemTreeNormalizer.TryRestore(legacy, out error) == null, "legacy unknown variable types must not be guessed");
        Duckov.Utilities.ItemAssetsCollection.Prefab = new Item();
        Duckov.Utilities.ItemAssetsCollection.Prefab.Variables.Values.AddRange(root.variables);
        Duckov.Utilities.ItemAssetsCollection.Prefab.Variables.Values.AddRange(ammo.variables);
        Check(ModeHItemTreeNormalizer.TryRestore(legacy, out error) != null, "legacy known prefab types recover without digest migration");
    }
}
