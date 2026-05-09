"""ModeERespawnItemFeedbackGuard: unavailable Mode E respawn items give feedback without being consumed."""

from pathlib import Path


USAGE = Path("ModeE/RespawnItemUsage.cs")


def fail(message: str) -> int:
    print("ModeERespawnItemFeedbackGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str:
    start = text.find(signature)
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
                return text[brace:index + 1]
    return ""


def main() -> int:
    text = USAGE.read_text(encoding="utf-8")
    can_be_used_body = extract_method_body(text, "public override bool CanBeUsed(Item item, object user)")
    if not can_be_used_body:
        return fail("missing CanBeUsed body")

    can_be_used_required = [
        "RespawnItemConfig.TAUNT_SMOKE_TYPE_ID",
        "RespawnItemConfig.CHAOS_DETONATOR_TYPE_ID",
        "inst.CanQueryUseModeERespawnItem()",
        "CanShowModeERespawnItemRejectMessage()",
        "inst.CanUseModeERespawnItem(true)",
    ]
    for token in can_be_used_required:
        if token not in can_be_used_body:
            return fail("CanBeUsed missing pure eligibility token: " + token)

    can_be_used_forbidden = [
        "RejectModeERespawnItemUse",
        "NotificationText.Push",
        "inst.CanUseModeERespawnItem(false)",
    ]
    for token in can_be_used_forbidden:
        if token in can_be_used_body:
            return fail("CanBeUsed still performs feedback or exact-use work: " + token)

    on_use_body = extract_method_body(text, "protected override void OnUse(Item item, object user)")
    if not on_use_body:
        return fail("missing OnUse body")

    on_use_required = [
        "!inst.IsModeEActive",
        "NotificationText.Push",
        "inst.CanUseModeERespawnItem(true)",
    ]
    for token in on_use_required:
        if token not in on_use_body:
            return fail("OnUse missing user-facing failure token: " + token)

    respawn_items = Path("ModeE/ModeERespawnItems.cs").read_text(encoding="utf-8")
    can_use_body = extract_method_body(respawn_items, "internal bool CanUseModeERespawnItem(bool showFailureFeedback)")
    if not can_use_body:
        return fail("missing CanUseModeERespawnItem body")
    if "!modeEActive" not in can_use_body or "showFailureFeedback" not in can_use_body:
        return fail("CanUseModeERespawnItem must expose non-ModeE failure feedback for CanBeUsed")

    print("ModeERespawnItemFeedbackGuard: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
