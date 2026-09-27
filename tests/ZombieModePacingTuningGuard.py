from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
WAVE_CONTROLLER = Path("ZombieMode/ZombieModeRuntimeModule_WaveController.cs")
POLLUTION = Path("ZombieMode/ZombieModeRuntimeModule_PollutionTuning.cs")
HUD = Path("ZombieMode/ZombieModeHudController.cs")
HUD_RUNTIME = Path("ZombieMode/ZombieModeRuntimeModule_Hud.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
BOSS_CONTROLLER = Path("ZombieMode/ZombieModeRuntimeModule_BossController.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
REWARD_CATALOG = Path("ZombieMode/ZombieModeRuntimeModule_RewardCatalogAndSelection.cs")
REWARD_PREPARATION = Path("ZombieMode/ZombieModeRewardHostBridge.cs")
RUNTIME_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
REWARD_SERVICES = Path("ZombieMode/ZombieModeRewardNpcServices.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print("ZombieModePacingTuningGuard: FAIL - " + message)
    return 1


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""
    brace = text.find("{", start)
    if brace < 0:
        return ""
    depth = 0
    for index in range(brace, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    return ""


def main() -> int:
    text = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    wave_text = WAVE_CONTROLLER.read_text(encoding="utf-8")
    pollution_text = POLLUTION.read_text(encoding="utf-8")
    hud_text = HUD.read_text(encoding="utf-8")
    hud_runtime_text = HUD_RUNTIME.read_text(encoding="utf-8")
    spawner_text = SPAWNER.read_text(encoding="utf-8")
    boss_controller_text = BOSS_CONTROLLER.read_text(encoding="utf-8")
    drops_text = DROPS.read_text(encoding="utf-8")
    reward_catalog_text = REWARD_CATALOG.read_text(encoding="utf-8")
    reward_preparation_text = REWARD_PREPARATION.read_text(encoding="utf-8")
    runtime_module_text = RUNTIME_MODULE.read_text(encoding="utf-8")
    host_bridge_text = Path("ZombieMode/ZombieModeEntryHostBridge.cs").read_text(encoding="utf-8")
    reward_services_text = REWARD_SERVICES.read_text(encoding="utf-8")
    localization_text = LOCALIZATION.read_text(encoding="utf-8")

    for required in [
        "public const float PreparationCountdownSeconds = 45f;",
    ]:
        if required not in text:
            return fail("preparation pacing contract missing -> " + required)

    if not (
        ": GetZombieModeSelectedPreparationDuration(runId);" in wave_text
        and "ZombieModePreparationDurationMaximumSeconds = 300" in runtime_module_text
        and "module.GetZombieModeSelectedPreparationDuration(runId)" in reward_preparation_text
    ):
        return fail("BeginZombieModePreparation must use the run-persistent player duration with a 45-second fallback")

    for required in [
        "internal int GetZombieModeSelectedPreparationDuration(int runId)",
        "Mathf.RoundToInt(ZombieModeTuning.PreparationCountdownSeconds)",
        "Mathf.Clamp(",
        "ZombieModePreparationDurationMinimumSeconds",
        "ZombieModePreparationDurationMaximumSeconds",
        "internal void OpenZombieModePreparationDurationEditor(int runId)",
        "internal void SetZombieModePreparationDuration(int runId, int seconds)",
        "ZombieModePreparationDurationOptions[i] != seconds",
        "runState.SelectedPreparationDurationSeconds = seconds;",
    ]:
        if required not in runtime_module_text:
            return fail("preparation-duration runtime logic missing -> " + required)

    host_methods = [
        ("public int GetZombieModeSelectedPreparationDuration(int runId)",
         "module.GetZombieModeSelectedPreparationDuration(runId)"),
        ("public void OpenZombieModePreparationDurationEditor(int runId)",
         "module.OpenZombieModePreparationDurationEditor(runId)"),
        ("public void SetZombieModePreparationDuration(int runId, int seconds)",
         "module.SetZombieModePreparationDuration(runId, seconds)"),
    ]
    for signature, forward in host_methods:
        method = extract_method(reward_preparation_text, signature)
        if not method or forward not in method:
            return fail("legacy preparation-duration API must forward to RuntimeModule -> " + signature)
        if "zombieModeRunState." in method or "ZombieModePreparationDurationOptions" in method:
            return fail("legacy preparation-duration API must not retain state or option logic -> " + signature)

    if "owner.ShowZombieModeRewardSelectionForRuntimeModule(runId, runState.CurrentRewardNode.BossNode, true);" not in runtime_module_text:
        return fail("opening the duration editor must preserve the current reward node and expanded-editor flag")
    if "owner.ShowZombieModeRewardSelectionForRuntimeModule(runId, bossNode, false);" not in runtime_module_text:
        return fail("saving duration must preserve the current reward node and collapsed-editor flag")
    if "ShowZombieModeRewardSelection(runId, bossNode, restEditorExpanded);" not in host_bridge_text:
        return fail("RuntimeModule reward-selection bridge must preserve the existing UI arguments")

    if "runState.SelectedPreparationDurationSeconds = 0;" in wave_text:
        return fail("player-selected preparation duration must persist across waves")

    for required in [
        "public const float PreparationSpawnIntervalSeconds = 1.65f;",
        "public const float NormalWaveSpawnIntervalStartSeconds = 1f;",
        "public const float NormalWaveSpawnIntervalStageStepSeconds = 0.14f;",
        "public const float NormalWaveSpawnIntervalCycleStepSeconds = 0.05f;",
        "public const float NormalWaveSpawnIntervalMinSeconds = 0.4f;",
        "public const float BossWaveSpawnIntervalStartSeconds = 1f;",
        "public const float BossWaveSpawnIntervalCycleStepSeconds = 0.04f;",
        "public const float BossWaveSpawnIntervalMinSeconds = 0.65f;",
        "public const int MaxNormalZombieCount = 150;",
        "public const int NormalWavePressureBase = 24;",
        "public const int NormalWavePressurePerCycle = 18;",
        "public static readonly int[] NormalWavePressureStageOffsets = { 0, 12, 27, 45 };",
        "public const int NormalWaveKillTargetBase = 18;",
        "public const int NormalWaveKillTargetPerCycle = 18;",
        "public static readonly int[] NormalWaveKillTargetStageOffsets = { 0, 6, 12, 20 };",
        "public const int NormalWavePressurePerRemainingKill = 3;",
        "public const int PreparationPressureMinimum = 12;",
        "public const int PreparationPressureMaximum = 48;",
        "public const int BossWaveSupportPressureBase = 24;",
        "public const int BossWaveSupportPressurePerCycle = 9;",
        "public const int BossWaveSupportPressureMaximum = 60;",
        "public const float WaveSpeedMultiplierStart = 0.72f;",
        "public const float WaveSpeedMultiplierPerWave = 0.035f;",
        "public const float WaveSpeedMultiplierMaximum = 1f;",
        "public const int BossWaveCountBase = 1;",
        "public const int BossWaveCountPerCycle = 1;",
        "public const int LateWaveNormalEnemyWeight = 100;",
        "public const int LateWaveEliteWeightPerWave = 3;",
        "public const int LateWaveSpecialWeightPerWave = 5;",
        "public const float HealthScalePerCycle = 0.30f;",
        "public const float HealthScaleMaximum = 2.5f;",
        "public const float DamageScalePerCycle = 0.12f;",
        "public const float DamageScaleMaximum = 1.8f;",
        "public const float BossRewardScalePerCycle = 0.25f;",
        "public const float BossRewardScaleMaximum = 3f;",
        "public const int BossBonusSelectionStartCycle = 1;",
        "public const int BossRewardSelectionMaximum = 2;",
        "public const int StarterGunnerExtraAmmoCount = 2000;",
        "public const int RandomGunRewardAmmoCount = 120;",
        "public const int AmmoSupplyRewardAmmoCount = 240;",
        "public const int ContractGunRewardAmmoCount = 120;",
        "public const int MerchantAmmoPurchaseCount = 200;",
        "public const int AmmoRainSingleStackCount = 120;",
        "public const int AmmoRainDoubleStackCount = 180;",
    ]:
        if required not in text:
            return fail("tidal pacing contract missing -> " + required)

    for required in [
        "return ZombieModeTuning.PreparationSpawnIntervalSeconds;",
        "ZombieModeTuning.NormalWaveSpawnIntervalStartSeconds -",
        "stage * ZombieModeTuning.NormalWaveSpawnIntervalStageStepSeconds -",
        "cycle * ZombieModeTuning.NormalWaveSpawnIntervalCycleStepSeconds",
        "ZombieModeTuning.NormalWaveSpawnIntervalMinSeconds",
        "ZombieModeTuning.WaveSpeedMultiplierStart +",
        "Mathf.Max(0, wave - 1) * ZombieModeTuning.WaveSpeedMultiplierPerWave",
        "ZombieModeTuning.WaveSpeedMultiplierMaximum",
        "runState.CurrentWaveBossesRemaining = GetZombieModeBossCountForWave(runState.CurrentWave);",
        "return ZombieModeTuning.BossWaveCountBase +",
        "GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossWaveCountPerCycle;",
    ]:
        if required not in wave_text:
            return fail("wave controller does not consume tidal pacing curve -> " + required)

    for required in [
        "SpawnZombieModeBossWaveAsync(runId, runState.CurrentWaveBossesRemaining).Forget();",
        "runState.CurrentWaveBossesRemaining = Mathf.Max(0, runState.CurrentWaveBossesRemaining - 1);",
        "runState.CurrentWaveBossesRemaining <= 0",
        "TrySpawnZombieModeBossDrop(runId, marker, character.transform.position);",
    ]:
        if required not in wave_text:
            return fail("multi-Boss waves must settle all Bosses and preserve per-Boss drops -> " + required)

    if "int total = owner.GetZombieModeBossCountForWaveForHud(runState.CurrentWave);" not in hud_runtime_text:
        return fail("Boss HUD total must use the planned wave count while Bosses are still spawning")

    if "int total = runState.CurrentWaveBossInstances.Count;" in hud_runtime_text:
        return fail("Boss HUD total must not grow incrementally with spawned instances")

    if "PeriodicSpawnIntervalSeconds" in text + wave_text:
        return fail("fixed periodic spawn interval must not replace the tidal pacing curve")

    for forbidden in [
        "BossesPerBossWave",
        "GetZombieModeBossCount()",
        "effectiveSpawnPointCount",
        "BossWaveCountMaximum",
        "BossCountMaximum",
        "MaxBossesPerBossWave",
    ]:
        if forbidden in wave_text + spawner_text:
            return fail("Boss count must grow only by wave cycle without a gameplay cap -> " + forbidden)

    for required in [
        "int lateWave = pacingWave - 5;",
        "eliteWeight = GetZombieModeEliteBaseWeight(pollution) +",
        "lateWave * (float)ZombieModeTuning.LateWaveEliteWeightPerWave;",
        "specialWeight = GetZombieModeSpecialBaseWeight(pollution) +",
        "lateWave * (float)ZombieModeTuning.LateWaveSpecialWeightPerWave;",
        "normalWeight = ZombieModeTuning.LateWaveNormalEnemyWeight;",
        "Random.value * (eliteWeight + specialWeight + normalWeight)",
    ]:
        if required not in pollution_text:
            return fail("late-wave elite/special weights must keep growing without a probability cap -> " + required)

    for required in [
        "tuning.HealthMultiplier * GetZombieModeBossHealthScale(runState.CurrentWave)",
        "tuning.DamageMultiplier * GetZombieModeBossDamageScale(runState.CurrentWave)",
        "multiplier *= GetZombieModeBossRewardScale(runState.CurrentWave);",
    ]:
        if required not in spawner_text:
            return fail("Boss body or kill reward does not scale by Boss cycle -> " + required)

    if "speedMultiplier = tuning.SpeedMultiplier *" in spawner_text:
        return fail("Boss cycle scaling must not increase Boss move speed")

    for required in [
        "ZombieModeTuning.TitanShockwaveDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
        "ZombieModeTuning.HunterDashDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
        "ZombieModeTuning.CorruptorZoneDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
        "ZombieModeTuning.CorruptorPoisonPathDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
        "ZombieModeTuning.SplitterBossDeathDamage * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
        "ZombieModeTuning.CorruptorDeathCloudDamagePerSecond * owner.GetZombieModeBossDamageScaleForRuntimeModule(runState.CurrentWave)",
    ]:
        if required not in boss_controller_text:
            return fail("Boss skill damage does not consume the displayed cycle multiplier -> " + required)

    for required in [
        "BossLootboxMinItemsBase + cycle * ZombieModeTuning.BossLootboxItemsPerCycle",
        "BossLootboxMaxItemsBase + cycle * ZombieModeTuning.BossLootboxItemsPerCycle",
        "BossLootboxMinQualityBase + cycle / ZombieModeTuning.BossLootboxMinQualityCycleStep",
        "Mathf.Clamp(5 + runState.PollutionTier + cycle, minQuality, 8)",
    ]:
        if required not in drops_text:
            return fail("Boss lootbox does not grow by Boss cycle -> " + required)

    for required in [
        "public int RemainingSelections = 1;",
        "owner.GetZombieModeBossRewardSelectionCountForRewardRuntimeModule(runState.CurrentWave)",
        "IsZombieModeBossBonusRewardSelection",
        "KeepZombieModeBossBonusRewardEntries",
        "selectedNode.RemainingSelections = Mathf.Max(1, selectedNode.RemainingSelections - 1);",
    ]:
        if required not in text + reward_catalog_text:
            return fail("later Boss nodes must grant the extra combat reward selection -> " + required)

    if "GetZombieModeBossRewardScale(runState.CurrentWave)" not in reward_services_text:
        return fail("Boss reward-node purification option does not scale by Boss cycle")

    if '"下一波 {0}：压力 {1} | 非 Boss 移速 {2}% | {3}"' not in localization_text:
        return fail("next-wave preview must identify the speed curve as non-Boss speed")

    if '"下一波 {0}：Boss 强度 {1} | 数量 {2} | 生命 {3}% | 伤害 {4}% | 支援 {5} | 净化收益 {6}%"' not in localization_text:
        return fail("Boss preview must expose the synchronized risk/reward curve")

    print("ZombieModePacingTuningGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
