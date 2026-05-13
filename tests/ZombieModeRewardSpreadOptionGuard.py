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
PATCH = Path("ZombieMode/ZombieModeRewardProjectilePatch.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    effects = read_effects() if EFFECTS.exists() else ""
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
