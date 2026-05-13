"""
Guard the remaining Rev.2 Phantom Witch spec gaps found during static review.

This repository cannot reliably run the Windows Unity compile/runtime loop from
WSL, so this source guard locks the remaining boss-side contracts:
- teleport-driven stealth must be tracked through the stealth state machine
- FlankPressure must stay on the tracked-teleport strike path with a fallback sweep
- WraithTrailObserve must have explicit boss-side windup telegraph/fallback
- WraithTrailObserve follow-up must keep the windup-locked direction
- semi-stealth must create its own boss-side readability effect and downgrade cleanly
- P3 minions must spawn as stable left/right partners instead of radial ring slots
- P2 transition copy must match the redesigned phase identity
- player scythe realm visuals must keep honoring runtime detail-level policy
- Harass minion must have real pressure behavior
- telemetry must include periodic minion census and realm commit timing
- boss-side FX factories must keep honoring detail-level skip policy
- VFX adaptive counts must not bypass the low-end policy
"""

from pathlib import Path
import re
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
ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
ASSET_PARTS = [
    ASSET,
    Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs"),
]
VFX = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
VFX_PARTS = [
    VFX,
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_EmittersAndTextures.cs"),
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_RuntimeComponents.cs"),
]
CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
SCYTHE = Path("Integration/PhantomWitch/PhantomWitchScytheAction.cs")
SCYTHE_PARTS = [
    SCYTHE,
    Path("Integration/PhantomWitch/PhantomWitchScytheAction_RuntimeComponents.cs"),
]


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def read_vfx() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in VFX_PARTS)


def read_asset_manager() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in ASSET_PARTS)


def read_scythe() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in SCYTHE_PARTS)


