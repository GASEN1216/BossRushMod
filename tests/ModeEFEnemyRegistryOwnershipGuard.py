"""Pin shared enemy ownership while keeping Mode E gameplay policy in its module."""
from pathlib import Path
import re
from cs_source_util import clean_source
from ArchitectureStructureGuard import extract_method_body

ROOT = Path(__file__).resolve().parents[1]


def read(relative):
    return clean_source((ROOT / relative).read_text(encoding="utf-8-sig"))


def compact(text):
    return re.sub(r"\s+", "", text)


def require(text, value, message):
    assert compact(value) in compact(text), message


def main():
    registry = read("Utilities/ModeEFEnemyRegistry.cs")
    registration = read("ModBehaviourRuntimeModules.cs")
    scaling = read("ModeE/ModeEBattle_ScalingAndRuntime.cs")
    startup = read("ModeE/ModeEStartup.cs")
    respawn = read("ModeF/ModeFRespawn.cs")
    require(registry, "internal sealed class ModeEFEnemyRegistry", "shared registry must have an independent owner")
    assert not re.search(r"\b(?:ModBehaviour|Mode[DEF]RuntimeModule)\s+\w+", registry), "registry must not hold a concrete mode or host"
    for forbidden in ("modeEActive", "modeEPlayerFaction", "ModeEShellReward", "ModeEEnemyScalingState", "dropBoxOnDead"):
        assert forbidden not in registry, "Mode E gameplay policy must stay in Mode E: " + forbidden
    for name in ("modeEAliveEnemies", "modeEAliveEnemySet", "modeEBossRegenCache", "modeEAliveEnemyFactionMap",
                 "modeEFactionAliveMap", "modeEEnemyDeathHandlers", "modeEEnemyLootHandlers"):
        assert re.search(r"private readonly [^;\n]*\b" + name + r"\s*=", registry), "shared state must be private: " + name
        assert not re.search(r"(?:private|internal) (?:readonly )?[^;\n{]*\b" + name + r"\s*=", scaling + read("ModeE/ModeE.cs")), "Mode E must not duplicate shared state: " + name
    assert registration.count("new ModeEFEnemyRegistry()") == 1, "one shared registry per host"
    for statement in (
        "modeEFEnemyRegistry.BindDeathCallback(modeERuntime.OnModeEEnemyDeath);",
        "modeERuntime.BindEnemyRegistry(modeEFEnemyRegistry);",
        "modeFRuntime.BindEnemyRegistry(modeEFEnemyRegistry);",
    ):
        require(registration, statement, "both modes must bind the same registry")
    for module in ("ModeE/ModeERuntimeModule.cs", "ModeF/ModeFRuntimeModule.cs"):
        require(read(module), "private ModeEFEnemyRegistry enemyRegistry;", "module must retain the supplied registry")
    for name in ("TrackModeEAliveEnemy", "RegisterModeEEnemyDeath", "UnregisterModeEEnemyDeath", "UnregisterModeEEnemyLootHandler", "UntrackModeEAliveEnemy"):
        require(respawn, "enemyRegistry." + name + "(", "Mode F must use shared registration directly: " + name)
        assert "modeE." + name not in respawn, "Mode F must not borrow registry through Mode E: " + name
    require(respawn, "modeE.RemovePendingModeEAggroTarget(boss);", "Mode E aggro cleanup must be an explicit action")
    assert "modeE.modeEPendingAggroTraceDistance" not in respawn, "Mode F must not mutate Mode E private aggro state"
    loot = extract_method_body(scaling, "private void RegisterModeEEnemyLootHandler(")
    for value in ("UnregisterModeEEnemyLootHandler(enemy);", "if (!modeEActive || capturedFaction != modeEPlayerFaction || capturedEnemy == null)",
                  "capturedEnemy.dropBoxOnDead = false;", "enemyRegistry.AttachLootHandler(enemy, handler);"):
        require(loot, value, "Mode E must retain its live loot policy")
    reset = extract_method_body(startup, "internal void ResetModeESharedRuntimeState(")
    order = ["enemyRegistry.ClearAliveEnemies();", "enemyRegistry.ClearAliveEnemySet();", "ClearModeEBossRegenCache();",
             "modeEHost.ClearModeEFSpawnPostprocessScheduler();", "enemyRegistry.ClearFactionLookup();", "modeEFactionDeathCount.Clear();",
             "enemyRegistry.ClearFactionAliveLists();", "modeEEnemyScalingStates.Clear();", "modeEPlayerLastHitKillCount = 0;",
             "enemyRegistry.ClearDeathHandlers();", "enemyRegistry.ClearLootHandlers();"]
    positions = [reset.find(token) for token in order]
    assert min(positions) >= 0 and positions == sorted(positions), "reset must retain interleaving with Mode E gameplay state"
    print("ModeEFEnemyRegistryOwnershipGuard: PASS")


if __name__ == "__main__":
    main()
