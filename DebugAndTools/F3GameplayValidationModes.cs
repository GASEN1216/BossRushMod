// ============================================================================
// F3GameplayValidationModes.cs - 完整验收的各模式生命周期用例
// ============================================================================
// 模块说明：
//   Mode D/E/F/G/H/丧尸的入场—存活敌人—清理链验收。从主 runner 拆出来，
//   让主 runner 守住 1200 行预算（LargeFileBudgetGuard），内容逐字保持原语义。
//
//   每个用例都由 RunIsolatedCase 包壳调用（见 F3GameplayValidationStages.cs），
//   所以这里不需要自己做异常兜底，但仍必须在退出前 ValidationSafeCleanup。
// ============================================================================

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BossRush
{
    internal sealed partial class F3GameplayValidationRunner
    {
        private IEnumerator RunModeD()
        {
            Stopwatch sw = Stopwatch.StartNew();
            bool started = false;
            try
            {
                string entryReason;
                if (!PrepareModeDValidationEntry(out entryReason))
                {
                    Record("MODE_D_LIFECYCLE", "FAIL", sw.ElapsedMilliseconds, string.Empty, entryReason);
                    yield break;
                }
                started = _host.TryStartModeD() && _host.IsModeDActive && _host.ModeDStartNextWave();
                yield return WaitSeconds(8f);
                string hostileDetails;
                int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
                string trackedDetails;
                int playable = _host.ValidationCountPlayableModeDEnemies(out trackedDetails);
                bool passed = started && _host.IsModeDActive && playable > 0 && enemies > 0;
                Record("MODE_D_LIFECYCLE", passed ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                    "wave=" + _host.ModeDWaveIndex + ",enemies=" + enemies
                        + ",playable_tracked=" + playable + ",hostiles=" + hostileDetails
                        + ",tracked_details=" + trackedDetails,
                    !started ? "start_rejected" : (passed ? string.Empty
                        : "mode_d_has_no_active_alive_hostile_tracked_enemy"));
            }
            finally
            {
                try { _host.ValidationSafeCleanup(); }
                finally { RestoreModeDValidationEntry(); }
            }
        }

        private F3ModeDLoadoutLease _modeDValidationLoadout;
        private bool PrepareModeDValidationEntry(out string reason)
        {
            reason = "dev_build_required";
#if BOSSRUSH_DEV
            if (_modeDValidationLoadout != null && !_modeDValidationLoadout.TryRestore())
            { reason = "previous_mode_d_loadout_restore_pending"; return false; }
            return F3ModeDLoadoutLease.TryPrepare(IsDedicatedCurrentSlot(), _host.IsPlayerNaked,
                out _modeDValidationLoadout, out reason);
#else
            return false;
#endif
        }
        private void RestoreModeDValidationEntry()
        {
#if BOSSRUSH_DEV
            bool restored = _modeDValidationLoadout == null || _modeDValidationLoadout.TryRestore();
            if (restored) _modeDValidationLoadout = null;
            else _fatalAbort = true;
            Record("MODE_ENTRY_LOADOUT_RESTORE", restored ? "PASS" : "FAIL", 0L,
                "original_objects=true", restored ? null : "original_loadout_retained_in_storage");
#endif
        }

        private bool PrepareModeEFValidationEntry(bool modeF, out string reason)
        {
            if (!PrepareModeDValidationEntry(out reason)) return false;
#if BOSSRUSH_DEV
            if (!_modeDValidationLoadout.TryGiveLoan(BossRushMapSelectionHelper.GetBossRushTicketTypeId(), out reason)
                || !_modeDValidationLoadout.TryGiveLoan(modeF ? BloodhuntTransponderConfig.TYPE_ID : FactionFlagConfig.SCAV_FLAG_TYPE_ID, out reason))
            { RestoreModeDValidationEntry(); return false; }
            WriteRaw("MODE_ENTRY_FEE_LOAN | mode=" + (modeF ? "F" : "E") + " | asset=" + BossRushCoop.LocalAssetId
                + " | ticket=1 | personal_fee=1 | original_objects_in_escrow=true");
            return true;
#else
            return false;
#endif
        }

        private IEnumerator RunModeE()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                string reason;
                if (!PrepareModeEFValidationEntry(false, out reason))
                { Record("MODE_E_LIFECYCLE", "FAIL", sw.ElapsedMilliseconds, string.Empty, reason); yield break; }
                bool started = _host.TryStartModeE();
                yield return WaitSeconds(8f);
                string hostileDetails;
                int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
                Record("MODE_E_LIFECYCLE", started && _host.IsModeEActive && enemies > 0 ? "PASS" : "FAIL",
                    sw.ElapsedMilliseconds, "enemies=" + enemies + ",hostiles=" + hostileDetails,
                    started ? string.Empty : "start_rejected");
            }
            finally { try { _host.ValidationSafeCleanup(); } finally { RestoreModeDValidationEntry(); } }
        }

        private IEnumerator RunModeF()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                string reason;
                if (!PrepareModeEFValidationEntry(true, out reason))
                { Record("MODE_F_LIFECYCLE", "FAIL", sw.ElapsedMilliseconds, string.Empty, reason); yield break; }
                bool started = _host.TryStartModeF();
                yield return WaitSeconds(8f);
                string bloodfireMetrics;
                bool bloodfire = _host.DebugValidateModeFBloodfire(out bloodfireMetrics);
                string hostileDetails;
                int enemies = _host.ValidationCountHostileCharacters(out hostileDetails);
                Record("MODE_F_LIFECYCLE", started && _host.IsModeFActive && enemies > 0 ? "PASS" : "FAIL",
                    sw.ElapsedMilliseconds, "enemies=" + enemies + ",hostiles=" + hostileDetails,
                    started ? string.Empty : "start_rejected");
                Record("MODE_F_BLOODFIRE", bloodfire ? "PASS" : "FAIL", 0L, bloodfireMetrics,
                    bloodfire ? string.Empty : "speed_modifier_validation_failed");
            }
            finally { try { _host.ValidationSafeCleanup(); } finally { RestoreModeDValidationEntry(); } }
        }

        private IEnumerator RunModeG() { return RunModeGNineWaves(); }

        private IEnumerator RunModeH(bool fullSeason)
        {
            string id = fullSeason ? "MODE_H_PLAYER_REENTRY" : "MODE_H_PLAYER_ENTRY";
            Stopwatch sw = Stopwatch.StartNew();
            ModeHSupportedMap map;
            if (!ModeHEntry.ResolveTargetMap(SceneManager.GetActiveScene().name, out map) || map == null)
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, "map_unsupported");
                yield break;
            }
            string loanReason;
            if (!PrepareModeDValidationEntry(out loanReason))
            { Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, loanReason); yield break; }
            try
            {
#if BOSSRUSH_DEV
                if (!_modeDValidationLoadout.TryGiveLoan(BossRushMapSelectionHelper.GetBossRushTicketTypeId(), out loanReason))
                { Record(id, "FAIL", sw.ElapsedMilliseconds, string.Empty, loanReason); yield break; }
#endif
            // 走与地图条目相同的官方付款和 OnCostPaid 收据，不能只伪造入场意图。
            BossRushMapSelectionHelper.MarkEntryFlowFromMapSelectionUi(BossRushPendingEntryKind.ModeH);
            BossRushMapSelectionHelper.FreezeModeHEntryIntent(map.SceneName, map.SceneId);
            Duckov.Economy.Cost entryCost = BossRushMapSelectionHelper.CreateBossRushCost();
            bool paid = false;
#if BOSSRUSH_DEV
            paid = _modeDValidationLoadout.TryPayFee(BossRushMapSelectionHelper.GetBossRushTicketTypeId(), delegate
            {
                BossRushMapSelectionHelper.CaptureModeHPayment(entryCost);
                return Duckov.Economy.EconomyManager.Pay(entryCost);
            }, out loanReason);
#endif
            if (!paid || !BossRushMapSelectionHelper.HasPendingPrepaidTicket())
            {
                Record(id, "FAIL", sw.ElapsedMilliseconds, "entry_paid=" + paid,
                    paid ? "entry_payment_receipt_missing" : (loanReason ?? "entry_payment_failed"));
                BossRushMapSelectionHelper.ClearPendingEntryFlowStateAfterUnexpectedScene(_host);
                yield break;
            }
            _host.ModeHRuntime.OnSceneLoaded(new SceneRuntimeContext(SceneManager.GetActiveScene(), LoadSceneMode.Single));
            float deadline = Time.realtimeSinceStartup + CaseTimeoutSeconds;
            bool sawDiagnostics = _host.ModeHRuntime.IsCertificationDiagnosticRunning;
            while (Time.realtimeSinceStartup < deadline && !ShouldAbort())
            {
                sawDiagnostics |= _host.ModeHRuntime.IsCertificationDiagnosticRunning;
                ModeHRunState state = _host.ModeHRuntime.RunState;
                if (state != null && state.Lifecycle == ModeHLifecycle.Drafting) break;
                if ((!_host.ModeHRuntime.HasActiveRun && !_host.ModeHRuntime.IsSceneEntryPending) || SceneLoader.IsSceneLoading
                    || !string.Equals(SceneManager.GetActiveScene().name, map.SceneName, System.StringComparison.Ordinal)) break;
                yield return null;
            }
            ModeHRunState finalState = _host.ModeHRuntime.RunState;
            bool drafting = finalState != null && finalState.Lifecycle == ModeHLifecycle.Drafting;
            Record(id, drafting && !sawDiagnostics ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "drafting=" + drafting + ",dynamic_diagnostics=" + sawDiagnostics,
                !drafting ? "player_entry_timeout_or_abort" : (sawDiagnostics ? "player_entry_started_diagnostics" : null));
            if (fullSeason)
            {
                // 群战（2026-09-29）没有整备与 ERROR 互换：两条单挑版用例记 SKIP
                bool group = ModeHRuntimeModule.GroupModeEnabled;
                if (drafting && !group) yield return RunModeHStarterKits(map);
                else Record("MODE_H_STARTER_KITS", "SKIP", 0L, string.Empty, group ? "group_battle_mode" : "player_drafting_not_ready");
                if (drafting && !group) yield return RunModeHErrorSwap(map);
                else Record("MODE_H_ERROR_SWAP", "SKIP", 0L, string.Empty, group ? "group_battle_mode" : "player_drafting_not_ready");
                if (drafting) yield return RunModeHFullSeason();
                else Record("MODE_H_FULL_SEASON", "SKIP", 0L, string.Empty, "player_drafting_not_ready");
            }
            bool archived = fullSeason ? !_host.ModeHRuntime.HasActiveRun
                : drafting && _host.ModeHRuntime.DebugFinishValidationSeason();
            bool intentCleared = !BossRushMapSelectionHelper.HasPendingModeHEntryIntent();
            Record(id + "_CLEANUP", drafting && !sawDiagnostics && archived && intentCleared ? "PASS" : "FAIL", sw.ElapsedMilliseconds,
                "drafting=" + drafting + ",dynamic_diagnostics=" + sawDiagnostics
                    + ",intent_cleared=" + intentCleared
                    + ",archived=" + archived + ",exit_reason=" + _host.ModeHRuntime.LastExitReasonId,
                !intentCleared ? "entry_intent_not_consumed"
                    : (!archived ? "season_not_archived:" + ModeHSaveFlushCoordinator.LastError
                    : (sawDiagnostics ? "player_entry_started_diagnostics" : string.Empty)));
            _host.ValidationSafeCleanup();
            yield return WaitSeconds(0.5f);
            }
            finally { try { _host.ValidationSafeCleanup(); } finally { RestoreModeDValidationEntry(); } }

        }

        private bool PrepareZombieValidationEntry(out string reason)
        {
            if (!PrepareModeDValidationEntry(out reason)) return false;
#if BOSSRUSH_DEV
            if (!_modeDValidationLoadout.TryGiveLoan(ZombieTideInvitationConfig.TYPE_ID, out reason))
            { RestoreModeDValidationEntry(); return false; }
            return true;
#else
            return false;
#endif
        }

        private bool StartZombieValidationEntry(out string reason)
        {
            reason = "dev_build_required";
#if BOSSRUSH_DEV
            string startReason = null, paymentReason;
            bool started = _modeDValidationLoadout.TryPayFee(ZombieTideInvitationConfig.TYPE_ID,
                delegate { return _host.ValidationStartZombie(out startReason); }, out paymentReason);
            reason = paymentReason ?? startReason;
            return started;
#else
            return false;
#endif
        }

        private IEnumerator RunZombie()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                string reason;
                bool started = PrepareZombieValidationEntry(out reason) && StartZombieValidationEntry(out reason);
                yield return WaitSeconds(6f);
                Record("MODE_ZOMBIE_LIFECYCLE", started && _host.IsZombieModeActive ? "PASS" : "FAIL",
                    sw.ElapsedMilliseconds, "active=" + _host.IsZombieModeActive + ",invitation_paid=" + started, reason);
            }
            finally { try { _host.ValidationSafeCleanup(); } finally { RestoreModeDValidationEntry(); } }
        }

    }

    // 专用档用独立官方 Inventory 保管原物；竞技场没有基地仓库，失败/换体也不能丢原物。
    internal sealed class F3ModeDLoadoutLease
    {
        private sealed class Held
        {
            internal ItemStatsSystem.Item Item;
            internal ItemStatsSystem.Items.Slot Slot;
            internal ItemStatsSystem.Inventory Inventory;
            internal int Position, StoragePosition;
            internal string SlotKey;
        }
        private readonly List<Held> items = new List<Held>();
        private readonly List<Held> feeReturns = new List<Held>();
        private readonly HashSet<ItemStatsSystem.Item> loans = new HashSet<ItemStatsSystem.Item>();
        private readonly ItemStatsSystem.Item root;
        private readonly ItemStatsSystem.Inventory storage;
        private readonly GameObject escrowObject;
        private readonly string asset;
        private readonly int slot;
        private bool prepared;
        private bool released;
        private F3ModeDLoadoutLease(ItemStatsSystem.Item root, ItemStatsSystem.Inventory storage, GameObject escrowObject, string asset, int slot)
        {
            this.root = root; this.storage = storage; this.escrowObject = escrowObject;
            this.asset = asset; this.slot = slot;
        }

        internal static bool TryPrepare(bool dedicated, System.Func<bool> isNaked, out F3ModeDLoadoutLease lease, out string reason)
        {
            lease = null; reason = null;
            if (!ModBehaviour.DevModeEnabled || !dedicated) { reason = "dedicated_test_slot_required"; return false; }
            CharacterMainControl player = CharacterMainControl.Main;
            if (player == null || player.CharacterItem == null)
            { reason = "mode_d_original_player_unavailable"; return false; }
            ItemStatsSystem.Item originalRoot = player.CharacterItem;
            string originalAsset = BossRushCoop.LocalAssetId;
            int originalSlot = Saves.SavesSystem.CurrentSlot;
            GameObject escrow = null;
            try
            {
                List<ItemStatsSystem.Item> originals = Collect(originalRoot);
                escrow = new GameObject("BossRushValidation_EntryEscrow");
                escrow.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(escrow);
                ItemStatsSystem.Inventory inventory = escrow.AddComponent<ItemStatsSystem.Inventory>();
                inventory.SetCapacity(System.Math.Max(1, originals.Count));
                lease = new F3ModeDLoadoutLease(originalRoot, inventory, escrow, originalAsset, originalSlot);
                foreach (ItemStatsSystem.Item item in originals)
                {
                    ItemStatsSystem.Inventory originalInventory = item.InInventory;
                    lease.items.Add(new Held { Item = item, Slot = item.PluggedIntoSlot, Inventory = originalInventory,
                        Position = originalInventory != null ? originalInventory.Content.IndexOf(item) : -1,
                        SlotKey = item.PluggedIntoSlot != null ? item.PluggedIntoSlot.Key : null });
                }
                int start = 0;
                foreach (Held held in lease.items)
                {
                    held.StoragePosition = inventory.GetFirstEmptyPosition(start);
                    if (held.StoragePosition < 0) { reason = "mode_d_original_storage_full"; return false; }
                    start = held.StoragePosition + 1;
                }
                foreach (Held held in lease.items)
                {
                    if (lease.storage.GetItemAt(held.StoragePosition) != null) return false;
                    held.Item.Detach();
                    bool added = lease.storage.AddAt(held.Item, held.StoragePosition);
                    if (!added || held.Item.InInventory != lease.storage) return false;
                }
                lease.prepared = isNaked != null && isNaked() && lease.IsEntryOwnerCurrent();
                if (!lease.prepared) reason = lease.IsEntryOwnerCurrent() ? "mode_d_naked_prepare_rejected" : "mode_entry_prepare_owner_changed";
                return lease.prepared;
            }
            catch (System.Exception e) { reason = "mode_d_naked_prepare_failed:" + e.GetType().Name; return false; }
            finally
            {
                if (lease != null && !lease.prepared) lease.TryRestore();
                else if (lease == null && escrow != null) UnityEngine.Object.Destroy(escrow);
            }
        }

        private static List<ItemStatsSystem.Item> Collect(ItemStatsSystem.Item source)
        {
            var result = new List<ItemStatsSystem.Item>();
            if (source == null) return result;
            if (source.Inventory != null)
                foreach (ItemStatsSystem.Item item in source.Inventory.Content) if (item != null && !result.Contains(item)) result.Add(item);
            if (source.Slots != null)
                foreach (var position in source.Slots) if (position != null && position.Content != null && !result.Contains(position.Content)) result.Add(position.Content);
            return result;
        }

        private bool IsEntryOwnerCurrent()
        {
            return slot == Saves.SavesSystem.CurrentSlot && asset == BossRushCoop.LocalAssetId
                && CharacterMainControl.Main != null && CharacterMainControl.Main.CharacterItem == root;
        }

        internal bool TryGiveLoan(int typeId, out string reason)
        {
            reason = null;
            if (!prepared || !IsEntryOwnerCurrent())
            { reason = "entry_loan_owner_changed"; return false; }
            ItemStatsSystem.Item item = null;
            try
            {
                item = ItemStatsSystem.ItemAssetsCollection.InstantiateSync(typeId);
                if (item == null || !ItemUtilities.SendToPlayerCharacterInventory(item, false)
                    || !Collect(root).Contains(item))
                { reason = "entry_loan_not_delivered:" + typeId; return false; }
                loans.Add(item);
                return true;
            }
            catch (System.Exception e) { reason = "entry_loan_failed:" + typeId + ":" + e.GetType().Name; return false; }
            finally
            {
                if (reason != null && item != null) { item.Detach(); item.DestroyTree(); }
            }
        }

        internal bool TryPayFee(int typeId, System.Func<bool> payment, out string reason)
        {
            reason = null;
            if (!prepared || !IsEntryOwnerCurrent()
                || !TryRestoreFeeObjects()) { reason = "entry_fee_owner_or_return_pending"; return false; }
            bool paid = false;
            try
            {
                // 官方 Pay 会从仓库/背包按耐久排序消费，同步付款期间只让新 loan 可见。
                foreach (ItemStatsSystem.Item item in ItemUtilities.FindAllBelongsToPlayer(candidate => candidate != null && candidate.TypeID == typeId))
                {
                    if (loans.Contains(item)) continue;
                    ItemStatsSystem.Inventory inventory = item.InInventory;
                    feeReturns.Add(new Held { Item = item, Slot = item.PluggedIntoSlot, Inventory = inventory,
                        Position = inventory != null ? inventory.Content.IndexOf(item) : -1 });
                    storage.SetCapacity(System.Math.Max(storage.Capacity, items.Count + feeReturns.Count));
                    item.Detach();
                }
                paid = payment != null && payment();
            }
            catch (System.Exception e) { reason = "entry_fee_payment_failed:" + e.GetType().Name; }
            finally
            {
                if (!TryRestoreFeeObjects()) { paid = false; reason = "original_fee_return_pending"; }
            }
            return paid;
        }

        private bool TryRestoreFeeObjects()
        {
            if (slot != Saves.SavesSystem.CurrentSlot || asset != BossRushCoop.LocalAssetId) return false;
            for (int i = feeReturns.Count - 1; i >= 0; i--)
            {
                Held held = feeReturns[i];
                if (held.Slot != null && held.Slot.Content == held.Item
                    || held.Slot == null && held.Inventory != null && held.Inventory.GetItemAt(held.Position) == held.Item)
                { feeReturns.RemoveAt(i); continue; }
                bool restored = false;
                try
                {
                    if (held.Slot != null ? held.Slot.Content == null
                        : held.Inventory != null && held.Position >= 0 && held.Inventory.GetItemAt(held.Position) == null)
                    {
                        held.Item.Detach();
                        ItemStatsSystem.Item displaced;
                        restored = held.Slot != null ? held.Slot.Plug(held.Item, out displaced)
                            : held.Inventory.AddAt(held.Item, held.Position);
                    }
                }
                catch (System.Exception e) { ModBehaviour.DevLog("[Validation] 原费用对象归还待重试: " + e.Message); }
                if (restored) feeReturns.RemoveAt(i);
                else if (held.Item.InInventory == null && held.Item.PluggedIntoSlot == null)
                {
                    int free = storage.GetFirstEmptyPosition(0);
                    if (free >= 0 && storage.GetItemAt(free) == null) storage.AddAt(held.Item, free);
                }
            }
            return feeReturns.Count == 0;
        }

        internal bool TryRestore()
        {
            if (released) return true;
            if (storage == null || slot != Saves.SavesSystem.CurrentSlot || asset != BossRushCoop.LocalAssetId
                || CharacterMainControl.Main == null || CharacterMainControl.Main.CharacterItem == null) return false;
            ItemStatsSystem.Item currentRoot = CharacterMainControl.Main.CharacterItem;
            if (!TryRestoreFeeObjects()) return false;
            try
            {
                var original = new HashSet<ItemStatsSystem.Item>();
                foreach (Held held in items) original.Add(held.Item);
                // 只清本次成功发装后新增的物品；预备失败不扫描原包，更不销毁未转出的原物。
                if (prepared)
                    foreach (ItemStatsSystem.Item item in Collect(currentRoot))
                        if (!original.Contains(item)) { item.Detach(); item.DestroyTree(); }
                bool restored = true;
                foreach (Held held in items)
                {
                    ItemStatsSystem.Items.Slot targetSlot = currentRoot == root ? held.Slot
                        : (held.SlotKey != null && currentRoot.Slots != null ? currentRoot.Slots.GetSlot(held.SlotKey) : null);
                    ItemStatsSystem.Inventory targetInventory = currentRoot == root ? held.Inventory : currentRoot.Inventory;
                    if (held.SlotKey != null && targetSlot == null) { restored = false; continue; }
                    if (targetSlot != null && targetSlot.Content == held.Item) continue;
                    if (held.SlotKey == null && targetInventory != null && held.Item.InInventory == targetInventory) continue;
                    if (targetSlot != null && targetSlot.Content != null
                        || targetSlot == null && (targetInventory == null || targetInventory.GetItemAt(held.Position) != null))
                    { restored = false; continue; }
                    bool returned = false;
                    try
                    {
                        held.Item.Detach();
                        ItemStatsSystem.Item displaced;
                        returned = targetSlot != null ? targetSlot.Plug(held.Item, out displaced)
                            : targetInventory.AddAt(held.Item, held.Position);
                    }
                    finally
                    {
                        if (!returned && held.Item.InInventory == null && held.Item.PluggedIntoSlot == null)
                        {
                            int free = storage.GetFirstEmptyPosition(0);
                            if (free >= 0 && storage.GetItemAt(free) == null) storage.AddAt(held.Item, free);
                        }
                    }
                    restored &= returned;
                }
                if (restored)
                {
                    foreach (ItemStatsSystem.Item remaining in storage.Content) if (remaining != null) return false;
                    prepared = false;
                    released = true;
                    UnityEngine.Object.Destroy(escrowObject);
                }
                return restored;
            }
            catch (System.Exception e)
            { ModBehaviour.DevLog("[Validation] Mode D 原物恢复待重试: " + e.Message); return false; }
        }
    }
}
