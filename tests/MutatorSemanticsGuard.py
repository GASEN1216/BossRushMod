"""Mutator semantics must match their descriptions and active modes.

Run mutators only buff enemies or change environment rules. They must NOT
alter loot box quality / quantity / type, and the system defaults ON so every
BossRush mode rolls a mutator at run start.
"""

from pathlib import Path
import sys


DEFINITIONS = Path("Integration/Mutators/MutatorDefinitions.cs")
MANAGER = Path("Integration/Mutators/MutatorManager.cs")
RUNTIME_BRIDGE = Path("Integration/Mutators/MutatorRuntimeBridge.cs")
WAVES = Path("WavesArena/WavesArenaBossSpawning.cs")
LOOT = Path("LootAndRewards/LootAndRewardsRandomBossLoot.cs")


def fail(message: str) -> int:
    print("MutatorSemanticsGuard: FAIL - " + message)
    return 1


def main() -> int:
    definitions = DEFINITIONS.read_text(encoding="utf-8")
    manager = MANAGER.read_text(encoding="utf-8")
    runtime_bridge = RUNTIME_BRIDGE.read_text(encoding="utf-8")
    waves = WAVES.read_text(encoding="utf-8")
    loot = LOOT.read_text(encoding="utf-8")
    config = Path("Config/Config.cs").read_text(encoding="utf-8")

    required_definition_snippets = [
        "public Func<string, bool> IsAllowedForMode;",
        "public bool AllowsMode(string modeTag)",
        'Id = "bleed_accelerate"',
        'string.Equals(modeTag, "ModeF", StringComparison.OrdinalIgnoreCase)',
        'playerItem.GetStat("GunDamageMultiplier")',
        'playerItem.GetStat("MeleeDamageMultiplier")',
    ]
    for snippet in required_definition_snippets:
        if snippet not in definitions:
            return fail("mutator definitions missing snippet -> " + snippet)

    required_manager_snippets = [
        "string modeTag = null",
        "definition.AllowsMode(modeTag)",
        "pool.Count == 0",
    ]
    for snippet in required_manager_snippets:
        if snippet not in manager:
            return fail("manager missing mode-filter snippet -> " + snippet)

    if "MutatorManager.RollAndApply(player, count, null, modeTag);" not in runtime_bridge:
        return fail("runtime bridge must pass modeTag into RollAndApply")
    if 'infiniteHellMode ? "InfiniteHell" : "BossRush"' not in waves:
        return fail("standard BossRush mutators must pass an explicit modeTag")
    if "TryRollMutatorsForMode(" not in waves:
        return fail("standard BossRush must roll a run mutator at StartFirstWave")
    if "public bool enableMutators = true;" not in config:
        return fail("mutators must default on so every BossRush mode rolls a run mutator")
    if 'ModName + "_EnableMutators"' not in config:
        return fail("mutator enable switch must be exposed through ModConfig")
    if 'ModName + "_MutatorCount"' not in config:
        return fail("mutator count must be exposed through ModConfig")

    # Mutators must NOT touch loot box quality / quantity / type anymore.
    forbidden_loot_symbols = [
        "LootQualityOffset",
        "LootQuantityMultiplier",
        "LootTypeFilter",
        "ApplyMutatorLootQualityOffset",
        "GetActiveMutatorLootQualityOffset",
        "MutatorCategory.LootChange",
    ]
    for symbol in forbidden_loot_symbols:
        if symbol in loot or symbol in definitions or symbol in manager:
            return fail("mutators must not alter loot box quality/quantity/type -> " + symbol)

    print("MutatorSemanticsGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
