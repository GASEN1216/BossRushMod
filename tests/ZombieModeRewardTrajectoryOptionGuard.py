from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""
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
