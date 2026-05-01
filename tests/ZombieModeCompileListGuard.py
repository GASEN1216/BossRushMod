from pathlib import Path
import sys


COMPILE = Path("compile_official.bat")

REQUIRED_FILES = [
    "ZombieMode\\ZombieModeModels.cs",
    "ZombieMode\\ZombieModeEntry.cs",
    "ZombieMode\\ZombieModeMapSelection.cs",
    "ZombieMode\\ZombieModeMapSelectionHelper.cs",
    "ZombieMode\\ZombieModeInventoryTransfer.cs",
    "ZombieMode\\ZombieModeMapIsolation.cs",
    "ZombieMode\\ZombieModeSpawner.cs",
    "ZombieMode\\ZombieModeWaveController.cs",
    "ZombieMode\\ZombieModeEnemyRuntime.cs",
    "ZombieMode\\ZombieModeRewards.cs",
    "ZombieMode\\ZombiePurificationPointController.cs",
    "ZombieMode\\ZombieModeSafeZoneController.cs",
    "ZombieMode\\ZombieModeExtractionController.cs",
    "ZombieMode\\ZombieModeHudController.cs",
    "ZombieMode\\ZombieModeCashInvestmentView.cs",
    "ZombieMode\\ZombieModeCleanup.cs",
    "ZombieMode\\ZombieModeDebug.cs",
    "Integration\\Items\\ZombieTideInvitationConfig.cs",
    "Integration\\Items\\ZombieTideInvitationUsage.cs",
    "Integration\\Items\\ZombieTideBeaconConfig.cs",
    "Integration\\Items\\ZombieTideBeaconUsage.cs",
]


def fail(message: str) -> int:
    print(message)
    return 1


def main() -> int:
    text = COMPILE.read_text(encoding="utf-8")
    missing = [path for path in REQUIRED_FILES if path not in text]
    if missing:
        return fail("ZombieModeCompileListGuard: missing compile entries: " + ", ".join(missing))

    model_index = text.find("ZombieMode\\ZombieModeModels.cs")
    entry_index = text.find("ZombieMode\\ZombieModeEntry.cs")
    cleanup_index = text.find("ZombieMode\\ZombieModeCleanup.cs")
    if not (0 <= model_index < entry_index < cleanup_index):
        return fail("ZombieModeCompileListGuard: ZombieMode compile order is unsafe")

    print("ZombieModeCompileListGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
