"""Guard: Mode E/F spawn pool prewarm stays centralized and behavior-neutral."""

from pathlib import Path
import sys


BATTLE = Path("ModeE/ModeEBattle.cs")
STARTUP = Path("ModeE/ModeEStartup.cs")
MODEF_ENTRY = Path("ModeF/ModeFEntry.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")


def fail(message: str) -> int:
    print("ModeEFPrewarmCacheGuard: FAIL - " + message)
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
    battle = BATTLE.read_text(encoding="utf-8")
    startup = STARTUP.read_text(encoding="utf-8")
    entry = MODEF_ENTRY.read_text(encoding="utf-8")
    respawn = MODEF_RESPAWN.read_text(encoding="utf-8")

    ensure_body = extract_method_body(battle, "private void EnsureModeEFSpawnPoolsReady")
    if ensure_body is None:
        return fail("missing EnsureModeEFSpawnPoolsReady")
    for needle, message in (
        ("InitializeEnemyPresets();", "prewarm helper must include enemy preset discovery"),
        ("InitializeModeDEnemyPools();", "prewarm helper must include Mode D minion cache"),
        ("BuildModeEFactionPresetCaches();", "prewarm helper must include Mode E/F faction caches"),
        ("DevLog(\"[ModeE/F] [WARNING] 生成池缓存预热失败", "prewarm failures must warn and preserve fallback behavior"),
    ):
        result = require(ensure_body, needle, message)
        if result is not None:
            return result

    for text, needle, message in (
        (startup, "EnsureModeEFSpawnPoolsReady(\"StartModeE\");", "Mode E startup must use centralized spawn-pool prewarm"),
        (entry, "EnsureModeEFSpawnPoolsReady(\"StartModeF\");", "Mode F startup must use centralized spawn-pool prewarm"),
        (respawn, "EnsureModeEFSpawnPoolsReady(\"ModeF.RespawnModeFBoss\");", "Mode F respawn dispatch must ensure caches before spawning"),
        (respawn, "EnsureModeEFSpawnPoolsReady(\"ModeF.GetRandomModeFRespawnBossPreset\");", "Mode F respawn picker must keep an idempotent fallback ensure"),
        (respawn, "modeFRespawnInFlightCount > 0", "Mode F respawn must keep one in-flight replacement"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    respawn_body = extract_method_body(respawn, "private bool RespawnModeFBoss")
    if respawn_body is None:
        return fail("missing RespawnModeFBoss")
    for needle, message in (
        ("InitializeEnemyPresets();", "Mode F respawn dispatch must not hand-roll preset initialization"),
        ("InitializeModeDEnemyPools();", "Mode F respawn dispatch must not hand-roll Mode D pool initialization"),
    ):
        result = forbid(respawn_body, needle, message)
        if result is not None:
            return result

    print("ModeEFPrewarmCacheGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
