"""Legacy wave timers and countdown state belong to the registered arena module."""

from pathlib import Path
import re

from cs_source_util import clean_source


ROOT = Path(__file__).resolve().parent.parent
HOST = clean_source((ROOT / "ModBehaviour.cs").read_text(encoding="utf-8-sig"))
BRIDGE = clean_source((ROOT / "WavesArena/WavesArenaRuntimeHooks.cs").read_text(encoding="utf-8-sig"))
LOOT_BRIDGE = clean_source((ROOT / "LootAndRewards/LootAndRewards.cs").read_text(encoding="utf-8-sig"))
MODULE = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule.cs").read_text(encoding="utf-8-sig"))
TICK = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_Tick.cs").read_text(encoding="utf-8-sig"))
SPAWNERS = clean_source((ROOT / "WavesArena/WavesArenaSpawnerControl.cs").read_text(encoding="utf-8-sig"))
STATIC_RESET = clean_source((ROOT / "LootAndRewards/LootAndRewardsStaticCacheReset.cs").read_text(encoding="utf-8-sig"))
ARENA = clean_source((ROOT / "WavesArena/WavesArena.cs").read_text(encoding="utf-8-sig"))
PRESETS = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_EnemyPresets.cs").read_text(encoding="utf-8-sig"))
HELL_COMPLETION = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_InfiniteHellCompletion.cs").read_text(encoding="utf-8-sig"))
HELL_BRIDGE = clean_source((ROOT / "LootAndRewards/LootAndRewardsInfiniteHell.cs").read_text(encoding="utf-8-sig"))
BOSS_SPAWN = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_BossSpawning.cs").read_text(encoding="utf-8-sig"))
BOSS_BRIDGE = clean_source((ROOT / "WavesArena/WavesArenaBossSpawning.cs").read_text(encoding="utf-8-sig"))
COUNTDOWN = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_Countdown.cs").read_text(encoding="utf-8-sig"))
WAVE_DEATHS = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_WaveDeaths.cs").read_text(encoding="utf-8-sig"))
LOOT_STATE = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_LootState.cs").read_text(encoding="utf-8-sig"))
START = clean_source((ROOT / "WavesArena/WavesArenaRuntimeModule_Start.cs").read_text(encoding="utf-8-sig"))
BOSS_START_HOST = clean_source((ROOT / "WavesArena/WavesArenaBossSpawning.cs").read_text(encoding="utf-8-sig"))
REGISTRATION = clean_source((ROOT / "ModBehaviourRuntimeModules.cs").read_text(encoding="utf-8-sig"))


