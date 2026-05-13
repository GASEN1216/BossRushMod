from pathlib import Path
import sys


REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in REWARD_PARTS)

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

DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")


def fail(message: str) -> int:
    print("ZombieModeRewardLowTierOptionGuard: FAIL - " + message)
    return 1


def main() -> int:
    rewards = read_rewards()
    effects = read_effects() if EFFECTS.exists() else ""
    drops = DROPS.read_text(encoding="utf-8")

    banned = [
        "IsZombieModeRewardSuppressedByPerformance",
        "ZombieModePerformanceTier",
        "SoftProtect",
        "PerformanceTier",
        "RefreshZombieModeProjectileRewardPerformanceState",
    ]
    combined = rewards + "\n" + effects + "\n" + drops
    for token in banned:
        if token in combined:
            return fail("reward options must not change by performance tier -> " + token)

    for token in [
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileTrident",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileShotgunSpray",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileRicochet",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileFork",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileReturn",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileHelix",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ProjectileTrail",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldBlackHole",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.BattlefieldGravityDrag",
        "StartZombieModeBattlefieldGravityRuntimeIfNeeded(zombieModeRunState.RunId);",
    ]:
        if token not in combined:
            return fail("selected rewards should remain consistently available/effective -> " + token)

    print("ZombieModeRewardLowTierOptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
