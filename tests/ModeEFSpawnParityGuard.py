"""Guard: capture Mode E/F spawn parity baselines before performance refactors."""

from pathlib import Path
import sys
from cs_source_util import clean_source


MODEE_ALLOCATION = Path("Utilities/ModeEFSpawnPreparation.cs")
MODEE_BATTLE = Path("Utilities/ModeEFEnemySpawnRuntime.cs")
MODEE_RESPAWN = Path("ModeE/ModeERespawnItems.cs")
MODEF_PHASES = Path("ModeF/ModeFPhases.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")


def fail(message: str) -> int:
    print("ModeEFSpawnParityGuard: FAIL - " + message)
    return 1


def extract_method_body(text: str, signature: str) -> str | None:
    start = text.find(signature)
    if start < 0:
        return None

    brace_start = text.find("{", start)
    if brace_start < 0:
        return None

    depth = 0
    for idx in range(brace_start, len(text)):
        ch = text[idx]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[brace_start : idx + 1]

    return None


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def require_ordered(text: str, needles: list[str], message: str) -> int | None:
    cursor = -1
    for needle in needles:
        idx = text.find(needle, cursor + 1)
        if idx < 0:
            return fail(message + " missing " + needle)
        cursor = idx
    return None


def main() -> int:
    def read_source(path):
        return clean_source(Path(path).read_text(encoding="utf-8"))

    allocation = read_source(MODEE_ALLOCATION)
    registration = read_source("ModBehaviourRuntimeModules.cs")
    mode_e = read_source("ModeE/ModeESpawnAllocation.cs")
    mode_f = read_source("ModeF/ModeFEntry.cs")
    reset = read_source("ModeE/ModeEStartup.cs")
    for text, needle in (
        (allocation, "internal sealed class ModeEFSpawnPreparation"),
        (registration, "modeERuntime.BindSharedServices(modeDRuntime, wavesArenaRuntime, modeEFSpawnPreparation, modeEFMerchantCatalog);"),
        (registration, "modeFRuntime.BindSharedServices(modeDRuntime, modeERuntime, wavesArenaRuntime, modeEFSpawnPreparation,"),
        (registration, "() => modeERuntime.ModeEPlayerFaction"),
        (mode_e, "spawnPreparation.AllocateSpawnPoints();"),
        (mode_f, "spawnPreparation.AllocateSpawnPoints();"),
        (reset, "spawnPreparation.Reset(clearSpawnAllocation, clearSpawnerCache);"),
        (read_source("ModeE/ModeERuntimeModule.cs"), "this.spawnPreparation = spawnPreparation;"),
        (read_source("ModeF/ModeFRuntimeModule.cs"), "this.spawnPreparation = spawnPreparation;"),
    ):
        if needle not in text:
            return fail("shared E/F spawn preparation wiring missing -> " + needle)
    if registration.count("new ModeEFSpawnPreparation(") != 1:
        return fail("E/F must share exactly one spawn preparation instance")
    battle = read_source(MODEE_BATTLE)
    respawn_e = read_source(MODEE_RESPAWN)
    phases = read_source(MODEF_PHASES)
    respawn_f = read_source(MODEF_RESPAWN)

    allocate_body = extract_method_body(allocation, "internal void AllocateSpawnPoints")
    if allocate_body is None:
        return fail("missing AllocateSpawnPoints body")

    for text, needle, message in (
        (allocation, "private const float MODE_E_SPAWN_MIN_DISTANCE = 10f;", "Mode E minimum spawn spacing must stay 10m"),
        (allocation, "private const float MODE_E_SPAWN_MIN_DISTANCE_SQR = MODE_E_SPAWN_MIN_DISTANCE * MODE_E_SPAWN_MIN_DISTANCE;", "Mode E spacing check must use the squared 10m constant"),
        (allocate_body, "Array.Sort(sorted", "Mode E spawn allocation must keep player-distance sorting"),
        (allocate_body, "distA.CompareTo(distB)", "Mode E spawn allocation sort direction must stay nearest first"),
        (allocation, "private static List<Vector3> FilterModeESpawnPointsByDistanceGrid(Vector3[] sorted)", "Mode E spawn spacing filter must use the grid-equivalent helper"),
        (allocation, "private static Vector2Int GetModeESpawnPointGridCell(Vector3 point)", "Mode E grid filter must keep a deterministic x/z cell helper"),
        (allocation, "dx <= 1", "Mode E grid filter must inspect neighboring x cells"),
        (allocation, "dz <= 1", "Mode E grid filter must inspect neighboring z cells"),
        (allocation, "(candidate - acceptedPoint).sqrMagnitude < MODE_E_SPAWN_MIN_DISTANCE_SQR", "Mode E spawn spacing predicate must stay strictly less than the squared threshold"),
        (allocate_body, "List<Vector3> filtered = FilterModeESpawnPointsByDistanceGrid(sorted);", "Mode E allocation must use the grid-equivalent filtered output"),
        (allocate_body, "bool isPlayerFaction = (modeEPlayerFaction == Teams.player);", "Mode E lone-player flag must keep its special allocation path"),
        (allocate_body, "orderedFactions[0] = ModeEAvailableFactions[playerFactionIdx];", "Mode E selected faction must still receive nearest spawn priority"),
        (allocate_body, "Teams faction = orderedFactions[i % factionCount];", "Mode E per-faction allocation must remain round-robin"),
        (allocate_body, "modeESpawnAllocation[faction].Add(filtered[i]);", "Mode E per-faction allocation must add the same filtered point order"),
        (battle, "modeETotalSpawnExpected = spawnTasks.Count;", "Mode E startup queued count baseline missing"),
        (battle, "countSpawnAttemptImmediately: false", "Mode E startup must not double-count pre-counted attempts"),
        (respawn_e, "List<Vector3> acceptedPoints = CopyModeERespawnAcceptedPoints(points, points.Count);", "Mode E respawn items must copy every selected point into async-owned storage"),
        (respawn_e, "modeERespawnTaskRunning = true;", "Mode E respawn items must preserve single-task gating"),
        (respawn_e, "Teams faction = RespawnFactions[UnityEngine.Random.Range(0, RespawnFactions.Length)];", "Mode E respawn item faction selection baseline missing"),
        (respawn_e, "SpawnSingleModeEBoss(", "Mode E respawn items must continue using the shared Mode E spawn path"),
        (phases, "RefreshModeFBossTargets();", "Mode F target refresh call baseline missing"),
        (respawn_f, "modeFPendingRespawnCount += count;", "Mode F death respawn must keep pending count semantics"),
        (respawn_f, "modeFRespawnInFlightCount += 1;", "Mode F death respawn must keep in-flight count semantics"),
        (respawn_f, "onCommit: (ctx) => ConfigureModeFRespawnedBoss(ctx, selectedDragonDescendant, spawnPos)", "Mode F respawn must still finish registration through the shared commit barrier"),
        (respawn_f, "CompleteModeFBossRespawnAttempt(true, true);", "Mode F respawn success completion baseline missing"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    result = require_ordered(
        allocate_body,
        [
            "Array.Sort(sorted",
            "List<Vector3> filtered = FilterModeESpawnPointsByDistanceGrid(sorted);",
            "bool isPlayerFaction = (modeEPlayerFaction == Teams.player);",
            "for (int i = 0; i < filtered.Count; i++)",
            "modeESpawnAllocation[faction].Add(filtered[i]);",
            "RebuildModeEFlattenedSpawnPointCache();",
        ],
        "Mode E allocation steps must remain sort -> filter -> faction order -> round-robin -> cache rebuild",
    )
    if result is not None:
        return result

    print("ModeEFSpawnParityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
