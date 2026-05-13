"""
Guard the combat-facing Phantom Witch AI redesign contract.

This environment cannot reliably run the Windows Unity compile/deploy flow, so
the guard checks the approved source-level integration points instead:
- Phase3-aware config/state machine contracts
- package-based scheduler entrypoints
- boss-only curse realm runtime split
- single active boss realm ownership
- compile list entry for the new runtime
"""

from pathlib import Path
import re
import sys


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
RUNTIME = Path("Integration/PhantomWitch/PhantomWitchBossCurseRealmRuntime.cs")
COMPILE = Path("compile_official.bat")


def fail(message: str) -> int:
    print(message)
    return 1


def require(text: str, pattern: str, description: str, flags: int = 0) -> str | None:
    if re.search(pattern, text, flags) is None:
        return description
    return None


def main() -> int:
    config_text = CONFIG.read_text(encoding="utf-8")
    ability_text = read_phantom_witch_ability_sources()
    compile_text = COMPILE.read_text(encoding="utf-8")
    missing: list[str] = []

    missing.extend(
        item
        for item in [
            require(config_text, r"enum\s+PhantomWitchPhase\s*\{[^}]*Phase3", "config missing Phase3 enum member", re.S),
            require(config_text, r"enum\s+PhantomWitchAttackPackageType\s*\{", "config missing package enum"),
            require(config_text, r"enum\s+PhantomWitchMinionRole\s*\{", "config missing minion role enum"),
            require(config_text, r"enum\s+PhantomWitchStealthMode\s*\{", "config missing stealth mode enum"),
            require(config_text, r"Phase3HealthThreshold", "config missing Phase3HealthThreshold"),
            require(config_text, r"PairFillSpawnsPerPackage", "config missing PairFillSpawnsPerPackage"),
            require(config_text, r"Phase3MessageCN", "config missing Phase3MessageCN"),
        ]
        if item is not None
    )

    missing.extend(
        item
        for item in [
            require(ability_text, r"currentPackageIndex", "ability missing currentPackageIndex"),
            require(ability_text, r"pendingTransitionTargetPhase", "ability missing pendingTransitionTargetPhase"),
            require(ability_text, r"activeBossCurseRealm", "ability missing activeBossCurseRealm"),
            require(ability_text, r"SetStealthMode\s*\(\s*PhantomWitchStealthMode\s+\w+\s*\)", "ability missing SetStealthMode entrypoint"),
            require(ability_text, r"ExecuteAttackPackage\s*\(", "ability missing ExecuteAttackPackage"),
            require(ability_text, r"ExecuteCurseTrapPackage\s*\(", "ability missing ExecuteCurseTrapPackage"),
            require(ability_text, r"ClearActiveBossCurseRealm\s*\(\s*string\s+\w+\s*\)", "ability missing ClearActiveBossCurseRealm"),
        ]
        if item is not None
    )

    if not RUNTIME.exists():
        missing.append("missing Integration/PhantomWitch/PhantomWitchBossCurseRealmRuntime.cs")
    else:
        runtime_text = RUNTIME.read_text(encoding="utf-8")
        missing.extend(
            item
            for item in [
                require(runtime_text, r"class\s+PhantomWitchBossCurseRealmRuntime", "boss runtime class missing"),
                require(runtime_text, r"Initialize\s*\(", "boss runtime missing Initialize"),
            ]
            if item is not None
        )

    compile_item = require(
        compile_text,
        r"Integration\\PhantomWitch\\PhantomWitchBossCurseRealmRuntime\.cs",
        "compile list missing boss curse realm runtime"
    )
    if compile_item is not None:
        missing.append(compile_item)

    if re.search(r"AddComponent<\s*PhantomWitchCurseRealmRuntime\s*>", ability_text) is not None:
        missing.append("ability still attaches player curse realm runtime")

    if missing:
        return fail("PhantomWitchAiRedesignGuard: FAIL | " + " | ".join(missing))

    print("PhantomWitchAiRedesignGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
