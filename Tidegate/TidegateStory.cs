using System;
using System.Collections.Generic;
using ItemStatsSystem;
using UnityEngine;

namespace BossRush
{
    /// <summary>潮闸镇剧情的持久事实（本槽）。按出击刷新的东西（阀门、大闸、灯塔）不进这里。</summary>
    internal sealed class TidegateStoryData
    {
        /// <summary>阿稳已经把湿包裹（闸伯的信）送到仓库。只送一次。</summary>
        internal bool LetterDelivered;
        /// <summary>读过信：地堡下水道的竹筏航线开放，闸伯在竹筏边等人。</summary>
        internal bool LetterRead;
        /// <summary>找到的闸工日志，按位记（第 1–4 页 = 位 0–3）。</summary>
        internal int Pages;
        /// <summary>潮汐怀表已经交到闸伯手上（剧情收尾）。</summary>
        internal bool WatchDelivered;
    }

    /// <summary>只保存潮闸镇剧情事实；存槽、写屏障、采集与物理保存复用共享管线（形态照 EclipseBloomIntroductionPersistence）。</summary>
    internal sealed class TidegateStoryPersistence : IBossRushSaveBatchSource, IDisposable
    {
        internal const string StorageKey = "BossRush_Tidegate_Story_v1";
        private const int Schema = 1;
        private readonly BossRushSlotJsonStore<TidegateStoryData> store;
        private readonly BossRushSaveCoordinatorEngine coordinator;
        private readonly Action slotChanged;

        internal TidegateStoryPersistence(Action onSlotChanged)
        {
            slotChanged = onSlotChanged;
            coordinator = new BossRushSaveCoordinatorEngine(this, true);
            store = new BossRushSlotJsonStore<TidegateStoryData>(new BossRushSlotJsonStoreSpec<TidegateStoryData>
            {
                StorageKey = StorageKey, SchemaVersion = Schema, LogPrefix = "[TidegateStory] ", DisplayName = "潮闸镇剧情",
                Encode = Encode, Decode = Decode, ReadSchemaVersion = ReadSchemaVersion,
                CreateDefault = () => new TidegateStoryData(),
                NotifySlotChanged = OnSlotChanged,
                AfterPendingWriteFlushed = coordinator.NotifyPendingWriteFlushed
            });
            store.EnsureSubscribed();
        }

