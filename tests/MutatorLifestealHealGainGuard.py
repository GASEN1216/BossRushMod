from pathlib import Path
import sys


SOURCE = Path("Integration/Mutators/MutatorDefinitions.cs")


def fail(message: str) -> int:
    print("MutatorLifestealHealGainGuard: FAIL - " + message)
    return 1


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8", errors="ignore")

    marker = 'Id = "lifesteal_on_kill"'
    start = text.find(marker)
    if start < 0:
        return fail("missing lifesteal_on_kill definition")

    end = text.find('Id = "explode_on_death"', start)
    if end < 0:
        return fail("missing explode_on_death definition after lifesteal block")

    block = text[start:end]

    if "player.AddHealth(heal);" not in block:
        return fail("lifesteal heal must route through CharacterMainControl.AddHealth")

    if "player.Health.AddHealth(heal);" in block:
        return fail("lifesteal still bypasses HealGain by calling Health.AddHealth directly")

    print("MutatorLifestealHealGainGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
