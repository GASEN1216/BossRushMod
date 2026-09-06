#!/usr/bin/env python3
"""Cash reward claim markers must never bypass the live balance snapshot.

2026-09-06（D-2）起：协调器状态机在 Common/Lifecycle/BossRushSaveCoordinatorEngine.cs，
存档门面在 Common/Lifecycle/BossRushSlotJsonStore.cs；征程 / 日报只保留门面与现金义务。
本守卫据此断言：义务由门面持有、经 IBossRushSaveBatchSource 交给引擎；引擎在 typed pending
之前采集、只有 SaveFile 成功后才回调清除；官方采集点先采现金再交出 pending。
"""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent


def read(path):
    return (ROOT / path).read_text(encoding="utf-8-sig")


def strip_comments(text):
    text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
    text = re.sub(r"//[^\n]*", "", text)
    return text


def method_body(source, signature):
    """从签名起，按花括号配平截取方法体。找不到返回空串。"""
    start = source.find(signature)
    if start < 0:
        return ""
    brace = source.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for i in range(brace, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
    return ""


def main():
    errors = []
    engine = strip_comments(read("Common/Lifecycle/BossRushSaveCoordinatorEngine.cs"))
    store = strip_comments(read("Common/Lifecycle/BossRushSlotJsonStore.cs"))

    # 引擎：义务不得被 typed pending 消费；采集 -> typed flush -> SaveFile -> 成功回调 的顺序冻结
    flush = method_body(engine, "private bool FlushBatch(out string error, bool bypassGates)")
    no_work = re.search(r"if \(!_source\.HasPendingWrite[^\n]+", flush)
    if not no_work or "!_source.HasSnapshotObligation" not in no_work.group(0):
        errors.append("engine: snapshot obligation must survive typed pending consumption")
    positions = [flush.find(token) for token in ("_source.CollectSnapshot(", "_source.FlushPending()",
                                                  "SavesSystem.SaveFile(false);", "_source.OnPhysicalSaveSucceeded();")]
    if min(positions) < 0 or positions != sorted(positions):
        errors.append("engine: collect before typed flush; clear obligation only after physical save")

    # 存档门面：官方采集点先跑前置步骤（采现金），未就绪不交出 pending
    collect = method_body(store, "private void HandleCollectSaveData()")
    if not 0 <= collect.find("BeforeCollectSaveData()) return;") < collect.find("FlushPending();"):
        errors.append("store: official collection must run BeforeCollectSaveData before FlushPending")

    for prefix, directory, grant in (
        ("Campaign", "Campaign", "EconomyManager.Add(def.RewardCash)"),
        ("DailyReport", "Integration/DailyReport", "DailyReportRewards.TryGrantBountyCash(settled.CashReward, out reason)"),
    ):
        coordinator = strip_comments(read(f"{directory}/{prefix}SaveCoordinator.cs"))
        persistence = strip_comments(read(f"{directory}/{prefix}Persistence.cs"))
        service = read(f"{directory}/" + ("CampaignProgressService.cs" if prefix == "Campaign" else "DailyReportService.cs"))
        prepare = prefix + "SaveCoordinator.TryPrepareCashReward()"
        if not 0 <= service.find(prepare) < service.find(grant):
            errors.append(prefix + ": cash readiness and obligation must precede Add")

        collect = method_body(coordinator, "internal static bool CollectPendingCash()")
        for token in ("_cashSnapshotRequired", "if (!required) return true", "SavesSystem.CurrentSlot < 0",
                      "SavesSystem.IsSaving", "EconomyManager.Instance == null",
                      'SavesSystem.Save<EconomyManager.SaveData>("EconomyData",',
                      "EconomyManager.Instance.GenerateSaveData()"):
            if token not in collect:
                errors.append(prefix + ": incomplete live cash collection: " + token)

        obligation = method_body(coordinator, "public bool HasSnapshotObligation")
        if "_cashSnapshotRequired" not in obligation:
            errors.append(prefix + ": engine source must expose the cash obligation")
        snapshot = method_body(coordinator, "public bool CollectSnapshot(out string error)")
        if "CollectPendingCash()" not in snapshot:
            errors.append(prefix + ": engine source must collect cash through CollectPendingCash")
        succeeded = method_body(coordinator, "public void OnPhysicalSaveSucceeded()")
        if "_cashSnapshotRequired = false;" not in succeeded:
            errors.append(prefix + ": clear cash obligation only in OnPhysicalSaveSucceeded")
        for signature in ("internal static void NotifySlotChanged()", "internal static void ResetStaticCaches()"):
            if "_cashSnapshotRequired = false;" not in method_body(coordinator, signature):
                errors.append(prefix + ": cash obligation must reset with slot/runtime: " + signature)

        before = method_body(persistence, "private static bool BeforeCollectSaveData()")
        if "if (!" + prefix + "SaveCoordinator.CollectPendingCash()) return false;" not in before:
            errors.append(prefix + ": official collection must gather cash before claim key")
        if "BeforeCollectSaveData = BeforeCollectSaveData" not in persistence:
            errors.append(prefix + ": persistence facade must bind BeforeCollectSaveData into the shared store")
    for error in errors:
        print("  - " + error)
    print("ContentCashSnapshotGuard: " + ("FAIL" if errors else "PASS"))
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
