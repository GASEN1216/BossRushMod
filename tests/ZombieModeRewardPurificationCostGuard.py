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



def fail(message: str) -> int:
    print("ZombieModeRewardPurificationCostGuard: FAIL - " + message)
    return 1


def main() -> int:
    rewards = read_rewards()
    effects = read_effects()

    for token in [
        "private bool IsZombieModeRewardUnaffordable(ZombieModeRewardType rewardType)",
        "GetZombieModeOptionTradeoffPurificationCost(rewardType)",
        "zombieModeRunState.PurificationPoints < purificationCost",
        "if (IsZombieModeRewardUnaffordable(rewardType))",
        "NotificationText.Push(L10n.T(\"BossRush_ZombieMode_Notify_RefreshNoPoints\"));",
    ]:
        if token not in rewards:
            return fail("reward selection/catalog must reject unaffordable purification-cost rewards -> " + token)

    guard_index = rewards.find("if (IsZombieModeRewardUnaffordable(rewardType))")
    entry_index = rewards.find("ZombieModeRewardCatalogEntry entry = new ZombieModeRewardCatalogEntry();")
    if guard_index < 0 or entry_index < 0 or guard_index > entry_index:
        return fail("unaffordable rewards must be filtered before catalog entry creation")

    for token in [
        "private bool TrySpendZombieModeOptionPurificationCost(ZombieModeRewardType rewardType)",
        "SpendZombieModePurificationPoints(purificationCost, \"OptionRewardTradeoff\")",
        "if (!TrySpendZombieModeOptionPurificationCost(rewardType))",
    ]:
        if token not in effects:
            return fail("option reward apply path must spend purification cost before applying effects -> " + token)

    if "zombieModeRunState.PurificationPoints = Mathf.Max(0, zombieModeRunState.PurificationPoints - purificationCost);" in effects:
        return fail("purification-cost tradeoff must not silently clamp to zero")

    print("ZombieModeRewardPurificationCostGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
