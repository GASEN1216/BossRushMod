"""
Guard: track large-file budgets for the refactor roadmap.

Roadmap targets are hard budgets. Older oversized files are pinned in
large_file_existing_allowlist.txt so they cannot grow while later batches
continue to split them.
"""

from pathlib import Path
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
EXCLUDE_DIRS = {"Build", ".codex_tmp", ".git", ".kiro", "docs", "tests", "鸭科夫源码"}
ALLOWLIST_FILE = Path(__file__).resolve().parent / "large_file_existing_allowlist.txt"

ROADMAP_BUDGETS = {
    "ModBehaviour.cs": 2000,
    "Integration/DragonKing/DragonKingAbilityController.cs": 2500,
    "LootAndRewards/LootAndRewards.cs": 2000,
    "ZombieMode/ZombieModeRewards.cs": 2000,
    "Integration/WishFountain/WishFountainService.cs": 1500,
    "Integration/NPCs/Courier/StorageDepositService.cs": 2000,
    "Integration/Reforge/ReforgeUIManager.cs": 2000,
    "Integration/BossRushIntegration.cs": 2000,
    "ModeF/ModeFFortifications.cs": 2000,
    "Common/MapConfig/LegacyMapSpawnPointFallback.cs": 2000,
}

DEFAULT_NEW_FILE_BUDGET = 1200


def should_exclude(path: Path) -> bool:
    rel = path.relative_to(PROJECT_ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def count_lines(path: Path) -> int:
    return len(path.read_text(encoding="utf-8", errors="ignore").splitlines())


def load_existing_allowlist() -> dict:
    allowlist = {}
    if not ALLOWLIST_FILE.exists():
        return allowlist

    for line in ALLOWLIST_FILE.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue

        parts = [part.strip() for part in line.split("|")]
        if len(parts) < 2:
            continue

        try:
            allowlist[parts[0]] = int(parts[1])
        except ValueError:
            continue

    return allowlist


def main() -> int:
    print("LargeFileBudgetGuard: checking source file line budgets...")

    existing_allowlist = load_existing_allowlist()
    warnings = []
    failures = []
    max_file = None
    max_lines = 0

    for cs_file in sorted(PROJECT_ROOT.rglob("*.cs")):
        if should_exclude(cs_file):
            continue

        rel = cs_file.relative_to(PROJECT_ROOT).as_posix()
        line_count = count_lines(cs_file)
        if line_count > max_lines:
            max_file = rel
            max_lines = line_count

        budget = ROADMAP_BUDGETS.get(rel)
        if budget is not None and line_count > budget:
            failures.append((rel, line_count, budget, "roadmap target"))
        elif budget is None and line_count > DEFAULT_NEW_FILE_BUDGET:
            allowed_lines = existing_allowlist.get(rel)
            if allowed_lines is None:
                failures.append((rel, line_count, DEFAULT_NEW_FILE_BUDGET, "new oversized file"))
            elif line_count > allowed_lines:
                failures.append((rel, line_count, allowed_lines, "allowlisted file grew"))
            else:
                warnings.append((rel, line_count, allowed_lines, "existing oversized debt"))

    if failures:
        print("LargeFileBudgetGuard: FAIL oversized files exceeded hard budgets:")
        for rel, line_count, budget, reason in failures:
            print(f"  FAIL {rel}: {line_count} lines > {budget} ({reason})")
        return 1

    if warnings:
        print("LargeFileBudgetGuard: WARN oversized files remain:")
        for rel, line_count, budget, reason in warnings:
            if reason == "existing oversized debt":
                print(f"  WARN {rel}: {line_count} lines (frozen cap {budget}; target {DEFAULT_NEW_FILE_BUDGET})")
            else:
                print(f"  WARN {rel}: {line_count} lines > {budget} ({reason})")
    else:
        print("LargeFileBudgetGuard: all source files are within hard budgets")

    if max_file is not None:
        print(f"LargeFileBudgetGuard: largest file is {max_file} ({max_lines} lines)")

    print("LargeFileBudgetGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
