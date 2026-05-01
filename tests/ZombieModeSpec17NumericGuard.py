from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
POLLUTION = Path("ZombieMode/ZombieModePollution.cs")
PURIFICATION = Path("ZombieMode/ZombiePurificationPointController.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, snippet: str, label: str) -> int:
    if snippet not in text:
        return fail("ZombieModeSpec17NumericGuard: missing " + label + " -> " + snippet)
    return 0


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    pollution = POLLUTION.read_text(encoding="utf-8")
    purification = PURIFICATION.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")

    for snippet in [
        "SprinterDashStartupSeconds = 0.5f",
        "SprinterDashDistance = 12f",
        "ExploderTriggerDistance = 2.5f",
        "ExploderDetonationDelaySeconds = 1.0f",
        "PlagueCloudRadius = 4f",
        "PlagueCloudDurationSeconds = 3f",
        "PlagueCloudDamagePerSecond = 8f",
        "SummonerSpawnCount = 2",
        "HarasserProjectileSpeed = 12f",
        "HarasserProjectileDamage = 25f",
        "HarasserProjectileLifetimeSeconds = 2f",
    ]:
        result = require(models, snippet, "SPEC 17 special-zombie tuning constant")
        if result:
            return result

    for snippet in [
        "ZombieModeTuning.SprinterDashDistance",
        "ZombieModeTuning.ExploderDetonationDelaySeconds",
        "ZombieModeTuning.PlagueCloudRadius",
        "ZombieModeTuning.PlagueCloudDamagePerSecond",
        "ZombieModeTuning.SummonerSpawnCount",
        "ZombieModeTuning.HarasserProjectileDamage",
        "\"\\u00B7\"",
    ]:
        result = require(pollution, snippet, "SPEC 17 special-zombie/elite-name usage")
        if result:
            return result

    for snippet in [
        "ZombieModeTuning.StarMagnetRadius",
        "Vector3.MoveTowards",
        "Time.unscaledDeltaTime",
        "distanceToPlayer <= ZombieModeTuning.StarMagnetRadius",
    ]:
        result = require(purification, snippet, "SPEC 17 star magnet movement")
        if result:
            return result

    for snippet in [
        "ZombieModeNpcCatalog.RepairPackFoldableCoverNormal",
        "ZombieModeNpcCatalog.RepairPackReinforcedRoadblockNormal",
        "ZombieModeNpcCatalog.RepairPackBarbedWireNormal",
        "ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayNormal",
        "ZombieModeNpcCatalog.RepairPackFoldableCoverBoss",
        "ZombieModeNpcCatalog.RepairPackReinforcedRoadblockBoss",
        "ZombieModeNpcCatalog.RepairPackBarbedWireBoss",
        "ZombieModeNpcCatalog.RepairPackEmergencyRepairSprayBoss",
    ]:
        result = require(rewards, snippet, "SPEC 19 repair-pack quantity usage")
        if result:
            return result

    print("ZombieModeSpec17NumericGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
