"""
Guard: static cache allowlist must not grow.

Batch A records the current debt budget in tests/static_cache_allowlist_budget.txt.
Later batches should reduce that number when entries are removed.
"""

from pathlib import Path
import sys


TESTS_DIR = Path(__file__).resolve().parent
ALLOWLIST_FILE = TESTS_DIR / "static_cache_allowlist.txt"
BUDGET_FILE = TESTS_DIR / "static_cache_allowlist_budget.txt"


def effective_lines(path: Path) -> list:
    if not path.exists():
        return []
    result = []
    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if stripped and not stripped.startswith("#"):
            result.append(stripped)
    return result


def load_budget() -> int:
    lines = effective_lines(BUDGET_FILE)
    if not lines:
        raise ValueError("missing static cache allowlist budget")
    return int(lines[0])


def main() -> int:
    print("StaticCacheAllowlistShrinkGuard: checking allowlist size...")

    try:
        budget = load_budget()
    except Exception as exc:
        print(f"StaticCacheAllowlistShrinkGuard: FAIL {exc}")
        return 1

    entries = effective_lines(ALLOWLIST_FILE)
    count = len(entries)
    if count > budget:
        print(
            "StaticCacheAllowlistShrinkGuard: FAIL "
            f"allowlist has {count} entries, budget is {budget}"
        )
        return 1

    print(
        "StaticCacheAllowlistShrinkGuard: PASS "
        f"({count}/{budget} allowlist entries)"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
