"""Recovery state has one owner; original lifecycle and mode adapters stay connected."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from compile_list import read_compile_sources
from cs_source_util import clean_source
from ArchitectureStructureGuard import extract_method_body


def read(path):
    return clean_source((ROOT / path).read_text(encoding="utf-8-sig"))


def compact(value):
    return re.sub(r"\s+", "", value)


def main():
    core = read("Utilities/EnemyRecoveryMonitor.cs")
    host = read("Utilities/EnemyRecoveryHostBridge.cs")
    registration = read("ModBehaviourRuntimeModules.cs")
    zombie = read("ZombieMode/ZombieModeRuntimeModule_Recovery.cs")
    arena = read("WavesArena/WavesArenaRuntimeModule_Recovery.cs")
    validation = read("WavesArena/WavesArenaBossSpawning.cs")
    assert "internal sealed class EnemyRecoveryMonitor" in core and "partial class ModBehaviour" not in core, "recovery algorithm must belong to independent owner"
    for mode_type in ("ZombieModeEnemyRuntimeMarker", "ZombieModeTuning", "ModeGRuntimeGates", "ModBehaviour.Instance"):
        assert mode_type not in core, "recovery core must receive mode policy: " + mode_type
    sources = [read(p) for p in read_compile_sources()]
    assert sum(len(re.findall(r"new\s+EnemyRecoveryMonitor\s*\(", src)) for src in sources) == 1, "one production recovery instance required"
    assert "private readonly EnemyRecoveryMonitor enemyRecoveryMonitor = new EnemyRecoveryMonitor();" in host, "host must own stable monitor reference"
    bindings = extract_method_body(host, "private void BindEnemyRecoveryServices(")
    expected = """{
        zombieRuntime.BindEnemyRecoveryMonitor(enemyRecoveryMonitor);
        wavesArenaRuntime.BindEnemyRecoveryMonitor(enemyRecoveryMonitor);
        enemyRecoveryMonitor.BindModeQueries(() => modeDActive, () => modeEActive, () => modeFActive,
            () => IsActive, () => IsZombieModeActive, () => ModeGRuntimeGates.IsModeGRunInProgress);
        enemyRecoveryMonitor.BindTrackedEnemies(() => modeDCurrentWaveEnemies, () => modeEAliveEnemies,
            () => modeFState.ActiveBosses, ModeGRuntimeGates.GetTrackedBosses,
            zombieRuntime.MonitorZombieModeEnemyRecovery, wavesArenaRuntime.MonitorNormalBossRushRecovery);
        enemyRecoveryMonitor.BindSpawnPositions(() => modeESpawnAllocation, GetModeEFlattenedSpawnPoints,
            GetCurrentSceneSpawnPoints, position => GenerateFallbackSpawnPointsAroundPlayer(position),
            zombieRuntime.AppendZombieModeRecoverySpawnCandidates, zombieRuntime.TryGetZombieModeReliableSpawnPosition);
        enemyRecoveryMonitor.BindRecoveryPolicies(marker => ((ZombieModeEnemyRuntimeMarker)marker).IsBoss,
            () => ZombieModeTuning.NormalZombieDistantRecoveryDistance,
            () => ZombieModeTuning.NormalZombieDistantRecoveryDelaySeconds,
            (go, marker) => ZombieModeRuntimeModule.GetZombieModeEnemyAI(go, (ZombieModeEnemyRuntimeMarker)marker),
            ApplyModeFPressureToBoss);
    }"""
    assert compact(bindings) == compact(expected), "same recovery owner and original mode queries/policies must bind in order"
    assert compact("BindEnemyRecoveryServices(zombieRuntime); runtimeModuleHost.Register(zombieRuntime);") in compact(registration), "recovery bindings must precede Zombie registration"
    for signature, call in (
        ("private void ClearEnemyRecoveryMonitorState()", "enemyRecoveryMonitor.ClearEnemyRecoveryMonitorState();"),
        ("private void RegisterEnemyRecoveryAnchor(", "enemyRecoveryMonitor.RegisterEnemyRecoveryAnchor(enemy, anchorPosition);"),
        ("private void UnregisterEnemyRecovery(", "enemyRecoveryMonitor.UnregisterEnemyRecovery(enemy);"),
        ("private void UpdateEnemyRecoveryMonitor()", "enemyRecoveryMonitor.UpdateEnemyRecoveryMonitor();"),
    ):
        assert compact(extract_method_body(host, signature)) == compact("{" + call + "}"), "disconnected recovery bridge: " + signature
    for source in (zombie, arena):
        assert compact(extract_method_body(source, "internal void BindEnemyRecoveryMonitor(")) == "{enemyRecoveryMonitor=monitor;}", "mode must retain the shared recovery owner"
    for signature, call in (
        ("private void ValidateAndFixBossPosition(", "enemyRecoveryMonitor.ValidateAndFixBossPosition(boss);"),
        ("private IEnumerator DelayedBossPositionValidation(", "return enemyRecoveryMonitor.DelayedBossPositionValidation(boss, delay);"),
    ):
        assert compact(extract_method_body(validation, signature)) == compact("{" + call + "}"), "spawn validation must share recovery state"
    hooks = read("Utilities/GameplayRuntimeHooks.cs")
    assert "UpdateEnemyRecoveryMonitor();" in hooks and "ClearEnemyRecoveryMonitorState();" in hooks, "original Tick/cleanup hooks required"
    assert "ClearEnemyRecoveryMonitorState();" in read("ModBehaviour.cs"), "original destroy cleanup required"
    print("EnemyRecoveryOwnershipGuard: PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as error:
        print("EnemyRecoveryOwnershipGuard: FAIL - " + str(error))
        raise SystemExit(1)
