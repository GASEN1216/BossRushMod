from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeSpec17RewardTableGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for snippet in [
        "RandomMeleeWeapon",
        "RandomGunWithAmmo",
        "AmmoSupply",
        "MedicalSupply",
        "ArmorOrHelmet",
        "ContractGearDeal",
        "ContractHugePurification",
        "ContractInsurance",
        "InsuranceNearFull",
    ]:
        result = require(models, snippet, "SPEC 17 reward enum")
        if result:
            return result

    for snippet in [
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomMeleeWeapon, ZombieModeRewardCategory.Equipment, 10);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.RandomGunWithAmmo, ZombieModeRewardCategory.Equipment, 10);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.AmmoSupply, ZombieModeRewardCategory.Equipment, 15);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.MedicalSupply, ZombieModeRewardCategory.Equipment, 15);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ArmorOrHelmet, ZombieModeRewardCategory.Equipment, 8);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.FortificationPack, ZombieModeRewardCategory.Fortification, 10);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractGearDeal, ZombieModeRewardCategory.Contract, 8);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractHugePurification, ZombieModeRewardCategory.Contract, 5);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.ContractInsurance, ZombieModeRewardCategory.Contract, 4);",
        "AddZombieModeRewardCatalogEntry(entries, ZombieModeRewardType.InsuranceNearFull, ZombieModeRewardCategory.Insurance, 1);",
    ]:
        result = require(rewards, snippet, "SPEC 17 reward weight table")
        if result:
            return result

    for snippet in [
        "GrantZombieModeRandomMeleeReward",
        "GrantZombieModeRandomGunWithAmmoReward",
        "GrantZombieModeAmmoSupplyReward",
        "GrantZombieModeMedicalSupplyReward",
        "GrantZombieModeArmorOrHelmetReward",
        "ApplyZombieModeContractGearDeal",
        "ApplyZombieModeContractHugePurification",
        "ApplyZombieModeContractInsurance",
        "ApplyZombieModeInsuranceReward(0.80f, false)",
    ]:
        result = require(rewards, snippet, "SPEC 17 reward effect handler")
        if result:
            return result

    for snippet in [
        "BossRush_ZombieMode_Reward_RandomMeleeWeapon",
        "BossRush_ZombieMode_Reward_RandomGunWithAmmo",
        "BossRush_ZombieMode_Reward_AmmoSupply",
        "BossRush_ZombieMode_Reward_MedicalSupply",
        "BossRush_ZombieMode_Reward_ArmorOrHelmet",
        "BossRush_ZombieMode_Reward_ContractGearDeal",
        "BossRush_ZombieMode_Reward_ContractHugePurification",
        "BossRush_ZombieMode_Reward_ContractInsurance",
        "BossRush_ZombieMode_Reward_InsuranceNearFull",
    ]:
        result = require(localization, snippet, "SPEC 17 reward localization")
        if result:
            return result

    print("ZombieModeSpec17RewardTableGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
