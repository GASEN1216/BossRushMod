using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Duckov.Utilities;
using ItemStatsSystem;
using ItemStatsSystem.Data;

namespace BossRush
{
    /// <summary>
    /// SCHEMA+：只保存一次劫包的原物与提交结果。不会采集或强制保存整份角色背包。
    /// 原生正常保存附带排除收据；旧盘重载时只扣留同一原物的一份，不能按 TypeID 猜物。
    /// </summary>
    internal sealed class CoopWaylayerJournal : IDisposable, IBossRushSaveBatchSource
    {
        internal const string SourceKey = "BossRush_WaylayerSource";
        internal const string SnapshotKey = "BossRush_WaylayerSnapshot";
        internal const string RefundKey = "BossRush_WaylayerRefund";
        internal const string CustodyKey = "BossRush_WaylayerCustody";
        internal const string ExcludedKey = "BossRush_WaylayerExcluded";
        internal const string ReturnedKey = "BossRush_WaylayerReturned";
        internal const string CharacterOwnerKey = "BossRush_WaylayerOwner";
        internal const int Prepared = 0, Reserved = 1, Success = 2, Failure = 3;
        internal const int Custody = 4;

        [Serializable] internal sealed class Debt
        {
            public string Id, Host, Owner, Proof, Source;
            public ItemTreeData Tree;
            public int State;
        }
        [Serializable] internal sealed class Commit
        {
            public string Id, Host, Owner, Proof;
            public ItemTreeData Tree;
            public int State;
            public bool PublicationStarted;
        }
        [Serializable] internal sealed class Document
        {
            public int schemaVersion = 1;
            public string Identity, Snapshot;
            public List<Debt> Debts = new List<Debt>();
            public List<Commit> Commits = new List<Commit>();
            public List<string> Excluded = new List<string>();
            public List<string> Returned = new List<string>();
        }
        private readonly BossRushSlotJsonStore<Document> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly HashSet<string> debited = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> returned = new HashSet<string>(StringComparer.Ordinal);
        private CharacterMainControl boundCharacter;
        private string durableJson;
        private int durableSlot = int.MinValue;
        private bool disposed;
        internal static CoopWaylayerJournal Current { get; private set; }

