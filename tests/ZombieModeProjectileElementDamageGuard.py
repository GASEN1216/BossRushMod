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
    print("ZombieModeProjectileElementDamageGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not EFFECTS.exists():
        return fail("missing ZombieModeRewardEffects.cs")

    effects = read_effects()
    required_tokens = [
        "private bool TryApplyZombieModeElementalProjectileEffect(ref ProjectileContext context",
        "context.element_Fire",
        "context.element_Poison",
        "context.element_Ice",
        "UnityEngine.Random.value",
        "context.buff = buff",
        "context.buffChance = 1f",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("missing token -> " + token)

    if "SelectZombieModeElementalProjectileBuff(" in effects:
        return fail("element options must not only select a buff without writing ProjectileContext element fields")

    print("ZombieModeProjectileElementDamageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
