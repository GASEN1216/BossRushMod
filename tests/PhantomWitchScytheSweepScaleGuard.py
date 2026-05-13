"""
Guard: Phantom Witch basic scythe sweep VFX must follow the enlarged boss model scale.

Reason:
- the boss body is scaled up to `PhantomWitchConfig.BossModelScale`
- the normal scythe sweep effect should visually scale with the larger model
- this request is visual-only; the damage radius/offset should stay unchanged
"""

from pathlib import Path
import sys


ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")
PHANTOM_WITCH_ABILITY_SOURCES = [
    ABILITY,
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_PackageScheduler.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_StealthAndAttacks.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_Minions.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_RuntimeTicks.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_PhaseAndLifecycle.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_MovementAndDamage.cs"),
    Path("Integration/PhantomWitch/PhantomWitchAbilityController_CleanupAndTelemetry.cs"),
]


def read_phantom_witch_ability_sources() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in PHANTOM_WITCH_ABILITY_SOURCES)


def fail(message: str) -> int:
    print(message)
    return 1


def extract_block(text: str, signature: str) -> str:
    start = text.find(signature)
    if start == -1:
        return ""

    brace_start = text.find("{", start)
    if brace_start == -1:
        return ""

    depth = 0
    for index in range(brace_start, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    ability_text = read_phantom_witch_ability_sources()

    helper_block = extract_block(ability_text, "private float ResolveBossSweepVisualScale()")
    if not helper_block:
        return fail("PhantomWitchScytheSweepScaleGuard: missing ResolveBossSweepVisualScale helper")

    if "bossCharacter.transform.lossyScale" not in helper_block:
        return fail("PhantomWitchScytheSweepScaleGuard: helper must read bossCharacter.transform.lossyScale")

    sweep_block = extract_block(ability_text, "private IEnumerator ExecuteImmediateScytheSweep(CharacterMainControl target)")
    if not sweep_block:
        return fail("PhantomWitchScytheSweepScaleGuard: missing ExecuteImmediateScytheSweep block")

    required_needles = [
        "float sweepVisualScale = ResolveBossSweepVisualScale();",
        "sweepForward * (PhantomWitchConfig.ScytheSweepForwardOffset * sweepVisualScale);",
        "PhantomWitchConfig.ScytheSweepRadius * sweepVisualScale,",
    ]

    for needle in required_needles:
        if needle not in sweep_block:
            return fail("PhantomWitchScytheSweepScaleGuard: missing scaled sweep VFX usage -> " + needle)

    untouched_damage_needles = [
        "PhantomWitchConfig.ScytheSweepRadius,",
        "PhantomWitchConfig.ScytheSweepForwardOffset,",
    ]

    for needle in untouched_damage_needles:
        if needle not in sweep_block:
            return fail("PhantomWitchScytheSweepScaleGuard: damage scope drifted, expected unscaled value -> " + needle)

    print("PhantomWitchScytheSweepScaleGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
