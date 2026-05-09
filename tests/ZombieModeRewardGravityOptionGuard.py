from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
REWARDS = Path("ZombieMode/ZombieModeRewards.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")
LOCALIZATION = Path("Localization/LocalizationInjector.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    rewards = REWARDS.read_text(encoding="utf-8")
    effects = EFFECTS.read_text(encoding="utf-8") if EFFECTS.exists() else ""
    localization = LOCALIZATION.read_text(encoding="utf-8")

    for reward_type in ["BattlefieldBlackHole", "BattlefieldGravityDrag"]:
        if reward_type not in models:
            return fail("ZombieModeRewardGravityOptionGuard: missing enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardGravityOptionGuard: reward not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardGravityOptionGuard: localization missing -> " + reward_type)

    for token in [
        "BattlefieldBlackHoleStacks",
        "BattlefieldGravityDragStacks",
        "StartZombieModeBattlefieldGravityRuntimeIfNeeded",
        "ZombieModeGravityWellRuntime",
        "BattlefieldGravityRuntimeStarted",
        "ZombieModeBattlefieldGravityCoroutine",
        "CollectZombieModeRuntimeEnemyMarkers",
        "zombieModeEnemyMarkerScratch",
    ]:
        if token not in effects:
            return fail("ZombieModeRewardGravityOptionGuard: missing token -> " + token)

    for banned in ["FindObjectsOfType<ZombieModeEnemyRuntimeMarker>", "NavMeshAgent", "Zone.Healths"]:
        if banned in effects:
            return fail("ZombieModeRewardGravityOptionGuard: banned implementation detail -> " + banned)

    print("ZombieModeRewardGravityOptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
