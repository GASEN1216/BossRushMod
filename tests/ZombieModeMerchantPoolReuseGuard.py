from pathlib import Path
import sys
from cs_source_util import clean_source


MODEE = Path("Utilities/ModeEFMerchantCatalog.cs")
ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
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

CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")


def fail(message: str) -> int:
    print("ZombieModeMerchantPoolReuseGuard: FAIL - " + message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    modee = clean_source(MODEE.read_text(encoding="utf-8"))
    entry = clean_source(ENTRY.read_text(encoding="utf-8"))
    rewards = read_rewards()
    catalog = clean_source(CATALOG.read_text(encoding="utf-8"))

    for snippet in [
        "GetModeEMerchantCategories",
        'if (medTag == null) medTag = findTag("Medical");',
        'if (medTag == null) medTag = findTag("Consumable");',
        'if (medTag == null) medTag = findTag("Healing");',
        'Duckov.Utilities.Tag injectorTag = findTag("Injector");',
        "ModeESearchItemsMultiTag",
    ]:
        result = require(modee, snippet, "Mode E merchant reference contract")
        if result:
            return result

    for snippet in [
        "private string[] GetZombieModeMerchantGrantTagAliases(",
        'return new string[] { "Medic", "Medical", "Consumable", "Healing", "Injector" };',
        'return new string[] { "Armor", "Helmat", "Helmet" };',
        'return new string[] { "Headset", "Mask", "FaceMask" };',
    ]:
        result = require(rewards + entry, snippet, "Zombie merchant alias reuse contract")
        if result:
            return result

    for snippet in [
        "GetZombieModeMerchantGrantTagAliases(entry.GrantTag)",
        "TryGiveRandomItemByTags(grantTags, entry.GrantMinQuality, entry.GrantMaxQuality)",
        "FindRandomItemTypeByTags(grantTags, quality, quality)",
    ]:
        result = require(rewards, snippet, "merchant purchase alias flow")
        if result:
            return result

    for snippet in [
        'GrantTag = "Medical"',
        'GrantTag = "Food"',
        'GrantTag = "Bait"',
        'GrantTag = "Armor"',
        'GrantTag = "Helmet"',
    ]:
        result = require(catalog, snippet, "merchant stock shape")
        if result:
            return result

    print("ZombieModeMerchantPoolReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
