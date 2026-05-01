from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
HUD = Path("ZombieMode/ZombieModeHudController.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
NPC_CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeHudAndNpcServiceGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    hud = HUD.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    npc_catalog = NPC_CATALOG.read_text(encoding="utf-8")
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for snippet in [
        "public sealed class ZombieModeNpcServiceState",
        "public readonly List<int> MerchantStockRemaining",
        "public readonly List<int> NurseUsesRemaining",
        "public ZombieModeNpcServiceState ServiceState",
    ]:
        result = require(models, snippet, "NPC service state model")
        if result:
            return result

    for snippet in [
        "GetZombieModeBossProgressText",
        "GetZombieModeNextBossText",
        "GetZombieModeHudSafeZoneText",
        "GetZombieModeHudMainText",
        "GetZombieModeHudStageText",
        "GetZombieModeHudSafeZoneColor",
        "GetZombieModeBeaconHudText",
        "GetZombieModeExtractionHudText",
        "BossRush_ZombieMode_Hud_BossProgress",
        "BossRush_ZombieMode_Hud_NextBoss",
        "BossRush_ZombieMode_Hud_NextBossNow",
        "BossRush_ZombieMode_Hud_PreparationTimer",
        "BossRush_ZombieMode_Hud_SafeZone_Inside",
        "BossRush_ZombieMode_Hud_SafeZone_Outside",
        "BossRush_ZombieMode_Hud_SafeZone_StealthOk",
        "BossRush_ZombieMode_Hud_SafeZone_StealthBroken",
        "BossRush_ZombieMode_Hud_BeaconReady",
        "BossRush_ZombieMode_Hud_ExtractionOpenHint",
    ]:
        result = require(hud, snippet, "complete HUD")
        if result:
            return result

    for snippet in [
        "CreateZombieModeNpcServiceState",
        "OpenZombieModeTemporaryNpcServiceUi",
        "ZombieModeTemporaryNpcServiceView",
        "TryPurchaseZombieModeMerchantStock",
        "TryUseZombieModeNurseService",
        "GetZombieModeMerchantStock",
        "GetZombieModeNurseServices",
        "record.ServiceState = CreateZombieModeNpcServiceState",
        "zombieModeRunState.TemporaryNpcs[i].ServiceState",
        "MerchantStockRemaining[stockIndex]--",
        "NurseUsesRemaining[serviceIndex]--",
        "ZombieModeNpcCatalog.NormalWaveStock",
        "ZombieModeNpcCatalog.BossNodeStock",
        "ZombieModeNpcCatalog.NurseServices",
        "ZombieModeNpcCatalog.GetPollutionPriceMultiplier",
        "BossRush_ZombieMode_Npc_ServicePrice",
        "BossRush_ZombieMode_Npc_ServiceRemaining",
        "BossRush_ZombieMode_Npc_Close",
    ]:
        result = require(rewards, snippet, "NPC service UI and state")
        if result:
            return result

    if "TryUseZombieModeTemporaryNpcService(runId, serviceType);" in rewards:
        return fail("ZombieModeHudAndNpcServiceGuard: NPC interaction still performs one-shot service directly")

    for snippet in [
        "DisplayKey",
        "GrantTag",
        "GrantMinQuality",
        "GrantMaxQuality",
        "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",
        "BossRush_ZombieMode_Npc_Merchant_RandomMedical",
        "BossRush_ZombieMode_Npc_Merchant_RandomGun",
        "BossRush_ZombieMode_Npc_Merchant_RandomArmor",
        "BossRush_ZombieMode_Npc_NurseService_HealHalf",
        "BossRush_ZombieMode_Npc_NurseService_HealFull",
        "BossRush_ZombieMode_Npc_NurseService_Detox",
        "BossRush_ZombieMode_Npc_NurseService_StopBleed",
        "BossRush_ZombieMode_Npc_NurseService_FirstAid",
    ]:
        result = require(npc_catalog, snippet, "NPC catalog labels")
        if result:
            return result

    for snippet in [
        "BossRush_ZombieMode_Npc_ServicePrice",
        "BossRush_ZombieMode_Npc_ServiceRemaining",
        "BossRush_ZombieMode_Npc_Close",
        "BossRush_ZombieMode_Npc_Merchant_RandomAmmo",
        "BossRush_ZombieMode_Npc_Merchant_RandomMedical",
        "BossRush_ZombieMode_Npc_Merchant_RandomGun",
        "BossRush_ZombieMode_Npc_Merchant_RandomArmor",
        "BossRush_ZombieMode_Npc_NurseService_HealHalf",
        "BossRush_ZombieMode_Npc_NurseService_HealFull",
        "BossRush_ZombieMode_Npc_NurseService_Detox",
        "BossRush_ZombieMode_Npc_NurseService_StopBleed",
        "BossRush_ZombieMode_Npc_NurseService_FirstAid",
    ]:
        result = require(localization, snippet, "NPC service localization")
        if result:
            return result

    print("ZombieModeHudAndNpcServiceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
