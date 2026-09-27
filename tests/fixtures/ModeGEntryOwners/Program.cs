using System;
using System.Linq;
using System.Threading.Tasks;
using BossRush;
using ItemStatsSystem;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message + "\nEvents: " + string.Join(", ", Harness.Events));
    }
    private static void Ordered(params string[] events)
    {
        int position = -1;
        foreach (string value in events)
        {
            position = Harness.Events.FindIndex(position + 1, x => x == value);
            Check(position >= 0, "Missing or reordered event: " + value);
        }
    }
    private static ModBehaviour Fresh() { Harness.Reset(); return new ModBehaviour(); }
    private static void NoRefund() { Check(Harness.Refunds.Count == 0, "Unexpected refund"); }
    private static void OneRefundEach()
    {
        Check(Harness.RefundCount(9001) == 1, "Ticket refund ownership duplicated/lost");
        Check(Harness.RefundCount(FateEchoRelicConfig.TYPE_ID) == 1, "Relic refund ownership duplicated/lost");
    }

    private static void LazyAndTwoRuns()
    {
        ModBehaviour host = Fresh();
        host.Tick(1); host.Shutdown();
        Check(!host.HasEntry && host.Core == null && !host.Active, "Idle tick/cleanup created entry service");
        ModeGRuntimeModule shell = new ModeGRuntimeModule();
        ModeGEntryPreview first = host.GetOrCreateModeGEntryPreview();
        Check(first != null && ReferenceEquals(first, host.GetOrCreateModeGEntryPreview()), "Preview was not frozen");
        host.SetModeGSelectedContractId(first.contractCandidateIds[1]);
        Check(host.TryStartModeG(), "First run did not start through public host bridge");
        ModeGRuntimeModule core1 = host.Core;
        Check(!ReferenceEquals(shell, core1), "Registered shell reused as run core");
        Check(ReferenceEquals(ModeGRunContext.CurrentModule, core1), "Entry/context owners differ");
        Check(ReferenceEquals(ModeGRunContext.Current, core1.State), "State/context owners differ");
        Check(ReferenceEquals(ModeGHUD.LastCore, core1), "HUD points to another run");
        Check(core1.State.fateContractId == 2, "Contract host bridge disconnected");
        Ordered("consume:500057", "consume:9001", "new-core", "initialize", "arm-refund", "start", "contract:2", "hud-new", "banner");
        Check(!host.TryStartModeG() && ReferenceEquals(host.Core, core1), "Duplicate start replaced core");
        Harness.Events.Clear();
        Input.Pressed = true;
        shell.OnUpdate(1, 1);
        host.Tick(1);
        Check(shell.TickCalls == 0 && core1.TickCalls == 1, "Shell tick drove live run");
        Ordered("key", "abandon-open", "core-tick", "hud-tick");
        Harness.Events.Clear();
        core1.State.exitReason = ModeGExitReason.ManualExit;
        core1.State.TryAdvanceLifecycle(ModeGLifecyclePhase.Exiting);
        core1.State.TryAdvanceLifecycle(ModeGLifecyclePhase.None);
        host.Tick(1);
        Ordered("core-tick", "hud-tick", "confirm-close", "abandon-close", "hud-dispose", "core-dispose");
        Check(!host.Active && host.Core == null && core1.Disposed && core1.DisposeCalls == 1, "Terminal shutdown ownership wrong");
        Check(ModeGRunContext.Current == null && ModeGRunContext.CurrentModule == null, "Terminal context leaked");
        host.Shutdown();
        Check(core1.DisposeCalls == 1, "Repeated shutdown disposed old core again");
        ModeGEntryPreview second = host.GetOrCreateModeGEntryPreview();
        Check(!ReferenceEquals(first, second) && first.sessionCounter < second.sessionCounter, "Successful entry did not consume preview");
        Check(host.TryStartModeG(), "Second run failed");
        Check(!ReferenceEquals(core1, host.Core) && !host.Core.Disposed, "Disposed core reused by second run");
        Check(host.Core.State.fateContractId == 1, "Previous contract selection leaked into second run");
        ModeGRunContext.Unbind(core1.State);
        Check(ReferenceEquals(ModeGRunContext.CurrentModule, host.Core), "Old run unbind erased new run");
        host.Shutdown();
        NoRefund();
    }

    private static void EntryGatesAndRefunds()
    {
        Action<ModBehaviour>[] blockers = {
            h => h.IsActive = true, h => h.modeDActive = true, h => h.modeEActive = true,
            h => h.modeFActive = true, h => h.IsZombieModeActive = true,
            h => h.Flag = new Item(), h => h.Transponder = new Item(), h => h.Relic = null,
            h => h.Ticket = null, h => h.Pool.Clear(), h => ModeGAvailability.Ready = false,
            h => ModeGPresentationAssetCache.Ready = false, h => ModeHRuntimeGates.Allowed = false,
            h => ModeGNemesisPersistence.IsStoreFaulted = true,
            h => ModeGProfilePersistence.IsStoreFaulted = true,
            h => ModeGPersistenceFlushCoordinator.IsFaulted = true,
            h => ModeGLateCleanupSink.HasPendingLeases = true,
        };
        foreach (Action<ModBehaviour> block in blockers)
        {
            ModBehaviour host = Fresh();
            host.GetOrCreateModeGEntryPreview(); // 先绑定，再改宿主；查询必须读取即时值。
            block(host);
            Check(!host.TryStartModeG(), "Entry blocker lost");
            Check(!Harness.Events.Any(x => x.StartsWith("consume:")), "Blocked entry consumed inventory");
            Check(host.Core == null, "Blocked entry created core");
            NoRefund();
        }
        ModBehaviour failure = Fresh();
        failure.ConsumeTicketWorks = false;
        Check(!failure.TryStartModeG(), "Failed ticket consumption started run");
        Check(Harness.RefundCount(FateEchoRelicConfig.TYPE_ID) == 1 && Harness.RefundCount(9001) == 0, "Partial consumption refund wrong");
        Check(failure.Core == null, "Partial consumption created core");
        failure = Fresh(); Harness.InitOk = false;
        Check(!failure.TryStartModeG(), "Initialize failure started run");
        OneRefundEach();
        Check(!Harness.Events.Contains("arm-refund") && ModeGRunContext.Current == null, "Initialize failure transferred refund ownership");
        failure = Fresh(); Harness.StartOk = false;
        Check(!failure.TryStartModeG(), "Start failure reported success");
        OneRefundEach();
        Ordered("arm-refund", "start", "end:TechnicalIntegrityLoss", "refund:9001", "refund:500057", "core-dispose");
        Check(failure.Core == null && ModeGRunContext.Current == null, "Start failure retained owner");
        failure = Fresh(); Harness.HudThrow = true;
        Check(!failure.TryStartModeG(), "HUD failure reported success");
        OneRefundEach();
        Check(failure.Core == null && !failure.Active && ModeGRunContext.Current == null, "HUD failure retained owner");
        Ordered("hud-new", "end:TechnicalIntegrityLoss", "confirm-close", "abandon-close", "core-dispose");
    }

    private static void PrepaidAndArena()
    {
        ModBehaviour host = Fresh(); BossRushMapSelectionHelper.Prepaid = true; host.Ticket = null;
        Harness.StartOk = false;
        Check(!host.TryStartModeG(), "Prepaid startup failure not surfaced");
        OneRefundEach();
        Check(!BossRushMapSelectionHelper.Prepaid, "Prepaid ownership not transferred");
        Check(!Harness.Events.Contains("consume:9001"), "Prepaid ticket consumed twice");
        Ordered("consume:500057", "clear-prepaid", "initialize", "arm-refund");
        Check(!host.TryRefundModeGPendingPrepaidTicket(), "Already transferred prepaid ticket refunded again");
        OneRefundEach();
        host = Fresh(); BossRushMapSelectionHelper.Prepaid = true;
        host.bossRushArenaActive = host.bossRushArenaPlanned = host.spawnersDisabled = true;
        Check(host.TryRefundModeGPendingPrepaidTicket(), "Cancel did not refund prepaid");
        Check(!host.bossRushArenaActive && !host.bossRushArenaPlanned && !host.spawnersDisabled, "Staged arena ownership leaked");
        Check(!host.TryRefundModeGPendingPrepaidTicket() && Harness.RefundCount(9001) == 1, "Repeated cancellation double refunded");
        host = Fresh(); host.GetOrCreateModeGEntryPreview(); host.modeEActive = true;
        host.bossRushArenaActive = host.bossRushArenaPlanned = host.spawnersDisabled = true;
        host.RollbackModeGStagedArenaEntry();
        Check(host.bossRushArenaActive && host.bossRushArenaPlanned && host.spawnersDisabled, "Rollback erased another mode's live arena");
        host = Fresh(); ModeGEntryPreview preview = host.GetOrCreateModeGEntryPreview();
        Harness.Events.Clear();
        Check(host.PrepareModeGArenaRuntime(preview), "Arena prepare bridge failed");
        Ordered("set-points:Arena", "item-cache", "entry-point", "ammo-refill");
        Check(host.bossRushArenaActive, "Arena active write did not reach host");
        Check(host.CommitModeGArenaEntry(preview), "Arena commit bridge failed");
        Ordered("precache", "disable", "clear-enemies");
        host.spawnersDisabled = false; host.DisableWorks = false; Harness.Events.Clear();
        Check(!host.CommitModeGArenaEntry(preview) && !Harness.Events.Contains("clear-enemies"), "Commit ignored spawner failure");
        host.Points = new Vector3[0];
        Check(!host.PrepareModeGArenaRuntime(preview), "Prepare read stale spawn point array");
        UnityEngine.SceneManagement.SceneManager.Current = "Other";
        Check(!host.IsModeGEntryPreviewValidForCurrentScene(preview), "Preview scene validation disconnected");
        Check(!host.CommitModeGArenaEntry(preview), "Stale scene committed arena");
        host = Fresh(); bool owned;
        Check(host.DebugStart(host.GetOrCreateModeGEntryPreview(), out owned) && owned, "F3 private start bridge failed");
        Check(!Harness.Events.Any(x => x.StartsWith("consume:")), "F3 bridge consumed entry items");
        host.Shutdown();
    }

    private static void LiveQueriesAndPresentation()
    {
        ModBehaviour host = Fresh(); host.GetOrCreateModeGEntryPreview();
        host.Pool = new System.Collections.Generic.List<EnemyPresetInfo> {
            new EnemyPresetInfo { name = "B" }, new EnemyPresetInfo { name = "A" },
            new EnemyPresetInfo { name = "Dragon" }, new EnemyPresetInfo { name = "King" },
            new EnemyPresetInfo { name = "Witch" }, new EnemyPresetInfo { name = "OtherManaged" }
        };
        ModeGBossSnapshot snapshot = host.CreateModeGBossSnapshot();
        Check(snapshot.officialKeys.SequenceEqual(new[] { "A", "B" }), "Snapshot did not read new filtered pool or classify managed presets");
        Check(snapshot.infoByKey.ContainsKey(ModeGEncounterVariation.ManagedDragonDescendantKey), "Dragon alias not bound");
        Check(snapshot.infoByKey.ContainsKey(ModeGEncounterVariation.ManagedDragonKingKey), "King alias not bound");
        Check(snapshot.infoByKey.ContainsKey(ModeGEncounterVariation.ManagedPhantomWitchKey), "Witch alias not bound");
        Check(host.FindModeGOfficialPresetByKey("B") == host.Pool[0], "Preset lookup bridge returned another object");
        Check(host.GetModeGOfficialBossPoolKeys().Contains("A"), "Official pool bridge disconnected");
        Check(host.GetModeGRewardCandidates().Single().TypeId == 77, "Reward candidate binding lost");
        Check(host.GetModeGSpawnPositions(0, 1, ModeGPlanVariant.Split, ModeGNemesisTemperament.None, false).Length == 1, "Spawn position bridge disconnected");
        Harness.Events.Clear(); host.ShowModeGWaveBanner(0, null, ModeGCounterAxis.Distance, ModeGNemesisTemperament.None);
        Check(Harness.Events.SequenceEqual(new[] { "banner" }), "Wave banner binding lost");
        host = Fresh(); Check(host.TryStartModeG(), "Hotkey run failed");
        host.config.modeGAbandonHotkey = 0; Input.Pressed = true; Harness.Events.Clear(); host.Tick(1);
        Check(!Harness.Events.Contains("key"), "Hotkey captured stale config value");
        host.config.modeGAbandonHotkey = (int)KeyCode.F4; BossRushUI.Paused = true; Harness.Events.Clear(); host.Tick(1);
        Check(!Harness.Events.Contains("key"), "Pause gate ignored");
        host.Shutdown();
    }

    private static EnemySpawnCoreResult Spawned(CharacterMainControl character, ManagedBossRuntimeHandle handle = null)
    { return new EnemySpawnCoreResult { success = true, context = new EnemySpawnContext { character = character, managedBossHandle = handle } }; }

    private static async Task OfficialAsync()
    {
        ModBehaviour host = Fresh(); Check(host.TryStartModeG(), "Official async run failed");
        ModeGRunState oldState = host.Core.State;
        Task<ManagedBossPrepareResult> task = host.SpawnModeGOfficialBossAsync(host.Pool[0], new Vector3(), 1, null);
        Check(!task.IsCompleted && host.CoreCalls == 1 && host.LastActiveCheck, "Official factory was not awaited");
        CharacterRandomPreset staging = host.LastDirectPreset;
        Check(staging != null && staging != host.cachedCharacterPresets["Official"], "Official staging clone missing");
        Check(staging.team == Teams.middle && !staging.dropBoxOnDead && !staging.setActiveByPlayerDistance && staging.canDieIfNotRaidMap && staging.exp == 0, "Staging preset flags changed");
        Check(host.LastOptions.HoldForExternalCommit && !host.LastOptions.ApplySharedMutators && !host.LastOptions.AllowRandomRetryFallback && host.LastOptions.ManagedBossContext == null, "Official spawn options changed");
        CharacterMainControl character = new CharacterMainControl { characterPreset = staging };
        Check(oldState.IsStagingBossHealth(character.Health), "Staging preset was not registered before await");
        host.PendingCore.SetResult(Spawned(character));
        ManagedBossPrepareResult prepared = await task;
        Check(prepared != null && ReferenceEquals(prepared.Character, character), "Official factory result lost");
        Check(character.Health.Invincible && !character.gameObject.activeSelf, "Prepared boss activated before commit");
        Check(character.characterPreset == host.cachedCharacterPresets["Official"] && character.CharacterItem.Exp == 42, "Original preset/exp not restored");
        Check(staging == null && oldState.IsStagingBossHealth(character.Health), "Staging cleanup removed wrong owner");
        Check(prepared.Handle.ActivateOnce() && host.ActivateCalls == 1 && character.gameObject.activeSelf, "Activation delegate lost original host");
        ModeG.PrepareHostDestroy(); host.Shutdown(); UnityEngine.Object.Destroy(host);
        Check(oldState.spawnLeasesInvalidated && oldState.rewardNonceInvalidated && ModeGRunContext.Current == null, "Host destruction did not invalidate run");
        ModBehaviour replacement = new ModBehaviour();
        prepared.Handle.CleanupOnce(ManagedBossCleanupReason.RunEnded); prepared.Handle.CleanupOnce(ManagedBossCleanupReason.RunEnded);
        Check(host.CleanupCalls == 1 && replacement.CleanupCalls == 0, "Handle cleanup rebound to replacement host or ran twice");
        Check(character == null && character.Health == null && character.gameObject == null, "Unity GO destroy did not destroy character/health");

        host = Fresh(); Check(host.TryStartModeG(), "Late official run failed"); oldState = host.Core.State;
        task = host.SpawnModeGOfficialBossAsync(host.Pool[0], new Vector3(), 2, null);
        staging = host.LastDirectPreset;
        ModeG.PrepareHostDestroy(); host.Shutdown(); UnityEngine.Object.Destroy(host);
        replacement = new ModBehaviour(); Check(replacement.TryStartModeG(), "Replacement run failed");
        CharacterMainControl late = new CharacterMainControl { characterPreset = staging };
        host.PendingCore.SetResult(Spawned(late)); prepared = await task;
        Check(prepared != null && oldState.IsStagingBossHealth(late.Health), "Late official factory lost captured state");
        Check(!replacement.Core.State.IsStagingBossHealth(late.Health), "Late official factory registered on new run");
        prepared.Handle.CleanupOnce(ManagedBossCleanupReason.OwnerInvalid);
        Check(host.CleanupCalls == 1 && replacement.CleanupCalls == 0 && late == null, "Late official cleanup lost destroyed host callbacks");
        replacement.Shutdown();

        host = Fresh(); Check(host.TryStartModeG(), "Invalid official run failed");
        task = host.SpawnModeGOfficialBossAsync(host.Pool[0], new Vector3(), 1, null);
        CharacterMainControl invalid = new CharacterMainControl(); invalid.Health.CanDieIfNotRaidMap = false;
        host.PendingCore.SetResult(Spawned(invalid));
        Check(await task == null && invalid == null, "Rejected official factory product leaked");
        host.Shutdown();
    }

    private static async Task ManagedAsync()
    {
        string[] keys = { ModeGEncounterVariation.ManagedDragonDescendantKey, ModeGEncounterVariation.ManagedDragonKingKey, ModeGEncounterVariation.ManagedPhantomWitchKey };
        string[] adapters = { "Dragon", "King", "Witch" };
        for (int i = 0; i < keys.Length; i++)
        {
            ModBehaviour host = Fresh(); Check(host.TryStartModeG(), "Managed run failed");
            ModeGRunState state = host.Core.State;
            ManagedBossSpawnContext ctx = ManagedBossSpawnContext.CreateModeGPrimary(keys[i], () => !state.spawnLeasesInvalidated && ReferenceEquals(ModeGRunContext.Current, state));
            Task<ManagedBossPrepareResult> task = host.DispatchModeGManagedBossSpawnAsync(host.Pool[0], new Vector3(), ctx, false);
            Check(!task.IsCompleted && host.LastAdapter == adapters[i] && ReferenceEquals(host.LastManagedContext, ctx), "Managed adapter binding mismatch");
            CharacterMainControl late = new CharacterMainControl(); int cleanupCount = 0; ManagedBossCleanupReason reason = ManagedBossCleanupReason.Death;
            ManagedBossRuntimeHandle handle = new ManagedBossRuntimeHandle { Character = late, Cleanup = r => { cleanupCount++; reason = r; UnityEngine.Object.Destroy(late.gameObject); } };
            ModeG.PrepareHostDestroy(); host.Shutdown(); UnityEngine.Object.Destroy(host);
            ModBehaviour replacement = new ModBehaviour(); Check(replacement.TryStartModeG(), "Managed replacement run failed");
            host.PendingManaged.SetResult(new ManagedBossPrepareResult { Character = late, Handle = handle });
            Check(await task == null && cleanupCount == 1 && reason == ManagedBossCleanupReason.OwnerInvalid && late == null, "Late managed factory was not rejected and reclaimed");
            handle.CleanupOnce(ManagedBossCleanupReason.RunEnded);
            Check(cleanupCount == 1 && replacement.PrepareCalls == 0, "Late managed cleanup repeated or factory rebound");
            replacement.Shutdown();
        }
        ModBehaviour rejected = Fresh();
        ManagedBossSpawnContext invalidCtx = ManagedBossSpawnContext.CreateModeGPrimary(keys[0], () => false);
        Check(await rejected.DispatchModeGManagedBossSpawnAsync(rejected.Pool[0], new Vector3(), invalidCtx, false) == null && rejected.PrepareCalls == 0, "Invalid owner reached factory");
        invalidCtx.Owner = ManagedBossOwner.Legacy; invalidCtx.IsOwnerValid = () => true;
        Check(await rejected.DispatchModeGManagedBossSpawnAsync(rejected.Pool[0], new Vector3(), invalidCtx, false) == null && rejected.PrepareCalls == 0, "Legacy owner entered Mode G adapter");
        ModBehaviour managed = Fresh(); Check(managed.TryStartModeG(), "Managed shared spawn run failed");
        ManagedBossSpawnContext context = ManagedBossSpawnContext.CreateModeGPrimary(keys[0], () => true);
        Task<ManagedBossPrepareResult> shared = managed.SpawnModeGManagedBossAsync(managed.Pool[0], new Vector3(), 3, context);
        Check(ReferenceEquals(managed.LastOptions.ManagedBossContext, context) && managed.LastOptions.HoldForExternalCommit && !managed.LastOptions.ApplySharedMutators && !managed.LastOptions.AllowRandomRetryFallback, "Managed shared options/context lost");
        CharacterMainControl boss = new CharacterMainControl(); ManagedBossRuntimeHandle resultHandle = new ManagedBossRuntimeHandle { Character = boss };
        managed.PendingCore.SetResult(Spawned(boss, resultHandle));
        ManagedBossPrepareResult result = await shared;
        Check(ReferenceEquals(result.Handle, resultHandle) && ReferenceEquals(result.Character, boss), "Managed shared spawn bridge changed returned identity");
        managed.Shutdown();
    }

    private static async Task Main()
    {
        LazyAndTwoRuns(); EntryGatesAndRefunds(); PrepaidAndArena(); LiveQueriesAndPresentation();
        await OfficialAsync(); await ManagedAsync();
        Console.WriteLine("PASS ModeGEntryOwners " + assertions + " assertions");
    }
}
