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


TRADEOFF_REWARDS = [
    "ProjectilePenetration",
    "ProjectileBurn",
    "ProjectileCold",
    "ProjectilePoison",
    "ProjectileArmorBreak",
    "TriggerLifesteal",
    "TriggerLifestealMedium",
    "TriggerLifestealLarge",
    "TriggerCritBurst",
    "TriggerPurificationSiphon",
    "TriggerSecondWind",
    "TriggerDoomPulse",
    "MutatorCritFocus",
    "MutatorBulletTime",
    "MutatorGuardianShield",
    "MutatorQuickReload",
    "MutatorDashBoost",
    "BattlefieldAmmoRain",
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

TRADEOFF_EXPECTATIONS = {
    "ProjectilePenetration": "代价：换弹速度",
    "ProjectileBurn": "代价：枪械伤害",
    "ProjectileCold": "代价：换弹速度",
    "ProjectilePoison": "代价：最大生命",
    "ProjectileArmorBreak": "代价：承受伤害",
    "TriggerLifesteal": "代价：移动速度",
    "TriggerLifestealMedium": "代价：移动速度",
    "TriggerLifestealLarge": "代价：移动速度",
    "TriggerCritBurst": "代价：承受伤害",
    "TriggerPurificationSiphon": "代价：污染",
    "TriggerSecondWind": "代价：最大生命",
    "TriggerDoomPulse": "代价：承受伤害",
    "MutatorCritFocus": "代价：换弹速度",
    "MutatorBulletTime": "代价：承受伤害",
    "MutatorGuardianShield": "代价：枪械伤害",
    "MutatorQuickReload": "代价：枪械伤害",
    "MutatorDashBoost": "代价：枪械伤害",
    "BattlefieldAmmoRain": "代价：净化点",
    "ProjectileTrident": "代价：换弹速度",
    "ProjectileShotgunSpray": "代价：枪械伤害",
    "ProjectileStasis": "代价：移动速度",
    "ProjectileRicochet": "代价：换弹速度",
    "ProjectileFork": "代价：枪械伤害",
    "ProjectileReturn": "代价：最大生命",
    "ProjectileHelix": "代价：移动速度",
    "ProjectileTrail": "代价：承受伤害",
    "BattlefieldPurgeAura": "代价：污染",
    "BattlefieldCurseTrap": "代价：最大生命",
    "BattlefieldBlackHole": "代价：净化点",
    "BattlefieldGravityDrag": "代价：移动速度",
}


def fail(message: str) -> int:
    print("ZombieModeRewardTradeoffGuard: FAIL - " + message)
    return 1


def main() -> int:
    for path in [MODELS, REWARDS, EFFECTS, LOCALIZATION]:
        if not path.exists():
            return fail("missing file -> " + str(path))

    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    effects = read_effects()
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for token in [
        "OptionTradeoffMoveSpeedPenalty",
        "OptionTradeoffGunDamagePenalty",
        "OptionTradeoffReloadSpeedPenalty",
        "OptionTradeoffDamageTakenPenalty",
        "OptionTradeoffMaxHealthPenalty",
        "ApplyZombieModeOptionTradeoff(rewardType)",
        "GetZombieModeOptionTradeoffMoveSpeedPenalty",
        "GetZombieModeOptionTradeoffGunDamagePenalty",
        "GetZombieModeOptionTradeoffReloadSpeedPenalty",
        "GetZombieModeOptionTradeoffDamageTakenPenalty",
        "GetZombieModeOptionTradeoffMaxHealthPenalty",
        "GetZombieModeOptionTradeoffPollutionGain",
        "GetZombieModeOptionTradeoffPurificationCost",
        "GetZombieModeOptionTradeoffDisplayPercent",
        "ZombieModeStatNames.MaxHealth",
        "ZombieModeStatNames.MoveSpeed",
        "ZombieModeStatNames.WalkSpeed",
        "ZombieModeStatNames.RunSpeed",
        "ZombieModeStatNames.GunDamageMultiplier",
        "ZombieModeStatNames.ReloadSpeedGain",
        "ZombieModeStatNames.ElementFactorPhysics",
        "PollutionFromContracts",
        "BossRush_ZombieMode_Reward_Tradeoff_MoveSpeed",
        "BossRush_ZombieMode_Reward_Tradeoff_GunDamage",
        "BossRush_ZombieMode_Reward_Tradeoff_ReloadSpeed",
        "BossRush_ZombieMode_Reward_Tradeoff_DamageTaken",
        "BossRush_ZombieMode_Reward_Tradeoff_Pollution",
        "BossRush_ZombieMode_Reward_Tradeoff_MaxHealth",
        "BossRush_ZombieMode_Reward_Tradeoff_Purification",
    ]:
        combined = models + rewards + effects + localization
        if token not in combined:
            return fail("missing tradeoff token -> " + token)

    seen_tradeoff_labels = set()
    for reward_type in TRADEOFF_REWARDS:
        if "case ZombieModeRewardType." + reward_type + ":" not in effects:
            return fail("tradeoff switch missing reward -> " + reward_type)
        key = "BossRush_ZombieMode_Reward_" + reward_type
        key_index = localization.find(key)
        if key_index < 0:
            return fail("localization missing -> " + reward_type)
        line_end = localization.find("\n", key_index)
        line = localization[key_index:line_end if line_end >= 0 else len(localization)]
        expected = TRADEOFF_EXPECTATIONS[reward_type]
        seen_tradeoff_labels.add(expected)
        if "代价：" not in line:
            return fail("localization does not state a tradeoff -> " + reward_type)
        if expected not in line:
            return fail("localization states wrong tradeoff for " + reward_type + " -> expected " + expected)

    if len(seen_tradeoff_labels) < 7:
        return fail("tradeoffs must cover at least seven distinct cost families")

    print("ZombieModeRewardTradeoffGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
