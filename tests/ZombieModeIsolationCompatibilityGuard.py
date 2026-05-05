"""ZombieModeIsolationCompatibilityGuard: map isolation must stay shared-compatible with BossRush entry cleanup."""

from pathlib import Path
import sys


ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")
COMPILE = Path("compile_official.bat")
def fail(message: str) -> int:
    print("ZombieModeIsolationCompatibilityGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = ISOLATION.read_text(encoding="utf-8")
    compile_text = COMPILE.read_text(encoding="utf-8")

    required_tokens = [
        "spawnersDisabled = false;",
        "DisableAllSpawners();",
        "RestoreZombieModeOriginalSpawners()",
        "INPCController",
        "NPCInteractableBase",
        "NPCModuleRegistry",
    ]
    for token in required_tokens:
        if token not in text:
            return fail("missing token -> " + token)

    forbidden_tokens = [
        "OriginalSpawnerIsolationHelper.Disable(",
        "OriginalSpawnerIsolationHelper.Restore(",
        "typeName.IndexOf(\"Quest\"",
        "typeName.IndexOf(\"Dialogue\"",
        "typeName.IndexOf(\"Merchant\"",
        "typeName.IndexOf(\"Npc\"",
        "typeName.IndexOf(\"NPC\"",
        "GetComponentsInChildren<MonoBehaviour>(true)",
    ]
    for token in forbidden_tokens:
        if token in text:
            return fail("stale heuristic or old reversible spawner isolation remains -> " + token)

    if "ZombieMode\\ZombieModeMapIsolation.cs ^" not in compile_text:
        return fail("compile_official.bat must include ZombieMode\\ZombieModeMapIsolation.cs")

    print("ZombieModeIsolationCompatibilityGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
