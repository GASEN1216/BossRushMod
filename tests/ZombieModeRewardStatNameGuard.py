from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""

    for snippet in [
        'public const string GunCritRateGain = "GunCritRateGain";',
        'public const string ReloadSpeedGain = "ReloadSpeedGain";',
        'public const string DashSpeed = "DashSpeed";',
        'public const string ElementFactorPhysics = "ElementFactor_Physics";',
    ]:
        if snippet not in models:
            return fail("ZombieModeRewardStatNameGuard: missing stat name -> " + snippet)

    if not effects:
        return fail("ZombieModeRewardStatNameGuard: missing ZombieModeRewardEffects.cs")

    required_effect_tokens = [
        "ZombieModeStatNames.GunCritRateGain",
        "ZombieModeStatNames.ReloadSpeedGain",
        "ZombieModeStatNames.DashSpeed",
        "ZombieModeStatNames.ElementFactorPhysics",
        "ModifierType.Add",
        "ModifierType.PercentageAdd",
        "new Modifier(ModifierType.Add",
        "RuntimeStatModifierTracker.TryAdd",
    ]
    for token in required_effect_tokens:
        if token not in effects:
            return fail("ZombieModeRewardStatNameGuard: missing effect token -> " + token)

    if "ReloadSpeedMultiplier" in effects:
        return fail("ZombieModeRewardStatNameGuard: option rewards must not use ReloadSpeedMultiplier")

    if "MutatorQuickReload" in effects and "ZombieModeStatNames.ReloadSpeedGain" not in effects:
        return fail("ZombieModeRewardStatNameGuard: quick reload not wired to ReloadSpeedGain")

    print("ZombieModeRewardStatNameGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
