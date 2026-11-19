// ============================================================================
// CollectionBookPersistence.cs - 收藏册存档（BossRush_CollectionBook_v1，SCHEMA+ 2026-11-19）
// ============================================================================
// 只存事实：已登记的收集品、已发过集齐奖励的系列、全集齐大奖、杰夫引导 590217 的接取 / 交付。
// 存槽、写屏障、采集与物理保存全部复用共享管线（形态照 EclipseBloomIntroductionPersistence）。
// 数据按值提交：调用方 Clone 一份改完再 Store，Store 成功才替换内存快照。
// ============================================================================

using System;
using System.Collections.Generic;

namespace BossRush
{
    internal sealed class CollectionBookData
    {
        internal readonly HashSet<int> Registered = new HashSet<int>();
        internal readonly HashSet<int> PaidSeries = new HashSet<int>();
        internal bool FullPaid;
        internal bool JeffAccepted;
        internal bool JeffDone;

        internal CollectionBookData Clone()
        {
            var copy = new CollectionBookData { FullPaid = FullPaid, JeffAccepted = JeffAccepted, JeffDone = JeffDone };
            copy.Registered.UnionWith(Registered);
            copy.PaidSeries.UnionWith(PaidSeries);
            return copy;
        }

        /// <summary>接过引导（或交过）之后售货机才卖收藏册。</summary>
        internal bool BookUnlocked { get { return JeffAccepted || JeffDone; } }

