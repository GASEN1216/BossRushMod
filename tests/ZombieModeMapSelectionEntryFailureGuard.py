"""ZombieModeMapSelectionEntryFailureGuard: pre-map failures must not reload base.

The zombie map selection flow disables the vanilla MapSelectionEntry behavior and
runs its own Phase 1 checks before calling SceneLoader. If those checks fail
before the target map is loaded, the player should get a failure message and
stay in the current scene instead of being forced through LoadBaseScene.

Item transfer must not block map selection. Carried/equipped items are moved to
storage after the target map is loaded, so quest/bound/run-only tags must not be
checked before opening or confirming the map UI.
"""

from pathlib import Path
import re
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


def fail(message: str) -> int:
    print(message)
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


def main() -> int:
    entry = ENTRY.read_text(encoding="utf-8")

    can_start = extract_method(entry, "public bool CanStartZombieModeMapSelectionPhase1")
    if not can_start:
        return fail("ZombieModeMapSelectionEntryFailureGuard: CanStartZombieModeMapSelectionPhase1 not found")

    if "TryGetZombieModeBlockedTransferMessage" in can_start:
        return fail(
            "ZombieModeMapSelectionEntryFailureGuard: item transfer blockers must not run before opening map UI"
        )

    fail_method = extract_method(entry, "private void FailZombieModeBeforeActive")
    if not fail_method:
        return fail("ZombieModeMapSelectionEntryFailureGuard: FailZombieModeBeforeActive not found")

    if "ShouldReturnToBaseAfterZombieModePreActiveFailure" not in fail_method:
        return fail(
            "ZombieModeMapSelectionEntryFailureGuard: pre-active failure must gate LoadBaseScene behind scene-state check"
        )

    load_base_index = fail_method.find("LoadBaseScene")
    gate_index = fail_method.find("ShouldReturnToBaseAfterZombieModePreActiveFailure")
    if load_base_index < 0:
        return fail("ZombieModeMapSelectionEntryFailureGuard: target-map startup failures still need LoadBaseScene fallback")
    if gate_index < 0 or gate_index > load_base_index:
        return fail("ZombieModeMapSelectionEntryFailureGuard: LoadBaseScene must be gated before it is called")

    helper = extract_method(entry, "private bool ShouldReturnToBaseAfterZombieModePreActiveFailure")
    if not helper:
        return fail("ZombieModeMapSelectionEntryFailureGuard: return-to-base helper not found")

    for phase in [
        "ZombieModeLifecyclePhase.Prechecking",
        "ZombieModeLifecyclePhase.CommittingResources",
        "ZombieModeLifecyclePhase.LoadingMap",
    ]:
        if phase not in helper:
            return fail("ZombieModeMapSelectionEntryFailureGuard: missing pre-map phase -> " + phase)
    if "return false;" not in helper:
        return fail("ZombieModeMapSelectionEntryFailureGuard: pre-map phases must return false")

    for phase in [
        "ZombieModeLifecyclePhase.InitializingRun",
        "ZombieModeLifecyclePhase.WaitingStarterChoice",
    ]:
        if phase not in helper:
            return fail("ZombieModeMapSelectionEntryFailureGuard: missing target-map phase -> " + phase)
    if "return true;" not in helper:
        return fail("ZombieModeMapSelectionEntryFailureGuard: target-map startup phases must return true")

    precheck = extract_method(entry, "private bool TryRunZombieModePrechecks")
    if not precheck:
        return fail("ZombieModeMapSelectionEntryFailureGuard: TryRunZombieModePrechecks not found")

    for token in ["TryGetZombieModeBlockedTransferMessage", "BlockedTaskOrBoundItems"]:
        if token in precheck:
            return fail(
                "ZombieModeMapSelectionEntryFailureGuard: item transfer blockers must not run during entry prechecks -> "
                + token
            )

    print("ZombieModeMapSelectionEntryFailureGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
