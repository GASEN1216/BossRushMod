"""
Guard: removed prompt implementation identifiers must not return in text files.
"""

from pathlib import Path
import sys


ROOT = Path(".")
SKIP_DIRS = {".git", "__pycache__"}
TEXT_SUFFIXES = {".bat", ".cs", ".json", ".md", ".py", ".txt", ".xml"}


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    removed_type = "Confirm" + "Dialog" + "UI"
    removed_view = "Confirm" + "Dialog" + "View"
    removed_namespace = "BossRush" + "." + "UI"
    removed_guard = "Confirm" + "Dialog" + "UI" + "ViewGuard"
    forbidden = (removed_type, removed_view, removed_namespace, removed_guard)

    for path in ROOT.rglob("*"):
        if any(part in SKIP_DIRS for part in path.parts):
            continue

        path_text = path.as_posix()
        for token in forbidden:
            if token in path_text:
                return fail("RemovedLegacyPromptGuard: removed prompt path still exists -> " + path_text)

        if not path.is_file() or path.suffix.lower() not in TEXT_SUFFIXES:
            continue

        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue

        for token in forbidden:
            if token in text:
                return fail("RemovedLegacyPromptGuard: removed prompt text remains in " + path_text)

    print("RemovedLegacyPromptGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