        internal int RegisteredInSeries(CollectibleSeries series)
        {
            int count = 0;
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def != null && def.Series == series && Registered.Contains(def.TypeId)) count++;
            }
            return count;
        }
    }

    /// <summary>一次登记的规划结果：写进存档的新事实、收走哪些、哪些系列刚凑齐、发多少奖金。</summary>
    internal sealed class CollectionRegistrationPlan
    {
        internal CollectionBookData Next;
        internal readonly List<int> AcceptedIndices = new List<int>();
        internal readonly List<CollectibleSeries> Completed = new List<CollectibleSeries>();
        internal long Cash;
        internal bool FullNow;
        internal bool HasWork { get { return AcceptedIndices.Count > 0; } }
    }

    internal static class CollectionRegistration
    {
        /// <summary>
        /// 纯规划，不碰物品与存档：只收没登记过的收集品（同一次放两件同样的只收一件），
        /// 新凑齐的系列各发一次奖金，全部系列都齐了再发一次大奖；珍品组不算集齐。
        /// </summary>
        internal static CollectionRegistrationPlan Plan(CollectionBookData current, IList<int> typeIds)
        {
            var plan = new CollectionRegistrationPlan { Next = current == null ? new CollectionBookData() : current.Clone() };
            if (typeIds == null) return plan;
            for (int i = 0; i < typeIds.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.Get(typeIds[i]);
                if (def != null && plan.Next.Registered.Add(def.TypeId)) plan.AcceptedIndices.Add(i);
            }
            if (!plan.HasWork) return plan;
            for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
            {
                CollectibleSeries series = (CollectibleSeries)s;
                CollectibleSeriesInfo info = CollectibleCatalog.GetSeries(series);
                if (info == null || !info.CountsForSet || plan.Next.PaidSeries.Contains(s)) continue;
                if (plan.Next.RegisteredInSeries(series) < SeriesSize(series)) continue;
                plan.Next.PaidSeries.Add(s);
                plan.Completed.Add(series);
                plan.Cash += CollectibleTuning.SeriesCash(series);
            }
            if (!plan.Next.FullPaid && AllSetsComplete(plan.Next))
            {
                plan.Next.FullPaid = true;
                plan.FullNow = true;
                plan.Cash += CollectibleTuning.AllSeriesCash;
            }
            return plan;
        }

        /// <summary>
        /// 登记事务的提交：先写存档，写成功才跑后续步骤（发奖金、提示、刷新任务）。一旦写成功就返回 true，
        /// 调用方必须收走 <see cref="CollectionRegistrationPlan.AcceptedIndices"/> 对应的物品；后续步骤抛错只记下来，
        /// 不能让已登记的物品被当成没登记退回去（CR-2026-11-19-002）。写失败或写的时候抛错返回 false，后续步骤一个都不跑。
        /// </summary>
        internal static bool Apply(CollectionRegistrationPlan plan, Func<CollectionBookData, bool> store, Action<string> warn, params Action[] afterCommit)
        {
            if (plan == null || !plan.HasWork || store == null) return false;
            bool stored;
            try { stored = store(plan.Next); }
            catch (Exception e)
            {
                if (warn != null) warn("store: " + e.Message);
                return false;
            }
            if (!stored) return false;
            if (afterCommit == null) return true;
            for (int i = 0; i < afterCommit.Length; i++)
            {
                if (afterCommit[i] == null) continue;
                try { afterCommit[i](); }
                catch (Exception e) { if (warn != null) warn(e.Message); }
            }
            return true;
        }

        internal static int SeriesSize(CollectibleSeries series)
        {
            int count = 0;
            for (int i = 0; i < CollectibleCatalog.Count; i++)
            {
                CollectibleDefinition def = CollectibleCatalog.At(i);
                if (def != null && def.Series == series) count++;
            }
            return count;
        }

        private static bool AllSetsComplete(CollectionBookData data)
        {
            for (int s = 0; s < CollectibleCatalog.SeriesCount; s++)
            {
                CollectibleSeriesInfo info = CollectibleCatalog.GetSeries((CollectibleSeries)s);
                if (info != null && info.CountsForSet && !data.PaidSeries.Contains(s)) return false;
            }
            return true;
        }
    }

    internal sealed class CollectionBookPersistence : IBossRushSaveBatchSource, IDisposable
    {
        internal const string StorageKey = "BossRush_CollectionBook_v1";
        private const int SchemaVersion = 1;
        private readonly BossRushSlotJsonStore<CollectionBookData> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly Action slotChanged;
        private readonly Func<bool> canFlush;

        internal CollectionBookPersistence(Action onSlotChanged, Func<bool> flushAllowed)
        {
            slotChanged = onSlotChanged;
            canFlush = flushAllowed;
            coordinator = new BossRushSaveCoordinatorEngine(this, true);
            store = new BossRushSlotJsonStore<CollectionBookData>(new BossRushSlotJsonStoreSpec<CollectionBookData>
            {
                StorageKey = StorageKey, SchemaVersion = SchemaVersion, LogPrefix = CollectibleTuning.LogPrefix, DisplayName = "收藏册",
                Encode = Encode, Decode = Decode, ReadSchemaVersion = ReadSchemaVersion,
                CreateDefault = () => new CollectionBookData(),
                NotifySlotChanged = OnSlotChanged,
                AfterPendingWriteFlushed = coordinator.NotifyPendingWriteFlushed
            });
            store.EnsureSubscribed();
        }

        internal bool HasWriteBarrier { get { return store.HasWriteBarrier; } }
        internal bool Writable { get { return !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal CollectionBookData Load() { return store.LoadOrInit(); }

        internal bool Store(CollectionBookData value, bool requestFlush = true)
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

        string IBossRushSaveBatchSource.LogPrefix { get { return CollectibleTuning.LogPrefix; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store != null && store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store != null && store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store == null ? null : store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error)
        {
            bool allowed = canFlush == null || canFlush();
            error = allowed ? null : "collection_book_pending";
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

        /// <summary>越界、重复或类型不对的整份拒收（写屏障保护原档，不拿默认值覆盖）。</summary>
        internal static CollectionBookData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != SchemaVersion) return null;
            var data = new CollectionBookData
            {
                FullPaid = root.GetBool("fullPaid", false),
                JeffAccepted = root.GetBool("jeffAccepted", false),
                JeffDone = root.GetBool("jeffDone", false),
            };
            List<BossRushJsonValue> items;
            if (root.TryGetArray("registered", out items))
            {
                foreach (BossRushJsonValue value in items)
                {
                    int typeId = value == null ? -1 : value.AsInt(-1);
                    if (!CollectibleCatalog.IsCollectible(typeId) || !data.Registered.Add(typeId)) return null;
                }
            }
            if (root.TryGetArray("paidSeries", out items))
            {
                foreach (BossRushJsonValue value in items)
                {
                    int series = value == null ? -1 : value.AsInt(-1);
                    if (series < 0 || series >= CollectibleCatalog.SeriesCount || !data.PaidSeries.Add(series)) return null;
                }
            }
            return data;
        }

        internal static string Encode(CollectionBookData value)
        {
            if (value == null) return null;
            var registered = new List<int>(value.Registered);
            registered.Sort();
            var paid = new List<int>(value.PaidSeries);
            paid.Sort();
            var writer = new BossRushJsonWriter().BeginObject().Int("schemaVersion", SchemaVersion);
            writer.BeginArray("registered");
            foreach (int typeId in registered) writer.ItemInt(typeId);
            writer.EndArray().BeginArray("paidSeries");
            foreach (int series in paid) writer.ItemInt(series);
            writer.EndArray();
            return writer.Bool("fullPaid", value.FullPaid).Bool("jeffAccepted", value.JeffAccepted)
                .Bool("jeffDone", value.JeffDone).EndObject().ToString();
        }
    }
}