def main():
    if "wavesArenaRuntime = new WavesArenaRuntimeModule();" not in REGISTRATION:
        raise AssertionError("arena module instance must be created once")
    if "runtimeModuleHost.Register(wavesArenaRuntime);" not in REGISTRATION:
        raise AssertionError("registered arena module must be the stored instance")
    for old_name, new_name in (
        ("waitingForNextWave", "WaitingForNextWave"),
        ("waveCountdown", "WaveCountdown"),
        ("lastWaveCountdownSeconds", "LastWaveCountdownSeconds"),
        ("waveIntegrityCheckTimer", "WaveIntegrityCheckTimer"),
        ("daXingXingCleanTimer", "DaXingXingCleanTimer"),
        ("totalEnemies", "TotalEnemies"),
        ("defeatedEnemies", "DefeatedEnemies"),
        ("nextWaveBossName", "NextWaveBossName"),
        ("bossesPerWave", "BossesPerWave"),
        ("bossesInCurrentWaveTotal", "BossesInCurrentWaveTotal"),
        ("bossesInCurrentWaveRemaining", "BossesInCurrentWaveRemaining"),
    ):
        if new_name + " { get; set; }" not in MODULE:
            raise AssertionError(new_name + " state must be owned by WavesArenaRuntimeModule")
        if ("get { return wavesArenaRuntime." + new_name + "; }") not in BRIDGE:
            raise AssertionError(old_name + " getter must forward to the arena owner")
        if ("set { wavesArenaRuntime." + new_name + " = value; }") not in BRIDGE:
            raise AssertionError(old_name + " setter must forward to the arena owner")
        if re.search(r"^\s*private\s+(?:readonly\s+)?[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*(?:=|;)", HOST, re.M):
            raise AssertionError(old_name + " must not remain a host field")
    if "LastWaveCountdownSeconds { get; set; } = -1;" not in MODULE:
        raise AssertionError("initial countdown marker must stay negative")
    if "BossesPerWave { get; set; } = 1;" not in MODULE:
        raise AssertionError("default wave size must remain one")
    if "CurrentWaveBosses =" not in MODULE or "new System.Collections.Generic.List<UnityEngine.MonoBehaviour>()" not in MODULE:
        raise AssertionError("the module must own the mutable current wave boss list")
    if ("CurrentEnemyIndex { get; set; }" not in MODULE
            or "get { return wavesArenaRuntime.CurrentEnemyIndex; }" not in HOST
            or "set { wavesArenaRuntime.CurrentEnemyIndex = value; }" not in HOST):
        raise AssertionError("current enemy index must be stored in arena module and bridged by host")
    if re.search(r"private int currentEnemyIndex\s*(?:=|;)", HOST):
        raise AssertionError("current enemy index must not remain a host field")
    if ("CurrentBoss { get; set; }" not in MODULE
            or "get { return wavesArenaRuntime.CurrentBoss; }" not in HOST
            or "set { wavesArenaRuntime.CurrentBoss = value; }" not in HOST):
        raise AssertionError("current boss must be stored in arena module and bridged by host")
    if ("DemoChallengeStartPosition { get; set; }" not in MODULE
            or "get { return wavesArenaRuntime.DemoChallengeStartPosition; }" not in HOST
            or "set { wavesArenaRuntime.DemoChallengeStartPosition = value; }" not in HOST):
        raise AssertionError("arena return position must be stored in arena module and bridged by host")
    if "CountedDeadBosses =" not in MODULE or "wavesArenaRuntime.CountedDeadBosses" not in LOOT_BRIDGE:
        raise AssertionError("wave death de-duplication must belong to arena module")
    if "get { return wavesArenaRuntime.CurrentWaveBosses; }" not in BRIDGE:
        raise AssertionError("legacy boss list reads must forward to the module")
    if "private readonly List<MonoBehaviour> currentWaveBosses" in HOST:
        raise AssertionError("the current wave boss list must not remain a host field")
    for old_name, new_name in (
        ("enemyPresets", "EnemyPresets"),
        ("_enemyPresetInitializationScanCount", "EnemyPresetInitializationScanCount"),
        ("minBossBaseHealth", "MinBossBaseHealth"),
        ("maxBossBaseHealth", "MaxBossBaseHealth"),
    ):
        if new_name + " { get; set; }" not in MODULE:
            raise AssertionError(new_name + " must belong to the arena module")
        if "get { return wavesArenaRuntime." + new_name + "; }" not in BRIDGE:
            raise AssertionError(old_name + " getter must forward to the arena owner")
        if "set { wavesArenaRuntime." + new_name + " = value; }" not in BRIDGE:
            raise AssertionError(old_name + " setter must forward to the arena owner")
        if re.search(r"^\s*private\s+[^\n]+\s+" + re.escape(old_name) + r"\s*(?:=|;)", LOOT_BRIDGE, re.M):
            raise AssertionError(old_name + " must not remain a loot host field")
    if "internal static bool EnemyPresetsInitialized { get; set; }" not in MODULE:
        raise AssertionError("enemy preset initialization flag must belong to the arena module")
    if "WavesArenaRuntimeModule.EnemyPresetsInitialized" not in BRIDGE:
        raise AssertionError("enemy preset initialization bridge missing")
    if "internal bool SpawnersDisabled { get; set; }" not in MODULE:
        raise AssertionError("spawner latch must belong to the arena module")
    if "wavesArenaRuntime.SpawnersDisabled" not in HOST:
        raise AssertionError("spawner latch host bridge missing")
    if "internal sealed partial class WavesArenaRuntimeModule" not in SPAWNERS:
        raise AssertionError("spawner control must execute in arena module")
    if "internal sealed partial class WavesArenaRuntimeModule" not in STATIC_RESET:
        raise AssertionError("loot static cache reset must execute in arena module")
    if "WavesArenaRuntimeModule.ResetLootAndRewardsStaticCaches();" not in LOOT_BRIDGE:
        raise AssertionError("loot reset host entry must forward to arena module")
    if "internal static readonly HashSet<string> EarlyWaveExcludedBosses" not in MODULE:
        raise AssertionError("early-wave exclusion policy must belong to arena module")
    if "wavesArenaRuntime.EnsureEarlyWavesNoStrongBoss();" not in ARENA:
        raise AssertionError("early-wave reorder entry must forward to arena module")
    for implementation, forwarding in (
        ("internal void InitializeEnemyPresets()", "wavesArenaRuntime.InitializeEnemyPresets();"),
        ("internal bool EnsureEnemyPresetsReadyForGameplayCatalogs()", "wavesArenaRuntime.EnsureEnemyPresetsReadyForGameplayCatalogs();"),
        ("internal EnemyPresetInfo PickRandomEnemyForInfiniteHell()", "wavesArenaRuntime.PickRandomEnemyForInfiniteHell();"),
        ("internal string GetLocalizedCharacterName(string nameKey)", "wavesArenaRuntime.GetLocalizedCharacterName(nameKey);"),
    ):
        if implementation not in PRESETS or forwarding not in ARENA:
            raise AssertionError("enemy preset production method must execute in arena module: " + implementation)
    if "EnemyPresetsInitialized = true;" not in PRESETS or "owner.NotifyArenaPresetCatalogsRefreshed();" not in PRESETS:
        raise AssertionError("preset initialization must notify dependent catalogs after the arena state changes")
    if "WavesArenaRuntimeModule.IsRuntimeCharacterPresetClone(preset)" not in ARENA:
        raise AssertionError("legacy clone predicate must forward to the module")
    if re.search(r"private static (?:System\.Reflection\.)?MethodInfo _cachedToPlainTextMethod", ARENA):
        raise AssertionError("localization reflection cache must not remain on the host")
    if "wavesArenaRuntime.OnInfiniteHellWaveCompleted_LootAndRewards();" not in HELL_BRIDGE:
        raise AssertionError("Infinite Hell wave completion must forward to the arena module")
    for token in (
        "internal async void OnInfiniteHellWaveCompleted_LootAndRewards()",
        "WavesArenaRuntimeModule.CaptureValidity(owner, false, true)",
        "InfiniteHellWaveIndex++",
        "if (!isCompletionCurrent()) return;",
        "WavesArenaRuntimeModule.EnqueueMilestone(owner, currentTier, basePos);",
        "InfiniteHellMilestoneRewardTier = currentTier;",
        "owner.UseInteractBetweenWavesForArena",
        "owner.StartNextWaveCountdown();",
    ):
        if token not in HELL_COMPLETION:
            raise AssertionError("Infinite Hell completion moved without original gate or ordering: " + token)
    if HELL_COMPLETION.index("WavesArenaRuntimeModule.EnqueueMilestone(owner, currentTier, basePos);") > HELL_COMPLETION.index("InfiniteHellMilestoneRewardTier = currentTier;"):
        raise AssertionError("milestone must be queued before advancing the committed tier")
    if "internal void SpawnNextEnemy() { wavesArenaRuntime.SpawnNextEnemy(); }" not in BOSS_BRIDGE:
        raise AssertionError("legacy spawn entry must forward to the registered arena module")
    for token in (
        "internal void SpawnNextEnemy()",
        "owner.NotifyCourierBossFightStart();",
        "owner.NotifyCourierNoBoss(false);",
        "var filteredPresets = owner.GetFilteredEnemyPresets();",
        "owner.OnAllEnemiesDefeatedForArena();",
        "preset = PickRandomEnemyForInfiniteHell();",
        "WavesArenaRuntimeModule.CaptureValidity(owner, true, true)",
        "SpawnBossWithVerificationAsync(preset, spawnPos, spawnPoints, isSpawnCurrent).Forget();",
        "SpawnMultipleBossesWithVerificationAsync(bossSpawnInfos, spawnPoints, isSpawnCurrent).Forget();",
        "const int maxRetries = 3;",
        "if (!isSpawnCurrent()) return;",
        "await UniTask.Delay(200);",
        "await UniTask.Delay(50);",
        "await UniTask.Delay(300);",
        "await UniTask.Delay(100);",
        "int liveBossCount = PruneAndCountTrackedWaveBosses();",
        "BossesInCurrentWaveTotal = liveBossCount;",
        "BossesInCurrentWaveRemaining = liveBossCount;",
        "owner.ProceedAfterWaveFinished();",
    ):
        if token not in BOSS_SPAWN:
            raise AssertionError("spawn or retry trace moved without its original gate: " + token)
    if BOSS_SPAWN.index("owner.NotifyCourierBossFightStart();") > BOSS_SPAWN.index("var filteredPresets = owner.GetFilteredEnemyPresets();"):
        raise AssertionError("courier notification must precede filtered pool selection")
    if BOSS_SPAWN.index("int liveBossCount = PruneAndCountTrackedWaveBosses();") > BOSS_SPAWN.index("BossesInCurrentWaveRemaining = liveBossCount;"):
        raise AssertionError("failed batch must prune tracked bosses before correcting remaining count")
    if re.search(r"private async UniTaskVoid Spawn(?:Boss|MultipleBosses)WithVerificationAsync", BOSS_BRIDGE):
        raise AssertionError("spawn retry coroutine must not remain on the host")
    if "public void StartFirstWave() { wavesArenaRuntime.StartFirstWave(); }" not in BOSS_START_HOST:
        raise AssertionError("first wave entry must forward to arena module")
    for token in (
        "public void StartFirstWave()",
        "owner.GetFilteredEnemyPresets();",
        "owner.ClearEnemiesForBossRushForArena();",
        "owner.BeginAchievementSessionForArena(InfiniteHellMode ? \"InfiniteHell\" : \"BossRush\");",
        "owner.SetBossRushRuntimeActiveForArena(true);",
        "WavesArenaRuntimeModule.ResetMilestones(owner);",
        "owner.TryRollMutatorsForArena(InfiniteHellMode ? \"InfiniteHell\" : \"BossRush\");",
        "CurrentEnemyIndex = 0;",
        "CountedDeadBosses.Clear();",
        "owner.SubscribeArenaBossDeaths();",
        "SpawnNextEnemy();",
    ):
        if token not in START:
            raise AssertionError("first wave setup must retain its original state and subscription trace: " + token)
    start_order = (
        "owner.ClearEnemiesForBossRushForArena();",
        "owner.BeginAchievementSessionForArena(",
        "owner.SetBossRushRuntimeActiveForArena(true);",
        "WavesArenaRuntimeModule.ResetMilestones(owner);",
        "owner.TryRollMutatorsForArena(",
        "CurrentEnemyIndex = 0;",
        "owner.SubscribeArenaBossDeaths();",
        "SpawnNextEnemy();",
    )
    if [START.index(token) for token in start_order] != sorted(START.index(token) for token in start_order):
        raise AssertionError("first wave setup, mutator roll and death subscription order changed")
    if "wavesArenaRuntime.StartNextWaveCountdown(showInitialBanner, suppressImmediateRepeatBanner);" not in ARENA:
        raise AssertionError("legacy countdown entry must forward to arena module")
    if "wavesArenaRuntime.ShowNextWaveCountdownBanner(secondsInt);" not in ARENA:
        raise AssertionError("legacy banner entry must forward to arena module")
    for token in (
        "float interval = owner.GetWaveIntervalSeconds();",
        "float milestoneBonus = owner.GetMilestoneRestBonusSeconds();",
        "int completedWave = InfiniteHellMode ? InfiniteHellWaveIndex : CurrentEnemyIndex;",
        "if (completedWave > 0 && completedWave % 5 == 0)",
        "WaitingForNextWave = false;",
        "SpawnNextEnemy();",
        "WaitingForNextWave = true;",
        "WaveCountdown = interval;",
        "if (showInitialBanner && (interval <= 5f || milestoneBonusApplied))",
        "else if (suppressImmediateRepeatBanner)",
        "owner.ShowBigBanner(L10n.T(",
    ):
        if token not in COUNTDOWN:
            raise AssertionError("countdown trace moved without its original condition: " + token)
    if COUNTDOWN.index("WaitingForNextWave = false;") > COUNTDOWN.index("SpawnNextEnemy();"):
        raise AssertionError("zero interval must clear waiting state before spawning")
    for token in (
        "wavesArenaRuntime.OnEnemyDiedWithDamageInfo(deadHealth, damageInfo);",
        "wavesArenaRuntime.HandleBossDeath(bossMain, damageInfo);",
        "wavesArenaRuntime.ProceedAfterWaveFinished();",
        "wavesArenaRuntime.OnBossSpawnFailed(preset);",
    ):
        if token not in ARENA:
            raise AssertionError("wave death entry must forward to arena module: " + token)
    for token in (
        "internal void OnEnemyDiedWithDamageInfo(Health deadHealth, DamageInfo damageInfo)",
        "internal void HandleBossDeath(CharacterMainControl bossMain, DamageInfo damageInfo)",
        "if (!IsCurrentWaveBossMember(bossMain))",
        "owner.CheckBossKillAchievementsOnceForArena(bossMain);",
        "internal void ProceedAfterWaveFinished()",
        "OnInfiniteHellWaveCompleted_LootAndRewards();",
        "internal void OnBossSpawnFailed(EnemyPresetInfo preset)",
    ):
        if token not in WAVE_DEATHS:
            raise AssertionError("wave death or transition path did not move to arena module: " + token)
    for name in (
        "bossSpawnTimes", "bossOriginalLootCounts", "bossRushLootboxPathBosses",
        "trackedBossLootHooks", "bossRushLootboxPathTrackedBossScratch",
        "bossRushLootboxPathStaleBossScratch", "legacyBossGuaranteeCandidateScratch",
        "legacyBossGuaranteeQualityBucketsScratch", "difficultyRewardPreferredScratch",
        "difficultyRewardFallbackHighQualityScratch", "difficultyRewardKeepScratch",
        "lootNextWarningLogTimes", "_activeVictoryRewardShadowCrateController",
        "_difficultyRewardSpawnPositionOverrideActive", "_difficultyRewardSpawnPositionOverride",
    ):
        if not re.search(r"\b" + re.escape(name) + r"\s*=", LOOT_STATE):
            raise AssertionError("arena loot tracking or reward scratch must belong to module: " + name)
        if "wavesArenaRuntime." + name not in LOOT_BRIDGE:
            raise AssertionError("legacy loot access must forward to module: " + name)
    for cache_name in (
        "CachedLootBoxTemplateWithLoader",
        "CachedDifficultyRewardLootBoxTemplate",
        "CachedVictoryRewardVisualLootBoxTemplate",
        "ItemValueCache",
        "LegacyBossLootCandidateIds",
        "LegacyBossLootCandidateIdsByQuality",
    ):
        if cache_name + " { get; set; }" not in MODULE:
            raise AssertionError(cache_name + " must belong to the arena module")
        if "WavesArenaRuntimeModule." + cache_name not in LOOT_BRIDGE:
            raise AssertionError(cache_name + " host bridge missing")
    if "return wavesArenaRuntime.TickWavesArenaRuntime(deltaTime);" not in BRIDGE or "internal bool TickWavesArenaRuntime(float deltaTime)" not in TICK:
        raise AssertionError("arena timer tick must execute in arena module")
    for old_name, new_name in (
        ("infiniteHellMode", "InfiniteHellMode"),
        ("infiniteHellWaveIndex", "InfiniteHellWaveIndex"),
        ("infiniteHellCashPool", "InfiniteHellCashPool"),
        ("infiniteHellMilestoneRewardTier", "InfiniteHellMilestoneRewardTier"),
        ("infiniteHellWaveCashThisWave", "InfiniteHellWaveCashThisWave"),
        ("infiniteHellHighQualityItemPoolInitialized", "InfiniteHellHighQualityItemPoolInitialized"),
    ):
        if new_name + " { get; set; }" not in MODULE:
            raise AssertionError(new_name + " must be stored in the arena module")
        if "get { return wavesArenaRuntime." + new_name + "; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " loot bridge getter is missing")
        if "set { wavesArenaRuntime." + new_name + " = value; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " loot bridge setter is missing")
        if re.search(r"^\s*private\s+[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*(?:=|;)", LOOT_BRIDGE, re.M):
            raise AssertionError(old_name + " must not remain a host field")
    for old_name, new_name in (
        ("infiniteHellHighQualityItemPool", "InfiniteHellHighQualityItemPool"),
        ("infiniteHellHighQualityCandidateIdScratch", "InfiniteHellHighQualityCandidateIdScratch"),
        ("infiniteHellHighQualityPreferredScratch", "InfiniteHellHighQualityPreferredScratch"),
        ("infiniteHellHighQualityFallbackScratch", "InfiniteHellHighQualityFallbackScratch"),
    ):
        if new_name + " =" not in MODULE:
            raise AssertionError(new_name + " collection must be owned by the arena module")
        if "get { return wavesArenaRuntime." + new_name + "; }" not in LOOT_BRIDGE:
            raise AssertionError(old_name + " collection bridge is missing")
        if re.search(r"^\s*private\s+readonly\s+[\w<>,.\[\] ]+\s+" + re.escape(old_name) + r"\s*=", LOOT_BRIDGE, re.M):
            raise AssertionError(old_name + " must not remain a host collection")
    print("WavesArenaStateOwnershipGuard: PASS")


if __name__ == "__main__":
    main()
