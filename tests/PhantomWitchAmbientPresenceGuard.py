"""
Guard the first implementation slice of the Phantom Witch VFX redesign.

This environment cannot run the Windows Unity compile pipeline, so the guard
checks for the additive integration contract in source form:
- ambient presence controller file and public API
- config palette/tuning constants
- ability controller wiring
- batch compile list entry
"""

from pathlib import Path
import re
import sys


AMBIENT = Path("Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")
CONFIG = Path("Integration/PhantomWitch/PhantomWitchConfig.cs")
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
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def main() -> int:
    if not AMBIENT.exists():
        return fail("PhantomWitchAmbientPresence guard: missing Integration/PhantomWitch/PhantomWitchAmbientPresence.cs")

    ambient_text = AMBIENT.read_text(encoding="utf-8")
    config_text = CONFIG.read_text(encoding="utf-8")
    ability_text = read_phantom_witch_ability_sources()
    compile_text = COMPILE.read_text(encoding="utf-8")

    ambient_requirements = [
        require(ambient_text, r"public\s+sealed\s+class\s+PhantomWitchAmbientPresence\s*:\s*MonoBehaviour", "ambient class declaration"),
        require(ambient_text, r"public\s+void\s+Initialize\s*\(\s*Transform\s+bossBody\s*\)", "Initialize(Transform bossBody)"),
        require(ambient_text, r"internal\s+void\s+SetDetailLevel\s*\(\s*PhantomWitchFxDetailLevel\s+level\s*\)", "internal SetDetailLevel(detailLevel)"),
        require(ambient_text, r"public\s+void\s+SetPhase\s*\(\s*PhantomWitchPhase\s+phase\s*\)", "SetPhase(phase)"),
        require(ambient_text, r"public\s+void\s+Pause\s*\(\s*\)", "Pause()"),
        require(ambient_text, r"public\s+void\s+Resume\s*\(\s*\)", "Resume()"),
    ]

    config_requirements = [
        require(config_text, r"VioletVoidCore", "VioletVoidCore palette constant"),
        require(config_text, r"SilverAshCore", "SilverAshCore palette constant"),
        require(config_text, r"BloodRoseCore", "BloodRoseCore palette constant"),
        require(config_text, r"GhostBreathVeil", "GhostBreathVeil palette constant"),
        require(config_text, r"AmbientHaloBreathPeriod", "AmbientHaloBreathPeriod constant"),
        require(config_text, r"AmbientHeartbeatLowHealthMinInterval", "AmbientHeartbeatLowHealthMinInterval constant"),
        require(config_text, r"AmbientRuneFlashMaxInterval", "AmbientRuneFlashMaxInterval constant"),
    ]

    ability_requirements = [
        require(ability_text, r"PhantomWitchAmbientPresence\s+ambientPresence", "ability controller ambient field"),
        require(ability_text, r"ambientPresence\s*=\s*bossCharacter\.GetComponent<PhantomWitchAmbientPresence>\s*\(\s*\)", "ability controller ambient GetComponent"),
        require(ability_text, r"ambientPresence\.Initialize\s*\(\s*bossCharacter\.transform\s*\)", "ability controller ambient Initialize"),
        require(ability_text, r"ambientPresence\.SetDetailLevel\s*\(\s*PhantomWitchFxRuntime\.CurrentDetailLevel\s*\)", "ability controller ambient detail sync"),
        require(ability_text, r"ambientPresence\.Pause\s*\(\s*\)", "ability controller ambient Pause call"),
        require(ability_text, r"ambientPresence\.Resume\s*\(\s*\)", "ability controller ambient Resume call"),
        require(ability_text, r"ambientPresence\.SetPhase\s*\(\s*PhantomWitchPhase\.Phase2\s*\)", "ability controller ambient phase 2 call"),
    ]

    compile_requirements = [
        require(compile_text, r"Integration\\PhantomWitch\\PhantomWitchAmbientPresence\.cs", "compile list ambient source entry"),
    ]

    missing = [
        requirement
        for requirement in ambient_requirements + config_requirements + ability_requirements + compile_requirements
        if requirement is not None
    ]

    if missing:
        return fail("PhantomWitchAmbientPresence guard: missing required integration | " + " | ".join(missing))

    print("PhantomWitchAmbientPresenceGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
