from pathlib import Path
import sys


PAID_SWEEP = Path("Integration/NPCs/Courier/CourierPaidLootSweepService.cs")
MODEF_FORT = Path("ModeF/ModeFFortifications.cs")
ZOMBIE_CLEANUP = Path("ZombieMode/ZombieModeCleanup.cs")


def fail(message: str) -> int:
    print("ZombieModePromptAndFortificationCleanupGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def require_before(text: str, before: str, after: str, label: str) -> int:
    before_index = text.find(before)
    after_index = text.find(after)
    if before_index < 0 or after_index < 0:
        return fail("missing ordering token for " + label)
    if before_index > after_index:
        return fail("wrong ordering for " + label + ": " + before + " must appear before " + after)
    return 0


def check_prompt_method(body: str, label: str) -> int:
    if not body:
        return fail(label + " body not found")

    for snippet in [
        "bool promptOwnsState = false;",
        "TryClaimPromptState(npcTransform, promptGeneration, ref promptOwnsState)",
        "ClearPromptInProgressIfOwned(npcTransform, promptGeneration, promptOwnsState);",
    ]:
        result = require(body, snippet, label + " prompt ownership guard")
        if result:
            return result

    stale_unconditional_finally = "finally\n            {\n                sweepPromptInProgress = false;"
    if stale_unconditional_finally in body:
        return fail(label + " must not clear sweepPromptInProgress unconditionally in finally")

    stale_unconditional_catch = "catch (Exception e)\n            {\n                ModBehaviour.DevLog"
    if stale_unconditional_catch in body:
        return fail(label + " catch must guard state changes by prompt ownership")

    return 0


def main() -> int:
    paid_sweep = PAID_SWEEP.read_text(encoding="utf-8")
    modef_fort = MODEF_FORT.read_text(encoding="utf-8")
    zombie_cleanup = ZOMBIE_CLEANUP.read_text(encoding="utf-8")

    for snippet in [
        "private static bool TryClaimPromptState(Transform npcTransform, int promptGeneration, ref bool promptOwnsState)",
        "private static void ClearPromptInProgressIfOwned(Transform npcTransform, int promptGeneration, bool promptOwnsState)",
    ]:
        result = require(paid_sweep, snippet, "paid sweep prompt ownership helper")
        if result:
            return result

    for marker, label in [
        ("private static async UniTaskVoid RunPendingSweepResultPromptAsync", "pending sweep prompt"),
        ("private static async UniTaskVoid RunFreshSweepPromptAsync", "fresh sweep prompt"),
    ]:
        result = check_prompt_method(extract_method(paid_sweep, marker), label)
        if result:
            return result

    for snippet in [
        "internal void CleanupZombieModeFortificationInteractionState()",
        "private void ClearModeFFortificationHighlights()",
    ]:
        result = require(modef_fort, snippet, "zombie-mode fortification interaction cleanup helper")
        if result:
            return result

    fort_cleanup = extract_method(modef_fort, "internal void CleanupZombieModeFortificationInteractionState")
    for snippet in [
        "CancelFortPlacement();",
        "CancelModeFRepairSelection(L10n.T(\"模式结束\", \"Mode ended\"), true);",
        "ClearModeFFortificationHighlights();",
    ]:
        result = require(fort_cleanup, snippet, "zombie-mode fortification interaction cleanup action")
        if result:
            return result

    highlight_cleanup = extract_method(modef_fort, "private void ClearModeFFortificationHighlights")
    for snippet in [
        "marker.HighlightUntilTime = 0f;",
        "UnityEngine.Object.Destroy(marker.HighlightRoot);",
        "modeFHasActiveFortificationHighlight = false;",
    ]:
        result = require(highlight_cleanup, snippet, "fortification highlight cleanup")
        if result:
            return result

    run_cleanup = extract_method(zombie_cleanup, "private void CleanupZombieModeRunOnlyState")
    result = require(run_cleanup, "CleanupZombieModeFortificationInteractionState();", "zombie cleanup must clear fortification interaction state")
    if result:
        return result
    result = require_before(
        run_cleanup,
        "CleanupZombieModeFortificationInteractionState();",
        "RunScopedRegistry.ForEachReverse(",
        "interaction state cleanup before run-only object cleanup")
    if result:
        return result

    print("ZombieModePromptAndFortificationCleanupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
