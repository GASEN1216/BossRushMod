from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
MOD_BEHAVIOUR = Path("ModBehaviour.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    model_text = MODELS.read_text(encoding="utf-8")
    cleanup_text = CLEANUP.read_text(encoding="utf-8")
    entry_text = ENTRY.read_text(encoding="utf-8")
    mod_text = MOD_BEHAVIOUR.read_text(encoding="utf-8")

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
        "StartZombieModeCoroutine",
        "InvalidateZombieModeRun()",
        "CleanupZombieModeRunOnlyState",
        "CleanupZombieModeForSceneChange",
        "CleanupZombieModeOnDestroy",
        "zombieModeRunState.RunOnlyObjects.Clear();",
        "zombieModeEntryTransaction.Reset();",
    ]:
        if snippet not in cleanup_text:
            return fail("ZombieModeRunOnlyCleanupGuard: cleanup missing snippet -> " + snippet)

    for snippet in [
        "GrantZombieModeBeacon(int runId)",
        "ItemUtilities.SendToPlayer(beacon, true, false);",
    ]:
        if snippet not in entry_text:
            return fail("ZombieModeRunOnlyCleanupGuard: beacon grant missing snippet -> " + snippet)

    for forbidden in [
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.Beacon",
        "CleanupZombieModeBeaconItem",
        "DestroyZombieModeRunOnlyBeaconItem",
    ]:
        if forbidden in entry_text:
            return fail("ZombieModeRunOnlyCleanupGuard: reusable beacon must not be run-only cleanup -> " + forbidden)

    extraction_text = Path("ZombieMode/ZombieModeExtractionController.cs").read_text(encoding="utf-8")
    for snippet in [
        "CleanupZombieModePreparationObjects",
        "BreakZombieModeSafeZoneStealth",
        "zombieModeRunState.ActiveSafeZoneActive = false;",
        "zombieModeRunState.ActiveSafeZoneVisual = null;",
        "DestroyZombieModeSafeZoneMapPoi();",
        "zombieModeRunState.ActiveSafeZoneMapPoi = null;",
        "zombieModeRunState.ActiveExtractionArea = null;",
        "EvacuationCountdownUI.Release",
    ]:
        if snippet not in extraction_text:
            return fail("ZombieModeRunOnlyCleanupGuard: preparation cleanup missing snippet -> " + snippet)

    for snippet in [
        "CleanupZombieModeOnDestroy();",
        "CleanupZombieModeForSceneChange(ZombieModeFailureReason.SceneSwitched);",
    ]:
        if snippet not in mod_text:
            return fail("ZombieModeRunOnlyCleanupGuard: ModBehaviour missing cleanup hook -> " + snippet)

    print("ZombieModeRunOnlyCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
