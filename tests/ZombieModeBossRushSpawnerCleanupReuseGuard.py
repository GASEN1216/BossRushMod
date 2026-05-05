"""ZombieModeBossRushSpawnerCleanupReuseGuard: original map spawner cleanup must reuse BossRush entry logic."""

from pathlib import Path
import sys


MAP_ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")


def fail(message: str) -> int:
    print("ZombieModeBossRushSpawnerCleanupReuseGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    text = MAP_ISOLATION.read_text(encoding="utf-8")

    disable_method = extract_method(text, "private void DisableZombieModeOriginalSpawners")
    if not disable_method:
        return fail("DisableZombieModeOriginalSpawners not found")

    for token in [
        "spawnersDisabled = false;",
        "DisableAllSpawners();",
        "已复用 BossRush 进图逻辑清理原版刷怪器",
    ]:
        if token not in disable_method:
            return fail("missing BossRush reuse token -> " + token)

    forbidden = [
        "OriginalSpawnerIsolationHelper.Disable(",
        "CharacterSpawnerRoot",
        "Destroy(spawner.gameObject)",
        "_cachedCreatedField.SetValue(",
    ]
    for token in forbidden:
        if token in disable_method:
            return fail("ZombieMode spawner cleanup must not maintain its own parallel path -> " + token)

    restore_method = extract_method(text, "private void RestoreZombieModeOriginalSpawners")
    if not restore_method:
        return fail("RestoreZombieModeOriginalSpawners not found")

    for token in [
        "spawnersDisabled = false;",
    ]:
        if token not in restore_method:
            return fail("restore must reset BossRush spawner scan flag -> " + token)

    forbidden_restore = [
        "OriginalSpawnerIsolationHelper.Restore(",
    ]
    for token in forbidden_restore:
        if token in restore_method:
            return fail("restore must not attempt reversible original spawner restore -> " + token)

    print("ZombieModeBossRushSpawnerCleanupReuseGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