        internal CoopWaylayerJournal()
        {
            store = new BossRushSlotJsonStore<Document>(new BossRushSlotJsonStoreSpec<Document>
            {
                StorageKey = "BossRush_CoopWaylayerJournal", SchemaVersion = 1,
                LogPrefix = "[SkyIslandWaylayer] ", DisplayName = "劫包核账",
                Encode = BossRushCoopJson.ToJson, Decode = Decode,
                ReadSchemaVersion = ReadVersion, CreateDefault = Create,
                NotifySlotChanged = OnSlotChanged, AfterPendingWriteFlushed = OnFlushed
            });
            coordinator = new BossRushSaveCoordinatorEngine(this, false);
            Current = this;
            store.EnsureSubscribed();
        }
        private static Document Create() { return new Document { Identity = Guid.NewGuid().ToString("N") }; }
        private static int ReadVersion(string raw)
        {
            try { return BossRushCoopJson.FromJson<Document>(raw).schemaVersion; }
            catch (Exception) { return -1; }
        }
        private static Document Decode(string raw)
        {
            try
            {
                Document value = BossRushCoopJson.FromJson<Document>(raw);
                if (value == null || !ValidId(value.Identity) || value.Debts == null || value.Commits == null
                    || value.Excluded == null || value.Returned == null) return null;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (Debt debt in value.Debts)
                    if (debt == null || !ValidId(debt.Id) || !ValidId(debt.Host) || debt.Owner != value.Identity
                        || !ValidId(debt.Proof) || debt.State < Prepared || debt.State > Failure
                        || debt.Tree == null || debt.Tree.RootData == null || !ids.Add(debt.Id)) return null;
                ids.Clear();
                foreach (Commit commit in value.Commits)
                    if (commit == null || !ValidId(commit.Id) || commit.Host != value.Identity || !ValidId(commit.Owner)
                        || !ValidId(commit.Proof) || commit.State < Reserved || commit.State > Custody
                        || !ids.Add(commit.Id)) return null;
                return value;
            }
            catch (Exception) { return null; }
        }
        internal static bool ValidId(string value) { Guid parsed; return Guid.TryParseExact(value, "N", out parsed); }
        internal Document Data { get { return store.LoadOrInit(); } }
        internal string Identity { get { return Data.Identity; } }
        internal bool CanWrite { get { Document value = Data; return !disposed && !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal bool Persist()
        {
            if (!CanWrite) return false;
            string expected = BossRushCoopJson.ToJson(Data);
            int slot = Saves.SavesSystem.CurrentSlot;
            if (durableSlot == slot && durableJson == expected) return true;
            if (!store.Store(Data)) return false;
            string error;
            // 这是单件实物的提交屏障，不调用 LevelManager.SaveMainCharacter / CollectSaveData。
            bool saved = coordinator.RequestFlush(out error, true);
            try
            {
                if (Saves.SavesSystem.CurrentSlot != slot) return false;
                string path = Saves.SavesSystem.CurrentFilePath;
                var settings = new ES3Settings(path, (ES3Settings)null) { location = ES3.Location.File };
                // 官方 SaveFile 可能写完后抛错；只读该 journal 的实际文件，不能拿 ES3 缓存作 durable 证明。
                bool confirmed = ES3.KeyExists("BossRush_CoopWaylayerJournal", path, settings)
                    && ES3.Load<string>("BossRush_CoopWaylayerJournal", path, settings) == expected;
                if (confirmed) { durableJson = expected; durableSlot = slot; }
                return confirmed;
            }
            catch (Exception)
            {
                // 官方 SaveFile 正常返回已确认本批落盘；只读校验另行失败不能把真实提交反转成退款。
                if (saved && Saves.SavesSystem.CurrentSlot == slot)
                { durableJson = expected; durableSlot = slot; return true; }
                return false;
            }
        }
        internal Debt FindDebt(string id) { return Data.Debts.Find(value => value.Id == id); }
        internal Commit FindCommit(string id) { return Data.Commits.Find(value => value.Id == id); }
        internal Debt Prepare(string id, string host, Item original, ItemTreeData one)
        {
            Debt existing = FindDebt(id);
            if (existing != null) return existing;
            if (!CanWrite || !ValidId(id) || !ValidId(host) || original == null || one == null) return null;
            string source = original.GetString(SourceKey);
            if (string.IsNullOrEmpty(source))
            {
                source = Identity + ":live:" + Guid.NewGuid().ToString("N");
                original.SetString(SourceKey, source);
            }
            var debt = new Debt { Id = id, Host = host, Owner = Identity, Proof = Guid.NewGuid().ToString("N"),
                Source = source, Tree = Clone(one), State = Prepared };
            Data.Debts.Add(debt);
            if (Persist()) return debt;
            Data.Debts.Remove(debt);
            return null;
        }
        internal bool Reserve(Debt debt)
        {
            debt.State = Reserved;
            debited.Add(debt.Id);
            StampCharacter();
            return Persist();
        }
        internal bool Decide(Debt debt, bool success)
        {
            int state = success ? Success : Failure;
            if ((debt.State == Success || debt.State == Failure) && debt.State != state) return false;
            debt.State = state;
            return Persist();
        }
        internal bool IsDebited(string id) { return debited.Contains(id); }
        internal bool IsReturned(string id) { return returned.Contains(id); }
        internal void NoteReturned(string id) { returned.Add(id); StampCharacter(); }
        internal bool OwnsCharacter(Item character)
        {
            if (character == null) return false;
            string owner = character.GetString(CharacterOwnerKey);
            CharacterMainControl current = CharacterMainControl.Main;
            bool respawn = !ReferenceEquals(boundCharacter, null) && !ReferenceEquals(boundCharacter, current)
                && (boundCharacter == null || boundCharacter.Health == null || boundCharacter.Health.IsDead);
            if (string.IsNullOrEmpty(owner) && (Data.Debts.Count == 0 || respawn))
            { character.SetString(CharacterOwnerKey, Identity); owner = Identity; }
            bool valid = owner == Identity;
            if (valid && current != null && ReferenceEquals(current.CharacterItem, character)) boundCharacter = current;
            return valid;
        }
        internal Commit PrepareCommit(string id, string owner, string proof, ItemTreeData tree)
        {
            Commit old = FindCommit(id);
            if (old != null) return old.Owner == owner && old.Proof == proof ? old : null;
            if (!CanWrite || !ValidId(id) || !ValidId(owner) || !ValidId(proof)) return null;
            var value = new Commit { Id = id, Host = Identity, Owner = owner, Proof = proof,
                Tree = tree == null ? null : Clone(tree), State = Reserved };
            Data.Commits.Add(value);
            if (Persist()) return value;
            Data.Commits.Remove(value);
            return null;
        }
        internal bool FinishCommit(Commit value, bool success)
        {
            if (success)
            {
                if (value.State == Failure) return false;
                if (value.State == Reserved) value.State = Custody;
            }
            else
            {
                if (value.State == Success || value.PublicationStarted) return false;
                value.State = Failure;
            }
            // Custody 只代表原树在账本，绝不冒充已公开成功。未公开原件可以持久拒绝后退回本人。
            return Persist();
        }
        internal bool StartPublication(Commit value)
        {
            if (value.State != Custody) return false;
            value.PublicationStarted = true;
            return Persist();
        }
        internal bool CancelUnpublished(Commit value, Item retracted)
        {
            // 只由本机同帧已撤销的实物证明授权；不能对已公开 Success 或未知历史调用。
            if (value.State == Success || (retracted != null && !retracted.IsBeingDestroyed)) return false;
            value.PublicationStarted = false;
            value.State = Failure;
            return Persist();
        }
        internal bool MarkPublished(Commit value)
        {
            if (!value.PublicationStarted || (value.State != Custody && value.State != Success)) return false;
            value.State = Success;
            return Persist();
        }

        internal static ItemTreeData Clone(ItemTreeData value)
        { return BossRushCoopJson.FromJson<ItemTreeData>(BossRushCoopJson.ToJson(value)); }
        private static string Hash(ItemTreeData value)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(BossRushCoopJson.ToJson(value)))).Replace("-", "");
        }
        private static string Get(ItemTreeData.DataEntry entry, string key)
        {
            CustomData value = entry.variables.Find(item => item.Key == key);
            return value == null ? null : value.GetString();
        }
        private static void Set(ItemTreeData.DataEntry entry, string key, string value)
        {
            entry.variables.RemoveAll(item => item.Key == key);
            entry.variables.Add(new CustomData(key, value));
        }
        private static void RemoveOne(ItemTreeData tree, ItemTreeData.DataEntry entry)
        {
            if (entry.StackCount > 1)
            {
                int count = entry.StackCount - 1;
                entry.variables.RemoveAll(item => item.Key == "Count");
                entry.variables.Add(new CustomData("Count", count));
                return;
            }
            var removed = new HashSet<int>();
            var pending = new Stack<int>(); pending.Push(entry.instanceID);
            while (pending.Count > 0)
            {
                int id = pending.Pop();
                if (!removed.Add(id)) continue;
                ItemTreeData.DataEntry child = tree.GetEntry(id);
                if (child == null) continue;
                foreach (ItemTreeData.InventoryDataEntry item in child.inventory) pending.Push(item.instanceID);
                foreach (ItemTreeData.SlotInstanceIDPair item in child.slotContents) pending.Push(item.instanceID);
            }
            tree.entries.RemoveAll(item => removed.Contains(item.instanceID));
            foreach (ItemTreeData.DataEntry parent in tree.entries)
            {
                parent.inventory.RemoveAll(item => removed.Contains(item.instanceID));
                parent.slotContents.RemoveAll(item => removed.Contains(item.instanceID));
            }
        }
        /// <summary>仅原生 MainCharacterItemData 加载调用域调用；克隆后操作，不改 ES3 缓存的树。</summary>
        internal ItemTreeData PrepareLoad(ItemTreeData saved)
        {
            if (saved == null || saved.RootData == null || !CanWrite) return saved;
            Document value = Data;
            ItemTreeData tree = Clone(saved);
            string fingerprint = Hash(saved);
            foreach (ItemTreeData.DataEntry entry in tree.entries)
                if (SkyIslandBossRules.StealScore(entry.typeID) >= 0 && string.IsNullOrEmpty(Get(entry, SourceKey)))
                    Set(entry, SourceKey, "tree:" + fingerprint + ":" + entry.instanceID);
            // 原生首载可能只有临时 owner。空槽先保存自有 GUID，不能给角色贴上随临时 owner 消失的身份。
            // 写盘失败仍保留独立于 GUID 的原物来源；非空旧账的既有排除继续执行。
            if (value.Debts.Count == 0 && value.Commits.Count == 0 && !Persist()) return tree;
            Set(tree.RootData, CharacterOwnerKey, value.Identity);
            bool knownSnapshot = Get(tree.RootData, SnapshotKey) == value.Snapshot && !string.IsNullOrEmpty(value.Snapshot);
            debited.Clear(); returned.Clear();
            foreach (Debt debt in value.Debts)
            {
                if (debt.State == Prepared) continue; // 未出站；原盘沿原生回滚，不另造原件。
                bool excluded = Contains(Get(tree.RootData, ExcludedKey), debt.Id) || (knownSnapshot && value.Excluded.Contains(debt.Id));
                bool refunded = Contains(Get(tree.RootData, ReturnedKey), debt.Id) || (knownSnapshot && value.Returned.Contains(debt.Id));
                ItemTreeData.DataEntry source = tree.entries.Find(entry => Get(entry, SourceKey) == debt.Source);
                if (debt.State == Failure)
                {
                    // 旧盘里原物尚在，或者正常保存已包含返还物，不能再增一份。
                    if (refunded || (!excluded && source != null)) returned.Add(debt.Id);
                    else debited.Add(debt.Id);
                    continue;
                }
                if (!excluded && source != null) RemoveOne(tree, source);
                debited.Add(debt.Id);
            }
            Set(tree.RootData, ExcludedKey, Join(debited, returned));
            Set(tree.RootData, ReturnedKey, Join(returned, null));
            return tree;
        }