        internal bool Writable { get { return !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal TidegateStoryData Load() { return store.LoadOrInit(); }

        internal bool Store(TidegateStoryData value)
        {
            if (!store.Store(value)) return false;
            string error;
            coordinator.RequestFlush(out error);
            return true;
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

        string IBossRushSaveBatchSource.LogPrefix { get { return "[TidegateStory] "; } }
        bool IBossRushSaveBatchSource.HasPendingWrite { get { return store != null && store.HasPendingWrite; } }
        bool IBossRushSaveBatchSource.IsStoreFaulted { get { return store != null && store.IsStoreFaulted; } }
        bool IBossRushSaveBatchSource.HasSnapshotObligation { get { return false; } }
        string IBossRushSaveBatchSource.LastError { get { return store == null ? null : store.LastError; } }
        bool IBossRushSaveBatchSource.CollectSnapshot(out string error) { error = null; return true; }
        bool IBossRushSaveBatchSource.FlushPending() { return store.FlushPending(); }
        void IBossRushSaveBatchSource.OnPhysicalSaveSucceeded() { }

        internal static int ReadSchemaVersion(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int version;
            return root != null && root.TryGetInt("schemaVersion", out version) ? version : -1;
        }

        internal static TidegateStoryData Decode(string raw)
        {
            BossRushJsonValue root = BossRushJsonParser.ParseOrNull(raw);
            int schema, pages;
            bool delivered, read, watch;
            if (root == null || !root.TryGetInt("schemaVersion", out schema) || schema != Schema
                || !root.TryGetBool("letterDelivered", out delivered) || !root.TryGetBool("letterRead", out read)
                || !root.TryGetInt("pages", out pages) || !root.TryGetBool("watchDelivered", out watch)
                || pages < 0 || pages > 15 || (read && !delivered)) return null;
            return new TidegateStoryData { LetterDelivered = delivered, LetterRead = read, Pages = pages, WatchDelivered = watch };
        }

        internal static string Encode(TidegateStoryData value)
        {
            if (value == null) return null;
            return new BossRushJsonWriter().BeginObject().Int("schemaVersion", Schema)
                .Bool("letterDelivered", value.LetterDelivered).Bool("letterRead", value.LetterRead)
                .Int("pages", value.Pages).Bool("watchDelivered", value.WatchDelivered).EndObject().ToString();
        }
    }

    /// <summary>
    /// 潮闸镇剧情线（owner 2026-11-05 拍板的钩子）：阿稳把一个泡湿的包裹送进仓库 → 读闸伯的信，地堡下水道东头出现竹筏 →
    /// 在潮闸镇翻出四页闸工日志、开闸、趁低潮进沉没的储蓄所拿回潮汐怀表 → 在竹筏边交给闸伯。
    /// 我们的存档是唯一权威，官方笔记图鉴（`TidegateNoteBridge`）只做镜像。不新增官方任务。
    /// 状态由 <see cref="TidegateRuntimeModule"/> 持有；物品的使用行为经静态入口 <see cref="ReadLetter"/> 进来。
    /// </summary>
    internal static class TidegateStory
    {
        internal const int PageCount = 4;
        private static TidegateStoryPersistence persistence;

        /// <summary>模块启动时绑定，销毁时解绑。</summary>
        internal static void Bind(TidegateStoryPersistence value) { persistence = value; }

        internal static TidegateStoryData Current
        {
            get { return persistence == null ? null : persistence.Load(); }
        }

        /// <summary>竹筏航线是否开放（读过信）。</summary>
        internal static bool RouteUnlocked
        {
            get { TidegateStoryData data = Current; return data != null && data.LetterRead; }
        }

        internal static bool WatchDelivered
        {
            get { TidegateStoryData data = Current; return data != null && data.WatchDelivered; }
        }

        internal static bool HasPage(int index)
        {
            TidegateStoryData data = Current;
            return data != null && index >= 0 && index < PageCount && (data.Pages & (1 << index)) != 0;
        }

        private static bool Mutate(Action<TidegateStoryData> change)
        {
            if (persistence == null || !persistence.Writable) return false;
            TidegateStoryData data = persistence.Load();
            if (data == null) return false;
            var copy = new TidegateStoryData
            {
                LetterDelivered = data.LetterDelivered, LetterRead = data.LetterRead, Pages = data.Pages, WatchDelivered = data.WatchDelivered
            };
            change(copy);
            return persistence.Store(copy);
        }

        /// <summary>
        /// 闸伯的信的「使用」：打开信，读过就开放竹筏航线并点亮图鉴。可以反复读，开放只发生一次。
        /// 由物品使用行为调用（`Integration/Tidegate/TidegateItems.cs`）。
        /// </summary>
        internal static void ReadLetter()
        {
            bool first = !RouteUnlocked;
            if (first && !Mutate(delegate (TidegateStoryData d) { d.LetterDelivered = true; d.LetterRead = true; }))
                Debug.LogWarning(TidegateRaidScene.LogTag + " 读信状态没写进存档（存档只读或换槽中），这次先只显示信");
            TidegateNoteBridge.Unlock(TidegateNoteBridge.LetterId);
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = TidegateText.LetterTitle,
                Body = TidegateText.LetterBody,
                Warning = first ? L10n.T("地堡下水道东头，水道最北边，多了一只挂马灯的竹筏。",
                    "A raft with a lantern now waits at the far north end of the bunker sewer canal.") : null,
                ConfirmLabel = L10n.T("收好信", "Keep the letter"),
                CancelLabel = L10n.T("合上", "Close"),
                Danger = false,
                OnConfirm = delegate { },
            });
        }

        /// <summary>在潮闸镇翻到第 <paramref name="index"/> 页闸工日志（0 起）。返回是不是第一次找到。</summary>
        internal static bool RecordPage(int index)
        {
            if (index < 0 || index >= PageCount || HasPage(index)) return false;
            bool stored = Mutate(delegate (TidegateStoryData d) { d.Pages |= 1 << index; });
            TidegateNoteBridge.Unlock(TidegateNoteBridge.PageId(index));
            return stored;
        }

        /// <summary>
        /// 把潮汐怀表交给闸伯：从背包里拿走一块怀表，写剧情收尾，送一个防水袋作谢礼。
        /// 返回 false 时什么都没改（没有怀表、已经交过、存档不可写）。
        /// </summary>
        internal static bool TryDeliverWatch(CharacterMainControl player, out string reason)
        {
            reason = null;
            if (WatchDelivered) { reason = L10n.T("怀表已经交给闸伯了", "Old Zha already has the watch"); return false; }
            if (persistence == null || !persistence.Writable) { reason = L10n.T("存档暂时不可写，稍后再试", "The save is not writable right now"); return false; }
            Item watch = TidegateInventory.Find(player, BossRushItemIds.TidegateTideWatch);
            if (watch == null) { reason = L10n.T("身上没有潮汐怀表", "You are not carrying the tide pocket watch"); return false; }
            if (!Mutate(delegate (TidegateStoryData d) { d.WatchDelivered = true; }))
            { reason = L10n.T("存档暂时不可写，稍后再试", "The save is not writable right now"); return false; }
            TidegateInventory.Consume(watch);
            TidegateInventory.Give(player, BossRushItemIds.TidegateDryBag);
            TidegateNoteBridge.Unlock(TidegateNoteBridge.EndingId);
            BossRushConfirmDialog.Show(new BossRushConfirmDialog.Options
            {
                Title = TidegateText.EndingTitle,
                Body = TidegateText.EndingBody,
                Warning = L10n.T("闸伯塞给你一个防水袋。", "Old Zha presses a dry bag into your hands."),
                ConfirmLabel = L10n.T("收下", "Take it"),
                CancelLabel = L10n.T("合上", "Close"),
                Danger = false,
                OnConfirm = delegate { },
            });
            return true;
        }

        /// <summary>
        /// 阿稳送湿包裹：在基地、关卡已就绪、还没送过时送一次（快递进仓库并弹官方横幅）。
        /// 物品没注册好或快递失败都不记「已送」，下次进基地再试。
        /// </summary>
        internal static void TryDeliverLetter()
        {
            TidegateStoryData data = Current;
            if (data == null || data.LetterDelivered || persistence == null || !persistence.Writable) return;
            Item letter = null;
            try
            {
                if (ItemAssetsCollection.GetPrefab(BossRushItemIds.TidegateLetter) == null) return;
                letter = ItemAssetsCollection.InstantiateSync(BossRushItemIds.TidegateLetter);
                if (letter == null || letter.TypeID != BossRushItemIds.TidegateLetter) { if (letter != null) letter.DestroyTree(); return; }
                int fallback;
                int sent = CourierService.QuickDeliverItems(new[] { letter }, L10n.T(
                    "阿稳：潮闸镇那边漂来一个湿透的包裹，写着你的名字，放你仓库了。",
                    "Awen: A soaked parcel drifted in from Tidegate with your name on it. It's in your storage."), true, out fallback);
                if (sent <= 0 && fallback <= 0) { if (letter.InInventory == null) letter.DestroyTree(); return; }
                letter = null;
                Mutate(delegate (TidegateStoryData d) { d.LetterDelivered = true; });
                Debug.Log(TidegateRaidScene.LogTag + " LETTER_DELIVERED");
            }
            catch (Exception e)
            {
                if (letter != null && letter.InInventory == null) { try { letter.DestroyTree(); } catch (Exception inner) { Debug.LogWarning(TidegateRaidScene.LogTag + " 信件回收失败：" + inner.Message); } }
                Debug.LogWarning(TidegateRaidScene.LogTag + " 湿包裹没送出去，下次进基地再试：" + e.Message);
            }
        }

        internal static void Tick() { if (persistence != null) persistence.Tick(); }
    }

