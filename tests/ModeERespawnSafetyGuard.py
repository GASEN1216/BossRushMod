"""
Guard: Mode E respawn consumables must be gated by one active spawn task and
an alive/pending Boss pressure cap. This prevents stacked fire-and-forget
respawn jobs from multiplying AI, health bars, lootboxes, and death callbacks.
"""

from pathlib import Path
import sys


RESPAWN = Path("ModeE/ModeERespawnItems.cs")
USAGE = Path("ModeE/RespawnItemUsage.cs")


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
    respawn_text = RESPAWN.read_text(encoding="utf-8")
    usage_text = USAGE.read_text(encoding="utf-8")

    for required in (
        "MODE_E_RESPAWN_ALIVE_BOSS_LIMIT",
        "modeERespawnTaskRunning",
        "GetModeERespawnAvailableSpawnSlots",
        "CanUseModeERespawnItem",
        "TryStartModeERespawn",
    ):
        if required not in respawn_text:
            return fail(f"ModeERespawnSafetyGuard: missing respawn safety invariant -> {required}")

    start_body = extract_method_body(respawn_text, "private bool TryStartModeERespawn")
    if start_body is None:
        return fail("ModeERespawnSafetyGuard: missing TryStartModeERespawn body")

    for required in (
        "CanUseModeERespawnItem(true)",
        "GetModeERespawnAvailableSpawnSlots()",
        "modeERespawnTaskRunning = true",
        "RespawnBossesAtPoints(",
    ):
        if required not in start_body:
            return fail(f"ModeERespawnSafetyGuard: TryStartModeERespawn lacks -> {required}")

    respawn_body = extract_method_body(respawn_text, "private async UniTaskVoid RespawnBossesAtPoints")
    if respawn_body is None:
        return fail("ModeERespawnSafetyGuard: missing RespawnBossesAtPoints body")

    for required in (
        "finally",
        "modeERespawnTaskRunning = false",
        "IsModeESessionStillValid(modeESessionToken, relatedScene)",
    ):
        if required not in respawn_body:
            return fail(f"ModeERespawnSafetyGuard: RespawnBossesAtPoints lacks -> {required}")

    can_use_body = extract_method_body(usage_text, "public override bool CanBeUsed(Item item, object user)")
    if can_use_body is None:
        return fail("ModeERespawnSafetyGuard: missing RespawnItemUsage.CanBeUsed body")

    for required in (
        "RespawnItemConfig.TAUNT_SMOKE_TYPE_ID",
        "RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID",
        "inst.CanQueryUseModeERespawnItem()",
        "CanShowModeERespawnItemRejectMessage()",
        "inst.CanUseModeERespawnItem(true)",
    ):
        if required not in can_use_body:
            return fail(f"ModeERespawnSafetyGuard: CanBeUsed lacks respawn gate -> {required}")

    for forbidden in (
        "NotificationText.Push",
        "RejectModeERespawnItemUse",
    ):
        if forbidden in can_use_body:
            return fail(f"ModeERespawnSafetyGuard: CanBeUsed still has side effects or heavy exact checks -> {forbidden}")

    on_use_body = extract_method_body(usage_text, "protected override void OnUse(Item item, object user)")
    if on_use_body is None:
        return fail("ModeERespawnSafetyGuard: missing RespawnItemUsage.OnUse body")

    for required in (
        "!inst.IsModeEActive",
        "NotificationText.Push",
        "inst.CanUseModeERespawnItem(true)",
    ):
        if required not in on_use_body:
            return fail(f"ModeERespawnSafetyGuard: OnUse lacks user-facing failure guard -> {required}")

    print("ModeERespawnSafetyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
