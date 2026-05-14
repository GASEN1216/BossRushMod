"""Guard: manual smoke checklist must cover the roadmap's high-risk gameplay paths."""

from pathlib import Path
import sys


SCRIPT = Path("test_bossrush_smoke_manual.bat")

REQUIRED_SNIPPETS = [
    "2026-05-14-final-runtime-smoke.md",
    "2026-05-14 Final Runtime Smoke Record",
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
    "cloud sync failure prompt",
    "ignore/continue",
    "Duckov.exe will not start",
]

REQUIRED_TEMPLATE_SNIPPETS = [
    "2026-05-14 Final Runtime Smoke Record",
    "Conclusion: User-reported smoke passed after the 2026-05-14 21:01 deploy; latest log scan PASS with 0 BossRush-related error blocks.",
    "validate_refactor_step.bat 2026-05-14-modbehaviour-classification-codex",
    "Deployed DLL timestamp: `2026-05-14 21:01:09 +0800`",
    "Latest pre-smoke log scan: `python3 tests/SmokeLogScan.py` = `STALE_LOG`",
    "`SmokeLogScan.py` result after smoke",
    "BossRush-related error blocks",
    "SmokeLogScan: BossRush-related error blocks: 0",
    "SmokeLogScan: PASS",
    "JSON-backed map",
    "Standard BossRush full run works",
    "reward/lootbox drops",
    "Mode D",
    "Mode F",
    "Mode E",
    "Zombie Mode",
    "Courier storage/sweep",
    "Wish Fountain",
    "slashFx / hitFx",
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

    smoke_note = Path("docs/testing/2026-05-14-final-runtime-smoke.md")
    if not smoke_note.exists():
        return fail("missing final runtime smoke template")

    note_text = smoke_note.read_text(encoding="utf-8", errors="ignore")
    note_missing = [snippet for snippet in REQUIRED_TEMPLATE_SNIPPETS if snippet not in note_text]
    if note_missing:
        return fail("final runtime smoke template missing: " + ", ".join(note_missing))

    print("ManualSmokeChecklistCoverageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
