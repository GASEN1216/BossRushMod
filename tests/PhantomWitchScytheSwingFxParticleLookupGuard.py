"""
Guard: Scythe smoke burst should reuse its particle lookup within one spawn.

Reason:
- SpawnSmokeBurst builds a short-lived particle hierarchy and then needs to
  configure, tint, and restart those same particles.
- Repeating GetComponentsInChildren<ParticleSystem>(true) in that hot VFX path
  allocates redundant arrays without changing behavior.

Requirement:
- SpawnSmokeBurst may call GetComponentsInChildren<ParticleSystem>(true) once.
- Stardust lookup, tinting, and restart must reuse that same array.
"""

from pathlib import Path
import re
import sys


SOURCE = Path("Integration/PhantomWitch/PhantomWitchScytheSwingFx.cs")


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
    text = SOURCE.read_text(encoding="utf-8")
    block = extract_block(
        text,
        "internal static GameObject SpawnSmokeBurst(Transform parent, Vector3 localPosition, Quaternion localRotation, float rangeScale, float duration)",
    )
    if not block:
        return fail("PhantomWitchScytheSwingFxParticleLookupGuard: SpawnSmokeBurst block missing")

    particle_lookups = re.findall(r"GetComponentsInChildren<ParticleSystem>\s*\(\s*true\s*\)", block)
    if len(particle_lookups) != 1:
        return fail(
            "PhantomWitchScytheSwingFxParticleLookupGuard: "
            f"expected one particle hierarchy lookup in SpawnSmokeBurst, found {len(particle_lookups)}"
        )

    if "ParticleSystem[] particlesForTint" in block:
        return fail(
            "PhantomWitchScytheSwingFxParticleLookupGuard: tint/restart still uses a second particle array"
        )

    if not re.search(r"ParticleSystem\s+stardust\s*=\s*particles\.Length\s*>\s*1\s*\?\s*particles\s*\[\s*1\s*\]\s*:\s*null\s*;", block):
        return fail(
            "PhantomWitchScytheSwingFxParticleLookupGuard: stardust lookup does not reuse the particles array"
        )

    if not re.search(r"TintParticles\s*\(\s*particles\s*,", block):
        return fail(
            "PhantomWitchScytheSwingFxParticleLookupGuard: TintParticles does not reuse the particles array"
        )

    if not re.search(r"RestartParticles\s*\(\s*particles\s*\)", block):
        return fail(
            "PhantomWitchScytheSwingFxParticleLookupGuard: RestartParticles does not reuse the particles array"
        )

    print("PhantomWitchScytheSwingFxParticleLookupGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
