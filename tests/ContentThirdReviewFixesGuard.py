"""Structural invariants for CR-2026-09-06-009/010/015; behavioral fixture is separate."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]


def read(path):
    text = (ROOT / path).read_text(encoding="utf-8-sig")
    return re.sub(r"//[^\n]*|/\*.*?\*/", "", text, flags=re.S)


def method(text, signature):
    begin = text.index(signature)
    opening = text.index("{", begin)
    depth = 1
    for index in range(opening + 1, len(text)):
        depth += (text[index] == "{") - (text[index] == "}")
        if depth == 0:
            return text[opening:index + 1]
    raise AssertionError("Unclosed method: " + signature)


def main():
    service = read("Integration/DailyReport/DailyReportService.cs")
    codec = read("Integration/DailyReport/DailyReportCodec.cs")
    catalog = read("Integration/Codex/CodexBossCatalog.cs")
    collector = read("Integration/Codex/CodexKillCollector.cs")
    milestones = read("Integration/Codex/CodexMilestones.cs")
    for signature in ("void SettleOneDay(", "bool TrySignInToday("):
        body = method(service, signature)
        assert body.index("StagePendingMilestones(data)") < body.index("data.PeriodSignedCount = 0"), \
            "DailyReport must freeze outstanding milestones before resetting progress"
    deliver = method(service, "bool TryDeliverMilestone(")
    assert deliver.index("CanDeliverMilestone()") < deliver.index("TryGrantMilestone"), "Known storage fault must block grants"
    assert deliver.index("TryGrantMilestone") < deliver.index("MarkMilestoneClaimed"), "Keep grant-before-marker recovery semantics"
    assert "debt.SignDayIndex" in deliver and "debt.Seed" in deliver and "debt.Quality" in deliver, \
        "Delivery must consume the frozen identity"
    gate = method(service, "bool CanDeliverMilestone(")
    assert "IsStoreFaulted" in gate and "HasWriteBarrier" in gate, "Both storage barriers must gate physical rewards"
    retry = method(service, "void TryRedeliverPendingMilestones(")
    assert retry.index("Persist(candidate)") < retry.index("TryDeliverMilestone"), "Debt preparation must precede delivery"
    assert "PeriodSignedCount <= 0" not in retry, "Old-period debts remain reachable after streak reset"
    mark = method(service, "bool MarkMilestoneClaimed(")
    assert "SameIdentity(debt)" in mark and "debt.SignDayIndex == ResolveMilestoneSignDayIndex" in mark, \
        "Payment must remove only its debt and mark only the matching current streak"
    assert '"pendingMilestoneCount"' in codec and "DecodePendingMilestones(root)" in codec, "Debt must survive real encoding and decoding"
    assert "data.PendingMilestones == null) return null" in codec, "Corrupt debt must fail closed"
    kill = method(collector, "void RecordKill(")
    assert kill.index("SynchronizeHistoricalEntries") < kill.index("CodexMilestones.Evaluate"), "New key must join catalog before achievement evaluation"
    full = method(catalog, "bool IsFullyUnlocked(")
    assert "data.Find(_ordered[i].Key)" in full and "entry.Kills <= 0" in full, "Full collection must prove every actual key unlocked"
    evaluate = method(milestones, "void EvaluateUnlockCount(")
    assert evaluate.index("SynchronizeHistoricalEntries") < evaluate.index("IsFullyUnlocked"), "Panel/load evaluation must use current catalog"
    assert "unlocked >= total" not in milestones, "Raw counts cannot replace key membership"
    for relative in ("DailyReport/DailyReport.csproj", "DailyReport/Program.cs", "Codex/Codex.csproj", "Codex/Program.cs", "run.py"):
        assert (ROOT / "tests/fixtures/ContentThirdReviewFixes" / relative).is_file(), "Missing regression fixture: " + relative
    print("ContentThirdReviewFixesGuard: PASS (behavioral regressions require separate fixture runner)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (AssertionError, ValueError) as error:
        print("ContentThirdReviewFixesGuard: FAIL - " + str(error))
        sys.exit(1)
