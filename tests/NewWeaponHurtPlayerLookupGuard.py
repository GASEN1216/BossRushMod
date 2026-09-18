"""Guard: new weapon global hurt hooks should avoid avoidable main-player lookups."""

from pathlib import Path
import sys


ENERGY_SHIELD = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")
VIPER_DAGGER = Path("Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs")
THUNDER_RING = Path("Integration/NewWeapons/ThunderRing/ThunderRingRuntime.cs")
SUMMON_STAFF_MANAGER = Path("Integration/NewWeapons/SummonStaff/SummonStaffManager.cs")


def fail(message: str) -> int:
    print("NewWeaponHurtPlayerLookupGuard: FAIL - " + message)
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
    energy_text = ENERGY_SHIELD.read_text(encoding="utf-8-sig")
    energy_on_hurt = extract_method_body(energy_text, "private static void OnHurt(")
    if energy_on_hurt is None:
        return fail("missing EnergyShield OnHurt body")

    energy_required = [
        "if (targetHealth == null || targetHealth.IsDead || !targetHealth.IsMainCharacterHealth) return;",
        "CharacterMainControl player = CharacterMainControl.Main;",
        "if (player == null || player.Health != targetHealth) return;",
    ]
    for snippet in energy_required:
        if snippet not in energy_on_hurt:
            return fail("EnergyShield missing cheap main-health filter snippet -> " + snippet)

    if energy_on_hurt.find("!targetHealth.IsMainCharacterHealth") > energy_on_hurt.find("CharacterMainControl player = CharacterMainControl.Main;"):
        return fail("EnergyShield should reject non-main health before reading CharacterMainControl.Main")

    viper_text = VIPER_DAGGER.read_text(encoding="utf-8-sig")
    viper_on_hurt = extract_method_body(viper_text, "private static void OnHurt(")
    apply_poison = extract_method_body(viper_text, "private static void ApplyPoisonStack(")
    trigger_burst = extract_method_body(viper_text, "private static void TriggerBurst(")
    if viper_on_hurt is None:
        return fail("missing ViperDagger OnHurt body")
    if apply_poison is None:
        return fail("missing ViperDagger ApplyPoisonStack body")
    if trigger_burst is None:
        return fail("missing ViperDagger TriggerBurst body")

    viper_required = [
        "if (targetHealth == null || targetHealth.IsDead) return;",
        "CharacterMainControl player = CharacterMainControl.Main;",
        # 已解析好的 player 必须一路传下去，不许在下游再读一次 CharacterMainControl.Main。
        # 只钉「形参里带 CharacterMainControl player」，不钉整条签名——
        # 否则每加一个数值参数就要改守卫，而不变式本身没变。
        "ApplyPoisonStack(targetId, targetHealth, player",
        "private static void ApplyPoisonStack(int targetId, Health targetHealth, CharacterMainControl player",
        "TriggerBurst(targetHealth, player",
        "private static void TriggerBurst(Health targetHealth, CharacterMainControl player",
    ]
    for snippet in viper_required:
        if snippet not in viper_text:
            return fail("ViperDagger missing player reuse snippet -> " + snippet)

    if viper_on_hurt.find("targetHealth == null") > viper_on_hurt.find("CharacterMainControl player = CharacterMainControl.Main;"):
        return fail("ViperDagger should reject invalid targets before reading CharacterMainControl.Main")

    if "CharacterMainControl player = CharacterMainControl.Main;" in trigger_burst:
        return fail("ViperDagger burst still rereads CharacterMainControl.Main")

    # ---- 装备判定必须走共享缓存，不许在受击回调里遍历槽位（AGENTS §4.12）----
    # 三个运行时此前各写一份「遍历 CharacterItem.Slots 找 Totem*」/「GetMeleeWeapon + CurrentHoldItemAgent」，
    # 既重复又是每次受击一次线性扫描。现在统一读 NewWeaponEquipState 的事件驱动缓存。
    thunder_text = THUNDER_RING.read_text(encoding="utf-8-sig")
    staff_text = SUMMON_STAFF_MANAGER.read_text(encoding="utf-8-sig")

    for label, text, expected in (
        ("EnergyShield", energy_text,
         "NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.EnergyShieldTypeId)"),
        ("ThunderRing", thunder_text,
         "NewWeaponEquipState.IsTotemEquipped(NewWeaponIds.ThunderRingTypeId)"),
        ("ViperDagger", viper_text,
         "NewWeaponEquipState.IsHolding(NewWeaponIds.ViperDaggerTypeId)"),
        ("SummonStaff", staff_text,
         "NewWeaponEquipState.IsHolding(NewWeaponIds.SummonStaffTypeId)"),
    ):
        if expected not in text:
            return fail(label + " must read the shared equip cache -> " + expected)
        if 'StartsWith("Totem")' in text or "CharacterItem.Slots" in text:
            return fail(label + " still scans totem slots inline; use NewWeaponEquipState")

    print("NewWeaponHurtPlayerLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
