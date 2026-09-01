#!/usr/bin/env python3
"""Guard: PetNest Bundle_v2 / reward debt and DailyReport candidate commits."""

from pathlib import Path
import re
import sys


def fail(messages):
    for message in messages:
        print("  - " + message)
    print("GameplayReliabilityPersistenceGuard: FAIL")
    return 1


def read(path):
    return Path(path).read_text(encoding="utf-8", errors="ignore")


def main():
    errors = []
    tuning = read("PetNest/PetNestTuning.cs")
    persistence = read("PetNest/PetNestPersistence.cs")
    service = read("PetNest/PetNestService.cs")
    expedition = read("PetNest/PetNestExpeditionService.cs")
    models = read("PetNest/PetNestModels.cs")
    daily = read("Integration/DailyReport/DailyReportService.cs")
    daily_store = read("Integration/DailyReport/DailyReportPersistence.cs")

    for token in (
        'BundleStorageKey = "BossRush_PetNest_Bundle_v2"',
        "BundleSchemaVersion = 2",
        'NestStorageKey = "BossRush_PetNest_Nest_v1"',
        'ExpeditionStorageKey = "BossRush_PetNest_Expedition_v1"',
        'MuseumStorageKey = "BossRush_PetNest_Museum_v1"',
    ):
        if token not in tuning:
            errors.append("PetNest key/schema 契约缺失: " + token)

    for token in (
        "if (exists)",
        "PetNestPersistence.TryBuildLegacyBundle",
        "bundle_schema_mismatch",
        "legacy_migration_readback_failed",
        "PetNestSaveCoordinator.RequestFlush",
        "PetNestCodec.CloneBundle",
        "BeginTransaction",
        "CommitTransaction",
        "[ThreadStatic]",
    ):
        if token not in persistence:
            errors.append("Bundle_v2 加载/迁移/事务缺失: " + token)
    if persistence.find("if (exists)") > persistence.find("TryBuildLegacyBundle"):
        errors.append("Bundle_v2 必须优先于 v1 迁移源")
    if "SavesSystem.Save<string>(PetNestTuning.NestStorageKey" in persistence:
        errors.append("v1 键只读，不得恢复运行时分拆写")

    for method in ("TryAddPet", "TryRemovePet", "TryReleasePet", "TryRenamePet",
                   "TrySetDeployedPet", "ClearDeployedPet", "TrySpendSouls"):
        start = service.find("internal static bool " + method)
        body = service[start:start + 3300] if start >= 0 else ""
        if "BeginCandidate(out failureReasonId)" not in body or "CommitCandidate(out failureReasonId)" not in body:
            errors.append(method + " 未完整经过候选包提交")

    for token in ("cashGranted", "grantedLootUnits", "rewardsGranted"):
        if token not in models or token not in expedition:
            errors.append("奖励欠账游标缺失: " + token)
    for token in ("IValidationRewardBackend", "SetValidationRewardBackend",
                  "IsRewardRuntimeReady", "TryGrantPendingRewards", "TimeSpan.TicksPerSecond * 5L"):
        if token not in expedition:
            errors.append("奖励可注入/就绪门控/固定退避缺失: " + token)
    if re.search(r"rewardGrantAttempts\s*>=\s*PetNestTuning\.MaxRewardGrantAttempts[\s\S]{0,240}rewardsGranted\s*=\s*true", expedition):
        errors.append("奖励重试达上限后不得伪装已发放")
    if "if (candidate.rewardsGranted) Records.Remove(candidate);" not in expedition:
        errors.append("翻牌 UI 与奖励欠账未解耦")

    for token in ("DailyReportData candidate = current.Clone()", "Persist(candidate)",
                  "candidate.PendingIssueBanner", "candidate.BountyRewardClaimed = true"):
        if token not in daily:
            errors.append("DailyReport 候选副本提交缺失: " + token)
    if "SetValidationRejectStore" not in daily_store or "if (_validationRejectStore && ModBehaviour.DevModeEnabled) return false;" not in daily_store:
        errors.append("DailyReport 缺少 Dev Store 失败注入")
    if re.search(r"\bdata\.CarrySeconds\s*=\s*_carrySeconds", daily):
        errors.append("SyncCarrySecondsToPersistence 仍在 Store 前改权威缓存")

    if errors:
        return fail(errors)
    print("GameplayReliabilityPersistenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
