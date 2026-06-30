"""Guard: Thunder Ring global hurt hook should cheaply skip unrelated events."""

from pathlib import Path
import sys


SOURCE = Path("Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs")


def fail(message: str) -> int:
    print("ThunderRingHurtPrefilterGuard: FAIL - " + message)
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
    text = SOURCE.read_text(encoding="utf-8-sig")
    body = extract_method_body(text, "private static void OnHurt(")
    if body is None:
        return fail("missing OnHurt body")

    required = [
        "bool isPlayerHurt = targetHealth.IsMainCharacterHealth;",
        "bool hasAttacker = damageInfo.fromCharacter != null;",
        "if (!isPlayerHurt && !hasAttacker) return;",
        "CharacterMainControl player = CharacterMainControl.Main;",
        "if (isPlayerHurt && player.Health == targetHealth)",
        "if (hasAttacker && damageInfo.fromCharacter == player)",
    ]
    for snippet in required:
        if snippet not in body:
            return fail("missing Thunder Ring hurt prefilter snippet -> " + snippet)

    prefilter_index = body.find("if (!isPlayerHurt && !hasAttacker) return;")
    main_index = body.find("CharacterMainControl player = CharacterMainControl.Main;")
    if prefilter_index < 0 or main_index < 0 or prefilter_index > main_index:
        return fail("Thunder Ring should run unrelated-event prefilter before CharacterMainControl.Main")

    attack_body = extract_method_body(text, "private static void HandlePlayerAttack(")
    if attack_body is None:
        return fail("missing HandlePlayerAttack body")

    attack_required = [
        "if (currentCharges <= 0) return;",
        "if (currentCharges < ThunderRingConfig.MaxCharges) return;",
        "if (!IsEquippingThunderRing(player))",
    ]
    for snippet in attack_required:
        if snippet not in attack_body:
            return fail("missing Thunder Ring attack hot-path snippet -> " + snippet)

    first_charge_exit = attack_body.find("if (currentCharges <= 0) return;")
    max_charge_exit = attack_body.find("if (currentCharges < ThunderRingConfig.MaxCharges) return;")
    equip_check = attack_body.find("if (!IsEquippingThunderRing(player))")
    if first_charge_exit < 0 or equip_check < 0 or first_charge_exit > equip_check:
        return fail("Thunder Ring attack should skip equip scanning when no charges exist")
    if max_charge_exit < 0 or max_charge_exit > equip_check:
        return fail("Thunder Ring attack should skip equip scanning until charges are full")

    equip_body = extract_method_body(text, "private static bool IsEquippingThunderRing(")
    if equip_body is None:
        return fail("missing IsEquippingThunderRing body")

    equip_cache_required = [
        "cachedEquipCheckFrame == frame && cachedEquipCheckPlayer == player",
        "return cachedEquipCheckResult;",
        "cachedEquipCheckFrame = frame;",
        "cachedEquipCheckPlayer = player;",
        "cachedEquipCheckResult = isEquipped;",
    ]
    for snippet in equip_cache_required:
        if snippet not in equip_body:
            return fail("missing Thunder Ring same-frame equip cache snippet -> " + snippet)

    unsubscribe_body = extract_method_body(text, "public static void Unsubscribe(")
    if unsubscribe_body is None:
        return fail("missing Unsubscribe body")
    if "ResetStaticCaches();" not in unsubscribe_body:
        return fail("Thunder Ring unsubscribe must clear equip-check cache references")

    print("ThunderRingHurtPrefilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
