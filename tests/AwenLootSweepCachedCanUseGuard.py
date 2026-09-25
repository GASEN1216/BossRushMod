"""
Guard: Awen sweep token availability checks should use the short-lived cached
target count, while actual activation still performs a fresh target collection.
"""

from pathlib import Path
import sys
from cs_source_util import clean_source


SOURCE = Path("LootAndRewards/AwenLootSweepRuntime.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def main() -> int:
    text = clean_source(SOURCE.read_text(encoding="utf-8"))
    host = clean_source(Path("LootAndRewards/ModeEFLootboxTracker.cs").read_text(encoding="utf-8"))
    registration = clean_source(Path("ModBehaviourRuntimeModules.cs").read_text(encoding="utf-8"))
    if host.count("new AwenLootSweepRuntime()") != 1 or "private int modeEFBossDeathGrantCounter" in host:
        return fail("AwenLootSweepCachedCanUseGuard: host must delegate state to one sweep runtime")
    bind = extract_method_body(host, "private void BindAwenLootSweepRuntime()") or ""
    for statement in ("awenLootSweepRuntime.BindServices(TryGetActiveModeEFLootboxContext, CanUseAwenLootSweepInCurrentMode,",
                      "IsAwenLootSweepSessionStillValid, () => courierNPCInstance, () => courierController,",
                      "modeFRuntime.TryGiveItemToPlayerOrDrop(typeId, name, bubble, drop)"):
        if statement not in bind:
            return fail("AwenLootSweepCachedCanUseGuard: missing runtime binding -> " + statement)
    if "BindAwenLootSweepRuntime();" not in registration:
        return fail("AwenLootSweepCachedCanUseGuard: missing startup binding")
    for signature, call in (
        ("internal void ResetModeEFLootboxTrackerState()", "awenLootSweepRuntime.ResetModeEFLootboxTrackerState();"),
        ("internal bool TryActivateAwenLootSweepToken", "return awenLootSweepRuntime.TryActivateAwenLootSweepToken(player);"),
        ("internal bool TryRefundAwenLootSweepToken()", "return awenLootSweepRuntime.TryRefundAwenLootSweepToken();"),
    ):
        if call not in (extract_method_body(host, signature) or ""):
            return fail("AwenLootSweepCachedCanUseGuard: missing host forward -> " + call)

    can_use_body = extract_method_body(text, "internal bool CanUseAwenLootSweepToken(CharacterMainControl player, bool showFailureFeedback)")
    if can_use_body is None:
        return fail("AwenLootSweepCachedCanUseGuard: missing CanUseAwenLootSweepToken body")
    if "GetCurrentAwenLootSweepTargetCount()" not in can_use_body:
        return fail("AwenLootSweepCachedCanUseGuard: CanUseAwenLootSweepToken does not use cached target count")
    if "CopyFreshAwenLootSweepTargets(" in can_use_body:
        return fail("AwenLootSweepCachedCanUseGuard: CanUseAwenLootSweepToken still performs fresh O(n^2) target collection")

    activate_body = extract_method_body(text, "internal bool TryActivateAwenLootSweepToken")
    if activate_body is None:
        return fail("AwenLootSweepCachedCanUseGuard: missing TryActivateAwenLootSweepToken body")
    if "CopyFreshAwenLootSweepTargets(" not in activate_body:
        return fail("AwenLootSweepCachedCanUseGuard: activation no longer performs fresh target collection")

    print("AwenLootSweepCachedCanUseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
