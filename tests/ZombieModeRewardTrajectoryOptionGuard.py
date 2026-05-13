from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
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

LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    effects = read_effects() if EFFECTS.exists() else ""
    localization = LOCALIZATION.read_text(encoding="utf-8")

    reward_types = [
        "ProjectileRicochet",
        "ProjectileFork",
        "ProjectileReturn",
        "ProjectileHelix",
        "ProjectileTrail",
    ]
    for reward_type in reward_types:
        if reward_type not in models:
            return fail("ZombieModeRewardTrajectoryOptionGuard: missing enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardTrajectoryOptionGuard: reward not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardTrajectoryOptionGuard: localization missing -> " + reward_type)

    for token in [
        "ProjectileRicochetStacks",
        "ProjectileForkStacks",
        "ProjectileReturnStacks",
        "ProjectileHelixStacks",
        "ProjectileTrailStacks",
        "ZombieModePlayerProjectileRuntime",
        "TrySpawnZombieModePlayerSupportProjectile",
        "TryFindZombieModeNearestEnemyTarget",
        "options.ProjectileHelixStacks <= 0",
        "options.ProjectileTrailStacks <= 0",
        "GameplayDataSettings.Prefabs.DefaultBullet",
        "ctx.fromWeaponItemID = 0",
        "CanTriggerZombieModeProjectileTrailDamage(runId)",
        "LastProjectileTrailDamageTime",
    ]:
        if token not in effects:
            return fail("ZombieModeRewardTrajectoryOptionGuard: missing token -> " + token)

    print("ZombieModeRewardTrajectoryOptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
