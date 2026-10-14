#!/usr/bin/env python3
"""Guard: F3 full gameplay validation remains Dev-only, serial and self-cleaning."""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cs_source_util import clean_source
from ModeHOneClickFlowGuard import method_body, squeeze


RUNNER = Path("DebugAndTools/F3GameplayValidationRunner.cs")
RANDOM_EVENTS_CASE = Path("DebugAndTools/F3GameplayValidationRandomEvents.cs")
PERSISTENCE_CASE = Path("DebugAndTools/F3GameplayValidationPersistence.cs")
DIAGNOSTICS_CASE = Path("DebugAndTools/F3GameplayValidationDiagnostics.cs")
MENU = Path("DebugAndTools/F3DebugCheatMenuUi.cs")
COMPILE = Path("compile_official.bat")

# 用例已按主题拆分到多个 partial 文件（主 runner 要守 1200 行预算）。
# 不变式断言对全套文件的拼接生效，不关心具体某条落在哪个文件里。
CASE_FILES = (
    Path("DebugAndTools/F3GameplayValidationExecution.cs"),
    Path("DebugAndTools/F3GameplayValidationSeasons.cs"),
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
            "MODE_H_PLAYER_ENTRY", "MODE_H_PLAYER_REENTRY", "BGM_OWNER_LEASES",
            "StartModeHCertificationFromF3", "CanRunModeHCertification",
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
            "BACKMOUNTAIN_SHOWCASE_DISPLAY", "BACKMOUNTAIN_RAID_MEAL",
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

        modeh_case = squeeze(method_body(clean_source(Path("DebugAndTools/F3GameplayValidationModes.cs").read_text(encoding="utf-8")), "private IEnumerator RunModeH(bool fullSeason)"))
        for token in ("_host.ModeHRuntime.OnSceneLoaded(", "sawDiagnostics |= _host.ModeHRuntime.IsCertificationDiagnosticRunning;", 'drafting && !sawDiagnostics ? "PASS" : "FAIL"', "RunModeHFullSeason()"):
            if token not in modeh_case:
                errors.append("Mode H 自动验收必须模拟普通入场并观察无动态测试: " + token)
        for token in ("InvalidateCache", "StartCertificationFromF3", "TryUseCachedReport"):
            if token in modeh_case:
                errors.append("Mode H 自动入场用例不得启动或准备逐项认证: " + token)

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

        scenes = clean_source(Path("DebugAndTools/F3GameplayValidationScenes.cs").read_text(encoding="utf-8"))
        runner = RUNNER.read_text(encoding="utf-8")
        stages = Path("DebugAndTools/F3GameplayValidationStages.cs").read_text(encoding="utf-8")
        for token in ("SceneLoader.Instance.LoadBaseScene(null, true)",
                      "while (SceneLoader.IsSceneLoading", "previous_scene_load_timeout",
                      "scene_loaded_but_runtime_not_ready", "DescribeSceneReadiness",
                      "LevelManager.AfterInit", "player.gameObject.activeInHierarchy",
                      "!player.Health.IsDead", "manager.GameCamera.isActiveAndEnabled",
                      "SceneManager.GetActiveScene().name, expectedScene",
                      "int previousSceneHandle = SceneManager.GetActiveScene().handle;",
                      "while ((SceneManager.GetActiveScene().handle == previousSceneHandle || !IsRuntimeReady(expectedScene))",
                      "_operationSucceeded = sceneChanged && IsRuntimeReady(expectedScene)",
                      '"scene_instance_did_not_change"'):
            if token not in scenes:
                errors.append("过图不能只凭加载任务完成报 PASS，缺少: " + token)
        if scenes.count("if (returnToBase) TryClickAutotestClosureContinue();") != 2:
            errors.append("返基地的原生任务等待与场景就绪等待均须驱动真实结算继续按钮")
        for signature in ("private IEnumerator RunModeD()", "private IEnumerator RunModeDMultiWave()"):
            entry = method_body(clean_source(code), signature) or ""
            if "PrepareModeDValidationEntry(out entryReason)" not in entry or "_host.TryStartModeD()" not in entry or "RestoreModeDValidationEntry();" not in entry:
                errors.append("Mode D 验收必须保存原物、准备裸装、走真实玩家入口并 finally 归还: " + signature)
            if "_host.StartModeD();" in entry:
                errors.append("Mode D 验收不能绕过玩家裸装入口: " + signature)
        for signature, mode in (("private IEnumerator RunModeE()", "E"), ("private IEnumerator RunModeF()", "F"),
                                ("private IEnumerator RunModeEExtraction()", "E"), ("private IEnumerator RunModeFBounty()", "F"),
                                ("private IEnumerator RunModeFExtraction()", "F")):
            entry = method_body(clean_source(code), signature) or ""
            for token in ("PrepareModeEFValidationEntry(", "_host.TryStartMode" + mode + "()", "RestoreModeDValidationEntry();"):
                if token not in entry:
                    errors.append("E/F 验收必须保管原物、发测试费用、走真实个人入口并 finally 归还: " + signature + " " + token)
            if "ValidationStartMode" + mode + "()" in entry:
                errors.append("E/F 验收不得绕过真实个人费用入口: " + signature)
        raw_modes = Path("DebugAndTools/F3GameplayValidationModes.cs").read_text(encoding="utf-8-sig")
        if "using ItemStatsSystem;" not in clean_source(raw_modes):
            errors.append("原物租约需要真实 ItemStatsSystem 扩展命名空间，不能用实例 DestroyTree 替身掩盖")
        preparation = method_body(clean_source(raw_modes), "internal static bool TryPrepare(") or ""
        for token in ("if (!ModBehaviour.DevModeEnabled || !dedicated)",
                      'new GameObject("BossRushValidation_EntryEscrow")',
                      "UnityEngine.Object.DontDestroyOnLoad(escrow);",
                      "escrow.AddComponent<ItemStatsSystem.Inventory>()",
                      "inventory.SetCapacity(System.Math.Max(1, originals.Count));",
                      "new F3ModeDLoadoutLease(originalRoot, inventory, escrow, originalAsset, originalSlot)",
                      "isNaked() && lease.IsEntryOwnerCurrent()"):
            if token not in preparation:
                errors.append("专用档原物保管必须使用跨场景独立官方 Inventory: " + token)
        if "PlayerStorage.Inventory" in preparation:
            errors.append("竞技场没有基地仓库，验收准备不能要求 PlayerStorage.Inventory 存在")
        restoration = method_body(clean_source(raw_modes), "internal bool TryRestore()") or ""
        release = method_body(restoration, "if (restored)") or ""
        for token in ("foreach (ItemStatsSystem.Item remaining in storage.Content) if (remaining != null) return false;",
                      "UnityEngine.Object.Destroy(escrowObject);"):
            if token not in release:
                errors.append("原物全部归还且 escrow 为空后才能销毁载体: " + token)
        if "if (root == null" in restoration or restoration.count("UnityEngine.Object.Destroy(escrowObject);") != 1:
            errors.append("死亡后旧 root 销毁不能阻断原资产新角色归还，载体也不能提前销毁")
        payment = method_body(raw_modes, "internal bool TryPayFee(") or ""
        for token in ("ItemUtilities.FindAllBelongsToPlayer", "if (loans.Contains(item)) continue;", "item.Detach();", "finally", "TryRestoreFeeObjects()"):
            if token not in payment:
                errors.append("官方费用扫描不能消费验收保管的原物: " + token)
        f_extract = method_body(clean_source(deep), "private IEnumerator RunModeFExtraction()") or ""
        if "if (!triggered || !resolved) break;" in f_extract or f_extract.count("ModeFSuccessfulExtractionCountForValidation != completedBefore") < 2:
            errors.append("F 撤离必须逐帧重新观测异步收据，不能在最初未结算时结束")
        f_trigger = method_body(clean_source(modef), "internal bool DebugTriggerModeFExtractionForValidation(") or ""
        if "if (BossRushCoop.IsConnected)" not in f_trigger or "CharacterMainControl.Main.SetPosition(coopExtractionPosition);" not in f_trigger or "IsCoopModeFLocalAdmitted()" not in f_trigger:
            errors.append("F 联机验收必须移动获准本地玩家进入实际撤离圈，不能调用已停用监听")
        zombie = Path("DebugAndTools/F3GameplayValidationZombie.cs").read_text(encoding="utf-8-sig")
        z_start = method_body(clean_source(zombie), "internal bool ValidationStartZombie(") or ""
        for token in ("TryBeginMapSelectionShell()", "ConfigureZombieModePendingCashInvestment(0L", "CommitZombieModeEntryResourcesShell", "SelectZombieModeStarterLoadout"):
            if token not in z_start:
                errors.append("丧尸验收必须实际提交个人入场费用并选择流派: " + token)
        z_extract = method_body(clean_source(zombie), "internal IEnumerator ValidationCompleteZombieExtraction(") or ""
        for token in ("!coopZombieStarterChosen", "while (!CoopZombieAllInsideExtraction(area)", "while (IsZombieModeActive", "CharacterMainControl.Main.SetPosition(area.transform.position);", "asset != BossRushCoop.LocalAssetId", "slot != Saves.SavesSystem.CurrentSlot"):
            if token not in z_extract:
                errors.append("丧尸撤离验收必须等待真实全员同圈和个人结算且固定原资产: " + token)
        protection = method_body(clean_source(code), "private void TryDamageModeGChildProtector(") or ""
        for token in ("controller.IsInChildProtection", "_modeGChildProtectorField.GetValue(controller)",
                      "child.Health.Invincible", "Team.IsEnemy(Teams.player, child.Team)",
                      "ApplyValidationDamage(child, CharacterMainControl.Main)"):
            if token not in protection:
                errors.append("Mode G 验收护卫辅助必须只伤实际敌对且可受伤的专属召唤物: " + token)
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
        final_case = method_body(clean_source(code), "private IEnumerator RunCampaignFinalBoss()") or ""
        if "new DamageInfo(player)" not in final_case or "damage.damageCreator" in final_case:
            errors.append("终章验收必须显式调用官方 DamageInfo 构造器初始化元素列表，不能引用不存在的字段")
        modeh = Path("ModeH/ModeHRuntimeModule_SceneFlow.cs").read_text(encoding="utf-8")
        reset = modeh.split("internal void ForceResetStateForValidation()", 1)[-1].split("private IEnumerator DriveCertification", 1)[0]
        for token in ("if (!ModBehaviour.DevModeEnabled) return;", "ReleaseRuntimeObjects();",
                      'TryReturnRealStakeOnAbort("f3_validation_cleanup")', "ModeHRuntimeGates.SetRunOwnerActive(false)"):
            if token not in reset:
                errors.append("Mode H 验收清理缺少: " + token)
        if "_arenaLease.Dispose()" in reset or "_spectatorLease.Dispose()" in reset:
            errors.append("Mode H 租约只支持 Release(sceneGeneration)，不存在 Dispose API")

        # 实际执行共享栈，不能靠注释中的 Current 关键字骗过守卫；行为由 C# fixture 验证。
        execution = Path("DebugAndTools/F3GameplayValidationExecution.cs").read_text(encoding="utf-8")
        stack = Path("DebugAndTools/ValidationCoroutineStack.cs").read_text(encoding="utf-8")
        for token in ("new ValidationCoroutineStack(inner)", "try { stack.Dispose(); }", "yield return current;", 'caseId + "_DISPOSE"'):
            if token not in stages:
                errors.append("用例必须由可 Dispose 的共享栈驱动: " + token)
        for token in ("top.Current", "_stack.Add(child)", "item.Dispose()", "AggregateException"):
            if token not in stack:
                errors.append("共享协程栈缺少嵌套/清理保证: " + token)
        for token in ("RunSession()", "TryStep(_sessionStack", "DisposeSessionStack();", "CompleteSession();",
                      "SavesSystem.OnSetFile += OnSessionSetFile", "SavesSystem.OnSetFile -= OnSessionSetFile",
                      "Application.logMessageReceived += OnSessionLog", "Application.logMessageReceived -= OnSessionLog",
                      "RestoreProtectedPlayers();", "_sessionCompleted", "HEARTBEAT", "RUNTIME_ERROR"):
            if token not in execution:
                errors.append("验收 session 缺少退出/日志契约: " + token)
        if "_coverage.AutomaticNotPassed > 0" not in runner or 'status = "INCOMPLETE"' not in runner:
            errors.append("缺少自动覆盖时不能给出总 PASS")
        seasons = Path("DebugAndTools/F3GameplayValidationSeasons.cs").read_text(encoding="utf-8")
        for token in ("MODE_G_NINE_WAVES", "state.AreAllSlotsResolved", "ModeGRuntimeGates.GetTrackedBosses()",
                      "ModeGExitReason.Victory", "MODE_H_FULL_SEASON", "button.onClick.Invoke()",
                      "GetParticipantsForValidation(true)", "transfers.Contains(2)", "transfers.Contains(4)",
                      "ModeHMatchReportStatus.Archived", "ModeHSeasonRewardOperationStatus.Archived",
                      "tokens.Add(report.resultToken)", "IsRuntimeReady(arenaScene)", "target.Health.Hurt(damage)"):
            if token not in seasons:
                errors.append("完整模式流程必须观察生产事实: " + token)
        # 名人堂确认后 Mode H 原地收场、不切场景：赛季用例再等基地场景就是白等超时后判红（2026-09-14 复核）。
        if "BaseSceneNameForValidation()" in seasons:
            errors.append("完整赛季收场在竞技场原地判干净，不得等基地场景")
        for token in ("SetInvincible(false)", "TryLockBattleResult", "TryTransition(", "FindObjectsOfType", "DebugFinishValidationSeason"):
            if token in seasons:
                errors.append("完整模式验收不能改状态/清场伪造自然终态: " + token)

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
