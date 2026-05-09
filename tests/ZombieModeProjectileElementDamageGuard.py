from pathlib import Path
import sys


EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print("ZombieModeProjectileElementDamageGuard: FAIL - " + message)
    return 1


def main() -> int:
    if not EFFECTS.exists():
        return fail("missing ZombieModeRewardEffects.cs")

    effects = EFFECTS.read_text(encoding="utf-8")
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
