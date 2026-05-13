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
    print("ZombieModeRewardPerformanceTierGuard: FAIL - " + message)
    return 1


def main() -> int:
    effects = read_effects()

    banned = [
        "IsZombieModeTrajectorySupportAllowed",
        "IsZombieModePlayerProjectileRuntimeAllowed",
        "IsZombieModeBattlefieldGravityAllowed",
        "IsZombieModeBattlefieldGravityRuntimeAllowed",
        "ZombieModePerformanceTier",
        "SoftProtect",
        "PerformanceTier",
    ]
    for token in banned:
        if token in effects:
            return fail("reward runtime must not scale gameplay by performance tier -> " + token)

    for token in [
        "bool enableHelixRuntime = options.ProjectileHelixStacks > 0;",
        "bool enableTrailRuntime = options.ProjectileTrailStacks > 0;",
        "if (IsZombieModePlayerProjectileDamage(damageInfo))",
        "while (IsZombieModeRunValid(runId)",
        "if (inst == null || inst.ZombieModeCurrentRunId != runId)",
    ]:
        if token not in effects:
            return fail("reward runtime should stay consistently enabled when selected -> " + token)

    print("ZombieModeRewardPerformanceTierGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
