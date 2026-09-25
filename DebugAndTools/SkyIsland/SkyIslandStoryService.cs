using System;
using System.Collections.Generic;
using Saves;
using UnityEngine;

namespace BossRush
{
    /// <summary>会话持有的独立槽位剧情门面。共享 store / coordinator 分别拥有存档订阅与唯一物理写盘。</summary>
    internal sealed partial class SkyIslandStoryService
    {
        private BossRushSlotJsonStore<SkyIslandStoryData> store;
        private BossRushSaveCoordinatorEngine coordinator;
        private bool opened, slotChanged;
        private int entrySlot;
        private string lastSaveError;
        private float nextRecoveryAt;
        private bool assetSnapshotRequired;
        private bool cashSnapshotRequired, rewardCommitting;
        private SkyIslandStoryAction? cashPaidPendingAction;
        /// <summary>这一趟出击里暂不入档的永久记录 id（见 <see cref="EncodeForSave"/>）。</summary>
        private readonly HashSet<string> raidHeldNotes = new HashSet<string>(StringComparer.Ordinal);
        private string summaryCache, summaryStatus;
        private int summaryFlags;
        private bool summaryChinese;

        internal SkyIslandStoryService()
        {
            store = CreateStore();
            coordinator = new BossRushSaveCoordinatorEngine(new SaveSource(this, store), false);
        }

