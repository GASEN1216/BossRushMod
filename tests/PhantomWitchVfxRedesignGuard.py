"""
Guard the combat-facing Phantom Witch VFX redesign structure.

This is a source-level guard for the spec migration in environments where the
full Windows Unity compile/deploy flow is unavailable.
"""

from pathlib import Path
import re
import sys


ASSET = Path("Integration/PhantomWitch/PhantomWitchAssetManager.cs")
ASSET_PARTS = [
    ASSET,
    Path("Integration/PhantomWitch/PhantomWitchAssetManager_RuntimeComponents.cs"),
]
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
CURSE = Path("Integration/PhantomWitch/PhantomWitchCurseSweatVfx.cs")
REDESIGN = Path("Integration/PhantomWitch/PhantomWitchVfxRedesign.cs")
REDESIGN_PARTS = [
    REDESIGN,
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_EmittersAndTextures.cs"),
    Path("Integration/PhantomWitch/PhantomWitchVfxRedesign_RuntimeComponents.cs"),
]
COMPILE = Path("compile_official.bat")


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


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def read_redesign() -> str:
    return "\n".join(path.read_text(encoding="utf-8") for path in REDESIGN_PARTS)


def main() -> int:
    asset_text = "\n".join(path.read_text(encoding="utf-8") for path in ASSET_PARTS)
    ability_text = read_phantom_witch_ability_sources()
    curse_text = CURSE.read_text(encoding="utf-8")
    redesign_text = read_redesign()
    compile_text = COMPILE.read_text(encoding="utf-8")

    summon_block = extract_block(redesign_text, "internal static GameObject CreateSummonCircleEffect")
    death_block = extract_block(redesign_text, "internal static GameObject CreateDeathEffect")
    teleport_block = extract_block(redesign_text, "internal static GameObject CreateTeleportEffect")
    hit_block = extract_block(redesign_text, "internal static GameObject CreateDamageHitEffect")
    transition_block = extract_block(redesign_text, "internal static GameObject CreatePhaseTransitionEffect")
    realm_block = extract_block(redesign_text, "internal static GameObject CreateCurseRealmVisual")

    missing = [
        require(asset_text, r"public\s+static\s+GameObject\s+CreateChannelChargeEffect\s*\(", "CreateChannelChargeEffect"),
        require(ability_text, r"CreateChannelChargeEffect\s*\(", "ability windup VFX calls"),
        require(compile_text, r"Integration\\PhantomWitch\\PhantomWitchVfxRedesign\.cs", "compile list redesign source entry"),
        require(curse_text, r"GhostBreathVeil", "curse aura uses GhostBreath palette"),
        require(curse_text, r"BloodRoseVeil|BloodRoseMid|BloodRoseCore", "curse aura layered intensity feedback"),
        require(redesign_text, r"CreateBrokenRing|CreateOpenRing|CreatePartialRing", "curse realm broken edge ring"),
        require(redesign_text, r"CreateRealmRuneFlashSpawner|CreateRuneFlashSpawner|CreateRandomRune", "curse realm sparse rune flash"),
        require(teleport_block, r"SilverCross|CrossFlash", "teleport silver cross flash"),
        require(hit_block, r"BloodRoseCore", "damage hit blood accent"),
        require(transition_block, r"InvertedCross|invert", "phase transition inverted cross"),
    ]

    if "CreatePentagramLR" in summon_block or "CreateHexagramLR" in summon_block:
        missing.append("summon circle still uses pentagram/hexagram geometry")

    if "CreatePentagramLR" in death_block or "CreateHexagramLR" in death_block:
        missing.append("death effect still uses pentagram/hexagram geometry")

    if not realm_block:
        missing.append("boss curse realm visual block is missing")
    elif "CreatePentagram(" in realm_block or "CreateHexagram(" in realm_block or "InscribedRing" in realm_block:
        missing.append("boss curse realm visual still uses legacy pentagram/hexagram/inscribed ring stack")

    missing = [item for item in missing if item is not None]
    if missing:
        return fail("PhantomWitchVfxRedesignGuard: missing required redesign structure | " + " | ".join(missing))

    print("PhantomWitchVfxRedesignGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
