from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")


def fail(message: str) -> int:
    print("ZombieModePacingTuningGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = MODELS.read_text(encoding="utf-8")

    required = "public const float PeriodicSpawnIntervalSeconds = 1f;"
    if required not in text:
        return fail("periodic ambient zombie pressure must stay at the design interval -> " + required)

    forbidden = "public const float PeriodicSpawnIntervalSeconds = 30f;"
    if forbidden in text:
        return fail("periodic ambient zombie pressure is still configured as every 30 seconds")

    print("ZombieModePacingTuningGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
