from pathlib import Path
import re
import sys


LOCALIZATION = Path("Localization/LocalizationInjector.cs")
USAGE_FILES = [
    Path("ZombieMode/ZombieModeEntry.cs"),
    Path("ZombieMode/ZombieModeEntry_StarterLoadout.cs"),
    Path("ZombieMode/ZombieModeMapSelectionHelper.cs"),
    Path("ZombieMode/ZombieModeHudController.cs"),
    Path("ZombieMode/ZombieModeWaveController.cs"),
    Path("ZombieMode/ZombieModeExtractionController.cs"),
    Path("ZombieMode/ZombieModeRewards.cs"),
    Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
    Path("Integration/Items/ZombieTideBeaconUsage.cs"),
]

REQUIRED_KEYS = [
    "BossRush_ZombieMode",
    "BossRush_ZombieMode_NotInitialized",
    "BossRush_ZombieMode_NoInvitation",
    "BossRush_ZombieMode_NoMaps",
    "BossRush_ZombieMode_OpenMapFailed",
    "BossRush_ZombieMode_OtherModeActive",
    "BossRush_ZombieMode_Starter_Title",
    "BossRush_ZombieMode_Starter_Melee",
    "BossRush_ZombieMode_Starter_Gunner",
    "BossRush_ZombieMode_EntryName",
    "BossRush_ZombieMode_MapEntryPrefix",
    "BossRush_ZombieMode_Hud_Wave",
    "BossRush_ZombieMode_Hud_Pollution",
    "BossRush_ZombieMode_Hud_PollutionTier_Base",
    "BossRush_ZombieMode_Hud_StageBattle",
    "BossRush_ZombieMode_Hud_StagePreparation",
    "BossRush_ZombieMode_Hud_StageExtractionOpportunity",
    "BossRush_ZombieMode_Hud_KillProgress",
    "BossRush_ZombieMode_Hud_PurificationPoints",
    "BossRush_ZombieMode_Banner_Started",
    "BossRush_ZombieMode_Banner_PreparationStarted",
    "BossRush_ZombieMode_Banner_PreparationNextWave",
    "BossRush_ZombieMode_Banner_WaveIncoming",
    "BossRush_ZombieMode_Banner_WaveCleared",
    "BossRush_ZombieMode_Banner_Failed",
    "BossRush_ZombieMode_Banner_ExtractionOpen",
    "BossRush_ZombieMode_Extraction_Title",
    "BossRush_ZombieMode_Extraction_ExtractNow",
    "BossRush_ZombieMode_Extraction_Continue",
    "BossRush_ZombieMode_Reward_Title_Normal",
    "BossRush_ZombieMode_Reward_Title_Boss",
    "BossRush_ZombieMode_Reward_Info",
    "BossRush_ZombieMode_Reward_RefreshFree",
    "BossRush_ZombieMode_Reward_RefreshPaid",
    "BossRush_ZombieMode_Reward_PurificationPoints",
    "BossRush_ZombieMode_Reward_Heal",
    "BossRush_ZombieMode_Reward_RandomSupply",
    "BossRush_ZombieMode_Reward_RandomHighQualityItem",
    "BossRush_ZombieMode_Reward_StarterReroll",
    "BossRush_ZombieMode_Reward_CurrentNodeFreeRefresh",
    "BossRush_ZombieMode_Reward_NextNodeFreeRefresh",
    "BossRush_ZombieMode_Reward_HalfPricePaidRefresh",
    "BossRush_ZombieMode_Reward_Attribute_MaxHealth",
    "BossRush_ZombieMode_Reward_TempMerchant",
    "BossRush_ZombieMode_Reward_TempNurse",
    "BossRush_ZombieMode_Reward_TempGoblinNpc",
    "BossRush_ZombieMode_Reward_TempNurseNpc",
    "BossRush_ZombieMode_Reward_TempCourierNpc",
    "BossRush_ZombieMode_Reward_FortificationPack",
    "BossRush_ZombieMode_Reward_ContractPollutionDeal",
    "BossRush_ZombieMode_Reward_InsuranceKeepOne",
    "BossRush_ZombieMode_Npc_TempMerchant",
    "BossRush_ZombieMode_Npc_TempNurse",
    "BossRush_ZombieMode_Npc_TempGoblinNpc",
    "BossRush_ZombieMode_Npc_TempNurseNpcReal",
    "BossRush_ZombieMode_Npc_TempCourierNpc",
    "BossRush_ZombieMode_Npc_InteractMerchant",
    "BossRush_ZombieMode_Npc_InteractNurse",
    "BossRush_ZombieMode_Banner_RepairPackReceived",
    "BossRush_ZombieMode_Notify_BeaconNotZombieMode",
    "BossRush_ZombieMode_Notify_BeaconNotPreparation",
    "BossRush_ZombieMode_Notify_BeaconExtractionLocked",
    "BossRush_ZombieMode_Notify_ExtractionBeaconLocked",
    "BossRush_ZombieMode_Notify_RefreshNoPoints",
    "BossRush_ZombieMode_Notify_NpcServiceNoPoints",
    "BossRush_ZombieMode_Notify_RewardGranted",
    "BossRush_ZombieMode_Notify_AttributeMaxHealth",
    "BossRush_ZombieMode_Notify_AttributeBonus",
    "BossRush_ZombieMode_Notify_InsuranceKeepOne",
    "BossRush_ZombieMode_Notify_RefundedInvitation",
    "BossRush_ZombieMode_Reason_InitializationFailed",
    "BossRush_ZombieMode_Reason_PlayerDeath",
    "BossRush_ZombieMode_Reason_SceneSwitched",
    "BossRush_ZombieMode_Reason_SuccessfulExtraction",
    "BossRush_ZombieMode_Settle_SuccessTitle",
    "BossRush_ZombieMode_Settle_PointsToCash",
    "BossRush_ZombieMode_BossSkill_TitanShockwave",
    "BossRush_ZombieMode_BossSkill_TitanFortify",
    "BossRush_ZombieMode_BossSkill_HunterDash",
    "BossRush_ZombieMode_BossSkill_SplitterSummon",
    "BossRush_ZombieMode_BossSkill_ShielderSelfShield",
    "BossRush_ZombieMode_BossSkill_ShielderGroupShield",
    "BossRush_ZombieMode_BossSkill_CorruptorZone",
]

