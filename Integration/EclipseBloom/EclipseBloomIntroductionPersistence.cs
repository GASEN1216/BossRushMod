using System;

namespace BossRush
{
    internal sealed class EclipseBloomIntroductionData
    {
        internal bool Accepted;
        internal bool Defeated;
        internal bool Completed;
    }

    /// <summary>只保存花冠引导事实；存槽、写屏障、采集与物理保存均复用共享管线。</summary>
    internal sealed class EclipseBloomIntroductionPersistence : IBossRushSaveBatchSource, IDisposable
    {
        internal const string StorageKey = "BossRush_EclipseBloom_Introduction_v1";
        private readonly BossRushSlotJsonStore<EclipseBloomIntroductionData> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly Action slotChanged;
        private readonly Func<bool> canFlush;

        internal EclipseBloomIntroductionPersistence(Action onSlotChanged, Func<bool> beforeCollect, Func<bool> flushAllowed)
        {
            slotChanged = onSlotChanged;
            canFlush = flushAllowed;
            coordinator = new BossRushSaveCoordinatorEngine(this, true);
            store = new BossRushSlotJsonStore<EclipseBloomIntroductionData>(new BossRushSlotJsonStoreSpec<EclipseBloomIntroductionData>
            {
                StorageKey = StorageKey, SchemaVersion = 1, LogPrefix = "[EclipseBloomIntroduction] ", DisplayName = "蚀日花冠引导",
                Encode = Encode, Decode = Decode, ReadSchemaVersion = ReadSchemaVersion,
                CreateDefault = () => new EclipseBloomIntroductionData(),
                NotifySlotChanged = OnSlotChanged,
                BeforeCollectSaveData = beforeCollect,
                AfterPendingWriteFlushed = coordinator.NotifyPendingWriteFlushed
            });
            store.EnsureSubscribed();
        }

        internal bool HasWriteBarrier { get { return store.HasWriteBarrier; } }
        internal bool Writable { get { return !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal EclipseBloomIntroductionData Load() { return store.LoadOrInit(); }
        internal bool Store(EclipseBloomIntroductionData value, bool requestFlush = true)
        {
            if (!store.Store(value)) return false;
            if (requestFlush) RequestFlush();
            return true;
        }
        internal void RequestFlush()
        {
            string error;
            coordinator.RequestFlush(out error);
        }
        internal void Tick() { coordinator.Tick(); }
        private void OnSlotChanged()
        {
            coordinator.NotifySlotChanged();
            if (slotChanged != null) slotChanged();
        }
        public void Dispose()
        {
            coordinator.TryFlushOnHostDestroy();
            store.ShutdownSubscription();
            store.ResetAll();
            coordinator.Reset();
        }

        string IBossRushSaveBatchSource.LogPrefix { get { return "[EclipseBloomIntroduction] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store != null && store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store != null && store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store == null ? null : store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error)
        {
            bool allowed = canFlush == null || canFlush();
            error = allowed ? null : "eclipse_introduction_facts_pending";
            return allowed;
        }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }

        internal static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            return root != null && root.TryGetInt("schemaVersion", out version) ? version : -1;
        }
        internal static EclipseBloomIntroductionData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema;
            bool accepted, defeated, completed;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != 1
                || !root.TryGetBool("accepted", out accepted) || !root.TryGetBool("defeated", out defeated)
                || !root.TryGetBool("completed", out completed)
                || (defeated && !accepted) || (completed && (accepted || defeated))) return null;
            return new EclipseBloomIntroductionData { Accepted = accepted, Defeated = defeated, Completed = completed };
        }
        internal static string Encode(EclipseBloomIntroductionData value)
        {
            if (value == null) return null;
            return new BossRushJsonWriter().BeginObject().Int("schemaVersion", 1)
                .Bool("accepted", value.Accepted).Bool("defeated", value.Defeated)
                .Bool("completed", value.Completed).EndObject().ToString();
        }
    }
}
