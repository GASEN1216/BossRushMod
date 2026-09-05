#!/usr/bin/env python3
"""Cash reward claim markers must never bypass the live balance snapshot."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def body(source, signature):
    start = source.find(signature)
    if start < 0:
        return ""
    match = re.search(r"\n        \}", source[start:])
    return source[start:start + match.end()] if match else ""


def main():
    errors = []
    for prefix, directory, grant in (
        ("Campaign", "Campaign", "EconomyManager.Add(def.RewardCash)"),
        ("DailyReport", "Integration/DailyReport", "DailyReportRewards.TryGrantBountyCash(settled.CashReward, out reason)"),
    ):
        coordinator = read(f"{directory}/{prefix}SaveCoordinator.cs")
        persistence = read(f"{directory}/{prefix}Persistence.cs")
        service = read(f"{directory}/" + ("CampaignProgressService.cs" if prefix == "Campaign" else "DailyReportService.cs"))
        prepare = prefix + "SaveCoordinator.TryPrepareCashReward()"
        if not 0 <= service.find(prepare) < service.find(grant):
            errors.append(prefix + ": cash readiness and obligation must precede Add")
        collect = body(coordinator, "internal static bool CollectPendingCash()")
        for token in ("if (!_cashSnapshotRequired) return true", "SavesSystem.CurrentSlot < 0",
                      "SavesSystem.IsSaving", "EconomyManager.Instance == null",
                      'SavesSystem.Save<EconomyManager.SaveData>("EconomyData",',
                      "EconomyManager.Instance.GenerateSaveData()"):
            if token not in collect:
                errors.append(prefix + ": incomplete live cash collection: " + token)
        flush = body(coordinator, "private static bool FlushBatch(")
        no_work = re.search(r"if \(!" + prefix + r"Persistence.HasPendingWrite[^\n]+", flush)
        if not no_work or "!_cashSnapshotRequired" not in no_work.group(0):
            errors.append(prefix + ": cash obligation must survive typed pending consumption")
        positions = [flush.find(token) for token in ("if (!CollectPendingCash())",
                     prefix + "Persistence.FlushPending()", "SavesSystem.SaveFile(false);", "_cashSnapshotRequired = false;")]
        if min(positions) < 0 or positions != sorted(positions):
            errors.append(prefix + ": collect before claimed key; clear cash obligation only after physical save")
        callback = body(persistence, "private static void HandleCollectSaveData()")
        if not 0 <= callback.find("if (!" + prefix + "SaveCoordinator.CollectPendingCash()) return;") < callback.find("FlushPending();"):
            errors.append(prefix + ": official collection must gather cash before claim key")
        for signature in ("internal static void NotifySlotChanged()", "internal static void ResetStaticCaches()"):
            if "_cashSnapshotRequired = false;" not in body(coordinator, signature):
                errors.append(prefix + ": cash obligation must reset with slot/runtime: " + signature)
    for error in errors:
        print("  - " + error)
    print("ContentCashSnapshotGuard: " + ("FAIL" if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
