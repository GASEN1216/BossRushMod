using System;
using System.Collections.Generic;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>会话持有的独立槽位剧情门面。共享 store / coordinator 分别拥有存档订阅与唯一物理写盘。</summary>
    internal sealed class SkyIslandStoryService
    {
        private BossRushSlotJsonStore<SkyIslandStoryData> store;
        private BossRushSaveCoordinatorEngine coordinator;
        private bool opened, slotChanged;
        private int entrySlot;
        private string lastSaveError;
        private float nextRecoveryAt;
        private string summaryCache, summaryStatus;
        private int summaryFlags;

        internal SkyIslandStoryService()
        {
            store = CreateStore();
            coordinator = new BossRushSaveCoordinatorEngine(new SaveSource(store), false);
        }

        private BossRushSlotJsonStore<SkyIslandStoryData> CreateStore()
        {
            return new BossRushSlotJsonStore<SkyIslandStoryData>(new BossRushSlotJsonStoreSpec<SkyIslandStoryData>
            {
                StorageKey = SkyIslandStoryRules.StorageKey,
                SchemaVersion = SkyIslandStoryRules.SchemaVersion,
                LogPrefix = "[SkyIsland] ", DisplayName = "晴岚群岛",
                CreateDefault = SkyIslandStoryRules.CreateDefault,
                Encode = SkyIslandStoryCodec.Encode, Decode = SkyIslandStoryCodec.Decode,
                ReadSchemaVersion = SkyIslandStoryCodec.ReadSchemaVersion,
                NotifySlotChanged = OnSlotChanged
            });
        }

        internal bool IsCurrentSlot
        {
            get
            {
                if (!opened || slotChanged) return false;
                try { return SavesSystem.CurrentSlot == entrySlot; }
                catch (Exception) { return false; }
            }
        }
        internal SkyIslandStoryData Current { get { return store.Current; } }
        internal bool CanWrite { get { return IsCurrentSlot && !store.HasWriteBarrier && !store.IsStoreFaulted; } }
        internal string CurrentObjective { get { return SkyIslandStoryRules.Objective(Current); } }
        internal string SaveStatus
        {
            get
            {
                if (!IsCurrentSlot) return "存档槽已改变，请重新进入群岛";
                if (store.HasWriteBarrier) return "群岛记录无法读取，已保护原存档；任务暂不可提交";
                if (store.IsStoreFaulted || lastSaveError != null) return "群岛进度保存待重试：" + (store.LastError ?? lastSaveError);
                return store.HasPendingWrite || coordinator.HasDeferredFlush ? "群岛记录待安全时机保存" : "群岛记录已同步";
            }
        }
        /// <summary>
        /// F6 地图每帧读一次摘要，逐帧重建这串文本会持续产生小额分配（实测 264–320 B/次）。
        /// 只在剧情位或保存状态真的改变时重建。
        /// </summary>
        internal string Summary
        {
            get
            {
                SkyIslandStoryData data = Current;
                string status = SaveStatus;
                if (summaryCache != null && summaryFlags == data.flags && string.Equals(summaryStatus, status, StringComparison.Ordinal))
                    return summaryCache;
                summaryFlags = data.flags;
                summaryStatus = status;
                summaryCache = CurrentObjective + "\n支线：种植记录" + Mark(data.Has(SkyIslandStoryFlag.PlantingRecord)) +
                    " 旧信" + Mark(data.Has(SkyIslandStoryFlag.OldLetter)) + " 航路图" + Mark(data.Has(SkyIslandStoryFlag.RouteChart)) +
                    " 观星镜" + Mark(data.Has(SkyIslandStoryFlag.Telescope)) + "\n" + status;
                return summaryCache;
            }
        }
        private static string Mark(bool value) { return value ? " ✓" : " ○"; }

        internal void Open()
        {
            if (opened) return;
            entrySlot = SavesSystem.CurrentSlot;
            store.EnsureSubscribed();
            store.LoadOrInit();
            slotChanged = false;
            opened = true;
        }

        internal bool TryApply(SkyIslandStoryAction action, out string message)
        {
            // LoadOrInit 必须先经过槽位烙印再 Store；不允许旧会话把候选状态写到新槽。
            if (!CanWrite) { message = SaveStatus; return false; }
            SkyIslandStoryData candidate;
            if (!SkyIslandStoryRules.TryApply(Current, action, out candidate, out message)) return false;
            if (!store.Store(candidate)) { message = "群岛记录提交失败，可稍后再次操作。"; return false; }
            message += "\n进度已记录，待安全时机保存。";
            return true;
        }

        internal bool RecordEncounterCleared(string regionId)
        {
            if (!CanWrite || string.IsNullOrEmpty(regionId) || Current.EncounterCleared(regionId)) return false;
            // 战斗 owner 只在整个遭遇确认清场后调用；中断不消费永久结果。
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.clearedEncounters);
            values.Add(regionId);
            candidate.clearedEncounters = values.ToArray();
            return store.Store(candidate);
        }

        internal bool RecordSearch(string marker, out string message)
        {
            SkyIslandStoryAction action;
            if (SkyIslandStoryRules.TrySearchAction(marker, out action)) return TryApply(action, out message);
            if (!CanWrite) { message = SaveStatus; return false; }
            if (Array.IndexOf(Current.discoveredNotes, marker) >= 0) { message = "这页见闻已经收进群岛手记。"; return false; }
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.discoveredNotes);
            values.Add(marker);
            candidate.discoveredNotes = values.ToArray();
            if (!store.Store(candidate)) { message = "手记提交失败，请稍后重试。"; return false; }
            message = "群岛见闻已收入本槽手记。";
            return true;
        }

        internal static int RegionBit(string id)
        {
            switch (id)
            {
                case "A": return 1; case "B": return 2; case "C": return 4; case "D": return 8;
                case "E": return 16; case "F": return 32; case "G": return 64; case "H": return 128;
                case "S1": return 256; case "S2": return 512; case "S3": return 1024; case "S4": return 2048;
                default: return 0;
            }
        }
        internal bool HasVisitedRegion(string id) { return (Current.visitedRegions & RegionBit(id)) != 0; }
        internal bool RecordRegionVisited(string regionId)
        {
            int bit = RegionBit(regionId);
            if (!CanWrite || bit == 0 || (Current.visitedRegions & bit) != 0) return false;
            SkyIslandStoryData candidate = Current.Copy();
            candidate.visitedRegions |= bit;
            return store.Store(candidate);
        }

        internal string DescribeNpc(string id)
        {
            SkyIslandStoryData data = Current;
            switch (id)
            {
                case "sky_qinghe": return data.Has(SkyIslandStoryFlag.PlantingDelivered) ? "晴禾：新风车转起来了。下一船归来时，他们会有热菜吃。" : "晴禾：蛙鸣池那边有我丢下的种植记录。没来得及说出口的事，都写在里面了。";
                case "sky_weibai": return "苇白：西边悬根林有风标，东边残星工坊有星灯，先去哪边都行。装置还在，修好它们就能让双航标门重新工作。\n" + CurrentObjective;
                case "sky_fuzhou": return data.Has(SkyIslandStoryFlag.Ending) ? "浮舟：钟声听见了。船一直在这里，下次来时，我们再讲归航的故事。" : "浮舟：沿桥去风铃集找苇白。走累了随时回来，码头的系泊桩会送你回家，已记录的故事下次继续。";
                case "sky_miantai": return "眠苔：风标困在根环那头，先清掉附近的威胁再校准。倒挂邮亭还吊着一封信——风没有把它送到，或许你可以。";
                case "sky_zheling": return data.ZhelingResolved ? (data.Has(SkyIslandStoryFlag.ZhelingReconciled) ? "折翎：路已修好。这次我守着灯，等大家回来。" : "旧腰牌上刻着：『航路交给后来的人。』") : "折翎：我不会再让人走进那场风灾。若有旧信和航路图，就留下来谈；若坚持通行，请明确挑战。";
                case "sky_bellkeeper": return data.BellKeeperResolved ? "钟守：去吧，敲响归航钟。让他们知道，岛上还有人在等。" : "无声钟守：钟一响，就会有人再出海。我需要你证明航路安全，否则先停下我的守钟装置。";
                default: return CurrentObjective;
            }
        }

        internal void Tick(bool safeToFlush)
        {
            // 独立出击地图由会话提供实际战斗安全门；无论官方基地身份如何都不在交火帧写盘。
            if (!IsCurrentSlot || !safeToFlush) return;
            if (!TryRecoverFaultedStore()) return;
            // 共享引擎 Tick 固定只在基地重试；独立地图使用受战斗门保护的 RequestFlush 接续欠账。
            if (store.HasPendingWrite || coordinator.HasDeferredFlush) coordinator.RequestFlush(out lastSaveError);
            if (!store.HasPendingWrite && !coordinator.HasDeferredFlush && !store.IsStoreFaulted) lastSaveError = null;
        }

        internal void Close()
        {
            if (!opened) return;
            if (TryClose()) return;
            if (IsCurrentSlot)
                ModBehaviour.DevLog("[SkyIsland] [WARNING] 离岛保存未完成：" + (store.LastError ?? coordinator.LastError));
            store.ShutdownSubscription();
            opened = false;
        }

        /// <summary>正常离岛先尝试持久化；推迟时保持订阅和 pending，由 recovery owner 接续。</summary>
        internal bool TryClose()
        {
            if (!opened) return true;
            if (IsCurrentSlot && (!TryRecoverFaultedStore() || !coordinator.TryFlushOnHostDestroy())) return false;
            store.ShutdownSubscription();
            opened = false;
            return true;
        }

        private bool TryRecoverFaultedStore()
        {
            if (!store.IsStoreFaulted) return true;
            if (!IsCurrentSlot || SavesSystem.IsSaving || Time.unscaledTime < nextRecoveryAt) return false;
            nextRecoveryAt = Time.unscaledTime + 1f;
            BossRushSlotJsonStore<SkyIslandStoryData> replacement = null;
            bool adopted = false;
            try
            {
                SkyIslandStoryData accepted = store.Current.Copy();
                if (SkyIslandStoryCodec.Decode(SkyIslandStoryCodec.Encode(accepted)) == null)
                { lastSaveError = "recovery_snapshot_invalid"; return false; }
                // 旧 store 的单向故障不清除。先校验当前槽原 key，再把最后已接受快照交给新 owner。
                replacement = CreateStore();
                replacement.EnsureSubscribed();
                replacement.LoadOrInit();
                if (!IsCurrentSlot || replacement.HasWriteBarrier || replacement.IsStoreFaulted)
                { lastSaveError = replacement.LastError ?? "recovery_read_barrier"; return false; }
                if (!replacement.Store(accepted))
                { lastSaveError = replacement.LastError ?? "recovery_store_failed"; return false; }
                var replacementCoordinator = new BossRushSaveCoordinatorEngine(new SaveSource(replacement), false);
                store.ShutdownSubscription();
                store = replacement;
                coordinator = replacementCoordinator;
                adopted = true;
                lastSaveError = null;
                return true;
            }
            catch (Exception e)
            {
                lastSaveError = "recovery_failed:" + e.GetType().Name;
                return false;
            }
            finally
            {
                if (!adopted && replacement != null) replacement.ShutdownSubscription();
            }
        }

        private void OnSlotChanged()
        {
            slotChanged = true;
            if (coordinator != null) coordinator.NotifySlotChanged();
        }

        private sealed class SaveSource : IBossRushSaveBatchSource
        {
            private readonly BossRushSlotJsonStore<SkyIslandStoryData> store;
            internal SaveSource(BossRushSlotJsonStore<SkyIslandStoryData> value) { store = value; }
            public string LogPrefix { get { return "[SkyIsland] "; } }
            public bool HasPendingWrite { get { return store.HasPendingWrite; } }
            public bool IsStoreFaulted { get { return store.IsStoreFaulted; } }
            public bool HasSnapshotObligation { get { return false; } }
            public string LastError { get { return store.LastError; } }
            public bool CollectSnapshot(out string error) { error = null; return true; }
            public bool FlushPending() { return store.FlushPending(); }
            public void OnPhysicalSaveSucceeded() { }
        }
    }
}
