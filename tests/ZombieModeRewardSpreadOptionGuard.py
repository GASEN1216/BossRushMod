from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")
PATCH = Path("ZombieMode/ZombieModeRewardProjectilePatch.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""
    localization = LOCALIZATION.read_text(encoding="utf-8")
    patch = PATCH.read_text(encoding="utf-8")

    for reward_type in ["ProjectileTrident", "ProjectileShotgunSpray"]:
        if reward_type not in models:
            return fail("ZombieModeRewardSpreadOptionGuard: missing enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardSpreadOptionGuard: reward not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardSpreadOptionGuard: localization missing -> " + reward_type)

    for token in [
        "ProjectileTridentStacks",
        "ProjectileShotgunSprayStacks",
        "RebuildZombieModeProjectileSpreadState",
        "RestoreZombieModeProjectileSpreadState",
        "CurrentHoldItemAgent",
    ]:
        if token not in effects:
            return fail("ZombieModeRewardSpreadOptionGuard: missing token -> " + token)

    if "UpdateMoveAndCheck" in patch or "UpdateMoveAndCheck" in effects:
        return fail("ZombieModeRewardSpreadOptionGuard: this phase must not patch Projectile.UpdateMoveAndCheck")

    print("ZombieModeRewardSpreadOptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
