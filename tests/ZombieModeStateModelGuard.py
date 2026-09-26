from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
ENTRY = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
HOST_BRIDGE = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")
MODE_RUNTIME_HOOKS = Path("Utilities/ModeRuntimeHooks.cs")
ZOMBIE_RUNTIME_HOOKS = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
HOST_LIFECYCLE = Path("ZombieMode/ZombieModeRuntimeModule_HostLifecycle.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")

REQUIRED_MODEL_SNIPPETS = [
    "public enum ZombieModeLifecyclePhase",
    "WaitingStarterChoice",
    "Active",
    "Exiting",
    "public enum ZombieModeCombatPhase",
    "InitialPreparation",
    "ExtractionOpportunity",
    "public enum ZombieModeFailureReason",
    "SuccessfulExtraction",
    "public enum ZombieModeBossKind",
    "public static class ZombieModeTuning",
    "PreparationCountdownSeconds = 45f",
    "BeaconChannelDurationSeconds = 3f",
    "ExtractionCountdownSeconds = 15f",
    "public static class ZombieModePhaseGuards",
    "IsBeforeActive",
    "AllowsBeacon",
    "AllowsExtraction",
    "IsRunActive",
    "IsSettling",
    "Boss,",
    "Projectile,",
    "Fortification,",
    "Buff",
    "public sealed class ZombieModeRunState",
    "public int RunId;",
    "public ZombieModeMapProfile MapProfile;",
    "public long PendingCashInvestment;",
    "public long ConfirmedCashInvested;",
    "public readonly List<ZombiePurificationStar> PendingPurificationStars",
    "public readonly List<ZombieModeBossInstance> CurrentWaveBossInstances",
    "public readonly List<ZombieModeSpawnPoint> EffectiveSpawnPoints",
    "public int LivingZombieCount;",
    "public float BeaconChannelStartTime;",
    "public float BeaconChannelDuration",
    "public CountDownArea ActiveExtractionArea;",
    "public Vector3 ActiveSafeZoneCenter;",
    "public bool ActiveSafeZonePortable;",
    "public int PollutionFromNatural;",
    "public int TotalPollution",
    "public ZombieModeRewardNode CurrentRewardNode;",
    "public int SelectedPreparationDurationSeconds = 45;",
    "public int RemainingSelections = 1;",
    "public readonly ZombieModeInsuranceState InsuranceState",
    "public readonly List<ZombieModeDropCandidate> EntityDropCleanupCandidates",
    "public string StarterAmmoCaliber",
    "public readonly List<ZombieModeRunOnlyRecord> RunOnlyObjects",
    "public sealed class ZombieModeMapProfile",
    "public string MainSceneName",
    "public Vector3[][] SafeZoneExclusionPolygons",
    "public long CashWithheldAmount;",
    "public readonly List<string> BlockingMessages",
]

REQUIRED_ENTRY_SNIPPETS = [
    "private ZombieModeRunState zombieModeRunState",
    "private ZombieModeEntryTransaction zombieModeEntryTransaction",
    "private bool pendingZombieModeEntry",
    "private int nextZombieModeRunId",
    "public bool IsZombieModeActive",
    "public int ZombieModeCurrentRunId",
    "private bool IsZombieModeRunValid(int runId)",
    "BuildZombieModeMapProfile",
    "FailZombieModeBeforeActive(ZombieModeFailureReason reason)",
]

