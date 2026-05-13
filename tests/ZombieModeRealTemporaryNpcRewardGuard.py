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

LOCALIZATION = Path("Localization/LocalizationInjector.cs")

REWARD_TYPES = [
    "TempGoblinNpc",
    "TempNurseNpc",
    "TempCourierNpc",
]


def fail(message: str) -> int:
    print("ZombieModeRealTemporaryNpcRewardGuard: FAIL - " + message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = read_rewards()
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for reward_type in REWARD_TYPES:
        if reward_type not in models:
            return fail("reward enum missing -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("reward wiring missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in rewards:
            return fail("reward display missing -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("reward localization missing -> " + reward_type)

    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempMerchant, ZombieModeRewardCategory.Npc, 10);" not in rewards:
        return fail("baseline merchant weight check missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempGoblinNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("goblin low-weight reward entry missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempNurseNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("nurse low-weight reward entry missing")
    if "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.TempCourierNpc, ZombieModeRewardCategory.Npc, 3);" not in rewards:
        return fail("courier low-weight reward entry missing")

    print("ZombieModeRealTemporaryNpcRewardGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
