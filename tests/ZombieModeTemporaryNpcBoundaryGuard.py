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

CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    rewards = read_rewards()
    catalog = CATALOG.read_text(encoding="utf-8")

    for token in [
        "private GameObject CreateZombieModeTemporaryServiceTerminal(",
        "private ZombieModeTemporaryNpc CreateZombieModeTemporaryNpcRecord(",
        "SpawnZombieModeTemporaryNpc(runId, pendingTemporaryNpcServiceType, extractionOpportunity)",
        "RegisterZombieModeRunOnlyObject(runId, ZombieModeRunOnlyObjectKind.TemporaryNpc",
        "record.ServiceState = CreateZombieModeNpcServiceState(serviceType, bossNodeStock, zombieModeRunState.ActiveSafeZoneActive)",
        "ZombieModeNpcCatalog.NormalWaveStock",
        "ZombieModeNpcCatalog.BossNodeStock",
        "ZombieModeNpcCatalog.NurseServices",
    ]:
        if token not in rewards:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: missing run-only service terminal token -> " + token)

    for token in [
        "NPCModuleRegistry",
        "DuckovDialogueActor",
        "StockShop",
        "NPCShopInteractable",
        "NPCGiftInteractable",
        "AffinityManager",
    ]:
        if token in rewards:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: temporary service terminal must not bind normal NPC module systems -> " + token)

    for token in [
        "public static readonly MerchantStockEntry[] NormalWaveStock",
        "public static readonly MerchantStockEntry[] BossNodeStock",
        "public static readonly NurseServiceEntry[] NurseServices",
        "public static float GetPollutionPriceMultiplier",
    ]:
        if token not in catalog:
            return fail("ZombieModeTemporaryNpcBoundaryGuard: service catalog missing token -> " + token)

    print("ZombieModeTemporaryNpcBoundaryGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
