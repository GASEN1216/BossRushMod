"""ZombieModeOriginalCharacterIsolationGuard: original non-player characters must be isolated reversibly, not destroyed."""

from pathlib import Path
import sys


ISOLATION = Path("ZombieMode/ZombieModeMapIsolation.cs")
HELPER = Path("Utilities/OriginalCharacterIsolationHelper.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print("ZombieModeOriginalCharacterIsolationGuard: FAIL - " + message)
    return 1


def main() -> int:
    isolation = ISOLATION.read_text(encoding="utf-8")
    helper = HELPER.read_text(encoding="utf-8") if HELPER.exists() else ""
    compile_text = COMPILE.read_text(encoding="utf-8")

    required_isolation_tokens = [
        "OriginalCharacterIsolationHelper.Disable(",
        "OriginalCharacterIsolationHelper.Restore(",
        "RestoreZombieModeOriginalCharacters()",
    ]
    for token in required_isolation_tokens:
        if token not in isolation:
            return fail("missing reversible character isolation token -> " + token)

    forbidden_isolation_tokens = [
        "Destroy(character.gameObject)",
        "private void ClearZombieModeOriginalEnemies()",
    ]
    for token in forbidden_isolation_tokens:
        if token in isolation:
            return fail("destructive original-character isolation remains -> " + token)

    required_helper_tokens = [
        "internal sealed class OriginalCharacterIsolationRecord",
        "internal static class OriginalCharacterIsolationHelper",
        "public GameObject GameObject;",
        "public bool WasActive;",
        "public bool WasEnabled;",
        "CharacterMainControl[] characters = UnityEngine.Object.FindObjectsOfType<CharacterMainControl>(true);",
        "character.enabled = false;",
        "character.gameObject.SetActive(false);",
        "character.enabled = record.WasEnabled;",
        "record.GameObject.SetActive(record.WasActive);",
    ]
    for token in required_helper_tokens:
        if token not in helper:
            return fail("missing helper behavior -> " + token)

    if "Utilities\\OriginalCharacterIsolationHelper.cs ^" not in compile_text:
        return fail("compile_official.bat must include Utilities\\OriginalCharacterIsolationHelper.cs")

    print("ZombieModeOriginalCharacterIsolationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
