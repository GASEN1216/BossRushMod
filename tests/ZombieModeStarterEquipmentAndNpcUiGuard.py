"""Guard ZombieMode starter protection gear and temporary NPC merchant UI rebuilds."""

from pathlib import Path
import re
import sys


ENTRY = Path("ZombieMode/ZombieModeEntry.cs")
ENTRY_PARTS = [
    ENTRY,
    Path("ZombieMode/ZombieModeEntry_StarterLoadout.cs"),
]
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


def read_entry() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in ENTRY_PARTS)

CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")


def fail(message: str) -> int:
    print("ZombieModeStarterEquipmentAndNpcUiGuard: FAIL - " + message)
    return 1


def extract_block(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    entry = read_entry()
    rewards = read_rewards()
    catalog = CATALOG.read_text(encoding="utf-8")

    if "if (!GrantZombieModeStarterProtectionSet())" not in entry:
        return fail("starter loadout must require the guaranteed armor/helmet/headset set")

    protection = extract_block(entry, "private bool GrantZombieModeStarterProtectionSet(")
    if not protection:
        return fail("GrantZombieModeStarterProtectionSet helper missing")
    for snippet in [
        'TryGiveRandomItemByTags(new string[] { "BodyArmor" }',
        'TryGiveRandomItemByTags(new string[] { "Helmet" }',
        'TryGiveRandomItemByTags(new string[] { "Headset" }',
    ]:
        if snippet not in protection:
            return fail("starter protection helper missing gear grant: " + snippet)

    aliases = extract_block(entry, "private static string[] GetZombieModeTagAliases(")
    if not aliases or '"Helmet"' not in aliases or '"Helmat"' not in aliases:
        return fail("Helmet tag lookup must fall back to the game's Helmat tag spelling")
    if '"Armor"' not in aliases or 'return new string[] { "Armor", "Helmat", "Helmet" };' not in aliases:
        return fail("Armor tag lookup must include body armor and helmets")
    if '"BodyArmor"' not in aliases or 'return new string[] { "Armor" };' not in aliases:
        return fail("starter body armor lookup must stay body-armor only")

    if "private Tag FindZombieModeTagByName(" not in entry:
        return fail("single-tag resolver must remain for ZombieMode melee tag checks")
    resolver = extract_block(entry, "private Tag FindZombieModeTagByName(")
    if "GetZombieModeTagAliases(tagName)" not in resolver:
        return fail("single-tag resolver must share ZombieMode tag aliases")

    candidates = extract_block(entry, "private int[] GetZombieModeRewardCandidateIds(")
    if not candidates:
        return fail("GetZombieModeRewardCandidateIds helper missing")
    if "ResolveZombieModeTags(new string[] { requiredTags[i] })" not in candidates:
        return fail("reward candidate lookup must resolve each logical tag separately")
    if "new Tag[] { tag }" not in candidates:
        return fail("reward candidate lookup must search one concrete tag at a time for OR semantics")
    if "filter.requireTags = tags;" in candidates:
        return fail("reward candidate lookup must not pass merged aliases as one requireTags array")
    dedupe_helper = extract_block(entry, "private void AddZombieModeRewardCandidates(")
    if "!zombieModeRewardSafeCandidateScratch.Contains(candidates[i])" not in (candidates + dedupe_helper):
        return fail("reward candidate lookup must de-duplicate OR-merged tag results")

    service_view = extract_block(rewards, "public sealed class ZombieModeTemporaryNpcServiceView")
    if not service_view:
        return fail("ZombieModeTemporaryNpcServiceView not found")
    if "gameObject.GetComponent<Canvas>()" not in service_view:
        return fail("temporary NPC service rebuild must reuse the existing Canvas component")
    if "Canvas canvas = gameObject.AddComponent<Canvas>();" in service_view:
        return fail("temporary NPC service rebuild still adds a second Canvas directly")
    if "i < stock.Length && i < ZombieModeNpcCatalog.MaxMerchantStockButtons" not in service_view:
        return fail("merchant UI must cap against catalog constant, not a hidden literal")
    if "grid.constraintCount = 4;" not in service_view:
        return fail("merchant grid must have four columns so helmet is visible without overlapping close")

    match = re.search(r"public const int MaxMerchantStockButtons = (\d+);", catalog)
    if not match:
        return fail("merchant catalog missing MaxMerchantStockButtons")
    if int(match.group(1)) < 13:
        return fail("merchant UI cap must render the normal stock helmet entry")

    print("ZombieModeStarterEquipmentAndNpcUiGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
