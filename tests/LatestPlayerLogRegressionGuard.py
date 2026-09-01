#!/usr/bin/env python3
"""Guard: fixes derived from the 2026-08-31 F3 Player.log remain wired."""

from pathlib import Path
import sys


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8", errors="ignore")


def main() -> int:
    errors = []

    patch_path = Path("Patches/Compatibility/MagicBlendInitializationOrderPatch.cs")
    patch = read(str(patch_path)) if patch_path.is_file() else ""
    for token in ("MagicBlendState", "_isInitialized", "ReplayWhenInitialized",
                  "current.fullPathHash == enteredState.fullPathHash", "return false;"):
        if token not in patch:
            errors.append("MagicBlend 动态角色初始化竞态修复缺失: " + token)
    if "Patches\\Compatibility\\MagicBlendInitializationOrderPatch.cs" not in read("compile_official.bat"):
        errors.append("MagicBlend 兼容补丁未登记正式编译清单")

    required_group_init = {
        "Campaign/CampaignBoardInteractable.cs": "[CampaignBoard]",
        "Campaign/CampaignFinalBossInteractable.cs": "[CampaignFinalBoss]",
        "Integration/DailyReport/DailyReportInteractable.cs": "[DailyReport]",
        "Integration/BackMountain/ShowcaseInteractable.cs": "[BackMountainShowcase]",
        "Integration/AffixForge/GoblinAffixForgeInteractable.cs": "[AffixForge]",
        "RandomEvents/RandomEventEffectsBridge_Spawn.cs": "[RandomEventMerchantShop]",
    }
    for path, label in required_group_init.items():
        code = read(path)
        at = code.find(label)
        base_at = code.find("base.Awake()", at)
        if at < 0 or base_at < at or "GetOrCreateGroupList" not in code[max(0, at - 180):base_at]:
            errors.append(path + " 未在 base.Awake 前初始化 otherInterablesInGroup")

    ui = read("UIAndSigns/UIAndSigns.cs")
    runner = read("DebugAndTools/F3GameplayValidationRunner.cs")
    if ui.count("if (GameplayValidationSuppressNotifications) return;") < 2:
        errors.append("完整验收未抑制普通消息与大横幅队列")
    if "GameplayValidationSuppressNotifications = false" not in runner:
        errors.append("完整验收缺少通知抑制复位")

    hooks = read("Utilities/ModeRuntimeHooks.cs")
    scheduler_at = hooks.find("TickModeEFSpawnPostprocessScheduler()")
    waves_at = hooks.find("TickWavesArenaRuntime(deltaTime)")
    if not (0 <= scheduler_at < waves_at):
        errors.append("共享刷怪后处理队列仍被 WavesArena early-return 门控")

    mode_d_waves = read("ModeD/ModeDWaves.cs")
    for token in ("Team.IsEnemy(Teams.player, character.Team)",
                  "character.SetTeam(Teams.wolf)", "confirmedHostile",
                  "modeDCurrentWaveEnemies.Add(character)"):
        if token not in mode_d_waves:
            errors.append("Mode D 敌对性/登记不变式缺失: " + token)
    if not (mode_d_waves.find("character.SetTeam(Teams.wolf)")
            < mode_d_waves.find("modeDCurrentWaveEnemies.Add(character)")):
        errors.append("Mode D 必须在登记本波敌人前完成敌对性修正")

    mode_d = read("ModeD/ModeD.cs")
    for token in ("CleanupModeDWaveEnemiesOnExit()", "UnregisterEnemyRecovery(enemy)",
                  "enemy.dropBoxOnDead = false", "Destroy(enemy.gameObject)"):
        if token not in mode_d:
            errors.append("Mode D 退出实体清理缺失: " + token)
    inactive_at = mode_d.find("if (!modeDActive)")
    inactive_cleanup_at = mode_d.find("CleanupModeDWaveEnemiesOnExit()", inactive_at)
    inactive_return_at = mode_d.find("return;", inactive_at)
    if not (0 <= inactive_at < inactive_cleanup_at < inactive_return_at):
        errors.append("Mode D 重复结束必须仍清理可能残留的登记实体")

    mode_e_battle = read("ModeE/ModeEBattle.cs")
    for token in ("ModeECharacterPresetLease", "presetLease.Assign(customPreset)",
                  "Destroy(_ownedPreset, 0.05f)"):
        if token not in mode_e_battle:
            errors.append("Mode E/F 克隆预设租约缺失: " + token)

    for path in ("ModeE/ModeELifecycle.cs", "ModeE/ModeEIntegrityAndHelpers.cs",
                 "ModeE/ModeEBattle_ScalingAndRuntime.cs", "ModeF/ModeFRespawn.cs"):
        code = read(path)
        if "Destroy(enemy.characterPreset" in code or "Destroy(character.characterPreset" in code:
            errors.append(path + " 仍在角色销毁前提前释放 characterPreset")

    mode_e_lifecycle = read("ModeE/ModeELifecycle.cs")
    lifecycle_tokens = ("enemy.dropBoxOnDead = false", "CleanupModeEEnemyRuntimeState(enemy, enemyFaction)",
                        "enemy.gameObject.SetActive(false)", "Destroy(enemy.gameObject)")
    positions = [mode_e_lifecycle.find(token) for token in lifecycle_tokens]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        errors.append("Mode E 结束清理必须按禁掉落、注销运行时、停用、销毁角色的顺序执行")
    if "enemy.Health.Hurt" in mode_e_lifecycle:
        errors.append("Mode E 模式结束不得通过 Hurt 触发死亡副作用")

    merchant = read("ModeE/ModeEMerchant.cs")
    helper_start = merchant.find("private StockShop CreateConfiguredModeEMerchantShop(")
    helper_end = merchant.find("\n        private ", helper_start + 1)
    helper = merchant[helper_start:helper_end] if helper_start >= 0 and helper_end > helper_start else ""
    merchant_tokens = ("shopObject.SetActive(false)", "shopObject.AddComponent<StockShop>()",
                       "ModeEMerchantAwakeBootstrapId", "shopObject.SetActive(true)",
                       "TryConfigureModeEMerchantShopIdentity(shop, stableMerchantId, true)")
    merchant_positions = [helper.find(token) for token in merchant_tokens]
    if any(position < 0 for position in merchant_positions) or merchant_positions != sorted(merchant_positions):
        errors.append("Mode E 分类商店未按 inactive/Add/bootstrap/Awake/stable ID 顺序初始化")

    diagnostics = read("DebugAndTools/F3GameplayValidationDiagnostics.cs")
    for token in ("ValidationCountPlayableModeDEnemies", "ValidationCountModeOwnedCharacters",
                  "FindObjectsOfType<CharacterMainControl>(true)", "ValidationTryGetArenaCleanState"):
        if token not in diagnostics:
            errors.append("F3 清场诊断缺失: " + token)
    if "Time.realtimeSinceStartup + 2f" not in runner:
        errors.append("F3 用例清理未等待 Unity 延迟销毁完成")

    if errors:
        for error in errors:
            print("  - " + error)
        print("LatestPlayerLogRegressionGuard: FAIL")
        return 1
    print("LatestPlayerLogRegressionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
