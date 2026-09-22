using System;
using System.Collections.Generic;
using System.Text;
using Duckov.Economy;
using Saves;

namespace BossRush
{
    /// <summary>
    /// 全局领奖意向先落盘；本槽现金与收据同批落盘后，才允许提交旧的全局已领标记。
    /// 中断后原槽凭收据续交，其他槽不能抢领这笔尚未结清的奖金。
    /// </summary>
    internal sealed class AchievementRewardJournal : IBossRushSaveBatchSource
    {
        internal const string ReceiptKey = "BossRush_AchievementCashReceipts_v1";
        internal const string IntentKey = "BossRush_AchievementCashIntent_v1";
        private readonly BossRushSlotJsonStore<List<string>> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private bool cashSnapshotRequired, staging;
        private int slot = -1;

        internal AchievementRewardJournal()
        {
            store = new BossRushSlotJsonStore<List<string>>(new BossRushSlotJsonStoreSpec<List<string>>
            {
                StorageKey = ReceiptKey, SchemaVersion = 1, LogPrefix = "[Achievement] ", DisplayName = "成就领奖凭据",
                CreateDefault = () => new List<string>(), Encode = Encode, Decode = Decode,
                ReadSchemaVersion = ReadSchema, NotifySlotChanged = OnSlotChanged,
                BeforeCollectSaveData = CollectCash
            });
            coordinator = new BossRushSaveCoordinatorEngine(this, false);
            store.EnsureSubscribed();
        }

        internal bool TryPay(string id, long amount, Func<string, bool> isClaimed)
        {
            if (staging || SavesSystem.IsSaving || SavesSystem.CurrentSlot < 0 || EconomyManager.Instance == null) return false;
            store.LoadOrInit();
            if (store.HasWriteBarrier || store.IsStoreFaulted) return false;
            slot = SavesSystem.CurrentSlot;
            string intent = SavesSystem.LoadGlobal<string>(IntentKey, string.Empty);
            string expected = "1|" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + id;
            if (!string.IsNullOrEmpty(intent) && intent != expected)
            {
                string[] parts = intent.Split('|');
                if (parts.Length != 3 || parts[0] != "1" || !isClaimed(parts[2])) return false;
                SavesSystem.SaveGlobal(IntentKey, string.Empty);
                intent = string.Empty;
            }
            // LoadGlobal 读取 ES3 缓存：上次 SaveGlobal 可能先改缓存、物理落盘再抛异常。
            // 即便缓存已有 expected 也必须补一次成功落盘，才能放行本槽现金与收据。
            SavesSystem.SaveGlobal(IntentKey, expected);
            if (SavesSystem.LoadGlobal<string>(IntentKey, string.Empty) != expected) return false;

            if (!store.Current.Contains(id))
            {
                long before = EconomyManager.Money;
                if (amount < 0 || before > long.MaxValue - amount) return false;
                var previous = new List<string>(store.Current);
                var candidate = new List<string>(previous) { id };
                staging = true;
                try
                {
                    // Store 只排队；在钱真实变动前，BeforeCollectSaveData 拒绝发布收据。
                    if (!store.Store(candidate)) return false;
                    try { if (amount > 0) EconomyManager.Add(amount); }
                    catch (Exception e) { ModBehaviour.DevLog("[Achievement] 奖金通知异常: " + e.Message); }
                    if (EconomyManager.Money != before + amount)
                    {
                        store.Store(previous);
                        return false;
                    }
                    cashSnapshotRequired = true;
                }
                finally { staging = false; }
            }
            string error;
            return coordinator.RequestFlush(out error, true);
        }

        internal void Complete()
        {
            SavesSystem.SaveGlobal(IntentKey, string.Empty);
        }

        internal void Shutdown()
        {
            coordinator.TryFlushOnHostDestroy();
            store.ShutdownSubscription();
        }

        private void OnSlotChanged()
        {
            slot = -1;
            cashSnapshotRequired = false;
            if (coordinator != null) coordinator.NotifySlotChanged();
        }

        private bool CollectCash()
        {
            if (staging) return false;
            if (!cashSnapshotRequired) return true;
            if (SavesSystem.IsSaving || SavesSystem.CurrentSlot != slot || EconomyManager.Instance == null) return false;
            SavesSystem.Save<EconomyManager.SaveData>("EconomyData", (EconomyManager.SaveData)EconomyManager.Instance.GenerateSaveData());
            return true;
        }

        public string LogPrefix { get { return "[Achievement] "; } }
        public bool HasPendingWrite { get { return store.HasPendingWrite; } }
        public bool IsStoreFaulted { get { return store.IsStoreFaulted; } }
        public bool HasSnapshotObligation { get { return cashSnapshotRequired; } }
        public string LastError { get { return store.LastError; } }
        public bool CollectSnapshot(out string error) { bool ok = CollectCash(); error = ok ? null : "cash_snapshot_unavailable"; return ok; }
        public bool FlushPending() { return store.FlushPending(); }
        public void OnPhysicalSaveSucceeded() { cashSnapshotRequired = false; }

        private static int ReadSchema(string json)
        {
            BossRushJsonValue root; string error; int schema;
            return BossRushJsonParser.TryParse(json, out root, out error) && root.TryGetInt("schemaVersion", out schema) ? schema : -1;
        }

        private static List<string> Decode(string json)
        {
            BossRushJsonValue root; string error; List<BossRushJsonValue> values;
            if (!BossRushJsonParser.TryParse(json, out root, out error) || ReadSchema(json) != 1 || !root.TryGetArray("receipts", out values)) return null;
            var result = new List<string>();
            foreach (BossRushJsonValue value in values)
            {
                if (value == null || value.Kind != BossRushJsonKind.String || string.IsNullOrEmpty(value.StringValue) || result.Contains(value.StringValue)) return null;
                result.Add(value.StringValue);
            }
            return result;
        }

        private static string Encode(List<string> receipts)
        {
            var sb = new StringBuilder("{\"schemaVersion\":1,\"receipts\":[");
            for (int i = 0; i < receipts.Count; i++)
            {
                if (i != 0) sb.Append(',');
                sb.Append('"'); SimpleJsonHelper.EscapeString(sb, receipts[i]); sb.Append('"');
            }
            return sb.Append("]}").ToString();
        }
    }
}
