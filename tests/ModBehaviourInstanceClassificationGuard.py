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
    "Integration": 259,
    "ZombieMode": 38,
    "Interactables": 23,
    "ModeE": 26,
    "Campaign": 15,
    "Audio": 9,
    "ModeF": 6,
    "Patches": 8,
    "RandomEvents": 5,
    "ModeG": 4,
    "MapSelection": 3,
    "ModeD": 1,
    "ModeH": 1,
    "DebugAndTools": 2,
}

EXPECTED_TOTAL = 400


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
    if total != EXPECTED_TOTAL:
        return fail("expected " + str(EXPECTED_TOTAL)
                    + " ModBehaviour.Instance lines, got " + str(total))

    doc = DOC.read_text(encoding="utf-8")

    # 各目录的行数从 EXPECTED_COUNTS 派生，不再手抄一遍：
    # 两处硬编码同一组数字，改动时必然漏掉一处（本守卫自己就漂移过）。
    # ModeD 与 DebugAndTools 在文档里合并成一行，单独处理。
    merged_groups = {"ModeD", "DebugAndTools"}
    required_doc_tokens = ["- Raw matches: " + str(EXPECTED_TOTAL)]
    for group, count in EXPECTED_COUNTS.items():
        if group in merged_groups:
            continue
        required_doc_tokens.append("| `" + group + "/` | " + str(count) + " |")
    merged_total = sum(EXPECTED_COUNTS.get(g, 0) for g in merged_groups)
    required_doc_tokens.append("| `ModeD`, `DebugAndTools` | " + str(merged_total) + " |")

    required_doc_tokens += [
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
