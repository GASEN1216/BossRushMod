from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
EFFECTS = Path("ZombieMode/ZombieModeRewardEffects.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    models = MODELS.read_text(encoding="utf-8")
    effects = EFFECTS.read_text(encoding="utf-8")

    banned_tokens = [
        "ContractCursedReloadEnabled",
        "ContractBloodPriceStacks",
    ]
    for token in banned_tokens:
        if token in models or token in effects:
            return fail("ZombieModeRewardContractStateGuard: unused contract state still present -> " + token)

    required_tokens = [
        "ApplyZombieModeContractCursedReload",
        "ApplyZombieModeContractBloodPrice",
        "TryAddZombieModeOptionModifier(",
        "ZombieMode Contract CursedReload",
        "player.Health.AddHealth",
        "ContractRuntimeModifierRecords",
    ]
    for token in required_tokens:
        if token not in effects:
            return fail("ZombieModeRewardContractStateGuard: contract effect missing token -> " + token)

    print("ZombieModeRewardContractStateGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
