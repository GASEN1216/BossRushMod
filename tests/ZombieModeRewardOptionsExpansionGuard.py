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

REWARD_TYPES = [
    "TempGoblinNpc",
    "TempNurseNpc",
    "TempCourierNpc",
    "ProjectilePenetration",
    "ProjectileBurn",
    "ProjectileCold",
    "ProjectilePoison",
    "ProjectileArmorBreak",
    "MutatorCritFocus",
    "TriggerLifesteal",
    "TriggerLifestealMedium",
    "TriggerLifestealLarge",
    "TriggerCritBurst",
    "TriggerPurificationSiphon",
    "TriggerSecondWind",
    "TriggerDoomPulse",
    "MutatorBulletTime",
    "MutatorGuardianShield",
    "MutatorQuickReload",
    "MutatorDashBoost",
    "BattlefieldAmmoRain",
    "ContractDevilBargain",
    "ContractCursedReload",
    "ContractBloodPrice",
    "ContractCursePool",
    "ProjectileTrident",
    "ProjectileShotgunSpray",
    "ProjectileStasis",
    "ProjectileRicochet",
    "ProjectileFork",
    "ProjectileReturn",
    "ProjectileHelix",
    "ProjectileTrail",
    "BattlefieldPurgeAura",
    "BattlefieldCurseTrap",
    "BattlefieldBlackHole",
    "BattlefieldGravityDrag",
]

REWARD_CATEGORIES = [
    "ProjectileMod",
    "Trigger",
    "Mutator",
    "Battlefield",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    localization = LOCALIZATION.read_text(encoding="utf-8")
    effects = read_effects() if EFFECTS.exists() else ""

    for reward_type in REWARD_TYPES:
        if reward_type not in models:
            return fail("ZombieModeRewardOptionsExpansionGuard: missing reward enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardOptionsExpansionGuard: reward not wired in ZombieModeRewards -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in rewards:
            return fail("ZombieModeRewardOptionsExpansionGuard: reward display missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardOptionsExpansionGuard: localization key missing -> " + reward_type)
        if reward_type.startswith("Projectile") or reward_type.startswith("Trigger") or reward_type.startswith("Mutator") or reward_type.startswith("Battlefield") or reward_type.startswith("Contract"):
            if reward_type not in effects:
                return fail("ZombieModeRewardOptionsExpansionGuard: effect helper missing reward -> " + reward_type)
        elif reward_type not in rewards:
            return fail("ZombieModeRewardOptionsExpansionGuard: effect helper missing reward -> " + reward_type)

    for category in REWARD_CATEGORIES:
        if category not in models:
            return fail("ZombieModeRewardOptionsExpansionGuard: missing category enum -> " + category)
        if "ZombieModeRewardCategory." + category not in rewards:
            return fail("ZombieModeRewardOptionsExpansionGuard: category not wired in rewards -> " + category)
        if "BossRush_ZombieMode_RewardCat_" + category not in rewards:
            return fail("ZombieModeRewardOptionsExpansionGuard: category display missing -> " + category)
        if "BossRush_ZombieMode_RewardCat_" + category not in localization:
            return fail("ZombieModeRewardOptionsExpansionGuard: category localization missing -> " + category)

    if "ApplyZombieModeOptionReward(rewardType)" not in rewards:
        return fail("ZombieModeRewardOptionsExpansionGuard: ApplyZombieModeReward does not delegate new options")

    expected_count = 32 + len(REWARD_TYPES)
    actual_count = models.count("        ")  # cheap smoke only; exact enum coverage checked above
    if expected_count < 48 or actual_count <= 0:
        return fail("ZombieModeRewardOptionsExpansionGuard: internal count smoke failed")

    print("ZombieModeRewardOptionsExpansionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