def main() -> int:
    ability_text = read_phantom_witch_ability_sources()
    asset_text = read_asset_manager()
    vfx_text = read_vfx()
    config_text = CONFIG.read_text(encoding="utf-8")
    scythe_text = read_scythe()
    missing: list[str] = []

    missing.extend(
        item
        for item in [
            require(
                ability_text,
                r"private\s+IEnumerator\s+TeleportTo\s*\([^)]*\)\s*\{.*?SetStealthMode\s*\(\s*PhantomWitchStealthMode\.TrueStealthTransition\b.*?SetStealthMode\s*\(\s*preTeleportStealthMode\b",
                "teleport does not transition through TrueStealth and restore prior stealth mode",
                re.S,
            ),
            require(
                ability_text,
                r"private\s+IEnumerator\s+ExecuteFlankPressurePackage\s*\([^)]*\)\s*\{.*?if\s*\(\s*target\s*!=\s*null\s*\).*?ExecuteTrackedTeleportStrike\s*\(target\).*?else.*?ExecuteImmediateScytheSweep\s*\(null\)",
                "FlankPressure package no longer matches the tracked-teleport plus fallback-sweep contract",
                re.S,
            ),
            require(
                ability_text,
                r"CreateWraithWindupOutlineEffect\s*\(",
                "WraithTrailObserve still lacks explicit boss-side windup telegraph effect",
            ),
            require(
                ability_text,
                r"wraith_fallback_to_sweep\",\s*\"reason=missing_windup_vfx",
                "WraithTrailObserve still lacks missing_windup_vfx fallback telemetry",
            ),
            require(
                ability_text,
                r"Vector3\s+lockedForward\s*=\s*ResolveAttackForward\(target\).*?CreateHeavySlashEffect\(.*?lockedForward.*?CreateScytheSweepEffect\(.*?lockedForward",
                "WraithTrailObserve still does not keep a windup-locked forward vector for both hits",
                re.S,
            ),
            require(
                ability_text,
                r"WraithWindupMinGate",
                "WraithTrailObserve still does not honor WraithWindupMinGate",
            ),
            require(
                ability_text,
                r"TickHarassMinionPressure\s*\(",
                "Harass minion pressure loop missing",
            ),
            require(
                ability_text,
                r"HarassMinionPressure",
                "Harass minion config/behavior hooks missing",
            ),
            require(
                ability_text,
                r"nextMinionCensusTime",
                "periodic minion census timer missing",
            ),
            require(
                ability_text,
                r"realm_commit\",\s*\"origin=.*?commitMs=",
                "realm_commit telemetry missing commitMs payload",
                re.S,
            ),
            require(
                ability_text,
                r"activeSemiStealthEffect",
                "Semi-stealth readability effect tracking missing",
            ),
            require(
                ability_text,
                r"CreateSemiStealthWindupEffect\s*\(",
                "Semi-stealth readability effect is never created",
            ),
            require(
                ability_text,
                r"if\s*\(!alphaSupported\)\s*\{.*?currentStealthMode\s*=\s*PhantomWitchStealthMode\.Visible",
                "Semi-stealth still does not downgrade to Visible when alpha modulation is unavailable",
                re.S,
            ),
            require(
                ability_text,
                r"bossCharacter\.transform\.right",
                "Minion spawn still lacks stable left/right placement",
            ),
        ]
        if item is not None
    )

    missing.extend(
        item
        for item in [
            require(
                asset_text,
                r"public\s+static\s+GameObject\s+CreateChannelChargeEffect\s*\([^)]*\)\s*\{.*?ShouldSkipEffect\s*\(",
                "CreateChannelChargeEffect missing detail-level skip policy",
                re.S,
            ),
            require(
                asset_text,
                r"public\s+static\s+GameObject\s+CreateHeavySlashEffect\s*\([^)]*\)\s*\{.*?ShouldSkipEffect\s*\(",
                "CreateHeavySlashEffect missing detail-level skip policy",
                re.S,
            ),
            require(
                asset_text,
                r"public\s+static\s+GameObject\s+CreateSummonCircleEffect\s*\([^)]*\)\s*\{.*?ShouldSkipEffect\s*\(",
                "CreateSummonCircleEffect missing detail-level skip policy",
                re.S,
            ),
            require(
                asset_text,
                r"public\s+static\s+GameObject\s+CreateMinionSpawnEffect\s*\([^)]*\)\s*\{.*?ShouldSkipEffect\s*\(",
                "CreateMinionSpawnEffect missing detail-level skip policy",
                re.S,
            ),
            require(
                asset_text,
                r"CreateWraithWindupOutlineEffect\s*\(",
                "AssetManager missing Wraith windup outline entrypoint",
            ),
            require(
                asset_text,
                r"CreateSemiStealthWindupEffect\s*\(",
                "AssetManager missing semi-stealth readability entrypoint",
            ),
        ]
        if item is not None
    )

    missing.extend(
        item
        for item in [
            require(
                vfx_text,
                r"ResolveAdaptiveCount\s*\(\s*PhantomWitchFxRuntime\.CurrentDetailLevel\s*,",
                "VFX adaptive count still bypasses runtime detail level",
            ),
            require(
                vfx_text,
                r"CreateWraithWindupOutlineEffect\s*\(",
                "VFX redesign missing Wraith windup outline factory",
            ),
        ]
        if item is not None
    )

    if "幽灵女巫召唤了亡灵随从" in config_text:
        missing.append("Phase2 transition copy still claims the witch summons minions")

    if "Ignore detail level performance drops" in scythe_text:
        missing.append("Player scythe realm still bypasses detail-level policy via comment-coded intent")

    if re.search(r"CreateRisingWisps\s*\(\s*root\.transform\s*,\s*radius\s*,\s*duration\s*,\s*PhantomWitchFxDetailLevel\.Full", scythe_text):
        missing.append("Player scythe realm still forces Full detail wisps")

    if re.search(r"CreateOrbitSparks\s*\(\s*root\.transform\s*,\s*radius\s*,\s*duration\s*,\s*PhantomWitchFxDetailLevel\.Full", scythe_text):
        missing.append("Player scythe realm still forces Full detail orbit sparks")

    if missing:
        return fail("PhantomWitchSpecCompletionGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchSpecCompletionGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
