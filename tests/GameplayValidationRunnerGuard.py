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
    Path("DebugAndTools/F3GameplayValidationCoverage.cs"),
    Path("DebugAndTools/F3GameplayValidationItems.cs"),
    Path("DebugAndTools/F3GameplayValidationZombie.cs"),
    Path("DebugAndTools/F3GameplayValidationModeHKits.cs"),
    Path("DebugAndTools/F3GameplayValidationScenes.cs"),
    Path("DebugAndTools/F3GameplayValidationStages.cs"),
    Path("DebugAndTools/F3GameplayValidationModes.cs"),
    Path("DebugAndTools/F3GameplayValidationBackMountain.cs"),
    Path("DebugAndTools/F3GameplayValidationEconomy.cs"),
    Path("DebugAndTools/F3GameplayValidationDepth.cs"),
    Path("DebugAndTools/F3GameplayValidationDeepFlows.cs"),
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
            "ValidationGetCommittedStandardBoss", "ValidationKillCommittedStandardBoss",
            "ValidationDefeatModeDWave", "nextWaveDeadline", "modeDCurrentWaveEnemies.ToArray()",
            "yield return _host.ValidationDefeatModeDWave", "internal IEnumerator ValidationDefeatModeDWave",
            "ModeFSuccessfulExtractionCountForValidation != completedBefore",
            "IsRuntimeReady(BaseSceneNameForValidation())", "extraction_base_not_ready",
        ):
            if token not in code:
                errors.append("验收 runner 不变式缺失: " + token)

        deep = Path("DebugAndTools/F3GameplayValidationDeepFlows.cs").read_text(encoding="utf-8")
        victory = deep[deep.index("private IEnumerator RunStandardVictoryReward()"):]
        if "ValidationCountHostileCharacters" in victory or "ValidationForceClearArenaEnemies" in victory:
            errors.append("标准胜利必须等生产登记并走真实死亡，不能靠全场发现对象或 Destroy 清场")
        diagnostics = DIAGNOSTICS_CASE.read_text(encoding="utf-8")
        wave_start = diagnostics.find("internal IEnumerator ValidationDefeatModeDWave")
        wave_end = diagnostics.find("private bool ValidationDefeatModeDEnemy", wave_start)
        wave = diagnostics[wave_start:wave_end]
        if not (0 <= wave.find("ValidationDefeatModeDEnemy(enemy") < wave.find("yield return null;")
                < wave.find("onCompleted(true, null)")):
            errors.append("Mode D 快照击杀必须逐帧推进，让死亡回调 UI 完成更新后再杀下一只")
        if 'DevLog("[Validation] [ERROR] Mode D 伤害链失败: " + e)' not in diagnostics:
            errors.append("Mode D 伤害异常必须保留完整栈，不能只记录异常类型")
        modef = Path("ModeF/ModeFExtraction.cs").read_text(encoding="utf-8")
        if "if (!modeFActive) modeFSuccessfulExtractionCount++;" not in modef:
            errors.append("Mode F 撤离计数必须由实际结算并退出的路径写入")
        if "Destroy(gameObject)" in code:
            errors.append("runner 不得因 F3 页关闭自行销毁")

        scenes = Path("DebugAndTools/F3GameplayValidationScenes.cs").read_text(encoding="utf-8")
        runner = RUNNER.read_text(encoding="utf-8")
        stages = Path("DebugAndTools/F3GameplayValidationStages.cs").read_text(encoding="utf-8")
        for token in ("SceneLoader.Instance.LoadBaseScene(null, true)",
                      "while (SceneLoader.IsSceneLoading", "previous_scene_load_timeout",
                      "scene_loaded_but_runtime_not_ready", "DescribeSceneReadiness",
                      "LevelManager.AfterInit", "player.gameObject.activeInHierarchy",
                      "!player.Health.IsDead", "manager.GameCamera.isActiveAndEnabled",
                      "SceneManager.GetActiveScene().name, expectedScene",
                      "while (!IsRuntimeReady(expectedScene)",
                      "_operationSucceeded = IsRuntimeReady(expectedScene)"):
            if token not in scenes:
                errors.append("过图不能只凭加载任务完成报 PASS，缺少: " + token)
        if 'LoadScene(null, "SCENE_RETURN_BASE", returnToBase: true)' not in runner:
            errors.append("F3 返基地必须走完整 LoadBaseScene 入口")
        if "LoadScene(BaseSceneNameForValidation()" in runner or "WaitSceneSettled" in code:
            errors.append("禁止直载 Base_SceneV2 或跳过 AfterInit 的弱终态检查")
        gate_position = stages.find("yield return EnsureArenaForCase(caseId);")
        factory_position = stages.find("inner = factory();")
        if gate_position < 0 or factory_position < 0 or gate_position > factory_position:
            errors.append("场内用例必须在创建/启动玩法协程前恢复并确认竞技场")
        if '"base_runtime_not_ready"' not in runner or '"arena_runtime_not_ready"' not in stages:
            errors.append("场景未就绪时应跳过依赖用例，不能继续制造模式 FAIL/PASS")
        if "new DamageInfo(player)" not in runner or "damage.damageCreator" in runner:
            errors.append("终章验收必须显式调用官方 DamageInfo 构造器初始化元素列表，不能引用不存在的字段")
        modeh = Path("ModeH/ModeHRuntimeModule_SceneFlow.cs").read_text(encoding="utf-8")
        reset = modeh.split("internal void ForceResetStateForValidation()", 1)[-1].split("private IEnumerator DriveCertification", 1)[0]
        for token in ("if (!ModBehaviour.DevModeEnabled) return;", "ReleaseRuntimeObjects();",
                      'TryReturnRealStakeOnAbort("f3_validation_cleanup")', "ModeHRuntimeGates.SetRunOwnerActive(false)"):
            if token not in reset:
                errors.append("Mode H 验收清理缺少: " + token)
        if "_arenaLease.Dispose()" in reset or "_spectatorLease.Dispose()" in reset:
            errors.append("Mode H 租约只支持 Release(sceneGeneration)，不存在 Dispose API")

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
