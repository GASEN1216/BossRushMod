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

    EQUIP_CHECK = "if (!NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.ThunderRingTypeId))"
    ATTRIBUTION = "if (!NewWeaponAttribution.IsPlayerDirectHit(targetHealth, ref damageInfo, player)) return;"

    attack_required = [
        "if (currentCharges <= 0) return;",
        "if (currentCharges < ThunderRingConfig.MaxCharges) return;",
        ATTRIBUTION,
        EQUIP_CHECK,
    ]
    for snippet in attack_required:
        if snippet not in attack_body:
            return fail("missing Thunder Ring attack hot-path snippet -> " + snippet)

    first_charge_exit = attack_body.find("if (currentCharges <= 0) return;")
    max_charge_exit = attack_body.find("if (currentCharges < ThunderRingConfig.MaxCharges) return;")
    attribution_check = attack_body.find(ATTRIBUTION)
    equip_check = attack_body.find(EQUIP_CHECK)
    # 层数判定必须排在归因与装备判定之前：没攒到层就不该为它们付一分钱
    if first_charge_exit < 0 or attribution_check < 0 or first_charge_exit > attribution_check:
        return fail("Thunder Ring attack should skip attribution work when no charges exist")
    if max_charge_exit < 0 or max_charge_exit > attribution_check:
        return fail("Thunder Ring attack should skip attribution work until charges are full")
    if equip_check < attribution_check:
        return fail("Thunder Ring attack should attribute the hit before reading the equip cache")

    # 装备判定必须走共享的事件驱动缓存：本文件不得再自己遍历图腾槽，
    # 也不得回退到「每帧缓存一次」那版——那仍然是每帧一次线性扫描。
    if "IsEquippingThunderRing" in text or "CharacterItem.Slots" in text or 'StartsWith("Totem")' in text:
        return fail("Thunder Ring must not scan totem slots itself; use NewWeaponEquipState")

    equip_state = Path("Integration/NewWeapons/Common/NewWeaponEquipState.cs").read_text(encoding="utf-8-sig")
    equip_cache_required = [
        "private static bool isDirty = true;",
        "private static void EnsureFresh()",
        "if (!isDirty && ReferenceEquals(player, cachedPlayer))",
        "internal static bool IsTotemEquipped(int typeId)",
    ]
    for snippet in equip_cache_required:
        if snippet not in equip_state:
            return fail("missing shared equip-state cache snippet -> " + snippet)

    unsubscribe_body = extract_method_body(text, "public static void Unsubscribe(")
    if unsubscribe_body is None:
        return fail("missing Unsubscribe body")
    if "ResetStaticCaches();" not in unsubscribe_body:
        return fail("Thunder Ring unsubscribe must clear equip-check cache references")

    print("ThunderRingHurtPrefilterGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
