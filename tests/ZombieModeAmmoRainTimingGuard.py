from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
EFFECT_PARTS = [
    EFFECTS,
    Path("ZombieMode/ZombieModeRewardOptionCore.cs"),
    Path("ZombieMode/ZombieModeRewardProjectileSpread.cs"),
    Path("ZombieMode/ZombieModeRewardRuntimeModifiers.cs"),
    Path("ZombieMode/ZombieModeRewardTriggerEffects.cs"),
]


def read_effects() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in EFFECT_PARTS)



def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    if not EFFECTS.exists():
        return fail("ZombieModeAmmoRainTimingGuard: missing ZombieModeRewardEffects.cs")

    effects = read_effects()

    required_tokens = [
        "float nextGrantDelay = stacks >= 2 ? 35f : 45f;",
        "float countdownRemaining = nextGrantDelay;",
        "nextGrantDelay = stacks >= 2 ? 35f : 45f;",
        "countdownRemaining = Mathf.Min(countdownRemaining, nextGrantDelay);",
        "countdownRemaining -= deltaTime;",
        "countdownRemaining = nextGrantDelay;",
        "phase == ZombieModeCombatPhase.InitialPreparation",
        "phase == ZombieModeCombatPhase.Preparation",
        "phase == ZombieModeCombatPhase.ExtractionOpportunity",
        "phase == ZombieModeCombatPhase.Combat",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("ZombieModeAmmoRainTimingGuard: missing token -> " + token)

    banned_tokens = [
        "float nextGrantTime = GetZombieModeRuntimeNow() + 1f;",
        "countdownRemaining -= deltaTime;\n                if (IsZombieModeRuntimePaused())",
    ]
    for token in banned_tokens:
        if token in effects:
            return fail("ZombieModeAmmoRainTimingGuard: banned timer behavior -> " + token)

    phase_guard = "if (!allowedPhase)\n                {\n                    yield return null;\n                    continue;\n                }"
    phase_guard_index = effects.find(phase_guard)
    countdown_index = effects.find("countdownRemaining -= deltaTime;")
    if phase_guard_index < 0:
        return fail("ZombieModeAmmoRainTimingGuard: missing disallowed-phase guard")
    if countdown_index < 0:
        return fail("ZombieModeAmmoRainTimingGuard: missing countdown consumption")
    if countdown_index < phase_guard_index:
        return fail("ZombieModeAmmoRainTimingGuard: disallowed phases still leak timer progression")

    print("ZombieModeAmmoRainTimingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