REQUIRED_USAGE_KEYS = [
    "BossRush_ZombieMode_NotInitialized",
    "BossRush_ZombieMode_NoInvitation",
    "BossRush_ZombieMode_NoMaps",
    "BossRush_ZombieMode_OpenMapFailed",
    "BossRush_ZombieMode_OtherModeActive",
    "BossRush_ZombieMode_Starter_Title",
    "BossRush_ZombieMode_Starter_Melee",
    "BossRush_ZombieMode_Starter_Gunner",
    "BossRush_ZombieMode_EntryName",
    "BossRush_ZombieMode_MapEntryPrefix",
    "BossRush_ZombieMode_Hud_Wave",
    "BossRush_ZombieMode_Hud_Pollution",
    "BossRush_ZombieMode_Hud_KillProgress",
    "BossRush_ZombieMode_Hud_PurificationPoints",
    "BossRush_ZombieMode_Banner_PreparationStarted",
    "BossRush_ZombieMode_Banner_PreparationNextWave",
    "BossRush_ZombieMode_Banner_WaveIncoming",
    "BossRush_ZombieMode_Banner_WaveCleared",
    "BossRush_ZombieMode_Banner_Failed",
    "BossRush_ZombieMode_Banner_ExtractionOpen",
    "BossRush_ZombieMode_Extraction_Title",
    "BossRush_ZombieMode_Extraction_ExtractNow",
    "BossRush_ZombieMode_Extraction_Continue",
    "BossRush_ZombieMode_Reward_Title_Normal",
    "BossRush_ZombieMode_Reward_Title_Boss",
    "BossRush_ZombieMode_Reward_Info",
    "BossRush_ZombieMode_Reward_RefreshFree",
    "BossRush_ZombieMode_Reward_RefreshPaid",
    "BossRush_ZombieMode_Reward_PurificationPoints",
    "BossRush_ZombieMode_Reward_Attribute_MaxHealth",
    "BossRush_ZombieMode_Reward_TempMerchant",
    "BossRush_ZombieMode_Reward_TempNurse",
    "BossRush_ZombieMode_Reward_TempGoblinNpc",
    "BossRush_ZombieMode_Reward_TempNurseNpc",
    "BossRush_ZombieMode_Reward_TempCourierNpc",
    "BossRush_ZombieMode_Reward_FortificationPack",
    "BossRush_ZombieMode_Reward_ContractPollutionDeal",
    "BossRush_ZombieMode_Reward_InsuranceKeepOne",
    "BossRush_ZombieMode_Npc_TempMerchant",
    "BossRush_ZombieMode_Npc_TempNurse",
    "BossRush_ZombieMode_Npc_TempGoblinNpc",
    "BossRush_ZombieMode_Npc_TempNurseNpcReal",
    "BossRush_ZombieMode_Npc_TempCourierNpc",
    "BossRush_ZombieMode_Banner_RepairPackReceived",
    "BossRush_ZombieMode_Notify_BeaconNotZombieMode",
    "BossRush_ZombieMode_Notify_BeaconNotPreparation",
    "BossRush_ZombieMode_Notify_RefreshNoPoints",
    "BossRush_ZombieMode_Notify_NpcServiceNoPoints",
    "BossRush_ZombieMode_Notify_RewardGranted",
    "BossRush_ZombieMode_Notify_AttributeBonus",
    "BossRush_ZombieMode_Notify_InsuranceKeepOne",
    "BossRush_ZombieMode_Notify_RefundedInvitation",
]


def fail(message: str) -> int:
    print(message)
    return 1



def main() -> int:
    localization_text = LOCALIZATION.read_text(encoding="utf-8")

    missing = [key for key in REQUIRED_KEYS if key not in localization_text]
    if missing:
        return fail("ZombieModeLocalizationGuard: missing injected keys -> " + ", ".join(missing))

    for file_path in USAGE_FILES:
        usage_text = file_path.read_text(encoding="utf-8")
        hardcoded = re.findall(r'L10n\.T\("[^\"]+",\s*"[^\"]+"\)', usage_text)
        if hardcoded:
            return fail(
                "ZombieModeLocalizationGuard: lingering hardcoded bilingual text in "
                + str(file_path)
                + " -> "
                + hardcoded[0]
            )

    combined_usage = "\n".join(path.read_text(encoding="utf-8") for path in USAGE_FILES)
    missing_usage = [key for key in REQUIRED_USAGE_KEYS if key not in combined_usage]
    if missing_usage:
        return fail("ZombieModeLocalizationGuard: required keys not used in ZombieMode flows -> " + ", ".join(missing_usage))

    print("ZombieModeLocalizationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