        private BossRushSlotJsonStore<SkyIslandStoryData> CreateStore()
        {
            return new BossRushSlotJsonStore<SkyIslandStoryData>(new BossRushSlotJsonStoreSpec<SkyIslandStoryData>
            {
                StorageKey = SkyIslandStoryRules.StorageKey,
                SchemaVersion = SkyIslandStoryRules.SchemaVersion,
                LogPrefix = "[SkyIsland] ", DisplayName = "晴岚群岛",
                CreateDefault = SkyIslandStoryRules.CreateDefault,
                Encode = EncodeForSave, Decode = SkyIslandStoryCodec.Decode,
                ReadSchemaVersion = SkyIslandStoryCodec.ReadSchemaVersion,
                NotifySlotChanged = OnSlotChanged, BeforeCollectSaveData = CollectPendingCash
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
        /// 会话在出击图（天空岛）里打开的剧情门面：花材料换来的永久记录随这一趟出击结算，不在岛上立资产快照。
        /// 官方仓库箱只在基地场景（出击图里 <c>PlayerStorage.Instance</c> 为空），出击中途退游戏背包回到出击前、不算死；
        /// 在岛上存背包会让「点完灯直接退游戏」保住这一趟捡的东西。owner 2026-09-14 拍板「随撤离一起存」。
        /// </summary>
        internal bool RaidHeldCosts;

        /// <summary>
        /// 永久记录（点灯、放生、纪念品）提交前的资产义务。基地：立实物快照，与手记同一批落盘。
        /// 出击图（<see cref="RaidHeldCosts"/>）：记录内存里立刻算数，存档里先不写，等 <see cref="SettleRaidHeld"/> 结算。
        /// </summary>
        internal bool RequireAssetSnapshot(string noteId, out string error)
        {
            error = null;
            if (!CanWrite || SavesSystem.IsSaving || CharacterMainControl.Main == null || CharacterMainControl.Main.CharacterItem == null)
            { error = "asset_save_not_ready"; return false; }
            if (RaidHeldCosts)
            {
                if (string.IsNullOrEmpty(noteId)) { error = "raid_record_needs_id"; return false; }
                raidHeldNotes.Add(noteId);
                return true;
            }
            if (PlayerStorage.Instance == null || !PlayerStorage.Instance.HasInitialized() || PlayerStorage.Loading ||
                PlayerStorage.Inventory == null || PlayerStorageBuffer.Instance == null)
            { error = "asset_save_not_ready"; return false; }
            assetSnapshotRequired = true;
            return true;
        }

        /// <summary>
        /// 写进存档的编码：这一趟暂不入档的记录从手记里剥掉，其余事实（到访、清场、剧情动作）照常落盘。
        /// 官方收集存档（撤离、切图）也走这里，所以任何一次写盘都带不走它们；崩溃时它们本来就不在盘上。
        /// </summary>
        private string EncodeForSave(SkyIslandStoryData value)
        {
            if (value == null || raidHeldNotes.Count == 0 || value.discoveredNotes == null) return SkyIslandStoryCodec.Encode(value);
            SkyIslandStoryData persisted = value.Copy();
            var kept = new List<string>(persisted.discoveredNotes.Length);
            foreach (string id in persisted.discoveredNotes)
                if (!raidHeldNotes.Contains(id)) kept.Add(id);
            persisted.discoveredNotes = kept.ToArray();
            return SkyIslandStoryCodec.Encode(persisted);
        }

        /// <summary>
        /// 这一趟出击结算。<paramref name="keep"/> 为真：回到基地（撤离或倒下，官方已按结果存好背包），记录放进待写批次；
        /// 为假：退游戏或会话被销毁（背包会回到出击前），从手记里撤掉，与没做过一样。
        /// </summary>
        internal void SettleRaidHeld(bool keep)
        {
            if (raidHeldNotes.Count == 0) return;
            SkyIslandStoryData current = Current;
            var present = new List<string>();
            if (current != null && current.discoveredNotes != null)
                foreach (string id in current.discoveredNotes)
                    if (raidHeldNotes.Contains(id)) present.Add(id);
            if (present.Count == 0) { raidHeldNotes.Clear(); return; }
            // keep=false 时，写入屏障 / StoreFaulted 下必须保留 raidHeldNotes：EncodeForSave 会继续把这些
            // 本应随出击回滚的记录剥掉，随后 TryRecoverFaultedStore 才不会把内存里的旧快照误救回磁盘。
            // 旧代码先 Clear 再因 !CanWrite 返回，恢复 owner 会把灯 / 蛙记录永久保存。
            if (!CanWrite)
            {
                if (keep) raidHeldNotes.Clear();
                return;
            }
            SkyIslandStoryData candidate = current.Copy();
            if (keep)
            {
                // Store 在接受候选时就调用 EncodeForSave；先解除排除，返回基地的记录才会进入待写 JSON。
                raidHeldNotes.Clear();
            }
            else
            {
                var kept = new List<string>(candidate.discoveredNotes.Length);
                foreach (string id in candidate.discoveredNotes)
                    if (present.IndexOf(id) < 0) kept.Add(id);
                candidate.discoveredNotes = kept.ToArray();
            }
            bool stored = store.Store(candidate);
            if (stored)
            {
                raidHeldNotes.Clear();
                MarkPending(true);
            }
            Debug.Log("[SkyIsland] RAID_HELD_SETTLE keep=" + keep + " stored=" + stored + " ids=" + string.Join(",", present.ToArray()));
        }
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
                    return L10n.T("存档槽已改变，请重新进入群岛", "Save slot changed. Re-enter the archipelago");
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
        /// **有问题才回话**；一切正常时返回 null。
        ///
        /// 「群岛记录已同步」是存档系统的内部状态，不该印给玩家看——它此前被
        /// <see cref="Summary"/> 无条件拼在旅程进度末尾。HUD 那边本来就只在出问题时才显示
        /// （`SkyIslandSession` 的 `!story.CanWrite ? story.SaveStatus : null`），这里跟上同一条口径。
        /// <see cref="SaveStatus"/> 本身不动：F3 验收面板要读到「正常」那一支。
        /// </summary>
        internal string SaveProblem
        {
            get
            {
                if (!IsCurrentSlot || store.HasWriteBarrier || store.IsStoreFaulted || lastSaveError != null)
                    return SaveStatus;
                return null;
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
                string status = SaveProblem;
                if (summaryCache != null && summaryFlags == data.flags && summaryChinese == L10n.IsChinese
                    && string.Equals(summaryStatus, status, StringComparison.Ordinal))
                    return summaryCache;
                summaryFlags = data.flags;
                summaryChinese = L10n.IsChinese;
                summaryStatus = status;
                summaryCache = CurrentObjective +
                    L10n.T("\n支线：种植记录", "\nSide paths: planting record") + Mark(data.Has(SkyIslandStoryFlag.PlantingRecord)) +
                    L10n.T(" 旧信", " · old letter") + Mark(data.Has(SkyIslandStoryFlag.OldLetter)) +
                    L10n.T(" 航路图", " · route chart") + Mark(data.Has(SkyIslandStoryFlag.RouteChart)) +
                    L10n.T(" 观星镜", " · telescope") + Mark(data.Has(SkyIslandStoryFlag.Telescope))
                    + (string.IsNullOrEmpty(status) ? string.Empty : "\n" + status);
                return summaryCache;
            }
        }
        private static string Mark(bool value) { return value ? " √" : " ○"; }

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
        /// 分段计时日志：给 owner 实机回填时长模型（`docs/guides/sky-island/天空岛_待人工验证清单.md` 的计时步骤）。
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
            // 不再在回话末尾追加「进度已记录，待安全时机保存」：存档一切正常时那是状态转储、不是剧情（2026-09-14 审核 F-24 ①）；
            // 存档出了问题才需要玩家知道，那一句由 Summary 里的 SaveProblem 给。
            return true;
        }

        // 与 Campaign 的补偿式交付相同：确认到账、提交候选、同批采集现金，失败保留重试 owner。
        internal bool TryDeliverQuest(SkyIslandStoryAction action, int money, out string message)
        {
            message = L10n.T("奖金暂时无法提交，请稍后重试。", "The reward cannot be committed yet. Please try again.");
            if (rewardCommitting || !CanWrite || SavesSystem.IsSaving || Duckov.Economy.EconomyManager.Instance == null) return false;
            SkyIslandStoryData candidate;
            string appliedMessage;
            if (!SkyIslandStoryRules.TryApply(Current, action, out candidate, out appliedMessage)) { message = appliedMessage; return false; }
            if (cashPaidPendingAction.HasValue && cashPaidPendingAction.Value != action) return false;
            rewardCommitting = true;
            try
            {
                if (!cashPaidPendingAction.HasValue)
                {
                    long before = Duckov.Economy.EconomyManager.Money;
                    if (money <= 0 || before > long.MaxValue - money) return false;
                    try { Duckov.Economy.EconomyManager.Add(money); }
                    catch (Exception e) { ModBehaviour.DevLog("[SkyIsland] 奖金通知异常: " + e.Message); }
                    if (Duckov.Economy.EconomyManager.Money != before + money) return false;
                    cashPaidPendingAction = action;
                    cashSnapshotRequired = true;
                }
                if (!store.Store(candidate))
                {
                    long before = Duckov.Economy.EconomyManager.Money;
                    try { Duckov.Economy.EconomyManager.Pay(new Duckov.Economy.Cost((long)money), true, true); }
                    catch (Exception e) { ModBehaviour.DevLog("[SkyIsland] 奖金回滚异常: " + e.Message); }
                    if (Duckov.Economy.EconomyManager.Money == before - money) cashPaidPendingAction = null;
                    return false;
                }
                cashPaidPendingAction = null;
                MarkPending(true);
                message = appliedMessage;
                return true;
            }
            finally { rewardCommitting = false; }
        }

        private bool CollectPendingCash()
        {
            if (rewardCommitting) return false;
            if (!cashSnapshotRequired) return true;
            try
            {
                if (!IsCurrentSlot || SavesSystem.IsSaving || Duckov.Economy.EconomyManager.Instance == null) return false;
                SavesSystem.Save<Duckov.Economy.EconomyManager.SaveData>("EconomyData",
                    (Duckov.Economy.EconomyManager.SaveData)Duckov.Economy.EconomyManager.Instance.GenerateSaveData());
                return true;
            }
            catch (Exception e) { lastSaveError = "cash_collect_failed:" + e.GetType().Name; return false; }
        }

        /// <summary>把入口门上线前已经玩过群岛的槽位迁移成完整序章状态，并按既有事实回填岛上主线任务的接取 / 交付位；新槽不动。</summary>
        internal bool EnsureRouteCompatibility(out bool migrated, out string message)
        {
            migrated = false;
            message = null;
            if (!CanWrite) { message = SaveStatus; return false; }
            SkyIslandStoryData candidate;
            bool changed = SkyIslandStoryRules.TryGrantLegacyRoute(Current, out candidate);
            SkyIslandStoryData withQuests;
            if (SkyIslandStoryRules.TryBackfillIslandQuests(changed ? candidate : Current, out withQuests)) { candidate = withQuests; changed = true; }
            if (!changed) return true;
            if (!store.Store(candidate))
            {
                message = L10n.T("旧群岛记录迁移失败，请稍后重试。",
                    "Could not migrate the existing archipelago record. Try again shortly.");
                return false;
            }
            migrated = true;
            MarkPending(true);
            LogTiming("story", "LegacyRouteUnlock");
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

        /// <summary>
        /// 信鸽来信、归航船名册、纪念品发放记录与点起来的风晶灯：和见闻一样写进本槽手记（`discoveredNotes`），共用 <see cref="RecordSearch"/> 的
        /// 去重、去抖与计时口径，不加存档字段。只收已登记的 id（<see cref="SkyIslandLetters.Find"/> / <see cref="SkyIslandCrew.IndexOf"/> /
        /// <see cref="SkyIslandItemRules.FindKeepsake"/> / <see cref="SkyIslandLights.Find"/>），不让任意字符串进存档。
        /// </summary>
        internal bool RecordNote(string id, out string message)
        {
            // 内容批次四：放回蛙鸣池的蛙卵（Frog_1..3，SkyIslandMosquitoRules.IsFrogNote）同样只收登记过的 id。
            // 头目 / 岛主 R1：首杀记录（Lord_Foreman / Chief_Stargazer，SkyIslandBossRules.IsBossNote）。
            if (SkyIslandLetters.Find(id) == null && SkyIslandCrew.IndexOf(id) < 0 && SkyIslandItemRules.FindKeepsake(id) == null &&
                SkyIslandLights.Find(id) == null && !SkyIslandMosquitoRules.IsFrogNote(id) && !SkyIslandBossRules.IsBossNote(id))
            {
                message = L10n.T("这条手记没有登记。", "That journal entry is not registered.");
                return false;
            }
            // 永久成本记录必须与已扣除材料的背包一起落盘：基地立实物快照（SaveFile 本身不会采集角色），
            // 出击图里随这一趟结算（RaidHeldCosts）。放在提交入口，点灯、放生及后续调用方都不能漏掉这项义务。
            if (SkyIslandLights.Find(id) != null || SkyIslandMosquitoRules.IsFrogNote(id))
            {
                if (!CanWrite) { message = SaveStatus; return false; }
                string error;
                if (Array.IndexOf(Current.discoveredNotes, id) < 0 && !RequireAssetSnapshot(id, out error))
                {
                    message = L10n.T("物品存档尚未就绪，请稍后再试。", "Item saving is not ready. Please try again shortly.");
                    return false;
                }
            }
            return RecordSearch(id, out message);
        }

        /// <summary>
        /// 仅供「已写入纪念品台账、但官方物品交付在真正接管实例前抛异常」的补偿路径使用。
        /// 只删除当前槽中仍存在的精确 id；写屏障、槽位变化或编码失败时拒绝修改，避免把别的槽/别的事实一起回滚。
        /// </summary>
        internal bool RemoveNote(string id)
        {
            if (!CanWrite || string.IsNullOrEmpty(id)) return false;
            SkyIslandStoryData current = Current;
            if (current == null || current.discoveredNotes == null) return false;
            var values = new List<string>(current.discoveredNotes);
            int index = values.IndexOf(id);
            if (index < 0) return false;
            values.RemoveAt(index);
            SkyIslandStoryData candidate = current.Copy();
            candidate.discoveredNotes = values.ToArray();
            if (!store.Store(candidate)) return false;
            raidHeldNotes.Remove(id);
            MarkPending(true);
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

        /// <summary>
        /// 面对面跟这位居民说话时他会讲什么（官方对话一句一屏，`SkyIslandResidentDialogue.Split` 负责断句）。
        ///
        /// 【人设，写话之前先看这一栏】头顶气泡的话语表 `SkyIslandChatterLines` 与这里共用同一份人设：
        /// 晴禾是种地的（短句、絮叨，拿吃的表达关心）；苇白管委托板（嘴快、爱张罗，说话像在派活）；
        /// 浮舟是码头老手（话少、稳，说船说绳说料）；眠苔是药师（冷淡专业，只关心伤口）；
        /// 折翎是旧航路守卫（戒备，句子短而重）；无声钟守只在面对面时开口，气泡里一个字都不说。
        ///
        /// 【2026-09-17 复核】逐条读了这里的三十多句，把读着像 UI 提示的四句换成各人自己的口气
        /// （苇白派活那句、浮舟指路那句、眠苔说风标那句、浮舟说熔晶炉那句），信息一个不少。
        /// 改写**必须保持屏数不变**：全自动验收按屏序截图、并对某一屏断言关键词
        /// （`Assets/Data/SkyIslandAutotest.json` 的 `shot:xxx_lineN` 与 `dialogue_line_contains`）。
        /// 核对屏数用 `python3 tools/sky_island_line_screens.py "<台词>"`。
        /// </summary>
        internal string DescribeNpc(string id, bool married = false, bool onIsland = true, bool weibaiAway = false)
        {
            SkyIslandStoryData data = Current;
            switch (id)
            {
                // 居民台词随进度与收到的信变化：信鸽送来的信大多是写给他们的（SkyIslandLetters），收下之后当面会提一句。
                case "sky_qinghe":
                    return QingheStoryLine(data, married, onIsland) +
                        (SkyIslandLetters.Collected(data, "Letter_03")
                            ? L10n.T("\n那封没署名的信，我认得字。田埂上那一格，还给他留着。",
                            "\nI know the writing on that unsigned letter. His plot is still waiting.")
                            : string.Empty) + QingheGnatLine(data, onIsland) + SkyIslandBossRules.ResidentLine("sky_qinghe", data);
                case "sky_weibai":
                    return WeibaiStoryLine(data, married, onIsland) +
                        (SkyIslandLetters.Collected(data, "Letter_02")
                            ? L10n.T("苇生的信你收到了？这家伙，还说回来替我修风铃。\n",
                            "You got Weisheng's letter? He still says he'll come back and fix my chimes.\n")
                            : string.Empty) + SkyIslandBossRules.ResidentLine("sky_weibai", data) + WeibaiZapperLine(data);
                case "sky_fuzhou":
                    return (data.Has(SkyIslandStoryFlag.Ending)
                        ? L10n.T("听见钟声了，船头的名册也添了四页。归来的人亲手写的，去看看。",
                            "Heard the bell. Four new pages in the roster at the bow. Written by the people who came home.")
                        : weibaiAway
                            ? L10n.T("航路任务找苇白接交；她没来岛上，就用风铃集委托板。要返航就回码头解系泊桩，记下的事下趟接着算。",
                            "Take and turn in route quests with Weibai; if she stayed home, use the Windchime Market board. To head home, use the mooring post, and your progress carries over.")
                            : L10n.T("沿桥去风铃集，找苇白。要返航就回码头解系泊桩，记下的事下趟接着算。",
                            "Follow the bridge to Windchime Market and find Weibai. To head home, come back to the mooring post, and your progress carries over.")) +
                        (SkyIslandLetters.Collected(data, "Letter_01")
                            ? L10n.T("\n阿潮的缆绳，我挂回最高那根桩上了。打结的手法还是老样子。",
                            "\nAchao's mooring line is back on the tallest post. Same old knots.")
                            : string.Empty) + FuzhouLampLine(data) + SkyIslandBossRules.ResidentLine("sky_fuzhou", data);
                case "sky_miantai":
                    return MiantaiLine(data) + MiantaiItchLine + SkyIslandBossRules.ResidentLine("sky_miantai", data);
                case "sky_zheling":
                    return ZhelingLine(data);
                case "sky_bellkeeper":
                    if (data.Has(SkyIslandStoryFlag.Ending))
                        return L10n.T("这回钟声传到海上了。名册新添的那行字，我会留着。",
                            "The bell reached the sea this time. I'll keep that new line in the register.") +
                            (SkyIslandMosquitoRules.FrogsComplete(data)
                                ? L10n.T("\n夜里敲钟，蛙鸣池也有蛙应声了。",
                            "\nAt night, frogs at Frogsong Pool answer the bell.")
                                : string.Empty) + BellKeeperEchoLine(data);
                    return data.BellKeeperResolved
                        ? L10n.T("去敲归航钟吧。让他们知道，岛上还有人在等。",
                            "Ring the Homecoming Bell. Let them know we're still here.")
                        : L10n.T("钟一响，又会有人出海。证明航路安全，我才放行；不然就来停下守钟装置。",
                            "Once the bell rings, people will put to sea. Prove the lanes safe, or stop my bell engine.");
                default: return CurrentObjective;
            }
        }

        private static string MiantaiLine(SkyIslandStoryData data)
        {
            return data.Has(SkyIslandStoryFlag.WindBeacon)
                        ? (data.Has(SkyIslandStoryFlag.OldLetter)
                            ? L10n.T("风标转顺了，根环也不呜呜响了。带上那封旧信去镜水寺，折翎在等。",
                            "The beacon turns freely and the roots have stopped howling. Take the old letter to Zheling at Mirrorwater Temple.")
                            : L10n.T("风标转顺了，根环也不呜呜响了。倒挂邮亭还吊着封旧信，别落下。",
                            "The beacon turns freely and the roots have stopped howling. There's still an old letter at the Upturned Post Hut."))
                        : L10n.T("根环那头就是风标，先把跟前的人清干净再动手。倒挂邮亭那封旧信，顺路取了。",
                            "The beacon is past the root ring, so clear whoever's standing there before you touch it. Pick up the old letter at the Upturned Post Hut on your way.");
        }

        private static string ZhelingLine(SkyIslandStoryData data)
        {
            return data.ZhelingResolved
                ? (data.Has(SkyIslandStoryFlag.ZhelingReconciled)
                    ? L10n.T("路通了，这次我留下守灯。有人回来，总得有人接。",
                            "The road is open. I'll keep the lamp lit and watch for them.") +
                      (SkyIslandLetters.Collected(data, "Letter_06")
                        ? L10n.T("\n老人还替我擦着寺里的钟。等灯都亮了，我就回去喝他那杯茶。",
                            "\nThe old sweeper still polishes the temple bell for me. Once the lamps are lit, I'll join him for tea.")
                        : string.Empty) + ZhelingFrogLine(data)
                    : L10n.T("旧腰牌上刻着：『航路交给你。』",
                            "The old badge reads: 'The route is yours now.'"))
                : L10n.T("那场风灾，我不想再见第二回。带旧信和航路图来谈，或者正面打赢我。",
                            "I won't let that storm happen again. Bring the old letter and route chart, or face me in a fight.");
        }

        /// <summary>
        /// 噬风·回响：结局后钟守提一句栈道那阵风还会回来。解锁口径与引风同一个 <see cref="SkyIslandStormEchoRules.UnlockedBySave"/>，
        /// 没打过噬风的结局存档不说这句。
        /// </summary>
        private static string BellKeeperEchoLine(SkyIslandStoryData data)
        {
            if (!SkyIslandStormEchoRules.UnlockedBySave(data)) return string.Empty;
            return L10n.T("\n带着噬风的核，去双航标门烧一块晴岚风晶。它会回来找你，一趟只能引一次。",
                            "\nCarry the Windeater Core and burn a Qinglan Windcrystal at the twin-beacon gate. Then it will come looking for you, once per raid.");
        }

        /// <summary>内容批次四：眠苔说苔药与药膏怎么分工——苔药管伤、顺手止痒；药膏管痒、抹上一阵都不怕叮。</summary>
        private static string MiantaiItchLine
        {
            get
            {
                return L10n.T("\n痒也别抓。苔药治伤兼止痒，药膏能让你一阵子不怕叮。",
                            "\nDon't scratch. My moss remedy heals and stops the itch; salve keeps bites from itching for a while.");
            }
        }

        /// <summary>内容批次四：晴禾说起梯田水车边的云蚋、她的纱笠与蛙鸣池的青蛙（<see cref="SkyIslandMosquitoRules"/>）。</summary>
        private static string QingheGnatLine(SkyIslandStoryData data, bool onIsland)
        {
            if (!onIsland)
                return SkyIslandMosquitoRules.FrogsComplete(data)
                    ? L10n.T("\n蛙鸣池的青蛙养起来了，下趟去岛上还可以听听。纱笠的织法留在菜畦灶台，夜里去记得戴上。",
                        "\nThe Frogsong frogs are thriving; listen for them next trip. The veil pattern is at the island's garden stove. Wear one at night.")
                    : L10n.T("\n岛上的水车边蚋多。纱笠的织法留在菜畦灶台，带云苔纤维和星屑就能做。",
                        "\nGnats swarm by the island's waterwheel. The veil pattern is at the garden stove; bring cloudmoss fiber and stardust to make one.");
            if (SkyIslandMosquitoRules.FrogsComplete(data))
                return L10n.T("\n蛙鸣池又有蛙叫了，水车边的蚋也少了。夜里下地，我还是戴着纱笠。",
                            "\nFrogsong Pool is croaking again, and there are fewer gnats by the wheel. I still wear my veil at night.");
            if (SkyIslandMosquitoRules.FrogsReleased(data) > 0)
                return L10n.T("\n有人往蛙鸣池送蛙卵了？好啊，等青蛙长起来，蚋就少了。",
                            "\nSomeone brought frogspawn to Frogsong Pool? Good. More frogs means fewer gnats.");
            return L10n.T("\n水车边蚋多，我织了顶纱笠。拿云苔纤维和星屑来灶台，也给你织一顶。",
                            "\nGnats swarm by the wheel, so I made a veil. Bring cloudmoss fiber and stardust to the stove for yours.");
        }

        /// <summary>内容批次四：苇白是修灯的人——点亮的风晶灯够了，她就把灯芯调成引蚋的陷阱（灭蚊灯配方的门槛读同一个数）。</summary>
        private static string WeibaiZapperLine(SkyIslandStoryData data)
        {
            int lamps = SkyIslandLights.LampsLit(data);
            if (lamps <= 0) return string.Empty;
            SkyIslandRecipe zapper = SkyIslandFieldcraftRules.FindRecipe("Zapper");
            if (zapper != null && lamps < zapper.RequiresLamps)
                return L10n.T("云蚋追着灯芯的亮光和嗡声跑。再亮一盏，我就能试着调个灭蚊的灯芯。\n",
                            "Gnats follow the wick's light and hum. Light one more lamp and I can tune a wick to trap them.\n");
            return L10n.T("灭蚊灯的调子交给浮舟了。带晴岚风晶和残铜片，去他的渡口工台做。\n",
                            "Fuzhou has my gnat zapper plans. Take a Qinglan Windcrystal and brass scrap to his dock workbench.\n");
        }

        /// <summary>内容批次四：和解之后的折翎说起寺里池子的青蛙——蛙卵从这里捧回蛙鸣池。</summary>
        private static string ZhelingFrogLine(SkyIslandStoryData data)
        {
            if (SkyIslandMosquitoRules.FrogsComplete(data))
                return L10n.T("\n寺里还听得见蛙叫，蛙鸣池也有了新的繁育处。你送的蛙卵活下来了。",
                            "\nThe temple still has frogs, and Frogsong Pool has a new breeding ground. Your frogspawn survived.");
            return L10n.T("\n风灾时，蛙鸣池的青蛙躲进了寺里。夜里去池边捧一团蛙卵，送它们回家。",
                            "\nThe Frogsong frogs sheltered here during the storm. Scoop up some frogspawn at night and take it home.");
        }

        /// <summary>
        /// 浮舟说起风晶灯（<see cref="SkyIslandLights"/>）：星灯亮起之前不提（碎晶要进工坊的熔晶炉，炉子还烧不起来），
        /// 之后告诉玩家碎晶攒够五片来找他；十盏都亮了换一句。这是玩家头一回听说「岛上的灯」的地方。
        /// </summary>
        private static string FuzhouLampLine(SkyIslandStoryData data)
        {
            if (!data.Has(SkyIslandStoryFlag.StarLamp)) return string.Empty;
            if (SkyIslandLights.AllLit(data))
                return L10n.T("\n十盏灯都亮着了。夜里看得清回码头的路，我也少操点心。",
                            "\nAll ten lamps are lit. You can see the way to the dock at night, so I worry less.");
            return L10n.T("\n星灯一亮，熔晶炉就能烧了。攒五片碎晶，拿到我工台熔成整块，点一盏灯，夜风就吹不透。",
                            "\nWith the star lamp lit, the crystal furnace burns again. Fuse five shards at my bench, light a lamp, and the night wind won't get through.");
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
                var replacementCoordinator = new BossRushSaveCoordinatorEngine(new SaveSource(this, replacement), false);
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
            assetSnapshotRequired = false;
            cashSnapshotRequired = false;
            cashPaidPendingAction = null;
        }

        private sealed class SaveSource : IBossRushSaveBatchSource
        {
            private readonly SkyIslandStoryService owner;
            private readonly BossRushSlotJsonStore<SkyIslandStoryData> store;
            internal SaveSource(SkyIslandStoryService ownerValue, BossRushSlotJsonStore<SkyIslandStoryData> value)
            { owner = ownerValue; store = value; }
            public string LogPrefix { get { return "[SkyIsland] "; } }
            public bool HasPendingWrite { get { return store.HasPendingWrite; } }
            public bool IsStoreFaulted { get { return store.IsStoreFaulted; } }
            public bool HasSnapshotObligation { get { return owner.assetSnapshotRequired || owner.cashSnapshotRequired; } }
            public string LastError { get { return store.LastError; } }
            public bool CollectSnapshot(out string error)
            {
                error = null;
                if (!owner.CollectPendingCash()) { error = "cash_snapshot_unavailable"; return false; }
                if (!owner.assetSnapshotRequired) return true;
                try
                {
                    // LevelManager.SaveMainCharacter 是官方程序集的 internal 成员，生产编译不可直接绑定；
                    // 与 PetNest 的资产屏障一致，直接保存主角物品快照。
                    CharacterMainControl.Main.CharacterItem.Save("MainCharacterItemData");
                    SavesSystem.Save<float>("MainCharacterHealth", CharacterMainControl.Main.Health.CurrentHealth);
                    PlayerStorage.Inventory.Save("PlayerStorage");
                    PlayerStorageBuffer.SaveBuffer();
                    return true;
                }
                catch (Exception e) { error = "asset_collect_failed:" + e.GetType().Name; return false; }
            }
            public bool FlushPending() { return store.FlushPending(); }
            public void OnPhysicalSaveSucceeded() { owner.assetSnapshotRequired = false; owner.cashSnapshotRequired = false; }
        }
    }
}
