"""Guard ModBehaviour.Instance classification evidence for Batch Final-5."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
DOC = ROOT / "docs/testing/2026-05-14-modbehaviour-instance-classification.md"

EXCLUDE_DIRS = {
    ".codex_tmp",
    ".git",
    ".kiro",
    "Build",
    "docs",
    "tests",
    "wiki-site",
    "鸭科夫源码",
}

EXPECTED_COUNTS = {
    "Integration": 220,
    "ZombieMode": 38,
    "Interactables": 26,
    "ModeE": 12,
    "Audio": 8,
    "ModeF": 6,
    "Patches": 6,
    "MapSelection": 3,
    "TeleportDebugMonitor.cs": 2,
    "ModeD": 1,
    "DebugAndTools": 1,
}


def fail(message: str) -> int:
    print("ModBehaviourInstanceClassificationGuard: FAIL - " + message)
    return 1


def is_excluded(path: Path) -> bool:
    rel = path.relative_to(ROOT)
    return any(part in EXCLUDE_DIRS for part in rel.parts)


def count_instance_lines() -> dict:
    counts = {}
    for path in sorted(ROOT.rglob("*.cs")):
        if is_excluded(path):
            continue
        rel = path.relative_to(ROOT)
        group = rel.parts[0]
        text = path.read_text(encoding="utf-8", errors="ignore")
        line_count = sum(1 for line in text.splitlines() if "ModBehaviour.Instance" in line)
        if line_count:
            counts[group] = counts.get(group, 0) + line_count
    return counts


def main() -> int:
    if not DOC.exists():
        return fail("classification doc is missing: " + str(DOC.relative_to(ROOT)))

    counts = count_instance_lines()
    if counts != EXPECTED_COUNTS:
        return fail("current counts differ from documented baseline: " + repr(counts))

    total = sum(counts.values())
    if total != 323:
        return fail("expected 323 ModBehaviour.Instance lines, got " + str(total))

    doc = DOC.read_text(encoding="utf-8")
    required_doc_tokens = [
        "- Raw matches: 323",
        "| `Integration/` | 220 |",
        "| `ZombieMode/` | 38 |",
        "| `Interactables/` | 26 |",
        "| `ModeE/` | 12 |",
        "| `Audio/` | 8 |",
        "| `ModeF/` | 6 |",
        "| `Patches/` | 6 |",
        "| `MapSelection/` | 3 |",
        "| `root`, `ModeD`, `DebugAndTools` | 4 |",
        "Keep: Unity owner",
        "Keep: gameplay state",
        "Candidate: notification",
        "Candidate: service query",
        "Achievement notification already moved to `BossRushEventBus`",
        "Broad decoupling remains a future long-term goal",
        "BossRushEventBusLifecycleGuard.py",
        "LongTermGoalNonGoalGuard.py",
    ]
    for token in required_doc_tokens:
        if token not in doc:
            return fail("classification doc missing token: " + token)

    print("ModBehaviourInstanceClassificationGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
