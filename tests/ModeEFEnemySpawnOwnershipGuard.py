"""E/F must share the same independently owned faction spawn runtime."""
from pathlib import Path
import re
from cs_source_util import clean_source
from ArchitectureStructureGuard import extract_method_body

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def compact(value):
    return re.sub(r"\s+", "", value)


def require(text, statement, label):
    assert compact(statement) in compact(text), label + ": " + statement


def main():
    service = read("Utilities/ModeEFEnemySpawnRuntime.cs")
    registration = read("ModBehaviourRuntimeModules.cs")
    e = read("ModeE/ModeERuntimeModule.cs")
    f = read("ModeF/ModeFRuntimeModule.cs")
    startup = read("ModeE/ModeEStartup.cs")
    respawn_e = read("ModeE/ModeERespawnItems.cs")
    respawn_f = read("ModeF/ModeFRespawn.cs")
    battle = read("ModeE/ModeEBattle.cs")
    require(service, "internal sealed class ModeEFEnemySpawnRuntime", "independent owner")
    assert not re.search(r"\b(?:ModeERuntimeModule|ModeFRuntimeModule|ModeDRuntimeModule)\b", service), "shared service must use narrow callbacks"
    assert registration.count("new ModeEFEnemySpawnRuntime(") == 1, "one shared spawn runtime"
    for statement in (
        "modeEFEnemySpawnRuntime = new ModeEFEnemySpawnRuntime(modeEFSpawnPreparation, enemySpawnRuntime);",
        "modeERuntime.BindEnemySpawnRuntime(modeEFEnemySpawnRuntime);",
        "modeFRuntime.BindEnemySpawnRuntime(modeEFEnemySpawnRuntime);",
        "modeEFEnemySpawnRuntime.BindSpawnCallbacks(modeERuntime.IsModeEOrModeFSpawnSessionStillValid, modeERuntime.OnModeEEnemySpawned);",
        "modeEFEnemySpawnRuntime.BindPresetQueries(GetFilteredEnemyPresets, () => modeDRuntime.MinionPresets, IsDragonKingPreset, IsDragonDescendantPreset, wavesArenaRuntime.InitializeEnemyPresets, () => modeDRuntime.InitializeModeDEnemyPools(wavesArenaRuntime.EnemyPresets, wavesArenaRuntime.GetLocalizedCharacterName));",
    ):
        require(registration, statement, "shared instance assembly")
    for module in (e, f):
        require(module, "private ModeEFEnemySpawnRuntime spawnRuntime;", "module references service")
        body = extract_method_body(module, "internal void BindEnemySpawnRuntime")
        assert compact(body) == compact("{ spawnRuntime = runtime; }"), "binding must retain supplied shared owner"
    for name in ("modeETotalSpawnExpected", "modeESpawnResolved", "modeEDragonDescendantSpawned",
                 "modeEDragonKingSpawned", "modeEWolfBossCount", "modeEWolfBossAssigned"):
        assert re.search(r"private\s+(?:int|bool)\s+" + name + r"\s*=", service), "spawn state must remain private: " + name
        assert not re.search(r"(?:private|internal)\s+(?:int|bool)\s+" + name + r"\s*=", battle), "Mode E must not duplicate shared state: " + name
    require(service, "private readonly Dictionary<Teams, List<EnemyPresetInfo>> modeEBossPoolByFaction", "preset owner")
    require(service, "private readonly Dictionary<Teams, List<EnemyPresetInfo>> modeEMinionPoolByFaction", "minion owner")
    require(service, "IsModeEOrModeFSpawnSessionStillValid = isSessionValid;", "active query binding")
    require(service, "OnModeEEnemySpawned = onSpawned;", "commit binding")
    require(service, "spawnCore.SpawnEnemyCore(", "same shared spawn core")
    require(startup, "spawnRuntime.ResetSpawnTracking();", "reset in original shared reset")
    reset = extract_method_body(service, "internal void ResetSpawnTracking")
    assert compact(reset) == compact("""{
        modeETotalSpawnExpected = 0;
        modeESpawnResolved = 0;
        modeEDragonDescendantSpawned = false;
        modeEDragonKingSpawned = false;
        modeEWolfBossCount = 0;
        modeEWolfBossAssigned = 0;
    }"""), "reset preserves six original assignments and cache lifetime"
    for text, statement in (
        (startup, "spawnRuntime.EnsureModeEFSpawnPoolsReady(\"StartModeE\");"),
        (read("ModeF/ModeFEntry.cs"), "spawnRuntime.ModeESpawnAllBosses(modeFSessionToken, relatedScene);"),
        (respawn_e, "spawnRuntime.SpawnSingleModeEBoss("),
        (respawn_f, "spawnRuntime.EnsureModeEFSpawnPoolsReady(\"ModeF.RespawnModeFBoss\");"),
        (respawn_f, "spawnRuntime.SyncModeEDragonDescendantSpawnFlag(selectedDragonDescendant, spawnedPreset, \"ModeF\");"),
        (battle, "spawnRuntime.ResolveModeESpawnAttempt();"),
        (battle, "spawnRuntime.ContainsBossPreset(finalPreset)"),
        (battle, "spawnRuntime.ContainsMinionPreset(finalPreset)"),
    ):
        require(text, statement, "production consumer wiring")
    assert not re.search(r"\bmodeE\.(?:modeEDragonDescendantSpawned|EnsureModeEFSpawnPoolsReady|ModeESpawnAllBosses)\b", respawn_f + read("ModeF/ModeFEntry.cs")), "F must use shared owner directly"
    print("ModeEFEnemySpawnOwnershipGuard: PASS")


if __name__ == "__main__":
    main()
