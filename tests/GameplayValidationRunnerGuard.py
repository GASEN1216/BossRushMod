#!/usr/bin/env python3
"""Guard: F3 full gameplay validation remains Dev-only, serial and self-cleaning."""

from pathlib import Path
import sys


RUNNER = Path("DebugAndTools/F3GameplayValidationRunner.cs")
RANDOM_EVENTS_CASE = Path("DebugAndTools/F3GameplayValidationRandomEvents.cs")
PERSISTENCE_CASE = Path("DebugAndTools/F3GameplayValidationPersistence.cs")
DIAGNOSTICS_CASE = Path("DebugAndTools/F3GameplayValidationDiagnostics.cs")
MENU = Path("DebugAndTools/F3DebugCheatMenuUi.cs")
COMPILE = Path("compile_official.bat")

# 用例已按主题拆分到多个 partial 文件（主 runner 要守 1200 行预算）。
# 不变式断言对全套文件的拼接生效，不关心具体某条落在哪个文件里。
CASE_FILES = (
    Path("DebugAndTools/F3GameplayValidationStages.cs"),
    Path("DebugAndTools/F3GameplayValidationModes.cs"),
    Path("DebugAndTools/F3GameplayValidationBackMountain.cs"),
    Path("DebugAndTools/F3GameplayValidationEconomy.cs"),
    Path("DebugAndTools/F3GameplayValidationDepth.cs"),
    Path("DebugAndTools/F3GameplayValidationLeaks.cs"),
)


