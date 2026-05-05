from pathlib import Path
import sys


MODEE = Path("ModeE/ModeEMerchant.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print("ZombieModeMerchantModeEPoolGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    modee = MODEE.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")

    for snippet in [
        "private List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(",
        "private List<int> ModeESearchItemsMultiTag(",
        "private static readonly Dictionary<string, int[]> modeEMerchantCategoryItemCache",
        "private static readonly HashSet<int> modeEMedicalShopExcludedIds",
        "internal int[] GetModeEMerchantCategoryPoolIds(",
        'if (suffix == "Medical")',
        "allIds.RemoveAll(id => modeEMedicalShopExcludedIds.Contains(id));",
    ]:
        result = require(modee, snippet, "Mode E merchant pool export")
        if result:
            return result

    for snippet in [
        "private string GetZombieModeMerchantModeECategorySuffix(",
        "GetModeEMerchantCategoryPoolIds(",
        "TryPurchaseZombieModeGuaranteedMerchantStockFromPool(",
        "TryGiveRandomZombieModeMerchantItemFromModeEPool(",
        "PickZombieModeStrictQualityCandidate(",
        "TryGiveZombieModeItemToPlayerOrDrop(typeId)",
    ]:
        result = require(rewards, snippet, "Zombie merchant Mode E pool reuse")
        if result:
            return result

    print("ZombieModeMerchantModeEPoolGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
