// 仅替代宿主 IO/物品数据边界；待测协调器、节流器、树序列化及恢复均直接编译生产源。
using System;
using System.Collections.Generic;
using System.Linq;
namespace UnityEngine { public static class Time { public static int frameCount; } }
namespace Saves
{
    public static class SavesSystem
    {
        public static bool IsSaving, FailNext;
        public static int PhysicalWrites;
        public static void SaveFile(bool flag)
        {
            if (FailNext) { FailNext = false; throw new Exception("injected IO failure"); }
            PhysicalWrites++;
        }
    }
}
namespace BossRush
{
    public class ModeHStakeJournalDto { }
    public class ModeHSeasonDto { }
    public class ModeHHallOfFameRecordDto { }
    public class ModeHProductionCertificationDto { }
    public class ModeHItemTreeSnapshotDto
    {
        public int sourcePosition, gameQuality, preCount, postCount;
        public string normalizedTreePayload, semanticTreeDigest;
    }
    public static class ModeHConfig { public const int MinGameQuality = 1, MaxGameQuality = 8; }
    public static class BossRushDynamicItemRegistry { public static void EnsureRegistered(int id) { } }
    public static class ModeHRuntimeGates
    {
        public static bool IsModeHCombatFrameActive, Blocked;
        public static void SetRecoveryOnlyBlocked(bool value, string reason) { Blocked = value; }
    }
    public static class ModeHWarehouseStakeJournal
    {
        public static int Collected;
        public static bool CollectAssetSnapshot(ModeHStakeJournalDto journal, out string error)
        { error = null; Collected++; return true; }
        public static bool RefreshAssetCache(out string error)
        { error = null; Collected++; return true; }
    }
    public static class ModeHProfilePersistence
    {
        public static bool HasPendingWrite, IsStoreFaulted;
        public static string LastError;
        public static void EnsureSubscribed() { } public static void ShutdownSubscription() { }
        public static void ResetStaticCaches() { HasPendingWrite = false; }
        public static bool StageWrite(ModeHSeasonDto dto, out string error) { error = null; HasPendingWrite = true; return true; }
        public static bool FlushPending() { HasPendingWrite = false; return true; }
    }
    public static class ModeHHallOfFamePersistence
    {
        public static bool HasPendingWrite, IsStoreFaulted;
        public static string LastError;
        public static void EnsureSubscribed() { } public static void ShutdownSubscription() { }
        public static void ResetStaticCaches() { HasPendingWrite = false; }
        public static bool StageRecordInsert(ModeHHallOfFameRecordDto dto, out string error) { error = null; HasPendingWrite = true; return true; }
        public static bool StageCertificationCache(ModeHProductionCertificationDto dto, int gen, out string error) { error = null; HasPendingWrite = true; return true; }
        public static bool StageInvalidateCertificationCache(out string error) { error = null; HasPendingWrite = true; return true; }
        public static bool FlushPending() { HasPendingWrite = false; return true; }
    }
    public static class ModeHStakeJournalPersistence
    {
        public static bool HasPendingWrite, IsStoreFaulted;
        public static string LastError;
        public static void EnsureSubscribed() { } public static void ShutdownSubscription() { }
        public static bool StageWrite(ModeHStakeJournalDto dto, out string error) { error = null; HasPendingWrite = true; return true; }
        public static bool FlushPending() { HasPendingWrite = false; return true; }
    }
}
namespace ItemStatsSystem
{
    public enum CustomDataType { Int, Float, String, Bool }
    public class CustomData
    {
        public string Key; public CustomDataType DataType; public bool Display; private byte[] raw;
        public CustomData(string key, CustomDataType type, byte[] bytes) { Key = key; DataType = type; raw = bytes; }
        public byte[] GetRawCopied() { return (byte[])raw.Clone(); }
    }
    public class CustomDataCollection
    {
        public List<CustomData> Values = new List<CustomData>();
        public CustomData GetEntry(string key) { return Values.Find(v => v.Key == key); }
    }
    public class Item
    {
        public int Quality = 4;
        public Data.ItemTreeData Tree;
        public CustomDataCollection Variables = new CustomDataCollection();
    }
}
namespace Duckov.Utilities
{
    public static class ItemAssetsCollection
    {
        public static ItemStatsSystem.Item Prefab = new ItemStatsSystem.Item();
        public struct Meta { public int quality; }
        public static Meta GetMetaData(int typeId) { return new Meta { quality = 4 }; }
        public static ItemStatsSystem.Item GetPrefab(int typeId) { return Prefab; }
    }
}
namespace ItemStatsSystem.Data
{
    public class ItemTreeData
    {
        public int rootInstanceID;
        public List<DataEntry> entries = new List<DataEntry>();
        public DataEntry RootData { get { return entries.Find(e => e.instanceID == rootInstanceID); } }
        public int RootTypeID { get { return RootData.typeID; } }
        public static ItemTreeData FromItem(Item item) { return item.Tree; }
        public struct SlotInstanceIDPair
        {
            public string slot; public int instanceID;
            public SlotInstanceIDPair(string s, int i) { slot = s; instanceID = i; }
        }
        public struct InventoryDataEntry
        {
            public int position, instanceID;
            public InventoryDataEntry(int p, int i) { position = p; instanceID = i; }
        }
        public class DataEntry
        {
            public int instanceID, typeID;
            public List<CustomData> variables = new List<CustomData>();
            public List<SlotInstanceIDPair> slotContents = new List<SlotInstanceIDPair>();
            public List<InventoryDataEntry> inventory = new List<InventoryDataEntry>();
            public List<int> inventorySortLocks = new List<int>();
            public int StackCount { get { var c = variables.Find(v => v.Key == "Count"); return c == null ? 1 : BitConverter.ToInt32(c.GetRawCopied(), 0); } }
        }
    }
}
