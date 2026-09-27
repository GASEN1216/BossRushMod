from pathlib import Path
import sys
from cs_source_util import clean_source


MODEE = Path("Utilities/ModeEFMerchantCatalog.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
REWARD_PARTS = [
    REWARDS,
    Path("ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs"),
    Path("ZombieMode/ZombieModeRewardEffectsAndNpc.cs"),
    Path("ZombieMode/ZombieModeRewardItemGrants.cs"),
    Path("ZombieMode/ZombieModeRewardNpcServices.cs"),
]


def read_rewards() -> str:
    return "\n".join(clean_source(path.read_text(encoding="utf-8")) for path in REWARD_PARTS)



def fail(message: str) -> int:
    print("ZombieModeMerchantModeEPoolGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    modee = clean_source(MODEE.read_text(encoding="utf-8"))
    rewards = read_rewards()

    for path, snippet in (
        ("ModBehaviourRuntimeModules.cs", "modeERuntime.BindSharedServices(modeDRuntime, wavesArenaRuntime, modeEFSpawnPreparation, modeEFMerchantCatalog);"),
        ("ModeE/ModeERuntimeModule.cs", "this.merchantCatalog = merchantCatalog;"),
        ("ModeE/ModeEHostBridge.cs", "return modeEFMerchantCatalog.GetModeEMerchantCategoryPoolIds(suffix);"),
        ("ModeE/ModeEMerchant.cs", "ModeEFMerchantCatalog.ResetStaticCaches();"),
    ):
        source = clean_source(Path(path).read_text(encoding="utf-8"))
        if require(source, snippet, "shared merchant catalog wiring"):
            return 1

    for snippet in [
        "internal List<System.Tuple<List<Duckov.Utilities.Tag>, string, string>> GetModeEMerchantCategories(",
        "internal List<int> ModeESearchItemsMultiTag(",
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
