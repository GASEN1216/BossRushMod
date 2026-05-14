"""Guard: DragonKingBoss must keep removable per-instance death listeners."""

from pathlib import Path
import sys


SOURCE = Path("Integration/DragonKing/DragonKingBoss.cs")


def fail(message: str) -> int:
    print("DragonKingBossEventLifecycleGuard: FAIL - " + message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start < 0:
        return ""
    brace_start = text.find("{", start)
    if brace_start < 0:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    if not SOURCE.exists():
        return fail("missing source")

    text = SOURCE.read_text(encoding="utf-8")
    if "OnDeadEvent.AddListener((dmgInfo)" in text:
        return fail("anonymous OnDeadEvent listener is not removable")
    if "dragonKingDeathEventHandlers" not in text:
        return fail("missing per-instance death handler registry")

    spawn_block = extract_block(text, "public async UniTask<CharacterMainControl> SpawnDragonKing(")
    cleanup_block = extract_block(text, "private void CleanupTrackedDragonKingsOnArenaExit()")
    death_block = extract_block(text, "private void OnDragonKingDeath(")

    for block, label in [
        (spawn_block, "spawn"),
        (cleanup_block, "arena-exit cleanup"),
        (death_block, "death cleanup"),
    ]:
        if not block:
            return fail("missing " + label + " block")

    for snippet in [
        "UnityEngine.Events.UnityAction<DamageInfo> deathHandler",
        "dragonKingDeathEventHandlers[character] = deathHandler;",
        "character.Health.OnDeadEvent.AddListener(deathHandler);",
    ]:
        if snippet not in spawn_block:
            return fail("spawn missing removable death listener token: " + snippet)

    for snippet in [
        "dragonKingDeathEventHandlers.TryGetValue(character, out deathHandler)",
        "character.Health.OnDeadEvent.RemoveListener(deathHandler);",
        "dragonKingDeathEventHandlers.Clear();",
    ]:
        if snippet not in cleanup_block:
            return fail("arena-exit cleanup missing token: " + snippet)

    for snippet in [
        "dragonKingDeathEventHandlers.TryGetValue(deadKing, out deathHandler)",
        "deadKing.Health.OnDeadEvent.RemoveListener(deathHandler);",
        "dragonKingDeathEventHandlers.Remove(deadKing);",
    ]:
        if snippet not in death_block:
            return fail("death cleanup missing token: " + snippet)

    print("DragonKingBossEventLifecycleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
