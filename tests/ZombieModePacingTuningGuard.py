from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
WAVE_CONTROLLER = Path("ZombieMode/ZombieModeWaveController.cs")
POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
HUD = Path("ZombieMode/ZombieModeHudController.cs")
SPAWNER = Path("ZombieMode/ZombieModeSpawner.cs")
BOSS_CONTROLLER = Path("ZombieMode/ZombieModeBossController.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
REWARD_CATALOG = Path("ZombieMode/ZombieModeRewardCatalogAndSelection.cs")
REWARD_SERVICES = Path("ZombieMode/ZombieModeRewardNpcServices.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print("ZombieModePacingTuningGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    wave_text = WAVE_CONTROLLER.read_text(encoding="utf-8")
    pollution_text = POLLUTION.read_text(encoding="utf-8")
    hud_text = HUD.read_text(encoding="utf-8")
    spawner_text = SPAWNER.read_text(encoding="utf-8")
    boss_controller_text = BOSS_CONTROLLER.read_text(encoding="utf-8")
    drops_text = DROPS.read_text(encoding="utf-8")
    reward_catalog_text = REWARD_CATALOG.read_text(encoding="utf-8")
    reward_services_text = REWARD_SERVICES.read_text(encoding="utf-8")
    localization_text = LOCALIZATION.read_text(encoding="utf-8")

    for required in [
        "public const float PreparationCountdownSeconds = 45f;",
        "public const float BossPreparationCountdownSeconds = 75f;",
    ]:
        if required not in text:
            return fail("preparation pacing contract missing -> " + required)

    if "extractionOpportunity\n                ? ZombieModeTuning.BossPreparationCountdownSeconds\n                : ZombieModeTuning.PreparationCountdownSeconds" not in wave_text:
        return fail("BeginZombieModePreparation must select 75/45 seconds from extractionOpportunity")

    for required in [
        "public const float PreparationSpawnIntervalSeconds = 5f;",
        "public const float NormalWaveSpawnIntervalStartSeconds = 3f;",
        "public const float NormalWaveSpawnIntervalStageStepSeconds = 0.4f;",
        "public const float NormalWaveSpawnIntervalCycleStepSeconds = 0.15f;",
        "public const float NormalWaveSpawnIntervalMinSeconds = 1.25f;",
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
        "zombieModeRunState.CurrentWaveBossesRemaining = GetZombieModeBossCountForWave(zombieModeRunState.CurrentWave);",
        "return ZombieModeTuning.BossWaveCountBase +",
        "GetZombieModeWaveCycleIndex(wave) * ZombieModeTuning.BossWaveCountPerCycle;",
    ]:
        if required not in wave_text:
            return fail("wave controller does not consume tidal pacing curve -> " + required)

    for required in [
        "SpawnZombieModeBossWaveAsync(runId, zombieModeRunState.CurrentWaveBossesRemaining).Forget();",
        "zombieModeRunState.CurrentWaveBossesRemaining = Mathf.Max(0, zombieModeRunState.CurrentWaveBossesRemaining - 1);",
        "zombieModeRunState.CurrentWaveBossesRemaining <= 0",
        "TrySpawnZombieModeBossDrop(runId, marker, character.transform.position);",
    ]:
        if required not in wave_text:
            return fail("multi-Boss waves must settle all Bosses and preserve per-Boss drops -> " + required)

    if "int total = GetZombieModeBossCountForWave(zombieModeRunState.CurrentWave);" not in hud_text:
        return fail("Boss HUD total must use the planned wave count while Bosses are still spawning")

    if "int total = zombieModeRunState.CurrentWaveBossInstances.Count;" in hud_text:
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
        "tuning.HealthMultiplier * GetZombieModeBossHealthScale(zombieModeRunState.CurrentWave)",
        "tuning.DamageMultiplier * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "multiplier *= GetZombieModeBossRewardScale(zombieModeRunState.CurrentWave);",
    ]:
        if required not in spawner_text:
            return fail("Boss body or kill reward does not scale by Boss cycle -> " + required)

    if "speedMultiplier = tuning.SpeedMultiplier *" in spawner_text:
        return fail("Boss cycle scaling must not increase Boss move speed")

    for required in [
        "ZombieModeTuning.TitanShockwaveDamage * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "ZombieModeTuning.HunterDashDamage * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "ZombieModeTuning.CorruptorZoneDamagePerSecond * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "ZombieModeTuning.CorruptorPoisonPathDamagePerSecond * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "ZombieModeTuning.SplitterBossDeathDamage * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
        "ZombieModeTuning.CorruptorDeathCloudDamagePerSecond * GetZombieModeBossDamageScale(zombieModeRunState.CurrentWave)",
    ]:
        if required not in boss_controller_text:
            return fail("Boss skill damage does not consume the displayed cycle multiplier -> " + required)

    for required in [
        "BossLootboxMinItemsBase + cycle * ZombieModeTuning.BossLootboxItemsPerCycle",
        "BossLootboxMaxItemsBase + cycle * ZombieModeTuning.BossLootboxItemsPerCycle",
        "BossLootboxMinQualityBase + cycle / ZombieModeTuning.BossLootboxMinQualityCycleStep",
        "Mathf.Clamp(5 + zombieModeRunState.PollutionTier + cycle, minQuality, 8)",
    ]:
        if required not in drops_text:
            return fail("Boss lootbox does not grow by Boss cycle -> " + required)

    for required in [
        "public int RemainingSelections = 1;",
        "GetZombieModeBossRewardSelectionCount(zombieModeRunState.CurrentWave)",
        "IsZombieModeBossBonusRewardSelection",
        "KeepZombieModeBossBonusRewardEntries",
        "selectedNode.RemainingSelections = Mathf.Max(1, selectedNode.RemainingSelections - 1);",
    ]:
        if required not in text + reward_catalog_text:
            return fail("later Boss nodes must grant the extra combat reward selection -> " + required)

    if "GetZombieModeBossRewardScale(zombieModeRunState.CurrentWave)" not in reward_services_text:
        return fail("Boss reward-node purification option does not scale by Boss cycle")

    if '"下一波 {0}：压力 {1} | 非 Boss 移速 {2}% | {3}"' not in localization_text:
        return fail("next-wave preview must identify the speed curve as non-Boss speed")

    if '"下一波 {0}：Boss 强度 {1} | 数量 {2} | 生命 {3}% | 伤害 {4}% | 支援 {5} | 净化收益 {6}%"' not in localization_text:
        return fail("Boss preview must expose the synchronized risk/reward curve")

    print("ZombieModePacingTuningGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
