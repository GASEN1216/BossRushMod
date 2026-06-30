"""Guard: new weapon global hurt hooks should avoid avoidable main-player lookups."""

from pathlib import Path
import sys


ENERGY_SHIELD = Path("Integration/NewWeapons/EnergyShield/EnergyShieldRuntime.cs")
VIPER_DAGGER = Path("Integration/NewWeapons/ViperDagger/ViperDaggerRuntime.cs")


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
        "if (targetHealth == null || !targetHealth.IsMainCharacterHealth) return;",
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
        "ApplyPoisonStack(targetId, targetHealth, player);",
        "private static void ApplyPoisonStack(int targetId, Health targetHealth, CharacterMainControl player)",
        "TriggerBurst(targetHealth, player);",
        "private static void TriggerBurst(Health targetHealth, CharacterMainControl player)",
    ]
    for snippet in viper_required:
        if snippet not in viper_text:
            return fail("ViperDagger missing player reuse snippet -> " + snippet)

    if viper_on_hurt.find("targetHealth == null") > viper_on_hurt.find("CharacterMainControl player = CharacterMainControl.Main;"):
        return fail("ViperDagger should reject invalid targets before reading CharacterMainControl.Main")

    if "CharacterMainControl player = CharacterMainControl.Main;" in trigger_burst:
        return fail("ViperDagger burst still rereads CharacterMainControl.Main")

    print("NewWeaponHurtPlayerLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
