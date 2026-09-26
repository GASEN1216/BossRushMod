from pathlib import Path
import sys


MODEF_FORT_PARTS = [
    Path("ModeF/ModeFFortifications.cs"),
    Path("ModeF/ModeFFortifications_RuntimePlacement.cs"),
    Path("ModeF/ModeFFortifications_RepairRewardsCleanup.cs"),
    Path("ModeF/ModeFItemUsageAndTriggers.cs"),
]
ZOMBIE_ENTRY = Path("ZombieMode/ZombieModeEntryHostBridge.cs")
ZOMBIE_MODULE = Path("ZombieMode/ZombieModeRuntimeModule.cs")
ZOMBIE_BRIDGE = Path("ZombieMode/ZombieModeEntryHostBridge.cs")


def fail(message: str) -> int:
    print("ZombieModeFortificationUsageGuard: FAIL - " + message)
    return 1


def read_modef_fortifications() -> str:
    return "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in MODEF_FORT_PARTS)


def extract_method(text: str, marker: str) -> str:
    start = text.find(marker)
    if start < 0:
        return ""

    brace = text.find("{", start)
    if brace < 0:
        return ""

    depth = 0
    for index in range(brace, len(text)):
        char = text[index]
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return text[start:index + 1]

    return ""


def main() -> int:
    fort = read_modef_fortifications()
    zombie = ZOMBIE_ENTRY.read_text(encoding="utf-8")
    zombie_module = ZOMBIE_MODULE.read_text(encoding="utf-8")
    zombie_bridge = ZOMBIE_BRIDGE.read_text(encoding="utf-8")
    registration = Path("ModBehaviourRuntimeModules.cs").read_text(encoding="utf-8")
    modef_module = Path("ModeF/ModeFRuntimeModule.cs").read_text(encoding="utf-8")
    if "() => IsZombieModeActive, () => zombieModeRunState.RunId, RegisterZombieModeRunOnlyObject" not in registration:
        return fail("Mode F must bind the original zombie active/run queries and run-only registration")
    for assignment in (
        "this.isZombieModeActive = isZombieModeActive;",
        "this.getZombieRunId = getZombieRunId;",
        "this.registerZombieRunOnly = registerZombieRunOnly;",
    ):
        if assignment not in modef_module:
            return fail("Mode F zombie fortification boundary is not bound -> " + assignment)

    for token in [
        "private bool CanUseModeFortificationUtilities()",
        "return modeFActive || isZombieModeActive();",
        "if (!CanUseModeFortificationUtilities())",
        "This item can only be used in Mode F or Zombie Mode",
        "inst.IsModeFActive || inst.IsZombieModeActive",
        "modeFState.ActiveFortifications.Remove(marker.FortificationId)",
    ]:
        if token not in fort:
            return fail("missing fortification zombie-mode support token -> " + token)

    fort_without_whitespace = "".join(fort.split())
    if "registerZombieRunOnly(getZombieRunId(),ZombieModeRunOnlyObjectKind.Fortification" not in fort_without_whitespace:
        return fail("missing fortification zombie-mode run-only registration")

    highlight_method = extract_method(fort, "internal void UpdateModeFFortificationHighlights")
    if "if (!CanUseModeFortificationUtilities())" not in highlight_method:
        return fail("fortification highlights must run in Zombie Mode")

    tick_body_tokens = [
        "owner.UpdateModeFFortificationHighlightsForRuntimeModule();",
        "owner.UpdateFortPlacementMode();",
        "owner.UpdateModeFRepairSelection();",
    ]
    for token in tick_body_tokens:
        if token not in zombie_module:
            return fail("ZombieMode tick must update fortification runtime -> " + token)
    if "if (module != null) module.TickZombieMode(deltaTime);" not in zombie:
        return fail("ZombieMode host tick must forward to RuntimeModule")
    if "UpdateModeFFortificationHighlights();" not in zombie_bridge:
        return fail("ZombieMode host bridge must preserve the fortification-highlight update")

    print("ZombieModeFortificationUsageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
