from pathlib import Path
import sys


MODELS = Path("ZombieMode/ZombieModeModels.cs")
TUNING = Path("ZombieMode/ZombieModeTuning.cs")
WAVE_CONTROLLER = Path("ZombieMode/ZombieModeWaveController.cs")


def fail(message: str) -> int:
    print("ZombieModePacingTuningGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = MODELS.read_text(encoding="utf-8") + "\n" + TUNING.read_text(encoding="utf-8")
    wave_text = WAVE_CONTROLLER.read_text(encoding="utf-8")

    for required in [
        "public const float PreparationCountdownSeconds = 45f;",
        "public const float BossPreparationCountdownSeconds = 75f;",
    ]:
        if required not in text:
            return fail("preparation pacing contract missing -> " + required)

    if "extractionOpportunity\n                ? ZombieModeTuning.BossPreparationCountdownSeconds\n                : ZombieModeTuning.PreparationCountdownSeconds" not in wave_text:
        return fail("BeginZombieModePreparation must select 75/45 seconds from extractionOpportunity")

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
