from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
LIFECYCLE = Path("ZombieMode/ZombieModeRuntimeModule_HostLifecycle.cs")
CLEANUP = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
ENTRY = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")
MODE_RUNTIME_HOOKS = Path("Utilities/ModeRuntimeHooks.cs")
ZOMBIE_RUNTIME_HOOKS = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
BRIDGES = Path("ZombieMode/ZombieModeEntryHostBridge.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    model_text = MODELS.read_text(encoding="utf-8")
    cleanup_text = CLEANUP.read_text(encoding="utf-8")
    lifecycle_text = LIFECYCLE.read_text(encoding="utf-8")
    entry_text = ENTRY.read_text(encoding="utf-8")
    module_text = RUNTIME_MODULE.read_text(encoding="utf-8")
    bridge_text = BRIDGES.read_text(encoding="utf-8")
    entry_flow_text = entry_text + "\n" + module_text
    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8")
    mode_runtime_hooks_text = MODE_RUNTIME_HOOKS.read_text(encoding="utf-8")
    zombie_runtime_hooks_text = ZOMBIE_RUNTIME_HOOKS.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeRunOnlyRecord",
        "public bool Cleanup(bool destroyGameObject)",
        "UnityEngine.Object.Destroy(GameObject)",
        "Target = null;",
        "GameObject = null;",
        "CleanupAction = null;",
    ]:
        if snippet not in model_text:
            return fail("ZombieModeRunOnlyCleanupGuard: model cleanup missing snippet -> " + snippet)

    for snippet in [
        "RegisterZombieModeRunOnlyObject",
        "InvalidateZombieModeRun()",
        "CleanupZombieModeRunOnlyState",
        "ShouldSettleZombieModeFailureInsurance",
        "RunScopedRegistry.ForEachReverse(",
        "runState.RunOnlyObjects.Clear();",
    ]:
        if snippet not in module_text:
            return fail("ZombieModeRunOnlyCleanupGuard: RuntimeModule cleanup missing snippet -> " + snippet)

    for snippet in [
        "StartZombieModeCoroutine",
        "CleanupZombieModeForSceneChange",
        "CleanupZombieModeOnDestroy",
        "zombieModeRunState.RunOnlyObjects.Clear();",
        "zombieModeEntryTransaction.Reset();",
    ]:
        if snippet == "zombieModeRunState.RunOnlyObjects.Clear();":
            continue
        if snippet not in lifecycle_text:
            return fail("ZombieModeRunOnlyCleanupGuard: lifecycle owner cleanup missing snippet -> " + snippet)

    host_cleanup = cleanup_text[cleanup_text.index("private void CleanupZombieModeRunOnlyState"):]
    host_cleanup = host_cleanup[:host_cleanup.index("private bool ShouldSettleZombieModeFailureInsurance")]
    if "zombieModeRuntimeModule.CleanupZombieModeRunOnlyState(reason, destroyGameObjects)" not in host_cleanup:
        return fail("ZombieModeRunOnlyCleanupGuard: host cleanup compatibility entry must forward to RuntimeModule")

    for signature, call in [
        ("private void RegisterZombieModeRunOnlyObject(", "zombieModeRuntimeModule.RegisterZombieModeRunOnlyObject(runId, kind, gameObject, target, cleanupAction)"),
        ("private void PruneZombieModeRunOnlyEnemyRecords(", "zombieModeRuntimeModule.PruneZombieModeRunOnlyEnemyRecords(runId)"),
        ("private void RemoveZombieModeRunOnlyObjectRecord(", "zombieModeRuntimeModule.RemoveZombieModeRunOnlyObjectRecord(target)"),
        ("private void PruneZombieModeUnknownRunOnlyRecords(", "zombieModeRuntimeModule.PruneZombieModeUnknownRunOnlyRecords()"),
    ]:
        start = cleanup_text.find(signature)
        end = cleanup_text.find("\n        }", start)
        if start < 0 or end < 0 or call not in cleanup_text[start:end]:
            return fail("ZombieModeRunOnlyCleanupGuard: host RunOnly bridge missing -> " + signature)

    cleanup_method = module_text[module_text.index("internal void CleanupZombieModeRunOnlyState"):]
    cleanup_method = cleanup_method[:cleanup_method.index("internal bool ShouldRollbackZombieModeEntryResources")]
    cleanup_order = [
        "SettleZombieModeFailureInsuranceShell(runState.RunId)",
        "RemoveZombieModeAttributeModifiers();",
        "RemoveZombieModeOptionRuntimeEffects();",
        "owner.CleanupZombieModeFortificationInteractionStateForRuntimeModule();",
        "InvalidateZombieModeRun();",
        "owner.ClearZombieModeSupportSpawnQueueForRuntimeModule();",
        # Ownership rescan must precede the destroy-all pass: pickup scans are throttled,
        # so drops picked up within the last interval are still run-only records.
        "ReleaseZombieModeOwnedDropCandidates();",
        "RunScopedRegistry.ForEachReverse(",
        "runState.RunOnlyObjects.Clear();",
        "owner.ClearZombieModeEnemyInstanceIdsForRuntimeModule();",
        "ClearZombieModeRewardShell();",
        "RestoreZombieModeMapIsolationShell();",
    ]
    positions = [cleanup_method.find(token) for token in cleanup_order]
    positions = [position for position in positions if position >= 0]
    if len(positions) != len(cleanup_order) or positions != sorted(positions):
        return fail("ZombieModeRunOnlyCleanupGuard: RuntimeModule cleanup order or RunId invalidation point changed")

    if "if (destroyGameObjects)" not in cleanup_method[:cleanup_method.find("ReleaseZombieModeOwnedDropCandidates();")]:
        return fail("ZombieModeRunOnlyCleanupGuard: ownership rescan must run on the destroy-all cleanup path")
    drops_text = Path("ZombieMode/ZombieModeDropsAndPerformance.cs").read_text(encoding="utf-8")
    release_start = drops_text.find("internal void ReleaseZombieModeOwnedDropCandidates()")
    # read_text 走通用换行，CRLF 已折成 \n；方法体以 8 空格缩进的闭括号收尾。
    release_end = drops_text.find("\n        }\n", release_start) if release_start >= 0 else -1
    release_body = drops_text[release_start:release_end] if release_start >= 0 and release_end > 0 else ""
    for snippet in [
        "ownedItem.InInventory != null || ownedItem.PluggedIntoSlot != null",
        "RemoveZombieModeRunOnlyObjectRecord(candidate.GameObject);",
        "runState.EntityDropCleanupCandidates.RemoveAt(i);",
    ]:
        if snippet not in release_body:
            return fail("ZombieModeRunOnlyCleanupGuard: ownership rescan missing snippet -> " + snippet)
    if "Destroy(" in release_body:
        return fail("ZombieModeRunOnlyCleanupGuard: ownership rescan must only release records, never destroy")

    for bridge in [
        "SettleZombieModeFailureInsuranceShell(runId);",
        "RemoveZombieModeAttributeModifiers();",
        "RemoveZombieModeOptionRuntimeEffects();",
        "CleanupZombieModeFortificationInteractionState();",
        "ClearZombieModeSupportSpawnQueue();",
        "ClearZombieModeEnemyInstanceIds();",
        "ClearZombieModeRewardShell();",
        "RestoreZombieModeMapIsolationShell();",
    ]:
        if bridge not in bridge_text:
            return fail("ZombieModeRunOnlyCleanupGuard: owner compatibility helper missing -> " + bridge)

    scene_cleanup = lifecycle_text[lifecycle_text.index("internal void CleanupZombieModeForSceneChange"):]
    scene_cleanup = scene_cleanup[:scene_cleanup.index("internal void CleanupZombieModeOnDestroy")]
    lifecycle_order = [
        "LifecyclePhase = ZombieModeLifecyclePhase.Exiting;",
        "RollbackZombieModeInventoryTransferShell();",
        "CleanupZombieModeRunOnlyState(reason, true);",
        "zombieModeRunState.ClearRuntime();",
        "LifecyclePhase = ZombieModeLifecyclePhase.None;",
        "zombieModeEntryTransaction.Reset();",
    ]
    lifecycle_positions = [scene_cleanup.find(token) for token in lifecycle_order]
    lifecycle_positions = [position for position in lifecycle_positions if position >= 0]
    if len(lifecycle_positions) != len(lifecycle_order) or lifecycle_positions != sorted(lifecycle_positions):
        return fail("ZombieModeRunOnlyCleanupGuard: scene cleanup entry order changed")

    for snippet in [
        "GrantZombieModeBeacon(int runId)",
        "ItemUtilities.SendToPlayer(beacon, true, false);",
    ]:
        if snippet not in module_text:
            return fail("ZombieModeRunOnlyCleanupGuard: beacon grant missing snippet -> " + snippet)

    for forbidden in [
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Beacon",
        "CleanupZombieModeBeaconItem",
        "DestroyZombieModeRunOnlyBeaconItem",
    ]:
        if forbidden in entry_flow_text:
            return fail("ZombieModeRunOnlyCleanupGuard: reusable beacon must not be run-only cleanup -> " + forbidden)

    extraction_text = Path("ZombieMode/ZombieModeRuntimeModule_Extraction.cs").read_text(encoding="utf-8")
    for snippet in [
        "CleanupZombieModePreparationObjects",
        "CancelZombieModeSafeZone",
        "runState.ActiveSafeZoneActive = false;",
        "runState.ActiveSafeZoneVisual = null;",
        "DestroyZombieModeSafeZoneMapPoi();",
        "runState.ActiveSafeZoneMapPoi = null;",
        "runState.ActiveExtractionArea = null;",
        "EvacuationCountdownUI.Release",
    ]:
        if snippet not in extraction_text:
            return fail("ZombieModeRunOnlyCleanupGuard: preparation cleanup missing snippet -> " + snippet)

    if "CleanupModeRuntimeOnDestroy();" not in mod_text:
        return fail("ZombieModeRunOnlyCleanupGuard: ModBehaviour missing cleanup hook -> CleanupModeRuntimeOnDestroy();")
    if "CleanupModeRuntimeForSceneLoad(scene);" not in mod_text:
        return fail("ZombieModeRunOnlyCleanupGuard: ModBehaviour missing cleanup hook -> CleanupModeRuntimeForSceneLoad(scene);")
    if "CleanupZombieModeOnDestroyRuntime();" not in mode_runtime_hooks_text:
        return fail("ZombieModeRunOnlyCleanupGuard: mode cleanup group missing hook -> CleanupZombieModeOnDestroyRuntime();")
    if "CleanupZombieModeForSceneLoad(scene);" not in mode_runtime_hooks_text:
        return fail("ZombieModeRunOnlyCleanupGuard: mode cleanup group missing hook -> CleanupZombieModeForSceneLoad(scene);")
    if "CleanupZombieModeOnDestroy();" not in zombie_runtime_hooks_text:
        return fail("ZombieModeRunOnlyCleanupGuard: ZombieMode runtime cleanup missing hook -> CleanupZombieModeOnDestroy();")
    if "CleanupZombieModeForSceneChange(ZombieModeFailureReason.SceneSwitched);" not in zombie_runtime_hooks_text:
        return fail("ZombieModeRunOnlyCleanupGuard: ZombieMode runtime cleanup missing hook -> CleanupZombieModeForSceneChange(ZombieModeFailureReason.SceneSwitched);")

    print("ZombieModeRunOnlyCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