def main():
    errors = []
    if not RUNNER.is_file():
        errors.append("缺少 F3GameplayValidationRunner.cs")
    else:
        code = RUNNER.read_text(encoding="utf-8", errors="ignore")
        if RANDOM_EVENTS_CASE.is_file():
            code += "\n" + RANDOM_EVENTS_CASE.read_text(encoding="utf-8", errors="ignore")
        if DIAGNOSTICS_CASE.is_file():
            code += "\n" + DIAGNOSTICS_CASE.read_text(encoding="utf-8", errors="ignore")
        for case_file in CASE_FILES:
            if not case_file.is_file():
                errors.append("验收用例文件缺失: " + str(case_file))
                continue
            code += "\n" + case_file.read_text(encoding="utf-8", errors="ignore")
        for token in (
            "DontDestroyOnLoad", "DevModeEnabled", "DedicatedSlotKey", "RunMarkerKey",
            "SavesSystem.IsSaving", "ModalInputLeaseCount", "ValidationHasActiveMode",
            "SceneTimeoutSeconds = 90f", "CaseTimeoutSeconds = 30f",
            "ModeHTimeoutSeconds = 180f", "SuiteTimeoutSeconds = 2700f",
            'SetStage("1/7 基线与数据")', 'SetStage("7/7 最终清场、泄漏与回读")',
            "DATA_CAMPAIGN_JSON", "DATA_CODEX_CATALOG", "DAILY_REPORT_ROLLBACK",
            "PETNEST_REWARD_DEBT", "AFFIX_TEMP_ITEM_LIFECYCLE", "MODE_F_BLOODFIRE",
            "MODE_H_FIRST_CERTIFICATION", "MODE_H_CACHE_HIT", "BGM_OWNER_LEASES",
            "ModeHProductionCertification.InvalidateCache",
            "CAMPAIGN_FINAL_BOSS", "FINAL_CLEAN_STATE", "FINAL_SAVE_READBACK",
            "CANCELLED", "ValidationSafeCleanup", "BossRushTestReports",
            '" | " + outcome + " | "', "_baselineP95Ms * 1.75f", "_peakFrameMs",
            "ValidationCountHostileCharacters", "Team.IsEnemy(Teams.player, c.Team)",
            "PetNestCompanionAgent.IsCompanionCharacter", "GetActiveValidationOutcome",
            "RandomEventValidationOutcome.Pending", '"RANDOM_EVENT_"',
            "GameplayValidationSuppressNotifications",
            "ValidationCountPlayableModeDEnemies", "ValidationCountModeOwnedCharacters",
            "ValidationTryGetArenaCleanState", "Time.realtimeSinceStartup + 2f",
            # 按用例隔离：单个红项不得再拖垮整套，超时与取消必须分开报。
            "RunIsolatedCase", "ForceReclaimArena", "SkipRemainingArenaCases",
            "_dirtyStreak", "_suiteTimedOut", "TIMEOUT", "ABORTED_DIRTY",
            "failed_ids=", "skipped_ids=",
            # 覆盖面扩充：后山、经济、深度流程、泄漏差值。
            'SetStage("3/7 后山与经济")', 'SetStage("5/7 模式深度流程")',
            "BACKMOUNTAIN_SHOWCASE_LEDGER", "BACKMOUNTAIN_RAID_MEAL",
            "AFFIX_FORGE_REJECT_NO_COST", "CODEX_PERSISTENCE_READBACK",
            "MODE_D_MULTI_WAVE", "FINAL_LEAK_DELTA",
            "ValidationForceClearArenaEnemies",
        ):
            if token not in code:
                errors.append("验收 runner 不变式缺失: " + token)
        if "Destroy(gameObject)" in code:
            errors.append("runner 不得因 F3 页关闭自行销毁")

        # 隔离壳必须透传子协程；写死 yield return null 会让 WaitSeconds 之类永不推进
        # （Mode H 认证踩过同款坑，见 ModeHCertificationCoroutineDriveGuard）。
        if "yield return inner.Current;" not in code:
            errors.append("RunIsolatedCase 必须 `yield return inner.Current;` 透传子协程")

        # 无 code-drivable 入口的项必须如实记 SKIP，不许伪造 PASS 凑绿。
        for token in ("MODE_E_EXTRACTION", "MODE_F_BOUNTY", '"SKIP"'):
            if token not in code:
                errors.append("缺少不可自动化项的 SKIP 留痕: " + token)

    persistence_case = PERSISTENCE_CASE.read_text(encoding="utf-8", errors="ignore") if PERSISTENCE_CASE.is_file() else ""
    for token in ("TrySignInToday", "DebugAdvanceGameSeconds", "SavesSystem.SaveFile(false)",
                  "DailyReportPersistence.ResetStaticCaches", "DailyReportSignInOutcome.PersistBlocked"):
        if token not in persistence_case:
            errors.append("日报真实验收不变式缺失: " + token)

    menu = MENU.read_text(encoding="utf-8", errors="ignore") if MENU.is_file() else ""
    for label in ("验收测试", "完整玩法验收", "取消并安全清理"):
        if label not in menu and (not RUNNER.is_file() or label not in RUNNER.read_text(encoding="utf-8", errors="ignore")):
            errors.append("F3 验收页缺少: " + label)

    compile_text = COMPILE.read_text(encoding="utf-8", errors="ignore")
    if "DebugAndTools\\F3GameplayValidationRunner.cs" not in compile_text:
        errors.append("F3GameplayValidationRunner.cs 未登记编译清单")
    if "DebugAndTools\\F3GameplayValidationRandomEvents.cs" not in compile_text:
        errors.append("F3GameplayValidationRandomEvents.cs 未登记编译清单")
    if "DebugAndTools\\F3GameplayValidationCodex.cs" not in compile_text:
        errors.append("F3GameplayValidationCodex.cs 未登记编译清单")
    if "DebugAndTools\\F3GameplayValidationPersistence.cs" not in compile_text:
        errors.append("F3GameplayValidationPersistence.cs 未登记编译清单")
    if "DebugAndTools\\F3GameplayValidationDiagnostics.cs" not in compile_text:
        errors.append("F3GameplayValidationDiagnostics.cs 未登记编译清单")
    # 拆分出来的用例文件同样必须进清单：漏登记不会报错，只是用例静默不存在（AGENTS.md 4.1）。
    for case_file in CASE_FILES:
        entry = "DebugAndTools\\" + case_file.name
        if entry not in compile_text:
            errors.append(case_file.name + " 未登记编译清单")

    if errors:
        for error in errors:
            print("  - " + error)
        print("GameplayValidationRunnerGuard: FAIL")
        return 1
    print("GameplayValidationRunnerGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
