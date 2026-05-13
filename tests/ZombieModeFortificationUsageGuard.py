from pathlib import Path
import sys


MODEF_FORT_PARTS = [
    Path("ModeF/ModeFFortifications.cs"),
    Path("ModeF/ModeFFortifications_RuntimePlacement.cs"),
    Path("ModeF/ModeFFortifications_RepairRewardsCleanup.cs"),
    Path("ModeF/ModeFItemUsageAndTriggers.cs"),
]
ZOMBIE_ENTRY = Path("ZombieMode/ZombieModeEntry.cs")


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

    for token in [
        "private bool CanUseModeFortificationUtilities()",
        "return modeFActive || IsZombieModeActive;",
        "if (!CanUseModeFortificationUtilities())",
        "This item can only be used in Mode F or Zombie Mode",
        "inst.IsModeFActive || inst.IsZombieModeActive",
        "modeFState.ActiveFortifications.Remove(marker.FortificationId)",
    ]:
        if token not in fort:
            return fail("missing fortification zombie-mode support token -> " + token)

    fort_without_whitespace = "".join(fort.split())
    if "RegisterZombieModeRunOnlyObject(zombieModeRunState.RunId,ZombieModeRunOnlyObjectKind.Fortification" not in fort_without_whitespace:
        return fail("missing fortification zombie-mode run-only registration")

    highlight_method = extract_method(fort, "private void UpdateModeFFortificationHighlights")
    if "if (!CanUseModeFortificationUtilities())" not in highlight_method:
        return fail("fortification highlights must run in Zombie Mode")

    tick_body_tokens = [
        "UpdateModeFFortificationHighlights();",
        "UpdateFortPlacementMode();",
        "UpdateModeFRepairSelection();",
    ]
    for token in tick_body_tokens:
        if token not in zombie:
            return fail("ZombieMode tick must update fortification runtime -> " + token)

    print("ZombieModeFortificationUsageGuard: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
