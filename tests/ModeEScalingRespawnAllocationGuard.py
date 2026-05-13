"""Guard: Batch 5 Mode E scaling/respawn optimizations stay gameplay-equivalent."""

from pathlib import Path
import sys


RESPAWN = Path("ModeE/ModeERespawnItems.cs")
SCALING = Path("ModeE/ModeEBattle_ScalingAndRuntime.cs")


def fail(message: str) -> int:
    print("ModeEScalingRespawnAllocationGuard: FAIL - " + message)
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


def forbid(text: str, needle: str, message: str) -> int | None:
    if needle in text:
        return fail(message)
    return None


def main() -> int:
    respawn = RESPAWN.read_text(encoding="utf-8")
    scaling = SCALING.read_text(encoding="utf-8")

    for text, needle, message in (
        (respawn, "private readonly List<Vector3> modeERespawnSpawnPointScratch", "Mode E respawn must reuse a spawn-point scratch list"),
        (respawn, "private readonly List<Vector3> modeERespawnAcceptedPointScratch", "Mode E respawn must reuse the accepted-point list while the async task is running"),
        (respawn, "private const int MODE_E_RESPAWN_ALIVE_BOSS_LIMIT = 64;", "Mode E respawn limit must remain 64"),
        (respawn, "await UniTask.Delay(250);", "Mode E respawn cadence must remain 250ms"),
        (respawn, "Teams faction = RespawnFactions[UnityEngine.Random.Range(0, RespawnFactions.Length)];", "Mode E respawn faction randomization must remain unchanged"),
        (respawn, "int pressureCount = CountValidModeEAliveBosses() + GetModeERespawnPendingBossCount();", "Mode E exact slot check must still use valid alive plus pending counts"),
        (scaling, "public int appliedStacks = -1;", "Mode E enemy scaling state must remember the applied stack count"),
        (scaling, "if (scalingState.appliedStacks == personalStacks)", "Mode E scaling must skip repeated modifier rewrites for unchanged stacks"),
        (scaling, "float hpPercent = personalStacks * 0.05f;", "Mode E enemy HP scaling must remain 5 percent per stack"),
        (scaling, "float damagePercent = personalStacks * 0.05f;", "Mode E enemy damage scaling must remain 5 percent per stack"),
        (scaling, "float hpPercent = modeEPlayerLastHitKillCount * 0.001f;", "Mode E player HP growth must remain 0.1 percent per kill"),
        (scaling, "float damagePercent = modeEPlayerLastHitKillCount * 0.001f;", "Mode E player damage growth must remain 0.1 percent per kill"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    nearest_body = extract_method_body(respawn, "private List<Vector3> GetNearestSpawnPoints")
    if nearest_body is None:
        return fail("missing GetNearestSpawnPoints")
    for needle, message in (
        ("new List<Vector3>", "GetNearestSpawnPoints must not allocate a new List"),
        ("GetRange(", "GetNearestSpawnPoints must not allocate via GetRange"),
    ):
        result = forbid(nearest_body, needle, message)
        if result is not None:
            return result

    all_body = extract_method_body(respawn, "private List<Vector3> GetAllSpawnPoints")
    if all_body is None:
        return fail("missing GetAllSpawnPoints")
    result = forbid(all_body, "new List<Vector3>", "GetAllSpawnPoints must not allocate a new List")
    if result is not None:
        return result

    start_body = extract_method_body(respawn, "private bool TryStartModeERespawn")
    if start_body is None:
        return fail("missing TryStartModeERespawn")
    for needle, message in (
        ("points.GetRange", "TryStartModeERespawn must not allocate clipped spawn lists with GetRange"),
        ("new List<Vector3>(points)", "TryStartModeERespawn must not allocate a copied spawn list"),
    ):
        result = forbid(start_body, needle, message)
        if result is not None:
            return result

    print("ModeEScalingRespawnAllocationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
