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

    for reward_type in ["BattlefieldPurgeAura", "BattlefieldCurseTrap"]:
        if reward_type not in models:
            return fail("ZombieModeRewardAreaOptionGuard: missing enum -> " + reward_type)
        if "ZombieModeRewardType." + reward_type not in rewards:
            return fail("ZombieModeRewardAreaOptionGuard: reward not wired -> " + reward_type)
        if "BossRush_ZombieMode_Reward_" + reward_type not in localization:
            return fail("ZombieModeRewardAreaOptionGuard: localization missing -> " + reward_type)

    for token in [
        "BattlefieldPurgeAuraStacks",
        "BattlefieldCurseTrapStacks",
        "StartZombieModeBattlefieldAreaRuntimeIfNeeded",
        "ZombieModeBattlefieldAreaCoroutine",
        "StartZombieModeTelegraphedAreaDamage",
        "DealZombieModeExplosionAreaDamage",
    ]:
        if token not in effects:
            return fail("ZombieModeRewardAreaOptionGuard: missing token -> " + token)

    for banned in ["OnTriggerEnter", "OnTriggerExit", "Zone.Healths"]:
        if banned in effects:
            return fail("ZombieModeRewardAreaOptionGuard: this phase must not depend on zone trigger internals -> " + banned)

    print("ZombieModeRewardAreaOptionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
