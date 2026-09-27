using System;
using System.Collections.Generic;
using System.Linq;
using BossRush;
using Saves;

namespace UnityEngine
{
    static class Time { public static float realtimeSinceStartup; }
    static class Mathf { public static int Max(int a, int b) { return Math.Max(a, b); } }
}
namespace Duckov.Rules
{
    class Rules { public int SaveDeadbodyCount = 4; }
    static class GameRulesManager { public static Rules Current = new Rules(); }
}
namespace BossRush
{
    sealed class WraithInfo { public uint raidID; public bool valid = true; }
    partial class DeathWraithRuntimeModule
    {
        internal void Append(uint id) { AppendStoredDeathWraithInfo_DeathWraith(new WraithInfo { raidID = id }); }
        internal bool Remove(uint id) { return RemoveStoredDeathWraithInfoByRaidId_DeathWraith(id, "test"); }
        internal WraithInfo Find(uint id) { return FindStoredDeathWraithInfoByRaidId_DeathWraith(id); }
        internal bool Dirty { get { return _deathWraithListDirty; } }
        internal int Pending { get { return _deathWraithPendingAppends.Count + _deathWraithPendingRemovals.Count; } }
        internal readonly List<uint> Removed = new List<uint>();
        internal string Key { get { return DEATH_WRAITH_LIST_SAVE_KEY; } }
        private void ClearDeathWraithState_DeathWraith() { Removed.Clear(); }
        private void DestroyActiveWraithByRaidId_DeathWraith(uint id, string reason) { Removed.Add(id); }
        private static void DevLog(string message) { }
    }
}
namespace Saves
{
    static class SavesSystem
    {
        internal static int CurrentSlot, Writes, Reads;
        internal static bool IsSaving, FailRead, FailWrite, FailClassify, NullList;
        internal static Dictionary<int, List<WraithInfo>> Data = new Dictionary<int, List<WraithInfo>>();
        internal static bool KeyExisits(string key)
        { if (FailClassify) throw new Exception("classification"); return Data.ContainsKey(CurrentSlot); }
        internal static T Load<T>(string key)
        {
            Reads++; if (FailRead) throw new Exception("read");
            if (NullList) return default(T);
            return (T)(object)Clone(Data[CurrentSlot]);
        }
        internal static void Save<T>(string key, T value)
        {
            if (FailWrite) throw new Exception("write");
            Data[CurrentSlot] = Clone((List<WraithInfo>)(object)value); Writes++;
        }
        private static List<WraithInfo> Clone(List<WraithInfo> values)
        { return values.Select(v => new WraithInfo { raidID = v.raidID, valid = v.valid }).ToList(); }
        internal static void Reset()
        {
            CurrentSlot = Writes = Reads = 0; Data.Clear();
            IsSaving = FailRead = FailWrite = FailClassify = NullList = false;
            UnityEngine.Time.realtimeSinceStartup = 0;
        }
    }
}
class Program
{
    static int checks;
    static void Check(bool condition, string text) { if (!condition) throw new Exception("FAIL " + text); checks++; Console.WriteLine("PASS " + text); }
    static DeathWraithRuntimeModule Prepare()
    { SavesSystem.Reset(); SavesSystem.Data[0] = new List<WraithInfo> { new WraithInfo { raidID = 1 }, new WraithInfo { raidID = 2 } }; return new DeathWraithRuntimeModule(); }
    static bool Ids(params uint[] ids) { return SavesSystem.Data[SavesSystem.CurrentSlot].Select(v => v.raidID).SequenceEqual(ids); }
    static void Advance(DeathWraithRuntimeModule owner) { UnityEngine.Time.realtimeSinceStartup += 31f; owner.UpdateDeferredDeathWraithSave_DeathWraith(); }
    static void Main()
    {
        foreach (string failure in new[] { "read", "classification", "null" })
        {
            var owner = Prepare(); SavesSystem.FailRead = failure == "read"; SavesSystem.FailClassify = failure == "classification"; SavesSystem.NullList = failure == "null";
            owner.Append(3); owner.FlushDeathWraithListIfDirty_DeathWraith();
            Check(Ids(1, 2) && SavesSystem.Writes == 0 && owner.Dirty && owner.Pending == 1, failure + " cannot replace old records with a new empty-based list");
            int reads = SavesSystem.Reads;
            for (int i = 0; i < 100; i++) owner.FlushDeathWraithListIfDirty_DeathWraith();
            Check(SavesSystem.Reads == reads, failure + " retries are throttled instead of reading every frame");
            SavesSystem.FailRead = SavesSystem.FailClassify = SavesSystem.NullList = false; Advance(owner);
            Check(Ids(1, 2, 3) && !owner.Dirty && owner.Pending == 0 && SavesSystem.Writes == 1, failure + " retry merges old and new records");
        }
        var current = Prepare(); SavesSystem.FailRead = true; current.Append(3); current.Append(3); current.Append(4);
        current.Remove(1); current.Remove(3); SavesSystem.FailRead = false; Advance(current);
        Check(Ids(2, 4), "pending upsert and removal intents merge once without resurrecting removed records");
        current = Prepare(); SavesSystem.FailRead = true;
        for (uint i = 3; i <= 20; i++) current.Append(i);
        Check(current.Pending <= 4, "unreadable store retains bounded recent append intentions");
        SavesSystem.FailRead = false; Advance(current);
        Check(Ids(17, 18, 19, 20), "merged list keeps the official corpse-record limit and order");
        current = Prepare(); current.Append(3); SavesSystem.FailWrite = true; current.FlushDeathWraithListIfDirty_DeathWraith();
        Check(Ids(1, 2) && current.Dirty, "write failure leaves old typed data and dirty obligation intact");
        SavesSystem.FailWrite = false; Advance(current); Check(Ids(1, 2, 3) && !current.Dirty, "write retry persists original and appended records");
        current = Prepare(); current.Append(3); SavesSystem.IsSaving = true; current.FlushDeathWraithListIfDirty_DeathWraith();
        Check(SavesSystem.Writes == 0 && current.Dirty, "official busy gate preserves pending records");
        SavesSystem.IsSaving = false; current.FlushDeathWraithListIfDirty_DeathWraith(); Check(Ids(1, 2, 3), "official collection after busy state writes once");
        current = Prepare(); SavesSystem.FailRead = true; current.Append(3); current.Remove(1);
        SavesSystem.CurrentSlot = 1; SavesSystem.Data[1] = new List<WraithInfo> { new WraithInfo { raidID = 8 } };
        current.OnSetFile_DeathWraith(); SavesSystem.FailRead = false; Advance(current);
        Check(Ids(8) && current.Pending == 0 && !current.Dirty && SavesSystem.Writes == 0, "slot switch clears old append/removal obligations without writing new slot");
        current.Append(9); current.FlushDeathWraithListIfDirty_DeathWraith(); Check(Ids(8, 9), "new slot starts from its own records");
        current = Prepare(); SavesSystem.FailRead = true; current.Append(3);
        current.InvalidateStoredDeathWraithRecords_DeathWraith("explicit disable"); current.FlushDeathWraithListIfDirty_DeathWraith();
        Check(Ids() && current.Pending == 0, "explicit disable retains existing clear semantics and discards pending appends");
        current = Prepare(); SavesSystem.Data.Clear(); current.Append(1); current.FlushDeathWraithListIfDirty_DeathWraith();
        Check(Ids(1) && SavesSystem.Reads == 0, "genuinely missing key initializes without a read barrier");
        Console.WriteLine("DeathWraithPersistence: " + checks + " assertions passed");
    }
}
