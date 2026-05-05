"""ZombieModeContinuousSpawnAndLootboxGuard: keep ZombieMode spawns map-wide and boss loot aggregated."""

from pathlib import Path
import re
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
WAVE = Path("ZombieMode/ZombieModeWaveController.cs")
DROPS = Path("ZombieMode/ZombieModeDropsAndPerformance.cs")
INTERACTABLES = Path("Interactables/BossRushInteractables.cs")


def fail(message: str) -> int:
    print("ZombieModeContinuousSpawnAndLootboxGuard: FAIL - " + message)
    return 1


def extract_method(text: str, method_name: str) -> str:
    match = re.search(r"\b" + re.escape(method_name) + r"\s*\([^)]*\)\s*\{", text)
    if match is None:
        return ""

    depth = 0
    for index in range(match.end() - 1, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[match.start():index + 1]
    return ""


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    wave = WAVE.read_text(encoding="utf-8")
    drops = DROPS.read_text(encoding="utf-8")
    interactables = INTERACTABLES.read_text(encoding="utf-8")

    for token in [
        "public const float PeriodicSpawnIntervalSeconds = 1f;",
        "public const int MaxInitialWaveSpawnCount = 0;",
        "public const int MaxNormalZombieCount = 50;",
        "public int LivingNormalZombieCount;",
        "public int PendingNormalZombieSpawns;",
        "public int NextSpawnPointIndex;",
    ]:
        if token not in models:
            return fail("missing tuning/state token -> " + token)

    start_wave = extract_method(wave, "StartZombieModeWave")
    if not start_wave:
        return fail("StartZombieModeWave not found")
    if "SpawnZombieModeWaveAsync(runId, initialSpawnCount" in start_wave:
        return fail("wave start must not burst-spawn the whole initial wave")
    if "zombieModeRunState.NextSpawnPointIndex = 0;" not in start_wave:
        return fail("wave start must reset the round-robin map spawn index")
    if "GetZombieModeInitialWaveSpawnCount(effectiveSpawnPointCount)" in start_wave:
        return fail("StartZombieModeWave must not compute burst initial spawn count")

    complete_wave = extract_method(wave, "CompleteZombieModeWave")
    if not complete_wave:
        return fail("CompleteZombieModeWave not found")

    tick_pressure = extract_method(wave, "TickZombieModeAmbientZombiePressure")
    if "SpawnZombieModeWaveAcrossMapAsync(runId, spawnCount, false).Forget();" not in tick_pressure:
        return fail("ambient pressure must spawn through the map-wide round-robin helper")
    if "ZombieModeTuning.PeriodicSpawnIntervalSeconds" not in tick_pressure:
        return fail("ambient pressure must use the 1s periodic interval")
    if "GetZombieModePeriodicSpawnCount()" not in tick_pressure:
        return fail("ambient pressure must use capped normal-zombie slot calculation")
    if "ZombieModeTuning.MaxPeriodicSpawnCount" in tick_pressure:
        return fail("ambient pressure must fill open 50-cap slots instead of limiting to one zombie per interval")

    ambient_phase = extract_method(wave, "IsZombieModeAmbientZombieSpawnPhase")
    for token in [
        "ZombieModeCombatPhase.InitialPreparation",
        "ZombieModeCombatPhase.Preparation",
        "ZombieModeCombatPhase.ExtractionOpportunity",
        "ZombieModeCombatPhase.Combat",
    ]:
        if token not in ambient_phase:
            return fail("ambient pressure must run in combat and preparation phases -> " + token)

    spawn_across_map = extract_method(wave, "SpawnZombieModeWaveAcrossMapAsync")
    if not spawn_across_map:
        return fail("map-wide spawn helper missing")
    for token in [
        "GetNextZombieModeMapSpawnPosition()",
        "TrySpawnZombieModeNormalZombieAsync(runId, spawnPosition)",
        "GetZombieModeNormalZombieSpawnSlots() <= 0",
        "await UniTask.Yield();",
    ]:
        if token not in spawn_across_map:
            return fail("map-wide spawn helper missing token -> " + token)

    next_position = extract_method(wave, "GetNextZombieModeMapSpawnPosition")
    if not next_position:
        return fail("map spawn position helper missing")
    for token in [
        "TryGetNearestZombieModeMapSpawnPositionToPlayer(out position)",
        "return position;",
        "GetZombieModeSpawnPosition()",
    ]:
        if token not in next_position:
            return fail("nearest-point helper missing token -> " + token)

    if "BossLootCrateBaseAtWave5" in models or "BossLootCrateGrowthEvery5Waves" in models:
        return fail("old multi-crate boss loot tuning must be removed")

    boss_drop = extract_method(drops, "TrySpawnZombieModeBossDrop")
    if "TrySpawnZombieModeBossLootbox(runId, marker.BossKind, position, maxQuality);" not in boss_drop:
        return fail("boss death must create one aggregate boss lootbox")
    if re.search(r"for\s*\([^)]*TrySpawnZombieModeBossLootbox", boss_drop, re.S):
        return fail("boss death must not loop-create multiple boss lootboxes")
    if "TryDropZombieModeItemNearPosition(runId, typeId" in boss_drop:
        return fail("boss death must not add loose extra item drops beside the aggregate box")

    boss_box = extract_method(drops, "TrySpawnZombieModeBossLootbox")
    for token in [
        "RefillZombieModeLootboxInventory(runId, lootbox, 6, 9, 3, maxQuality, true)",
        "BossRushLootboxUtility.DecorateLootbox(lootbox, this, false, true)",
        "TryAdjustZombieModeBossLootboxCapacity(lootbox)",
    ]:
        if token not in boss_box:
            return fail("boss lootbox must be aggregate and fully decorated -> " + token)

    decorate = extract_method(interactables, "DecorateLootbox")
    if "bool includeCarryInteraction = false" not in decorate:
        return fail("DecorateLootbox must expose includeCarryInteraction")
    for token in [
        "BossRushCarryInteractable carryInteract",
        "includeCarryInteraction",
        "hostList.Add(carryInteract)",
    ]:
        if token not in decorate:
            return fail("DecorateLootbox must add carry interaction when requested -> " + token)

    print("ZombieModeContinuousSpawnAndLootboxGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
