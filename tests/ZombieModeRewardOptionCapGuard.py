from pathlib import Path
import sys


REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    rewards = REWARDS.read_text(encoding="utf-8")

    helper = "private bool IsZombieModeRewardAtSelectionCap(ZombieModeRewardType rewardType)"
    if helper not in rewards:
        return fail("ZombieModeRewardOptionCapGuard: missing selection-cap helper")

    guard = "if (IsZombieModeRewardAtSelectionCap(rewardType))"
    guard_index = rewards.find(guard)
    if guard_index < 0:
        return fail("ZombieModeRewardOptionCapGuard: AddZombieModeRewardCatalogEntry does not skip fully-capped rewards")

    entry_index = rewards.find("ZombieModeRewardCatalogEntry entry = new ZombieModeRewardCatalogEntry();")
    if entry_index < 0 or guard_index > entry_index:
        return fail("ZombieModeRewardOptionCapGuard: cap guard must run before catalog entry creation")

    required_tokens = [
        "case ZombieModeRewardType.ProjectilePenetration:",
        "return options.ProjectilePenetrationStacks >= 3;",
        "case ZombieModeRewardType.ProjectileTrident:",
        "return options.ProjectileTridentStacks >= 1;",
        "case ZombieModeRewardType.ProjectileRicochet:",
        "return options.ProjectileRicochetStacks >= 1;",
        "case ZombieModeRewardType.MutatorBulletTime:",
        "return options.MutatorBulletTimeEnabled;",
        "case ZombieModeRewardType.MutatorGuardianShield:",
        "return options.MutatorGuardianShieldEnabled;",
        "case ZombieModeRewardType.BattlefieldAmmoRain:",
        "return options.BattlefieldAmmoRainStacks >= 2;",
    ]
    for token in required_tokens:
        if token not in rewards:
            return fail("ZombieModeRewardOptionCapGuard: missing cap token -> " + token)

    print("ZombieModeRewardOptionCapGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
