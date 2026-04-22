"""
Guard: Phantom Witch particle Velocity over Lifetime axes must use the same MinMaxCurve mode.

Reason:
- latest Player.log contains repeated `Particle Velocity curves must all be in the same mode`
- the Phantom Witch right-click realm VFX currently sets X/Z velocity curves but leaves Y at the
  module default in a few emitters, which makes Unity complain and spam the log

Requirements:
- `CreateSoulMistEmitter` in `PhantomWitchVfxRedesign.cs` must assign velocity.x / velocity.y / velocity.z
- `CreateVeilParticles` in `PhantomWitchAmbientPresence.cs` must assign velocityOverLifetime.x / y / z
"""

from pathlib import Path
import sys


REDESIGN = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
AMBIENT = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")


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


def require_axis_assignments(block: str, prefix: str, guard_name: str) -> int:
    missing = []
    for axis in ("x", "y", "z"):
        needle = prefix + axis + " = new ParticleSystem.MinMaxCurve("
        if needle not in block:
            missing.append(axis)

    if missing:
        return fail(guard_name + ": missing velocity axis assignments for " + ", ".join(missing))

    return 0


def main() -> int:
    redesign_text = REDESIGN.read_text(encoding="utf-8")
    ambient_text = AMBIENT.read_text(encoding="utf-8")

    soul_mist_block = extract_block(
        redesign_text,
        "private static ParticleSystem CreateSoulMistEmitter",
    )
    if not soul_mist_block:
        return fail("PhantomWitchParticleVelocityModeGuard: missing CreateSoulMistEmitter block")

    result = require_axis_assignments(
        soul_mist_block,
        "velocity.",
        "PhantomWitchParticleVelocityModeGuard(CreateSoulMistEmitter)",
    )
    if result != 0:
        return result

    veil_block = extract_block(ambient_text, "private void CreateVeilParticles()")
    if not veil_block:
        return fail("PhantomWitchParticleVelocityModeGuard: missing CreateVeilParticles block")

    result = require_axis_assignments(
        veil_block,
        "velocityOverLifetime.",
        "PhantomWitchParticleVelocityModeGuard(CreateVeilParticles)",
    )
    if result != 0:
        return result

    print("PhantomWitchParticleVelocityModeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
