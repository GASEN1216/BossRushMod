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
        Ordered("set-points:Arena", "item-cache");
        Check(!Harness.Events.Contains("entry-point") && !Harness.Events.Contains("ammo-refill"),
            "Mode G must not create standard arena signs, trash cans or refill options");
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
        AssemblyOrder.Run(Check);
        IdleCoopGuests();
        CoopReceiptOwners();
        CoopAdmissionBoundaries();
        CoopAdmissionTimeout();
        Console.WriteLine("PASS ModeGEntryOwners " + assertions + " assertions");
    }

    private static void Use(TestPeer peer,ModBehaviour owner)
    {BossRushCoop.Current=peer;ModBehaviour.Instance=owner;owner.GetModeGEntryRuntimeForHost().AuditUseOwner();}

    private static ModeGCoopCombat Fact(ulong run,long sequence,int wave=0)
    {return new ModeGCoopCombat{Run=run,Sequence=sequence,Wave=wave,Boss=-7,Phase=(int)ModeGCombatPhase.Fighting,Damage=17,Final=17};}

    private static void CoopReceiptOwners()
    {
        ModBehaviour host=Fresh(),paid=new ModBehaviour(),declined=new ModBehaviour();declined.Ticket=null;
        var peers=new[]{new TestPeer{Id="host",Host=true},new TestPeer{Id="paid"},new TestPeer{Id="declined"}};
        BossRushCoop.Peers.Clear();BossRushCoop.Peers.AddRange(peers);var owners=new[]{host,paid,declined};
        for(int i=0;i<3;i++){Use(peers[i],owners[i]);owners[i].GetModeGEntryRuntimeForHost().EnsureCoop();}
        Use(peers[0],host);Check(host.TryStartModeG(),"Receipt fixture host starts through paid production entry");host.Tick(1);BossRushCoop.Flush();
        for(int i=1;i<3;i++){Use(peers[i],owners[i]);ModeGRunContext.Unbind(ModeGRunContext.Current);owners[i].Tick(1);BossRushCoop.Flush();}
        var hostEntry=host.GetModeGEntryRuntimeForHost();var guestEntry=paid.GetModeGEntryRuntimeForHost();var declinedEntry=declined.GetModeGEntryRuntimeForHost();
        var runA=host.Core;var guestA=paid.Core;ulong a=runA.State.runId;string asset=peers[1].AssetId;
        Check(guestA.State.runId==a&&guestA.CoopRunRoom==peers[1].Run&&guestA.CoopRunAsset==asset,"Actual freeze binds admitted core to room and original slot");guestA.AuditBindBoss(-7);
        Use(peers[2],declined);Check(!declined.Active&&declined.Core==null,"Actual decline has no personal core or eligibility");
        // The host-side leave set belongs to another native process. The fixture
        // explicitly selects that process owner before examining its state.
        Use(peers[0],host);Check(ModeGEntryRuntime.HasLeft("declined"),"Host receives real leave command for declined participant");
        hostEntry.AuditDeliver("declined",a.ToString("x")+":audit:before-admission",Fact(a,60));BossRushCoop.Flush();
        Check(!hostEntry.AuditHasDebt&&declined.Core==null,"Unqualified declined receipt acknowledges without awarding personal combat");
        Use(peers[2],declined);declined.Ticket=new Item{TypeID=9001};ModeGRunContext.Unbind(ModeGRunContext.Current);
        BossRushCoop.AuditDropAdmitted=true;BossRushCoop.AuditDropAdmission=true;
        Check(declined.TryStartModeG()&&declined.Core.State.runId==a,"Declined guest can pay and actually enter same world run");declined.Core.AuditBindBoss(-7);
        BossRushCoop.Flush();Use(peers[0],host);Check(ModeGEntryRuntime.HasLeft("declined")&&!declined.Core.CoopAdmissionReady,"Lost admitted command leaves explicit causal gate closed");
        Use(peers[0],host);hostEntry.AuditDeliver("declined",a.ToString("x")+":audit:start-in-flight",Fact(a,65));BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==0&&hostEntry.AuditHasDebt,"Real combat earned after Start but before admitted arrives remains pending");
        BossRushCoop.AuditDropAdmitted=false;
        Use(peers[2],declined);declinedEntry.AuditRequestAdmission(a+1);BossRushCoop.Flush();Use(peers[0],host);
        Check(ModeGEntryRuntime.HasLeft("declined"),"Unknown run admission cannot clear departure from actual run");
        Use(peers[2],declined);declined.Tick(1);BossRushCoop.Flush();
        Use(peers[0],host);Check(!ModeGEntryRuntime.HasLeft("declined"),"Successful same-run personal admission clears host departed through authenticated command");
        Use(peers[0],host);BossRushCoop.AuditWire.Clear();hostEntry.AuditDeliver("declined",a.ToString("x")+":audit:live",Fact(a,70));BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==0&&!declined.Core.CoopAdmissionReady&&hostEntry.AuditHasDebt,"Combat arriving before lost baseline waits without arbitrary sequence skip");
        BossRushCoop.AuditDropAdmission=false;hostEntry.AuditRetry(1);BossRushCoop.Flush();hostEntry.AuditRetry(1);BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==2&&!hostEntry.AuditHasDebt,"Frozen Start baseline preserves in-flight earned combat and later ordered fact");
        Use(peers[2],declined);declinedEntry.AuditRequestAdmission(a);declined.Tick(2);BossRushCoop.Flush();Check(declined.Core.AuditDamageCalls==2,"Baseline retry and duplicate admitted request never erase or replay earned combat");
        Use(peers[0],host);BossRushCoop.AuditWire.Clear();var prior=Fact(a,80);prior.Order=1;
        BossRushCoop.SendToPlayer("declined","BossRush.Personal.G.Combat",new CoopPersonalReceipt{RunId=peers[0].Run,TargetAssetId=peers[2].AssetId,Id="1:prior-qualified-fact",Payload=BossRushCoopJson.ToJson(prior)});BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==2&&BossRushCoop.AuditWire.Any(p=>p.Topic.EndsWith(".ack")),"Explicit admission baseline acknowledges only known prequalification facts without scoring");
        BossRushCoop.AuditWire.Clear();var gap=Fact(a,81);gap.Order=5;
        BossRushCoop.SendToPlayer("declined","BossRush.Personal.G.Combat",new CoopPersonalReceipt{RunId=peers[0].Run,TargetAssetId=peers[2].AssetId,Id="1:post-baseline-gap",Payload=BossRushCoopJson.ToJson(gap)});BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==2&&!BossRushCoop.AuditWire.Any(p=>p.Topic.EndsWith(".ack")),"Combat after admission still rejects a missing intermediate order");
        BossRushCoop.AuditWire.Clear();var changed=Fact(a,82);changed.Admission=true;changed.Order=3;
        BossRushCoop.SendToPlayer("declined","BossRush.Personal.G.Combat",new CoopPersonalReceipt{RunId=peers[0].Run,TargetAssetId=peers[2].AssetId,Id="1:changed-baseline",Payload=BossRushCoopJson.ToJson(changed)});BossRushCoop.Flush();
        Check(declined.Core.AuditDamageCalls==2&&!BossRushCoop.AuditWire.Any(p=>p.Topic.EndsWith(".ack")),"Later admission fact cannot replace baseline and erase earned ordered combat");
        Use(peers[0],host);
        var late=new TestPeer{Id="late",AssetId="late-slot-A"};BossRushCoop.Peers.Add(late);
        Check(runA.GetCoopParticipantAsset("late")=="late-slot-A","Late participant freezes current asset at first actual lookup");late.AssetId="late-slot-B";
        Check(runA.GetCoopParticipantAsset("late")=="late-slot-A","Later slot changes never overwrite late participant frozen asset");
        peers[1].AssetId="other-slot";BossRushCoop.AuditWire.Clear();hostEntry.AuditDeliver("paid",a.ToString("x")+":audit:asset",Fact(a,1001));BossRushCoop.Flush();
        var sent=BossRushCoop.AuditWire.Single(p=>p.Topic=="BossRush.Personal.G.Combat"&&p.Target=="paid");var receipt=BossRushCoopJson.FromJson<CoopPersonalReceipt>(sent.Json);
        Check(receipt.TargetAssetId==asset&&guestA.AuditDamageCalls==0&&hostEntry.AuditHasDebt&&!hostEntry.AuditPending,"Real receipt freezes original asset; wrong slot neither consumes nor blocks current world");
        peers[1].AssetId=asset;Use(peers[0],host);hostEntry.AuditRetry(1);BossRushCoop.Flush();
        Check(guestA.AuditDamageCalls==1&&!hostEntry.AuditHasDebt,"Original slot returns and consumes exact ordered debt once");
        hostEntry.AuditDeliver("paid",a.ToString("x")+":audit:unfinished",Fact(a,1002,99));BossRushCoop.Flush();
        Check(hostEntry.AuditHasDebt&&hostEntry.AuditPending&&guestA.AuditDamageCalls==1,"Real inner wave check leaves rejected receipt pending");
        hostEntry.AuditDepart("paid");Check(!hostEntry.AuditPending&&hostEntry.AuditHasDebt,"Departed participant stops blocking without deleting original debt");
        Use(peers[1],paid);guestEntry.AuditFinish(guestA);Check(!guestA.State.IsTerminal,"Early Finish cannot fabricate a terminal state");
        // End is a documented controlled core dependency in this entry fixture;
        // real state transitions occur before the separately extracted Finish.
        guestA.End(ModeGExitReason.ManualExit);Check(guestA.State.IsTerminal,"Controlled End advances actual production state to terminal");guestEntry.AuditFinish(guestA);paid.Shutdown();
        int consumedBeforeClosed=paid.Consumed.Count;ModeGRunContext.Unbind(ModeGRunContext.Current);
        Check(!paid.TryStartModeG()&&paid.Consumed.Count==consumedBeforeClosed&&paid.Core==null,"Actually completed original owner cannot pay again into the same run or replace earned state with admission baseline");
        Use(peers[0],host);host.Shutdown();ModeGRunContext.Unbind(ModeGRunContext.Current);Check(host.TryStartModeG(),"New run starts through original production entry");host.Tick(0);
        ulong b=host.Core.State.runId;Check(b!=a&&!hostEntry.AuditPending&&hostEntry.AuditHasDebt,"Old run pending debt cannot block new run even after departure set resets");BossRushCoop.Flush();
        Use(peers[1],paid);ModeGRunContext.Unbind(ModeGRunContext.Current);Check(paid.TryStartModeG()&&paid.Core.State.runId==b,"Original slot starts matching next run with fresh core");var guestB=paid.Core;guestB.AuditBindBoss(-7);
        peers[1].AssetId="other-slot";Use(peers[0],host);hostEntry.AuditRetry(1);BossRushCoop.Flush();Check(hostEntry.AuditHasDebt&&guestB.AuditDamageCalls==0,"Terminal tombstone does not acknowledge on another slot");
        peers[1].AssetId=asset;hostEntry.AuditRetry(1);BossRushCoop.Flush();Check(!hostEntry.AuditHasDebt&&guestB.AuditDamageCalls==0,"Terminal old-run fact acknowledges on original slot without changing new-run telemetry");
        BossRushCoop.AuditWire.Clear();var unknown=new CoopPersonalReceipt{RunId=peers[0].Run,TargetAssetId=asset,Id="1:unknown:fact",Payload=BossRushCoopJson.ToJson(Fact(a+1000,99))};
        BossRushCoop.SendToPlayer("paid","BossRush.Personal.G.Combat",unknown);BossRushCoop.Flush();
        Check(!BossRushCoop.AuditWire.Any(p=>p.Topic.EndsWith(".ack"))&&guestB.AuditDamageCalls==0,"Unknown old run receives no acknowledgement and cannot pollute active core");
        peers[1].Run="other-room";BossRushCoop.AuditWire.Clear();unknown.RunId="other-room";unknown.Id="1:old-room:fact";unknown.Payload=BossRushCoopJson.ToJson(Fact(a,100));BossRushCoop.SendToPlayer("paid","BossRush.Personal.G.Combat",unknown);BossRushCoop.Flush();
        Check(!BossRushCoop.AuditWire.Any(p=>p.Topic.EndsWith(".ack"))&&guestB.AuditDamageCalls==0,"New room clears old room tombstones even for same original slot");
        for(int i=0;i<3;i++){Use(peers[i],owners[i]);owners[i].Shutdown();ModeGEntryRuntime.DisposeCoopOwner();}
        BossRushCoop.Current=new TestPeer{Id="offline",Host=true,Connected=false};
    }

    private static void CoopAdmissionBoundaries()
    {
        var host=Fresh();var client=new ModBehaviour{Ticket=null};var peers=new[]{new TestPeer{Id="boundary-host",Host=true},new TestPeer{Id="boundary-client"}};
        BossRushCoop.Peers.Clear();BossRushCoop.Peers.AddRange(peers);Use(peers[0],host);host.GetModeGEntryRuntimeForHost().EnsureCoop();Use(peers[1],client);client.GetModeGEntryRuntimeForHost().EnsureCoop();
        Use(peers[0],host);Check(host.TryStartModeG(),"Boundary host uses actual paid entry");host.Tick(1);BossRushCoop.Flush();Use(peers[1],client);ModeGRunContext.Unbind(ModeGRunContext.Current);client.Tick(1);BossRushCoop.Flush();
        var h=host.GetModeGEntryRuntimeForHost();var c=client.GetModeGEntryRuntimeForHost();ulong run=host.Core.State.runId;
        client.Ticket=new Item{TypeID=9001};peers[1].AssetId="new-slot";Use(peers[0],host);host.Tick(1);BossRushCoop.Flush();Use(peers[1],client);int cost=client.Consumed.Count;
        Check(!client.TryStartModeG()&&client.Consumed.Count==cost&&client.Core==null,"Known frozen participant asset rejects actual new-slot entry before payment");
        var world=new ModeGCoopWorld{Id=run,Active=true,Wave=0,Phase=(int)ModeGCombatPhase.Fighting,Expected=1,Resolved=1,Committed=0,Actors=new ModeGCoopActor[0],ActorsComplete=true,Seed=host.Core.State.runSeed,SceneName="Arena",SceneId="arena-id",Verification=ModeGAvailability.CurrentVerificationRevision};
        c.AuditWorld(world);ModeGRunContext.Unbind(ModeGRunContext.Current);Check(client.TryStartModeG(),"Stale participant snapshot exercises actual paid entry before host rejection");var denied=client.Core;
        denied.AuditGuestDrive(world);Check(denied.RefundTicket&&denied.RefundRelic,"Real guest drive preserves paid refund lease before admission even with resolved fighting wave");
        BossRushCoop.Flush();Check(denied.State.IsTerminal&&Harness.RefundCount(9001)==1&&Harness.RefundCount(FateEchoRelicConfig.TYPE_ID)==1,"Reliable host denial uses real technical startup refund once for both paid items");
        Use(peers[0],host);h.AuditRetry(1);BossRushCoop.Flush();Check(Harness.RefundCount(9001)==1&&Harness.RefundCount(FateEchoRelicConfig.TYPE_ID)==1,"Repeated denial cannot refund paid items twice");Use(peers[1],client);client.Tick(1);Check(client.Core==null,"Denied paid runtime reaches real entry shutdown");
        peers[1].AssetId="slot-A";world.Wave=1;world.Actors=new[]{new ModeGCoopActor{Id=-8,Slot=0,Key="living"}};world.Committed=1;world.Participants=null;Use(peers[1],client);c.AuditWorld(world);ModeGRunContext.Unbind(ModeGRunContext.Current);
        Check(client.TryStartModeG(),"Original slot starts later wave with zero known prequalification order");var late=client.Core;late.State.waveEpoch=1;late.AuditBindBoss(-8);BossRushCoop.Flush();Check(late.CoopAdmissionReady&&late.CoopRequestedAdmissionBaseline==0,"Later-wave admission confirms explicit zero baseline");
        Use(peers[0],host);BossRushCoop.AuditDropCombat=true;h.AuditDeliver(peers[1].Id,run.ToString("x")+":old-wave-first",Fact(run,900,0));BossRushCoop.Flush();BossRushCoop.AuditDropCombat=false;h.AuditDeliver(peers[1].Id,run.ToString("x")+":old-wave-second",Fact(run,901,0));BossRushCoop.Flush();Check(late.AuditDamageCalls==0&&h.AuditHasDebt,"Late old-wave order cannot skip a missing intermediate fact");
        h.AuditRetry(1);BossRushCoop.Flush();Check(late.AuditDamageCalls==0&&!h.AuditHasDebt,"Strictly ordered old-wave receipts acknowledge without scoring or blocking later facts");
        h.AuditDeliver(peers[1].Id,run.ToString("x")+":already-dead-current-wave",Fact(run,902,1));BossRushCoop.Flush();Check(late.AuditDamageCalls==0&&!h.AuditHasDebt,"Complete frozen entry actor set acknowledges same-wave pre-entry dead AI absent from actual core");
        var alive=Fact(run,903,1);alive.Boss=-8;h.AuditDeliver(peers[1].Id,run.ToString("x")+":living-current-wave",alive);BossRushCoop.Flush();Check(late.AuditDamageCalls==1&&!h.AuditHasDebt,"Actual present current-wave AI still grants earned combat after old receipts");
        Use(peers[0],host);host.Shutdown();ModeGRunContext.Unbind(ModeGRunContext.Current);Check(host.TryStartModeG(),"Actual same-room replacement host run starts");ulong next=host.Core.State.runId;host.Tick(1);BossRushCoop.Flush();
        Check(late.State.IsTerminal&&client.Core==null,"Fresh host run snapshot closes still-active old guest rather than ignoring it");Use(peers[1],client);ModeGRunContext.Unbind(ModeGRunContext.Current);client.Tick(1);BossRushCoop.Flush();Check(client.Core!=null&&client.Core.State.runId==next&&client.Core.CoopAdmissionReady,"Guest automatically enters genuine replacement host run through original paid entry");var current=client.Core;
        peers[1].Run="";Check(current.CheckCoopAsset()&&!current.State.IsTerminal,"Empty reconnect handshake preserves original room core");peers[1].Connected=false;Check(!current.CoopRunHasWorldAuthority&&current.CoopRunWasClient&&!ModeGEntryRuntime.CanApplyPersonalCombat,"Disconnected original guest cannot acquire world authority or personal telemetry");peers[1].Connected=true;peers[1].Run="room-B";Use(peers[0],host);host.Shutdown();peers[0].Run="room-B";ModeGRunContext.Unbind(ModeGRunContext.Current);Check(host.TryStartModeG(),"Host new room starts genuine replacement run");host.Tick(1);BossRushCoop.Flush();
        Check(current.State.IsTerminal&&client.Core==null,"Nonempty new room snapshot closes original guest via actual End");Use(peers[1],client);ModeGRunContext.Unbind(ModeGRunContext.Current);client.Tick(1);BossRushCoop.Flush();Check(client.Core!=null&&client.Core.CoopRunRoom=="room-B"&&client.Core.State.runId==host.Core.State.runId&&client.Core.CoopAdmissionReady,"Original guest automatically adopts newly connected host room without retaining old core");
        for(int i=0;i<2;i++){Use(peers[i],i==0?host:client);(i==0?host:client).Shutdown();ModeGEntryRuntime.DisposeCoopOwner();}BossRushCoop.Current=new TestPeer{Id="offline",Host=true,Connected=false};
    }

    private static void CoopAdmissionTimeout()
    {
        var host=Fresh();var client=new ModBehaviour();var peers=new[]{new TestPeer{Id="timeout-host",Host=true},new TestPeer{Id="timeout-client"}};BossRushCoop.Peers.Clear();BossRushCoop.Peers.AddRange(peers);
        Use(peers[0],host);host.GetModeGEntryRuntimeForHost().EnsureCoop();Use(peers[1],client);client.GetModeGEntryRuntimeForHost().EnsureCoop();Use(peers[0],host);Check(host.TryStartModeG(),"Admission timeout host starts actual entry");host.Tick(1);BossRushCoop.Flush();
        Use(peers[1],client);ModeGRunContext.Unbind(ModeGRunContext.Current);BossRushCoop.AuditDropAdmission=true;client.Tick(1);BossRushCoop.Flush();var core=client.Core;
        Check(core!=null&&!core.CoopAdmissionReady&&client.Consumed.Count==2,"Actual paid guest remains waiting when all admission facts are lost");Time.realtimeSinceStartup=121;client.Tick(1);BossRushCoop.Flush();
        Check(core.State.IsTerminal&&client.Core==null&&Harness.RefundCount(9001)==1&&Harness.RefundCount(FateEchoRelicConfig.TYPE_ID)==1,"Bounded admission timeout uses actual technical refund and entry shutdown");
        BossRushCoop.AuditDropAdmission=false;Use(peers[0],host);host.GetModeGEntryRuntimeForHost().AuditRetry(1);BossRushCoop.Flush();Check(Harness.RefundCount(9001)==1,"Late baseline cannot reopen timed-out paid owner or double refund");Time.realtimeSinceStartup=0;
        for(int i=0;i<2;i++){Use(peers[i],i==0?host:client);(i==0?host:client).Shutdown();ModeGEntryRuntime.DisposeCoopOwner();}BossRushCoop.Current=new TestPeer{Id="offline",Host=true,Connected=false};
    }

    private static void IdleCoopGuests()
    {
        ModBehaviour host = Fresh(), paidGuest = Fresh(), declinedGuest = Fresh();
        declinedGuest.Ticket = null;
        var peers = new[] { new TestPeer { Id = "host", Host = true }, new TestPeer { Id = "guest-paid" }, new TestPeer { Id = "guest-unpaid" } };
        BossRushCoop.Peers.Clear(); BossRushCoop.Peers.AddRange(peers);
        var owners = new[] { host, paidGuest, declinedGuest };
        for (int i = 0; i < peers.Length; i++)
        {
            BossRushCoop.Current = peers[i]; ModBehaviour.Instance = owners[i];
            owners[i].GetModeGEntryRuntimeForHost().EnsureCoop(); owners[i].GetModeGEntryRuntimeForHost().EnsureCoop();
            Check(peers[i].Handlers["BossRush.Gameplay.G.snapshot"].Count == 1, "Idle bootstrap registers each real snapshot channel exactly once");
            Check(!owners[i].Active && owners[i].Consumed.Count == 0, "Bootstrap cannot start a run or consume personal fees");
        }
        BossRushCoop.Current = peers[0]; ModBehaviour.Instance = host;
        Check(host.TryStartModeG(), "Room host enters through the real paid Mode G player entry");
        host.Tick(1); BossRushCoop.Flush();
        for (int i = 1; i < peers.Length; i++)
        {
            BossRushCoop.Current = peers[i]; ModBehaviour.Instance = owners[i];
            // 原版各端在不同进程，客端不持有房主进程的静态 RunContext。
            ModeGRunContext.Unbind(ModeGRunContext.Current);
            owners[i].Tick(1); BossRushCoop.Flush(); owners[i].Tick(1); BossRushCoop.Flush();
        }
        Check(paidGuest.Active && paidGuest.Consumed.Count == 2 && paidGuest.Consumed.Contains(9001)
            && paidGuest.Consumed.Contains(FateEchoRelicConfig.TYPE_ID), "Idle guest receives the real encoded world and pays its own original ticket and relic");
        Check(!declinedGuest.Active && declinedGuest.Consumed.Count == 0 && peers[2].ReadyKeys.Count == 1,
            "Guest lacking its ticket declines through real TryStart and stops blocking the world scene barrier");
        BossRushCoop.Current = peers[1]; ModBehaviour.Instance = paidGuest; paidGuest.Tick(1);
        Check(paidGuest.Consumed.Count == 2, "Repeated world ticks never charge admitted guest twice");
        for (int i = 0; i < peers.Length; i++) { BossRushCoop.Current = peers[i]; ModBehaviour.Instance = owners[i]; owners[i].Shutdown(); }
        BossRushCoop.Current = new TestPeer { Id = "offline", Host = true, Connected = false };
    }
}
