"""Guard: manual smoke checklist must cover the roadmap's high-risk gameplay paths."""

from pathlib import Path
import sys


SCRIPT = Path("test_bossrush_smoke_manual.bat")

REQUIRED_SNIPPETS = [
    "2026-05-13-roadmap-validation-smoke.md",
    "Roadmap Validation In-Game Smoke Record",
    "JSON-backed map",
    "Standard BossRush",
    "Reward and lootbox drops",
    "Mode D",
    "Mode F",
    "Mode E",
    "Zombie Mode",
    "Courier storage/sweep",
    "Wish Fountain",
    "slashFx / hitFx",
    "SmokeLogScan.py",
    "STALE_LOG",
]


def fail(message: str) -> int:
    print("ManualSmokeChecklistCoverageGuard: " + message)
    return 1


def main() -> int:
    if not SCRIPT.exists():
        return fail("missing test_bossrush_smoke_manual.bat")

    text = SCRIPT.read_text(encoding="utf-8", errors="ignore")
    missing = [snippet for snippet in REQUIRED_SNIPPETS if snippet not in text]
    if missing:
        return fail("manual smoke checklist missing: " + ", ".join(missing))

    print("ManualSmokeChecklistCoverageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