        private static bool Contains(string values, string id)
        { return !string.IsNullOrEmpty(values) && values.IndexOf("|" + id + "|", StringComparison.Ordinal) >= 0; }
        private static string Join(HashSet<string> values, HashSet<string> except)
        {
            var result = new StringBuilder("|");
            foreach (string value in values) if (except == null || !except.Contains(value)) result.Append(value).Append('|');
            return result.ToString();
        }
        private void StampCharacter()
        {
            CharacterMainControl player = CharacterMainControl.Main;
            if (player != null && player.CharacterItem != null) BeforeNativeSave(player.CharacterItem);
        }

        /// <summary>只跟随原生已决定进行的正常角色保存；不主动保存角色。</summary>
        internal void BeforeNativeSave(Item character)
        {
            if (character == null || disposed || !OwnsCharacter(character)) return;
            // 收据随同实际角色树保存，不依赖另一笔 journal 写入成功；合堆、移动原物也不丢排除事实。
            // 只有原件实际交入本人背包后才会 NoteReturned，外部掉落不算返还。
            character.SetString(ExcludedKey, Join(debited, returned));
            character.SetString(ReturnedKey, Join(returned, null));
        }
        private void OnSlotChanged()
        {
            debited.Clear(); returned.Clear();
            boundCharacter = null; durableJson = null; durableSlot = int.MinValue;
            if (coordinator != null) coordinator.NotifySlotChanged();
        }
        private void OnFlushed() { if (coordinator != null) coordinator.NotifyPendingWriteFlushed(); }
        internal void Tick() { if (!disposed) coordinator.Tick(); }
        public void Dispose()
        {
            if (disposed) return;
            coordinator.TryFlushOnHostDestroy();
            store.ShutdownSubscription(); coordinator.Reset();
            debited.Clear(); returned.Clear(); disposed = true;
            boundCharacter = null; durableJson = null;
            if (ReferenceEquals(Current, this)) Current = null;
        }
        string IBossRushSaveBatchSource.LogPrefix { get { return "[SkyIslandWaylayer] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error) { error = null; return true; }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }
    }
}
