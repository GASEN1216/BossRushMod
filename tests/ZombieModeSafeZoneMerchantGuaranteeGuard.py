from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
EXTRACTION = Path("ZombieMode/ZombieModeExtractionController.cs")
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


def fail(message: str) -> int:
    print("ZombieModeSafeZoneMerchantGuaranteeGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    extraction = EXTRACTION.read_text(encoding="utf-8")
    rewards = read_rewards()
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for snippet in [
        "public int GuaranteedMerchantPurchaseMinQuality;",
        "public bool GuaranteedMerchantPurchasePending;",
        "GuaranteedMerchantPurchaseMinQuality = 0;",
        "GuaranteedMerchantPurchasePending = false;",
    ]:
        result = require(models, snippet, "run state contract")
        if result:
            return result

    for snippet in [
        "EnsureZombieModeSafeZoneMerchantTerminal(runId);",
        "SpawnZombieModeTemporaryNpc(runId, \"Merchant\", false);",
        "FindZombieModeTemporaryNpc(\"Merchant\") == null",
    ]:
        result = require(extraction, snippet, "safe zone merchant binding")
        if result:
            return result

    for snippet in [
        "case ZombieModeRewardType.TempMerchant:",
        "GrantZombieModeMerchantPurchaseGuarantee();",
        "zombieModeRunState.GuaranteedMerchantPurchasePending = true;",
        "zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 6;",
        "TryPurchaseZombieModeGuaranteedMerchantStockFromPool(",
        "for (int quality = maxQuality; quality >= minQuality; quality--)",
        "zombieModeRunState.GuaranteedMerchantPurchasePending = false;",
        "zombieModeRunState.GuaranteedMerchantPurchaseMinQuality = 0;",
    ]:
        result = require(rewards, snippet, "merchant guarantee flow")
        if result:
            return result

    for snippet in [
        "BossRush_ZombieMode_Reward_TempMerchant",
        "BossRush_ZombieMode_Notify_TempMerchantGuarantee",
    ]:
        result = require(localization, snippet, "localization contract")
        if result:
            return result

    print("ZombieModeSafeZoneMerchantGuaranteeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
