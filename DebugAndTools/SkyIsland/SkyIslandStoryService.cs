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
        /// <summary>
        /// 当前目标。HUD 每 0.5 秒读一次，而 `SkyIslandStoryRules.Objective` 每次都重新拼接字符串；
        /// 目标文本只取决于剧情位与界面语言，两者都没变就复用上一次的结果（口径同 <see cref="Summary"/>）。
        /// </summary>
        internal string CurrentObjective
        {
            get
            {
                SkyIslandStoryData data = Current;
                bool chinese = L10n.IsChinese;
                if (objectiveCache == null || objectiveFlags != data.flags || objectiveChinese != chinese)
                {
                    objectiveCache = SkyIslandStoryRules.Objective(data);
                    objectiveFlags = data.flags;
                    objectiveChinese = chinese;
                }
                return objectiveCache;
            }
        }
        private string objectiveCache;
        private int objectiveFlags;
        private bool objectiveChinese;
        internal string SaveStatus
        {
            get
            {
                if (!IsCurrentSlot)
                    return L10n.T("存档槽已改变，请重新进入群岛", "Save slot changed — re-enter the archipelago");
                if (store.HasWriteBarrier)
                    return L10n.T("群岛记录无法读取，已保护原存档；任务暂不可提交",
                        "Archipelago records are unreadable; the original save is protected and progress cannot be submitted");
                if (store.IsStoreFaulted || lastSaveError != null)
                    return L10n.T("群岛进度保存待重试：", "Archipelago progress save will retry: ") +
                        (store.LastError ?? lastSaveError);
                return store.HasPendingWrite || coordinator.HasDeferredFlush
                    ? L10n.T("群岛记录待安全时机保存", "Archipelago records will save at a safe moment")
                    : L10n.T("群岛记录已同步", "Archipelago records are in sync");
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
                summaryCache = CurrentObjective +
                    L10n.T("\n支线：种植记录", "\nSide paths: planting record") + Mark(data.Has(SkyIslandStoryFlag.PlantingRecord)) +
                    L10n.T(" 旧信", " · old letter") + Mark(data.Has(SkyIslandStoryFlag.OldLetter)) +
                    L10n.T(" 航路图", " · route chart") + Mark(data.Has(SkyIslandStoryFlag.RouteChart)) +
                    L10n.T(" 观星镜", " · telescope") + Mark(data.Has(SkyIslandStoryFlag.Telescope)) + "\n" + status;
                return summaryCache;
            }
        }
        private static string Mark(bool value) { return value ? " ✓" : " ○"; }

        /// <summary>
        /// 岛上物理落盘的去抖秒数。官方 `SavesSystem.SaveFile` 是「备份拷贝 + 整档同步写」（反编译源 `Saves/SavesSystem.cs:503`），
        /// 旧写法每接受一条事实、下一个 45 m 内无敌人的帧就整档写一次：一个新存档从头玩下来，光是首次到访 12 个区域、
        /// 首次清掉 13 组遭遇、收录 20 处见闻就是四十多次同步写盘，而且恰好落在「刚打完一组」「刚踏上新岛」的帧上。
        /// 到访、清场、见闻这类丢了也能重做的事实攒到这个秒数再一起写；剧情动作（航标、捷径、支线物证、和解、敲钟、噬风）
        /// 与已经欠着的重试照旧下一个安全帧就写。离岛、死亡与宿主销毁走 <see cref="TryClose"/> 的绕闸落盘，不受去抖影响。
        /// </summary>
        internal const float FlushDebounceSeconds = 30f;
        /// <summary>最早一条还没落盘的事实被接受的时刻（unscaled，基础设施节流而非玩法计时）；-1 表示没有待写事实。</summary>
        private float pendingSince = -1f;
        /// <summary>待写批次里有剧情动作：下一个安全帧就写，不等去抖。</summary>
        private bool urgentPending;

        /// <summary>分段计时的起点（realtimeSinceStartup，含读条、读剧情与暂停）；-1 表示本服务还没开过。</summary>
        private float timingOrigin = -1f;
        /// <summary>本趟已记过的计时事件，避免同一件事每秒重投时刷屏。</summary>
        private readonly HashSet<string> timingSeen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// 分段计时日志：给 owner 实机回填时长模型（`docs/制作教程/天空岛/天空岛_待人工验证清单.md` 的计时步骤）。
        /// 只在事件上各记一行 `[SkyIsland] SKY_TIMING t=秒 ev=事件 id=对象`——进岛、落地、剧情动作被接受、首次到访、清场、
        /// 见闻、委托与服务、挑战开始、离岛——不在任何每帧路径上。时钟用 realtimeSinceStartup：读剧情、开背包与暂停的时间
        /// 都算进「这一段实际玩了多久」。走 Debug.Log 而不是 DevLog，正式构建里照样有。
        /// </summary>
        internal void LogTiming(string ev, string id)
        {
            if (timingOrigin < 0f || string.IsNullOrEmpty(ev)) return;
            double seconds = Math.Round((double)(Time.realtimeSinceStartup - timingOrigin), 1);
            Debug.Log("[SkyIsland] SKY_TIMING t=" + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                " ev=" + ev + (string.IsNullOrEmpty(id) ? string.Empty : " id=" + id));
        }

        /// <summary>同一件事一趟只记一行（清场会每秒重投、离岛保存可能被推迟重试）。</summary>
        internal void LogTimingOnce(string ev, string id)
        {
            if (timingOrigin < 0f || !timingSeen.Add(ev + ":" + id)) return;
            LogTiming(ev, id);
        }

        /// <summary>登记一条刚被 store 接受的事实。urgent = 剧情动作，下一个安全帧就落盘。</summary>
        private void MarkPending(bool urgent)
        {
            if (pendingSince < 0f) pendingSince = Time.unscaledTime;
            if (urgent) urgentPending = true;
        }

        internal void Open()
        {
            if (opened) return;
            entrySlot = SavesSystem.CurrentSlot;
            store.EnsureSubscribed();
            store.LoadOrInit();
            slotChanged = false;
            opened = true;
            timingOrigin = Time.realtimeSinceStartup;
            timingSeen.Clear();
            LogTiming("raid_open", "slot" + entrySlot);
        }

        internal bool TryApply(SkyIslandStoryAction action, out string message)
        {
            // LoadOrInit 必须先经过槽位烙印再 Store；不允许旧会话把候选状态写到新槽。
            if (!CanWrite) { message = SaveStatus; return false; }
            SkyIslandStoryData candidate;
            if (!SkyIslandStoryRules.TryApply(Current, action, out candidate, out message)) return false;
            if (!store.Store(candidate))
            {
                message = L10n.T("群岛记录提交失败，可稍后再次操作。",
                    "Could not commit the archipelago record. Try again in a moment.");
                return false;
            }
            MarkPending(true);
            LogTiming("story", action.ToString());
            message += L10n.T("\n进度已记录，待安全时机保存。",
                "\nProgress recorded; it will be written at a safe moment.");
            return true;
        }

        internal bool RecordEncounterCleared(string regionId)
        {
            if (!CanWrite || string.IsNullOrEmpty(regionId)) return false;
            if (Current.EncounterCleared(regionId))
            {
                // 自动组按出击刷新，老档上每趟都会再清一次：存档不动，计时照记（每趟每组一行）。
                LogTimingOnce("clear", regionId);
                return false;
            }
            // 战斗 owner 只在整个遭遇确认清场后调用；中断不消费永久结果。
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.clearedEncounters);
            values.Add(regionId);
            candidate.clearedEncounters = values.ToArray();
            if (!store.Store(candidate)) return false;
            MarkPending(false);
            LogTimingOnce("clear", regionId);
            return true;
        }

        internal bool RecordSearch(string marker, out string message)
        {
            SkyIslandStoryAction action;
            if (SkyIslandStoryRules.TrySearchAction(marker, out action)) return TryApply(action, out message);
            if (!CanWrite) { message = SaveStatus; return false; }
            if (Array.IndexOf(Current.discoveredNotes, marker) >= 0)
            {
                message = L10n.T("这页见闻已经收进群岛手记。",
                    "That page is already in your archipelago notes.");
                return false;
            }
            SkyIslandStoryData candidate = Current.Copy();
            var values = new List<string>(candidate.discoveredNotes);
            values.Add(marker);
            candidate.discoveredNotes = values.ToArray();
            if (!store.Store(candidate))
            { message = L10n.T("手记提交失败，请稍后重试。", "Could not commit the note. Try again shortly."); return false; }
            MarkPending(false);
            LogTiming("note", marker);
            message = L10n.T("群岛见闻已收入本槽手记。", "The note is saved to this slot's archipelago journal.");
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
        /// <summary>
        /// 地面碰撞体名 → 区域 id。生成器把地面按区域切成 `COL_Ground_{区域}`（12 个岛 + 15 座桥，
        /// 见 tools/generate_sky_island.py）；岛（A–H、S1–S4）返回 id，桥（AB、CS1、K1…）与其它名字返回 null。
        /// 纯函数：会话据此建「脚下是哪个区域」的表，隔离回归直接执行它。
        /// </summary>
        internal static string GroundRegionOf(string colliderName)
        {
            const string prefix = "COL_Ground_";
            if (colliderName == null || colliderName.Length <= prefix.Length ||
                !colliderName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string id = colliderName.Substring(prefix.Length);
            return RegionBit(id) == 0 ? null : id;
        }

        internal bool HasVisitedRegion(string id) { return (Current.visitedRegions & RegionBit(id)) != 0; }
        internal bool RecordRegionVisited(string regionId)
        {
            int bit = RegionBit(regionId);
            if (!CanWrite || bit == 0 || (Current.visitedRegions & bit) != 0) return false;
            SkyIslandStoryData candidate = Current.Copy();
            candidate.visitedRegions |= bit;
            if (!store.Store(candidate)) return false;
            MarkPending(false);
            LogTiming("region_first", regionId);
            return true;
        }

        internal string DescribeNpc(string id)
        {
            SkyIslandStoryData data = Current;
            switch (id)
            {
                case "sky_qinghe":
                    return data.Has(SkyIslandStoryFlag.PlantingDelivered)
                        ? L10n.T("晴禾：新风车转起来了。下一船归来时，他们会有热菜吃。",
                            "Qinghe: The new pinwheel is turning. When the next ship comes home there will be a hot meal waiting.")
                        : L10n.T("晴禾：蛙鸣池那边有我丢下的种植记录。没来得及说出口的事，都写在里面了。",
                            "Qinghe: My planting record is still out at Frogsong Pool. Everything I never got to say is written in it.");
                case "sky_weibai":
                    return L10n.T("苇白：西边悬根林有风标，东边残星工坊有星灯，先去哪边都行。装置还在，修好它们就能让双航标门重新工作。\n",
                        "Weibai: The wind beacon is west in the Hanging Root Wood, the star lamp east at the Fallen Star Workshop — either order works. The devices are still standing; repair them and the twin-beacon gate runs again.\n") + CurrentObjective;
                case "sky_fuzhou":
                    return data.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("浮舟：钟声听见了。船一直在这里，下次来时，我们再讲归航的故事。",
                            "Fuzhou: I heard the bell. The boat has always been here — come back and we will tell the homecoming story again.")
                        : L10n.T("浮舟：沿桥去风铃集找苇白。走累了随时回来，码头的系泊桩会送你回家，已记录的故事下次继续。",
                            "Fuzhou: Follow the bridge to Windchime Market and find Weibai. Come back whenever you tire — the mooring post at the dock takes you home, and whatever you have recorded carries over.");
                case "sky_miantai":
                    return L10n.T("眠苔：风标困在根环那头，先清掉附近的威胁再校准。倒挂邮亭还吊着一封信——风没有把它送到，或许你可以。",
                        "Miantai: The wind beacon is stuck out past the root ring; clear the threats around it before you calibrate. A letter still hangs in the Upturned Post Hut — the wind never delivered it. Perhaps you can.");
                case "sky_zheling":
                    return data.ZhelingResolved
                        ? (data.Has(SkyIslandStoryFlag.ZhelingReconciled)
                            ? L10n.T("折翎：路已修好。这次我守着灯，等大家回来。",
                                "Zheling: The road is mended. This time I keep the light and wait for them to come home.")
                            : L10n.T("旧腰牌上刻着：『航路交给你。』",
                                "The old badge is engraved: 'The route is yours now.'"))
                        : L10n.T("折翎：我不会再让人走进那场风灾。若有旧信和航路图，就留下来谈；若坚持通行，请明确挑战。",
                            "Zheling: I will not let anyone walk into that storm again. If you carry the old letter and the route chart, stay and talk. If you insist on passing, challenge me outright.");
                case "sky_bellkeeper":
                    return data.BellKeeperResolved
                        ? L10n.T("钟守：去吧，敲响归航钟。让他们知道，岛上还有人在等。",
                            "The Bell Keeper: Go on, ring the Homecoming Bell. Let them know someone on the islands is still waiting.")
                        : L10n.T("无声钟守：钟一响，就会有人再出海。我需要你证明航路安全，否则先停下我的守钟装置。",
                            "The Silent Bell Keeper: Ring it and someone puts to sea again. Prove the lanes are safe — or stop my bell engine first.");
                default: return CurrentObjective;
            }
        }

        internal void Tick(bool safeToFlush)
        {
            // 独立出击地图由会话提供实际战斗安全门；无论官方基地身份如何都不在交火帧写盘。
            if (!IsCurrentSlot || !safeToFlush) return;
            if (!TryRecoverFaultedStore()) return;
            // 共享引擎 Tick 固定只在基地重试；独立地图使用受战斗门保护的 RequestFlush 接续欠账。
            if (store.HasPendingWrite || coordinator.HasDeferredFlush)
            {
                // 去抖只挡「丢了也能重做」的事实（见 FlushDebounceSeconds）；剧情动作与已经欠着的重试照旧立刻写。
                if (pendingSince < 0f) pendingSince = Time.unscaledTime;
                if (urgentPending || coordinator.HasDeferredFlush || Time.unscaledTime - pendingSince >= FlushDebounceSeconds)
                    coordinator.RequestFlush(out lastSaveError);
            }
            if (!store.HasPendingWrite && !coordinator.HasDeferredFlush && !store.IsStoreFaulted)
            {
                lastSaveError = null;
                pendingSince = -1f;
                urgentPending = false;
            }
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
            // 离岛计时只记第一次尝试：保存被官方推迟时恢复 owner 每秒重试一次，不重复记。
            LogTimingOnce("raid_close", null);
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
                // 接回来的快照是恢复出来的欠账，不等去抖。
                MarkPending(true);
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
            // 旧槽的待写批次不会再写（IsCurrentSlot 挡住），去抖状态一并作废。
            pendingSince = -1f;
            urgentPending = false;
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
