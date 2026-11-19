using System;
using System.Collections.Generic;

// 只替身存档管线与本地化注入：生产的物品表、抽签、账本、送礼偏好、存档编解码与本地化表都直接编译。
namespace BossRush
{
    internal static class L10n
    {
        internal static bool IsChinese = true;
        internal static string T(string cn, string en) { return IsChinese ? cn : en; }
    }

    public class ModBehaviour
    {
        internal static void DevLog(string message) { }
    }

    internal static class LocalizationHelper
    {
        internal static readonly Dictionary<string, string> Injected = new Dictionary<string, string>();
        internal static void InjectLocalizations(Dictionary<string, string> values)
        {
            foreach (KeyValuePair<string, string> pair in values) Injected[pair.Key] = pair.Value;
        }
    }

    internal interface IBossRushSaveBatchSource
    {
        string LogPrefix { get; }
        bool HasPendingWrite { get; }
        bool IsStoreFaulted { get; }
        bool HasSnapshotObligation { get; }
        string LastError { get; }
        bool CollectSnapshot(out string error);
        bool FlushPending();
        void OnPhysicalSaveSucceeded();
    }

    internal sealed class BossRushSaveCoordinatorEngine
    {
        internal BossRushSaveCoordinatorEngine(IBossRushSaveBatchSource source, bool flag) { }
        internal void NotifyPendingWriteFlushed() { }
        internal bool RequestFlush(out string error) { error = null; return true; }
        internal void Tick() { }
        internal void NotifySlotChanged() { }
        internal void TryFlushOnHostDestroy() { }
        internal void Reset() { }
    }

    internal sealed class BossRushSlotJsonStoreSpec<T> where T : class
    {
        public string StorageKey;
        public int SchemaVersion;
        public string LogPrefix;
        public string DisplayName;
        public Func<T, string> Encode;
        public Func<string, int> ReadSchemaVersion;
        public Func<string, T> Decode;
        public Func<T> CreateDefault;
        public Action NotifySlotChanged;
        public Action AfterPendingWriteFlushed;
    }

    internal sealed class BossRushSlotJsonStore<T> where T : class
    {
        private readonly BossRushSlotJsonStoreSpec<T> spec;
        private T cache;
        internal string LastJson;
        internal BossRushSlotJsonStore(BossRushSlotJsonStoreSpec<T> value) { spec = value; }
        internal void EnsureSubscribed() { }
        internal bool HasWriteBarrier { get { return false; } }
        internal bool IsStoreFaulted { get { return false; } }
        internal bool HasPendingWrite { get { return false; } }
        internal string LastError { get { return null; } }
        internal T LoadOrInit() { if (cache == null) cache = spec.CreateDefault(); return cache; }
        internal bool Store(T value) { LastJson = spec.Encode(value); cache = value; return LastJson != null; }
        internal bool FlushPending() { return true; }
        internal void ShutdownSubscription() { }
        internal void ResetAll() { cache = null; }
    }
}
