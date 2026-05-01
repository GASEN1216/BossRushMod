from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
NPC_CATALOG = Path("ZombieMode/ZombieModeNpcCatalog.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeRewardCatalogGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    npc_catalog = NPC_CATALOG.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    for snippet in [
        "public enum ZombieModeRewardCategory",
        "Attribute",
        "Equipment",
        "Economy",
        "Npc",
        "Fortification",
        "Contract",
        "Insurance",
        "MapEvent",
        "public sealed class ZombieModeRewardCatalogEntry",
        "public ZombieModeRewardCategory Category;",
        "public int Weight;",
        "public sealed class ZombieModeAttributeModifierRecord",
        "public ItemStatsSystem.Stats.Modifier Modifier;",
        "public enum ZombieModePendingMapEventType",
        "HighValueAirdrop",
        "EliteSquad",
        "public ZombieModePendingMapEventType PendingMapEvent",
        "public int PendingEliteSquadCount;",
    ]:
        result = require(models, snippet, "reward model contract")
        if result:
            return result

    for snippet in [
        "BuildZombieModeRewardCatalogEntries",
        "GetZombieModeRewardCategoryWeight",
        "RollZombieModeRewardCategory",
        "SelectZombieModeRewardEntryForCategory",
        "EnsureZombieModeRewardCategoryDiversity",
        "GetZombieModeRewardCategory",
        "GetZombieModeRewardCategoryCount",
        "node.Options.Count < optionCount",
        "GetZombieModeMinimumRewardCategoryCount(node.BossNode)",
        "ZombieModeRewardType.AttributeMoveSpeed",
        "ZombieModeRewardType.AttributeMeleeDamage",
        "ZombieModeRewardType.AttributeRangedDamage",
        "ZombieModeRewardType.AttributeReloadSpeed",
        "ZombieModeRewardType.AttributeDamageReduction",
        "ZombieModeRewardType.MapEventHighValueAirdrop",
        "ZombieModeRewardType.MapEventEliteSquad",
        "ZombieModeRewardType.InsuranceRandom10",
        "ZombieModeRewardType.InsuranceRandom20",
        "ApplyZombieModePlayerAttributeModifiers",
        "RemoveZombieModeAttributeModifiers",
        "AddZombieModeAttributeModifier",
        "new Modifier(ModifierType.Add",
        "RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId, ZombieModeRunOnlyObjectKind.Buff",
        "ApplyZombieModeMapEventReward",
        "SpawnPendingZombieModeEliteSquad",
        "CreateZombieModeHighValueAirdrop",
        "PendingMapEvent = ZombieModePendingMapEventType.EliteSquad",
        "TryGiveRandomItemByTags(new string[] { \"Gun\" }",
        "TryGiveRandomItemByTags(new string[] { \"MeleeWeapon\" }",
        "ZombieModeNpcCatalog.GetPollutionPriceMultiplier",
    ]:
        result = require(rewards, snippet, "weighted reward implementation")
        if result:
            return result

    if "List<ZombieModeRewardType> pool = new List<ZombieModeRewardType>" in rewards:
        return fail("ZombieModeRewardCatalogGuard: reward generation still uses flat enum pool")

    if "Random.Range(0, pool.Count)" in rewards:
        return fail("ZombieModeRewardCatalogGuard: reward generation still uses unweighted pool index")

    for snippet in [
        "BossRush_ZombieMode_RewardCat_Attribute",
        "BossRush_ZombieMode_RewardCat_MapEvent",
        "BossRush_ZombieMode_Reward_Attribute_MoveSpeed",
        "BossRush_ZombieMode_Reward_MapEventHighValueAirdrop",
        "BossRush_ZombieMode_Reward_MapEventEliteSquad",
    ]:
        result = require(rewards, snippet, "reward UI category/key usage")
        if result:
            return result

    if "ZombieMode\\ZombieModeNpcCatalog.cs" not in compile_text:
        return fail("ZombieModeRewardCatalogGuard: missing NPC catalog compile entry")

    for snippet in [
        "public static readonly float[] NpcAngleArrangement = { 0f, 120f, -120f };",
        "GetPollutionPriceMultiplier",
    ]:
        result = require(npc_catalog, snippet, "NPC catalog usage")
        if result:
            return result

    print("ZombieModeRewardCatalogGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
