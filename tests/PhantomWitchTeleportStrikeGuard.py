"""
Guard the close-range tracked Phantom Witch teleport strike behavior.

This is a source-level guard for the user-requested combat update:
- offensive blink uses a meaningful tracked offset point instead of collapsing at the player pivot
- the point is recomputed from the current player position during telegraph
- telegraph runs for 2.0s and ends with a flash cue before blink
- offensive blink packages land into an immediate sweep instead of the old delayed flow
- teleport telegraph visuals reuse Frostmourne aura dressing
"""

from pathlib import Path
import re
import sys


CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
ABILITY = Path("Integration/PhantomWitch/PhantomWitchAbilityController.cs")
ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
VFX = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    ability_text = ABILITY.read_text(encoding="utf-8")
    asset_text = ASSET.read_text(encoding="utf-8")
    vfx_text = VFX.read_text(encoding="utf-8")

    missing = [
        require(config_text, r"BlinkTrackedOffsetDistance\s*=\s*2\.2f", "config missing current 2.2m tracked blink offset"),
        require(config_text, r"BlinkTrackedTelegraphDuration\s*=\s*2f", "config missing current 2.0s tracked blink telegraph"),
        require(config_text, r"BlinkTrackedFlashLeadDuration\s*=\s*0\.1f", "config missing 0.1s blink flash lead"),
        require(ability_text, r"ResolveTrackedTeleportStrikePosition\s*\(", "ability missing tracked teleport position helper"),
        require(
            ability_text,
            r"while\s*\(\s*Time\.time\s*-\s*telegraphStartedAt\s*<\s*PhantomWitchConfig\.BlinkTrackedTelegraphDuration\s*\).*?target\.transform\.position",
            "telegraph loop does not recompute from current player position",
            re.S,
        ),
        require(ability_text, r"CreateTrackedTeleportMarkerEffect\s*\(", "ability never spawns tracked teleport marker"),
        require(ability_text, r"CreateTrackedTeleportFlashEffect\s*\(", "ability never spawns tracked teleport flash cue"),
        require(ability_text, r"ExecuteImmediateScytheSweep\s*\(", "ability missing immediate post-teleport sweep"),
        require(
            ability_text,
            r"ExecuteFlankPressurePackage\s*\([^)]*\)\s*\{.*?ExecuteTrackedTeleportStrike\s*\(",
            "FlankPressure still does not use tracked teleport strike",
            re.S,
        ),
        require(
            ability_text,
            r"ExecuteShortDriftPressurePackage\s*\([^)]*\)\s*\{.*?ExecuteTrackedTeleportStrike\s*\(",
            "ShortDriftPressure still does not use tracked teleport strike",
            re.S,
        ),
        require(
            ability_text,
            r"Vector3\s+lockedTeleportPos\s*=\s*ResolveTrackedTeleportStrikePosition\s*\(target\)\s*;",
            "tracked teleport does not lock the final blink position before commit",
            re.S,
        ),
        require(ability_text, r"yield\s+return\s+TeleportTo\s*\(\s*lockedTeleportPos\s*\)", "tracked teleport does not blink to lockedTeleportPos"),
        require(asset_text, r"CreateTrackedTeleportMarkerEffect\s*\(", "AssetManager missing tracked teleport marker entrypoint"),
        require(asset_text, r"CreateTrackedTeleportFlashEffect\s*\(", "AssetManager missing tracked teleport flash entrypoint"),
        require(vfx_text, r"FrostmourneWeaponConfig\.TryAddIceEffectsToGraphic\s*\(", "tracked teleport marker does not reuse Frostmourne aura dressing"),
    ]

    if re.search(r"private\s+IEnumerator\s+ExecuteBlink\s*\(", ability_text) is not None:
        missing.append("legacy ExecuteBlink wrapper still exists")

    missing = [item for item in missing if item is not None]
    if missing:
        return fail("PhantomWitchTeleportStrikeGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchTeleportStrikeGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
