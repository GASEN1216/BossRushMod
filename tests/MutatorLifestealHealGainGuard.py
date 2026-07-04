from pathlib import Path
import sys


SOURCE = Path("Integration/Mutators/MutatorDefinitions.cs")


def fail(message: str) -> int:
    print("MutatorLifestealHealGainGuard: FAIL - " + message)
    return 1


def extract_block(text: str, start_marker: str, end_marker: str) -> str | None:
    start = text.find(start_marker)
    if start < 0:
        return None
    end = text.find(end_marker, start)
    if end < 0:
        return None
    return text[start:end]


def main() -> int:
    text = SOURCE.read_text(encoding="utf-8", errors="ignore")

    lifesteal_block = extract_block(
        text,
        'Id = "lifesteal_on_kill"',
        'Id = "blood_pact"'
    )
    if lifesteal_block is None:
        return fail("missing lifesteal_on_kill definition")

    if "player.AddHealth(heal);" not in lifesteal_block:
        return fail("lifesteal heal must route through CharacterMainControl.AddHealth")

    if "player.Health.AddHealth(heal);" in lifesteal_block:
        return fail("lifesteal still bypasses HealGain by calling Health.AddHealth directly")

    blood_pact_block = extract_block(
        text,
        'Id = "blood_pact"',
        'Id = "explode_on_death"'
    )
    if blood_pact_block is None:
        return fail("missing blood_pact definition")

    if 'playerItem.GetStat("HealGain")' not in blood_pact_block:
        return fail("blood_pact must apply a HealGain penalty")

    if "new Modifier(ModifierType.Add, -0.3f, MutatorSource)" not in blood_pact_block:
        return fail("blood_pact must reduce HealGain by 30%")

    if "player.Health.AddHealth(heal);" not in blood_pact_block:
        return fail("blood_pact heal must route through Health.AddHealth directly")

    if "player.AddHealth(heal);" in blood_pact_block:
        return fail("blood_pact must not route through CharacterMainControl.AddHealth")

    print("MutatorLifestealHealGainGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
