"""Guard: Mode E/F performance work must not throttle gameplay-visible rules."""

from pathlib import Path
import re
import sys


MODEE_BATTLE = Path("ModeE/ModeEBattle.cs")
MODEE_RESPAWN = Path("ModeE/ModeERespawnItems.cs")
MODEF_PHASES = Path("ModeF/ModeFPhases.cs")
MODEF_RESPAWN = Path("ModeF/ModeFRespawn.cs")
MODEF_ENTRY = Path("ModeF/ModeFEntry.cs")
MODEE_DIR = Path("ModeE")
MODEF_DIR = Path("ModeF")


def fail(message: str) -> int:
    print("ModeEFNoGameplayThrottleGuard: FAIL - " + message)
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


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


def require(text: str, needle: str, message: str) -> int | None:
    if needle not in text:
        return fail(message)
    return None


def forbid(text: str, needle: str, message: str) -> int | None:
    if needle in text:
        return fail(message)
    return None


def scan_forbidden_tokens() -> int | None:
    banned = (
        "activeBossLimit",
        "maxActiveBoss",
        "maxActiveEnemies",
        "initialBossLimit",
        "spawnBudget",
        "spawnThrottle",
        "MODEF_INITIAL_BOSS_SPAWN_LIMIT",
        "MODEE_INITIAL_BOSS_SPAWN_LIMIT",
        "MODEF_ACTIVE_BOSS_LIMIT",
        "MODEE_ACTIVE_BOSS_LIMIT",
    )
    allowed = {
        "MODE_E_RESPAWN_ALIVE_BOSS_LIMIT",
    }

    for root in (MODEE_DIR, MODEF_DIR):
        for path in sorted(root.rglob("*.cs")):
            text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
            for token in banned:
                if token in allowed:
                    continue
                if token in text:
                    return fail(f"unexpected gameplay throttle token {token} in {path.as_posix()}")

    return None


def main() -> int:
    battle = MODEE_BATTLE.read_text(encoding="utf-8")
    respawn_e = MODEE_RESPAWN.read_text(encoding="utf-8")
    phases = MODEF_PHASES.read_text(encoding="utf-8")
    respawn_f = MODEF_RESPAWN.read_text(encoding="utf-8")
    entry = MODEF_ENTRY.read_text(encoding="utf-8")

    for text, needle, message in (
        (battle, "const int SPAWN_DELAY_MS = 500;", "Mode E startup spawn cadence must stay 500ms after the opening batch"),
        (battle, "const int INITIAL_BATCH_DELAY_MS = 800;", "Mode E startup opening cadence must stay 800ms"),
        (battle, "modeETotalSpawnExpected = spawnTasks.Count;", "Mode E startup must continue counting every queued spawn task"),
        (respawn_e, "private const int MODE_E_RESPAWN_ALIVE_BOSS_LIMIT = 64;", "Mode E respawn pressure limit must remain the existing 64"),
        (respawn_e, "await UniTask.Delay(250);", "Mode E respawn item cadence must stay 250ms"),
        (phases, "private const float MODEF_PREPARATION_DURATION = 180f;", "Mode F preparation duration must remain 180s"),
        (phases, "private const float MODEF_BOUNTY_DURATION = 180f;", "Mode F bounty duration must remain 180s"),
        (phases, "private const float MODEF_HUNTSTORM_DURATION = 180f;", "Mode F hunt-storm duration must remain 180s"),
        (phases, "private const float MODEF_FORCED_TRACE_DISTANCE = 500f;", "Mode F forced target trace distance must remain 500m"),
        (phases, "private const float MODEF_BOSS_RETARGET_INTERVAL = 1.5f;", "Mode F boss retarget interval must remain 1.5s"),
        (phases, "private const float MODEF_BOSS_INTEGRITY_CHECK_INTERVAL = 1f;", "Mode F boss integrity interval must remain 1s"),
        (entry, "ModeESpawnAllBosses(modeFSessionToken, relatedScene);", "Mode F initial spawn must continue using the same Mode E spawn path"),
        (respawn_f, "modeFRespawnInFlightCount > 0", "Mode F respawn must preserve one in-flight replacement at a time"),
        (respawn_f, "modeFRespawnInFlightCount += 1;", "Mode F respawn dispatch must still mark exactly one in-flight replacement"),
        (respawn_f, "modeFPendingRespawnCount = Mathf.Max(0, modeFPendingRespawnCount - 1);", "Mode F respawn dispatch must still decrement one pending replacement"),
    ):
        result = require(text, needle, message)
        if result is not None:
            return result

    spawn_all_body = extract_method_body(battle, "public async UniTaskVoid ModeESpawnAllBosses")
    if spawn_all_body is None:
        return fail("missing ModeESpawnAllBosses body")
    for needle, message in (
        ("spawnTasks.RemoveRange", "Mode E/F initial spawns must not cap or remove queued Boss tasks"),
        ("maxSpawnTasks", "Mode E/F initial spawns must not add a max spawn task throttle"),
        ("activeBossLimit", "Mode E/F initial spawns must not add an active boss limit"),
    ):
        result = forbid(spawn_all_body, needle, message)
        if result is not None:
            return result

    result = scan_forbidden_tokens()
    if result is not None:
        return result

    print("ModeEFNoGameplayThrottleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
