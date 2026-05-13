"""Guard: capture Mode E/F spawn parity baselines before performance refactors."""

from pathlib import Path
import sys


MODEE_ALLOCATION = Path("ModeE/ModeESpawnAllocation.cs")
MODEE_BATTLE = Path("ModeE/ModeEBattle.cs")
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
    allocation = MODEE_ALLOCATION.read_text(encoding="utf-8")
    battle = MODEE_BATTLE.read_text(encoding="utf-8")
    respawn_e = MODEE_RESPAWN.read_text(encoding="utf-8")
    phases = MODEF_PHASES.read_text(encoding="utf-8")
    respawn_f = MODEF_RESPAWN.read_text(encoding="utf-8")

    allocate_body = extract_method_body(allocation, "private void AllocateSpawnPoints")
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
        (respawn_e, "int pressureCount = CountValidModeEAliveBosses() + GetModeERespawnPendingBossCount();", "Mode E respawn exact slot calculation must include alive and pending Bosses"),
        (respawn_e, "int availableSlots = MODE_E_RESPAWN_ALIVE_BOSS_LIMIT - pressureCount;", "Mode E respawn exact slot calculation must subtract from the existing limit"),
        (respawn_e, "List<Vector3> acceptedPoints = CopyModeERespawnAcceptedPoints(points, acceptedPointCount);", "Mode E respawn item clipping must preserve the first accepted points without allocating"),
        (respawn_e, "Teams faction = RespawnFactions[UnityEngine.Random.Range(0, RespawnFactions.Length)];", "Mode E respawn item faction selection baseline missing"),
        (respawn_e, "SpawnSingleModeEBoss(", "Mode E respawn items must continue using the shared Mode E spawn path"),
        (phases, "RefreshModeFBossTargets();", "Mode F target refresh call baseline missing"),
        (respawn_f, "modeFPendingRespawnCount += count;", "Mode F death respawn must keep pending count semantics"),
        (respawn_f, "modeFRespawnInFlightCount += 1;", "Mode F death respawn must keep in-flight count semantics"),
        (respawn_f, "CompleteModeFBossRespawnAttempt(configured, true);", "Mode F respawn success/failure completion baseline missing"),
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
