"""
Guard the Phantom Witch teleport delay and smoke consistency update.

Requirements:
- teleport pre-blink delay must be 0.3 second
- teleport VFX must reuse the scythe normal-attack smoke helper
- curse realm black smoke density must be stronger than the previous profile
"""

from pathlib import Path
import re
import sys


CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
REDESIGN = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
SCYTHE = Path("Integration/PhantomWitch/PhantomWitchScytheAction.cs")
SWING = Path("Integration/PhantomWitch/PhantomWitchScytheSwingFx.cs")
ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")


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
    config_text = CONFIG.read_text(encoding="utf-8")
    redesign_text = REDESIGN.read_text(encoding="utf-8")
    scythe_text = SCYTHE.read_text(encoding="utf-8")
    swing_text = SWING.read_text(encoding="utf-8")
    ability_text = ABILITY.read_text(encoding="utf-8")

    teleport_block = extract_block(redesign_text, "internal static GameObject CreateTeleportEffect")
    black_smoke_block = extract_block(scythe_text, "private static void CreateAreaBlackSmoke")
    tracked_block = extract_block(ability_text, "private IEnumerator ExecuteTrackedTeleportStrike(CharacterMainControl target)")
    smoke_burst_block = extract_block(swing_text, "internal static GameObject SpawnSmokeBurst")
    marker_block = extract_block(redesign_text, "internal static GameObject CreateTrackedTeleportMarkerEffect")
    flash_block = extract_block(redesign_text, "internal static GameObject CreateTrackedTeleportFlashEffect")

    missing: list[str] = []

    if re.search(r"public\s+const\s+float\s+BlinkHideDuration\s*=\s*0\.3f\s*;", config_text) is None:
        missing.append("BlinkHideDuration is not set to 0.3f")

    if re.search(r"public\s+const\s+float\s+BlinkTrackedTelegraphDuration\s*=\s*2f\s*;", config_text) is None:
        missing.append("BlinkTrackedTelegraphDuration is not set to 2f")

    if re.search(r"internal\s+static\s+GameObject\s+SpawnSmokeBurst\s*\(", swing_text) is None:
        missing.append("PhantomWitchScytheSwingFx is missing SpawnSmokeBurst helper")

    if re.search(r"(private|internal)\s+static\s+void\s+BuildSharedSmokeParticles\s*\(", swing_text) is None:
        missing.append("PhantomWitchScytheSwingFx is missing shared smoke profile builder")

    if re.search(r"BuildSharedSmokeParticles\s*\(", smoke_burst_block) is None:
        missing.append("teleport smoke burst still does not reuse the normal-attack shared smoke profile")

    if re.search(r"PhantomWitchScytheSwingFx\.SpawnSmokeBurst\s*\(", teleport_block) is None:
        missing.append("teleport VFX still does not reuse scythe smoke burst")

    if re.search(r"float\s+markerDuration\s*=\s*Mathf\.Max\s*\(\s*PhantomWitchConfig\.BlinkTrackedMarkerFxDuration\s*,\s*PhantomWitchConfig\.BlinkTrackedTelegraphDuration\s*\+\s*0\.[0-9]+f\s*\)\s*;", tracked_block) is None:
        missing.append("tracked teleport marker duration is not extended to cover the 2s telegraph")

    if re.search(r"Vector3\s+lockedTeleportPos\s*=\s*ResolveTrackedTeleportStrikePosition\s*\(target\)\s*;", tracked_block) is None:
        missing.append("tracked teleport does not explicitly lock position after flash")

    if re.search(r"CreateTrackedTeleportFlashEffect\s*\(\s*lockedTeleportPos\s*\)", tracked_block) is None:
        missing.append("tracked teleport flash is not bound to lockedTeleportPos")

    if re.search(r"yield\s+return\s+TeleportTo\s*\(\s*lockedTeleportPos\s*\)", tracked_block) is None:
        missing.append("tracked teleport still does not teleport to lockedTeleportPos")

    if re.search(r"PhantomWitchScytheSwingFx\.SpawnSmokeBurst\s*\(", marker_block) is None:
        missing.append("tracked teleport marker still does not use the scythe smoke burst")

    if re.search(r"CreatePointLight\s*\([^)]*,\s*new\s+Vector3\(0f,\s*0\.18f,\s*0f\),\s*PhantomWitchConfig\.VioletVoidCore,\s*(?:4\.[5-9]f|[5-9]\.[0-9]f|\d{2,}f)", flash_block) is None:
        missing.append("tracked teleport flash light is still too small")

    if re.search(r"CreateBillboardQuad\s*\([^)]*,\s*0\.(?:3[2-9]|[4-9][0-9])f,\s*0\.(?:3[2-9]|[4-9][0-9])f", flash_block) is None:
        missing.append("tracked teleport flash billboard is still too small")

    if re.search(r"ResolveAdaptiveCount\s*\(\s*detailLevel\s*,\s*(?:7[0-9]|[89][0-9])\s*,\s*(?:4[0-9]|[5-9][0-9])\s*,\s*0\s*\)", black_smoke_block) is None:
        missing.append("curse realm inner smoke density is still too low")

    if re.search(r"ResolveAdaptiveFloat\s*\(\s*detailLevel\s*,\s*(?:1[89]f|[2-9][0-9]f)\s*,\s*(?:1[0-9]f|[2-9][0-9]f)\s*,\s*0f\s*\)", black_smoke_block) is None:
        missing.append("curse realm smoke emission is still too low")

    if missing:
        return fail("PhantomWitchTeleportSmokeConsistencyGuard: " + " | ".join(missing))

    print("PhantomWitchTeleportSmokeConsistencyGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
