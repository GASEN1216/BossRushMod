"""Guard: track empty catch blocks so new silent exception sinks do not grow."""

from pathlib import Path
import re
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests", "鸭科夫源码"}
BUDGET_FILE = Path(__file__).resolve().parent / "empty_catch_budget.txt"

EMPTY_CATCH_RE = re.compile(r"catch\s*(?:\([^)]*\))?\s*\{\s*\}", re.S)


def should_exclude(path: Path) -> bool:
    rel = path.relative_to(PROJECT_ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def load_budget() -> int:
    try:
        return int(BUDGET_FILE.read_text(encoding="utf-8").strip())
    except Exception:
        return 0


def main() -> int:
    budget = load_budget()
    print(f"EmptyCatchGuard: checking empty catch budget (budget={budget})...")

    total = 0
    offenders = []
    for cs_file in sorted(PROJECT_ROOT.rglob("*.cs")):
        if should_exclude(cs_file):
            continue

        text = cs_file.read_text(encoding="utf-8", errors="ignore")
        count = len(EMPTY_CATCH_RE.findall(text))
        if count:
            offenders.append((cs_file.relative_to(PROJECT_ROOT).as_posix(), count))
            total += count

    if total > budget:
        print(f"EmptyCatchGuard: FAIL empty catch count {total} exceeds budget {budget}")
        for rel, count in offenders[:200]:
            print(f"  FAIL {rel}: {count}")
        return 1

    print(f"EmptyCatchGuard: PASS empty catch count {total} within budget {budget}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
