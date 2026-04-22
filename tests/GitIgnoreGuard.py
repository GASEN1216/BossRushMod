"""
Guard: repository ignore rules should only ignore tests build artifacts.
"""

from pathlib import Path
import sys


GITIGNORE = Path(".gitignore")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    lines = GITIGNORE.read_text(encoding="utf-8").splitlines()
    normalized = [line.strip() for line in lines]

    if "/tests" in normalized:
        return fail(".gitignore regression: broad /tests ignore is still present")

    required_entries = ["/tests/bin", "/tests/obj"]
    missing = [entry for entry in required_entries if entry not in normalized]
    if missing:
        return fail(".gitignore regression: missing test artifact ignore entries: " + ", ".join(missing))

    print("GitIgnoreGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
