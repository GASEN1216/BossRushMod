"""Guard: Mode E/F must not branch gameplay by hardware or performance tier."""

from pathlib import Path
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
SCAN_PATHS = [
    PROJECT_ROOT / "ModeE",
    PROJECT_ROOT / "ModeF",
    PROJECT_ROOT / "Utilities" / "EnemySpawnCore.cs",
]

BANNED_RE = re.compile(
    r"low\s*spec|lowspec|performance\s*tier|performancetier|hardware\s*tier|hardwaretier|fps\s*tier|fpstier",
    re.IGNORECASE,
)


def fail(message: str) -> int:
    print("ModeEFNoLowSpecScalingGuard: FAIL - " + message)
    return 1


def iter_sources():
    for scan_path in SCAN_PATHS:
        if scan_path.is_file():
            yield scan_path
            continue

        if not scan_path.exists():
            continue

        for path in sorted(scan_path.rglob("*.cs")):
            yield path


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.DOTALL)
    return "\n".join(line.split("//", 1)[0] for line in text.splitlines())


def main() -> int:
    failures = []
    for path in iter_sources():
        text = strip_comments(path.read_text(encoding="utf-8", errors="ignore"))
        for line_no, line in enumerate(text.splitlines(), 1):
            if BANNED_RE.search(line):
                rel = path.relative_to(PROJECT_ROOT).as_posix()
                failures.append(f"{rel}:{line_no}: {line.strip()}")

    if failures:
        print("ModeEFNoLowSpecScalingGuard: Mode E/F gameplay must use one shared configuration.")
        for failure in failures:
            print("  " + failure)
        return 1

    print("ModeEFNoLowSpecScalingGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
