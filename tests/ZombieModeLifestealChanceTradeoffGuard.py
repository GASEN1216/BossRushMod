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
    print("ZombieModeLifestealChanceTradeoffGuard: FAIL - " + message)
    return 1


def main() -> int:
    for path in [MODELS, REWARDS, EFFECTS, LOCALIZATION]:
        if not path.exists():
            return fail("missing file -> " + str(path))

    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    effects = read_effects()
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for reward_type in [
        "TriggerLifesteal",
        "TriggerLifestealMedium",
        "TriggerLifestealLarge",
    ]:
        if reward_type not in models:
            return fail("missing reward enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("reward not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in rewards:
            return fail("display not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("localization missing -> " + reward_type)

    required_tokens = [
        "TriggerLifestealChancePercent",
        "TriggerLifestealHealAmount",
        "ZombieModeLifestealChanceCapPercent = 50",
        "ApplyZombieModeLifestealReward(10, 1)",
        "ApplyZombieModeLifestealReward(20, 1)",
        "ApplyZombieModeLifestealReward(30, 1)",
        "GetZombieModeOptionTradeoffMoveSpeedPenalty",
        "OptionTradeoffMoveSpeedPenalty",
        "GetZombieModeOptionTradeoffGunDamagePenalty",
        "GetZombieModeOptionTradeoffReloadSpeedPenalty",
        "GetZombieModeOptionTradeoffDamageTakenPenalty",
        "GetZombieModeOptionTradeoffMaxHealthPenalty",
        "GetZombieModeOptionTradeoffPollutionGain",
        "GetZombieModeOptionTradeoffPurificationCost",
        "return 0.11f;",
        "return 0.22f;",
        "return 0.33f;",
        "ZombieModeStatNames.MoveSpeed",
        "ZombieModeStatNames.WalkSpeed",
        "ZombieModeStatNames.RunSpeed",
        "UnityEngine.Random.value",
    ]
    combined = models + rewards + effects
    for token in required_tokens:
        if token not in combined:
            return fail("missing behavior token -> " + token)

    for duplicate_token in [
        "GetZombieModeLifestealMoveSpeedPenalty",
        "ZombieMode Option Lifesteal MoveSpeed",
        "ZombieMode Option Lifesteal WalkSpeed",
        "ZombieMode Option Lifesteal RunSpeed",
    ]:
        if duplicate_token in effects:
            return fail("lifesteal must use unified option tradeoff only -> " + duplicate_token)

    for snippet in [
        "10% 概率恢复 1",
        "20% 概率恢复 1",
        "30% 概率恢复 1",
        "代价：移动速度 -11%",
        "代价：移动速度 -22%",
        "代价：移动速度 -33%",
    ]:
        if snippet not in localization:
            return fail("missing localization detail -> " + snippet)

    print("ZombieModeLifestealChanceTradeoffGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