    /// <summary>潮闸镇剧情与物品行为用的背包小工具：只看主角背包顶层格子。</summary>
    internal static class TidegateInventory
    {
        internal static Item Find(CharacterMainControl player, int typeId)
        {
            if (player == null || player.CharacterItem == null || player.CharacterItem.Inventory == null) return null;
            List<Item> content = player.CharacterItem.Inventory.Content;
            for (int i = 0; i < content.Count; i++)
                if (content[i] != null && content[i].TypeID == typeId && !content[i].IsBeingDestroyed) return content[i];
            return null;
        }

        internal static bool Has(CharacterMainControl player, int typeId) { return Find(player, typeId) != null; }

        /// <summary>消耗一件（可堆叠的减一，不可堆叠的整件销毁）。</summary>
        internal static void Consume(Item item)
        {
            if (item == null) return;
            if (item.Stackable && item.StackCount > 1) { item.StackCount = item.StackCount - 1; return; }
            // 先 Detach 再销毁（口径同 SkyIslandNavInstrumentConfig.TryConsumeOne），不留无主物品。
            item.Detach();
            item.DestroyTree();
        }

        /// <summary>给主角一件：先进背包，满了落地（`SkyIslandInventoryTransaction.TryDeliver` 同一口径）。</summary>
        internal static bool Give(CharacterMainControl player, int typeId)
        {
            Item item = null;
            try
            {
                if (ItemAssetsCollection.GetPrefab(typeId) == null) return false;
                item = ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || item.TypeID != typeId) { if (item != null) item.DestroyTree(); return false; }
                if (SkyIslandInventoryTransaction.TryDeliver(item, player)) return true;
                item.DestroyTree();
                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning(TidegateRaidScene.LogTag + " 物品 " + typeId + " 发放失败：" + e.Message);
                return false;
            }
        }
    }
}
