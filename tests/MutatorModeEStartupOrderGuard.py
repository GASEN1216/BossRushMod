from pathlib import Path
import sys


SOURCE = Path("ModeE/ModeEStartup.cs")


def fail(message: str) -> int:
    print("MutatorModeEStartupOrderGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8", errors="ignore")

    roll = 'TryRollMutatorsForMode("ModeE");'
    spawn = "ModeESpawnAllBosses("

    roll_index = text.find(roll)
    spawn_index = text.find(spawn)
    if roll_index < 0:
        return fail("missing Mode E mutator roll call")
    if spawn_index < 0:
        return fail("missing Mode E boss scheduling call")
    if roll_index > spawn_index:
        return fail("Mode E mutators must roll before boss scheduling starts")

    print("MutatorModeEStartupOrderGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