REQUIRED_MODULE_SNIPPETS = [
    "private ZombieModeRunState runState;",
    "private ZombieModeEntryTransaction entryTransaction;",
    "private Dictionary<string, int[]> rewardCandidateCache;",
    "private List<int> rewardCandidateScratch;",
    "private HashSet<int> opaqueFilterLogIds;",
    "private bool pendingEntry;",
    "private float runtimePausedDuration;",
    "private float runtimePauseStartTime = -1f;",
    "private int runtimePauseRunId;",
    "internal void AdoptHostState(",
    "currentOwner.DetachZombieModeRuntimeModule(this);",
    "this.runState = runState;",
    "this.entryTransaction = entryTransaction;",
    "this.rewardCandidateCache = rewardCandidateCache;",
    "this.rewardCandidateScratch = rewardCandidateScratch;",
    "this.opaqueFilterLogIds = opaqueFilterLogIds;",
    "internal bool CanStartZombieModeMapSelectionPhase1(out string failureReason)",
    "internal void MarkZombieModeMapConfirmedPhase1()",
    "internal bool CommitZombieModeEntryResourcesShell(out ZombieModeFailureReason reason)",
    "internal int BeginZombieModeRunShell(int sceneBuildIndex, string sceneName)",
    "internal bool InitializeZombieModeRunAfterMapLoaded(int runId)",
    "internal void FinalizeZombieModeEntryResources()",
    "internal void TickZombieMode(float deltaTime)",
    "ZombieModePhaseGuards.IsActive(phase)",
    "SceneManager.GetActiveScene()",
    "ZombieModeFailureReason.InitializationFailed",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    model_text = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    entry_text = ENTRY.read_text(encoding="utf-8")
    bridge_text = HOST_BRIDGE.read_text(encoding="utf-8")
    lifecycle_text = HOST_LIFECYCLE.read_text(encoding="utf-8")
    host_entry_text = entry_text + "\n" + bridge_text
    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8")
    mode_runtime_hooks_text = MODE_RUNTIME_HOOKS.read_text(encoding="utf-8")
    zombie_runtime_hooks_text = ZOMBIE_RUNTIME_HOOKS.read_text(encoding="utf-8")
    runtime_module_text = RUNTIME_MODULE.read_text(encoding="utf-8")

    for snippet in REQUIRED_MODEL_SNIPPETS:
        if snippet not in model_text:
            return fail("ZombieModeStateModelGuard: model missing snippet -> " + snippet)

    for snippet in REQUIRED_ENTRY_SNIPPETS:
        if snippet not in host_entry_text:
            return fail("ZombieModeStateModelGuard: entry missing snippet -> " + snippet)

    for snippet in REQUIRED_MODULE_SNIPPETS:
        if snippet not in runtime_module_text:
            return fail("ZombieModeStateModelGuard: runtime module missing snippet -> " + snippet)
    for snippet in [
        "module.AdoptHostState(",
        "zombieModeUnattachedRunState,",
        "zombieModeUnattachedEntryTransaction,",
        "zombieModeUnattachedRewardCandidateCache,",
        "zombieModeUnattachedRewardCandidateScratch,",
        "zombieModeUnattachedOpaqueFilterLogIds,",
        "zombieModeUnattachedPendingEntry);",
        "zombieModeRuntimeModule.RunState : zombieModeUnattachedRunState",
        "zombieModeRuntimeModule.EntryTransaction : zombieModeUnattachedEntryTransaction",
    ]:
        if snippet not in lifecycle_text:
            return fail("ZombieModeStateModelGuard: lifecycle state owner missing snippet -> " + snippet)
    if "private readonly ZombieModeRunState zombieModeRunState" in host_entry_text:
        return fail("ZombieModeStateModelGuard: ModBehaviour still owns the active ZombieModeRunState field")
    for snippet in [
        "return module.CanStartZombieModeMapSelectionPhase1(out failureReason);",
        "module.MarkZombieModeMapConfirmedPhase1();",
        "return module != null && module.IsZombieModeMapLoadReadyPhase1();",
        "if (module != null) module.AbortZombieModeMapLoadPhase1(reason);",
        "return module != null && module.TryHandleZombieModePendingMapSceneLoaded(scene, loadedMapConfig);",
        "return module != null && module.InitializeZombieModeRunAfterMapLoaded(runId);",
        "if (module != null) module.TickZombieMode(deltaTime);",
    ]:
        if snippet not in host_entry_text:
            return fail("ZombieModeStateModelGuard: host entry bridge missing module forward -> " + snippet)

    if "TickModeRuntimeGroup(Time.deltaTime, Time.unscaledDeltaTime)" not in mod_text:
        return fail("ZombieModeStateModelGuard: ModBehaviour.Update does not tick mode runtime group")
    if "TickZombieModeRuntime(unscaledDeltaTime);" not in mode_runtime_hooks_text:
        return fail("ZombieModeStateModelGuard: mode runtime group does not tick ZombieMode")
    if "TickZombieMode(unscaledDeltaTime);" not in zombie_runtime_hooks_text:
        return fail("ZombieModeStateModelGuard: ZombieMode runtime hook does not tick ZombieMode")

    print("ZombieModeStateModelGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
